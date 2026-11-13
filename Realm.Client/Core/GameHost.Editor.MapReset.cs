using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

namespace Realm.Client.Core;
public partial class GameHost
{
	public void ClearMapEntirely()
	{
		if (GroundTerrain == null) return;

		ClearAllEntities();
		ClearAllNodesAndTerrain();

		_editorObjectSelection.Clear();
		GameContext.EditorService?.ResetAllState();
		HideSelectionHighlight();

		bool temp = false;
		GameContext.EditorService?.ClearHistoryAndState(ref temp);
		EditorHasUnsavedChanges = temp;

		MapCameraBounds resetCameraBounds = GroundTerrain != null
			? MapCameraBounds.FromTerrain(GroundTerrain.Width, GroundTerrain.Depth, GroundTerrain.QuadSize)
			: MapCameraBounds.LegacyDefaults;
		GameContext.EditorService?.SetCameraBounds(GameContext.Simulation.WorldEntity, resetCameraBounds);
		GameContext.MapEditorHud?.UpdateCameraBoundsUI();
		RebuildCameraBoundsOverlay();

		GameContext.EditorService?.ClearEditorCoordinates();
		RebuildAllCoordinatePersistentMeshes();
		HideCoordinateSelectionOutline();

		RefreshUIAndExternalState();
	}

	private void ClearAllEntities()
	{
		var unitsCopy = new List<Unit3D>(AllUnits);
		foreach (var unit in unitsCopy)
		{
			if (GodotObject.IsInstanceValid(unit)) DeleteNodeExternal(unit);
		}

		SelectedUnits.Clear();
		AllUnits.Clear();
		ClearAllBuildQueueGhosts();
		AllProps.Clear();
		GameContext.PropMultiMesh?.Clear();
		AllDecals.Clear();
		AllVfx.Clear();
		ClearPlacedLootItems();
		GameContext.FXService.ClearActivePings();
		EntityToUnit3D.Clear();
		EntityIdToUnit3D.Clear();
		EntityToProp3D.Clear();
		EntityToVfx3D.Clear();

		if (_controlGroups != null)
		{
			for (int i = 0; i < _controlGroups.Length; i++)
			{
				_controlGroups[i]?.Clear();
			}
		}
	}

	private void ClearAllNodesAndTerrain()
	{
		var childrenCopy = new List<Node>(GetChildren());
		foreach (var child in childrenCopy)
		{
			if ((child is Prop3D prop && GodotObject.IsInstanceValid(prop)) ||
			    (child is Decal decal && GodotObject.IsInstanceValid(decal)) ||
			    (child is ProceduralVfxInstance3D vfx && GodotObject.IsInstanceValid(vfx)))
			{
				DeleteNodeExternal(child);
			}
		}

		if (GroundTerrain != null)
		{
			ResetTerrainData();
		}
	}

	private void ResetTerrainData()
	{
		var ground = RequireGroundTerrain();
		int width = ground.Width;
		int depth = ground.Depth;

		InitializeTerrainArrays(width, depth);
		PopulateTerrainCells(width, depth);
		PopulateSplatMaps(width, depth);
		UpdateEcsTerrainState();

		ground.UpdateMeshAndPhysics(true, true);
		ground.UpdatePathingTexture();
		UpdatePathingOverlay();
	}

	private void InitializeTerrainArrays(int width, int depth)
	{
		var ground = RequireGroundTerrain();
		TerrainCell[,] cells = ground.Cells;
		if (cells == null || cells.GetLength(0) != width || cells.GetLength(1) != depth)
			ground.Cells = new TerrainCell[width, depth];

		TerrainSplatWeights[,] splatMap = ground.SplatMap;
		if (splatMap == null || splatMap.GetLength(0) < width + 1 || splatMap.GetLength(1) < depth + 1)
			ground.SplatMap = new TerrainSplatWeights[width + 1, depth + 1];

		TerrainSplatWeights[,]? cliffSplatMap = ground.CliffSplatMap;
		if (cliffSplatMap == null || cliffSplatMap.GetLength(0) < width + 1 || cliffSplatMap.GetLength(1) < depth + 1)
			ground.CliffSplatMap = new TerrainSplatWeights[width + 1, depth + 1];

		int[,] pathingCodes = ground.PathingCodes;
		if (pathingCodes == null || pathingCodes.GetLength(0) != width || pathingCodes.GetLength(1) != depth)
			ground.PathingCodes = new int[width, depth];
	}

	private void PopulateTerrainCells(int width, int depth)
	{
		var ground = RequireGroundTerrain();
		int defaultPathingCode = EditableTerrain.GetDefaultPathingCode(WaterType.None);
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				ground.Cells[x, z] = new TerrainCell(0.0f);
				ground.PathingCodes[x, z] = defaultPathingCode;
			}
		}
	}


	private void PopulateSplatMaps(int width, int depth)
	{
		if (GroundTerrain is not { SplatMap: { } splatMap, CliffSplatMap: { } cliffSplatMap }) return;

		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				splatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
				cliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(1);
			}
		}
	}

	private void UpdateEcsTerrainState()
	{
		if (GameContext.World != null && GameContext.World.IsAlive(GameContext.Simulation.WorldEntity) && GameContext.World.Has<TerrainState>(GameContext.Simulation.WorldEntity))
		{
			var ground = RequireGroundTerrain();
			ref var ts = ref GameContext.World.Get<TerrainState>(GameContext.Simulation.WorldEntity);
			ts.Cells = ground.Cells;
			ts.PathingCodes = ground.PathingCodes;
			GameContext.World.Set(GameContext.Simulation.WorldEntity, ts);
		}
	}

	private void RefreshUIAndExternalState()
	{
		GameContext.MapEditorHud?.RefreshCoordinateListExternal();
		GameContext.MapEditorHud?.ResetEditingDirectorySyncState();
		GameContext.MapEditorHud?.GenerateVSCodeFilesExternal();
		GameContext.MapEditorHud?.ReadMetadataAndRefreshTextures();
		GameContext.MapEditorHud?.ViewLoadMapProperties();
		GameContext.MapEditorHud?.UpdateMapNameHeader();
		GameContext.MapEditorHud?.RefreshSavedDirectoryHash();
		GameContext.MapEditorHud?.ShowFeedbackExternal("Map reset: cleared all entities & terrain");
		GameContext.MapEditorHud?.RegenerateMinimap();
	}


}
