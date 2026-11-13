using System.Numerics;
using System;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Shared.Metadata;

namespace Realm.Shared.Maps;

public static class PropEntityFactory
{
	public static Entity CreateCore(EcsWorld world, string propId, Vector3 position, float rotationY, float scale, float authoredCollisionRadius)
	{
		Entity entity = world.Create();
		float placedScale = scale <= 0.001f ? 1f : scale;

		world.Add(entity, new PropIdentity(propId));
		world.Add(entity, new Prop());
		world.Add(entity, new Position(position));
		world.Add(entity, new RotationY(rotationY));
		world.Add(entity, new ModelScale(placedScale));
		world.Add(entity, new CollisionScale(placedScale));
		world.Add(entity, new CollisionRadius(GameplayConstants.ResolveContactRadius(authoredCollisionRadius, 1f, GameplayConstants.DefaultPropCollisionRadius)));
		return entity;
	}

	public static float ResolveCollisionRadius(PlaceableMetadata metadata)
	{
		return GameplayConstants.ResolveContactRadius(metadata.CollisionCircle, 1f, GameplayConstants.DefaultPropCollisionRadius);
	}

	public static Entity Create(EcsWorld world, DefinitionCatalog catalog, string propId, string? cleanId, Vector3 position, float rotationY, float scale, float collisionRadius, int level = 0)
	{
		PlaceableMetadata? metadata = FindPlaceable(catalog, propId, cleanId);
		float bodyRadius = metadata != null ? ResolveCollisionRadius(metadata) : collisionRadius;

		Entity entity = CreateCore(world, propId, position, rotationY, scale, bodyRadius);
		AddResourceNode(world, entity, FindResource(catalog, propId, cleanId), DefinitionCatalog.DefaultResourceAmount(propId), level);
		return entity;
	}

	public static void AddResourceNode(EcsWorld world, Entity entity, ResourceMetadata? metadata, float savedAmount, int level = 0)
	{
		if (metadata != null && (metadata.MaxCapacity > 0f || savedAmount > 0f))
		{
			AddHarvestableNode(world, entity, metadata, savedAmount, level);
			return;
		}

		if (savedAmount > 0f)
		{
			world.Add(entity, new ResourceNode(Guid.Empty, savedAmount, savedAmount, 10f, 0f, 5));
		}
	}

	/// <summary>
	///     Gives a gatherable node the body combat reads: its capacity is its health, its template's resistances decide how
	///     much of a worker's hit it keeps, and each point that lands is one piece of the item the template names.
	/// </summary>
	/// <param name="world">The world the node belongs to.</param>
	/// <param name="entity">The node entity to give its capacity and resistances.</param>
	/// <param name="metadata">The resource template the node is an instance of.</param>
	/// <param name="requestedAmount">The capacity the node is asked to hold, or zero to use the template's authored capacity at its level.</param>
	/// <param name="level">The level this node stands at, or zero to stand at the level its template states.</param>
	public static void AddHarvestableNode(EcsWorld world, Entity entity, ResourceMetadata metadata, float requestedAmount, int level = 0)
	{
		int nodeLevel = Math.Max(1, level > 0 ? level : metadata.Level);
		float authoredCapacity = metadata.MaxCapacity > 0f ? metadata.MaxCapacity : requestedAmount;
		float amount = requestedAmount > 0f ? requestedAmount : authoredCapacity + metadata.MaxCapacityPerLevel * (nodeLevel - 1);
		if (amount <= 0f) return;

		float harvestRate = metadata.HarvestRate > 0f ? metadata.HarvestRate : 10f;
		int maxWorkers = metadata.MaxWorkers > 0 ? metadata.MaxWorkers : 5;
		world.SetOrAdd(entity, new ResourceNode(Guid.Empty, amount, amount, harvestRate, metadata.GrowthRate, maxWorkers));
		AddHarvestableBody(world, entity, metadata, amount, nodeLevel);
	}

	private static void AddHarvestableBody(EcsWorld world, Entity entity, ResourceMetadata metadata, float amount, int level)
	{
		float armor = metadata.Armor + metadata.ArmorPerLevel * (level - 1);
		float magicResistance = metadata.MagicResistance + metadata.MagicResistancePerLevel * (level - 1);

		world.SetOrAdd(entity, new Level(level));
		world.Add(entity, new Health(amount, amount));
		world.Add(entity, new Resistances(armor, magicResistance));

		string resourceType = string.IsNullOrEmpty(metadata.ResourceType) ? string.Empty : metadata.ResourceType;
		string itemId = string.IsNullOrEmpty(metadata.ItemTemplateId) ? string.Empty : metadata.ItemTemplateId;
		if (resourceType.Length > 0 && itemId.Length > 0) world.Add(entity, new Harvestable(resourceType, itemId, metadata.YieldPerDamage));
	}

	private static ResourceMetadata? FindResource(DefinitionCatalog catalog, string propId, string? cleanId)
	{
		ResourceMetadata? metadata = catalog.FindResource(propId);
		if (metadata != null || string.IsNullOrEmpty(cleanId)) return metadata;
		return catalog.FindResource(cleanId);
	}

	private static PlaceableMetadata? FindPlaceable(DefinitionCatalog catalog, string propId, string? cleanId)
	{
		PlaceableMetadata? byId = catalog.FindProp(propId) ?? (PlaceableMetadata?)catalog.FindResource(propId);
		if (byId != null || string.IsNullOrEmpty(cleanId)) return byId;

		return catalog.FindProp(cleanId) ?? (PlaceableMetadata?)catalog.FindResource(cleanId);
	}
}
