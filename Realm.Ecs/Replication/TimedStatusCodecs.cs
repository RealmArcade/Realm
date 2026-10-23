using System;
using System.Runtime.InteropServices;
using Realm.Ecs.Components.Core;

namespace Realm.Ecs.Replication;

public static class TimedStatusPayload
{
	public const int MaxReplicatedSlots = 8;
	public const int SlotSizeInBytes = 12;
	public const int SizeInBytes = 1 + (MaxReplicatedSlots * SlotSizeInBytes);

	public static void Write(ReadOnlySpan<BuffSlot> slots, int count, Span<byte> destination)
	{
		destination.Clear();
		int written = 0;
		for (int index = 0; index < count && written < MaxReplicatedSlots; index++)
		{
			var slot = slots[index];
			if (slot.Remaining <= 0f || slot.StatusStringId == 0) continue;

			int offset = 1 + (written * SlotSizeInBytes);
			MemoryMarshal.Write(destination.Slice(offset, sizeof(int)), in slot.StatusStringId);
			MemoryMarshal.Write(destination.Slice(offset + 4, sizeof(float)), in slot.Remaining);
			MemoryMarshal.Write(destination.Slice(offset + 8, sizeof(float)), in slot.Duration);
			written++;
		}

		destination[0] = (byte)written;
	}

	public static void Apply(ReadOnlySpan<byte> source, ref BuffSlotArray slots, ref int count)
	{
		slots = default;
		count = 0;

		int written = Math.Min((int)source[0], MaxReplicatedSlots);
		for (int index = 0; index < written; index++)
		{
			int offset = 1 + (index * SlotSizeInBytes);
			slots[index] = new BuffSlot
			{
				StatusStringId = MemoryMarshal.Read<int>(source.Slice(offset, sizeof(int))),
				Remaining = MemoryMarshal.Read<float>(source.Slice(offset + 4, sizeof(float))),
				Duration = MemoryMarshal.Read<float>(source.Slice(offset + 8, sizeof(float)))
			};
		}

		count = written;
	}
}

public class CooldownsCodec : INormalizingComponentCodec
{
	public int ComponentTypeId => ComponentType<Cooldowns>.Id;
	public string ComponentName => nameof(Cooldowns);
	public ReplicatedScope Scope => ReplicatedScope.Unit;
	public int SizeInBytes => TimedStatusPayload.SizeInBytes;

	public bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<Cooldowns>(entity)) return false;

		ref var cooldowns = ref world.Get<Cooldowns>(entity);
		TimedStatusPayload.Write(cooldowns.Slots, cooldowns.Count, destination);
		return true;
	}

	public void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source)
	{
		if (world == null || !world.IsAlive(entity)) return;

		var cooldowns = world.Has<Cooldowns>(entity) ? world.Get<Cooldowns>(entity) : default;
		TimedStatusPayload.Apply(source, ref cooldowns.Slots, ref cooldowns.Count);
		world.SetOrAdd(entity, in cooldowns);
	}
}

public class SpellCooldownsCodec : INormalizingComponentCodec
{
	public int ComponentTypeId => ComponentType<SpellCooldowns>.Id;
	public string ComponentName => nameof(SpellCooldowns);
	public ReplicatedScope Scope => ReplicatedScope.Player;
	public int SizeInBytes => TimedStatusPayload.SizeInBytes;

	public bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<SpellCooldowns>(entity)) return false;

		ref var cooldowns = ref world.Get<SpellCooldowns>(entity);
		TimedStatusPayload.Write(cooldowns.Slots, cooldowns.Count, destination);
		return true;
	}

	public void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source)
	{
		if (world == null || !world.IsAlive(entity)) return;

		var cooldowns = world.Has<SpellCooldowns>(entity) ? world.Get<SpellCooldowns>(entity) : default;
		TimedStatusPayload.Apply(source, ref cooldowns.Slots, ref cooldowns.Count);
		world.SetOrAdd(entity, in cooldowns);
	}
}
