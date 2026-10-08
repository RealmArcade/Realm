using Arch.Core;
using Realm.Ecs.Components.Terrain;
using Realm.Shared.Textures;
using DotRecast.Detour;
using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public partial class EditableTerrain : RuntimeTerrain
{
	public new static EditableTerrain Instance => RuntimeTerrain.Instance as EditableTerrain;

	public EditableTerrain() : base()
	{
	}

	protected override bool IsRuntimeOnly => false;

	public override void ProcessAndSaveRawTexture(string rawPngPath, string outputRtexPath)
	{
		string globalInput = Godot.ProjectSettings.GlobalizePath(rawPngPath);
		string globalOutput = Godot.ProjectSettings.GlobalizePath(outputRtexPath);

		var result = TextureConverter.ProcessAndSaveTerrainTexture(globalInput, globalOutput);
		if (!result.Success)
		{
			Godot.GD.PrintErr($"Failed to process terrain texture '{rawPngPath}': {result.ErrorMessage}");
			throw new InvalidOperationException($"Failed to process terrain texture: {result.ErrorMessage}");
		}
	}

	public override void SetPathingVisible(bool visible)
	{
		if (_material != null)
		{
			_material.SetShaderParameter("pathing_visible", visible);
		}
	}

	public override void SetWireframeMode(bool enabled)
	{
		Viewport viewport = GetViewport();
		if (viewport != null)
		{
			viewport.DebugDraw = enabled ? Viewport.DebugDrawEnum.Wireframe : Viewport.DebugDrawEnum.Disabled;
		}
	}

	public override void ToggleWireframeMode()
	{
		Viewport viewport = GetViewport();
		if (viewport != null)
		{
			bool isWireframe = viewport.DebugDraw == Viewport.DebugDrawEnum.Wireframe;
			viewport.DebugDraw = isWireframe ? Viewport.DebugDrawEnum.Disabled : Viewport.DebugDrawEnum.Wireframe;
		}
	}

	public override void UpdatePathingTexture()
	{
		if (_material == null || PathingCodes == null) return;
		
		int w = Width;
		int d = Depth;
		_material.SetShaderParameter("terrain_size", new Vector2(w * QuadSize, d * QuadSize));
		_material.SetShaderParameter("grid_spacing", QuadSize);
		var img = Image.CreateEmpty(w, d, false, Image.Format.Rgba8);
		
		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				int code = PathingCodes[x, z];
				img.SetPixel(x, z, new Color(code / 255.0f, 0f, 0f, 0f));
			}
		}
		
		var tex = ImageTexture.CreateFromImage(img);
		_material.SetShaderParameter("pathing_texture", tex);
	}

	public override void ResizeTerrain(int newWidth, int newDepth)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
		
		int oldWidth = state.Width;
		int oldDepth = state.Depth;
		var oldCells = Cells;
		int[,] oldPathing = state.PathingCodes;
		TerrainSplatWeights[,] oldSplatMap = SplatMap;

		TerrainSplatWeights[,] oldCliffSplatMap = CliffSplatMap;

		var newCells = new TerrainCell[newWidth, newDepth];
		int[,] newPathing = new int[newWidth, newDepth];
		TerrainSplatWeights[,] newSplatMap = new TerrainSplatWeights[newWidth + 1, newDepth + 1];
		TerrainSplatWeights[,] newCliffSplatMap = oldCliffSplatMap != null ? new TerrainSplatWeights[newWidth + 1, newDepth + 1] : null;

		int offsetX = (newWidth - oldWidth) / 2;
		int offsetZ = (newDepth - oldDepth) / 2;

		PopulateResizedCellsAndPathing(newWidth, newDepth, oldWidth, oldDepth, offsetX, offsetZ, oldCells, oldPathing, newCells, newPathing);
		PopulateResizedSplatMap(newWidth, newDepth, offsetX, offsetZ, oldSplatMap, newSplatMap, 0);
		if (newCliffSplatMap != null && oldCliffSplatMap != null)
		{
			PopulateResizedSplatMap(newWidth, newDepth, offsetX, offsetZ, oldCliffSplatMap, newCliffSplatMap, 1);
		}

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, state.QuadSize, state.CellSize,
			newCells, newPathing, state.NavMesh, state.NavMeshQuery
		));

		_localCells = newCells;
		_localPathingCodes = newPathing;
		SplatMap = newSplatMap;
		CliffSplatMap = newCliffSplatMap;
		
		if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", new Vector2(newWidth * state.QuadSize, newDepth * state.QuadSize));
		}

		CreateChunks();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}

	public override void RemapSplatIndices(IReadOnlyDictionary<int, int> remap)
	{
		if (remap == null || remap.Count == 0) return;

		bool splatChanged = RemapSplatArray(SplatMap, remap) | RemapSplatArray(CliffSplatMap, remap);

		if (splatChanged)
		{
			UpdateMeshAndPhysics(false, false);
		}
	}

	private bool RemapSplatArray(TerrainSplatWeights[,] splatArray, IReadOnlyDictionary<int, int> remap)
	{
		if (splatArray == null) return false;

		bool splatChanged = false;
		int width = splatArray.GetLength(0);
		int depth = splatArray.GetLength(1);

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				splatChanged |= RemapSingleSplat(splatArray, x, z, remap);
			}
		}

		return splatChanged;
	}

	private bool RemapSingleSplat(TerrainSplatWeights[,] splatArray, int x, int z, IReadOnlyDictionary<int, int> remap)
	{
		var s = splatArray[x, z];
		int i0 = remap.TryGetValue(s.Index0, out int r0) ? r0 : s.Index0;
		int i1 = remap.TryGetValue(s.Index1, out int r1) ? r1 : s.Index1;
		int i2 = remap.TryGetValue(s.Index2, out int r2) ? r2 : s.Index2;
		int i3 = remap.TryGetValue(s.Index3, out int r3) ? r3 : s.Index3;

		if (i0 == s.Index0 && i1 == s.Index1 && i2 == s.Index2 && i3 == s.Index3)
		{
			return false;
		}

		splatArray[x, z] = new TerrainSplatWeights
		{
			Index0 = i0,
			Index1 = i1,
			Index2 = i2,
			Index3 = i3,
			Weight0 = s.Weight0,
			Weight1 = s.Weight1,
			Weight2 = s.Weight2,
			Weight3 = s.Weight3
		};

		return true;
	}

	private float[,] CalculateScaledGridHeights(int newWidth, int newDepth, int oldWidth, int oldDepth, TerrainCell[,] oldCells)
	{
		float[,] newGridHeights = new float[newWidth + 1, newDepth + 1];
		for (int vz = 0; vz <= newDepth; vz++)
		{
			for (int vx = 0; vx <= newWidth; vx++)
			{
				int oldVx = Math.Clamp((int)Math.Round(vx * (float)oldWidth / newWidth), 0, oldWidth);
				int oldVz = Math.Clamp((int)Math.Round(vz * (float)oldDepth / newDepth), 0, oldDepth);
				newGridHeights[vx, vz] = GetGridNodeHeight(oldVx, oldVz, oldCells, oldWidth, oldDepth);
			}
		}
		return newGridHeights;
	}

	private void PopulateScaledCellsAndPathing(int newWidth, int newDepth, int oldWidth, int oldDepth, float[,] newGridHeights, int[,] oldPathing, TerrainCell[,] newCells, int[,] newPathing)
	{
		for (int z = 0; z < newDepth; z++)
		{
			for (int x = 0; x < newWidth; x++)
			{
				float nw = newGridHeights[x, z];
				float ne = newGridHeights[x + 1, z];
				float sw = newGridHeights[x, z + 1];
				float se = newGridHeights[x + 1, z + 1];

				newCells[x, z] = new TerrainCell(nw, ne, se, sw);

				int cellX0 = Math.Clamp((int)Math.Floor(x * (float)oldWidth / newWidth), 0, oldWidth - 1);
				int cellZ0 = Math.Clamp((int)Math.Floor(z * (float)oldDepth / newDepth), 0, oldDepth - 1);

				if (oldPathing != null)
				{
					newPathing[x, z] = oldPathing[cellX0, cellZ0];
				}
				else
				{
					newPathing[x, z] = GetDefaultPathingCode(newCells[x, z]);
				}
			}
		}
	}

	private void PopulateScaledSplatMaps(int newWidth, int newDepth, TerrainSplatWeights[,] oldSplatMap, TerrainSplatWeights[,] newSplatMap, int defaultIndex)
	{
		for (int z = 0; z <= newDepth; z++)
		{
			for (int x = 0; x <= newWidth; x++)
			{
				if (oldSplatMap != null)
				{
					int x0 = Math.Clamp((int)Math.Floor(x * (float)(oldSplatMap.GetLength(0) - 1) / newWidth), 0, oldSplatMap.GetLength(0) - 1);
					int z0 = Math.Clamp((int)Math.Floor(z * (float)(oldSplatMap.GetLength(1) - 1) / newDepth), 0, oldSplatMap.GetLength(1) - 1);
					newSplatMap[x, z] = oldSplatMap[x0, z0];
				}
				else
				{
					newSplatMap[x, z] = TerrainSplatWeights.CreateSolid(defaultIndex);
				}
			}
		}
	}

	private void PopulateResizedCellsAndPathing(int newWidth, int newDepth, int oldWidth, int oldDepth, int offsetX, int offsetZ, TerrainCell[,] oldCells, int[,] oldPathing, TerrainCell[,] newCells, int[,] newPathing)
	{
		for (int z = 0; z < newDepth; z++)
		{
			for (int x = 0; x < newWidth; x++)
			{
				int oldX = x - offsetX;
				int oldZ = z - offsetZ;

				if (oldCells != null && oldX >= 0 && oldX < oldWidth && oldZ >= 0 && oldZ < oldDepth)
				{
					newCells[x, z] = oldCells[oldX, oldZ];
				}
				if (oldPathing != null && oldX >= 0 && oldX < oldWidth && oldZ >= 0 && oldZ < oldDepth)
				{
					newPathing[x, z] = oldPathing[oldX, oldZ];
				}
				else
				{
					newPathing[x, z] = GetDefaultPathingCode(newCells[x, z]);
				}
			}
		}
	}

	private void PopulateResizedSplatMap(int newWidth, int newDepth, int offsetX, int offsetZ, TerrainSplatWeights[,] oldSplatMap, TerrainSplatWeights[,] newSplatMap, int defaultSolidIndex)
	{
		for (int z = 0; z <= newDepth; z++)
		{
			for (int x = 0; x <= newWidth; x++)
			{
				int oldX = x - offsetX;
				int oldZ = z - offsetZ;

				if (oldSplatMap != null && oldX >= 0 && oldX < oldSplatMap.GetLength(0) && oldZ >= 0 && oldZ < oldSplatMap.GetLength(1))
				{
					newSplatMap[x, z] = oldSplatMap[oldX, oldZ];
				}
				else
				{
					newSplatMap[x, z] = TerrainSplatWeights.CreateSolid(defaultSolidIndex);
				}
			}
		}
	}

	public override void ScaleTerrainData(int newWidth, int newDepth)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);

		int oldWidth = state.Width;
		int oldDepth = state.Depth;
		var oldCells = Cells;
		int[,] oldPathing = state.PathingCodes;
		TerrainSplatWeights[,] oldSplatMap = SplatMap;
		TerrainSplatWeights[,] oldCliffSplatMap = CliffSplatMap;

		var newCells = new TerrainCell[newWidth, newDepth];
		int[,] newPathing = new int[newWidth, newDepth];
		TerrainSplatWeights[,] newSplatMap = new TerrainSplatWeights[newWidth + 1, newDepth + 1];
		TerrainSplatWeights[,] newCliffSplatMap = oldCliffSplatMap != null ? new TerrainSplatWeights[newWidth + 1, newDepth + 1] : null;

		float[,] newGridHeights = CalculateScaledGridHeights(newWidth, newDepth, oldWidth, oldDepth, oldCells);
		PopulateScaledCellsAndPathing(newWidth, newDepth, oldWidth, oldDepth, newGridHeights, oldPathing, newCells, newPathing);

		ReconcileScaledWater(oldCells, oldWidth, oldDepth, newCells, newPathing, newWidth, newDepth);

		PopulateScaledSplatMaps(newWidth, newDepth, oldSplatMap, newSplatMap, 0);
		if (newCliffSplatMap != null && oldCliffSplatMap != null)
		{
			PopulateScaledSplatMaps(newWidth, newDepth, oldCliffSplatMap, newCliffSplatMap, 1);
		}

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, state.QuadSize, state.CellSize,
			newCells, newPathing, state.NavMesh, state.NavMeshQuery
		));

		_localCells = newCells;
		_localPathingCodes = newPathing;
		SplatMap = newSplatMap;
		CliffSplatMap = newCliffSplatMap;
		
		if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", new Vector2(newWidth * state.QuadSize, newDepth * state.QuadSize));
		}

		CreateChunks();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}

	public override void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, TerrainCell[,] cells, int[,] pathingCodes, TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,] cliffSplatMap = null)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);

		TerrainCell[,] clonedCells = cells != null ? (TerrainCell[,])cells.Clone() : new TerrainCell[newWidth, newDepth];
		int[,] clonedPathing = pathingCodes != null ? (int[,])pathingCodes.Clone() : new int[newWidth, newDepth];
		TerrainSplatWeights[,] clonedSplatMap = splatMap != null ? (TerrainSplatWeights[,])splatMap.Clone() : new TerrainSplatWeights[newWidth + 1, newDepth + 1];
		TerrainSplatWeights[,] clonedCliffSplatMap = cliffSplatMap != null ? (TerrainSplatWeights[,])cliffSplatMap.Clone() : null;

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, quadSize, state.CellSize,
			clonedCells, clonedPathing, state.NavMesh, state.NavMeshQuery
		));

		_localCells = clonedCells;
		_localPathingCodes = clonedPathing;
		SplatMap = clonedSplatMap;
		CliffSplatMap = clonedCliffSplatMap;

		CreateChunks();
		UpdateWaterTransform();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}

	public override void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, float[,] heights, int[,] pathingCodes, TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,] cliffSplatMap = null)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);

		float[,] clonedSource = (float[,])heights.Clone();
		int[,] clonedPathing = pathingCodes != null ? (int[,])pathingCodes.Clone() : new int[newWidth, newDepth];
		TerrainSplatWeights[,] clonedSplatMap = splatMap != null ? (TerrainSplatWeights[,])splatMap.Clone() : new TerrainSplatWeights[newWidth + 1, newDepth + 1];
		TerrainSplatWeights[,] clonedCliffSplatMap = cliffSplatMap != null ? (TerrainSplatWeights[,])cliffSplatMap.Clone() : null;

		var calculatedCells = TerrainState.CalculateCells(newWidth, newDepth, clonedSource);

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, quadSize, state.CellSize,
			calculatedCells, clonedPathing, state.NavMesh, state.NavMeshQuery
		));

		_localCells = calculatedCells;
		_localPathingCodes = clonedPathing;
		SplatMap = clonedSplatMap;
		CliffSplatMap = clonedCliffSplatMap;

		CreateChunks();
		
		UpdateWaterTransform();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}
}
