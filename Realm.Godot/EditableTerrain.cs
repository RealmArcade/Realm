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

	public override void SetGridVisible(bool visible)
	{
		if (_material != null)
		{
			_material.SetShaderParameter("grid_visible", visible);
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

		for (int z = 0; z <= newDepth; z++)
		{
			for (int x = 0; x <= newWidth; x++)
			{
				int oldX = x - offsetX;
				int oldZ = z - offsetZ;
				if (x < newWidth && z < newDepth)
				{
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
				if (oldSplatMap != null && oldX >= 0 && oldX < oldSplatMap.GetLength(0) && oldZ >= 0 && oldZ < oldSplatMap.GetLength(1))
				{
					newSplatMap[x, z] = oldSplatMap[oldX, oldZ];
				}
				else
				{
					newSplatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
				}

				if (newCliffSplatMap != null && oldCliffSplatMap != null)
				{
					if (oldX >= 0 && oldX < oldCliffSplatMap.GetLength(0) && oldZ >= 0 && oldZ < oldCliffSplatMap.GetLength(1))
					{
						newCliffSplatMap[x, z] = oldCliffSplatMap[oldX, oldZ];
					}
					else
					{
						newCliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(1);
					}
				}
			}
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

		bool splatChanged = false;
		if (SplatMap != null)
		{
			int sw = SplatMap.GetLength(0);
			int sd = SplatMap.GetLength(1);
			for (int z = 0; z < sd; z++)
			{
				for (int x = 0; x < sw; x++)
				{
					var s = SplatMap[x, z];
					int i0 = remap.TryGetValue(s.Index0, out int r0) ? r0 : s.Index0;
					int i1 = remap.TryGetValue(s.Index1, out int r1) ? r1 : s.Index1;
					int i2 = remap.TryGetValue(s.Index2, out int r2) ? r2 : s.Index2;
					int i3 = remap.TryGetValue(s.Index3, out int r3) ? r3 : s.Index3;
					if (i0 != s.Index0 || i1 != s.Index1 || i2 != s.Index2 || i3 != s.Index3)
					{
						SplatMap[x, z] = new TerrainSplatWeights
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
						splatChanged = true;
					}
				}
			}
		}

		if (CliffSplatMap != null)
		{
			int cw = CliffSplatMap.GetLength(0);
			int cd = CliffSplatMap.GetLength(1);
			for (int z = 0; z < cd; z++)
			{
				for (int x = 0; x < cw; x++)
				{
					var c = CliffSplatMap[x, z];
					int i0 = remap.TryGetValue(c.Index0, out int r0) ? r0 : c.Index0;
					int i1 = remap.TryGetValue(c.Index1, out int r1) ? r1 : c.Index1;
					int i2 = remap.TryGetValue(c.Index2, out int r2) ? r2 : c.Index2;
					int i3 = remap.TryGetValue(c.Index3, out int r3) ? r3 : c.Index3;
					if (i0 != c.Index0 || i1 != c.Index1 || i2 != c.Index2 || i3 != c.Index3)
					{
						CliffSplatMap[x, z] = new TerrainSplatWeights
						{
							Index0 = i0,
							Index1 = i1,
							Index2 = i2,
							Index3 = i3,
							Weight0 = c.Weight0,
							Weight1 = c.Weight1,
							Weight2 = c.Weight2,
							Weight3 = c.Weight3
						};
						splatChanged = true;
					}
				}
			}
		}

		if (splatChanged)
		{
			UpdateMeshAndPhysics(false, false);
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

		for (int z = 0; z < newDepth; z++)
		{
			for (int x = 0; x < newWidth; x++)
			{
				float nw = newGridHeights[x, z];
				float ne = newGridHeights[x + 1, z];
				float sw = newGridHeights[x, z + 1];
				float se = newGridHeights[x + 1, z + 1];

				int cellX0 = Math.Clamp((int)Math.Floor(x * (float)oldWidth / newWidth), 0, oldWidth - 1);
				int cellZ0 = Math.Clamp((int)Math.Floor(z * (float)oldDepth / newDepth), 0, oldDepth - 1);
				var oldCell = oldCells != null ? oldCells[cellX0, cellZ0] : default;
				newCells[x, z] = new TerrainCell(nw, ne, se, sw, oldCell.WaterMode, oldCell.WaterProfileIndex, oldCell.WaterHeight);

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

		for (int z = 0; z <= newDepth; z++)
		{
			for (int x = 0; x <= newWidth; x++)
			{
				int x0 = oldSplatMap != null ? Math.Clamp((int)Math.Floor(x * (float)(oldSplatMap.GetLength(0) - 1) / newWidth), 0, oldSplatMap.GetLength(0) - 1) : 0;
				int z0 = oldSplatMap != null ? Math.Clamp((int)Math.Floor(z * (float)(oldSplatMap.GetLength(1) - 1) / newDepth), 0, oldSplatMap.GetLength(1) - 1) : 0;

				newSplatMap[x, z] = oldSplatMap != null ? oldSplatMap[x0, z0] : TerrainSplatWeights.CreateSolid(0);

				if (newCliffSplatMap != null && oldCliffSplatMap != null)
				{
					int cx0 = Math.Clamp((int)Math.Floor(x * (float)(oldCliffSplatMap.GetLength(0) - 1) / newWidth), 0, oldCliffSplatMap.GetLength(0) - 1);
					int cz0 = Math.Clamp((int)Math.Floor(z * (float)(oldCliffSplatMap.GetLength(1) - 1) / newDepth), 0, oldCliffSplatMap.GetLength(1) - 1);
					newCliffSplatMap[x, z] = oldCliffSplatMap[cx0, cz0];
				}
			}
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
