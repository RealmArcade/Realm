using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Terrain;
using Realm.EditorAPI;

namespace Realm.Client.Services;

public partial class EditorService
{
	public TerrainGridInfo GetTerrainGridInfo()
	{
		if (GameContext.Host?.GroundTerrain == null)
		{
			return new TerrainGridInfo(0, 0, EditableTerrain.DefaultQuadSize, EditableTerrain.DefaultCellSize, TerrainCell.TIER_HEIGHT);
		}

		RuntimeTerrain terrain = GameContext.Host.GroundTerrain;
		return new TerrainGridInfo(terrain.Width, terrain.Depth, terrain.QuadSize, terrain.CellSize, TerrainCell.TIER_HEIGHT);
	}

	public TerrainMaterialPalette GetTerrainMaterialPalette()
	{
		int swatchCount = SwatchDisplayNames.Count;
		if (swatchCount <= 0)
		{
			return new TerrainMaterialPalette(0, 0, 0, 0);
		}

		var usedMaterials = new HashSet<int>();
		int water = ResolveMaterialIndex(new[] { "water", "ocean", "sea", "river", "lake", "swamp", "marsh", "trench", "mud" }, 0, usedMaterials);
		int ground = ResolveMaterialIndex(new[] { "grass", "moss", "jungle", "plain", "soil", "dirt", "forest", "meadow", "sand" }, EditorPaintTextureIndex, usedMaterials);
		int rock = ResolveMaterialIndex(new[] { "rock", "granite", "stone", "basin", "mountain", "cobble", "boulder" }, FindNextUnusedMaterial(2, usedMaterials), usedMaterials);
		int cliff = ResolveMaterialIndex(new[] { "cliff", "crag", "basalt", "scarp", "plateau" }, EditorCliffPaintTextureIndex, usedMaterials);

		return new TerrainMaterialPalette(ClampMaterialIndex(water), ClampMaterialIndex(ground), ClampMaterialIndex(rock), ClampMaterialIndex(cliff));
	}

	private int ResolveMaterialIndex(string[] keywords, int fallbackIndex, HashSet<int> usedMaterials)
	{
		for (int materialIndex = 0; materialIndex < SwatchDisplayNames.Count; materialIndex++)
		{
			string displayName = SwatchDisplayNames[materialIndex] ?? string.Empty;
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
		int swatchCount = SwatchDisplayNames.Count;
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
		if (SwatchDisplayNames.Count <= 0)
		{
			return 0;
		}

		return Math.Clamp(materialIndex, 0, SwatchDisplayNames.Count - 1);
	}

	public int GetSelectedCliffTextureIndex()
	{
		return ClampMaterialIndex(EditorCliffPaintTextureIndex);
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
			GD.PrintErr($"[EditorService] Generated terrain grid {width}x{depth} does not match the live grid {terrain.Width}x{terrain.Depth}.");
			return false;
		}

		int cellCount = width * depth;
		if (tiers.Length != cellCount || groundMaterials.Length != cellCount)
		{
			GD.PrintErr($"[EditorService] Generated terrain arrays do not match {cellCount} cells.");
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
		SetTerrainSplatMap(splatMap, cliffSplatMap);

		terrain.UpdateMeshAndPhysics(true, true);
		terrain.UpdatePathingTexture();
		GameContext.Host?.UpdatePathingOverlay();
		UpdateWatchedFileTimestamps();
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

	public void RebuildTerrain()
	{
		RuntimeTerrain? terrain = GameContext.Host?.GroundTerrain;
		if (terrain == null)
		{
			return;
		}

		terrain.UpdateMeshAndPhysics(true, true);
		terrain.UpdatePathingTexture();
		GameContext.Host?.UpdatePathingOverlay();
	}

	public void RefreshEditorAfterTerrainChange(bool regenerateMinimap)
	{
		GameHost? host = GameContext.Host;
		if (host == null)
		{
			return;
		}

		host.AlignAllEntitiesToTerrainExternal();
		host.RebuildCameraBoundsOverlay();
		GameContext.MapEditorHud?.UpdateCameraBoundsUI();
		host.UpdateGridOverlayVisibility();
		host.UpdatePathingOverlay();

		if (regenerateMinimap)
		{
			GameContext.MapEditorHud?.RegenerateMinimap();
		}
	}

	public void MarkDocumentModified()
	{
		GameHost? host = GameContext.Host;
		if (host == null)
		{
			return;
		}

		host.EditorHasUnsavedChanges = true;
	}

	public void ResetWorkspaceFileWatchTimestamps()
	{
		UpdateWatchedFileTimestamps();
	}

	public bool PlaceProp(string propId, float x, float y, float z, float rotationY, float scale)
	{
		if (string.IsNullOrWhiteSpace(propId) || GameContext.Host == null)
		{
			return false;
		}

		return GameContext.Host.SpawnPropExternalWithParams(propId, new Godot.Vector3(x, y, z), rotationY, scale) != null;
	}

	public List<string> ResolvePropAssetIds(string[] keywords, bool includeResources)
	{
		var matches = new List<string>();
		if (keywords == null || keywords.Length == 0)
		{
			return matches;
		}

		CollectMatchingRegistryIds(GameHost.PropRegistry, keywords, matches);
		if (includeResources)
		{
			CollectMatchingRegistryIds(GameHost.ResourceRegistry, keywords, matches);
		}

		matches.Sort(StringComparer.OrdinalIgnoreCase);
		return matches;
	}

	private static void CollectMatchingRegistryIds<TMetadata>(Dictionary<StringName, TMetadata> registry, string[] keywords, List<string> matches) where TMetadata : Realm.Shared.Metadata.PlaceableMetadata
	{
		foreach (KeyValuePair<StringName, TMetadata> entry in registry)
		{
			string templateId = entry.Key.ToString();
			TMetadata metadata = entry.Value;
			if (metadata == null)
			{
				continue;
			}

			if (MatchesAnyKeyword(templateId, keywords) ||
			    MatchesAnyKeyword(metadata.Name, keywords) ||
			    MatchesAnyKeyword(metadata.ModelPath, keywords))
			{
				if (!matches.Exists(existingId => string.Equals(existingId, templateId, StringComparison.OrdinalIgnoreCase)))
				{
					matches.Add(templateId);
				}
			}
		}
	}

	private static bool MatchesAnyKeyword(string? value, string[] keywords)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
		{
			if (value.Contains(keywords[keywordIndex], StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
