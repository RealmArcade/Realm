using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     Produces the wire description of the world an authority owns. It reads the ECS and nothing else, so the process
///     that simulates the match describes it without a scene tree, and it remembers what each peer has already been
///     told so a snapshot carries what changed for that peer rather than the whole world every tick.
/// </summary>
public class AuthoritativeSnapshotBuilder
{
	private const float MaximumDescribedCoordinate = 100000f;
	private const float VisionCellSize = 16f;
	private const float DefaultVisionRadius = 15f;
	private const int VisionCellReach = 1;

	private static readonly QueryDescription DescribedUnitsQuery = new QueryDescription().WithAll<UnitOwnerPlayer, Health, Position>();

	private static readonly IReplicatedComponentCodec[] WorldCodecs = SelectCodecs(ReplicatedScope.World);
	private static readonly IReplicatedComponentCodec[] PlayerCodecs = SelectCodecs(ReplicatedScope.Player);
	private static readonly IReplicatedComponentCodec[] UnitCodecs = SelectCodecs(ReplicatedScope.Unit);
	private static readonly ComponentMask[] UnitCodecMasks = SelectMasks(UnitCodecs);

	private readonly EcsWorld _world;
	private readonly SimulationContext _context;
	private readonly Entity _worldEntity;
	private readonly MapRulesCatalog _rules;
	private readonly SnapshotPeerStateStore _peerStates;
	private readonly object _abilityLock;
	private readonly int[] _peerIdByPlayerSlot = new int[EcsWorld.MAX_PLAYERS];
	private readonly EntityCallback<UnitOwnerPlayer, Position> _collectDescribedUnits;
	private readonly EntityCallback<UnitOwnerPlayer, Position> _collectVisionSources;
	private readonly List<(Entity Entity, Vector3 Position)> _describedUnits = new();
	private readonly HashSet<int> _describedEntityIds = new();
	private readonly List<int> _forgottenEntityIds = new();
	private readonly Dictionary<(float X, float Z), List<int>> _visionCellSources = new();
	private readonly List<(Vector3 Position, float Radius)> _visionSources = new();
	private readonly Dictionary<int, string> _textByStringId = new();
	private byte[] _writeScratch = Array.Empty<byte>();
	private Entity[] _playerEntities = Array.Empty<Entity>();
	private Entity _viewer = Entity.Null;

	public AuthoritativeSnapshotBuilder(EcsWorld world, SimulationContext context, Entity worldEntity, MapRulesCatalog rules, SnapshotPeerStateStore peerStates, object abilityLock)
	{
		_world = world;
		_context = context;
		_worldEntity = worldEntity;
		_rules = rules;
		_peerStates = peerStates;
		_abilityLock = abilityLock;
		_collectDescribedUnits = CollectDescribedUnit;
		_collectVisionSources = CollectVisionSource;
	}

	/// <summary>
	///     The codecs that describe one scope, taken once from the table the wire contract is built from. A description
	///     walks three short lists instead of asking every codec which scope it belongs to on every entity.
	/// </summary>
	private static IReplicatedComponentCodec[] SelectCodecs(ReplicatedScope scope)
	{
		IReadOnlyList<IReplicatedComponentCodec> all = ReplicatedComponents.All;
		var selected = new List<IReplicatedComponentCodec>(all.Count);
		for (int index = 0; index < all.Count; index++)
		{
			if (all[index].Scope == scope) selected.Add(all[index]);
		}

		return selected.ToArray();
	}

	/// <summary>
	///     The single-component mask of each codec, so whether an entity carries it is one bit test on the entity's own
	///     mask rather than a codec call that would only report the component is not there.
	/// </summary>
	private static ComponentMask[] SelectMasks(IReplicatedComponentCodec[] codecs)
	{
		var masks = new ComponentMask[codecs.Length];
		for (int index = 0; index < codecs.Length; index++) masks[index] = ComponentMask.FromId(codecs[index].ComponentTypeId);
		return masks;
	}

	/// <summary>The peer id each player slot is connected through, refreshed once per tick before any peer is described.</summary>
	public void PrepareFor(ReadOnlySpan<int> peerIdByPlayerSlot) => peerIdByPlayerSlot.CopyTo(_peerIdByPlayerSlot);

	/// <summary>
	///     What the map registered about abilities. A map marks an order unavailable or hides a button on the authority,
	///     so how each unit's abilities are shown is described from here rather than worked out again on every client.
	/// </summary>
	public ScriptAbilityRegistry? Abilities { get; set; }

	/// <summary>
	///     How far each player is up each of the map's tech tracks. A peer that joins a match already in progress learns
	///     what every player has researched from the description it is given.
	/// </summary>
	public ServerTechLevels? TechLevels { get; set; }

	/// <summary>
	///     Forgets everything a peer was told. A peer that disconnects leaves this behind, and the peer that takes its
	///     place is described from scratch rather than from a description built for someone else.
	/// </summary>
	public void ForgetPeer(int peerId)
	{
		_peerStates.Forget(peerId);
		Abilities?.ForgetPeer(peerId);
	}

	/// <summary>
	///     Describes the world as the viewer's player can see it. What this peer was already told is left out unless it
	///     changed, and the first description a peer is given is a baseline it can recover from.
	/// </summary>
	public WorldSnapshot Build(int peerId, int sequence, PlayerEntity viewer, bool forceBaseline = false)
	{
		SnapshotPeerState state = _peerStates.Get(peerId);
		bool isBaseline = forceBaseline || state.LastBaselineSequence < 0;
		if (isBaseline)
		{
			state.LastBaselineSequence = sequence;
			state.ResetForDescription();
		}

		CollectDescribedUnits(viewer.Value);

		var snapshot = new WorldSnapshot
		{
			Sequence = sequence,
			IsBaseline = isBaseline,
			BaseSequence = isBaseline ? sequence : state.LastBaselineSequence,
			Units = new List<UnitSnapshot>(_describedUnits.Count),
			ColdPayloads = new List<ColdPayloadDelta>(),
			ComponentDeltas = new List<ComponentDelta>(),
			AbilityStates = new List<AbilityStateDelta>(),
			StaticTexts = new List<StaticTextDelta>(),
			Catalog = new List<CatalogEntry>(),
			UnitComponentPresence = isBaseline ? new List<UnitComponentPresence>(_describedUnits.Count) : null,
			StateFingerprint = 0u
		};

		for (int index = 0; index < _describedUnits.Count; index++)
		{
			(Entity unitEntity, Vector3 unitPosition) = _describedUnits[index];
			UnitSnapshot described = DescribeUnit(unitEntity, unitPosition);

			// A unit that is exactly where this peer was last told it is carries no information: a building, a unit
			// holding position or one standing where its order ended all stay on the peer's screen as they are. It is
			// left out of the description and its record is kept, so the tick it moves it is described again. A
			// baseline always states every unit, because that is what a peer with nothing recovers from.
			if (!isBaseline && state.SentUnits.TryGetValue(unitEntity.Id, out UnitSnapshot sent) && MatchesIn(in described, in sent)) continue;

			state.SentUnits[unitEntity.Id] = described;
			snapshot.Units.Add(described);
		}

		if (isBaseline) AppendRuleEntries(snapshot.Catalog);
		AppendTextEntries(state, snapshot.Catalog);
		AppendContentPayloads(state, snapshot);
		if (Abilities != null)
		{
			// The ability registry keeps one scratch table per process rather than one per peer, so two workers
			// describing two peers may not be inside it at once. Everything this match spends on describing a peer is
			// outside this lock; the ability state of a unit is a handful of entries, not the unit records themselves.
			lock (_abilityLock) snapshot.AbilityStates = new List<AbilityStateDelta>(Abilities.CollectDeltas(peerId, _world, isBaseline));
		}

		AppendComponentDeltas(state, snapshot, isBaseline);
		ForgetUntrackedUnitComponents(state);
		return snapshot;
	}

	/// <summary>
	///     Whether two descriptions of a unit say the same thing, which is what makes leaving the second one out of a
	///     delta safe. Every replicated field of the record is compared: a unit that is standing still but has lost hit
	///     points, started dying or been repainted by its owner is still a change the peer has to hear.
	/// </summary>
	private static bool MatchesIn(in UnitSnapshot described, in UnitSnapshot sent)
	{
		if (described.EntityId != sent.EntityId) return false;
		if (described.OwnerPlayerEntityId != sent.OwnerPlayerEntityId) return false;
		if (described.OwnerPlayerIndex != sent.OwnerPlayerIndex) return false;
		if (described.RotationY != sent.RotationY) return false;
		if (described.CurrentHp != sent.CurrentHp) return false;
		if (described.MaxHp != sent.MaxHp) return false;
		if (described.IsDead != sent.IsDead) return false;
		if (described.IsBuilding != sent.IsBuilding) return false;
		if (described.IsDetailed != sent.IsDetailed) return false;
		if (described.Position.X != sent.Position.X || described.Position.Y != sent.Position.Y || described.Position.Z != sent.Position.Z) return false;
		if (described.Velocity.X != sent.Velocity.X || described.Velocity.Y != sent.Velocity.Y || described.Velocity.Z != sent.Velocity.Z) return false;

		return string.Equals(described.UnitId, sent.UnitId, StringComparison.Ordinal);
	}

	/// <summary>
	///     States the rules the map published, in the channels a peer applies them through. A peer that never ran the map
	///     script has no other way to learn which buildings take a delivery, which units gather, which building is a
	///     player's base, how much room a placement needs, or what a resource node yields, and it needs all of them to
	///     resolve an order the authority and it both understand.
	/// </summary>
	private void AppendRuleEntries(List<CatalogEntry> entries)
	{
		IReadOnlyList<(string DefinitionKey, float Radius)> placementRadii = _rules.PlacementRadiusRules;
		for (int index = 0; index < placementRadii.Count; index++)
		{
			entries.Add(new CatalogEntry { Channel = "placement_radius", Key = placementRadii[index].DefinitionKey, Number = placementRadii[index].Radius });
		}

		IReadOnlyList<string> dropoffBuildings = _rules.ResourceDropoffBuildingKeys;
		for (int index = 0; index < dropoffBuildings.Count; index++)
		{
			entries.Add(new CatalogEntry { Channel = "dropoff", Key = dropoffBuildings[index] });
		}

		IReadOnlyList<string> workerUnits = _rules.ResourceWorkerUnitKeys;
		for (int index = 0; index < workerUnits.Count; index++)
		{
			entries.Add(new CatalogEntry { Channel = "worker", Key = workerUnits[index] });
		}

		IReadOnlyList<string> primaryBuildings = _rules.PrimaryBuildingIds;
		for (int index = 0; index < primaryBuildings.Count; index++)
		{
			entries.Add(new CatalogEntry { Channel = "primary", Key = primaryBuildings[index] });
		}

		foreach (var nodeType in _rules.ResourceNodeTypes)
		{
			entries.Add(new CatalogEntry { Channel = "resource_node_type", Key = nodeType.Key, Value = nodeType.Value });
		}

		TechLevels?.AppendDescription(entries);
	}

	/// <summary>The channel a peer receives the authority's text table in, one entry per identifier.</summary>
	private const string TextChannel = "string";

	/// <summary>
	///     Describes the text the authority registered, so a peer can name the content ids every other description is
	///     made of. An identifier is a content hash and travels as a number, so a peer that never registered a map's own
	///     text, such as a client that never ran its map script, could not render the item a unit carries, the consumable
	///     bound to it or the team it belongs to without this. Only identifiers this peer has not been told are sent, so
	///     the cost after the first snapshot is zero until the map registers new text.
	/// </summary>
	private void AppendTextEntries(SnapshotPeerState state, List<CatalogEntry> entries)
	{
		int entryCount = StringRegistry.EntryCount;
		int cursor = state.StringTableCursor > entryCount ? 0 : state.StringTableCursor;

		_stringTableEntries.Clear();
		state.StringTableCursor = StringRegistry.GetEntriesSince(cursor, _stringTableEntries);

		for (int index = 0; index < _stringTableEntries.Count; index++)
		{
			(int id, string text) = _stringTableEntries[index];
			if (id == 0 || string.IsNullOrEmpty(text)) continue;

			entries.Add(new CatalogEntry
			{
				Channel = TextChannel,
				Key = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
				Value = text
			});
		}
	}

	private readonly List<(int Id, string Text)> _stringTableEntries = new();

	/// <summary>
	///     Describes the content the world names by handle: an ability list a unit offers, the queue a building is
	///     working through, and the zones the map defined. A component carries only the handle, which is a content hash,
	///     so a peer that never registered the payload behind it could not name a single ability or queued unit. Only
	///     payloads this peer has not been sent travel, so a list that never changes costs nothing after the first
	///     description of it.
	/// </summary>
	private void AppendContentPayloads(SnapshotPeerState state, WorldSnapshot snapshot)
	{
		if (_world.IsAlive(_worldEntity) && _world.TryGet<ScriptZonesState>(_worldEntity, out ScriptZonesState zones))
		{
			AppendContentPayload(state, snapshot, (byte)ReplicatedScope.World, 0, ColdPayloadRole.ZoneList, zones.Handle);
		}

		for (int index = 0; index < _describedUnits.Count; index++)
		{
			Entity unitEntity = _describedUnits[index].Entity;

			if (_world.TryGet<ProductionQueue>(unitEntity, out ProductionQueue queue) && queue.Count > 0)
			{
				AppendContentPayload(state, snapshot, (byte)ReplicatedScope.Unit, unitEntity.Id, ColdPayloadRole.ProductionQueue, queue.Handle);
			}

			if (_world.TryGet<AbilityState>(unitEntity, out AbilityState abilityState) && abilityState.Count > 0)
			{
				AppendContentPayload(state, snapshot, (byte)ReplicatedScope.Unit, unitEntity.Id, ColdPayloadRole.AbilityList, abilityState.Handle);
			}
		}
	}

	private void AppendContentPayload(SnapshotPeerState state, WorldSnapshot snapshot, byte scope, int key, ColdPayloadRole role, int handle)
	{
		if (handle == 0 || !state.SentContentIds.Add(handle)) return;
		if (!ContentIdStore.TryGetPayload(handle, out byte contentKind, out ReadOnlySpan<byte> payload)) return;

		snapshot.ColdPayloads.Add(new ColdPayloadDelta
		{
			Scope = scope,
			Key = key,
			Kind = (byte)ColdPayloadKind.ContentIdPayload,
			Role = (byte)role,
			ContentKind = contentKind,
			ContentId = handle,
			Destroyed = 0,
			Bytes = payload.ToArray()
		});
	}

	private void CollectDescribedUnits(Entity viewerPlayer)
	{
		_describedUnits.Clear();
		_describedEntityIds.Clear();
		_viewer = viewerPlayer;
		_playerEntities = Array.Empty<Entity>();
		_visionSources.Clear();

		// The cells are emptied rather than replaced. A peer's own units sit in a handful of cells tick after tick, and
		// allocating a fresh list for every one of them on every tick was one of the larger allocations on this path.
		foreach (var cell in _visionCellSources) cell.Value.Clear();

		if (viewerPlayer != Entity.Null) CollectVisionSources();
		_world.Query(in DescribedUnitsQuery, _collectDescribedUnits);
	}

	private void CollectDescribedUnit(Entity entity, ref UnitOwnerPlayer owner, ref Position position)
	{
		if (!IsVisibleToViewer(entity, position.Value)) return;

		_describedUnits.Add((entity, position.Value));
		_describedEntityIds.Add(entity.Id);
	}

	/// <summary>
	///     Whether the viewer can see an entity at all: what it owns is its own business, and anything else has to be
	///     inside the sight of something the viewer has on the field. That is the rule the shroud grid encodes, read
	///     from the entities that cast it.
	/// </summary>
	private bool IsVisibleToViewer(Entity entity, Vector3 position)
	{
		// A map that shrouds nothing is a board every player watches whole, so the authority describes all of it rather
		// than making each peer's screen reconstruct what its own units happen to be near. Only a map that keeps its
		// shroud pays for the vision filter.
		if (_context.ShroudDisabled) return true;
		if (_viewer == Entity.Null) return true;
		if (!_world.TryGet<Owner>(entity, out Owner owner)) return true;
		if (owner.PlayerEntity.Value == _viewer) return true;

		return IsWithinVision(position);
	}

	private void CollectVisionSources()
	{
		_world.Query(in DescribedUnitsQuery, _collectVisionSources);

		for (int index = 0; index < _visionSources.Count; index++)
		{
			(float x, float z) cell = VisionCell(_visionSources[index].Position);
			if (!_visionCellSources.TryGetValue(cell, out List<int>? sources))
			{
				sources = new List<int>();
				_visionCellSources[cell] = sources;
			}

			sources.Add(index);
		}
	}

	private void CollectVisionSource(Entity entity, ref UnitOwnerPlayer owner, ref Position position)
	{
		if (!_world.TryGet<Owner>(entity, out Owner entityOwner)) return;
		if (entityOwner.PlayerEntity.Value != _viewer) return;

		float radius = _world.TryGet<ScanRadius>(entity, out ScanRadius scanRadius) ? scanRadius.Value : DefaultVisionRadius;
		if (radius <= 0f) return;

		_visionSources.Add((position.Value, radius));
	}

	private bool IsWithinVision(Vector3 position)
	{
		(float x, float z) cell = VisionCell(position);

		for (int offsetX = -VisionCellReach; offsetX <= VisionCellReach; offsetX++)
		{
			for (int offsetZ = -VisionCellReach; offsetZ <= VisionCellReach; offsetZ++)
			{
				if (!_visionCellSources.TryGetValue((cell.x + offsetX, cell.z + offsetZ), out List<int>? sources)) continue;

				for (int index = 0; index < sources.Count; index++)
				{
					(Vector3 sourcePosition, float radius) = _visionSources[sources[index]];
					if (Vector3.DistanceSquared(sourcePosition, position) <= radius * radius) return true;
				}
			}
		}

		return false;
	}

	private static (float X, float Z) VisionCell(Vector3 position)
	{
		float x = Math.Clamp(position.X, -MaximumDescribedCoordinate, MaximumDescribedCoordinate);
		float z = Math.Clamp(position.Z, -MaximumDescribedCoordinate, MaximumDescribedCoordinate);
		return (MathF.Floor(x / VisionCellSize), MathF.Floor(z / VisionCellSize));
	}

	private UnitSnapshot DescribeUnit(Entity entity, Vector3 position)
	{
		Vector3 velocity = _world.TryGet<Velocity>(entity, out Velocity describedVelocity) ? describedVelocity.Value : Vector3.Zero;
		float rotation = _world.TryGet<RotationY>(entity, out RotationY describedRotation) ? describedRotation.Value : 0f;
		Health health = _world.Get<Health>(entity);

		return new UnitSnapshot
		{
			EntityId = entity.Id,
			UnitId = _world.TryGet<DefinitionId>(entity, out DefinitionId definition) ? ResolveDefinitionText(definition.StringId) : string.Empty,
			OwnerPlayerEntityId = ResolveOwnerPeerId(entity),
			OwnerPlayerIndex = _world.TryGet<UnitOwnerPlayer>(entity, out UnitOwnerPlayer owner) ? owner.PlayerIndex : -1,
			Position = new NetworkVector3(position.X, position.Y, position.Z),
			RotationY = rotation,
			CurrentHp = health.Current,
			MaxHp = health.Max,
			IsDead = _world.Has<Dead>(entity),
			IsBuilding = _world.Has<Building>(entity),
			IsDetailed = true,
			Velocity = new NetworkVector3(velocity.X, velocity.Y, velocity.Z)
		};
	}

	/// <summary>
	///     The text an identifier stands for, remembered per builder. The table behind it is guarded by one lock for the
	///     whole process, and this was the lock a tick took once for every unit it described to every peer: thirteen
	///     hundred times over for a room of twenty-four, all answering with the same few dozen strings.
	/// </summary>
	private string ResolveDefinitionText(int stringId)
	{
		if (_textByStringId.TryGetValue(stringId, out string? known)) return known;

		string text = StringRegistry.Get(stringId);
		_textByStringId[stringId] = text;
		return text;
	}

	private int ResolveOwnerPeerId(Entity unitEntity)
	{
		if (!_world.TryGet<Owner>(unitEntity, out Owner owner)) return 0;
		if (!_world.TryGet<UnitOwnerPlayer>(owner.PlayerEntity.Value, out UnitOwnerPlayer ownerPlayer)) return 0;

		int playerIndex = ownerPlayer.PlayerIndex;
		if ((uint)playerIndex >= (uint)_peerIdByPlayerSlot.Length) return 0;

		return _peerIdByPlayerSlot[playerIndex];
	}

	private void AppendComponentDeltas(SnapshotPeerState state, WorldSnapshot snapshot, bool isBaseline)
	{
		for (int codecIndex = 0; codecIndex < WorldCodecs.Length; codecIndex++)
		{
			AppendComponentDelta(state, snapshot, WorldCodecs[codecIndex], _worldEntity, (byte)ReplicatedScope.World, 0, isBaseline);
		}

		Entity[] playerEntities = ResolvePlayerEntities();
		for (int codecIndex = 0; codecIndex < PlayerCodecs.Length; codecIndex++)
		{
			IReplicatedComponentCodec codec = PlayerCodecs[codecIndex];

			for (int slot = 0; slot < playerEntities.Length; slot++)
			{
				Entity playerEntity = playerEntities[slot];
				if (playerEntity == Entity.Null || !_world.IsAlive(playerEntity)) continue;

				AppendComponentDelta(state, snapshot, codec, playerEntity, (byte)ReplicatedScope.Player, slot, isBaseline);
			}
		}

		for (int unitIndex = 0; unitIndex < _describedUnits.Count; unitIndex++)
		{
			Entity unitEntity = _describedUnits[unitIndex].Entity;
			ulong presenceMask = 0;

			for (int codecIndex = 0; codecIndex < UnitCodecs.Length; codecIndex++)
			{
				IReplicatedComponentCodec codec = UnitCodecs[codecIndex];

				// A unit carries a handful of the components this match replicates. Asking its own mask costs one bit
				// test where calling the codec cost a virtual call that only ever answered "this unit does not have me".
				if (!_world.Has(unitEntity, UnitCodecMasks[codecIndex]))
				{
					RemoveComponentDeltaWhenSent(state, snapshot, codec, (byte)ReplicatedScope.Unit, unitEntity.Id);
					continue;
				}

				if (!AppendComponentDelta(state, snapshot, codec, unitEntity, (byte)ReplicatedScope.Unit, unitEntity.Id, isBaseline)) continue;
				if (!ReplicatedUnitComponentSlots.TryGetSlot(codec.ComponentTypeId, out int presenceSlot)) continue;

				presenceMask |= 1UL << presenceSlot;
			}

			snapshot.UnitComponentPresence?.Add(new UnitComponentPresence
			{
				ServerEntityId = unitEntity.Id,
				Mask = presenceMask
			});
		}
	}

	/// <summary>
	///     Describes one component, unless this peer was already told exactly this value. A component the entity no
	///     longer carries is stated as a removal, which is what lets a peer drop what the authority dropped.
	/// </summary>
	private bool AppendComponentDelta(SnapshotPeerState state, WorldSnapshot snapshot, IReplicatedComponentCodec codec, Entity entity, byte scope, int key, bool isBaseline)
	{
		if (_writeScratch.Length < codec.SizeInBytes) _writeScratch = new byte[codec.SizeInBytes];

		Span<byte> scratch = _writeScratch.AsSpan(0, codec.SizeInBytes);
		if (!codec.TryWrite(_world, entity, scratch))
		{
			RemoveComponentDeltaWhenSent(state, snapshot, codec, scope, key);
			return false;
		}

		var deltaKey = (scope, key, codec.ComponentTypeId);
		if (!isBaseline && state.SentComponents.TryGetValue(deltaKey, out byte[]? previous))
		{
			if (previous.AsSpan().SequenceEqual(scratch)) return true;

			// The payload is written into the buffer this peer's description already owned. A component that changes
			// every tick used to allocate a new array for every peer that had to be told about it, and those arrays
			// were the bulk of what this process left to the collector.
			scratch.CopyTo(previous);
			snapshot.ComponentDeltas.Add(new ComponentDelta
			{
				Key = key,
				ComponentTypeId = codec.ComponentTypeId,
				Scope = scope,
				Bytes = previous
			});
			return true;
		}

		var bytes = new byte[codec.SizeInBytes];
		scratch.CopyTo(bytes);
		state.SentComponents[deltaKey] = bytes;

		snapshot.ComponentDeltas.Add(new ComponentDelta
		{
			Key = key,
			ComponentTypeId = codec.ComponentTypeId,
			Scope = scope,
			Bytes = bytes
		});
		return true;
	}

	/// <summary>
	///     States that an entity no longer carries a component this peer was told it had, which is what lets a peer drop
	///     what the authority dropped.
	/// </summary>
	private static void RemoveComponentDeltaWhenSent(SnapshotPeerState state, WorldSnapshot snapshot, IReplicatedComponentCodec codec, byte scope, int key)
	{
		if (!state.SentComponents.Remove((scope, key, codec.ComponentTypeId))) return;

		snapshot.ComponentDeltas.Add(new ComponentDelta
		{
			Key = key,
			ComponentTypeId = codec.ComponentTypeId,
			Scope = scope,
			Bytes = Array.Empty<byte>()
		});
	}

	/// <summary>
	///     Stops tracking the components of a unit this peer can no longer see. The unit is not described again until it
	///     comes back, and nothing tells the peer to drop what it holds: a unit that walked out of vision is still where
	///     the peer last saw it, and only a death or a removal says otherwise.
	/// </summary>
	private void ForgetUntrackedUnitComponents(SnapshotPeerState state)
	{
		// Only the units that left this peer's sight are dropped, found by comparing the set it was described with last
		// tick against the one it is described with now. Walking every remembered component instead cost a full pass
		// over the peer's whole history on every tick, and a peer whose sight is steady has nothing to forget at all.
		_forgottenEntityIds.Clear();
		foreach (int rememberedEntityId in state.DescribedEntityIds)
		{
			if (!_describedEntityIds.Contains(rememberedEntityId)) _forgottenEntityIds.Add(rememberedEntityId);
		}

		for (int index = 0; index < _forgottenEntityIds.Count; index++)
		{
			int forgottenEntityId = _forgottenEntityIds[index];
			state.SentUnits.Remove(forgottenEntityId);

			for (int codecIndex = 0; codecIndex < UnitCodecs.Length; codecIndex++)
			{
				state.SentComponents.Remove(((byte)ReplicatedScope.Unit, forgottenEntityId, UnitCodecs[codecIndex].ComponentTypeId));
			}
		}

		state.DescribedEntityIds.Clear();
		foreach (int describedEntityId in _describedEntityIds) state.DescribedEntityIds.Add(describedEntityId);
	}

	private Entity[] ResolvePlayerEntities()
	{
		if (_playerEntities.Length > 0) return _playerEntities;
		if (!_world.IsAlive(_worldEntity)) return _playerEntities;
		if (!_world.TryGet<NetworkMappingState>(_worldEntity, out NetworkMappingState mapping)) return _playerEntities;
		if (mapping.PlayerEntities == null) return _playerEntities;

		_playerEntities = mapping.PlayerEntities;
		return _playerEntities;
	}
}
