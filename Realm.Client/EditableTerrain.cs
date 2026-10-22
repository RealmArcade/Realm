using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Textures;

namespace Realm.Client;

[GlobalClass]
public partial class EditableTerrain : RuntimeTerrain
{
	protected override bool IsRuntimeOnly => false;

	public override void ProcessAndSaveRawTexture(string rawPngPath, string outputRtexPath)
	{
		string globalInput = ProjectSettings.GlobalizePath(rawPngPath);
		string globalOutput = ProjectSettings.GlobalizePath(outputRtexPath);

		var result = TextureConverter.ProcessAndSaveTerrainTexture(globalInput, globalOutput);
		if (!result.Success)
		{
			GD.PrintErr($"Failed to process terrain texture '{rawPngPath}': {result.ErrorMessage}");
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

	private bool TryInitializeResizeOrScale(int newWidth, int newDepth, out int clampedWidth, out int clampedDepth, out TerrainState state)
	{
		clampedWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		clampedDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);
		state = default;

		if (GameContext.Host == null || GameContext.World == null || !GameContext.World.IsAlive(ECSContext.WorldEntity)) return false;
		if (!GameContext.World.Has<TerrainState>(ECSContext.WorldEntity)) return false;

		state = GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
		return true;
	}

		private void InitializeNewTerrainArrays(int newWidth, int newDepth, out TerrainCell[,] newCells, out int[,] newPathing, out TerrainSplatWeights[,] newSplatMap, out TerrainSplatWeights[,] newCliffSplatMap)
		{
			newCells = new TerrainCell[newWidth, newDepth];
			newPathing = new int[newWidth, newDepth];
			newSplatMap = new TerrainSplatWeights[newWidth + 1, newDepth + 1];
			newCliffSplatMap = CliffSplatMap != null ? new TerrainSplatWeights[newWidth + 1, newDepth + 1] : null;
		}

	private bool TryGetScaledTerrainArrays(int newWidth, int newDepth, out int finalWidth, out int finalDepth, out int oldWidth, out int oldDepth, out TerrainCell[,] oldCells, out int[,] oldPathing, out TerrainSplatWeights[,] oldSplatMap, out TerrainSplatWeights[,] oldCliffSplatMap, out TerrainCell[,] newCells, out int[,] newPathing, out TerrainSplatWeights[,] newSplatMap, out TerrainSplatWeights[,] newCliffSplatMap, out TerrainState state)
	{
		finalWidth = 0; finalDepth = 0; oldWidth = 0; oldDepth = 0;
		oldCells = null!; oldPathing = null!; oldSplatMap = null!; oldCliffSplatMap = null!;
		newCells = null!; newPathing = null!; newSplatMap = null!; newCliffSplatMap = null!;
		state = default;

		if (!TryInitializeResizeOrScale(newWidth, newDepth, out finalWidth, out finalDepth, out state)) return false;
		
		oldWidth = state.Width;
		oldDepth = state.Depth;
		oldCells = Cells;
		oldPathing = state.PathingCodes;
		oldSplatMap = SplatMap;
		oldCliffSplatMap = CliffSplatMap;

		InitializeNewTerrainArrays(finalWidth, finalDepth, out newCells, out newPathing, out newSplatMap, out newCliffSplatMap);
		return true;
	}

	public override void ResizeTerrain(int newWidth, int newDepth)
	{
		if (!TryGetScaledTerrainArrays(newWidth, newDepth, out newWidth, out newDepth, out var oldWidth, out var oldDepth, out var oldCells, out var oldPathing, out var oldSplatMap, out var oldCliffSplatMap, out var newCells, out var newPathing, out var newSplatMap, out var newCliffSplatMap, out var state)) return;

		int offsetX = (newWidth - oldWidth) / 2;
		int offsetZ = (newDepth - oldDepth) / 2;

		PopulateResizedCellsAndPathing(newWidth, newDepth, oldWidth, oldDepth, offsetX, offsetZ, oldCells, oldPathing, newCells, newPathing);
		PopulateResizedSplatMap(newWidth, newDepth, offsetX, offsetZ, oldSplatMap, newSplatMap, 0);
		if (newCliffSplatMap != null && oldCliffSplatMap != null)
		{
			PopulateResizedSplatMap(newWidth, newDepth, offsetX, offsetZ, oldCliffSplatMap, newCliffSplatMap, 1);
		}

		ApplyNewTerrainState(newWidth, newDepth, state.QuadSize, newCells, newPathing, newSplatMap, newCliffSplatMap);
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

				bool withinBounds = IsWithinOldBounds(oldX, oldZ, oldWidth, oldDepth);

				if (oldCells != null && withinBounds)
				{
					newCells[x, z] = oldCells[oldX, oldZ];
				}

				if (oldPathing != null && withinBounds)
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

	private static bool IsWithinOldBounds(int x, int z, int width, int depth)
	{
		return x >= 0 && x < width && z >= 0 && z < depth;
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
		if (!TryGetScaledTerrainArrays(newWidth, newDepth, out newWidth, out newDepth, out var oldWidth, out var oldDepth, out var oldCells, out var oldPathing, out var oldSplatMap, out var oldCliffSplatMap, out var newCells, out var newPathing, out var newSplatMap, out var newCliffSplatMap, out var state)) return;

		float[,] newGridHeights = CalculateScaledGridHeights(newWidth, newDepth, oldWidth, oldDepth, oldCells);
		PopulateScaledCellsAndPathing(newWidth, newDepth, oldWidth, oldDepth, newGridHeights, oldPathing, newCells, newPathing);

		ReconcileScaledWater(oldCells, oldWidth, oldDepth, newCells, newPathing, newWidth, newDepth);

		PopulateScaledSplatMaps(newWidth, newDepth, oldSplatMap, newSplatMap, 0);
		if (newCliffSplatMap != null && oldCliffSplatMap != null)
		{
			PopulateScaledSplatMaps(newWidth, newDepth, oldCliffSplatMap, newCliffSplatMap, 1);
		}

		ApplyNewTerrainState(newWidth, newDepth, state.QuadSize, newCells, newPathing, newSplatMap, newCliffSplatMap);
	}

	private bool ValidateAndClampDimensions(ref int newWidth, ref int newDepth)
	{
		if (GameContext.Host == null || GameContext.World == null || !GameContext.World.IsAlive(ECSContext.WorldEntity)) return false;
		if (!GameContext.World.Has<TerrainState>(ECSContext.WorldEntity)) return false;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);
		return true;
	}

	private void ApplySnapshotData(int newWidth, int newDepth, float quadSize, TerrainCell[,] calculatedCells, int[,] pathingCodes, TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,] cliffSplatMap)
	{
		int[,] clonedPathing = pathingCodes != null ? (int[,])pathingCodes.Clone() : new int[newWidth, newDepth];
		TerrainSplatWeights[,] clonedSplatMap = splatMap != null ? (TerrainSplatWeights[,])splatMap.Clone() : new TerrainSplatWeights[newWidth + 1, newDepth + 1];
		TerrainSplatWeights[,] clonedCliffSplatMap = cliffSplatMap != null ? (TerrainSplatWeights[,])cliffSplatMap.Clone() : null;

		ApplyNewTerrainState(newWidth, newDepth, quadSize, calculatedCells, clonedPathing, clonedSplatMap, clonedCliffSplatMap);
		UpdateWaterTransform();
	}

	private void ApplyNewTerrainState(int newWidth, int newDepth, float quadSize, TerrainCell[,] newCells, int[,] newPathing, TerrainSplatWeights[,] newSplatMap, TerrainSplatWeights[,] newCliffSplatMap)
	{
		ref var state = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);
		GameContext.World.Set(ECSContext.WorldEntity, new TerrainState(
			newWidth, newDepth, quadSize, state.CellSize,
			newCells, newPathing, state.NavMesh, state.NavMeshQuery
		));

		_localCells = newCells;
		_localPathingCodes = newPathing;
		SplatMap = newSplatMap;
		CliffSplatMap = newCliffSplatMap;

		if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", new Vector2(newWidth * quadSize, newDepth * quadSize));
		}

		CreateChunks();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}

	public override void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, TerrainCell[,] cells, int[,] pathingCodes, TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,] cliffSplatMap = null)
	{
		if (!ValidateAndClampDimensions(ref newWidth, ref newDepth)) return;
		TerrainCell[,] clonedCells = cells != null ? (TerrainCell[,])cells.Clone() : new TerrainCell[newWidth, newDepth];
		ApplySnapshotData(newWidth, newDepth, quadSize, clonedCells, pathingCodes, splatMap, cliffSplatMap);
	}

	public override void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, float[,] heights, int[,] pathingCodes, TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,] cliffSplatMap = null)
	{
		if (!ValidateAndClampDimensions(ref newWidth, ref newDepth)) return;
		float[,] clonedSource = (float[,])heights.Clone();
		var calculatedCells = TerrainState.CalculateCells(newWidth, newDepth, clonedSource);
		ApplySnapshotData(newWidth, newDepth, quadSize, calculatedCells, pathingCodes, splatMap, cliffSplatMap);
	}
}