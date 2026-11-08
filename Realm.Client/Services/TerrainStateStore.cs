using System;
using Realm.Ecs;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

/// <summary>
///     Reads and replaces the terrain grid the simulation keeps on the world entity. The rule that matters — which
///     fields survive a replacement, and that a new grid's dimensions follow the arrays that came with it — lived in
///     two places (the node's setters and the editor's restore path); it lives here once.
/// </summary>
internal static class TerrainStateStore
{
	private const int DefaultDimension = 128;

	/// <summary>The live state, or the default one a world with no terrain answers with.</summary>
	internal static TerrainState Read()
	{
		if (GameContext.Host != null
		    && GameContext.World is { } world
		    && world.IsAlive(GameContext.Simulation.WorldEntity)
		    && world.Has<TerrainState>(GameContext.Simulation.WorldEntity))
		{
			return world.Get<TerrainState>(GameContext.Simulation.WorldEntity);
		}

		return new TerrainState(DefaultDimension, DefaultDimension, TerrainState.DefaultQuadSize, TerrainState.DefaultCellSize,
			(TerrainCell[,]?)null, null, null, null);
	}

	internal static void SetQuadSize(float quadSize) =>
		WriteScalar(state => new TerrainState(state.Width, state.Depth, quadSize, state.CellSize,
			state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery));

	internal static void SetCellSize(float cellSize) =>
		WriteScalar(state => new TerrainState(state.Width, state.Depth, state.QuadSize, cellSize,
			state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery));

	internal static void SetWidth(int width) =>
		WriteScalar(state => new TerrainState(width, state.Depth, state.QuadSize, state.CellSize,
			state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery));

	internal static void SetDepth(int depth) =>
		WriteScalar(state => new TerrainState(state.Width, depth, state.QuadSize, state.CellSize,
			state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery));

	/// <summary>
	///     Replaces one or both grids, taking the new dimensions from whichever array was supplied and keeping the
	///     other grid, the quad size, the cell size and the baked navmesh as they were.
	/// </summary>
	internal static void ReplaceGrid(TerrainCell[,]? cells, int[,]? pathingCodes) =>
		Write(state =>
		{
			int width = state.Width;
			int depth = state.Depth;

			if (cells != null)
			{
				width = cells.GetLength(0);
				depth = cells.GetLength(1);
			}
			else if (pathingCodes != null)
			{
				width = pathingCodes.GetLength(0);
				depth = pathingCodes.GetLength(1);
			}

			return new TerrainState(width, depth, state.QuadSize, state.CellSize,
				cells ?? state.Cells, pathingCodes ?? state.PathingCodes, state.NavMesh, state.NavMeshQuery);
		});

	/// <summary>Replaces the whole grid at a new size, as an editor restore does.</summary>
	internal static void ReplaceGrid(int width, int depth, float quadSize, TerrainCell[,] cells, int[,] pathingCodes) =>
		Write(state => new TerrainState(width, depth, quadSize, state.CellSize,
			cells, pathingCodes, state.NavMesh, state.NavMeshQuery));

	/// <summary>
	///     Hands the state to the navmesh baker and stores what it produced. The bake itself belongs to the
	///     simulation, so this only moves the component across the call.
	/// </summary>
	internal static void BakeNavMesh()
	{
		if (GameContext.Host == null
		    || GameContext.World is not { } world
		    || !world.IsAlive(GameContext.Simulation.WorldEntity)
		    || !world.Has<TerrainState>(GameContext.Simulation.WorldEntity))
		{
			return;
		}

		ref var state = ref world.Get<TerrainState>(GameContext.Simulation.WorldEntity);
		GameContext.Simulation.TerrainNavMeshService.BakeNavMesh(ref state);
		world.Set(GameContext.Simulation.WorldEntity, state);
	}

	/// <summary>Terrain data lives in the ECS world; asking to change it before one exists is a caller bug.</summary>
	private static EcsWorld World => GameContext.World
		?? throw new InvalidOperationException("Terrain state changed before a world existed.");

	private static void WriteScalar(Func<TerrainState, TerrainState> build)
	{
		ref var current = ref World.Get<TerrainState>(GameContext.Simulation.WorldEntity);
		World.Set(GameContext.Simulation.WorldEntity, build(current));
	}

	private static void Write(Func<TerrainState, TerrainState> build)
	{
		if (GameContext.Host == null
		    || GameContext.World is not { } world
		    || !world.IsAlive(GameContext.Simulation.WorldEntity)
		    || !world.Has<TerrainState>(GameContext.Simulation.WorldEntity))
		{
			return;
		}

		ref var current = ref world.Get<TerrainState>(GameContext.Simulation.WorldEntity);
		world.Set(GameContext.Simulation.WorldEntity, build(current));
	}
}
