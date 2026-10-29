using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
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

	public static Entity Create(EcsWorld world, DefinitionCatalog catalog, string propId, string? cleanId, Vector3 position, float rotationY, float scale, float collisionRadius)
	{
		PlaceableMetadata? metadata = FindPlaceable(catalog, propId, cleanId);
		float bodyRadius = metadata != null ? ResolveCollisionRadius(metadata) : collisionRadius;

		Entity entity = CreateCore(world, propId, position, rotationY, scale, bodyRadius);
		AddResourceNode(world, entity, FindResource(catalog, propId, cleanId), DefinitionCatalog.DefaultResourceAmount(propId));
		return entity;
	}

	public static void AddResourceNode(EcsWorld world, Entity entity, ResourceMetadata? metadata, float savedAmount)
	{
		if (metadata != null && (metadata.MaxCapacity > 0f || savedAmount > 0f))
		{
			float amount = metadata.MaxCapacity > 0f ? metadata.MaxCapacity : savedAmount;
			float harvestRate = metadata.HarvestRate > 0f ? metadata.HarvestRate : 10f;
			int maxWorkers = metadata.MaxWorkers > 0 ? metadata.MaxWorkers : 5;
			world.Add(entity, new ResourceNode(Guid.Empty, amount, amount, harvestRate, metadata.GrowthRate, maxWorkers));
			return;
		}

		if (savedAmount > 0f)
		{
			world.Add(entity, new ResourceNode(Guid.Empty, savedAmount, savedAmount, 10f, 0f, 5));
		}
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
