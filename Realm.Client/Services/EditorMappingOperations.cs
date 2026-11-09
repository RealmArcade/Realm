using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Terrain;
using Realm.EditorAPI;

namespace Realm.Client.Services;

/// <summary>
///     The mapping partial's operations that need no editor state: they read the live host and the registries through
///     <see cref="GameContext" /> only, so they can be called without an editor session and hold no lock. EditorService
///     keeps each public method as a forwarder, which is the surface IEditorAPI is implemented against.
/// </summary>
public static class EditorMappingOperations
{

	public static TerrainGridInfo GetTerrainGridInfo()
	{
		if (GameContext.Host?.GroundTerrain == null)
		{
			return new TerrainGridInfo(0, 0, EditableTerrain.DefaultQuadSize, EditableTerrain.DefaultCellSize, TerrainCell.TIER_HEIGHT);
		}

		RuntimeTerrain terrain = GameContext.Host.GroundTerrain;
		return new TerrainGridInfo(terrain.Width, terrain.Depth, terrain.QuadSize, terrain.CellSize, TerrainCell.TIER_HEIGHT);
	}


	public static void RebuildTerrain()
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


	public static void RefreshEditorAfterTerrainChange(bool regenerateMinimap)
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


	public static void MarkDocumentModified()
	{
		GameHost? host = GameContext.Host;
		if (host == null)
		{
			return;
		}

		host.EditorHasUnsavedChanges = true;
	}


	public static bool PlaceProp(string propId, float x, float y, float z, float rotationY, float scale)
	{
		if (string.IsNullOrWhiteSpace(propId) || GameContext.Host == null)
		{
			return false;
		}

		return GameContext.Host.SpawnPropExternalWithParams(propId, new Godot.Vector3(x, y, z), rotationY, scale) != null;
	}


	public static List<string> ResolvePropAssetIds(string[] keywords, bool includeResources)
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


	public static void CollectMatchingRegistryIds<TMetadata>(Dictionary<StringName, TMetadata> registry, string[] keywords, List<string> matches) where TMetadata : Realm.Shared.Metadata.PlaceableMetadata
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
