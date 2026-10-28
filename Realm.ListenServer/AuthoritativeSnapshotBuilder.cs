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

	private static readonly QueryDescription DescribedUnitsQuery = new QueryDescription().WithAll<UnitOwnerPlayer, Health, Position>();

	private static readonly IReplicatedComponentCodec[] WorldCodecs = SelectCodecs(ReplicatedScope.World);
	private static readonly IReplicatedComponentCodec[] PlayerCodecs = SelectCodecs(ReplicatedScope.Player);
	private static readonly IReplicatedComponentCodec[] UnitCodecs = SelectCodecs(ReplicatedScope.Unit);
	private static readonly ComponentMask[] UnitCodecMasks = SelectMasks(UnitCodecs);

	/// <summary>
	///     How many components one unit can be described with. One component of one unit is described once for the whole
	///     room, so the description cache needs room for this many per entity.
	/// </summary>
	public static int UnitCodecCount => UnitCodecs.Length;

	static AuthoritativeSnapshotBuilder()
	{
		// What a unit carries this tick, what a peer was told it carried and the presence a baseline states are each held
		// in one word. A component added beyond the last bit would be described by a mask that cannot name it.
		if (UnitCodecs.Length > 64) throw new InvalidOperationException("A unit cannot be described with more than 64 replicated components.");
	}

	/// <summary>The presence slot of each unit codec, or minus one for a codec the wire contract does not state presence for.</summary>
	private static readonly int[] UnitCodecPresenceSlots = SelectPresenceSlots(UnitCodecs);

	private static int[] SelectPresenceSlots(IReplicatedComponentCodec[] codecs)
	{
		var slots = new int[codecs.Length];
		for (int index = 0; index < codecs.Length; index++)
		{
			slots[index] = ReplicatedUnitComponentSlots.TryGetSlot(codecs[index].ComponentTypeId, out int slot) ? slot : -1;
		}

		return slots;
	}

	private readonly EcsWorld _world;
	private readonly SimulationContext _context;
	private readonly Entity _worldEntity;
	private readonly MapRulesCatalog _rules;
	private readonly SnapshotPeerStateStore _peerStates;
	private readonly SnapshotDescriptionCache _descriptions;
	private readonly object _abilityLock;
	private readonly int[] _peerIdByPlayerSlot = new int[EcsWorld.MAX_PLAYERS];
	private readonly EntityCallback<UnitOwnerPlayer, Position> _collectCandidates;
	private readonly List<(Entity Entity, Vector3 Position)> _candidates = new();
	private readonly bool[] _candidateVisibility = new bool[EcsWorld.MAX_ENTITIES];
	private readonly List<(Entity Entity, Vector3 Position)> _describedUnits = new();
	private readonly List<UnitDescriptionState> _describedUnitStates = new();
	private readonly HashSet<int> _describedEntityIds = new();
	private readonly List<int> _forgottenEntityIds = new();
	private readonly Dictionary<(int X, int Z), List<int>> _unitCells = new();
	private readonly List<(Vector3 Position, float Radius)> _visionSources = new();
	private readonly Dictionary<int, string> _textByStringId = new();
	private byte[] _writeScratch = Array.Empty<byte>();
	private Entity[] _playerEntities = Array.Empty<Entity>();
	private Entity _viewer = Entity.Null;

	public AuthoritativeSnapshotBuilder(EcsWorld world, SimulationContext context, Entity worldEntity, MapRulesCatalog rules, SnapshotPeerStateStore peerStates, SnapshotDescriptionCache descriptions, object abilityLock)
	{
		_world = world;
		_context = context;
		_worldEntity = worldEntity;
		_rules = rules;
		_peerStates = peerStates;
		_descriptions = descriptions;
		_abilityLock = abilityLock;
		_collectCandidates = CollectCandidate;
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

		CollectDescribedUnits(viewer.Value, sequence);
		_describedUnitStates.Clear();

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

			// What this tick says about a unit is the same for every peer that can see it, so the first peer to reach it
			// describes it and the rest read what it said. A room of twenty-four peers sharing a field of units used to
			// read every component of every visible unit once per peer.
			UnitSnapshot described;
			if (!_descriptions.TryGetUnitRecord(sequence, unitEntity.Id, out described))
			{
				described = DescribeUnit(unitEntity, unitPosition);
				_descriptions.SetUnitRecord(sequence, unitEntity.Id, in described);
			}

			// The record of this unit this peer holds, which both what it is told and which components it holds are written
			// into. A unit it has not been told about yet starts from nothing, so its first record states everything.
			if (!state.SentUnits.TryGetValue(unitEntity.Id, out UnitDescriptionState? held))
			{
				held = new UnitDescriptionState(UnitCodecs.Length);
				state.SentUnits[unitEntity.Id] = held;
			}

			_describedUnitStates.Add(held);

			// A unit that is exactly where this peer was last told it is carries no information: a building, a unit
			// holding position or one standing where its order ended all stay on the peer's screen as they are. It is
			// left out of the description and its record is kept, so the tick it moves it is described again. A
			// baseline always states every unit, because that is what a peer with nothing recovers from.
			if (!isBaseline && MatchesIn(in described, in held.SentRecord)) continue;

			held.SentRecord = described;
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

		AppendComponentDeltas(state, snapshot, sequence, isBaseline);
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

	/// <summary>
	///     Gathers the units this peer is told about. A map that shrouds nothing shows every peer the whole board, so what
	///     is visible is the same answer for the entire room and is walked once per tick rather than once per peer; only a
	///     map that keeps its shroud makes each peer look at the world through its own units.
	/// </summary>
	private void CollectDescribedUnits(Entity viewerPlayer, int tick)
	{
		_describedUnits.Clear();
		_describedEntityIds.Clear();
		_viewer = viewerPlayer;
		_playerEntities = Array.Empty<Entity>();

		if (_context.ShroudDisabled || viewerPlayer == Entity.Null)
		{
			CollectWholeBoard(tick);
			return;
		}

		// The cells are emptied rather than replaced. A peer's units sit in a handful of cells tick after tick, and
		// allocating a fresh list for every one of them on every tick was one of the larger allocations on this path.
		foreach (var cell in _unitCells) cell.Value.Clear();

		_candidates.Clear();
		_visionSources.Clear();
		_world.Query(in DescribedUnitsQuery, _collectCandidates);

		MarkUnitsWithinSight();
		AppendVisibleCandidates();
	}

	/// <summary>
	///     Takes the visibility every peer of an unshrouded map shares, walking the board for this tick if no other peer
	///     has walked it yet. The worker that walks it is the only one that does: the others wait for it and are described
	///     from what it found, which is what makes the walk one pass over the board instead of one per peer.
	/// </summary>
	private void CollectWholeBoard(int tick)
	{
		lock (_descriptions.WholeBoardGate)
		{
			if (_descriptions.WholeBoardTick != tick)
			{
				_candidates.Clear();
				_world.Query(in DescribedUnitsQuery, _collectCandidates);

				List<(Entity Entity, Vector3 Position)> wholeBoardUnits = _descriptions.WholeBoardUnits;
				List<int> wholeBoardEntityIds = _descriptions.WholeBoardEntityIds;
				wholeBoardUnits.Clear();
				wholeBoardEntityIds.Clear();

				for (int index = 0; index < _candidates.Count; index++)
				{
					wholeBoardUnits.Add(_candidates[index]);
					wholeBoardEntityIds.Add(_candidates[index].Entity.Id);
					_describedUnits.Add(_candidates[index]);
					_describedEntityIds.Add(_candidates[index].Entity.Id);
				}

				_descriptions.PublishWholeBoard(tick);
				return;
			}

			List<(Entity Entity, Vector3 Position)> sharedUnits = _descriptions.WholeBoardUnits;
			List<int> sharedEntityIds = _descriptions.WholeBoardEntityIds;
			for (int index = 0; index < sharedUnits.Count; index++) _describedUnits.Add(sharedUnits[index]);
			for (int index = 0; index < sharedEntityIds.Count; index++) _describedEntityIds.Add(sharedEntityIds[index]);
		}
	}

	/// <summary>
	///     Gathers every unit that could be described, and what each one of them is to the viewer: what the viewer owns is
	///     in its own sight, what nobody owns is on the board for everyone, and everything else is decided afterwards by
	///     where the viewer's own units are looking. The units are filed into cells as they are gathered, so the second
	///     pass visits only the units a source could possibly see instead of testing every unit against every source.
	/// </summary>
	private void CollectCandidate(Entity entity, ref UnitOwnerPlayer owner, ref Position position)
	{
		bool visible = true;
		if (_world.TryGet<Owner>(entity, out Owner entityOwner))
		{
			if (entityOwner.PlayerEntity.Value == _viewer)
			{
				float radius = _world.TryGet<ScanRadius>(entity, out ScanRadius scanRadius) ? scanRadius.Value : DefaultVisionRadius;
				if (radius > 0f) _visionSources.Add((position.Value, radius));
			}
			else visible = false;
		}

		int candidateIndex = _candidates.Count;
		_candidates.Add((entity, position.Value));
		_candidateVisibility[candidateIndex] = visible;

		(int cellX, int cellZ) cell = VisionCell(position.Value);
		if (!_unitCells.TryGetValue(cell, out List<int>? unitsInCell))
		{
			unitsInCell = new List<int>();
			_unitCells[cell] = unitsInCell;
		}

		unitsInCell.Add(candidateIndex);
	}

	/// <summary>
	///     Marks every unit that lies inside the sight of one of the viewer's own units. Each source states the cells its
	///     circle reaches and each unit in those cells is measured against it, so a source costs the units around it rather
	///     than the whole board. A sight wide enough to cover more cells than there are units is answered by walking the
	///     units instead, because a map that reveals everything would otherwise probe thousands of empty cells.
	/// </summary>
	private void MarkUnitsWithinSight()
	{
		for (int sourceIndex = 0; sourceIndex < _visionSources.Count; sourceIndex++)
		{
			(Vector3 sourcePosition, float radius) = _visionSources[sourceIndex];
			float squaredRadius = radius * radius;

			(int minimumCellX, int minimumCellZ) = VisionCellCoordinates(sourcePosition.X - radius, sourcePosition.Z - radius);
			(int maximumCellX, int maximumCellZ) = VisionCellCoordinates(sourcePosition.X + radius, sourcePosition.Z + radius);
			long coveredCells = (long)(maximumCellX - minimumCellX + 1) * (maximumCellZ - minimumCellZ + 1);

			if (coveredCells > _unitCells.Count)
			{
				MarkUnitsWithinSightOf(sourcePosition, squaredRadius, null);
				continue;
			}

			for (int cellX = minimumCellX; cellX <= maximumCellX; cellX++)
			{
				for (int cellZ = minimumCellZ; cellZ <= maximumCellZ; cellZ++)
				{
					if (_unitCells.TryGetValue((cellX, cellZ), out List<int>? unitsInCell)) MarkUnitsWithinSightOf(sourcePosition, squaredRadius, unitsInCell);
				}
			}
		}
	}

	/// <summary>
	///     Marks the units of one cell, or every unit when no cell is named, that a source can see. A unit that is already
	///     visible is left alone, so a unit under the sight of several of the viewer's units is measured once.
	/// </summary>
	private void MarkUnitsWithinSightOf(Vector3 sourcePosition, float squaredRadius, List<int>? unitsInCell)
	{
		int count = unitsInCell?.Count ?? _candidates.Count;
		for (int index = 0; index < count; index++)
		{
			int candidateIndex = unitsInCell == null ? index : unitsInCell[index];
			if (_candidateVisibility[candidateIndex]) continue;
			if (Vector3.DistanceSquared(sourcePosition, _candidates[candidateIndex].Position) > squaredRadius) continue;

			_candidateVisibility[candidateIndex] = true;
		}
	}

	private void AppendVisibleCandidates()
	{
		for (int index = 0; index < _candidates.Count; index++)
		{
			if (!_candidateVisibility[index]) continue;

			(Entity entity, Vector3 position) = _candidates[index];
			_describedUnits.Add((entity, position));
			_describedEntityIds.Add(entity.Id);
		}
	}

	private static (int X, int Z) VisionCell(Vector3 position) => VisionCellCoordinates(position.X, position.Z);

	private static (int X, int Z) VisionCellCoordinates(float x, float z)
	{
		float clampedX = Math.Clamp(x, -MaximumDescribedCoordinate, MaximumDescribedCoordinate);
		float clampedZ = Math.Clamp(z, -MaximumDescribedCoordinate, MaximumDescribedCoordinate);
		return ((int)MathF.Floor(clampedX / VisionCellSize), (int)MathF.Floor(clampedZ / VisionCellSize));
	}

	private UnitSnapshot DescribeUnit(Entity entity, Vector3 position)
	{
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
			IsDetailed = true
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

	private void AppendComponentDeltas(SnapshotPeerState state, WorldSnapshot snapshot, int sequence, bool isBaseline)
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
			AppendUnitComponentDeltas(snapshot, _describedUnits[unitIndex].Entity, _describedUnitStates[unitIndex], sequence, isBaseline);
		}
	}

	/// <summary>
	///     Describes the components one unit carries to one peer. Which components a unit carries, the bytes they are
	///     written into and the presence a baseline states are all the same for every peer, so they are worked out once
	///     per tick; what is left here is one peer comparing its own record of what it was told.
	/// </summary>
	private void AppendUnitComponentDeltas(WorldSnapshot snapshot, Entity unitEntity, UnitDescriptionState held, int tick, bool isBaseline)
	{
		if (!_descriptions.TryGetUnitComponentState(tick, unitEntity.Id, out ulong presentCodecMask, out ulong presenceMask))
		{
			(presentCodecMask, presenceMask) = DescribeUnitComponentState(unitEntity);
			_descriptions.SetUnitComponentState(tick, unitEntity.Id, presentCodecMask, presenceMask);
		}

		// A component the authority dropped since this peer was told about it is stated as a removal, which is what lets
		// a peer drop what the authority dropped.
		for (ulong forgottenBits = held.ToldCodecMask & ~presentCodecMask; forgottenBits != 0; forgottenBits &= forgottenBits - 1)
		{
			RemoveSentUnitComponent(snapshot, held, BitOperations.TrailingZeroCount(forgottenBits), unitEntity.Id);
		}

		ulong describedMask = 0;
		for (ulong presentBits = presentCodecMask; presentBits != 0; presentBits &= presentBits - 1)
		{
			int codecIndex = BitOperations.TrailingZeroCount(presentBits);
			if (!AppendUnitComponentDelta(snapshot, held, UnitCodecs[codecIndex], codecIndex, unitEntity, tick, isBaseline)) continue;

			describedMask |= 1UL << codecIndex;
		}

		held.ToldCodecMask = describedMask;
		snapshot.UnitComponentPresence?.Add(new UnitComponentPresence
		{
			ServerEntityId = unitEntity.Id,
			Mask = presenceMask
		});
	}

	/// <summary>
	///     Which of the components a unit can be described with it carries, and which of those a baseline states the
	///     presence of. A unit carries a handful of them, so walking the components it has is a fraction of walking every
	///     component the wire contract describes.
	/// </summary>
	private (ulong PresentCodecMask, ulong PresenceMask) DescribeUnitComponentState(Entity unitEntity)
	{
		ulong presentCodecMask = 0;
		ulong presenceMask = 0;

		for (int codecIndex = 0; codecIndex < UnitCodecs.Length; codecIndex++)
		{
			if (!_world.Has(unitEntity, UnitCodecMasks[codecIndex])) continue;

			presentCodecMask |= 1UL << codecIndex;

			int presenceSlot = UnitCodecPresenceSlots[codecIndex];
			if (presenceSlot >= 0) presenceMask |= 1UL << presenceSlot;
		}

		return (presentCodecMask, presenceMask);
	}

	/// <summary>
	///     Describes one component of one unit to one peer, unless it was already told exactly this value. The bytes come
	///     from this tick's description of that unit rather than from writing the component again for this peer.
	/// </summary>
	private bool AppendUnitComponentDelta(WorldSnapshot snapshot, UnitDescriptionState held, IReplicatedComponentCodec codec, int codecIndex, Entity entity, int tick, bool isBaseline)
	{
		if (!_descriptions.TryGetComponentPayload(tick, entity.Id, codecIndex, out ReadOnlySpan<byte> payload))
		{
			payload = WriteComponentPayload(codec, entity);
			if (payload.Length == 0)
			{
				RemoveSentUnitComponent(snapshot, held, codecIndex, entity.Id);
				return false;
			}

			_descriptions.SetComponentPayload(tick, entity.Id, codecIndex, payload);
		}

		byte[]? previous = isBaseline ? null : held.SentPayloads[codecIndex];
		if (previous != null)
		{
			if (previous.AsSpan().SequenceEqual(payload)) return true;

			// The payload is written into the buffer this peer's description already owned. A component that changes
			// every tick used to allocate a new array for every peer that had to be told about it, and those arrays
			// were the bulk of what this process left to the collector.
			payload.CopyTo(previous);
			snapshot.ComponentDeltas.Add(new ComponentDelta
			{
				Key = entity.Id,
				ComponentTypeId = codec.ComponentTypeId,
				Scope = (byte)ReplicatedScope.Unit,
				Bytes = previous
			});
			return true;
		}

		var bytes = new byte[codec.SizeInBytes];
		payload.CopyTo(bytes);
		held.SentPayloads[codecIndex] = bytes;

		snapshot.ComponentDeltas.Add(new ComponentDelta
		{
			Key = entity.Id,
			ComponentTypeId = codec.ComponentTypeId,
			Scope = (byte)ReplicatedScope.Unit,
			Bytes = bytes
		});
		return true;
	}

	/// <summary>
	///     States that a unit no longer carries a component this peer was told it had, which is what lets a peer drop what
	///     the authority dropped. A component the peer was never told about states nothing.
	/// </summary>
	private static void RemoveSentUnitComponent(WorldSnapshot snapshot, UnitDescriptionState held, int codecIndex, int entityId)
	{
		if (held.SentPayloads[codecIndex] == null) return;

		held.SentPayloads[codecIndex] = null;
		snapshot.ComponentDeltas.Add(new ComponentDelta
		{
			Key = entityId,
			ComponentTypeId = UnitCodecs[codecIndex].ComponentTypeId,
			Scope = (byte)ReplicatedScope.Unit,
			Bytes = Array.Empty<byte>()
		});
	}

	/// <summary>
	///     Writes one component of one entity into the builder's scratch, or nothing when the entity does not carry it.
	/// </summary>
	private ReadOnlySpan<byte> WriteComponentPayload(IReplicatedComponentCodec codec, Entity entity)
	{
		if (_writeScratch.Length < codec.SizeInBytes) _writeScratch = new byte[codec.SizeInBytes];

		Span<byte> scratch = _writeScratch.AsSpan(0, codec.SizeInBytes);
		return codec.TryWrite(_world, entity, scratch) ? scratch : ReadOnlySpan<byte>.Empty;
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

		// Everything this peer held about a unit that left its sight is on the unit's own record, so forgetting one is one
		// removal rather than one per component it might have carried.
		for (int index = 0; index < _forgottenEntityIds.Count; index++) state.SentUnits.Remove(_forgottenEntityIds[index]);

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
