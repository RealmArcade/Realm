using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using DotRecast.Detour;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

public class WorldInitService
{
	private void AddOrSet<T>(Entity entity, T component) where T : unmanaged
	{
		GameContext.World.SetOrAdd(entity, component);
	}

	private Entity GetOrCreateWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var worldQuery = QueryCache.AllTerrainStateQuery;
		GameContext.World.Query(in worldQuery, entity => worldEntity = entity);

		if (worldEntity == Entity.Null)
		{
			worldEntity = GameContext.World.Create();
		}

		return worldEntity;
	}

	private void InitializeCommonWorldComponents(Entity worldEntity)
	{
		GameContext.Host?.LoadModelYOffsetsFromMetadataJson();

		AddOrSet(worldEntity, new WorldState(0f, 0, 0f, true));
		AddOrSet(worldEntity, new ReplayState(0, 500f, 400f, 200f));
		AddOrSet(worldEntity, new NetworkState(1, 0f, 0, -1, -1, false, 0, 1));
		var mappingState = new NetworkMappingState(new(), new(), new());
		if (GameContext.Simulation.PlayerResourceService.GoldResourceId == default)
		{
			GameContext.Simulation.PlayerResourceService.GoldResourceId = "gold".AsResourceId(GameContext.Simulation.DefinitionManager);
			GameContext.Simulation.PlayerResourceService.WoodResourceId = "wood".AsResourceId(GameContext.Simulation.DefinitionManager);
			GameContext.Simulation.PlayerResourceService.StoneResourceId = "stone".AsResourceId(GameContext.Simulation.DefinitionManager);
		}

		var defaultPlayers = CreateDefaultPlayers();

		for (int i = 0; i < 25; i++)
		{
			var playerEntity = GameContext.World.Create();
			GameContext.World.Add(playerEntity, new Player());
			GameContext.World.Add(playerEntity, new UnitOwnerPlayer(i));
			GameContext.World.Add(playerEntity, new Name(i == 0 ? "Neutral" : (i == 1 ? "Horaid_Topa" : $"Player {i}")));
			// Everyone starts with nothing; the map's own script decides what a player begins with.
			var resourcesDict = new Dictionary<ResourceId, int>
			{
				{ GameContext.Simulation.PlayerResourceService.GoldResourceId, 0 },
				{ GameContext.Simulation.PlayerResourceService.WoodResourceId, 0 },
				{ GameContext.Simulation.PlayerResourceService.StoneResourceId, 0 }
			};
			GameContext.World.Add(playerEntity, new PlayerResources(resourcesDict));
			GameContext.World.Add(playerEntity, new PlayerPopulation(0, 0));
			GameContext.World.Add(playerEntity, new SpellCooldowns(null));
			GameContext.World.Add(playerEntity, new PlayerUpgrades(false, false, false));
			GameContext.World.Add(playerEntity, new PlayerTeam(defaultPlayers[i].Team, defaultPlayers[i].TeamNameStringId));
			GameContext.World.GetBuffer<ScriptPlayer>().Set(i, in defaultPlayers[i]);
			mappingState.PlayerEntities[i] = playerEntity;
		}

		mappingState.PeerIdToPlayerEntityMap[0] = mappingState.PlayerEntities[0];
		mappingState.PeerIdToPlayerEntityMap[1] = mappingState.PlayerEntities[1];
		mappingState.PeerIdToPlayerEntityMap[-1] = mappingState.PlayerEntities[2];

		AddOrSet(worldEntity, mappingState);
		if (!GameContext.World.Has<EditorState>(worldEntity))
		{
			GameContext.World.Add(worldEntity, new EditorState(true, 3.0f, -95.0f, 95.0f, -95.0f, 125.0f, "", false));
		}

		AddOrSet(worldEntity, new InputState(0, null, null, null, false));
		AddOrSet(worldEntity, new VFXQueue(null));
		AddOrSet(worldEntity, new CameraState
		{
			MoveSpeed = 35.0f,
			ZoomSpeed = 10.0f,
			MinZoom = 10.0f,
			MaxZoom = 90.0f,
			ZoomStep = 4.0f,
			EdgePanMargin = 20.0f,
			EnableEdgePanning = true,
			IsLocked = false,
			LimitLeft = null,
			LimitRight = null,
			LimitTop = null,
			LimitBottom = null,
			TargetHeight = 35.0f,
			CurrentHeight = 35.0f,
			IsDraggingMouse = false,
			LastMousePosition = Vector2.Zero,
			TargetYaw = 0.0f,
			CurrentYaw = 0.0f,
			TargetPitch = -55.0f,
			CurrentPitch = -55.0f,
			IsTopDown = false,
			YawSwing = 0.0f,
			PitchSwing = 0.0f
		});

		if (GameContext.World.Has<ShroudState>(worldEntity))
		{
			ShroudGridRegistry.Release(GameContext.World.Get<ShroudState>(worldEntity).GridHandle);
		}

		AddOrSet(worldEntity, new ShroudState());
		AddOrSet(worldEntity, new WeatherState("clear", 0f));
		AddOrSet(worldEntity, new SpectatorPerspective(-1));
		AddOrSet(worldEntity, new CountdownState(false, 0f, ""));
		AddOrSet(worldEntity, new LeaderboardState(new LeaderboardBoards()));
		AddOrSet(worldEntity, new ScriptZonesState(null));

		AddOrSet(worldEntity, new ScriptPlayersState(defaultPlayers));
		AddOrSet(worldEntity, new CombatAlertState(0f));
		AddOrSet(worldEntity, new PlayerRelationshipMatrix());
		GameContext.SimulationService?.RecalculatePlayerRelationships();
	}

	private static ScriptPlayer[] CreateDefaultPlayers() => MatchRoster.CreateDefaultPlayers();

	private void UpdateTerrainState(
		Entity worldEntity,
		int width,
		int depth,
		float quadSize,
		float cellSize,
		TerrainCell[,] cells,
		int[,] pathingCodes,
		DtNavMesh navMesh,
		DtNavMeshQuery navMeshQuery)
	{
		if (GameContext.World.Has<TerrainState>(worldEntity))
		{
			ref var existing = ref GameContext.World.Get<TerrainState>(worldEntity);
			if (cells != null) existing.Cells = cells;
			if (pathingCodes != null) existing.PathingCodes = pathingCodes;
			if (navMesh != null) existing.NavMesh = navMesh;
			if (navMeshQuery != null) existing.NavMeshQuery = navMeshQuery;
			GameContext.World.Set(worldEntity, existing);
		}
		else
		{
			GameContext.World.Add(worldEntity, new TerrainState(
				width, depth, quadSize, cellSize,
				cells, pathingCodes, navMesh, navMeshQuery
			));
		}
	}

	private void UpdateTerrainState(
		Entity worldEntity,
		int width,
		int depth,
		float quadSize,
		float cellSize,
		float[,] heights,
		int[,] pathingCodes,
		DtNavMesh navMesh,
		DtNavMeshQuery navMeshQuery)
	{
		if (GameContext.World.Has<TerrainState>(worldEntity))
		{
			ref var existing = ref GameContext.World.Get<TerrainState>(worldEntity);
			if (heights != null) existing.SetHeights(heights);
			if (pathingCodes != null) existing.PathingCodes = pathingCodes;
			if (navMesh != null) existing.NavMesh = navMesh;
			if (navMeshQuery != null) existing.NavMeshQuery = navMeshQuery;
			GameContext.World.Set(worldEntity, existing);
		}
		else
		{
			GameContext.World.Add(worldEntity, new TerrainState(
				width, depth, quadSize, cellSize,
				heights, pathingCodes, navMesh, navMeshQuery
			));
		}
	}

	public Entity SetupWorldEntityComponents(
		int width,
		int depth,
		float quadSize,
		float cellSize,
		TerrainCell[,] cells,
		int[,] pathingCodes,
		DtNavMesh navMesh,
		DtNavMeshQuery navMeshQuery)
	{
		Entity worldEntity = GetOrCreateWorldEntity();
		InitializeCommonWorldComponents(worldEntity);
		UpdateTerrainState(worldEntity, width, depth, quadSize, cellSize, cells, pathingCodes, navMesh, navMeshQuery);
		return worldEntity;
	}

	public Entity SetupWorldEntityComponents(
		int width,
		int depth,
		float quadSize,
		float cellSize,
		float[,] heights,
		int[,] pathingCodes,
		DtNavMesh navMesh,
		DtNavMeshQuery navMeshQuery)
	{
		Entity worldEntity = GetOrCreateWorldEntity();
		InitializeCommonWorldComponents(worldEntity);
		UpdateTerrainState(worldEntity, width, depth, quadSize, cellSize, heights, pathingCodes, navMesh, navMeshQuery);
		return worldEntity;
	}
}