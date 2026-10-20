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
		public static void RequestOpenFromCas(string casSourceDirectory, string? defaultSaveFolder)
		{
			GameContext.EditorService.RequestOpenFromCasDomain(casSourceDirectory, defaultSaveFolder);
		}

		public void UpdateLastMetadataSyncTime(string? path = null)
		{
			long writeTimeTicks = 0;
			try
			{
				string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
					? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
					: GameContext.EditorService._tempWorkspacePath;
				string metadataPath = string.IsNullOrEmpty(path) ? Path.Combine(wsPath, "metadata.json") : path;
				writeTimeTicks = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);
			}
			catch
			{
			}

			GameContext.EditorService.UpdateLastMetadataSyncTimeDomain(writeTimeTicks);
		}

		private void OnMetadataSaved(string targetPath)
		{
			UpdateLastMetadataSyncTime(targetPath);
			string wsPath = string.IsNullOrEmpty(targetPath) ? MapWorkspaceService.GetActiveWorkspacePath() : (Directory.Exists(targetPath) ? targetPath : Path.GetDirectoryName(targetPath));
			if (!string.IsNullOrEmpty(wsPath))
			{
				MapTemplateGeneratorService.EnsureMapTemplates(wsPath);
			}
		}

		public void PerformAutoBackup()
		{
			try
			{
				if (GameHost.Instance == null || GameHost.Instance.GroundTerrain == null) return;
				string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
				if (string.IsNullOrEmpty(wsPath) || !Directory.Exists(wsPath)) return;

				MapWorkspaceService.EnsureWasmEntryPoint(wsPath);

				string tempTerrainPath = Path.Combine(wsPath, "terrain.json");
				GameHost.Instance.SaveMapToFile(tempTerrainPath, performReload: false);

				long terrainWriteTime = GameContext.EditorService.GetMaxTerrainWriteTime(tempTerrainPath);
				long metadataWriteTime = GameContext.EditorService.GetLastWriteTimeSafe(Path.Combine(wsPath, "metadata.json"));

				GameContext.EditorService.PerformAutoBackupDomain(terrainWriteTime, metadataWriteTime);

				ShowFeedback(TranslationServer.Translate("Auto-backup snapshot saved."));
				GD.Print("[MapEditorHUD] Auto-backup snapshot saved.");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] Auto-backup failed: {ex.Message}");
			}
		}

		public void SaveMapActionExternal()
		{
			SaveMapAction();
		}

		private void InitializeTempWorkspace()
		{
			if (!GameContext.SessionState.ReturningFromTest)
			{
				TryCreateTempWorkspaceDirectory();
			}

			CleanTempWorkspaceRmapFiles();

			string initTerrainPath = Path.Combine(GameContext.EditorService._tempWorkspacePath, "terrain.json");
			string initMetadataPath = Path.Combine(GameContext.EditorService._tempWorkspacePath, "metadata.json");

			long terrainTime = GameContext.EditorService.GetMaxTerrainWriteTime(initTerrainPath);
			long metadataTime = GameContext.EditorService.GetLastWriteTimeSafe(initMetadataPath);

			GameContext.EditorService.InitializeTempWorkspaceDomain(terrainTime, metadataTime);

			EnsureDefaultTemplateAnimationsSafe();

			var syncTimer = new Timer();
			syncTimer.WaitTime = 1.0f;
			syncTimer.Autostart = true;
			syncTimer.Timeout += OnSyncTimerTimeout;
			AddChild(syncTimer);

			TryLoadMapPropertiesSafe();
			TryReloadGroundTerrainTexturesSafe();

			if (!string.IsNullOrEmpty(GameContext.EditorService.PendingCasSourceDirectory) && Directory.Exists(GameContext.EditorService.PendingCasSourceDirectory))
			{
				var consumed = GameContext.EditorService.ConsumePendingCasSourceDomain();
				if (consumed.casDir != null)
				{
					_ = OpenFromCasAsync(consumed.casDir, consumed.defaultSave);
				}
			}
		}

		private void TryCreateTempWorkspaceDirectory()
		{
			try
			{
				GameContext.EditorService.TryCreateTempWorkspaceDirectoryDomain();
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed initializing temp workspace: {ex.Message}");
			}
		}

		private void CleanTempWorkspaceRmapFiles()
		{
			try
			{
				GameContext.EditorService.CleanTempWorkspaceRmapFilesDomain();
			}
			catch
			{
			}
		}

		private void EnsureDefaultTemplateAnimationsSafe()
		{
			GameContext.EditorService.EnsureDefaultTemplateAnimationsSafeDomain();
		}

		private void TryLoadMapPropertiesSafe()
		{
			try
			{
				ViewLoadMapProperties();
			}
			catch (Exception ex)
			{
				GD.PrintErr($"LoadMapProperties error: {ex.Message}");
			}
		}

		private void TryReloadGroundTerrainTexturesSafe()
		{
			MapWorkspaceService.TryReloadGroundTerrainTexturesSafeDomain(() => { GameHost.Instance.ClearMapEntirely(); }
			);
		}

		private async Task OpenFromCasAsync(string casSourceDirectory, string? defaultSaveFolder)
		{
			bool loaded = await LoadMapFolderAsync(casSourceDirectory);
			if (loaded && !string.IsNullOrEmpty(defaultSaveFolder))
			{
				MapWorkspaceService.OpenFromCasDomain(defaultSaveFolder, IsRestrictedSaveDirectory, (folder) =>
				{
					GameSettings.LastOpenedFolder = folder;
					GameSettings.Save();
				});
			}
		}

		public static string ComputeDirectoryBlake3(string directoryPath)
		{
			return MapWorkspaceService.ComputeDirectoryBlake3Domain(directoryPath, IsIgnoredPath);
		}

		public static bool HasUnsavedChangesStatic()
		{
			string tempPath = ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath);
			return MapWorkspaceService.HasUnsavedChangesDomain(IsIgnoredPath, tempPath);
		}

		public bool HasUnsavedChanges() => HasUnsavedChangesStatic();

		public void SaveCurrentDirectoryBlake3()
		{
			MapWorkspaceService.SaveCurrentDirectoryBlake3Domain(ProjectSettings.GlobalizePath("user://editor_last_save.txt"), IsIgnoredPath);
		}

		private void WriteCleanExitMarker()
		{
			try
			{
				SaveCurrentDirectoryBlake3();
				MapWorkspaceService.WriteCleanExitMarkerDomain(ProjectSettings.GlobalizePath("user://editor_clean_exit.txt"));
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] Error writing editor_clean_exit.txt: {ex.Message}");
			}
		}

		private void CheckUnsavedSessionOnLaunch(Action onCompleted = null)
		{
			if (GameContext.SessionState.ReturningFromTest || !string.IsNullOrEmpty(GameContext.EditorService.PendingCasSourceDirectory))
			{
				onCompleted?.Invoke();
				return;
			}

			if (CheckAndCleanCleanExitFile())
			{
				onCompleted?.Invoke();
				return;
			}

			CheckLastSaveFile(onCompleted);
		}

		private bool CheckAndCleanCleanExitFile()
		{
			string cleanExitFile = ProjectSettings.GlobalizePath("user://editor_clean_exit.txt");
			return MapWorkspaceService.CheckAndCleanCleanExitFileDomain(cleanExitFile, SaveCurrentDirectoryBlake3);
		}

		private void CheckLastSaveFile(Action onCompleted)
		{
			string editorLastSaveFile = ProjectSettings.GlobalizePath("user://editor_last_save.txt");
			var result = MapWorkspaceService.CheckLastSaveFileDomain(editorLastSaveFile, GameContext.EditorService._tempWorkspacePath, ComputeDirectoryBlake3);
			
			if (!result.HasSavedFile)
			{
				SaveCurrentDirectoryBlake3();
				onCompleted?.Invoke();
				return;
			}

			GameContext.EditorService.CurrentDirectoryBlake3 = result.SavedHash;

			if (result.HasChanges)
			{
				ShowUnsavedSessionModal(onCompleted);
			}
			else
			{
				onCompleted?.Invoke();
			}
		}

		private void ShowUnsavedSessionModal(Action onCompleted = null)
		{
			ShowConfirmationDialog(
				"There were unsaved changes in last editor session.",
				onConfirm: () =>
				{
					LoadTempWorkspaceMap();
					SaveCurrentDirectoryBlake3();
				},
				confirmText: "Restore",
				cancelText: "Discard",
				onCancel: () =>
				{
					GameContext.EditorService.IsSyncing = true;
					try
					{
						ResetFolderLocations();
						ClearTempWorkspaceExternal();
						MapWorkspaceService.SetupWorkspace(GameContext.EditorService._tempWorkspacePath, "MapScript");
						GameHost.Instance?.ClearMapEntirely();
						string terrainPath = Path.Combine(GameContext.EditorService._tempWorkspacePath, "terrain.json");
						string metadataPath = Path.Combine(GameContext.EditorService._tempWorkspacePath, "metadata.json");
						GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(terrainPath);
						GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);
						SaveCurrentDirectoryBlake3();
						ReadMetadataAndRefreshTextures();
						ViewLoadMapProperties();
						UpdateMapNameHeader();
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[ShowUnsavedSessionModal] Error discarding session: {ex.Message}");
					}
					finally
					{
						GameContext.EditorService.IsSyncing = false;
					}
				},
				onDismissed: () => { onCompleted?.Invoke(); }
			);
		}

		private void OnSyncTimerTimeout()
		{
			var editorService = GameContext.EditorService;
			if (string.IsNullOrEmpty(editorService._tempWorkspacePath) || !Directory.Exists(editorService._tempWorkspacePath)) return;

			if (editorService.CheckAndHandleEditorClosure()) return;

			editorService.SyncWasmFiles();
			editorService.UpdateWorkspaceModTime();
		}


		public static bool IsIgnoredPath(string relativePath)
		{
			return MapWorkspaceService.IsIgnoredPathDomain(relativePath);
		}

		public static bool IsPathIgnoredByRule(string normalized)
		{
			return MapWorkspaceService.IsPathIgnoredByRuleDomain(normalized);
		}

		private static void CopyFileClearingReadOnly(string sourceFile, string targetFile)
		{
			PathUtils.CopyFileClearingReadOnly(sourceFile, targetFile);
		}

		private void CopyFolderToTempWorkspace(string sourceFolder)
		{
			ClearTempWorkspaceExternal();
			if (!Directory.Exists(GameContext.EditorService._tempWorkspacePath))
			{
				Directory.CreateDirectory(GameContext.EditorService._tempWorkspacePath);
			}

			var allFiles = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
			var filesToProcess = new List<(string Source, string Target, bool IsMutable)>(allFiles.Length);
			var createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var file in allFiles)
			{
				string relativePath = file.Substring(sourceFolder.Length + 1);
				if (IsIgnoredPath(relativePath)) continue;

				string targetFile = Path.Combine(GameContext.EditorService._tempWorkspacePath, relativePath);
				string targetDir = Path.GetDirectoryName(targetFile);
				if (!string.IsNullOrEmpty(targetDir) && createdDirs.Add(targetDir))
				{
					Directory.CreateDirectory(targetDir);
				}

				filesToProcess.Add((file, targetFile, PathUtils.IsMutableMapFileType(relativePath)));
			}

			Parallel.ForEach(filesToProcess, item =>
			{
				if (item.IsMutable)
				{
					PathUtils.CopyFileClearingReadOnly(item.Source, item.Target);
				}
				else
				{
					PathUtils.LinkOrCopyFile(item.Source, item.Target, preferHardLink: true);
				}
			});

			MapWorkspaceService.EnsureWitFile(GameContext.EditorService._tempWorkspacePath);
			MapWorkspaceService.EnsureWasmEntryPoint(GameContext.EditorService._tempWorkspacePath);
			MapWorkspaceService.EnsureCsproj(GameContext.EditorService._tempWorkspacePath, Path.GetFileName(sourceFolder));
			CopyVsCodeFolderFromMapTemplate(GameContext.EditorService._tempWorkspacePath);
		}

		private static void CopyVsCodeFolderFromMapTemplate(string targetWorkspacePath)
		{
			if (string.IsNullOrEmpty(targetWorkspacePath)) return;

			string templateVsCodeDir = MapWorkspaceService.GetTemplatePath(".vscode");
			if (string.IsNullOrEmpty(templateVsCodeDir) || !Directory.Exists(templateVsCodeDir))
			{
				templateVsCodeDir = PathUtils.FindPath("MapTemplate/.vscode");
			}

			if (string.IsNullOrEmpty(templateVsCodeDir) || !Directory.Exists(templateVsCodeDir))
			{
				return;
			}

			string targetVsCodeDir = Path.Combine(targetWorkspacePath, ".vscode");
			if (!Directory.Exists(targetVsCodeDir))
			{
				Directory.CreateDirectory(targetVsCodeDir);
			}

			foreach (string file in Directory.GetFiles(templateVsCodeDir, "*", SearchOption.AllDirectories))
			{
				string relativePath = file.Substring(templateVsCodeDir.Length + 1);
				string destFile = Path.Combine(targetVsCodeDir, relativePath);
				PathUtils.CopyFileClearingReadOnly(file, destFile);
			}
		}

		private void CopyTempWorkspaceToFolder(string targetFolder)
		{
			if (!Directory.Exists(targetFolder))
			{
				Directory.CreateDirectory(targetFolder);
			}

			string tempTerrainPath = Path.Combine(GameContext.EditorService._tempWorkspacePath, "terrain.json");
			GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(tempTerrainPath);

			var allFiles = Directory.GetFiles(GameContext.EditorService._tempWorkspacePath, "*", SearchOption.AllDirectories);
			var filesToCopy = new List<(string Source, string Target)>(allFiles.Length);
			var createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var file in allFiles)
			{
				string relativePath = file.Substring(GameContext.EditorService._tempWorkspacePath.Length + 1);
				if (IsIgnoredPath(relativePath)) continue;
				string targetFile = Path.Combine(targetFolder, relativePath);
				string targetDir = Path.GetDirectoryName(targetFile);
				if (!string.IsNullOrEmpty(targetDir) && createdDirs.Add(targetDir))
				{
					Directory.CreateDirectory(targetDir);
				}

				filesToCopy.Add((file, targetFile));
			}

			Parallel.ForEach(filesToCopy, pair => { PathUtils.CopyFileClearingReadOnly(pair.Source, pair.Target); });

			if (OperatingSystem.IsWindows())
			{
				VSCodeManager.Instance.SaveRecentMapDir(targetFolder);
			}
		}

		private async Task SaveMapToFolderAsync(string targetFolder)
		{
			MapWorkspaceService.EnsureLicenseFile(GameContext.EditorService._tempWorkspacePath);
			MapWorkspaceService.EnsureWasmEntryPoint(GameContext.EditorService._tempWorkspacePath);
			string tempTerrainPath = Path.Combine(GameContext.EditorService._tempWorkspacePath, "terrain.json");
			if (GameHost.Instance != null)
			{
				GameHost.Instance.SaveMapToFile(tempTerrainPath);
				GameHost.Instance.EditorHasUnsavedChanges = false;
				InvalidateMetadataCache();
			}

			ShowFeedback(TranslationServer.Translate("Saving map folder..."));

			try
			{
				await Task.Run(() => CopyTempWorkspaceToFolder(targetFolder));

				MapWorkspaceService.EnsureLicenseFile(targetFolder);
				MapWorkspaceService.EnsureWasmEntryPoint(targetFolder);

				SaveCurrentDirectoryBlake3();
				GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(tempTerrainPath);
				GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(Path.Combine(GameContext.EditorService._tempWorkspacePath, "metadata.json"));
				EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;

				ShowFeedback(string.Format(TranslationServer.Translate("Map saved successfully to folder {0}!"), Path.GetFileName(targetFolder)));
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] SaveMapToFolderAsync failed: {ex}");
				ShowFeedback(string.Format(TranslationServer.Translate("Failed to save map: {0}"), ex.Message));
			}
		}

		private void SaveMapAction()
		{
			if (GameHost.Instance == null) return;

			if (!string.IsNullOrEmpty(mapSaveFolder))
			{
				_ = SaveMapToFolderAsync(mapSaveFolder);
				return;
			}

			PromptSaveMapFolder();
		}

		private void SaveAsMapAction()
		{
			if (GameHost.Instance == null) return;
			PromptSaveMapFolder();
		}

		public void PromptSaveMapFolder()
		{
			if (GameHost.Instance == null) return;

			string initialDir = !string.IsNullOrEmpty(defaultSaveFolder) ? defaultSaveFolder : GetDocumentsDirectory();
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
				TranslationServer.Translate("Save Map Folder"),
				initialDir,
				"",
				false,
				DisplayServer.FileDialogMode.OpenDir,
				Array.Empty<string>(),
				Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
				{
					if (status && selectedPaths.Length > 0)
					{
						string selected = selectedPaths[0];
						defaultLoadFolder = selected;
						defaultSaveFolder = selected;
						mapSaveFolder = selected;
						HandleSaveToFolder(selected);
					}
					else
					{
						ShowFeedback(TranslationServer.Translate("Save cancelled"));
					}
				})
			);

			if (err != Error.Ok)
			{
				string defaultFolder = GetDefaultDevelopmentMapDirectory();
				try
				{
					Directory.CreateDirectory(defaultFolder);
				}
				catch
				{
				}

				defaultLoadFolder = defaultFolder;
				defaultSaveFolder = defaultFolder;
				mapSaveFolder = defaultFolder;
				HandleSaveToFolder(defaultFolder);
			}
		}

		private void HandleSaveToFolder(string selectedFolder)
		{
			if (string.IsNullOrWhiteSpace(selectedFolder)) return;

			string fullSelectedPath = Path.GetFullPath(selectedFolder);

			if (IsRestrictedSaveDirectory(fullSelectedPath))
			{
				ShowFeedback(TranslationServer.Translate("Cannot save to restricted application directory. Please choose a folder in Documents or your workspace."));
				return;
			}

			var editorService = GameContext.EditorService;
			if (editorService.IsNestedInExistingMap(fullSelectedPath))
			{
				ShowFeedback(TranslationServer.Translate("Cannot save map inside an existing map folder. Please select a separate root directory."));
				return;
			}

			if (editorService.ContainsNestedMap(fullSelectedPath))
			{
				ShowFeedback(TranslationServer.Translate("Found nested map inside selected folder. Cannot save to this folder location."));
				return;
			}

			string rootManifest = Path.Combine(fullSelectedPath, "manifest.json");
			bool isSameAsCurrent = !string.IsNullOrEmpty(editorService.CurrentSourceFolder) &&
								   string.Equals(fullSelectedPath, Path.GetFullPath(editorService.CurrentSourceFolder), StringComparison.OrdinalIgnoreCase);

			if (File.Exists(rootManifest) && !isSameAsCurrent)
			{
				ShowSaveOverwriteConfirmation(fullSelectedPath);
				return;
			}

			ProceedWithSaveToFolder(fullSelectedPath);
		}


		private void ShowSaveOverwriteConfirmation(string fullSelectedPath)
		{
			ShowConfirmationDialog(
				TranslationServer.Translate("An existing map was found in this folder. Overwrite existing map?"),
				() => ProceedWithSaveToFolder(fullSelectedPath),
				confirmText: TranslationServer.Translate("OVERWRITE"),
				cancelText: TranslationServer.Translate("CANCEL")
			);
		}

		private void ProceedWithSaveToFolder(string fullSelectedPath)
		{
			GameContext.EditorService.LastUsedFolder = fullSelectedPath;
			GameContext.EditorService.CurrentSourceFolder = fullSelectedPath;
			defaultLoadFolder = fullSelectedPath;
			defaultSaveFolder = fullSelectedPath;
			mapSaveFolder = fullSelectedPath;

			_ = SaveMapToFolderAsync(fullSelectedPath);
		}

		public void LoadMapAction()
		{
			if (GameHost.Instance == null) return;

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
				TranslationServer.Translate("Load Map Folder"),
				initialDir,
				"",
				false,
				DisplayServer.FileDialogMode.OpenDir,
				Array.Empty<string>(),
				Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
				{
					if (status && selectedPaths.Length > 0)
					{
						string selectedFolder = selectedPaths[0];
						_ = LoadMapFolderAsync(selectedFolder);
					}
				})
			);

			if (err != Error.Ok)
			{
				ShowFeedback(TranslationServer.Translate("Map could not be loaded."));
			}
		}


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

			PrepareForMapLoad(selectedFolder);

			try
			{
				return PerformMapLoad(selectedFolder);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] Failed to load map folder: {ex.Message}");
				return false;
			}
		}

		private void PrepareForMapLoad(string selectedFolder)
		{
			GameHost.Instance?.ClearMapEntirely();
			GameContext.EditorService.LastUsedFolder = selectedFolder;
			GameContext.EditorService.CurrentSourceFolder = selectedFolder;
			if (!IsRestrictedSaveDirectory(selectedFolder))
			{
				GameSettings.LastOpenedFolder = selectedFolder;
				GameSettings.Save();
			}

			ShowFeedback(TranslationServer.Translate("Loading map..."));
			CopyFolderToTempWorkspace(selectedFolder);
		}

		private bool PerformMapLoad(string selectedFolder)
		{
			MapWorkspaceService.EnsureGlbAssetsOptimized(GameContext.EditorService._tempWorkspacePath);
			MapWorkspaceService.EnsurePngAssetsConverted(GameContext.EditorService._tempWorkspacePath);
			ViewLoadMapProperties();
			ReadMetadataAndRefreshTextures();
			string terrainPath = Path.Combine(GameContext.EditorService._tempWorkspacePath, "terrain.json");
			bool success = GameHost.Instance?.LoadMapFromFile(terrainPath, ensureGlbOptimized: false) ?? false;

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
				VSCodeManager.Instance.SaveRecentMapDir(selectedFolder);
			}

			GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(terrainPath);
			GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(Path.Combine(GameContext.EditorService._tempWorkspacePath, "metadata.json"));
			GameContext.EditorService?.StartWorkspaceWatcher(GameContext.EditorService._tempWorkspacePath);
			ShowFeedback(string.Format(TranslationServer.Translate("Map loaded successfully from folder {0}!"), Path.GetFileName(selectedFolder)));
			SaveCurrentDirectoryBlake3();
		}

		public async Task<bool> LoadMapFolderAsync(string selectedFolder)
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
			GameHost.Instance?.ClearMapEntirely();

			GameContext.EditorService.LastUsedFolder = selectedFolder;
			defaultLoadFolder = selectedFolder;
			defaultSaveFolder = selectedFolder;
			mapSaveFolder = selectedFolder;

			GameContext.EditorService.IsSyncing = true;
			try
			{
				PrepareForMapLoadAsync(selectedFolder);

				await Task.Run(() => CopyFolderToTempWorkspace(selectedFolder));

				string terrainPath = Path.Combine(GameContext.EditorService._tempWorkspacePath, "terrain.json");
				string metadataPath = Path.Combine(GameContext.EditorService._tempWorkspacePath, "metadata.json");
				GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(terrainPath);
				GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);

				await ProcessMapAssetsAsync();

				return FinalizeMapLoadAsync(selectedFolder, terrainPath, metadataPath);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] Failed to load map folder: {ex.Message}");
				return false;
			}
			finally
			{
				GameContext.EditorService.IsSyncing = false;
			}
		}

		private void PrepareForMapLoadAsync(string selectedFolder)
		{
			ModelCache.Clear();
			PathUtils.ClearCache();

			GameContext.EditorService.LastUsedFolder = selectedFolder;
			GameContext.EditorService.CurrentSourceFolder = selectedFolder;
			if (!IsRestrictedSaveDirectory(selectedFolder))
			{
				GameSettings.LastOpenedFolder = selectedFolder;
				GameSettings.Save();
			}

			ShowFeedback(TranslationServer.Translate("Loading map..."));
		}

		private async Task ProcessMapAssetsAsync()
		{
			await MapWorkspaceService.EnsureGlbAssetsOptimizedCooperativeAsync(GameContext.EditorService._tempWorkspacePath, async (current, total, fileName) =>
			{
				ShowFeedback(string.Format(TranslationServer.Translate("Optimizing 3D asset {0}/{1}: {2}..."), current, total, fileName));
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			});

			await MapWorkspaceService.EnsurePngAssetsConvertedCooperativeAsync(GameContext.EditorService._tempWorkspacePath, async (current, total, fileName) =>
			{
				ShowFeedback(string.Format(TranslationServer.Translate("Converting texture {0}/{1}: {2}..."), current, total, fileName));
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			});
		}

		private bool FinalizeMapLoadAsync(string selectedFolder, string terrainPath, string metadataPath)
		{
			ViewLoadMapProperties();
			ReadMetadataAndRefreshTextures();
			bool success = GameHost.Instance?.LoadMapFromFile(terrainPath, ensureGlbOptimized: false) ?? false;

			if (success)
			{
				if (OperatingSystem.IsWindows())
				{
					VSCodeManager.Instance.SaveRecentMapDir(selectedFolder);
				}

				GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(terrainPath);
				GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);
				GameContext.EditorService?.StartWorkspaceWatcher(GameContext.EditorService._tempWorkspacePath);
				ShowFeedback(string.Format(TranslationServer.Translate("Map loaded successfully from folder {0}!"), Path.GetFileName(selectedFolder)));
				SaveCurrentDirectoryBlake3();
			}
			else
			{
				ShowFeedback(TranslationServer.Translate("Failed to load map files from folder!"));
			}

			return success;
		}

		public static void ResetFolderLocations()
		{
			mapSaveFolder = null;
			defaultSaveFolder = GetDocumentsDirectory();
			GameContext.EditorService.LastUsedFolder = null;
			GameContext.EditorService.CurrentSourceFolder = null;
			GameContext.EditorService.PendingCasSourceDirectory = null;
			GameContext.EditorService.PendingDefaultSaveFolder = null;
		}

		public static string GetDocumentsDirectory()
		{
			string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			if (string.IsNullOrEmpty(docs))
			{
				string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				docs = !string.IsNullOrEmpty(userProfile) ? Path.Combine(userProfile, "Documents") : ProjectSettings.GlobalizePath("user://");
			}

			try
			{
				if (!Directory.Exists(docs))
				{
					Directory.CreateDirectory(docs);
				}
			}
			catch
			{
			}

			return docs;
		}

		private string GetInitialDirectory()
		{
			return GameContext.EditorService.GetInitialDirectoryDomain(GameSettings.LastOpenedFolder, GetDocumentsDirectory(), IsRestrictedSaveDirectory);
		}

		public string GetDefaultDevelopmentMapDirectory(string? mapName = null)
		{
			string name = !string.IsNullOrWhiteSpace(mapName) ? mapName : GetMapNameFromMetadata();
			string docs = GetDocumentsDirectory();
			return GameContext.EditorService.GetDefaultDevelopmentMapDirectoryDomain(name, docs);
		}

		public static bool IsRestrictedSaveDirectory(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) return true;
			try
			{
				string fullPath = Path.GetFullPath(path).Replace("\\", "/").TrimEnd('/');
				return CheckRestrictedPaths(fullPath);
			}
			catch
			{
				return false;
			}
		}

		private static bool CheckRestrictedPaths(string fullPath)
		{
			string tempWorkspace = Path.GetFullPath(ProjectSettings.GlobalizePath("user://temp_map_workspace")).Replace("\\", "/").TrimEnd('/');
			string mapBackups = Path.GetFullPath(ProjectSettings.GlobalizePath("user://map_backups")).Replace("\\", "/").TrimEnd('/');
			string userDir = Path.GetFullPath(ProjectSettings.GlobalizePath("user://")).Replace("\\", "/").TrimEnd('/');
			string resDir = Path.GetFullPath(ProjectSettings.GlobalizePath("res://")).Replace("\\", "/").TrimEnd('/');

			return GameContext.EditorService.IsRestrictedSaveDirectoryDomain(fullPath, tempWorkspace, mapBackups, userDir, resDir);
		}


		private NSec.Cryptography.Key GetOrGenerateAuthorshipKey()
		{
			string keyDir = ProjectSettings.GlobalizePath("user://appdata/keys/");
			string defaultUsername = LobbyManager.Instance?.AuthenticatedUsername ?? string.Empty;
			var (key, data, keyPath, createdNew) = GameContext.EditorService.GetOrGenerateAuthorshipKeyDomain(defaultUsername, keyDir);
			if (createdNew)
			{
				ShowFeedback(TranslationServer.Translate("A new authorship key has been generated at ") + keyPath + TranslationServer.Translate(". Please backup this file to retain your authorship identity."));
			}

			return key;
		}


		private void SaveMapProperties()
		{
			if (GameHost.Instance == null) return;

			_mapSettingsDialog?.SaveMapProperties();
		}

		private async Task CompileAndSignMapAsync(string workspace, bool skipAttribution = true, bool isDebug = false)
		{
			_wasmHasErrors = false;

			bool compiledSuccessfully = await PerformCompilationStepAsync(workspace, skipAttribution, isDebug);
			if (compiledSuccessfully)
			{
				GameContext.MapStorageService.DeduplicateWorkspaceBuildArtifacts(workspace);
			}

			if (!compiledSuccessfully || skipAttribution) return;

			await PerformSigningStepAsync(workspace);
		}

		private async Task<bool> PerformCompilationStepAsync(string workspace, bool skipAttribution, bool isDebug = false)
		{
			return await GameContext.EditorService.PerformCompilationStepAsyncDomain(workspace, FindProjectFile, TryBypassCompilation, CheckMapApiDependency, ExecuteDotnetPublishAsync, msg => { _wasmHasErrors = true; SetWasmConsoleStatus($"❌ WASM Compilation Failed: {msg}", new Color(1.0f, 0.3f, 0.3f)); GD.PrintErr($"[MapEditorHUD] Trigger compilation failed: {msg}"); }, err => AppendWasmConsoleLog($"[COMPILER EXCEPTION] {err}"), isDebug);
		}

		private string FindProjectFile(string workspace)
		{
			return GameContext.EditorService.FindProjectFileDomain(workspace, msg => { _wasmHasErrors = true; SetWasmConsoleStatus("❌ " + msg, new Color(1.0f, 0.3f, 0.3f)); AppendWasmConsoleLog(msg); GD.PrintErr(msg); });
		}

		private bool TryBypassCompilation(string workspace)
		{
			return GameContext.EditorService.TryBypassCompilationDomain(workspace, FindExistingWasmFile, HasNewerSourceFiles, () => { _wasmHasErrors = false; SetWasmConsoleStatus("✓ WASM Compilation Bypassed (Unchanged)", UIStyle.ColorCyanGlow); AppendWasmConsoleLog("[INFO] .cs files unchanged since last build. Bypassing compilation using existing WASM binary."); });
		}

		private string FindExistingWasmFile(string workspace)
		{
			return MapWorkspaceService.FindExistingWasmFileDomain(workspace, GameContext.EditorService.CurrentSourceFolder);
		}

		private string SearchForWasmInDirectory(string dir)
		{
			return MapWorkspaceService.SearchForWasmInDirectoryDomain(dir);
		}

		private bool HasNewerSourceFiles(string workspace, DateTime wasmTime)
		{
			string currentSourceFolder = GameContext.EditorService.CurrentSourceFolder;
			return GameContext.MapStorageService.HasNewerSourceFiles(workspace, wasmTime, currentSourceFolder);
		}

		private bool CheckMapApiDependency(string workspace)
		{
			bool hasDependency = GameContext.MapStorageService.CheckMapApiDependency(workspace);
			if (!hasDependency)
			{
				_wasmHasErrors = true;
				SetWasmConsoleStatus("❌ WASM Compilation Failed: Realm.MapAPI.dll missing", new Color(1.0f, 0.3f, 0.3f));
				AppendWasmConsoleLog("[ERROR] Realm.MapAPI.dll is missing from the workspace (expected at lib/Realm.MapAPI.dll).");
				AppendWasmConsoleLog("[ERROR] The map script cannot compile without the MapAPI assembly. Reopen the map in the editor to restore template files, then retry Test.");
			}

			return hasDependency;
		}

		private async Task<bool> ExecuteDotnetPublishAsync(string workspace, string csproj, bool isDebug = false)
		{
			var result = await GameContext.MapStorageService.ExecuteDotnetPublishDomainAsync(workspace, csproj,
				(output) => AppendWasmConsoleLog(output),
				(error) => AppendWasmConsoleLog(error),
				isDebug);

			if (!result.success)
			{
				_wasmHasErrors = true;
				SetWasmConsoleStatus($"❌ WASM Compilation Failed (exit code {result.exitCode})", new Color(1.0f, 0.3f, 0.3f));
				AppendWasmConsoleLog($"[ERROR] dotnet publish failed with exit code {result.exitCode}");
				GD.PrintErr($"[MapEditorHUD] Map script compilation failed (exit code {result.exitCode})");
				return false;
			}
			else
			{
				_wasmHasErrors = false;
				SetWasmConsoleStatus("✓ WASM Compilation Succeeded", UIStyle.ColorCyanGlow);
				AppendWasmConsoleLog("[SUCCESS] WASM compilation complete (exit code 0).");
				return true;
			}
		}

		private async Task PerformSigningStepAsync(string workspace)
		{
			try
			{
				var authorshipKey = GetOrGenerateAuthorshipKey();
				string pubKeyStr = Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
				string seedServerUrl = GameHost.Instance != null && GodotObject.IsInstanceValid(LobbyManager.Instance) ? LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
				string currentUsername = await FetchCurrentUsernameAsync(pubKeyStr, seedServerUrl) ?? "MapAuthor";

				var referencedHashes = new List<string>();
				using (var httpClient = new HttpClient())
				{
					var allFiles = Directory.GetFiles(workspace, "*", SearchOption.AllDirectories);
					foreach (var file in allFiles)
					{
						if (IsMetadataOrKeyFile(file)) continue;

						string hash = SignAndUploadAsset(file, authorshipKey, currentUsername, pubKeyStr, seedServerUrl, httpClient);
						if (!string.IsNullOrEmpty(hash)) referencedHashes.Add(hash);
					}

					await UpdateMetadataAndSignMapAsync(workspace, referencedHashes, authorshipKey, currentUsername, pubKeyStr, seedServerUrl, httpClient);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] Error signing/compiling map: {ex.Message}");
			}
		}

		private async Task<string> FetchCurrentUsernameAsync(string pubKeyStr, string seedServerUrl)
		{
			return await GameContext.MapStorageService.FetchCurrentUsernameAsync(pubKeyStr, seedServerUrl);
		}

		private bool IsMetadataOrKeyFile(string file)
		{
			return GameContext.MapStorageService.IsMetadataOrKeyFile(file);
		}

		private string SignAndUploadAsset(string file, Key authorshipKey, string currentUsername, string pubKeyStr, string seedServerUrl, HttpClient httpClient)
		{
			return GameContext.MapStorageService.SignAndUploadAsset(file, authorshipKey, currentUsername, pubKeyStr, seedServerUrl, httpClient);
		}

		private async Task UpdateMetadataAndSignMapAsync(string workspace, List<string> referencedHashes, Key authorshipKey, string currentUsername, string pubKeyStr, string seedServerUrl, HttpClient httpClient)
		{
			await GameContext.MapStorageService.UpdateMetadataAndSignMapAsync(workspace, referencedHashes, authorshipKey, currentUsername, pubKeyStr, seedServerUrl, httpClient,
				(path, doc, key, user, pubKey, serverUrl, client, opts, hashes) => { SignAndUploadMapMetadata(path, (JsonObject)doc, key, user, pubKey, serverUrl, client, opts, hashes); });
		}

		private HashSet<string> ExtractExistingContributors(JsonObject mapDoc)
		{
			return GameContext.MapStorageService.ExtractExistingContributors((JsonObject)mapDoc);
		}

		private async Task<Dictionary<string, int>> CollectAssetAuthorsAsync(List<string> referencedHashes, string seedServerUrl, HttpClient httpClient, HashSet<string> contributorsList)
		{
			return await GameContext.MapStorageService.CollectAssetAuthorsAsync(referencedHashes, seedServerUrl, httpClient, contributorsList);
		}


		private void SignAndUploadMapMetadata(string metadataJsonPath, JsonObject mapDoc, Key authorshipKey, string currentUsername, string pubKeyStr, string seedServerUrl, HttpClient httpClient, JsonSerializerOptions options, List<string> referencedHashes)
		{
			GameContext.MapStorageService.SignAndUploadMapMetadata(metadataJsonPath, mapDoc, authorshipKey, currentUsername, pubKeyStr, seedServerUrl, httpClient, options, referencedHashes);
		}

		private void UploadMapMetadata(byte[] mapBytes, string mapHash, byte[] mapSigBytes, string currentUsername, string pubKeyStr, string seedServerUrl, HttpClient httpClient)
		{
			GameContext.MapStorageService.UploadMapMetadata(mapBytes, mapHash, mapSigBytes, currentUsername, pubKeyStr, seedServerUrl, httpClient);
		}

		private void PublishMapMetadata(string finalMetadataJson, List<string> referencedHashes, byte[] mapSigBytes, string pubKeyStr, string seedServerUrl, HttpClient httpClient)
		{
			GameContext.MapStorageService.PublishMapMetadata(finalMetadataJson, referencedHashes, mapSigBytes, pubKeyStr, seedServerUrl, httpClient);
		}

		public void OpenAssetBrowser(string title, IEnumerable<string> allowedExtensions, Action<string> onAssetSelected, bool requireRealmMetadata = false, string? requiredAssetType = null)
		{
			if (_assetBrowserDialog == null)
			{
				_assetBrowserDialog = new AssetBrowserDialog(this);
			}

			_assetBrowserDialog.OpenForImport(title, allowedExtensions, onAssetSelected, requireRealmMetadata, requiredAssetType);
		}

		public void OpenAssetBrowser(string title, IEnumerable<string> allowedExtensions, Action<string, string?> onAssetSelected, bool requireRealmMetadata = false, string? requiredAssetType = null)
		{
			if (_assetBrowserDialog == null)
			{
				_assetBrowserDialog = new AssetBrowserDialog(this);
			}

			_assetBrowserDialog.OpenForImport(title, allowedExtensions, onAssetSelected, requireRealmMetadata, requiredAssetType);
		}

		public void ImportTerrainFromMinimapDialog()
		{
			string initialDir = GetInitialDirectory();
			var err = DisplayServer.FileDialogShow(
				TranslationServer.Translate("Select Minimap Image to Import Terrain"),
				initialDir,
				"",
				false,
				DisplayServer.FileDialogMode.OpenFile,
				new[] { "*.png,*.jpg,*.jpeg,*.webp,*.gif ; Image Files (*.png, *.jpg, *.jpeg, *.webp, *.gif)" },
				Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
				{
					if (status && selectedPaths.Length > 0)
					{
						ImportTerrainFromMinimapPathView(selectedPaths[0]);
					}
				})
			);

			if (err != Error.Ok)
			{
				ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
			}
		}

		public void SaveUnitObjectAttachment(string unitId, HumanoidBone hand, string attachmentId, HandAttachmentOrientation orientation)
		{
			string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
				? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
				: GameContext.EditorService._tempWorkspacePath;
			GameContext.RegistryService.SaveUnitObjectAttachment(unitId, hand, attachmentId, orientation, wsPath);
		}

		public void SaveUnitObjectAttachment(string unitId, string socket, string attachmentId, HandAttachmentOrientation orientation)
		{
			try
			{
				string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
					? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
					: GameContext.EditorService._tempWorkspacePath;
				GameContext.RegistryService.SaveUnitObjectAttachment(unitId, socket, attachmentId, orientation, wsPath);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] SaveUnitObjectAttachment error: {ex.Message}");
			}
		}

		private (string regKey, bool isBuildingMeta) ResolveRegistryKeyAndType(string unitId)
		{
			return GameContext.RegistryService.ResolveRegistryKeyAndType(unitId);
		}

		private (string regKey, bool isBuildingMeta) ResolveRegistryKeyByIterating(string unitId, string cleanId)
		{
			return GameContext.RegistryService.ResolveRegistryKeyByIterating(unitId, cleanId);
		}

		private void UpdateLocalRegistry(string regKey, bool isBuildingMeta, string socket, string attachmentId, HandAttachmentOrientation orientation)
		{
			GameContext.RegistryService.UpdateLocalRegistry(regKey, isBuildingMeta, socket, attachmentId, orientation);
		}

		private void UpdateMetadataFile(string unitId, string regKey, string socket, string attachmentId, HandAttachmentOrientation orientation, string wsPath = null)
		{
			if (wsPath == null)
			{
				wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
					? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
					: GameContext.EditorService._tempWorkspacePath;
			}

			GameContext.RegistryService.UpdateMetadataFile(unitId, regKey, socket, attachmentId, orientation, wsPath);
		}

		private void ApplyAttachmentsToGameInstances(string unitId, string regKey)
		{
			GameContext.RegistryService.ApplyAttachmentsToGameInstances(unitId, regKey);
		}

		private bool IsMatchingUnit(string instanceUnitId, string unitId, string regKey)
		{
			return GameContext.RegistryService.IsMatchingUnit(instanceUnitId, unitId, regKey);
		}

		public void SaveAllUnitObjectAttachments(string targetId, UnitObjectAttachments? attachments)
		{
			GameContext.RegistryService.SaveAllUnitObjectAttachments(targetId, attachments, (t, a) => RestoreUnitObjectAttachments(t, a));
		}

		public void SaveCustomWeaponToMetadata(string weaponId, WeaponMetadata weapon)
		{
			try
			{
				string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
					? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
					: GameContext.EditorService._tempWorkspacePath;
				GameContext.RegistryService.SaveCustomWeaponToMetadata(weaponId, weapon, wsPath);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] SaveCustomWeaponToMetadata error: {ex.Message}");
			}
		}

		public void SaveCustomVfxToMetadata(string vfxId, VfxAttachmentConfig config)
		{
			try
			{
				string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
					? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
					: GameContext.EditorService._tempWorkspacePath;
				GameContext.RegistryService.SaveCustomVfxToMetadata(vfxId, config, wsPath);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] SaveCustomVfxToMetadata error: {ex.Message}");
			}
		}

		public void RemoveCustomVfxFromMetadata(string vfxId)
		{
			try
			{
				string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
					? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
					: GameContext.EditorService._tempWorkspacePath;
				GameContext.RegistryService.RemoveCustomVfxFromMetadata(vfxId, wsPath);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] RemoveCustomVfxFromMetadata error: {ex.Message}");
			}
		}

		public void SaveCustomUnitAnimations(string unitId, Dictionary<string, List<UnitAnimationEntry>> animations)
		{
			try
			{
				GameContext.EditorService.SaveCustomUnitAnimations(unitId, animations);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] SaveCustomUnitAnimations error: {ex.Message}");
			}
		}

		public void SaveCustomUnitAnimations(string unitId, Dictionary<string, string[]> animations)
		{
			var converted = new Dictionary<string, List<UnitAnimationEntry>>(StringComparer.OrdinalIgnoreCase);
			if (animations != null)
			{
				foreach (var kvp in animations)
				{
					converted[kvp.Key] = (kvp.Value ?? Array.Empty<string>())
						.Select(s => new UnitAnimationEntry { Animation = s })
						.ToList();
				}
			}

			SaveCustomUnitAnimations(unitId, converted);
		}

		public void SaveEntityModelPathToMetadata(string entityId, string fieldName, string domain, string newModelPath)
		{
			try
			{
				GameContext.EditorService.SaveEntityModelPathToMetadata(entityId, fieldName, domain, newModelPath);

				GameHost.Instance?.LoadUnitMetadata(GameContext.EditorService.GetWorkspacePath());
				if (fieldName != "PortraitModelPath")
				{
					GameHost.Instance?.RefreshAllPlacedObjectModels(entityId);
				}

				_entityPaletteController?.SelectCategory(_entityPaletteController.CurrentCategory, triggerAddObject: false);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] SaveEntityModelPathToMetadata error: {ex.Message}");
			}
		}


		public void SaveCustomAbilityVfxToMetadata(string abilityId, string visualEffect, string castSound, string iconPath, float aoeRadius)
		{
			try
			{
				GameContext.EditorService.SaveCustomAbilityVfxToMetadata(abilityId, visualEffect, castSound, iconPath, aoeRadius);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] SaveCustomAbilityVfxToMetadata error: {ex.Message}");
			}
		}

		private void ExportMapAction()
		{
			if (GameHost.Instance == null) return;

			string wsPath = !string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath) ? GameContext.EditorService._tempWorkspacePath : MapWorkspaceService.GetActiveWorkspacePath();

			if (!ValidateWorkspaceForExport(wsPath)) return;

			string mapTitle = GetMapNameFromMetadata();
			string mapVersion = GetMapVersionFromMetadata();
			string manifestBlake3 = GetManifestBlake3();

			string defaultFileName = GameContext.MapStorageService.GetExportMapDefaultFileName(mapTitle, mapVersion, manifestBlake3);
			string initialDir = GetInitialDirectory();

			ShowExportFileDialog(initialDir, defaultFileName);
		}

		private bool ValidateWorkspaceForExport(string wsPath)
		{
			var result = GameContext.MapStorageService.ValidateWorkspaceForExportDomain(wsPath);
			if (!result.Success)
			{
				if (result.MissingAssets.Count > 0)
				{
					ShowFeedback(string.Format(TranslationServer.Translate("Export failed: Map is missing required asset files:\n{0}"), string.Join(", ", Enumerable.Take(result.MissingAssets, 4)) + (result.MissingAssets.Count > 4 ? "..." : "")));
					AppendWasmConsoleLog($"[ERROR] Export failed. Missing required asset files:\n  {string.Join("\n  ", result.MissingAssets)}");
				}
				else if (result.OversizedAssets.Count > 0)
				{
					string oversizedSummary = string.Join("\n", Enumerable.Select(Enumerable.Take(result.OversizedAssets, 4), o => $"• {o.RelativePath} ({o.SizeMB:F2} MB > 15 MB)")) + (result.OversizedAssets.Count > 4 ? "\n..." : "");
					ShowFeedback(string.Format(TranslationServer.Translate("Export failed: Assets exceed maximum 15 MB size limit:\n{0}"), oversizedSummary));
					AppendWasmConsoleLog($"[ERROR] Export failed. Assets exceed maximum 15 MB size limit:\n{string.Join("\n", Enumerable.Select(result.OversizedAssets, o => $"  {o.RelativePath} ({o.SizeMB:F2} MB)"))}");
				}

				return false;
			}

			return true;
		}

		private void ShowExportFileDialog(string initialDir, string defaultFileName)
		{
			var err = DisplayServer.FileDialogShow(
				TranslationServer.Translate("Export Map Package (.rmap)"),
				initialDir,
				defaultFileName,
				false,
				DisplayServer.FileDialogMode.SaveFile,
				new[] { "*.rmap ; Realm Map Package (*.rmap)" },
				Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
				{
					if (status && selectedPaths.Length > 0)
					{
						string destinationPath = selectedPaths[0];
						if (!destinationPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase))
						{
							destinationPath += ".rmap";
						}

						_ = ExportMapPackageAsync(destinationPath);
					}
				})
			);

			if (err != Error.Ok)
			{
				string fallbackPath = Path.Combine(initialDir, defaultFileName);
				_ = ExportMapPackageAsync(fallbackPath);
			}
		}

		public async Task ExportMapPackageAsync(string destinationPath, bool fullExport = false)
		{
			if (GameHost.Instance == null) return;

			string mapTitle = GetMapNameFromMetadata();
			string mapVersion = GetMapVersionFromMetadata();
			string manifestBlake3 = GetManifestBlake3();
			destinationPath = GameContext.MapStorageService.ResolveExportDestinationPath(destinationPath, mapTitle, mapVersion, manifestBlake3);
			var (popup, progressBar, statusLabel, closeBtn) = CreateExportUI(destinationPath);
			SetupWasmConsoleForExport();

			try
			{
				if (!await PerformExportStepsAsync(progressBar, statusLabel, closeBtn, destinationPath, fullExport))
				{
					return; // Export failed or aborted
				}

				FinalizeSuccessfulExport(popup, destinationPath, statusLabel, progressBar);
			}
			catch (Exception ex)
			{
				HandleExportException(ex, statusLabel, closeBtn);
			}
			finally
			{
				CleanupExportState();
			}
		}


		private (Panel popup, ProgressBar progressBar, Label statusLabel, Button closeBtn) CreateExportUI(string destinationPath)
		{
			var popup = new Panel();
			popup.Name = "Popup_Panel";
			popup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			popup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
			popup.ZIndex = 1100;
			AddChild(popup);

			var cardPanel = new Panel();
			cardPanel.Name = "CardPanel_Panel";
			cardPanel.CustomMinimumSize = new Vector2(560, 260);
			cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
			cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
			popup.AddChild(cardPanel);

			var vbox = new VBoxContainer();
			vbox.Name = "Vbox_VBoxContainer";
			vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			vbox.CustomMinimumSize = new Vector2(520, 220);
			vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
			vbox.AddThemeConstantOverride("separation", 10);
			cardPanel.AddChild(vbox);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

			var titleLabel = new Label();
			titleLabel.Name = "TitleLabel_Label";
			UIStyle.ApplyTitle(titleLabel, "📦 " + TranslationServer.Translate("EXPORTING MAP PACKAGE (.RMAP)"), 20);
			titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
			vbox.AddChild(titleLabel);

			var descLabel = new Label();
			descLabel.Name = "DescLabel_Label";
			descLabel.Text = string.Format(TranslationServer.Translate("Destination: {0}"), Path.GetFileName(destinationPath));
			descLabel.HorizontalAlignment = HorizontalAlignment.Center;
			descLabel.AddThemeFontSizeOverride("font_size", 13);
			descLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			vbox.AddChild(descLabel);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

			var progressBar = new ProgressBar();
			progressBar.Name = "ProgressBar_ProgressBar";
			progressBar.CustomMinimumSize = new Vector2(480, 24);
			progressBar.MinValue = 0;
			progressBar.MaxValue = 100;
			progressBar.Value = 0;
			vbox.AddChild(progressBar);

			var statusLabel = new Label();
			statusLabel.Name = "StatusLabel_Label";
			statusLabel.Text = TranslationServer.Translate("Preparing export...");
			statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
			statusLabel.AddThemeFontSizeOverride("font_size", 13);
			statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
			vbox.AddChild(statusLabel);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

			var buttonRow = new HBoxContainer();
			buttonRow.Name = "ButtonRow_HBoxContainer";
			buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
			vbox.AddChild(buttonRow);

			var closeBtn = new Button();
			closeBtn.Name = "CloseBtn_Button";
			closeBtn.Flat = false;
			closeBtn.AddThemeConstantOverride("icon_max_width", 0);
			closeBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
			closeBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
			closeBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
			closeBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
			UIStyle.ApplyButtonText(closeBtn, TranslationServer.Translate("CLOSE"), 14);
			closeBtn.CustomMinimumSize = new Vector2(130, 36);
			closeBtn.Visible = false;
			buttonRow.AddChild(closeBtn);

			closeBtn.Pressed += () =>
			{
				UIManager.Instance?.PlayClickSound();
				if (GodotObject.IsInstanceValid(popup))
				{
					popup.QueueFree();
				}
			};

			return (popup, progressBar, statusLabel, closeBtn);
		}

		private void SetupWasmConsoleForExport()
		{
			WasmConsoleWindow.Instance.Hide();
			_wasmHasErrors = false;
			_exportLogHandler = line => AppendWasmConsoleLog(line);
			WasmRuntime.OnWasmLog += _exportLogHandler;
		}

		private Action<string> _exportLogHandler { get; set; }

		private async Task<bool> PerformExportStepsAsync(ProgressBar progressBar, Label statusLabel, Button closeBtn, string destinationPath, bool fullExport)
		{
			await SaveMapStateAsync(progressBar, statusLabel);

			if (!await VerifyMapAssetsAsync(progressBar, statusLabel, closeBtn)) return false;

			if (!await CompileMapScriptAsync(progressBar, statusLabel, closeBtn)) return false;

			GameContext.MapStorageService.DeduplicateWorkspaceBuildArtifacts(GameContext.EditorService._tempWorkspacePath);

			await OptimizeMapAssetsAsync(progressBar, statusLabel);

			await GenerateMapMetadataAsync(progressBar, statusLabel);

			await CreateMapArchiveAsync(progressBar, statusLabel, destinationPath, fullExport);

			return true;
		}

		private async Task SaveMapStateAsync(ProgressBar progressBar, Label statusLabel)
		{
			progressBar.Value = 10;
			statusLabel.Text = TranslationServer.Translate("Saving map state & workspace...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string tempWorkspacePath = GameContext.EditorService._tempWorkspacePath;
			await GameContext.MapStorageService.SaveMapStateForExportAsync(tempWorkspacePath, OperatingSystem.IsWindows());
		}

		private async Task<bool> VerifyMapAssetsAsync(ProgressBar progressBar, Label statusLabel, Button closeBtn)
		{
			progressBar.Value = 15;
			statusLabel.Text = TranslationServer.Translate("Verifying map assets...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string tempWorkspacePath = GameContext.EditorService._tempWorkspacePath;
			var validationResult = GameContext.MapStorageService.ValidateMapAssetsForExport(tempWorkspacePath);

			if (!validationResult.assetsValid)
			{
				var missingAssets = validationResult.missingAssets;
				AbortExport(progressBar, statusLabel, closeBtn, "Missing required asset files. Export aborted.",
					$"[ERROR] Export aborted. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
				ShowFeedback(string.Format(TranslationServer.Translate("Export failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
				return false;
			}

			if (!validationResult.sizesValid)
			{
				var oversizedAssets = validationResult.oversizedAssets;
				string oversizedSummary = string.Join("\n", oversizedAssets.Take(4).Select(o => $"• {o.RelativePath} ({o.SizeMB:F2} MB > 15 MB)")) + (oversizedAssets.Count > 4 ? "\n..." : "");
				AbortExport(progressBar, statusLabel, closeBtn, "Asset exceeds 15 MB size limit. Export aborted.",
					$"[ERROR] Export aborted. Assets exceed maximum 15 MB size limit:\n{string.Join("\n", oversizedAssets.Select(o => $"  {o.RelativePath} ({o.SizeMB:F2} MB)"))}");
				ShowFeedback(string.Format(TranslationServer.Translate("Export failed: Assets exceed maximum 15 MB size limit:\n{0}"), oversizedSummary));
				return false;
			}

			return true;
		}

		private async Task<bool> CompileMapScriptAsync(ProgressBar progressBar, Label statusLabel, Button closeBtn)
		{
			progressBar.Value = 20;
			statusLabel.Text = TranslationServer.Translate("Compiling WASM map script...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string tempWorkspacePath = GameContext.EditorService._tempWorkspacePath;
			await CompileAndSignMapAsync(tempWorkspacePath, skipAttribution: true);

			if (_wasmHasErrors)
			{
				AbortExport(progressBar, statusLabel, closeBtn, "WASM compilation failed. Export aborted.", null);
				ShowFeedback(TranslationServer.Translate("WASM compilation failed. Export aborted."));
				return false;
			}

			if (!GameContext.MapStorageService.CheckCompiledWasmBinaryExists(tempWorkspacePath))
			{
				AbortExport(progressBar, statusLabel, closeBtn, "Compiled WASM binary missing. Export aborted.", null);
				ShowFeedback(TranslationServer.Translate("Compiled WASM binary missing. Export aborted."));
				return false;
			}

			return true;
		}

		private async Task OptimizeMapAssetsAsync(ProgressBar progressBar, Label statusLabel)
		{
			progressBar.Value = 35;
			statusLabel.Text = TranslationServer.Translate("Optimizing 3D assets...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string tempWorkspacePath = GameContext.EditorService._tempWorkspacePath;
			var storageService = GameContext.MapStorageService;

			await storageService.OptimizeMapAssetsCooperativeAsync(tempWorkspacePath, async (current, total, fileName) =>
			{
				float fraction = total > 0 ? (float)current / total : 1.0f;
				progressBar.Value = 35 + fraction * 20;
				statusLabel.Text = string.Format(TranslationServer.Translate("Optimizing 3D model {0}/{1}: {2}..."), current, total, fileName);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			});

			progressBar.Value = 55;
			statusLabel.Text = TranslationServer.Translate("Converting textures...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			await storageService.ConvertMapTexturesCooperativeAsync(tempWorkspacePath, async (current, total, fileName) =>
			{
				float fraction = total > 0 ? (float)current / total : 1.0f;
				progressBar.Value = 55 + fraction * 15;
				statusLabel.Text = string.Format(TranslationServer.Translate("Converting texture {0}/{1}: {2}..."), current, total, fileName);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			});
		}

		private async Task GenerateMapMetadataAsync(ProgressBar progressBar, Label statusLabel)
		{
			progressBar.Value = 70;
			statusLabel.Text = TranslationServer.Translate("Generating map thumbnail...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string tempWorkspacePath = GameContext.EditorService._tempWorkspacePath;
			if (_minimapController != null)
			{
				await _minimapController.GenerateAndSaveMinimapThumbnailAsync(tempWorkspacePath);
			}

			progressBar.Value = 75;
			statusLabel.Text = TranslationServer.Translate("Generating manifest & indexing asset hashes...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string mapTitle = GetMapNameFromMetadata();
			string mapVersion = GetMapVersionFromMetadata();
			string author = LobbyManager.Instance?.AuthenticatedUsername ?? "MapAuthor";

			GameContext.MapStorageService.GenerateMapManifest(tempWorkspacePath, mapTitle, mapVersion, author);
		}


		private async Task CreateMapArchiveAsync(ProgressBar progressBar, Label statusLabel, string destinationPath, bool fullExport)
		{
			progressBar.Value = 80;
			statusLabel.Text = TranslationServer.Translate("Compressing package into .rmap archive...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string tempWorkspacePath = GameContext.EditorService._tempWorkspacePath;
			long lastProgressUpdateTicks = 0;

			await GameContext.MapStorageService.CreateMapArchiveAsync(tempWorkspacePath, destinationPath, fullExport, (pct, file) =>
			{
				long now = Environment.TickCount64;
				if (now - lastProgressUpdateTicks < 50 && pct < 1.0f)
				{
					return;
				}

				lastProgressUpdateTicks = now;
				string fileName = Path.GetFileName(file);
				Callable.From(() =>
				{
					if (GodotObject.IsInstanceValid(progressBar) && GodotObject.IsInstanceValid(statusLabel))
					{
						progressBar.Value = 80 + pct * 18;
						statusLabel.Text = string.Format(TranslationServer.Translate("Compressing {0} ({1}%)..."), fileName, (int)(pct * 100));
					}
				}).CallDeferred();
			});
		}

		private void AbortExport(ProgressBar progressBar, Label statusLabel, Button closeBtn, string labelMessage, string logMessage)
		{
			progressBar.Value = 100;
			statusLabel.Text = "❌ " + TranslationServer.Translate(labelMessage);
			statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
			closeBtn.Visible = true;
			if (logMessage != null) AppendWasmConsoleLog(logMessage);
		}

		private async void FinalizeSuccessfulExport(Panel popup, string destinationPath, Label statusLabel, ProgressBar progressBar)
		{
			progressBar.Value = 100;
			statusLabel.Text = "✓ " + TranslationServer.Translate("Export completed successfully!");
			statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.3f));
			await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);

			if (GodotObject.IsInstanceValid(popup))
			{
				popup.QueueFree();
			}

			ShowFeedback(string.Format(TranslationServer.Translate("Map exported successfully to {0}!"), Path.GetFileName(destinationPath)));
			UIManager.Instance?.ShowConfirmationDialog(
				string.Format(TranslationServer.Translate("Map exported successfully to {0}."), destinationPath),
				() => { },
				confirmText: "OK",
				showCancel: false
			);
		}

		private void HandleExportException(Exception ex, Label statusLabel, Button closeBtn)
		{
			SetWasmConsoleStatus($"❌ Export Failed: {ex.Message}", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[ERROR] Export failed: {ex}");
			ShowFeedback(string.Format(TranslationServer.Translate("Export failed: {0}"), ex.Message));
			if (GodotObject.IsInstanceValid(statusLabel))
			{
				statusLabel.Text = $"❌ {string.Format(TranslationServer.Translate("Export failed: {0}"), ex.Message)}";
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
			}

			if (GodotObject.IsInstanceValid(closeBtn))
			{
				closeBtn.Visible = true;
			}
		}

		private void CleanupExportState()
		{
			if (_exportLogHandler != null)
			{
				WasmRuntime.OnWasmLog -= _exportLogHandler;
				_exportLogHandler = null;
			}

			if (_wasmHasErrors)
			{
				WasmConsoleWindow.Instance.ShowConsole();
			}
			else
			{
				WasmConsoleWindow.Instance.Hide();
			}
		}

		private Texture2D LoadSwatchTextureInternal(int i)
		{
			string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
				? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
				: GameContext.EditorService._tempWorkspacePath;
			if (i >= 0 && i < GameContext.EditorService.SwatchDisplayNames.Count && GameContext.EditorService.SwatchDisplayNames[i].EndsWith("(Empty)"))
			{
				return null;
			}

			var (swatchPaths, swatchNames) = (GameContext.EditorService.SwatchPaths, GameContext.EditorService.SwatchDisplayNames);
			string localRtex = GameContext.MapStorageService.TryGetLocalRtexPath(i, wsPath, swatchPaths, swatchNames);

			if (File.Exists(localRtex))
			{
				Texture2D loadedTex = TryLoadRtexTexture(localRtex);
				if (loadedTex != null) return loadedTex;
			}

			if (i >= 0 && i < GameContext.EditorService.SwatchPaths.Count && ResourceLoader.Exists(GameContext.EditorService.SwatchPaths[i]))
			{
				return TrimmedTextureLoader.LoadTrimmedTexture(GameContext.EditorService.SwatchPaths[i]);
			}

			return null;
		}


		private Texture2D TryLoadRtexTexture(string localRtex)
		{
			try
			{
				byte[]? webpBytes = GameContext.EditorService.GetRtexWebpBytes(localRtex);
				if (webpBytes != null && webpBytes.Length > 0)
				{
					var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
					if (img.LoadWebpFromBuffer(webpBytes) != Error.Ok)
					{
						img.LoadPngFromBuffer(webpBytes);
					}

					return ImageTexture.CreateFromImage(img);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to load swatch preview: {ex.Message}");
			}

			return null;
		}

		private void ImportTextureAction()
		{
			if (GameHost.Instance == null) return;
			int selectedIdx = GameContext.EditorService.EditorPaintTextureIndex;
			if (selectedIdx < 0 || selectedIdx >= GameContext.EditorService.SwatchDisplayNames.Count)
			{
				ShowFeedback(TranslationServer.Translate("Please select a texture slot first"));
				return;
			}

			OpenAssetBrowser("Import Texture Image", new[] { ".rtex" }, (imagePath, preferredName) =>
			{
				if (selectedIdx >= 0 && selectedIdx < GameContext.EditorService.SwatchPaths.Count && !string.IsNullOrEmpty(GameContext.EditorService.SwatchPaths[selectedIdx]))
				{
					string cleanPrevName = GameContext.EditorService.SwatchDisplayNames[selectedIdx];
					string msg = string.Format(TranslationServer.Translate("Replacing texture in Slot {0} ({1}) will update all areas of the terrain painted with this slot. Do you want to proceed?"), selectedIdx, cleanPrevName);
					ShowConfirmationDialog(msg, () => { ImportTextureFile(imagePath, selectedIdx, preferredName); }, confirmText: "REPLACE", cancelText: "CANCEL");
				}
				else
				{
					ImportTextureFile(imagePath, selectedIdx, preferredName);
				}
			}, requireRealmMetadata: false);
		}

		private void ImportTextureFile(string imagePath, int index, string? preferredFileName = null)
		{
			string cleanBaseName = GameContext.EditorService.ResolveTextureFileName(imagePath, preferredFileName);
			string outputRtex = GameContext.EditorService.PrepareTextureOutputDir(cleanBaseName);
			ShowFeedback(TranslationServer.Translate("Importing texture..."));
			try
			{
				GameContext.EditorService.CopyOrProcessTexture(imagePath, outputRtex, GameHost.Instance);
				UpdateTextureMetadataAndReload(index, cleanBaseName, outputRtex);
				ShowFeedback(string.Format(TranslationServer.Translate("Successfully imported custom texture for {0}!"), cleanBaseName));
			}
			catch (Exception ex)
			{
				ShowFeedback(string.Format(TranslationServer.Translate("Failed to import texture: {0}"), ex.Message));
				GD.PrintErr($"Failed to import texture: {ex.Message}");
			}
		}

		private void UpdateTextureMetadataAndReload(int index, string cleanBaseName, string outputRtex)
		{
			GameContext.EditorService.UpdateTextureMetadataAndReload(index, cleanBaseName, outputRtex, GameHost.Instance, (cat, name, blake, slot) => UpdateMetadataJsonAsset(cat, name, blake, targetSlot: slot));
			SetupTextureSwatches(false);
		}

		public void OpenConvertGlbDialog(string? initialPath = null, string? initialSubCat = null, Action<string>? onConverted = null)
		{
			Action<string> chainedCallback = (resultPath) =>
			{
				onConverted?.Invoke(resultPath);
				_assetManagerDialog?.RefreshAssetListAndSelect(resultPath);
				_templateManagerDialog?.RefreshObjectList();
			};
			_convertGlbDialog?.OpenWithPreset(initialPath, initialSubCat, chainedCallback);
		}

		public static string SanitizeMapName(string? candidateName)
		{
			return GameContext.EditorService.SanitizeMapName(candidateName, MapWorkspaceService.DefaultWorkspaceFolder);
		}

		public string GetMapVersionFromMetadata(bool forceReload = false)
		{
			string workspacePath = !string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath) ? GameContext.EditorService._tempWorkspacePath : MapWorkspaceService.GetActiveWorkspacePath();
			return GameContext.EditorService.GetMapVersionFromMetadata(forceReload, workspacePath);
		}

		public string GetManifestBlake3()
		{
			string workspacePath = !string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath) ? GameContext.EditorService._tempWorkspacePath : MapWorkspaceService.GetActiveWorkspacePath();
			string mapTitle = GetMapNameFromMetadata();
			string mapVersion = GetMapVersionFromMetadata();
			string author = LobbyManager.Instance?.AuthenticatedUsername ?? "MapAuthor";
			return GameContext.EditorService.GetManifestBlake3(workspacePath, mapTitle, mapVersion, author);
		}

		public void ImportTextureAssetFromExtension(string sourceFilePath, int slotIndex)
		{
			try
			{
				string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
					? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
					: GameContext.EditorService._tempWorkspacePath;

				bool success = GameContext.EditorService.ImportTextureAssetFromExtensionDomain(
					sourceFilePath, slotIndex, wsPath, GameHost.Instance,
					out string cleanBaseName, out string outputRtex,
					(cat, name, blake, slot) => UpdateMetadataJsonAsset(cat, name, blake, targetSlot: slot));

				if (success)
				{
					ReadMetadataAndRefreshTextures();
					ShowFeedback($"Successfully processed & imported RTEX texture for {cleanBaseName}!");
				}
				else
				{
					ShowFeedback($"Failed to generate RTEX texture at {outputRtex}");
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"ImportTextureAssetFromExtension error: {ex.Message}");
				ShowFeedback($"Failed to import texture: {ex.Message}");
			}
		}


		public void ReadMetadataAndRefreshTextures()
		{
			try
			{
				string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
					? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
					: GameContext.EditorService._tempWorkspacePath;
				string metadataPath = Path.Combine(wsPath, "metadata.json");

				string texDir = Path.Combine(wsPath, "Assets", "textures");
				Directory.CreateDirectory(texDir);

				if (File.Exists(metadataPath))
				{
					MapWorkspaceService.NormalizeMetadataTextureEntries(wsPath);
				}

				InvalidateMetadataCache();

				_swatchTextureCache.Clear();
				if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
				{
					GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
				}

				SetupTextureSwatches(false);
				RefreshSkyboxList();
				if (File.Exists(metadataPath))
				{
					GameHost.Instance?.LoadModelYOffsetsFromMetadataJson(wsPath);
					GameHost.Instance?.LoadUnitMetadata(wsPath);
				}

				_entityPaletteController?.SelectCategory(_entityPaletteController.CurrentCategory, triggerAddObject: false);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"ReadMetadataAndRefreshTextures error: {ex.Message}");
			}
		}

		public void ImportMixamoOrAnimationDialog()
		{
			OpenAssetBrowser("Select Animation (.ranim)", new[] { ".ranim" }, (path, preferredName) => { ImportAnimationAssetFromExtension(path, preferredName); });
		}

		public void ImportAnimationAssetFromExtension(string sourceFilePath, string? preferredFileName = null)
		{
			try
			{
				string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
					? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
					: GameContext.EditorService._tempWorkspacePath;
				string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
				string animsDir = GameContext.EditorService.PrepareAnimationsDirectory(wsPath);

				if (ext == ".glb" || ext == ".gltf" || ext == ".fbx")
				{
					Import3DAnimationFile(sourceFilePath, preferredFileName, animsDir);
				}
				else
				{
					ImportRanimFile(sourceFilePath, preferredFileName, animsDir);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"ImportAnimationAssetFromExtension error: {ex.Message}");
				ShowFeedback(string.Format(TranslationServer.Translate("Failed to import animation: {0}"), ex.Message));
			}
		}

		private void Import3DAnimationFile(string sourceFilePath, string? preferredFileName, string animsDir)
		{
			string originalFileName = ResolveAnimationFileName(sourceFilePath, preferredFileName);
			var result = GameContext.EditorService.Import3DAnimationFileDomain(sourceFilePath, originalFileName, animsDir);

			if (result.ImportedCount == 0 && result.SkippedCount == 0)
			{
				ShowFeedback(TranslationServer.Translate("No animations found in file."));
				return;
			}

			if (result.NewAnims != null)
			{
				foreach (var anim in result.NewAnims)
				{
					UpdateMetadataJsonAsset("animations", anim.FileName, anim.Blake3);
				}
			}

			PopulateAnimationPreviewDropdown();
			if (result.ImportedCount > 0)
			{
				ShowFeedback(string.Format(TranslationServer.Translate("Successfully imported {0} animation(s) (.ranim)!"), result.ImportedCount));
			}
			else
			{
				ShowFeedback(TranslationServer.Translate("Animation already imported (identical BLAKE3 hash)."));
			}
		}

		private string ResolveAnimationFileName(string sourceFilePath, string? preferredFileName)
		{
			return GameContext.EditorService.ResolveAnimationFileName(sourceFilePath, preferredFileName);
		}

		private void ImportRanimFile(string sourceFilePath, string? preferredFileName, string animsDir)
		{
			var result = GameContext.EditorService.ImportRanimFileDomain(sourceFilePath, preferredFileName, animsDir);
			if (result.Success)
			{
				UpdateMetadataJsonAsset("animations", result.FinalFileName, result.NewHash);
				PopulateAnimationPreviewDropdown();
				ShowFeedback(string.Format(TranslationServer.Translate("Imported animation {0}"), result.FinalFileName));
			}
			else
			{
				UpdateMetadataJsonAsset("animations", result.FinalFileName, result.NewHash);
				PopulateAnimationPreviewDropdown();
				ShowFeedback(TranslationServer.Translate("Animation already imported (identical BLAKE3 hash)."));
			}
		}

		public void ImportDecalAssetFromExtension(string sourceFilePath)
		{
			string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
				? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
				: GameContext.EditorService._tempWorkspacePath;

			var result = GameContext.EditorService.ImportDecalAssetDomain(sourceFilePath, wsPath);
			if (result.Success)
			{
				UpdateMetadataJsonAsset("decals", result.FileName, result.Blake3);
				ShowFeedback($"Imported decal {result.FileName}");
			}
			else
			{
				GD.PrintErr($"ImportDecalAssetFromExtension error: {result.Message}");
				ShowFeedback($"Failed to import decal: {result.Message}");
			}
		}

		public void ImportSkyboxAssetFromExtension(string sourceFilePath)
		{
			string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
				? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
				: GameContext.EditorService._tempWorkspacePath;
			var result = GameContext.EditorService.ImportSkyboxAsset(sourceFilePath, wsPath);
			if (result.Success)
			{
				UpdateMetadataJsonAsset("skyboxes", result.FileName, result.Blake3);
				RefreshSkyboxList();
				ShowFeedback($"Imported skybox {result.FileName}");
			}
			else
			{
				GD.PrintErr($"ImportSkyboxAssetFromExtension error: {result.Message}");
				ShowFeedback($"Failed to import skybox: {result.Message}");
			}
		}

		public void ImportSpritesheetAssetFromExtension(string sourceFilePath, int columns = 4, int rows = 4)
		{
			string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
				? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
				: GameContext.EditorService._tempWorkspacePath;
			var result = GameContext.EditorService.ImportSpritesheetAsset(sourceFilePath, wsPath);
			if (result.Success)
			{
				UpdateMetadataJsonAsset("vfx_spritesheets", result.FileName, result.Blake3, columns: columns, rows: rows);
				ShowFeedback($"Imported VFX spritesheet {result.FileName}");
			}
			else
			{
				GD.PrintErr($"ImportSpritesheetAssetFromExtension error: {result.Message}");
				ShowFeedback($"Failed to import VFX spritesheet: {result.Message}");
			}
		}

		public void ImportIconAssetFromExtension(string sourceFilePath)
		{
			string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
				? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
				: GameContext.EditorService._tempWorkspacePath;
			var result = GameContext.EditorService.ImportIconAsset(sourceFilePath, wsPath);
			if (result.Success)
			{
				UpdateMetadataJsonAsset("icons", result.FileName, result.Blake3);
				ShowFeedback($"Imported 2D Icon {result.FileName}");
			}
			else
			{
				GD.PrintErr($"ImportIconAssetFromExtension error: {result.Message}");
				ShowFeedback($"Failed to import 2D Icon: {result.Message}");
			}
		}

		public void ImportAudioAssetFromExtension(string sourceFilePath, string audioType)
		{
			string wsPath = string.IsNullOrEmpty(GameContext.EditorService._tempWorkspacePath)
				? ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath)
				: GameContext.EditorService._tempWorkspacePath;

			var result = GameContext.AudioService.ImportAudioAsset(sourceFilePath, audioType, wsPath);

			if (result.Success)
			{
				UpdateMetadataJsonAsset(result.AudioCategory, result.FileName, result.Blake3);
				ShowFeedback($"Imported audio {result.FileName} ({audioType})");
			}
			else
			{
				GD.PrintErr($"ImportAudioAssetFromExtension error: {result.Message}");
				ShowFeedback($"Failed to import audio asset: {result.Message}");
			}
		}

		private void LoadTempWorkspaceMap()
		{
			var editorService = GameContext.EditorService;
			editorService.PreLoadTempWorkspaceMapDomain();
			ViewLoadMapProperties();
			ReadMetadataAndRefreshTextures();
			editorService.PostLoadTempWorkspaceMapDomain();
			ShowFeedback(TranslationServer.Translate("Restored map workspace from last session!"));
		}


		public void ClearTempWorkspaceExternal()
		{
			GameContext.EditorService.ClearTempWorkspaceExternalDomain(msg => GD.PrintErr($"[ClearTempWorkspaceExternal] Error: {msg}"));
			_wasmHasErrors = false;
			_wasmCompileLogPath = "";
		}

		public void GenerateVSCodeFilesExternal()
		{
			GameContext.EditorService.GenerateVSCodeFilesExternalDomain();
		}
	}
}
