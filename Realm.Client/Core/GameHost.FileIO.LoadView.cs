using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Realm.Ecs;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Maps;
using Realm.Shared.Terrain;
using Realm.Shared.Textures;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Core;


public partial class GameHost
{
	private void InitializeDefaultTerrainState(int defaultWidth, int defaultDepth)
	{
		GameContext.EditorService.InitializeDefaultTerrainStateDomain(defaultWidth, defaultDepth);
	}

	private void ResetGroundTerrainView(int defaultWidth, int defaultDepth)
	{
		if (GroundTerrain == null) return;

		GameContext.EditorService.ResetTerrainSplatMap(GroundTerrain.SplatMap, defaultWidth, defaultDepth);

		GroundTerrain.UpdateMeshAndPhysics();
		GroundTerrain.UpdatePathingTexture();
	}

	private void ResetEditorUIView()
	{
		if (GameContext.MapEditorHud != null)
		{
			GameContext.MapEditorHud.UpdateBlockModeExternal(true);
			GameContext.MapEditorHud.UpdateBlockLevelHeightExternal(2.0f);
			GameContext.MapEditorHud.UpdateCameraBoundsUI();
		}

		UpdateCameraBoundsOverlayVisibility();
		UpdateGridOverlayVisibility();
		UpdatePathingOverlay();

		GameContext.EditorService.ClearEditorCoordinates();
		RebuildAllCoordinatePersistentMeshes();

		if (GameContext.MapEditorHud != null)
		{
			GameContext.MapEditorHud.RefreshCoordinateListExternal();
		}
	}

	private void EnsureGroundTerrainExistsView()
	{
		if (GroundTerrain != null) return;

		var toRemove = new List<Node>();
		foreach (var child in GetChildren())
		{
			if (child.Name.ToString().StartsWith("Ground"))
			{
				toRemove.Add(child);
			}
		}

		foreach (var child in toRemove)
		{
			RemoveChild(child);
			child.QueueFree();
		}

		var terrainNode = GameContext.SessionState.IsMapEditorMode ? (RuntimeTerrain)new EditableTerrain() : new RuntimeTerrain();
		terrainNode.Name = "Ground";
		AddChild(terrainNode);
		GroundTerrain = terrainNode;
	}

	private void ApplyEditorStateView(EditorState editor)
	{
		GameContext.MapEditorHud?.UpdateBlockModeExternal(editor.BlockMode);
		GameContext.MapEditorHud?.UpdateBlockLevelHeightExternal(editor.BlockLevelHeight);
		GameContext.MapEditorHud?.UpdateCameraBoundsUI();
		UpdateCameraBoundsOverlayVisibility();
		UpdateGridOverlayVisibility();
		UpdatePathingOverlay();

		if (!string.IsNullOrEmpty(editor.SkyboxPath))
		{
			SetSkyboxTexture(editor.SkyboxPath);
			GameContext.MapEditorHud?.UpdateSelectedSkyboxExternal(editor.SkyboxPath);
		}
	}

	private void ApplyTerrainStateView(TerrainState terrain)
	{
		if (GroundTerrain is not { } ground) return;

		if (terrain.Cells != null)
		{
			ground.Cells = (TerrainCell[,])terrain.Cells.Clone();
		}

		// The size is read after the cells land, because the cells are what states the map's size. Reading it first on a
		// ground built from nothing but its node returned the default size and dropped the pathing codes of every map
		// that is not that size.
		int width = ground.Width;
		int depth = ground.Depth;

		if (terrain.PathingCodes != null && terrain.PathingCodes.Length == width * depth)
		{
			GameContext.SaveLoadService.CopyPathingCodes(terrain.PathingCodes, ground.PathingCodes, width, depth);
		}
	}

	private void LoadSplatMapsView(string absolutePath)
	{
		var ground = RequireGroundTerrain();
		int splatW = ground.Width + 1;
		int splatD = ground.Depth + 1;

		TerrainSplatWeights[,]? splatMap = Path.GetDirectoryName(absolutePath) is { } directory
			? MapTerrainFiles.ReadSplatMap(directory, MapTerrainFiles.SplatPrefix, splatW, splatD)
			: null;

		if (splatMap == null)
		{
			LoadSplatMapFallbackView(splatW, splatD);
			return;
		}

		ground.SplatMap = splatMap;
	}

	private void LoadSplatMapFallbackView(int splatW, int splatD)
	{
		var ground = RequireGroundTerrain();
		ground.SplatMap = GameContext.SaveLoadService.GenerateFallbackSplatMap(splatW, splatD, ground.Width, ground.Depth);
	}

	private void LoadCliffSplatMapsView(string absolutePath)
	{
		if (GroundTerrain is not { } ground) return;

		int targetCliffW = ground.Width + 1;
		int targetCliffD = ground.Depth + 1;
		TerrainSplatWeights[,]? cliffSplatMap = Path.GetDirectoryName(absolutePath) is { } directory
			? MapTerrainFiles.ReadSplatMap(directory, MapTerrainFiles.CliffSplatPrefix, targetCliffW, targetCliffD)
			: null;

		if (cliffSplatMap != null)
		{
			ground.CliffSplatMap = cliffSplatMap;
			return;
		}

		ground.CliffSplatMap = new TerrainSplatWeights[targetCliffW, targetCliffD];
		for (int z = 0; z < targetCliffD; z++)
		{
			for (int x = 0; x < targetCliffW; x++)
			{
				ground.CliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(1);
			}
		}
	}
}
