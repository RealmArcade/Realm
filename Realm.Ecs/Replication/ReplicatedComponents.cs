using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MemoryPack;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;

namespace Realm.Ecs.Replication;

public enum ReplicatedScope : byte
{
	World = 0,
	Player = 1,
	Unit = 2
}

[MemoryPackable]
public partial struct ComponentDelta
{
	public int Key;
	public int ComponentTypeId;
	public byte Scope;
	public byte[] Bytes;
}

public interface IReplicatedComponentCodec
{
	int ComponentTypeId { get; }
	string ComponentName { get; }
	ReplicatedScope Scope { get; }
	int SizeInBytes { get; }
	bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination);
	void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source);
	void Remove(EcsWorld world, Entity entity);
}

public interface INormalizingComponentCodec : IReplicatedComponentCodec
{
}

public class ReplicatedComponentCodec<T> : IReplicatedComponentCodec where T : unmanaged
{
	private readonly ReplicatedScope _scope;

	public ReplicatedComponentCodec(ReplicatedScope scope)
	{
		_scope = scope;
	}

	public int ComponentTypeId => ComponentType<T>.Id;
	public string ComponentName => typeof(T).Name;
	public ReplicatedScope Scope => _scope;
	public int SizeInBytes => Unsafe.SizeOf<T>();

	public bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<T>(entity)) return false;
		ref var component = ref world.Get<T>(entity);
		MemoryMarshal.Write(destination, in component);
		return true;
	}

	public void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source)
	{
		if (world == null || !world.IsAlive(entity)) return;
		var value = MemoryMarshal.Read<T>(source);
		world.SetOrAdd(entity, in value);
	}

	public void Remove(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<T>(entity)) return;
		world.Remove<T>(entity);
	}
}

public class TagComponentCodec<T> : IReplicatedComponentCodec where T : unmanaged
{
	private readonly ReplicatedScope _scope;

	public TagComponentCodec(ReplicatedScope scope)
	{
		_scope = scope;
	}

	public int ComponentTypeId => ComponentType<T>.Id;
	public string ComponentName => typeof(T).Name;
	public ReplicatedScope Scope => _scope;
	public int SizeInBytes => 1;

	public bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<T>(entity)) return false;
		destination[0] = 1;
		return true;
	}

	public void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source)
	{
		if (world == null || !world.IsAlive(entity)) return;
		if (source[0] != 0)
		{
			if (!world.Has<T>(entity)) world.Add<T>(entity, default);
		}
		else if (world.Has<T>(entity))
		{
			world.Remove<T>(entity);
		}
	}

	public void Remove(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<T>(entity)) return;
		world.Remove<T>(entity);
	}
}

public class WorldStateCodec : IReplicatedComponentCodec
{
	public int ComponentTypeId => ComponentType<WorldState>.Id;
	public string ComponentName => nameof(WorldState);
	public ReplicatedScope Scope => ReplicatedScope.World;
	public int SizeInBytes => 13;

	public bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<WorldState>(entity)) return false;
		ref var state = ref world.Get<WorldState>(entity);
		BitConverter.TryWriteBytes(destination.Slice(0, 4), state.GameElapsedTime);
		MemoryMarshal.Write(destination.Slice(4, 4), in state.TimeOfDayIndex);
		BitConverter.TryWriteBytes(destination.Slice(8, 4), state.TimeOfDayTimer);
		destination[12] = state.DayNightCycleEnabledByte;
		return true;
	}

	public void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source)
	{
		if (world == null || !world.IsAlive(entity)) return;
		float gameElapsedTime = BitConverter.ToSingle(source.Slice(0, 4));
		int timeOfDayIndex = MemoryMarshal.Read<int>(source.Slice(4, 4));
		float timeOfDayTimer = BitConverter.ToSingle(source.Slice(8, 4));
		byte dayNightCycleEnabled = source[12];

		if (world.Has<WorldState>(entity))
		{
			ref var state = ref world.Get<WorldState>(entity);
			state.GameElapsedTime = gameElapsedTime;
			state.TimeOfDayIndex = timeOfDayIndex;
			state.TimeOfDayTimer = timeOfDayTimer;
			state.DayNightCycleEnabledByte = dayNightCycleEnabled;
			return;
		}

		var created = new WorldState(gameElapsedTime, timeOfDayIndex, timeOfDayTimer, dayNightCycleEnabled != 0);
		created.DayNightCycleEnabledByte = dayNightCycleEnabled;
		world.Add(entity, created);
	}

	public void Remove(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<WorldState>(entity)) return;
		world.Remove<WorldState>(entity);
	}
}

public class PlayerTeamCodec : IReplicatedComponentCodec
{
	public int ComponentTypeId => ComponentType<PlayerTeam>.Id;
	public string ComponentName => nameof(PlayerTeam);
	public ReplicatedScope Scope => ReplicatedScope.Player;
	public int SizeInBytes => sizeof(int) * 2;

	public bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<PlayerTeam>(entity)) return false;
		ref var team = ref world.Get<PlayerTeam>(entity);
		MemoryMarshal.Write(destination.Slice(0, sizeof(int)), in team.TeamIndex);
		MemoryMarshal.Write(destination.Slice(sizeof(int), sizeof(int)), in team.TeamNameStringId);
		return true;
	}

	public void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source)
	{
		if (world == null || !world.IsAlive(entity)) return;
		int teamIndex = MemoryMarshal.Read<int>(source.Slice(0, sizeof(int)));
		int teamNameStringId = MemoryMarshal.Read<int>(source.Slice(sizeof(int), sizeof(int)));
		if (world.Has<PlayerTeam>(entity))
		{
			ref var team = ref world.Get<PlayerTeam>(entity);
			team.TeamIndex = teamIndex;
			team.TeamNameStringId = teamNameStringId;
		}
		else
		{
			world.Add(entity, new PlayerTeam(teamIndex, teamNameStringId));
		}
	}

	public void Remove(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<PlayerTeam>(entity)) return;
		world.Remove<PlayerTeam>(entity);
	}
}

public class AttackNumbersCodec : IReplicatedComponentCodec
{
	private const int FloatCount = 14;
	private const int PayloadSize = FloatCount * 4 + 1;

	public int ComponentTypeId => ComponentType<Attack>.Id;
	public string ComponentName => nameof(Attack);
	public ReplicatedScope Scope => ReplicatedScope.Unit;
	public int SizeInBytes => PayloadSize;

	public bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<Attack>(entity)) return false;
		ref var attack = ref world.Get<Attack>(entity);
		WriteFloat(destination, 0, attack.Damage);
		WriteFloat(destination, 4, attack.Range);
		WriteFloat(destination, 8, attack.Cooldown);
		WriteFloat(destination, 12, attack.DamageVariance);
		WriteFloat(destination, 16, attack.FlatArmorPenetration);
		WriteFloat(destination, 20, attack.PercentArmorPenetration);
		WriteFloat(destination, 24, attack.CritChance);
		WriteFloat(destination, 28, attack.CritMultiplier);
		WriteFloat(destination, 32, attack.SplashInnerRadius);
		WriteFloat(destination, 36, attack.SplashMediumRadius);
		WriteFloat(destination, 40, attack.SplashOuterRadius);
		WriteFloat(destination, 44, attack.SplashInnerRatio);
		WriteFloat(destination, 48, attack.SplashMediumRatio);
		WriteFloat(destination, 52, attack.SplashOuterRatio);
		destination[56] = attack.FriendlyFireByte;
		return true;
	}

	public void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<Attack>(entity)) return;
		ref var attack = ref world.Get<Attack>(entity);
		attack.Damage = ReadFloat(source, 0);
		attack.Range = ReadFloat(source, 4);
		attack.Cooldown = ReadFloat(source, 8);
		attack.DamageVariance = ReadFloat(source, 12);
		attack.FlatArmorPenetration = ReadFloat(source, 16);
		attack.PercentArmorPenetration = ReadFloat(source, 20);
		attack.CritChance = ReadFloat(source, 24);
		attack.CritMultiplier = ReadFloat(source, 28);
		attack.SplashInnerRadius = ReadFloat(source, 32);
		attack.SplashMediumRadius = ReadFloat(source, 36);
		attack.SplashOuterRadius = ReadFloat(source, 40);
		attack.SplashInnerRatio = ReadFloat(source, 44);
		attack.SplashMediumRatio = ReadFloat(source, 48);
		attack.SplashOuterRatio = ReadFloat(source, 52);
		attack.FriendlyFireByte = source[56];
	}

	public void Remove(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<Attack>(entity)) return;
		world.Remove<Attack>(entity);
	}

	private static void WriteFloat(Span<byte> destination, int offset, float value)
	{
		BitConverter.TryWriteBytes(destination.Slice(offset, 4), value);
	}

	private static float ReadFloat(ReadOnlySpan<byte> source, int offset)
	{
		return BitConverter.ToSingle(source.Slice(offset, 4));
	}
}

public class ArmorNumbersCodec : IReplicatedComponentCodec
{
	public int ComponentTypeId => ComponentType<Armor>.Id;
	public string ComponentName => nameof(Armor);
	public ReplicatedScope Scope => ReplicatedScope.Unit;
	public int SizeInBytes => 8;

	public bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<Armor>(entity)) return false;
		ref var armor = ref world.Get<Armor>(entity);
		BitConverter.TryWriteBytes(destination.Slice(0, 4), armor.FlatArmor);
		BitConverter.TryWriteBytes(destination.Slice(4, 4), armor.RatedArmor);
		return true;
	}

	public void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<Armor>(entity)) return;
		ref var armor = ref world.Get<Armor>(entity);
		armor.FlatArmor = BitConverter.ToSingle(source.Slice(0, 4));
		armor.RatedArmor = BitConverter.ToSingle(source.Slice(4, 4));
	}

	public void Remove(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<Armor>(entity)) return;
		world.Remove<Armor>(entity);
	}
}

public class BuildTaskCodec : INormalizingComponentCodec
{
	public int ComponentTypeId => ComponentType<BuildTask>.Id;
	public string ComponentName => nameof(BuildTask);
	public ReplicatedScope Scope => ReplicatedScope.Unit;
	public int SizeInBytes => 12;

	public bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<BuildTask>(entity)) return false;
		ref var task = ref world.Get<BuildTask>(entity);
		int buildingEntityId = task.BuildingEntity.Id;
		MemoryMarshal.Write(destination.Slice(0, sizeof(int)), in buildingEntityId);
		BitConverter.TryWriteBytes(destination.Slice(4, 4), task.TotalBuildTime);
		BitConverter.TryWriteBytes(destination.Slice(8, 4), task.Progress);
		return true;
	}

	public void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source)
	{
		if (world == null || !world.IsAlive(entity)) return;
		int buildingEntityId = MemoryMarshal.Read<int>(source.Slice(0, sizeof(int)));
		float totalBuildTime = BitConverter.ToSingle(source.Slice(4, 4));
		float progress = BitConverter.ToSingle(source.Slice(8, 4));
		var task = new BuildTask(ReplicatedEntityReferences.Resolve(new Entity(buildingEntityId)), totalBuildTime)
		{
			Progress = progress
		};
		world.SetOrAdd(entity, in task);
	}

	public void Remove(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<BuildTask>(entity)) return;
		world.Remove<BuildTask>(entity);
	}
}

public class FollowCodec : INormalizingComponentCodec
{
	public int ComponentTypeId => ComponentType<Follow>.Id;
	public string ComponentName => nameof(Follow);
	public ReplicatedScope Scope => ReplicatedScope.Unit;
	public int SizeInBytes => sizeof(int);

	public bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<Follow>(entity)) return false;
		ref var follow = ref world.Get<Follow>(entity);
		int targetEntityId = follow.Target.Id;
		MemoryMarshal.Write(destination.Slice(0, sizeof(int)), in targetEntityId);
		return true;
	}

	public void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source)
	{
		if (world == null || !world.IsAlive(entity)) return;
		int targetEntityId = MemoryMarshal.Read<int>(source.Slice(0, sizeof(int)));
		var follow = new Follow(ReplicatedEntityReferences.Resolve(new Entity(targetEntityId)));
		world.SetOrAdd(entity, in follow);
	}

	public void Remove(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<Follow>(entity)) return;
		world.Remove<Follow>(entity);
	}
}

public class AttackTargetCodec : INormalizingComponentCodec
{
	public int ComponentTypeId => ComponentType<AttackTarget>.Id;
	public string ComponentName => nameof(AttackTarget);
	public ReplicatedScope Scope => ReplicatedScope.Unit;
	public int SizeInBytes => sizeof(int);

	public bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<AttackTarget>(entity)) return false;
		ref var target = ref world.Get<AttackTarget>(entity);
		int targetEntityId = target.Target.Id;
		MemoryMarshal.Write(destination.Slice(0, sizeof(int)), in targetEntityId);
		return true;
	}

	public void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source)
	{
		if (world == null || !world.IsAlive(entity)) return;
		int targetEntityId = MemoryMarshal.Read<int>(source.Slice(0, sizeof(int)));
		var target = new AttackTarget(ReplicatedEntityReferences.Resolve(new Entity(targetEntityId)));
		world.SetOrAdd(entity, in target);
	}

	public void Remove(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<AttackTarget>(entity)) return;
		world.Remove<AttackTarget>(entity);
	}
}

public class LastAttackerCodec : INormalizingComponentCodec
{
	public int ComponentTypeId => ComponentType<LastAttacker>.Id;
	public string ComponentName => nameof(LastAttacker);
	public ReplicatedScope Scope => ReplicatedScope.Unit;
	public int SizeInBytes => sizeof(int);

	public bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<LastAttacker>(entity)) return false;
		ref var attacker = ref world.Get<LastAttacker>(entity);
		int attackerEntityId = attacker.Value.Id;
		MemoryMarshal.Write(destination.Slice(0, sizeof(int)), in attackerEntityId);
		return true;
	}

	public void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source)
	{
		if (world == null || !world.IsAlive(entity)) return;
		int attackerEntityId = MemoryMarshal.Read<int>(source.Slice(0, sizeof(int)));
		var attacker = new LastAttacker(ReplicatedEntityReferences.Resolve(new Entity(attackerEntityId)));
		world.SetOrAdd(entity, in attacker);
	}

	public void Remove(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<LastAttacker>(entity)) return;
		world.Remove<LastAttacker>(entity);
	}
}

public class PatrolCodec : IReplicatedComponentCodec
{
	public int ComponentTypeId => ComponentType<Patrol>.Id;
	public string ComponentName => nameof(Patrol);
	public ReplicatedScope Scope => ReplicatedScope.Unit;
	public int SizeInBytes => 25;

	public bool TryWrite(EcsWorld world, Entity entity, Span<byte> destination)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<Patrol>(entity)) return false;
		ref var patrol = ref world.Get<Patrol>(entity);
		MemoryMarshal.Write(destination.Slice(0, 12), in patrol.PointA);
		MemoryMarshal.Write(destination.Slice(12, 12), in patrol.PointB);
		destination[24] = patrol.GoingToBByte;
		return true;
	}

	public void Apply(EcsWorld world, Entity entity, ReadOnlySpan<byte> source)
	{
		if (world == null || !world.IsAlive(entity)) return;
		var patrol = new Patrol
		{
			PointA = MemoryMarshal.Read<System.Numerics.Vector3>(source.Slice(0, 12)),
			PointB = MemoryMarshal.Read<System.Numerics.Vector3>(source.Slice(12, 12)),
			GoingToBByte = source[24]
		};
		world.SetOrAdd(entity, in patrol);
	}

	public void Remove(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<Patrol>(entity)) return;
		world.Remove<Patrol>(entity);
	}
}

public static class ReplicatedComponents
{
	private static readonly List<IReplicatedComponentCodec> _codecs = new();

	static ReplicatedComponents()
	{
		Register(new WorldStateCodec());
		Register(new ReplicatedComponentCodec<PlayerPopulation>(ReplicatedScope.Player));
		Register(new ReplicatedComponentCodec<PlayerResources>(ReplicatedScope.Player));
		Register(new ReplicatedComponentCodec<PlayerUpgrades>(ReplicatedScope.Player));
		Register(new SpellCooldownsCodec());
		Register(new CooldownsCodec());
		Register(new ReplicatedComponentCodec<ConstructionState>(ReplicatedScope.Unit));
		Register(new BuildTaskCodec());
		Register(new ReplicatedComponentCodec<BuildQueue>(ReplicatedScope.Unit));
		Register(new ReplicatedComponentCodec<MoveTo>(ReplicatedScope.Unit));
		Register(new ReplicatedComponentCodec<AttackMove>(ReplicatedScope.Unit));
		Register(new FollowCodec());
		Register(new TagComponentCodec<HoldPosition>(ReplicatedScope.Unit));
		Register(new PatrolCodec());
		Register(new ReplicatedComponentCodec<WaypointQueue>(ReplicatedScope.Unit));
		Register(new AttackTargetCodec());
		Register(new LastAttackerCodec());
		Register(new ReplicatedComponentCodec<CombatTargeting>(ReplicatedScope.Unit));
		Register(new ReplicatedComponentCodec<AutoCombatState>(ReplicatedScope.Unit));
		Register(new ReplicatedComponentCodec<CombatAlertState>(ReplicatedScope.Unit));
		Register(new ReplicatedComponentCodec<Inventory>(ReplicatedScope.Unit));
		Register(new ReplicatedComponentCodec<RallyPoint>(ReplicatedScope.Unit));
		Register(new ReplicatedComponentCodec<ResourceNode>(ReplicatedScope.Unit));
		Register(new BuffsCodec());
		Register(new BuffStateCodec());
		Register(new ModifierStateCodec());
		Register(new ReplicatedComponentCodec<AbilityState>(ReplicatedScope.Unit));
		Register(new ReplicatedComponentCodec<ScriptZonesState>(ReplicatedScope.World));
		Register(new ReplicatedComponentCodec<ScriptPlayersState>(ReplicatedScope.World));
		Register(new ReplicatedComponentCodec<ProductionQueue>(ReplicatedScope.Unit));
		Register(new PlayerTeamCodec());
		Register(new ReplicatedComponentCodec<Mana>(ReplicatedScope.Unit));
		Register(new ReplicatedComponentCodec<MovementStats>(ReplicatedScope.Unit));
		Register(new ReplicatedComponentCodec<ModelScale>(ReplicatedScope.Unit));
		Register(new ReplicatedComponentCodec<Level>(ReplicatedScope.Unit));
		Register(new ReplicatedComponentCodec<Experience>(ReplicatedScope.Unit));
		Register(new AttackNumbersCodec());
		Register(new ArmorNumbersCodec());
		Register(new TagComponentCodec<Invulnerable>(ReplicatedScope.Unit));
		Register(new TagComponentCodec<CommandsFrozen>(ReplicatedScope.Unit));
		Register(new TagComponentCodec<AnimationsFrozen>(ReplicatedScope.Unit));
		Register(new TagComponentCodec<OverheadBarsHidden>(ReplicatedScope.Unit));
		Register(new TagComponentCodec<SpellImmune>(ReplicatedScope.Unit));
		Register(new TagComponentCodec<UnderConstruction>(ReplicatedScope.Unit));
	}

	public static void Register(IReplicatedComponentCodec codec)
	{
		for (int i = 0; i < _codecs.Count; i++)
		{
			if (_codecs[i].ComponentTypeId == codec.ComponentTypeId) return;
		}
		_codecs.Add(codec);
	}

	public static IReadOnlyList<IReplicatedComponentCodec> All => _codecs;

	public static IReplicatedComponentCodec? Find(int componentTypeId)
	{
		for (int i = 0; i < _codecs.Count; i++)
		{
			if (_codecs[i].ComponentTypeId == componentTypeId) return _codecs[i];
		}
		return null;
	}
}

public static class ReplicatedStateFingerprint
{
	public const uint Seed = 2166136261u;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static uint Combine(uint hash, int value)
	{
		unchecked
		{
			for (int i = 0; i < 4; i++)
			{
				hash ^= (byte)(value >> (i * 8));
				hash *= 16777619u;
			}
			return hash;
		}
	}

	public static uint Combine(uint hash, ReadOnlySpan<byte> bytes)
	{
		unchecked
		{
			for (int i = 0; i < bytes.Length; i++)
			{
				hash ^= bytes[i];
				hash *= 16777619u;
			}
			return hash;
		}
	}

	public static uint Combine(uint hash, string? value)
	{
		if (string.IsNullOrEmpty(value)) return Combine(hash, 0);
		unchecked
		{
			for (int i = 0; i < value.Length; i++)
			{
				hash ^= (byte)value[i];
				hash *= 16777619u;
				hash ^= (byte)(value[i] >> 8);
				hash *= 16777619u;
			}
			return hash;
		}
	}

	public static uint CombineColdPayload(uint hash, int scope, int key, int kind, int role, int contentId)
	{
		hash = Combine(hash, 7);
		hash = Combine(hash, scope);
		hash = Combine(hash, key);
		hash = Combine(hash, kind);
		hash = Combine(hash, role);
		return Combine(hash, contentId);
	}

	public static uint CombineWeatherState(uint hash, EcsWorld world, Entity worldEntity)
	{
		if (world == null || worldEntity == Entity.Null || !world.IsAlive(worldEntity) || !world.Has<WeatherState>(worldEntity)) return hash;

		var weather = world.Get<WeatherState>(worldEntity);
		hash = Combine(hash, 6);
		return Combine(hash, (int)weather.Type);
	}
}
