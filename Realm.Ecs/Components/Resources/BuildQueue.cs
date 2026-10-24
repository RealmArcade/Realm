using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using Realm.Ecs.Common;
using Realm.MapAPI;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Holds a queue of pending construction tasks for a worker unit, to be executed sequentially after the current task completes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct BuildQueue
{
	private int _count;
	private BuildQueueSlots _slots;

	public readonly int Count => _count;

	public const int Capacity = GameplayConstants.BuildQueueCapacity;

	public bool TryEnqueue(string type, Vector3 position, Entity target = default)
	{
		if (_count >= Capacity) return false;
		SetSlot(_count, type, position, target);
		_count++;
		return true;
	}

	public readonly void PeekAt(int index, out string? type, out Vector3 position)
	{
		if (index < 0 || index >= _count)
		{
			type = null;
			position = default;
			return;
		}

		GetSlot(index, out type, out position, out _);
	}

	public readonly void PeekAt(int index, out string? type, out Vector3 position, out Entity target)
	{
		if (index < 0 || index >= _count)
		{
			type = null;
			position = default;
			target = default;
			return;
		}

		GetSlot(index, out type, out position, out target);
	}

	public bool TryDequeue(out string? type, out Vector3 position)
	{
		return TryDequeue(out type, out position, out _);
	}

	public bool TryDequeue(out string? type, out Vector3 position, out Entity target)
	{
		if (_count == 0)
		{
			type = null;
			position = default;
			target = default;
			return false;
		}

		GetSlot(0, out type, out position, out target);
		for (int slotIndex = 0; slotIndex < _count - 1; slotIndex++)
		{
			_slots[slotIndex] = _slots[slotIndex + 1];
		}

		_slots[_count - 1] = default;
		_count--;
		return true;
	}

	private void SetSlot(int index, string? type, Vector3 position, Entity target)
	{
		_slots[index] = new BuildQueueSlot
		{
			TypeStringId = StringRegistry.GetOrAdd(type),
			Position = position,
			Target = target
		};
	}

	private readonly void GetSlot(int index, out string? type, out Vector3 position, out Entity target)
	{
		BuildQueueSlot slot = _slots[index];
		type = slot.TypeStringId != 0 ? StringRegistry.Get(slot.TypeStringId) : null;
		position = slot.Position;
		target = slot.Target;
	}
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct BuildQueueSlot
{
	public int TypeStringId;
	public Vector3 Position;
	public Entity Target;
}

[InlineArray(GameplayConstants.BuildQueueCapacity)]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct BuildQueueSlots
{
	public BuildQueueSlot Element0;
}
