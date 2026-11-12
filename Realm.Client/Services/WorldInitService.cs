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
	private static void AddOrSet<T>(EcsWorld world, Entity entity, T component) where T : unmanaged
	{
		world.SetOrAdd(entity, component);
	}

	private static Entity GetOrCreateWorldEntity(EcsWorld world)
	{
		Entity worldEntity = Entity.Null;
		var worldQuery = QueryCache.AllTerrainStateQuery;
		world.Query(in worldQuery, entity => worldEntity = entity);

		if (worldEntity == Entity.Null)
		{
			worldEntity = world.Create();
		}

		return worldEntity;
	}

	private static void InitializeCommonWorldComponents(EcsWorld world, Entity worldEntity)
	{
		GameContext.Host?.LoadModelYOffsetsFromMetadataJson();

		AddOrSet(world, worldEntity, new WorldState(0f, 0, 0f, true));
		AddOrSet(world, worldEntity, new ReplayState(0, 500f, 400f, 200f));
		AddOrSet(world, worldEntity, new NetworkState(1, 0f, 0, -1, -1, false, 0, 1));
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
			var playerEntity = world.Create();
			world.Add(playerEntity, new Player());
			world.Add(playerEntity, new UnitOwnerPlayer(i));
			string playerName = i == 0
				? "Neutral"
				: i == 1
					? GameContext.Lobby?.PlayerDisplayName ?? Godot.TranslationServer.Translate("Guest")
					: $"Player {i}";
			world.Add(playerEntity, new Name(playerName));
			var resourcesDict = new Dictionary<ResourceId, int>
			{
				{ GameContext.Simulation.PlayerResourceService.GoldResourceId, 0 },
				{ GameContext.Simulation.PlayerResourceService.WoodResourceId, 0 },
				{ GameContext.Simulation.PlayerResourceService.StoneResourceId, 0 }
			};
			world.Add(playerEntity, new PlayerResources(resourcesDict));
			world.Add(playerEntity, new PlayerPopulation(0, 0));
			world.Add(playerEntity, new SpellCooldowns(null));
			world.Add(playerEntity, new PlayerUpgrades(false, false, false));
			world.Add(playerEntity, new PlayerTeam(defaultPlayers[i].Team, defaultPlayers[i].TeamNameStringId));
			world.GetBuffer<ScriptPlayer>().Set(i, in defaultPlayers[i]);
			mappingState.PlayerEntities[i] = playerEntity;
		}

		mappingState.PeerIdToPlayerEntityMap[0] = mappingState.PlayerEntities[0];
		mappingState.PeerIdToPlayerEntityMap[1] = mappingState.PlayerEntities[1];
		mappingState.PeerIdToPlayerEntityMap[-1] = mappingState.PlayerEntities[2];

		AddOrSet(world, worldEntity, mappingState);
		if (!world.Has<EditorState>(worldEntity))
		{
			world.Add(worldEntity, new EditorState(true, 3.0f, -95.0f, 95.0f, -95.0f, 125.0f, "", false));
		}

		AddOrSet(world, worldEntity, new InputState(0, null, null, null, false));
		AddOrSet(world, worldEntity, new VFXQueue(null));
		AddOrSet(world, worldEntity, new CameraState
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

		if (world.Has<ShroudState>(worldEntity))
		{
			ShroudGridRegistry.Release(world.Get<ShroudState>(worldEntity).GridHandle);
		}

		AddOrSet(world, worldEntity, new ShroudState());
		AddOrSet(world, worldEntity, new WeatherState("clear", 0f));
		AddOrSet(world, worldEntity, new SpectatorPerspective(-1));
		AddOrSet(world, worldEntity, new CountdownState(false, 0f, ""));
		AddOrSet(world, worldEntity, new LeaderboardState(new LeaderboardBoards()));
		AddOrSet(world, worldEntity, new ScriptZonesState(null));

		AddOrSet(world, worldEntity, new ScriptPlayersState(defaultPlayers));
		AddOrSet(world, worldEntity, new CombatAlertState(0f));
		AddOrSet(world, worldEntity, new PlayerRelationshipMatrix());
		GameContext.SimulationService?.RestartRelationshipDerivation();
		GameContext.SimulationService?.RecalculatePlayerRelationships();
	}

	private static ScriptPlayer[] CreateDefaultPlayers() => MatchRoster.CreateDefaultPlayers();

	private static void UpdateTerrainState(
		EcsWorld world,
		Entity worldEntity,
		int width,
		int depth,
		float quadSize,
		float cellSize,
		TerrainCell[,]? cells,
		int[,]? pathingCodes,
		DtNavMesh? navMesh,
		DtNavMeshQuery? navMeshQuery)
	{
		if (world.Has<TerrainState>(worldEntity))
		{
			ref var existing = ref world.Get<TerrainState>(worldEntity);
			if (cells != null) existing.Cells = cells;
			if (pathingCodes != null) existing.PathingCodes = pathingCodes;
			if (navMesh != null) existing.NavMesh = navMesh;
			if (navMeshQuery != null) existing.NavMeshQuery = navMeshQuery;
			world.Set(worldEntity, existing);
		}
		else
		{
			world.Add(worldEntity, new TerrainState(
				width, depth, quadSize, cellSize,
				cells, pathingCodes, navMesh, navMeshQuery
			));
		}
	}

	private static void UpdateTerrainState(
		EcsWorld world,
		Entity worldEntity,
		int width,
		int depth,
		float quadSize,
		float cellSize,
		float[,]? heights,
		int[,]? pathingCodes,
		DtNavMesh? navMesh,
		DtNavMeshQuery? navMeshQuery)
	{
		if (world.Has<TerrainState>(worldEntity))
		{
			ref var existing = ref world.Get<TerrainState>(worldEntity);
			if (heights != null) existing.SetHeights(heights);
			if (pathingCodes != null) existing.PathingCodes = pathingCodes;
			if (navMesh != null) existing.NavMesh = navMesh;
			if (navMeshQuery != null) existing.NavMeshQuery = navMeshQuery;
			world.Set(worldEntity, existing);
		}
		else if (heights != null)
		{
			world.Add(worldEntity, new TerrainState(
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
		TerrainCell[,]? cells,
		int[,]? pathingCodes,
		DtNavMesh? navMesh,
		DtNavMeshQuery? navMeshQuery)
	{
		if (GameContext.World is not { } world) return Entity.Null;

		Entity worldEntity = GetOrCreateWorldEntity(world);
		InitializeCommonWorldComponents(world, worldEntity);
		UpdateTerrainState(world, worldEntity, width, depth, quadSize, cellSize, cells, pathingCodes, navMesh, navMeshQuery);
		return worldEntity;
	}

	public Entity SetupWorldEntityComponents(
		int width,
		int depth,
		float quadSize,
		float cellSize,
		float[,]? heights,
		int[,]? pathingCodes,
		DtNavMesh? navMesh,
		DtNavMeshQuery? navMeshQuery)
	{
		if (GameContext.World is not { } world) return Entity.Null;

		Entity worldEntity = GetOrCreateWorldEntity(world);
		InitializeCommonWorldComponents(world, worldEntity);
		UpdateTerrainState(world, worldEntity, width, depth, quadSize, cellSize, heights, pathingCodes, navMesh, navMeshQuery);
		return worldEntity;
	}
}