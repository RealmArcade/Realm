using System;

namespace Realm.Ecs.Common;

public static class WorldExtensions
{
	public static TComponent GetOrDefault<TComponent>(
		this EcsWorld world,
		Entity entity,
		TComponent fallback = default)
		where TComponent : unmanaged
	{
		if (world.IsAlive(entity) && world.Has<TComponent>(entity))
			return world.Get<TComponent>(entity);
		return fallback;
	}

	public static TField GetFieldOrDefault<TComponent, TField>(
		this EcsWorld world,
		Entity entity,
		Func<TComponent, TField> selector,
		TField fallback = default!)
		where TComponent : unmanaged
	{
		if (world.IsAlive(entity) && world.Has<TComponent>(entity))
			return selector(world.Get<TComponent>(entity));
		return fallback;
	}

	public static void Mutate<TComponent>(
		this EcsWorld world,
		Entity entity,
		ComponentMutator<TComponent> mutator)
		where TComponent : unmanaged
	{
		if (world.IsAlive(entity) && world.Has<TComponent>(entity))
			mutator(ref world.Get<TComponent>(entity));
	}

	public static void SetIfAlive<TComponent>(
		this EcsWorld world,
		Entity entity,
		TComponent newValue)
		where TComponent : unmanaged
	{
		if (world.IsAlive(entity) && world.Has<TComponent>(entity))
			world.Set(entity, newValue);
	}

	public static ref TComponent AddOrGet<TComponent>(
		this EcsWorld world,
		Entity entity,
		in TComponent component = default)
		where TComponent : unmanaged
	{
		if (world.Has<TComponent>(entity))
			return ref world.Get<TComponent>(entity);
		return ref world.Add(entity, component);
	}

	public static Entity Create<T1>(this EcsWorld world, in T1 c1) where T1 : unmanaged
	{
		Entity e = world.Create();
		world.Add(e, c1);
		return e;
	}

	public static Entity Create<T1, T2>(this EcsWorld world, in T1 c1, in T2 c2) where T1 : unmanaged where T2 : unmanaged
	{
		Entity e = world.Create();
		world.Add(e, c1);
		world.Add(e, c2);
		return e;
	}

	public static Entity Create<T1, T2, T3>(this EcsWorld world, in T1 c1, in T2 c2, in T3 c3) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
	{
		Entity e = world.Create();
		world.Add(e, c1);
		world.Add(e, c2);
		world.Add(e, c3);
		return e;
	}

	public static Entity Create<T1, T2, T3, T4>(this EcsWorld world, in T1 c1, in T2 c2, in T3 c3, in T4 c4) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged
	{
		Entity e = world.Create();
		world.Add(e, c1);
		world.Add(e, c2);
		world.Add(e, c3);
		world.Add(e, c4);
		return e;
	}

	public static Entity Create<T1, T2, T3, T4, T5>(this EcsWorld world, in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged
	{
		Entity e = world.Create();
		world.Add(e, c1);
		world.Add(e, c2);
		world.Add(e, c3);
		world.Add(e, c4);
		world.Add(e, c5);
		return e;
	}
}
