using System.Collections.Generic;
using Realm.Ecs.Components.Core;

namespace Realm.Ecs.Common;

public static class TimedStatusSlots
{
	public static bool TryGetValue(in BuffSlotArray slots, int count, string? statusId, out float remaining)
	{
		remaining = 0f;

		int statusStringId = StringRegistry.ComputeId(statusId);
		if (statusStringId == 0) return false;

		int index = IndexOf(in slots, count, statusStringId);
		if (index < 0) return false;

		remaining = slots[index].Remaining;
		return true;
	}

	public static bool TryGetDuration(in BuffSlotArray slots, int count, string? statusId, out float duration)
	{
		duration = 0f;

		int statusStringId = StringRegistry.ComputeId(statusId);
		if (statusStringId == 0) return false;

		int index = IndexOf(in slots, count, statusStringId);
		if (index < 0) return false;

		duration = slots[index].Duration;
		return true;
	}

	public static void Set(ref BuffSlotArray slots, ref int count, string? statusId, float remaining, float duration)
	{
		int statusStringId = StringRegistry.GetOrAdd(statusId);
		if (statusStringId == 0) return;

		int index = IndexOf(in slots, count, statusStringId);
		if (index < 0)
		{
			if (count >= BuffSlotArray.Capacity)
			{
				index = IndexOfSoonestExpiring(in slots, count);
			}
			else
			{
				index = count;
				count++;
			}
		}

		slots[index] = new BuffSlot
		{
			StatusStringId = statusStringId,
			Remaining = remaining,
			Duration = duration
		};
	}

	public static void Remove(ref BuffSlotArray slots, ref int count, string? statusId)
	{
		int statusStringId = StringRegistry.ComputeId(statusId);
		if (statusStringId == 0) return;

		int index = IndexOf(in slots, count, statusStringId);
		if (index < 0) return;

		count--;
		slots[index] = slots[count];
		slots[count] = default;
	}

	public static void Advance(ref BuffSlotArray slots, ref int count, float delta)
	{
		for (int index = count - 1; index >= 0; index--)
		{
			float remaining = slots[index].Remaining - delta;
			if (remaining > 0f)
			{
				slots[index].Remaining = remaining;
				continue;
			}

			count--;
			slots[index] = slots[count];
			slots[count] = default;
		}
	}

	public static void CopyKeys(in BuffSlotArray slots, int count, List<string> destination)
	{
		for (int index = 0; index < count; index++)
		{
			string? statusId = GetKey(in slots, index);
			if (statusId != null) destination.Add(statusId);
		}
	}

	public static string? GetKey(in BuffSlotArray slots, int index)
	{
		if (index < 0) return null;
		if (!StringRegistry.TryGetText(slots[index].StatusStringId, out string statusId)) return null;

		return statusId;
	}

	public static float GetRemaining(in BuffSlotArray slots, int index)
	{
		return index < 0 ? 0f : slots[index].Remaining;
	}

	public static float GetDuration(in BuffSlotArray slots, int index)
	{
		return index < 0 ? 0f : slots[index].Duration;
	}

	private static int IndexOf(in BuffSlotArray slots, int count, int statusStringId)
	{
		for (int index = 0; index < count; index++)
		{
			if (slots[index].StatusStringId == statusStringId) return index;
		}

		return -1;
	}

	private static int IndexOfSoonestExpiring(in BuffSlotArray slots, int count)
	{
		int soonestIndex = 0;
		float soonestRemaining = slots[0].Remaining;
		for (int index = 1; index < count; index++)
		{
			if (slots[index].Remaining >= soonestRemaining) continue;

			soonestIndex = index;
			soonestRemaining = slots[index].Remaining;
		}

		return soonestIndex;
	}
}
