using System.Numerics;
using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.ListenServer;

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

	public List<(Entity Entity, Vector3 Position)> WholeBoardUnits { get; } = new();

	public List<int> WholeBoardEntityIds { get; } = new();

	public int WholeBoardTick => Volatile.Read(ref _wholeBoardTick);

	public object WholeBoardGate => _wholeBoardLock;

	public SnapshotDescriptionCache(int unitCodecCount)
	{
		_unitCodecCount = unitCodecCount;
		_componentPayloads = new byte[]?[EcsWorld.MAX_ENTITIES * unitCodecCount];
		_componentPayloadTicks = new int[EcsWorld.MAX_ENTITIES * unitCodecCount];

		for (int index = 0; index < _stripes.Length; index++) _stripes[index] = new object();
	}

	private object Stripe(int entityId) => _stripes[entityId & (StripeCount - 1)];

	public void PublishWholeBoard(int tick) => Volatile.Write(ref _wholeBoardTick, tick);

	public bool TryGetUnitRecord(int tick, int entityId, out UnitSnapshot described)
	{
		lock (Stripe(entityId))
		{
			described = _unitRecords[entityId];
			return _unitRecordTicks[entityId] == tick;
		}
	}

	public void SetUnitRecord(int tick, int entityId, in UnitSnapshot described)
	{
		lock (Stripe(entityId))
		{
			if (_unitRecordTicks[entityId] == tick) return;

			_unitRecords[entityId] = described;
			_unitRecordTicks[entityId] = tick;
		}
	}

	public bool TryGetUnitComponentState(int tick, int entityId, out ulong presentCodecMask, out ulong presenceMask)
	{
		lock (Stripe(entityId))
		{
			presentCodecMask = _presentCodecMasks[entityId];
			presenceMask = _presenceMasks[entityId];
			return _componentStateTicks[entityId] == tick;
		}
	}

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
