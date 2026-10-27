using System;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Shared.Metadata;
using Realm.Shared.Simulation;

namespace Realm.Shared.Maps;

/// <summary>
///     Places a building the way the authority does: the player pays for it, the entity is created from the catalog,
///     and a worker that was told to build it is left with the task and the walk to the site. The work is pure ECS, so
///     a headless process and the map loader build the same entity from the same template.
/// </summary>
public static class BuildingPlacementService
{
	/// <summary>
	///     Places the template for the player. A worker is handed the construction task and the building starts
	///     unfinished; without one the building is finished on the spot. Returns false, having changed nothing, when the
	///     template is unknown or the player cannot pay for it.
	/// </summary>
	public static bool TryPlace(
		EcsWorld world,
		DefinitionCatalog catalog,
		string templateId,
		Vector3 position,
		PlayerEntity ownerPlayer,
		Entity workerEntity,
		out Entity buildingEntity)
	{
		buildingEntity = Entity.Null;
		if (string.IsNullOrEmpty(templateId)) return false;
		if (ownerPlayer.Value == Entity.Null || !world.IsAlive(ownerPlayer.Value)) return false;

		UnitMetadata? metadata = catalog.FindUnitOrBuilding(templateId);
		if (metadata == null) return false;
		if (!PlayerResourceCost.TryCharge(world, ownerPlayer.Value, metadata)) return false;

		float buildSeconds = Math.Max(metadata.ProductionTime, 0f);
		bool builtByWorker = workerEntity != Entity.Null && world.IsAlive(workerEntity) && buildSeconds > 0f;

		buildingEntity = UnitEntityFactory.CreateFromMetadata(world, catalog, templateId, position, 0f, 0f, ownerPlayer, bypassPopulation: true);
		if (buildingEntity == Entity.Null) return false;

		if (world.TryGet<UnitOwnerPlayer>(ownerPlayer.Value, out UnitOwnerPlayer owner))
		{
			world.SetOrAdd(buildingEntity, owner);
		}

		if (!builtByWorker) return true;

		world.SetOrAdd(buildingEntity, new ConstructionState(buildSeconds));
		world.SetOrAdd(buildingEntity, new UnderConstruction());
		world.SetOrAdd(workerEntity, new BuildTask(buildingEntity, buildSeconds));
		world.SetOrAdd(workerEntity, new MoveTo(position));
		return true;
	}
}
