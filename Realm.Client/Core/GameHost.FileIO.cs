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
	public void SaveMapToFile(string customPath = "", bool performReload = true, Action<MapTerrainSaveRequest>? terrainWriter = null)
	{
		if (GroundTerrain == null) return;

		if (terrainWriter == null)
		{
			GameContext.MapAutosaveService.FlushPendingWrites(TimeSpan.FromSeconds(3));
		}

		var savedState = CaptureEditorState();
		string[] splatData = GetSplatData();
		var unitsData = GetUnitsData();
		var propsData = GetPropsData();
		var decalsData = GetDecalsData();
		var coordinatesData = GetCoordinatesData();

		string currentMapDirectory = GameContext.MapStorageService.CurrentMapDirectory;
		string defaultSaveDirectory = !string.IsNullOrEmpty(currentMapDirectory) ? currentMapDirectory : MapWorkspaceService.GetDefaultMapFolderGlobalPath();
		string path = string.IsNullOrEmpty(customPath) ? Path.Combine(defaultSaveDirectory, "terrain.json") : customPath;
		string absolutePath = ProjectSettings.GlobalizePath(path);
		GameContext.MapStorageService.CurrentMapDirectory = Path.GetDirectoryName(absolutePath) ?? string.Empty;

		string[] cliffSplatData = GetCliffSplatData();

		bool success = GameContext.SaveLoadService.SaveMapToFile(absolutePath, splatData, unitsData, propsData, decalsData, coordinatesData, cliffSplatData, terrainWriter);
		if (success)
		{
			if (performReload)
			{
				LoadMapFromFile(absolutePath, terrainOnly: false, clearUnits: true, ensureGlbOptimized: false);
				RestoreEditorStateAfterSave(savedState);
			}
			else
			{
				GameContext.MapEditorHud?.UpdateMapNameHeader();
			}
		}
	}

	public bool LoadMapFromFile(string customPath = "", bool terrainOnly = false, bool clearUnits = true, bool ensureGlbOptimized = true)
	{
		if (!BeginMapWorldLoad(customPath, terrainOnly, clearUnits, ensureGlbOptimized)) return false;
		InstantiateMapSpawnRequests();
		CompleteMapWorldLoad();
		return true;
	}

	private bool _mapWorldLoadTerrainOnly;

	private bool BeginMapWorldLoad(string customPath, bool terrainOnly, bool clearUnits, bool ensureGlbOptimized)
	{
		GameContext.SaveLoadService.IsLoadingMap = true;
		try
		{
			string absolutePath = ResolveMapDirectory(customPath, ensureGlbOptimized);
			if (clearUnits)
			{
				ClearAllUnits();
			}

			_mapWorldLoadTerrainOnly = terrainOnly;

			bool success = GameContext.SaveLoadService.LoadMapFromFile(absolutePath, terrainOnly);
			MatchLoadTrace.Mark("map_definition");
			if (!success)
			{
				return HandleLoadFailure();
			}

			EnsureGroundTerrainExistsView();

			TerrainState terrain = default;
			EditorState editor = default;
			bool foundWorld = false;

			var worldQuery = QueryCache.AllTerrainStateAndEditorStateQuery;
			World.Query(in worldQuery, (Entity _, ref TerrainState t, ref EditorState e) =>
			{
				terrain = t;
				editor = e;
				foundWorld = true;
			});

			if (!foundWorld) return false;

			ApplyEditorStateView(editor);
			ApplyTerrainStateView(terrain);
			LoadSplatMapsView(absolutePath);
			LoadCliffSplatMapsView(absolutePath);
			MatchLoadTrace.Mark("splat_images");
			var loadedGround = RequireGroundTerrain();
			GameContext.EditorService?.Surface.AttachSplatMaps(loadedGround.SplatMap, loadedGround.CliffSplatMap);
			AlignTerrainSplatMapExternal();
			NormalizeCameraBoundsToMap();

			return true;
		}
		finally
		{
			GameContext.SaveLoadService.IsLoadingMap = false;
		}
	}


	private void CompleteMapWorldLoad()
	{
		GameContext.SaveLoadService.IsLoadingMap = true;
		try
		{
			var ground = RequireGroundTerrain();
			ground.UpdateMeshAndPhysics(true, true);
			MatchLoadTrace.Mark("terrain_mesh");
			if (!GameContext.SessionState.IsMapEditorMode)
			{
				ground.ReloadTerrainTextures();
				MatchLoadTrace.Mark("terrain_textures");
			}
			GameContext.MapEditorHud?.RegenerateMinimap();

			RestoreLoadedCoordinates();

			EditorHistoryManager.Clear();
			EditorHasUnsavedChanges = false;
			_editorObjectSelection.Clear();
			GameContext.EditorService?.ResetAllState();
		}
		finally
		{
			GameContext.SaveLoadService.IsLoadingMap = false;
		}
	}

	private bool BeginLobbyMapWorldLoad(string rawMapName)
	{
		string targetPath = ResolveCustomTerrainPath(rawMapName);

		if (!string.IsNullOrEmpty(targetPath) && (targetPath.StartsWith("user://") || targetPath.StartsWith("res://")))
			targetPath = ProjectSettings.GlobalizePath(targetPath);

		if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath)) return false;

		// The assets are taken as they were published and downloaded: re-optimizing them here walks every file the map
		// holds while the lobby screen waits, and changes nothing, because this machine's copy is the copy the authority
		// published.
		bool terrainOnly = _multiplayerActive && !Multiplayer.IsServer();
		return BeginMapWorldLoad(targetPath, terrainOnly, true, false);
	}

	private void NormalizeCameraBoundsToMap()
	{
		if (GroundTerrain == null) return;

		MapCameraBounds mapBounds = MapCameraBounds.FromTerrain(GroundTerrain.Width, GroundTerrain.Depth, GroundTerrain.QuadSize);
		var currentBounds = new MapCameraBounds(EditorCameraBoundsLeft, EditorCameraBoundsRight, EditorCameraBoundsTop, EditorCameraBoundsBottom);
		if (!currentBounds.Matches(MapCameraBounds.LegacyDefaults) || currentBounds.Matches(mapBounds)) return;

		GameContext.EditorService.SetCameraBounds(GameContext.Simulation.WorldEntity, mapBounds);
		RebuildCameraBoundsOverlay();
	}

	private string ResolveMapDirectory(string customPath, bool ensureGlbOptimized)
	{
		string currentDir = GameContext.MapStorageService.CurrentMapDirectory;
		string defaultPath = MapWorkspaceService.GetDefaultMapFolderGlobalPath();
		string absolutePath = GameContext.MapStorageService.ResolveMapDirectoryDomain(customPath, currentDir, defaultPath, ensureGlbOptimized, ProjectSettings.GlobalizePath);
		return absolutePath;
	}

	private bool HandleLoadFailure()
	{
		int defaultWidth = 128;
		int defaultDepth = 128;

		InitializeDefaultTerrainState(defaultWidth, defaultDepth);
		ResetGroundTerrainView(defaultWidth, defaultDepth);
		ResetEditorUIView();
		NormalizeCameraBoundsToMap();

		return false;
	}

}
