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

public class AuthoritativeSnapshotBuilder
{
	private const float MaximumDescribedCoordinate = 100000f;
	private const float VisionCellSize = 16f;
	private const float DefaultVisionRadius = 15f;

	private static readonly QueryDescription DescribedUnitsQuery = new QueryDescription().WithAll<UnitOwnerPlayer, Health, Position>();

	private static readonly QueryDescription LootItemsQuery = new QueryDescription().WithAll<GroundLoot, Position>();

	private static readonly IReplicatedComponentCodec[] WorldCodecs = SelectCodecs(ReplicatedScope.World);
	private static readonly IReplicatedComponentCodec[] PlayerCodecs = SelectCodecs(ReplicatedScope.Player);
	private static readonly IReplicatedComponentCodec[] UnitCodecs = SelectCodecs(ReplicatedScope.Unit);
	private static readonly ComponentMask[] UnitCodecMasks = SelectMasks(UnitCodecs);

	public static int UnitCodecCount => UnitCodecs.Length;

	static AuthoritativeSnapshotBuilder()
	{
		if (UnitCodecs.Length > 64) throw new InvalidOperationException("A unit cannot be described with more than 64 replicated components.");
	}

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
	private readonly EntityCallback<GroundLoot, Position> _collectLootItems;
	private readonly List<(Entity Entity, Vector3 Position)> _candidates = new();
	private readonly List<(Entity Entity, GroundLoot Loot, Vector3 Position)> _lootItems = new();
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
		_collectLootItems = CollectLootItem;
		_collectCandidates = CollectCandidate;
	}

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

	private static ComponentMask[] SelectMasks(IReplicatedComponentCodec[] codecs)
	{
		var masks = new ComponentMask[codecs.Length];
		for (int index = 0; index < codecs.Length; index++) masks[index] = ComponentMask.FromId(codecs[index].ComponentTypeId);
		return masks;
	}

	public void PrepareFor(ReadOnlySpan<int> peerIdByPlayerSlot) => peerIdByPlayerSlot.CopyTo(_peerIdByPlayerSlot);

	public ScriptAbilityRegistry? Abilities { get; set; }

	public ServerTechLevels? TechLevels { get; set; }

	public void ForgetPeer(int peerId)
	{
		_peerStates.Forget(peerId);
		Abilities?.ForgetPeer(peerId);
	}

	public WorldSnapshot Build(int peerId, int sequence, PlayerEntity viewer, bool forceBaseline = false)
	{
		var snapshot = new WorldSnapshot();
		BuildInto(ref snapshot, peerId, sequence, viewer, forceBaseline);
		return snapshot;
	}

	public void BuildInto(ref WorldSnapshot snapshot, int peerId, int sequence, PlayerEntity viewer, bool forceBaseline = false)
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

		snapshot.ResetForDescription(sequence, isBaseline, isBaseline ? sequence : state.LastBaselineSequence);
		if (isBaseline) snapshot.UnitComponentPresence = new List<UnitComponentPresence>(_describedUnits.Count);

		for (int index = 0; index < _describedUnits.Count; index++)
		{
			(Entity unitEntity, Vector3 unitPosition) = _describedUnits[index];

			UnitSnapshot described;
			if (!_descriptions.TryGetUnitRecord(sequence, unitEntity.Id, out described))
			{
				described = DescribeUnit(unitEntity, unitPosition);
				_descriptions.SetUnitRecord(sequence, unitEntity.Id, in described);
			}

			if (!state.SentUnits.TryGetValue(unitEntity.Id, out UnitDescriptionState? held))
			{
				held = new UnitDescriptionState(UnitCodecs.Length);

				if (state.UnseenUnitComponents.Remove(unitEntity.Id, out ulong toldBeforeLeavingSight)) held.ToldCodecMask = toldBeforeLeavingSight;

				state.SentUnits[unitEntity.Id] = held;
			}

			_describedUnitStates.Add(held);

			if (!isBaseline && !IsSameUnitAsSent(in described, in held.SentRecord)) held.ForgetToldPayloadBytes();

			if (!isBaseline && MatchesIn(in described, in held.SentRecord)) continue;

			held.SentRecord = described;
			snapshot.Units.Add(described);
		}

		if (isBaseline) AppendRuleEntries(snapshot.Catalog);
		AppendTextEntries(state, snapshot.Catalog);
		AppendLoot(state, ref snapshot, isBaseline);
		AppendContentPayloads(state, snapshot);
		if (Abilities != null)
		{
			lock (_abilityLock) snapshot.AbilityStates.AddRange(Abilities.CollectDeltas(peerId, _world, isBaseline));
		}

		AppendComponentDeltas(state, snapshot, sequence, isBaseline);
		ForgetUntrackedUnitComponents(state, ref snapshot, isBaseline);
	}

	private static bool IsSameUnitAsSent(in UnitSnapshot described, in UnitSnapshot sent)
	{
		if (!string.Equals(described.UnitId, sent.UnitId, StringComparison.Ordinal)) return false;
		if (described.OwnerPlayerIndex != sent.OwnerPlayerIndex) return false;
		return described.IsBuilding == sent.IsBuilding;
	}

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
		if (described.Position.X != sent.Position.X || described.Position.Y != sent.Position.Y || described.Position.Z != sent.Position.Z) return false;

		return string.Equals(described.UnitId, sent.UnitId, StringComparison.Ordinal);
	}

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

	private const string TextChannel = "string";

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

	private void CollectLootItem(Entity entity, ref GroundLoot loot, ref Position position) => _lootItems.Add((entity, loot, position.Value));

	private void AppendLoot(SnapshotPeerState state, ref WorldSnapshot snapshot, bool isBaseline)
	{
		_lootItems.Clear();
		_world.Query(in LootItemsQuery, _collectLootItems);

		uint signature = 2166136261u;
		int visibleCount = 0;
		for (int index = 0; index < _lootItems.Count; index++)
		{
			(Entity entity, GroundLoot loot, Vector3 position) = _lootItems[index];
			if (!IsLootVisible(position)) continue;

			visibleCount++;
			signature = FoldLootSignature(signature, entity.Id, in loot, position);
		}

		signature ^= (uint)visibleCount;
		if (!isBaseline && state.HasSentLoot && state.SentLootSignature == signature) return;

		state.SentLootSignature = signature;
		state.HasSentLoot = true;

		var entries = new List<LootSnapshot>(visibleCount);
		for (int index = 0; index < _lootItems.Count; index++)
		{
			(Entity entity, GroundLoot loot, Vector3 position) = _lootItems[index];
			if (!IsLootVisible(position)) continue;

			entries.Add(new LootSnapshot
			{
				EntityId = entity.Id,
				ItemTypeId = loot.ItemTypeId,
				Charges = loot.Charges,
				Lifetime = loot.Lifetime,
				RemainingLifetime = loot.RemainingLifetime,
				PickupRadius = loot.PickupRadius,
				AutoPickup = loot.AutoPickup,
				OwnerPlayerIndex = loot.OwnerPlayerIndex,
				Position = new NetworkVector3(position.X, position.Y, position.Z)
			});
		}

		snapshot.Loot = entries;
	}

	private bool IsLootVisible(Vector3 position)
	{
		if (_context.ShroudDisabled || _viewer == Entity.Null) return true;

		for (int index = 0; index < _visionSources.Count; index++)
		{
			(Vector3 sourcePosition, float radius) = _visionSources[index];
			if (Vector3.DistanceSquared(sourcePosition, position) <= radius * radius) return true;
		}

		return false;
	}

	private static uint FoldLootSignature(uint hash, int entityId, in GroundLoot loot, Vector3 position)
	{
		hash = FoldLootValue(hash, entityId);
		hash = FoldLootValue(hash, loot.StringId);
		hash = FoldLootValue(hash, loot.Charges);
		hash = FoldLootValue(hash, BitConverter.SingleToInt32Bits(loot.Lifetime));
		hash = FoldLootValue(hash, BitConverter.SingleToInt32Bits(MathF.Round(loot.RemainingLifetime, 1)));
		hash = FoldLootValue(hash, BitConverter.SingleToInt32Bits(loot.PickupRadius));
		hash = FoldLootValue(hash, loot.AutoPickupByte);
		hash = FoldLootValue(hash, loot.OwnerPlayerIndex);
		hash = FoldLootValue(hash, BitConverter.SingleToInt32Bits(position.X));
		hash = FoldLootValue(hash, BitConverter.SingleToInt32Bits(position.Y));
		return FoldLootValue(hash, BitConverter.SingleToInt32Bits(position.Z));
	}

	private static uint FoldLootValue(uint hash, int value) => (hash ^ (uint)value) * 16777619u;

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

		foreach (var cell in _unitCells) cell.Value.Clear();

		_candidates.Clear();
		_visionSources.Clear();
		_world.Query(in DescribedUnitsQuery, _collectCandidates);

		MarkUnitsWithinSight();
		AppendVisibleCandidates();
	}

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
			Position = new NetworkVector3(
				UnitQuantization.SnapPosition(position.X),
				UnitQuantization.SnapPosition(position.Y),
				UnitQuantization.SnapPosition(position.Z)),
			RotationY = UnitQuantization.SnapRotation(rotation),
			CurrentHp = UnitQuantization.SnapHealth(health.Current),
			MaxHp = UnitQuantization.SnapHealth(health.Max),
			IsDead = _world.Has<Dead>(entity),
			IsBuilding = _world.Has<Building>(entity)
		};
	}

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

	private void AppendUnitComponentDeltas(WorldSnapshot snapshot, Entity unitEntity, UnitDescriptionState held, int tick, bool isBaseline)
	{
		if (!_descriptions.TryGetUnitComponentState(tick, unitEntity.Id, out ulong presentCodecMask, out ulong presenceMask))
		{
			(presentCodecMask, presenceMask) = DescribeUnitComponentState(unitEntity);
			_descriptions.SetUnitComponentState(tick, unitEntity.Id, presentCodecMask, presenceMask);
		}

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

	private static void RemoveSentUnitComponent(WorldSnapshot snapshot, UnitDescriptionState held, int codecIndex, int entityId)
	{
		if ((held.ToldCodecMask & (1UL << codecIndex)) == 0) return;

		held.SentPayloads[codecIndex] = null;
		snapshot.ComponentDeltas.Add(new ComponentDelta
		{
			Key = entityId,
			ComponentTypeId = UnitCodecs[codecIndex].ComponentTypeId,
			Scope = (byte)ReplicatedScope.Unit,
			Bytes = Array.Empty<byte>()
		});
	}

	private ReadOnlySpan<byte> WriteComponentPayload(IReplicatedComponentCodec codec, Entity entity)
	{
		if (_writeScratch.Length < codec.SizeInBytes) _writeScratch = new byte[codec.SizeInBytes];

		Span<byte> scratch = _writeScratch.AsSpan(0, codec.SizeInBytes);
		return codec.TryWrite(_world, entity, scratch) ? scratch : ReadOnlySpan<byte>.Empty;
	}

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

	private void ForgetUntrackedUnitComponents(SnapshotPeerState state, ref WorldSnapshot snapshot, bool isBaseline)
	{
		_forgottenEntityIds.Clear();
		foreach (int rememberedEntityId in state.DescribedEntityIds)
		{
			if (!_describedEntityIds.Contains(rememberedEntityId)) _forgottenEntityIds.Add(rememberedEntityId);
		}

		if (!isBaseline) snapshot.RetiredEntityIds!.AddRange(_forgottenEntityIds);

		for (int index = 0; index < _forgottenEntityIds.Count; index++)
		{
			int forgottenEntityId = _forgottenEntityIds[index];
			if (state.SentUnits.TryGetValue(forgottenEntityId, out UnitDescriptionState? forgotten) && forgotten.ToldCodecMask != 0)
			{
				state.UnseenUnitComponents[forgottenEntityId] = forgotten.ToldCodecMask;
			}

			state.SentUnits.Remove(forgottenEntityId);
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
