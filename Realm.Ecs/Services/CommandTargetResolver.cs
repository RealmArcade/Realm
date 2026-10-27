using System;
using System.Numerics;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;

namespace Realm.Ecs.Services;

/// <summary>
///     The resources and the time one production order costs, as the map authored them.
/// </summary>
public readonly struct ProductionCosts
{
	public readonly float Gold;
	public readonly float Wood;
	public readonly float Stone;
	public readonly float ProductionTime;

	public ProductionCosts(float gold, float wood, float stone, float productionTime)
	{
		Gold = gold;
		Wood = wood;
		Stone = stone;
		ProductionTime = productionTime;
	}
}

/// <summary>
///     What one ability costs its caster and how far it reaches, as the map authored it.
/// </summary>
public readonly struct AbilityRules
{
	public readonly float ManaCost;
	public readonly float Cooldown;
	public readonly float TargetRange;

	public AbilityRules(float manaCost, float cooldown, float targetRange)
	{
		ManaCost = manaCost;
		Cooldown = cooldown;
		TargetRange = targetRange;
	}
}

/// <summary>
///     Turns the entity ids a client names in a command into entities in this process's world, and finds the prop a
///     gather order points at. The process that owns the world carved those ids out of it, so a wire id is the entity
///     id there; a peer holds local entities the snapshot mapped from the authority's ids, so it translates through the
///     match's mapping. Either way a command is resolved without a scene node.
/// </summary>
public class CommandTargetResolver
{
	private static readonly QueryDescription PropQuery = QueryCache.AllPropIdentityAndPositionQuery;

	private readonly SimulationContext _context;
	private readonly EntityCallback<Position> _collectClosestProp;

	private Entity _closestProp = Entity.Null;
	private float _closestPropDistance = float.MaxValue;
	private Vector3 _closestPropOrigin;
	private string? _closestPropFilter;

	public CommandTargetResolver(SimulationContext context)
	{
		_context = context;
		_collectClosestProp = CollectClosestProp;
	}

	/// <summary>
	///     True on the process that owns the world, where a wire id is the entity id it was carved from. A peer that
	///     received its entities over the snapshot sets this false and resolves through the mapping instead.
	/// </summary>
	public bool OwnsEntityIds { get; set; } = true;

	/// <summary>The entity a command names, or <see cref="Entity.Null" /> when the id names nothing that is alive here.</summary>
	public Entity Resolve(int wireEntityId)
	{
		return TryResolve(wireEntityId, out Entity entity) ? entity : Entity.Null;
	}

	public bool TryResolve(int wireEntityId, out Entity entity)
	{
		entity = Entity.Null;
		if (wireEntityId <= 0) return false;

		if (OwnsEntityIds)
		{
			var candidate = new Entity(wireEntityId);
			if (!_context.World.IsAlive(candidate)) return false;

			entity = candidate;
			return true;
		}

		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return false;
		if (!_context.World.Has<NetworkMappingState>(worldEntity)) return false;

		if (!_context.World.Get<NetworkMappingState>(worldEntity).ServerToClientEntityMap.TryGetValue(wireEntityId, out Entity localEntity)) return false;
		if (localEntity == Entity.Null || !_context.World.IsAlive(localEntity)) return false;

		entity = localEntity;
		return true;
	}

	/// <summary>The id a command has to name this entity by, so an order round-trips through the same id it arrived on.</summary>
	public int ToWireEntityId(Entity entity)
	{
		if (entity == Entity.Null) return 0;
		if (OwnsEntityIds) return entity.Id;

		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.Has<NetworkMappingState>(worldEntity)) return 0;

		return _context.World.Get<NetworkMappingState>(worldEntity).ClientToServerEntityMap.TryGetValue(entity.Id, out int serverEntityId) ? serverEntityId : 0;
	}

	/// <summary>The nearest prop to a position, optionally restricted to one template id. Null when the world holds none.</summary>
	public bool TryFindClosestProp(Vector3 position, string propId, out Entity propEntity, out string resolvedPropId, out Vector3 propPosition)
	{
		_closestProp = Entity.Null;
		_closestPropDistance = float.MaxValue;
		_closestPropOrigin = position;
		_closestPropFilter = string.IsNullOrEmpty(propId) ? null : propId;

		EcsWorld world = _context.World;
		if (world != null) world.Query(in PropQuery, _collectClosestProp);

		propEntity = _closestProp;
		if (propEntity == Entity.Null)
		{
			resolvedPropId = string.Empty;
			propPosition = Vector3.Zero;
			return false;
		}

		resolvedPropId = world!.Has<PropIdentity>(propEntity) ? world.Get<PropIdentity>(propEntity).PropId : string.Empty;
		propPosition = world.Has<Position>(propEntity) ? world.Get<Position>(propEntity).Value : Vector3.Zero;
		return true;
	}

	private void CollectClosestProp(Entity entity, ref Position position)
	{
		EcsWorld world = _context.World;
		if (world.Has<Dead>(entity)) return;

		if (_closestPropFilter != null)
		{
			if (!world.Has<PropIdentity>(entity)) return;
			if (!string.Equals(world.Get<PropIdentity>(entity).PropId, _closestPropFilter, StringComparison.Ordinal)) return;
		}

		float distance = Vector3.Distance(position.Value, _closestPropOrigin);
		if (distance >= _closestPropDistance) return;

		_closestPropDistance = distance;
		_closestProp = entity;
	}
}
