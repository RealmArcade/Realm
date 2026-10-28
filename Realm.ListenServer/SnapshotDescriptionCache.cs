using System.Numerics;
using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     What one tick's description of the world is made of, held once for the whole room. A unit record is derived from
///     the world alone — where the unit is, how hurt it is, who owns it — so every peer that is told about that unit is
///     told the same thing, and a room of twenty-four peers describing the same field of units used to do the same work
///     twenty-four times over. The tick that first asks for a unit describes it; every peer after that reads what is
///     already there.
///     It also holds the one visibility the whole board shares. A map that shrouds nothing shows every peer every unit,
///     so the walk that decides what is visible is the same walk for the entire room and is made once.
///     The world does not change while a tick is being described, so two workers racing to describe the same unit write
///     the same values. A lock is still taken around the write, because a reader that saw a half-written record would
///     read a unit that never existed.
/// </summary>
public class SnapshotDescriptionCache
{
	private const int StripeCount = 64;

	private readonly object[] _stripes = new object[StripeCount];
	private readonly UnitSnapshot[] _unitRecords = new UnitSnapshot[EcsWorld.MAX_ENTITIES];
	private readonly int[] _unitRecordTicks = new int[EcsWorld.MAX_ENTITIES];
	private readonly ulong[] _presentCodecMasks = new ulong[EcsWorld.MAX_ENTITIES];
	private readonly ulong[] _presenceMasks = new ulong[EcsWorld.MAX_ENTITIES];
	private readonly int[] _componentStateTicks = new int[EcsWorld.MAX_ENTITIES];
	private readonly int _unitCodecCount;
	private readonly byte[]?[] _componentPayloads;
	private readonly int[] _componentPayloadTicks;

	private readonly object _wholeBoardLock = new();
	private int _wholeBoardTick = int.MinValue;

	/// <summary>The units a peer of a map that shrouds nothing is told about, described once for the whole room.</summary>
	public List<(Entity Entity, Vector3 Position)> WholeBoardUnits { get; } = new();

	/// <summary>The ids of the units in <see cref="WholeBoardUnits" />, so a peer's view of what left its sight is compared against them.</summary>
	public List<int> WholeBoardEntityIds { get; } = new();

	/// <summary>The tick <see cref="WholeBoardUnits" /> describes, so a peer of a later tick walks the board itself instead.</summary>
	public int WholeBoardTick => Volatile.Read(ref _wholeBoardTick);

	/// <summary>The lock a worker takes to decide whether this tick's whole-board visibility has to be walked or is already there.</summary>
	public object WholeBoardGate => _wholeBoardLock;

	/// <param name="unitCodecCount">How many components one unit can be described with, so a payload has somewhere to live.</param>
	public SnapshotDescriptionCache(int unitCodecCount)
	{
		_unitCodecCount = unitCodecCount;
		_componentPayloads = new byte[]?[EcsWorld.MAX_ENTITIES * unitCodecCount];
		_componentPayloadTicks = new int[EcsWorld.MAX_ENTITIES * unitCodecCount];

		for (int index = 0; index < _stripes.Length; index++) _stripes[index] = new object();
	}

	private object Stripe(int entityId) => _stripes[entityId & (StripeCount - 1)];

	/// <summary>Records the visibility the whole board shares, for the peers of the same tick that follow.</summary>
	public void PublishWholeBoard(int tick) => Volatile.Write(ref _wholeBoardTick, tick);

	/// <summary>
	///     What this tick says about one unit, if a peer describing this tick already asked. A unit that was described
	///     for another peer is described for this one by reading it.
	/// </summary>
	public bool TryGetUnitRecord(int tick, int entityId, out UnitSnapshot described)
	{
		lock (Stripe(entityId))
		{
			described = _unitRecords[entityId];
			return _unitRecordTicks[entityId] == tick;
		}
	}

	/// <summary>Remembers what this tick says about one unit, so the peers that follow it do not work it out again.</summary>
	public void SetUnitRecord(int tick, int entityId, in UnitSnapshot described)
	{
		lock (Stripe(entityId))
		{
			if (_unitRecordTicks[entityId] == tick) return;

			_unitRecords[entityId] = described;
			_unitRecordTicks[entityId] = tick;
		}
	}

	/// <summary>
	///     Which of the components a unit can be described with it actually carries, and which of them a baseline states
	///     presence for, if a peer describing this tick already worked that out. A unit carries a handful of the components
	///     the wire contract describes, so this is the difference between asking about forty-five of them for every peer
	///     and asking once for the room.
	/// </summary>
	public bool TryGetUnitComponentState(int tick, int entityId, out ulong presentCodecMask, out ulong presenceMask)
	{
		lock (Stripe(entityId))
		{
			presentCodecMask = _presentCodecMasks[entityId];
			presenceMask = _presenceMasks[entityId];
			return _componentStateTicks[entityId] == tick;
		}
	}

	/// <summary>Remembers the components one unit carries this tick, so the peers that follow do not look for them again.</summary>
	public void SetUnitComponentState(int tick, int entityId, ulong presentCodecMask, ulong presenceMask)
	{
		lock (Stripe(entityId))
		{
			if (_componentStateTicks[entityId] == tick) return;

			_presentCodecMasks[entityId] = presentCodecMask;
			_presenceMasks[entityId] = presenceMask;
			_componentStateTicks[entityId] = tick;
		}
	}

	/// <summary>
	///     The bytes one component of one unit is described with this tick, if a peer already wrote them. The bytes a
	///     component is written into are the same for every peer, so a room of twenty-four described the same component
	///     twenty-four times over before this.
	/// </summary>
	public bool TryGetComponentPayload(int tick, int entityId, int codecIndex, out ReadOnlySpan<byte> payload)
	{
		int slot = entityId * _unitCodecCount + codecIndex;
		lock (Stripe(entityId))
		{
			byte[]? stored = _componentPayloads[slot];
			if (stored != null && _componentPayloadTicks[slot] == tick)
			{
				payload = stored;
				return true;
			}

			payload = default;
			return false;
		}
	}

	/// <summary>Keeps the bytes one component of one unit was written into, for the peers that follow this tick.</summary>
	public void SetComponentPayload(int tick, int entityId, int codecIndex, ReadOnlySpan<byte> payload)
	{
		int slot = entityId * _unitCodecCount + codecIndex;
		lock (Stripe(entityId))
		{
			if (_componentPayloadTicks[slot] == tick) return;

			byte[] stored = _componentPayloads[slot] ??= new byte[payload.Length];
			payload.CopyTo(stored);
			_componentPayloadTicks[slot] = tick;
		}
	}
}
