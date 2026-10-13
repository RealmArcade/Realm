using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using DotRecast.Detour;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

public class WorldInitService
{
	public WorldInitService()
	{
	}

	private void AddOrSet<T>(Entity entity, T component) where T : struct
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
		GameHost.Instance?.LoadModelYOffsetsFromMetadataJson();

		AddOrSet(worldEntity, new WorldState(0f, 0, 0f, true));
		AddOrSet(worldEntity, new ReplayState(0, 500f, 400f, 200f));
		AddOrSet(worldEntity, new NetworkState(1, 0f, 0, -1, -1, false, 0, 1));
		AddOrSet(worldEntity, new NetworkMappingState(new(), new(), new()));
		if (!GameContext.World.Has<EditorState>(worldEntity))
		{
			GameContext.World.Add(worldEntity, new EditorState(true, 3.0f, -95.0f, 95.0f, -95.0f, 125.0f, "", false));
		}

		AddOrSet(worldEntity, new InputState(0, null, null, null, false));
		AddOrSet(worldEntity, new VFXQueue(new List<VFXRequest>()));
		AddOrSet(worldEntity, new CameraState
		{
			MoveSpeed = 35.0f,
			ZoomSpeed = 10.0f,
			MinZoom = 10.0f,
			MaxZoom = 60.0f,
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

		AddOrSet(worldEntity, new ShroudState(new byte[32, 32], "VisionShroud"));
		AddOrSet(worldEntity, new WeatherState("clear", 0f));
		AddOrSet(worldEntity, new SpectatorPerspective(-1));
		AddOrSet(worldEntity, new CountdownState(false, 0f, ""));
		AddOrSet(worldEntity, new LeaderboardState(false, "", new Dictionary<string, string>()));
		AddOrSet(worldEntity, new ScriptZonesState(new List<ZoneBounds>()));

		AddOrSet(worldEntity, new ScriptPlayersState(CreateDefaultPlayers()));
		AddOrSet(worldEntity, new CombatAlertState(0f));
	}

	private static ScriptPlayer[] CreateDefaultPlayers()
	{
		var players = new ScriptPlayer[12];
		for (int i = 0; i < 12; i++)
		{
			players[i] = new ScriptPlayer
			{
				Gold = i == 0 ? 500f : 0f,
				Wood = i == 0 ? 400f : 0f,
				Active = i == 0,
				Name = $"Player {i + 1}",
				KillCount = 0
			};
		}

		return players;
	}

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