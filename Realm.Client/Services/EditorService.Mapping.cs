using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Terrain;
using Realm.EditorAPI;

namespace Realm.Client.Services;

public partial class EditorService
{
	public TerrainGridInfo GetTerrainGridInfo() => EditorMappingOperations.GetTerrainGridInfo();

	public void RebuildTerrain() => EditorMappingOperations.RebuildTerrain();

	public void RefreshEditorAfterTerrainChange(bool regenerateMinimap) => EditorMappingOperations.RefreshEditorAfterTerrainChange(regenerateMinimap);

	public void MarkDocumentModified() => EditorMappingOperations.MarkDocumentModified();

	public bool PlaceProp(string propId, float x, float y, float z, float rotationY, float scale) =>
		EditorMappingOperations.PlaceProp(propId, x, y, z, rotationY, scale);

	public List<string> ResolvePropAssetIds(string[] keywords, bool includeResources) =>
		EditorMappingOperations.ResolvePropAssetIds(keywords, includeResources);

	public TerrainMaterialPalette GetTerrainMaterialPalette()
	{
		int swatchCount = Swatches.DisplayNames.Count;
		if (swatchCount <= 0)
		{
			return new TerrainMaterialPalette(0, 0, 0, 0);
		}

		var usedMaterials = new HashSet<int>();
		int water = ResolveMaterialIndex(new[] { "water", "ocean", "sea", "river", "lake", "swamp", "marsh", "trench", "mud" }, 0, usedMaterials);
		int ground = ResolveMaterialIndex(new[] { "grass", "moss", "jungle", "plain", "soil", "dirt", "forest", "meadow", "sand" }, Tools.PaintTextureIndex, usedMaterials);
		int rock = ResolveMaterialIndex(new[] { "rock", "granite", "stone", "basin", "mountain", "cobble", "boulder" }, FindNextUnusedMaterial(2, usedMaterials), usedMaterials);
		int cliff = ResolveMaterialIndex(new[] { "cliff", "crag", "basalt", "scarp", "plateau" }, Tools.CliffPaintTextureIndex, usedMaterials);

		return new TerrainMaterialPalette(ClampMaterialIndex(water), ClampMaterialIndex(ground), ClampMaterialIndex(rock), ClampMaterialIndex(cliff));
	}

	private int ResolveMaterialIndex(string[] keywords, int fallbackIndex, HashSet<int> usedMaterials)
	{
		for (int materialIndex = 0; materialIndex < Swatches.DisplayNames.Count; materialIndex++)
		{
			string displayName = Swatches.DisplayNames[materialIndex] ?? string.Empty;
			for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
			{
				if (displayName.Contains(keywords[keywordIndex], StringComparison.OrdinalIgnoreCase) && !usedMaterials.Contains(materialIndex))
				{
					usedMaterials.Add(materialIndex);
					return materialIndex;
				}
			}
		}

		int resolved = ClampMaterialIndex(fallbackIndex);
		if (usedMaterials.Contains(resolved))
		{
			resolved = FindNextUnusedMaterial(resolved, usedMaterials);
		}

		usedMaterials.Add(resolved);
		return resolved;
	}

	private int FindNextUnusedMaterial(int startIndex, HashSet<int> usedMaterials)
	{
		int swatchCount = Swatches.DisplayNames.Count;
		for (int offset = 0; offset < swatchCount; offset++)
		{
			int candidate = (startIndex + offset) % swatchCount;
			if (!usedMaterials.Contains(candidate))
			{
				return candidate;
			}
		}

		return ClampMaterialIndex(startIndex);
	}

	private int ClampMaterialIndex(int materialIndex)
	{
		if (Swatches.DisplayNames.Count <= 0)
		{
			return 0;
		}

		return Math.Clamp(materialIndex, 0, Swatches.DisplayNames.Count - 1);
	}

	public int GetSelectedCliffTextureIndex()
	{
		return ClampMaterialIndex(Tools.CliffPaintTextureIndex);
	}

	public void ResetMapToBlank()
	{
		ResetToBlankMapDomain();
	}

	public bool ApplyGeneratedTerrain(int width, int depth, ReadOnlySpan<sbyte> tiers, ReadOnlySpan<int> groundMaterials, int cliffMaterialIndex)
	{
		RuntimeTerrain? terrain = GameContext.Host?.GroundTerrain;
		if (terrain == null || width <= 0 || depth <= 0)
		{
			return false;
		}

		if (terrain.Width != width || terrain.Depth != depth)
		{
			GameLog.LogError($"[EditorService] Generated terrain grid {width}x{depth} does not match the live grid {terrain.Width}x{terrain.Depth}.");
			return false;
		}

		int cellCount = width * depth;
		if (tiers.Length != cellCount || groundMaterials.Length != cellCount)
		{
			GameLog.LogError($"[EditorService] Generated terrain arrays do not match {cellCount} cells.");
			return false;
		}

		int clampedCliffMaterial = ClampMaterialIndex(cliffMaterialIndex);
		var cells = new TerrainCell[width, depth];
		var pathingCodes = new int[width, depth];
		var splatMap = new TerrainSplatWeights[width + 1, depth + 1];
		var cliffSplatMap = new TerrainSplatWeights[width + 1, depth + 1];
		float[,] nodeHeights = BuildWeldedNodeHeights(width, depth, tiers);

		for (int z = 0; z < depth; z++)
		{
			int rowOffset = z * width;
			for (int x = 0; x < width; x++)
			{
				int cellIndex = rowOffset + x;
				var cell = new TerrainCell(nodeHeights[x, z], nodeHeights[x + 1, z], nodeHeights[x + 1, z + 1], nodeHeights[x, z + 1]);
				cells[x, z] = cell;
				pathingCodes[x, z] = RuntimeTerrain.GetDefaultPathingCode(WaterType.None);
			}
		}

		FillGeneratedSplatMaps(width, depth, groundMaterials, clampedCliffMaterial, splatMap, cliffSplatMap);

		terrain.Cells = cells;
		terrain.PathingCodes = pathingCodes;
		terrain.SplatMap = splatMap;
		terrain.CliffSplatMap = cliffSplatMap;
		Surface.AttachSplatMaps(splatMap, cliffSplatMap);

		terrain.UpdateMeshAndPhysics(true, true);
		terrain.UpdatePathingTexture();
		GameContext.Host?.UpdatePathingOverlay();
		EditorWorkspaceWatcher.UpdateWatchedFileTimestamps();
		return true;
	}

	private static float[,] BuildWeldedNodeHeights(int width, int depth, ReadOnlySpan<sbyte> tiers)
	{
		var nodeHeights = new float[width + 1, depth + 1];
		for (int nodeZ = 0; nodeZ <= depth; nodeZ++)
		{
			for (int nodeX = 0; nodeX <= width; nodeX++)
			{
				int shallowestBasinTier = int.MinValue;
				int lowestLandTier = int.MaxValue;

				for (int offsetZ = -1; offsetZ <= 0; offsetZ++)
				{
					int cellZ = nodeZ + offsetZ;
					if (cellZ < 0 || cellZ >= depth) continue;

					for (int offsetX = -1; offsetX <= 0; offsetX++)
					{
						int cellX = nodeX + offsetX;
						if (cellX < 0 || cellX >= width) continue;

						int tier = Math.Clamp(tiers[cellZ * width + cellX], (sbyte)-4, TerrainCell.MAX_MACRO_TIER);
						if (tier < 0)
						{
							shallowestBasinTier = Math.Max(shallowestBasinTier, tier);
						}
						else
						{
							lowestLandTier = Math.Min(lowestLandTier, tier);
						}
					}
				}

				int nodeTier = shallowestBasinTier != int.MinValue ? shallowestBasinTier : lowestLandTier;
				nodeHeights[nodeX, nodeZ] = nodeTier * TerrainCell.TIER_HEIGHT;
			}
		}

		return nodeHeights;
	}

	private static void FillGeneratedSplatMaps(int width, int depth, ReadOnlySpan<int> groundMaterials, int cliffMaterialIndex, TerrainSplatWeights[,] splatMap, TerrainSplatWeights[,] cliffSplatMap)
	{
		for (int z = 0; z <= depth; z++)
		{
			int sourceZ = Math.Min(z, depth - 1);
			for (int x = 0; x <= width; x++)
			{
				int sourceX = Math.Min(x, width - 1);
				int materialIndex = groundMaterials[sourceZ * width + sourceX];
				splatMap[x, z] = TerrainSplatWeights.CreateSolid(materialIndex);
				cliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(cliffMaterialIndex);
			}
		}
	}

	public void ResetWorkspaceFileWatchTimestamps()
	{
		EditorWorkspaceWatcher.UpdateWatchedFileTimestamps();
	}
}
