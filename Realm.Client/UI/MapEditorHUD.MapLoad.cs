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
		public bool LoadMapFolder(string selectedFolder)
		{
			if (!Directory.Exists(selectedFolder)) return false;

			if (!GameContext.EditorService.IsValidMapFolder(selectedFolder))
			{
				ShowInvalidMapFolderErrorDialog(selectedFolder);
				return false;
			}

			if (GameContext.EditorService.IsMapFolderCorrupt(selectedFolder, out string corruptFile, out string corruptError))
			{
				ShowCorruptMapFileErrorDialog(selectedFolder, corruptFile, corruptError);
				ShowFeedback(string.Format(TranslationServer.Translate("Map file '{0}' is corrupt: {1}"), corruptFile, corruptError));
				return false;
			}

			var upgradeService = GameContext.MapUpgradeService ?? GameContext.MapUpgradeService;
			if (upgradeService != null && upgradeService.NeedsUpgrade(selectedFolder, out _, out _))
			{
				_ = LoadMapFolderAsync(selectedFolder);
				return true;
			}

			if (!PrepareForMapLoad(selectedFolder)) return false;

			try
			{
				return PerformMapLoad(selectedFolder);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] Failed to load map folder: {ex.Message}");
				return false;
			}
		}

		private bool PrepareForMapLoad(string selectedFolder)
		{
			GameContext.Host?.ClearMapEntirely();
			GameContext.EditorService.LastUsedFolder = selectedFolder;
			GameContext.EditorService.SetCurrentSourceFolder(selectedFolder);
			if (!IsRestrictedSaveDirectory(selectedFolder))
			{
				GameSettings.LastOpenedFolder = selectedFolder;
				GameSettings.Save();
			}

			ShowFeedback(TranslationServer.Translate("Loading map..."));
			return PrepareMapDirectoryForEditing(selectedFolder);
		}

		private bool PrepareMapDirectoryForEditing(string selectedFolder)
		{
			string fullSelectedPath = Path.GetFullPath(selectedFolder);
			if (!CanWriteToMapFolder(fullSelectedPath))
			{
				GameContext.EditorService.ClearEditingDirectoryDomain();
				ShowFeedback(TranslationServer.Translate("This map folder cannot be written to. Copy it to a folder you can write to and open that folder instead."));
				return false;
			}

			GameContext.EditorService.SetActiveMapDirectory(fullSelectedPath);
			GameContext.EditorService.CurrentSourceFolder = fullSelectedPath;
			EnsureEditingFolderScaffolding(fullSelectedPath, Path.GetFileName(fullSelectedPath));
			return true;
		}

		private static bool CanWriteToMapFolder(string folder)
		{
			try
			{
				string probePath = Path.Combine(folder, ".realm_write_probe");
				File.WriteAllText(probePath, string.Empty);
				File.Delete(probePath);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static void EnsureEditingFolderScaffolding(string editingDirectory, string mapName)
		{
			try
			{
				MapWorkspaceService.EnsureWitFile(editingDirectory);
				MapWorkspaceService.EnsureWasmEntryPoint(editingDirectory);
				MapWorkspaceService.EnsureCsproj(editingDirectory, mapName);
				CopyVsCodeFolderFromMapTemplate(editingDirectory);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				GameLog.LogError($"[MapEditorHUD] The scaffolding in '{editingDirectory}' is incomplete because another program is holding a file open: {exception.Message}. The map still opens and the scaffolding is repaired on its next load.");
			}
		}

		private bool PerformMapLoad(string selectedFolder)
		{
			MapWorkspaceService.EnsureGlbAssetsOptimized(GameContext.EditorService._editingDirectory);
			MapWorkspaceService.EnsurePngAssetsConverted(GameContext.EditorService._editingDirectory);
			ViewLoadMapProperties();
			ReadMetadataAndRefreshTextures();
			string terrainPath = Path.Combine(GameContext.EditorService._editingDirectory, "terrain.json");
			bool success = GameContext.Host?.LoadMapFromFile(terrainPath, ensureGlbOptimized: false) ?? false;

			if (success)
			{
				OnMapLoadedSuccessfully(selectedFolder, terrainPath);
			}
			else
			{
				ShowFeedback(TranslationServer.Translate("Failed to load map files from folder!"));
			}

			return success;
		}

		private void OnMapLoadedSuccessfully(string selectedFolder, string terrainPath)
		{
			if (OperatingSystem.IsWindows())
			{
				GameContext.VSCode.SaveRecentMapDir(selectedFolder);
				GameContext.VSCode.QueueWorkspaceMetadataReload();
			}

			GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(terrainPath);
			GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(Path.Combine(GameContext.EditorService._editingDirectory, "metadata.json"));
			EditorWorkspaceWatcher.Start(GameContext.EditorService._editingDirectory);
			ShowFeedback(string.Format(TranslationServer.Translate("Map loaded successfully from folder {0}!"), Path.GetFileName(selectedFolder)));
			RefreshSavedDirectoryHash();
			PrimeMapHistory();
		}

		public async Task<bool> LoadMapFolderAsync(string selectedFolder)
		{
			_lastMapLoadFailure = null;
			if (!Directory.Exists(selectedFolder)) return false;

			if (!GameContext.EditorService.IsValidMapFolder(selectedFolder))
			{
				ShowInvalidMapFolderErrorDialog(selectedFolder);
				return false;
			}

			if (GameContext.EditorService.IsMapFolderCorrupt(selectedFolder, out string corruptFile, out string corruptError))
			{
				ShowCorruptMapFileErrorDialog(selectedFolder, corruptFile, corruptError);
				ShowFeedback(string.Format(TranslationServer.Translate("Map file '{0}' is corrupt: {1}"), corruptFile, corruptError));
				return false;
			}

			bool canProceed = await PromptAndUpgradeMapIfNeededAsync(selectedFolder);
			if (!canProceed)
			{
				ShowFeedback(TranslationServer.Translate("Map loading cancelled."));
				return false;
			}

			return await PerformMapLoadAsync(selectedFolder);
		}

		private async Task<bool> PerformMapLoadAsync(string selectedFolder)
		{
			GameContext.Host?.ClearMapEntirely();

			GameContext.EditorService.LastUsedFolder = selectedFolder;
			defaultLoadFolder = selectedFolder;
			defaultSaveFolder = selectedFolder;
			mapSaveFolder = selectedFolder;

			GameContext.EditorService.IsSyncing = true;
			try
			{
				if (!PrepareForMapLoadAsync(selectedFolder))
				{
					_lastMapLoadFailure = TranslationServer.Translate("The map folder cannot be written to.");
					return false;
				}
				string terrainPath = Path.Combine(GameContext.EditorService._editingDirectory, "terrain.json");
				string metadataPath = Path.Combine(GameContext.EditorService._editingDirectory, "metadata.json");
				GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(terrainPath);
				GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);

				await GameContext.MapHistoryService.CreateVersionAsync(GetActiveEditingDirectory(), MapVersionKind.Initial, TranslationServer.Translate("First version"), GameContext.EditorService.CurrentSourceFolder);

				await ProcessMapAssetsAsync();

				return FinalizeMapLoadAsync(selectedFolder, terrainPath, metadataPath);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] Failed to load map folder: {ex.Message}");
				_lastMapLoadFailure = ex.Message;
				return false;
			}
			finally
			{
				GameContext.EditorService.IsSyncing = false;
			}
		}

		private bool PrepareForMapLoadAsync(string selectedFolder)
		{
			ModelCache.Clear();
			PathUtils.ClearCache();

			GameContext.EditorService.LastUsedFolder = selectedFolder;
			GameContext.EditorService.SetCurrentSourceFolder(selectedFolder);
			if (!IsRestrictedSaveDirectory(selectedFolder))
			{
				GameSettings.LastOpenedFolder = selectedFolder;
				GameSettings.Save();
			}

			ShowFeedback(TranslationServer.Translate("Loading map..."));
			return PrepareMapDirectoryForEditing(selectedFolder);
		}

		private async Task ProcessMapAssetsAsync()
		{
			await MapWorkspaceService.EnsureGlbAssetsOptimizedCooperativeAsync(GameContext.EditorService._editingDirectory, async (current, total, fileName) =>
			{
				ShowFeedback(string.Format(TranslationServer.Translate("Optimizing 3D asset {0}/{1}: {2}..."), current, total, fileName));
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			});

			await MapWorkspaceService.EnsurePngAssetsConvertedCooperativeAsync(GameContext.EditorService._editingDirectory, async (current, total, fileName) =>
			{
				ShowFeedback(string.Format(TranslationServer.Translate("Converting texture {0}/{1}: {2}..."), current, total, fileName));
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			});
		}

		private bool FinalizeMapLoadAsync(string selectedFolder, string terrainPath, string metadataPath)
		{
			ViewLoadMapProperties();
			ReadMetadataAndRefreshTextures();
			bool success = GameContext.Host?.LoadMapFromFile(terrainPath, ensureGlbOptimized: false) ?? false;

			if (success)
			{
				if (OperatingSystem.IsWindows())
				{
					GameContext.VSCode.SaveRecentMapDir(selectedFolder);
					GameContext.VSCode.QueueWorkspaceMetadataReload();
				}

				GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(terrainPath);
				GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);
				EditorWorkspaceWatcher.Start(GameContext.EditorService._editingDirectory);
					ShowFeedback(string.Format(TranslationServer.Translate("Map loaded successfully from folder {0}!"), Path.GetFileName(selectedFolder)));
				RefreshSavedDirectoryHash();
				PrimeMapHistory();
			}
			else
			{
				_lastMapLoadFailure = TranslationServer.Translate("Failed to load map files from folder!");
				ShowFeedback(_lastMapLoadFailure);
			}

			return success;
		}
	}
}
