using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the visual effect spawn requests waiting to be shown, in the order they were queued.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct VFXQueue
{
	public int Count;
	public VFXSlotArray Slots;

	public VFXQueue(IReadOnlyList<VFXRequest>? requests)
	{
		this = default;
		if (requests == null) return;

		int copyCount = requests.Count < VFXSlotArray.Capacity ? requests.Count : VFXSlotArray.Capacity;
		for (int index = 0; index < copyCount; index++)
		{
			Slots[index] = requests[index];
		}

		Count = copyCount;
	}

	public void Enqueue(in VFXRequest request)
	{
		if (Count >= VFXSlotArray.Capacity)
		{
			for (int move = 0; move < VFXSlotArray.Capacity - 1; move++)
			{
				Slots[move] = Slots[move + 1];
			}

			Count = VFXSlotArray.Capacity - 1;
		}

		Slots[Count] = request;
		Count++;
	}

	public VFXRequest Get(int index)
	{
		return index < 0 || index >= Count ? default : Slots[index];
	}

	public void Clear()
	{
		Count = 0;
		Slots = default;
	}
}
