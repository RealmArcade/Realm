using System.Numerics;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Holds a queue of pending construction tasks for a worker unit, to be executed sequentially after the current task completes.
/// </summary>
public struct BuildQueue
{
    private const int MaxCapacity = 8;

    private int _count;
    private string? _type0; private Vector3 _pos0; private Arch.Core.Entity _target0;
    private string? _type1; private Vector3 _pos1; private Arch.Core.Entity _target1;
    private string? _type2; private Vector3 _pos2; private Arch.Core.Entity _target2;
    private string? _type3; private Vector3 _pos3; private Arch.Core.Entity _target3;
    private string? _type4; private Vector3 _pos4; private Arch.Core.Entity _target4;
    private string? _type5; private Vector3 _pos5; private Arch.Core.Entity _target5;
    private string? _type6; private Vector3 _pos6; private Arch.Core.Entity _target6;
    private string? _type7; private Vector3 _pos7; private Arch.Core.Entity _target7;

    public readonly int Count => _count;

    public const int Capacity = MaxCapacity;

    public bool TryEnqueue(string type, Vector3 position, Arch.Core.Entity target = default)
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

    public void PeekAt(int index, out string? type, out Vector3 position, out Arch.Core.Entity target)
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

    public bool TryDequeue(out string? type, out Vector3 position, out Arch.Core.Entity target)
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
            GetSlot(slotIndex + 1, out string? nextType, out Vector3 nextPos, out Arch.Core.Entity nextTarget);
            SetSlot(slotIndex, nextType, nextPos, nextTarget);
        }
        SetSlot(_count - 1, null, default, default);
        _count--;
        return true;
    }

    private void SetSlot(int index, string? type, Vector3 position, Arch.Core.Entity target)
    {
        switch (index)
        {
            case 0: _type0 = type; _pos0 = position; _target0 = target; break;
            case 1: _type1 = type; _pos1 = position; _target1 = target; break;
            case 2: _type2 = type; _pos2 = position; _target2 = target; break;
            case 3: _type3 = type; _pos3 = position; _target3 = target; break;
            case 4: _type4 = type; _pos4 = position; _target4 = target; break;
            case 5: _type5 = type; _pos5 = position; _target5 = target; break;
            case 6: _type6 = type; _pos6 = position; _target6 = target; break;
            case 7: _type7 = type; _pos7 = position; _target7 = target; break;
        }
    }

    private void GetSlot(int index, out string? type, out Vector3 position, out Arch.Core.Entity target)
    {
        switch (index)
        {
            case 0: type = _type0; position = _pos0; target = _target0; break;
            case 1: type = _type1; position = _pos1; target = _target1; break;
            case 2: type = _type2; position = _pos2; target = _target2; break;
            case 3: type = _type3; position = _pos3; target = _target3; break;
            case 4: type = _type4; position = _pos4; target = _target4; break;
            case 5: type = _type5; position = _pos5; target = _target5; break;
            case 6: type = _type6; position = _pos6; target = _target6; break;
            case 7: type = _type7; position = _pos7; target = _target7; break;
            default: type = null; position = default; target = default; break;
        }
    }
}
