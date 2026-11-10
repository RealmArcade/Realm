using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using NSec.Cryptography;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Services;
using Realm.Shared.Distribution;
using Environment = System.Environment;
using HttpClient = System.Net.Http.HttpClient;
using Key = NSec.Cryptography.Key;
using static Godot.Control;
using Realm.Client.Utils;

namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		public void ImportMapArchiveAction()
		{
			if (GameContext.Host == null) return;

			string initialDir = !string.IsNullOrEmpty(defaultLoadFolder) ? defaultLoadFolder : GetDocumentsDirectory();
			try
			{
				if (!Directory.Exists(initialDir))
				{
					Directory.CreateDirectory(initialDir);
				}
			}
			catch
			{
			}

			var err = DisplayServer.FileDialogShow(
				TranslationServer.Translate("Import Map Package (.rmap)"),
				initialDir,
				"",
				false,
				DisplayServer.FileDialogMode.OpenFile,
				new[] { "*.rmap ; Realm Map Package (*.rmap)" },
				Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
				{
					if (status && selectedPaths.Length > 0)
					{
						_ = ImportMapArchiveAsync(selectedPaths[0]);
					}
				})
			);

			if (err != Error.Ok)
			{
				ShowFeedback(TranslationServer.Translate("Map package could not be imported."));
			}
		}

		public async Task<bool> ImportMapArchiveAsync(string archivePath)
		{
			string? workspacePath = ResolveImportWorkspace(archivePath);
			if (workspacePath == null) return false;

			if (!EnsureArchiveHasManifest(archivePath)) return false;

			GameContext.Host?.ClearMapEntirely();
			GameContext.EditorService.IsSyncing = true;

			ShowFeedback(TranslationServer.Translate("Extracting map package into the editor workspace..."));

			if (!await ExtractArchiveToWorkspaceAsync(archivePath, workspacePath))
			{
				GameContext.EditorService.IsSyncing = false;
				return false;
			}

			if (!ValidateImportedMapFolder(workspacePath))
			{
				GameContext.EditorService.IsSyncing = false;
				return false;
			}

			bool canProceed = await PromptAndUpgradeMapIfNeededAsync(workspacePath);
			if (!canProceed)
			{
				ShowFeedback(TranslationServer.Translate("Map loading cancelled."));
				GameContext.EditorService.IsSyncing = false;
				return false;
			}

			return await LoadImportedWorkspaceAsync(workspacePath);
		}

		private string? ResolveImportWorkspace(string archivePath)
		{
			if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
			{
				ShowFeedback(TranslationServer.Translate("Map package could not be imported: the file does not exist."));
				return null;
			}

			string workspacePath = GameContext.EditorService._editingDirectory;
			if (string.IsNullOrEmpty(workspacePath))
			{
				EnsureEditingDirectoryReady();
				workspacePath = GameContext.EditorService._editingDirectory;
			}

			if (string.IsNullOrEmpty(workspacePath))
			{
				ShowFeedback(TranslationServer.Translate("The editor workspace is not available."));
				return null;
			}

			return workspacePath;
		}

		private bool EnsureArchiveHasManifest(string archivePath)
		{
			var (manifestJson, _) = MapArchiveHelper.ReadManifestFromArchive(archivePath);
			if (string.IsNullOrWhiteSpace(manifestJson))
			{
				ShowFeedback(TranslationServer.Translate("The map package does not contain a readable map."));
				return false;
			}
			return true;
		}

		private async Task<bool> ExtractArchiveToWorkspaceAsync(string archivePath, string workspacePath)
		{
			try
			{
				await Task.Run(() =>
				{
					MapWorkspaceService.CleanWorkspaceDirectory(workspacePath);
					Directory.CreateDirectory(workspacePath);
					MapArchiveHelper.ExtractArchive(archivePath, workspacePath);
				});
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] Failed to extract map package {archivePath}: {ex}");
				ShowFeedback(string.Format(TranslationServer.Translate("Failed to extract the map package: {0}"), ex.Message));
				return false;
			}
			return true;
		}

		private bool ValidateImportedMapFolder(string workspacePath)
		{
			if (!GameContext.EditorService.IsValidMapFolder(workspacePath))
			{
				ShowInvalidMapFolderErrorDialog(workspacePath);
				ShowFeedback(TranslationServer.Translate("The map package does not contain a readable map."));
				return false;
			}

			if (GameContext.EditorService.IsMapFolderCorrupt(workspacePath, out string corruptFile, out string corruptError))
			{
				ShowCorruptMapFileErrorDialog(workspacePath, corruptFile, corruptError);
				ShowFeedback(string.Format(TranslationServer.Translate("Map file '{0}' is corrupt: {1}"), corruptFile, corruptError));
				return false;
			}
			return true;
		}

		private async Task<bool> LoadImportedWorkspaceAsync(string workspacePath)
		{
			try
			{
				string mapName = GetImportedMapName();
				await Task.Run(() => MapWorkspaceService.SetupWorkspace(workspacePath, mapName));

				string terrainPath = Path.Combine(workspacePath, "terrain.json");
				string metadataPath = Path.Combine(workspacePath, "metadata.json");
				GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(terrainPath);
				GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);

				ViewLoadMapProperties();
				ReadMetadataAndRefreshTextures();

				bool success = GameContext.Host?.LoadMapFromFile(terrainPath, ensureGlbOptimized: false) ?? false;
				if (!success)
				{
					ShowFeedback(TranslationServer.Translate("Failed to load map files from folder!"));
					return false;
				}

				GameContext.EditorService.LastUsedFolder = null;
				GameContext.EditorService.SetCurrentSourceFolder(string.Empty);
				EditorWorkspaceWatcher.Start(workspacePath);
				mapSaveFolder = null;
				defaultSaveFolder = GetDocumentsDirectory();
				RefreshSavedDirectoryHash();
				ShowFeedback(TranslationServer.Translate("Map package imported. Choose a destination folder when you save."));
				return true;
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] Failed to import map package in {workspacePath}: {ex}");
				ShowFeedback(string.Format(TranslationServer.Translate("Failed to import the map package: {0}"), ex.Message));
				return false;
			}
			finally
			{
				GameContext.EditorService.IsSyncing = false;
			}
		}


		private string GetImportedMapName()
		{
			string mapName = GetMapNameFromMetadata(forceReload: true);
			mapName = string.Join("_", mapName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
			return string.IsNullOrWhiteSpace(mapName) ? "MapScript" : mapName;
		}
	}
}
