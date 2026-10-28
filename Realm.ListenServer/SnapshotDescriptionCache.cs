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

	public SnapshotDescriptionCache()
	{
		for (int index = 0; index < _stripes.Length; index++) _stripes[index] = new object();
	}

	/// <summary>Records the visibility the whole board shares, for the peers of the same tick that follow.</summary>
	public void PublishWholeBoard(int tick) => Volatile.Write(ref _wholeBoardTick, tick);

	/// <summary>
	///     What this tick says about one unit, if a peer describing this tick already asked. A unit that was described
	///     for another peer is described for this one by reading it.
	/// </summary>
	public bool TryGetUnitRecord(int tick, int entityId, out UnitSnapshot described)
	{
		lock (_stripes[entityId & (StripeCount - 1)])
		{
			described = _unitRecords[entityId];
			return _unitRecordTicks[entityId] == tick;
		}
	}

	/// <summary>Remembers what this tick says about one unit, so the peers that follow it do not work it out again.</summary>
	public void SetUnitRecord(int tick, int entityId, in UnitSnapshot described)
	{
		lock (_stripes[entityId & (StripeCount - 1)])
		{
			if (_unitRecordTicks[entityId] == tick) return;

			_unitRecords[entityId] = described;
			_unitRecordTicks[entityId] = tick;
		}
	}
}
