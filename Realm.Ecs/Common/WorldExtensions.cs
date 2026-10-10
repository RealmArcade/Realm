using Arch.Core;

namespace Realm.Ecs.Common;

public static class WorldExtensions
{
    /// <summary>
    ///     Returns the component value for <paramref name="entity"/> when the entity is alive
    ///     and the component exists; otherwise returns <paramref name="fallback"/>.
    /// </summary>
    public static TComponent GetOrDefault<TComponent>(
        this World world,
        Entity entity,
        TComponent fallback = default)
        where TComponent : struct
    {
        if (world.IsAlive(entity) && world.Has<TComponent>(entity))
            return world.Get<TComponent>(entity);
        return fallback;
    }

    /// <summary>
    ///     Selects a single field from the component for <paramref name="entity"/> when the entity
    ///     is alive and the component exists; otherwise returns <paramref name="fallback"/>.
    /// </summary>
    public static TField GetFieldOrDefault<TComponent, TField>(
        this World world,
        Entity entity,
        Func<TComponent, TField> selector,
        TField fallback = default!)
        where TComponent : struct
    {
        if (world.IsAlive(entity) && world.Has<TComponent>(entity))
            return selector(world.Get<TComponent>(entity));
        return fallback;
    }

    /// <summary>
    ///     Returns <see langword="true"/> and writes the component into <paramref name="value"/>
    ///     when the entity is alive and carries the component; otherwise returns
    ///     <see langword="false"/> and leaves <paramref name="value"/> at <see langword="default"/>.
    /// </summary>
    public static bool TryGet<TComponent>(
        this World world,
        Entity entity,
        out TComponent value)
        where TComponent : struct
    {
        if (world.IsAlive(entity) && world.Has<TComponent>(entity))
        {
            value = world.Get<TComponent>(entity);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    ///     Mutates a component on <paramref name="entity"/> via <paramref name="mutator"/> when
    ///     the entity is alive and the component exists. The mutation receives a
    ///     <see langword="ref"/> to the live component so no copy is required.
    /// </summary>
    public static void Mutate<TComponent>(
        this World world,
        Entity entity,
        ComponentMutator<TComponent> mutator)
        where TComponent : struct
    {
        if (world.IsAlive(entity) && world.Has<TComponent>(entity))
            mutator(ref world.Get<TComponent>(entity));
    }

    /// <summary>
    ///     Replaces the component on <paramref name="entity"/> with <paramref name="newValue"/>
    ///     when the entity is alive and the component exists. Has no effect otherwise.
    /// </summary>
    public static void SetIfAlive<TComponent>(
        this World world,
        Entity entity,
        TComponent newValue)
        where TComponent : struct
    {
        if (world.IsAlive(entity) && world.Has<TComponent>(entity))
            world.Set(entity, newValue);
    }

    /// <summary>
    ///     Sets the component if present or adds it if absent on <paramref name="entity"/>
    ///     when the entity is alive.
    /// </summary>
    public static void SetOrAdd<TComponent>(
        this World world,
        Entity entity,
        TComponent component)
        where TComponent : struct
    {
        if (!world.IsAlive(entity))
            return;

        if (world.Has<TComponent>(entity))
            world.Set(entity, component);
        else
            world.Add(entity, component);
    }
}