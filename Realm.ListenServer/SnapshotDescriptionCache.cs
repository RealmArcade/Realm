using System.Numerics;
using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     What this tick has already described, so a peer's description reuses another peer's work instead of recomputing
///     it. Every entry is written into the generation that is not published and then published by moving the slot's
///     generation index, so a value that a reader has been handed is never rewritten while it is being consumed: the
///     reader takes one volatile read and no lock, and a payload span it holds stays the bytes it was given for the rest
///     of the tick. Writers still serialize per stripe, because two peers describing the same unit must not both be the
///     first to describe it.
/// </summary>
public class SnapshotDescriptionCache
{
	private const int StripeCount = 64;
	private const int GenerationCount = 2;

	private readonly object[] _stripes = new object[StripeCount];

	private readonly int[] _unitRecordGeneration = new int[EcsWorld.MAX_ENTITIES];
	private readonly UnitSnapshot[][] _unitRecords = new UnitSnapshot[GenerationCount][];
	private readonly int[][] _unitRecordTicks = new int[GenerationCount][];

	private readonly int[] _componentStateGeneration = new int[EcsWorld.MAX_ENTITIES];
	private readonly ulong[][] _presentCodecMasks = new ulong[GenerationCount][];
	private readonly ulong[][] _presenceMasks = new ulong[GenerationCount][];
	private readonly int[][] _componentStateTicks = new int[GenerationCount][];

	private readonly int[] _componentPayloadGeneration;
	private readonly byte[]?[][] _componentPayloads = new byte[]?[GenerationCount][];
	private readonly int[][] _componentPayloadLengths = new int[GenerationCount][];
	private readonly int[][] _componentPayloadTicks = new int[GenerationCount][];

	private readonly int _unitCodecCount;

	private readonly object _wholeBoardLock = new();
	private int _wholeBoardTick = int.MinValue;

	public List<(Entity Entity, Vector3 Position)> WholeBoardUnits { get; } = new();

	public List<int> WholeBoardEntityIds { get; } = new();

	public int WholeBoardTick => Volatile.Read(ref _wholeBoardTick);

	public object WholeBoardGate => _wholeBoardLock;

	public SnapshotDescriptionCache(int unitCodecCount)
	{
		_unitCodecCount = unitCodecCount;
		int payloadSlots = EcsWorld.MAX_ENTITIES * unitCodecCount;
		_componentPayloadGeneration = new int[payloadSlots];

		for (int index = 0; index < _stripes.Length; index++) _stripes[index] = new object();

		for (int generation = 0; generation < GenerationCount; generation++)
		{
			_unitRecords[generation] = new UnitSnapshot[EcsWorld.MAX_ENTITIES];
			_unitRecordTicks[generation] = new int[EcsWorld.MAX_ENTITIES];
			_presentCodecMasks[generation] = new ulong[EcsWorld.MAX_ENTITIES];
			_presenceMasks[generation] = new ulong[EcsWorld.MAX_ENTITIES];
			_componentStateTicks[generation] = new int[EcsWorld.MAX_ENTITIES];
			_componentPayloads[generation] = new byte[]?[payloadSlots];
			_componentPayloadLengths[generation] = new int[payloadSlots];
			_componentPayloadTicks[generation] = new int[payloadSlots];

			for (int entityId = 0; entityId < EcsWorld.MAX_ENTITIES; entityId++)
			{
				_unitRecordTicks[generation][entityId] = int.MinValue;
				_componentStateTicks[generation][entityId] = int.MinValue;
			}

			for (int slot = 0; slot < payloadSlots; slot++) _componentPayloadTicks[generation][slot] = int.MinValue;
		}
	}

	private object Stripe(int entityId) => _stripes[entityId & (StripeCount - 1)];

	public void PublishWholeBoard(int tick) => Volatile.Write(ref _wholeBoardTick, tick);

	public bool TryGetUnitRecord(int tick, int entityId, out UnitSnapshot described)
	{
		int generation = Volatile.Read(ref _unitRecordGeneration[entityId]);
		if (_unitRecordTicks[generation][entityId] == tick)
		{
			described = _unitRecords[generation][entityId];
			return true;
		}

		described = default;
		return false;
	}

	public void SetUnitRecord(int tick, int entityId, in UnitSnapshot described)
	{
		lock (Stripe(entityId))
		{
			int current = _unitRecordGeneration[entityId];
			if (_unitRecordTicks[current][entityId] == tick) return;

			int next = current ^ 1;
			_unitRecords[next][entityId] = described;
			_unitRecordTicks[next][entityId] = tick;
			Volatile.Write(ref _unitRecordGeneration[entityId], next);
		}
	}

	public bool TryGetUnitComponentState(int tick, int entityId, out ulong presentCodecMask, out ulong presenceMask)
	{
		int generation = Volatile.Read(ref _componentStateGeneration[entityId]);
		if (_componentStateTicks[generation][entityId] == tick)
		{
			presentCodecMask = _presentCodecMasks[generation][entityId];
			presenceMask = _presenceMasks[generation][entityId];
			return true;
		}

		presentCodecMask = 0;
		presenceMask = 0;
		return false;
	}

	public void SetUnitComponentState(int tick, int entityId, ulong presentCodecMask, ulong presenceMask)
	{
		lock (Stripe(entityId))
		{
			int current = _componentStateGeneration[entityId];
			if (_componentStateTicks[current][entityId] == tick) return;

			int next = current ^ 1;
			_presentCodecMasks[next][entityId] = presentCodecMask;
			_presenceMasks[next][entityId] = presenceMask;
			_componentStateTicks[next][entityId] = tick;
			Volatile.Write(ref _componentStateGeneration[entityId], next);
		}
	}

	public bool TryGetComponentPayload(int tick, int entityId, int codecIndex, out ReadOnlySpan<byte> payload)
	{
		int slot = entityId * _unitCodecCount + codecIndex;
		int generation = Volatile.Read(ref _componentPayloadGeneration[slot]);
		if (_componentPayloadTicks[generation][slot] == tick)
		{
			payload = _componentPayloads[generation][slot].AsSpan(0, _componentPayloadLengths[generation][slot]);
			return true;
		}

		payload = default;
		return false;
	}

	public void SetComponentPayload(int tick, int entityId, int codecIndex, ReadOnlySpan<byte> payload)
	{
		int slot = entityId * _unitCodecCount + codecIndex;
		lock (Stripe(entityId))
		{
			int current = _componentPayloadGeneration[slot];
			if (_componentPayloadTicks[current][slot] == tick) return;

			int next = current ^ 1;
			byte[] stored = _componentPayloads[next][slot] ?? Array.Empty<byte>();
			if (stored.Length < payload.Length)
			{
				// Grown rather than replaced, so a component that changes size between ticks stops allocating once it
				// has reached its largest payload.
				stored = new byte[Math.Max(payload.Length, stored.Length * 2)];
				_componentPayloads[next][slot] = stored;
			}

			payload.CopyTo(stored);
			_componentPayloadLengths[next][slot] = payload.Length;
			_componentPayloadTicks[next][slot] = tick;
			Volatile.Write(ref _componentPayloadGeneration[slot], next);
		}
	}
}
