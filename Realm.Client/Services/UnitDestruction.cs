using System;
using System.Collections.Generic;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Services;

/// <summary>
///     Taking a unit off the world. Marking it dead is the light path the authority's kill message uses;
///     destroying it is the full one - uncarving a building's ground in the editor, dropping the network
///     mapping and the cached script wrapper, and clearing every LastAttacker that pointed at it so no stale
///     entity reference outlives the unit.
/// </summary>
public sealed class UnitDestruction
{
	private readonly Dictionary<int, Unit_WasmRuntime> _wrapperCache;
	private readonly List<Entity> _referenceHoldersScratch = new();

	public UnitDestruction(Dictionary<int, Unit_WasmRuntime> wrapperCache)
	{
		_wrapperCache = wrapperCache;
	}

	public void DomainKillUnit(Entity entity, Action viewAction)
	{
		if (SimulationService.World.IsAlive(entity) && !SimulationService.World.Has<Dead>(entity))
		{
			SimulationService.World.Add<Dead>(entity);
			viewAction?.Invoke();
		}
	}

	public void DomainDestroyUnit(Entity entity, bool isBuilding, float radius, Vector3 unitPos, Action viewAction)
	{
		if (!SimulationService.World.IsAlive(entity)) return;

		// Only the editor opens ground itself. In a match the mesh is baked from the authority's obstacle set, so a screen
		// that uncarved here would path through a blocker the authority still has.
		if (isBuilding && GameContext.SessionState.IsMapEditorMode)
		{
			if (SimulationService.World.IsAlive(GameContext.Simulation.WorldEntity) && SimulationService.World.Has<TerrainState>(GameContext.Simulation.WorldEntity))
			{
				ref var state = ref SimulationService.World.Get<TerrainState>(GameContext.Simulation.WorldEntity);
				GameContext.Simulation.TerrainNavMeshService?.UncarveObstacle(ref state, unitPos, radius);
			}
		}

		GameContext.NetworkService.RemoveMultiplayerMapping(entity.Id);

		int id = entity.Id;
		_wrapperCache.Remove(id);
		SimulationService.World.Destroy(entity);

		viewAction?.Invoke();
	}

	public bool HandleEnemyDeathEconomyDomain(Entity unitEntity, string unitId, Entity playerEntity, int resourceCap)
	{
		if (!GameContext.RegistryService.UnitRegistry.TryGetValue(unitId, out var bountyMeta) || bountyMeta.GoldBounty <= 0f) return false;
		if (!SimulationService.World.IsAlive(playerEntity) || !SimulationService.World.Has<PlayerResources>(playerEntity)) return false;

		SimulationService.World.Mutate<PlayerResources>(playerEntity, (ref PlayerResources r) =>
		{
			r.Gold = (int)Math.Min(resourceCap, r.Gold + bountyMeta.GoldBounty);
		});
		
		return true;
	}

	public (float Radius, System.Numerics.Vector3 Position) GetObstacleUncarveData(Entity unitEntity, System.Numerics.Vector3 fallbackPos)
	{
		float radius = SimulationService.World.Has<CollisionRadius>(unitEntity) ? SimulationService.World.Get<CollisionRadius>(unitEntity).Value : 2.0f;
		var unitPos = SimulationService.World.Has<Position>(unitEntity) ? SimulationService.World.Get<Position>(unitEntity).Value : fallbackPos;
		return (radius, unitPos);
	}

	public int GetDeathPopulationCost(Entity unitEntity, string unitId)
	{
		if (!GameContext.RegistryService.UnitRegistry.TryGetValue(unitId, out var killMeta)) return 0;
		
		int popCost = 0;
		if (!SimulationService.World.Has<BypassPopulationTag>(unitEntity))
		{
			popCost = killMeta.PopCost;
		}

		return popCost;
	}

	public Entity GetKillerEntityDomain(Entity unitEntity)
	{
		Entity killerEntity = Entity.Null;
		if (!SimulationService.World.IsAlive(unitEntity)) return killerEntity;

		if (SimulationService.World.Has<LastAttacker>(unitEntity))
		{
			var att = SimulationService.World.Get<LastAttacker>(unitEntity).Value;
			if (SimulationService.World.IsAlive(att))
			{
				killerEntity = att;
			}
		}
		return killerEntity;
	}

	public void DestroyUnitEntityDomain(Entity unitEntity)
	{
		if (!SimulationService.World.IsAlive(unitEntity)) return;
		_wrapperCache.Remove(unitEntity.Id);
		GameContext.NetworkService.ForgetReferencesHeldBy(unitEntity.Id);
		SimulationService.World.Destroy(unitEntity);
		RemoveReferencesToDestroyedEntity(unitEntity);
	}

	private void RemoveReferencesToDestroyedEntity(Entity destroyedEntity)
	{
		EcsWorld? world = GameContext.World;
		if (world == null) return;

		_referenceHoldersScratch.Clear();
		foreach (Entity entity in world.Query<LastAttacker>())
		{
			if (world.Get<LastAttacker>(entity).Value != destroyedEntity) continue;
			_referenceHoldersScratch.Add(entity);
		}

		for (int index = 0; index < _referenceHoldersScratch.Count; index++)
		{
			world.Remove<LastAttacker>(_referenceHoldersScratch[index]);
		}
	}

	public void DestroyEntitiesDomain(IEnumerable<Entity> entities)
	{
		foreach (var entity in entities)
		{
			if (SimulationService.World.IsAlive(entity))
			{
				_wrapperCache.Remove(entity.Id);
				SimulationService.World.Destroy(entity);
				RemoveReferencesToDestroyedEntity(entity);
			}
		}
	}
}
