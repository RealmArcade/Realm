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

public static class BuildingPlacementService
{
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
		if (!CanPlaceWithinBuildingSupply(world, ownerPlayer)) return false;
		if (!PlayerResourceCost.TryCharge(world, ownerPlayer.Value, metadata)) return false;

		float buildSeconds = Math.Max(metadata.ProductionTime, 0f);

		bool builtByWorker = workerEntity != Entity.Null && world.IsAlive(workerEntity);

		buildingEntity = UnitEntityFactory.CreateFromMetadata(world, catalog, templateId, position, 0f, 0f, ownerPlayer, bypassPopulation: false);
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

	/// <summary>
	/// Counts the buildings a player owns, including construction sites and buildings that were destroyed but are still
	/// standing. A building supply is a count of locations, so a site that has not finished yet already occupies one.
	/// </summary>
	/// <param name="world">The world the player belongs to.</param>
	/// <param name="ownerPlayer">The player whose buildings are counted.</param>
	/// <returns>The number of building entities the player owns.</returns>
	public static int CountBuildingsOwnedBy(EcsWorld world, PlayerEntity ownerPlayer)
	{
		if (ownerPlayer.Value == Entity.Null) return 0;

		int count = 0;
		foreach (Entity entity in world.Query<Owner, Building>())
		{
			if (!world.TryGet<Owner>(entity, out Owner owner)) continue;
			if (owner.PlayerEntity.Value != ownerPlayer.Value) continue;

			count++;
		}

		return count;
	}

	/// <summary>
	/// Reports the building supply a player holds: the cap a map placed on its buildings, or zero when the map left the
	/// player unlimited.
	/// </summary>
	/// <param name="world">The world the player belongs to.</param>
	/// <param name="ownerPlayer">The player whose supply is read.</param>
	/// <returns>The maximum number of buildings, or zero for no limit.</returns>
	public static int GetBuildingSupply(EcsWorld world, PlayerEntity ownerPlayer)
	{
		if (ownerPlayer.Value == Entity.Null) return 0;
		if (!world.TryGet<PlayerBuildingLimit>(ownerPlayer.Value, out PlayerBuildingLimit buildingLimit)) return 0;

		return Math.Max(buildingLimit.Value, 0);
	}

	/// <summary>
	/// Reports whether another building fits in the player's supply. A supply of zero means the map did not cap this
	/// player, so every placement fits.
	/// </summary>
	/// <param name="world">The world the player belongs to.</param>
	/// <param name="ownerPlayer">The player attempting the placement.</param>
	/// <returns>True when the player may hold one more building.</returns>
	public static bool CanPlaceWithinBuildingSupply(EcsWorld world, PlayerEntity ownerPlayer)
	{
		int supply = GetBuildingSupply(world, ownerPlayer);
		if (supply <= 0) return true;

		return CountBuildingsOwnedBy(world, ownerPlayer) < supply;
	}
}
