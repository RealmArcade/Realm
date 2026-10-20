using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Holds a queue of pending construction tasks for a worker unit, to be executed sequentially after the current task completes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct BuildQueue
{
	private const int MaxCapacity = 8;

	private int _count;
	private int _type0StringId;
	private Vector3 _pos0;
	private Entity _target0;
	private int _type1StringId;
	private Vector3 _pos1;
	private Entity _target1;
	private int _type2StringId;
	private Vector3 _pos2;
	private Entity _target2;
	private int _type3StringId;
	private Vector3 _pos3;
	private Entity _target3;
	private int _type4StringId;
	private Vector3 _pos4;
	private Entity _target4;
	private int _type5StringId;
	private Vector3 _pos5;
	private Entity _target5;
	private int _type6StringId;
	private Vector3 _pos6;
	private Entity _target6;
	private int _type7StringId;
	private Vector3 _pos7;
	private Entity _target7;

	public readonly int Count => _count;

	public const int Capacity = MaxCapacity;

	public bool TryEnqueue(string type, Vector3 position, Entity target = default)
	{
		if (_count >= MaxCapacity) return false;
		SetSlot(_count, type, position, target);
		_count++;
		return true;
	}

	public void PeekAt(int index, out string? type, out Vector3 position)
	{
		if (index < 0 || index >= _count)
		{
			type = null;
			position = default;
			return;
		}

		GetSlot(index, out type, out position, out _);
	}

	public void PeekAt(int index, out string? type, out Vector3 position, out Entity target)
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
			GetSlot(slotIndex + 1, out string? nextType, out Vector3 nextPos, out Entity nextTarget);
			SetSlot(slotIndex, nextType, nextPos, nextTarget);
		}

		SetSlot(_count - 1, null, default, default);
		_count--;
		return true;
	}

	private void SetSlot(int index, string? type, Vector3 position, Entity target)
	{
		int stringId = StringRegistry.GetOrAdd(type);
		switch (index)
		{
			case 0:
				_type0StringId = stringId;
				_pos0 = position;
				_target0 = target;
				break;
			case 1:
				_type1StringId = stringId;
				_pos1 = position;
				_target1 = target;
				break;
			case 2:
				_type2StringId = stringId;
				_pos2 = position;
				_target2 = target;
				break;
			case 3:
				_type3StringId = stringId;
				_pos3 = position;
				_target3 = target;
				break;
			case 4:
				_type4StringId = stringId;
				_pos4 = position;
				_target4 = target;
				break;
			case 5:
				_type5StringId = stringId;
				_pos5 = position;
				_target5 = target;
				break;
			case 6:
				_type6StringId = stringId;
				_pos6 = position;
				_target6 = target;
				break;
			case 7:
				_type7StringId = stringId;
				_pos7 = position;
				_target7 = target;
				break;
		}
	}

	private void GetSlot(int index, out string? type, out Vector3 position, out Entity target)
	{
		switch (index)
		{
			case 0:
				type = _type0StringId != 0 ? StringRegistry.Get(_type0StringId) : null;
				position = _pos0;
				target = _target0;
				break;
			case 1:
				type = _type1StringId != 0 ? StringRegistry.Get(_type1StringId) : null;
				position = _pos1;
				target = _target1;
				break;
			case 2:
				type = _type2StringId != 0 ? StringRegistry.Get(_type2StringId) : null;
				position = _pos2;
				target = _target2;
				break;
			case 3:
				type = _type3StringId != 0 ? StringRegistry.Get(_type3StringId) : null;
				position = _pos3;
				target = _target3;
				break;
			case 4:
				type = _type4StringId != 0 ? StringRegistry.Get(_type4StringId) : null;
				position = _pos4;
				target = _target4;
				break;
			case 5:
				type = _type5StringId != 0 ? StringRegistry.Get(_type5StringId) : null;
				position = _pos5;
				target = _target5;
				break;
			case 6:
				type = _type6StringId != 0 ? StringRegistry.Get(_type6StringId) : null;
				position = _pos6;
				target = _target6;
				break;
			case 7:
				type = _type7StringId != 0 ? StringRegistry.Get(_type7StringId) : null;
				position = _pos7;
				target = _target7;
				break;
			default:
				type = null;
				position = default;
				target = default;
				break;
		}
	}
}
