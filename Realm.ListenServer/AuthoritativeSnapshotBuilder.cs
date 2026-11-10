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

public partial class AuthoritativeSnapshotBuilder
	{
	private const float MaximumDescribedCoordinate = 100000f;

	/// <summary>
	///     How many descriptions a component removal is stated in. One lost description is what leaves a peer holding a
	///     component the authority dropped, so a handful of statements make that cost a burst of consecutive loss rather
	///     than a single datagram that did not arrive.
	/// </summary>
	private const int RemovalRestateSequences = 6;
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

	/// <summary>
	///     The deltas a match without a scripted ability registry has. A peer is described every tick, and an empty list
	///     made for each of those descriptions was allocated only to be read for its count.
	/// </summary>
	private static readonly List<AbilityStateDelta> NoAbilityDeltas = new();

	public AuthoritativeSnapshotBuilder(EcsWorld world, SimulationContext context, Entity worldEntity, MapRulesCatalog rules, SnapshotPeerStateStore peerStates, SnapshotDescriptionCache descriptions)
	{
		_world = world;
		_context = context;
		_worldEntity = worldEntity;
		_rules = rules;
		_peerStates = peerStates;
		_descriptions = descriptions;
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

	/// <summary>
	///     Creates the component buffers a description reads while the world is still the owner's alone, so the first
	///     publish never opens the shared buffer store from inside a lane.
	/// </summary>
	public void WarmDescriptionQueries()
	{
		_world.GetBuffer<UnitOwnerPlayer>();
		_world.GetBuffer<Health>();
		_world.GetBuffer<Position>();
		_world.GetBuffer<GroundLoot>();
	}

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
		bool isBaseline = forceBaseline || _peerStates.Get(peerId).LastBaselineSequence < 0;
		List<AbilityStateDelta> preparedAbilityDeltas = Abilities?.CollectDeltas(peerId, _world, isBaseline) ?? NoAbilityDeltas;
		BuildInto(ref snapshot, peerId, sequence, viewer, forceBaseline, preparedAbilityDeltas);
		return snapshot;
	}

}
