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
		private static string GetActiveEditingDirectory()
		{
			return GameContext.EditorService._editingDirectory;
		}

		public void PrimeMapHistory()
		{
			GameContext.MapHistoryService.PrimeForMap(GetActiveEditingDirectory());
		}

		private static void ScheduleHistoryMaintenanceOnLaunch()
		{
			GameContext.MapHistoryService.MaintainAllStoresInBackground();
		}

		public void PersistEditorStateBeforeRestore()
		{
			string editingDirectory = GetActiveEditingDirectory();
			if (string.IsNullOrEmpty(editingDirectory) || !Directory.Exists(editingDirectory)) return;
			if (!File.Exists(Path.Combine(editingDirectory, "metadata.json"))) return;
			if (GameContext.Host == null || GameContext.Host.GroundTerrain == null) return;

			try
			{
				GameContext.Host.SaveMapToFile(Path.Combine(editingDirectory, "terrain.json"), performReload: false);
				GameContext.Host.EditorHasUnsavedChanges = false;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] Could not write the current map before restoring a version: {ex.Message}");
			}

			GameContext.MapAutosaveService.FlushPendingWrites(TimeSpan.FromSeconds(3));
		}

		private void RecordMapVersion(MapVersionKind kind, string label, bool coalesce = false)
		{
			GameContext.MapAutosaveService.FlushPendingWrites(TimeSpan.FromSeconds(3));

			string editingDirectory = GetActiveEditingDirectory();
			string? sourceFolder = string.IsNullOrEmpty(GameContext.EditorService.CurrentSourceFolder) ? null : GameContext.EditorService.CurrentSourceFolder;
			GameContext.MapHistoryService.CreateVersion(editingDirectory, kind, label, coalesce: coalesce, sourceFolder: sourceFolder);
		}

		public static void RequestOpenFromCas(string casSourceDirectory, string? defaultSaveFolder)
		{
			GameContext.EditorService.RequestOpenFromCasDomain(casSourceDirectory, defaultSaveFolder);
		}

		public void UpdateLastMetadataSyncTime(string? path = null)
		{
			long writeTimeTicks = 0;
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;
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

			RefreshEntityPalette();
		}

		private const int AutosaveQuietMilliseconds = 1500;
		private const int AutosaveMinimumGapMilliseconds = 1500;
		private const int AutosaveMaximumDelayMilliseconds = 20000;

		private DateTime _lastAutosaveWriteUtc = DateTime.MinValue;
		private bool _hasChangesSinceLastVersion;
		private string? _reportedAutosaveFailure;
		private DateTime _autosaveRetryNotBeforeUtc = DateTime.MinValue;

		public void PerformAutoBackup()
		{
			try
			{
				if (GameContext.Host == null || GameContext.Host.GroundTerrain == null) return;
				if (!_hasChangesSinceLastVersion) return;

				string editingDirectory = GetActiveEditingDirectory();
				if (string.IsNullOrEmpty(editingDirectory) || !Directory.Exists(editingDirectory)) return;
				if (!File.Exists(Path.Combine(editingDirectory, "metadata.json"))) return;

				MapWorkspaceService.EnsureWasmEntryPoint(editingDirectory);
				GameContext.Host.SaveMapToFile(Path.Combine(editingDirectory, "terrain.json"), performReload: false);
				GameContext.Host.EditorHasUnsavedChanges = false;
				_hasChangesSinceLastVersion = false;

				RecordMapVersion(MapVersionKind.AutosaveCheckpoint, TranslationServer.Translate("Autosave"), coalesce: true);
				ShowFeedback(TranslationServer.Translate("Autosave version saved."));
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] Autosave failed: {ex.Message}");
			}
		}

		private void AutosaveEditorStateIfNeeded()
		{
			if (GameContext.Host == null) return;

			ReportFailedAutosaveWrite();

			if (!GameContext.Host.EditorHasUnsavedChanges) return;
			if (GameContext.EditorService.IsSyncing) return;
			if (GameContext.MapAutosaveService.HasPendingWrite) return;
			if (DateTime.UtcNow < _autosaveRetryNotBeforeUtc) return;

			string editingDirectory = GetActiveEditingDirectory();
			if (string.IsNullOrEmpty(editingDirectory) || !Directory.Exists(editingDirectory)) return;
			if (!File.Exists(Path.Combine(editingDirectory, "metadata.json"))) return;

			DateTime now = DateTime.UtcNow;
			if ((now - _lastAutosaveWriteUtc).TotalMilliseconds < AutosaveMinimumGapMilliseconds) return;

			bool editsHaveSettled = (now - EditorService.LastEditorChangeUtc).TotalMilliseconds >= AutosaveQuietMilliseconds;
			bool writeIsOverdue = (now - _lastAutosaveWriteUtc).TotalMilliseconds >= AutosaveMaximumDelayMilliseconds;
			if (!editsHaveSettled && !writeIsOverdue) return;

			try
			{
				_lastAutosaveWriteUtc = now;
				GameContext.Host.SaveMapToFile(
					Path.Combine(editingDirectory, "terrain.json"),
					performReload: false,
					terrainWriter: GameContext.MapAutosaveService.EnqueueTerrainWrite);
				GameContext.Host.EditorHasUnsavedChanges = false;
				_hasChangesSinceLastVersion = true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] Autosave write failed: {ex.Message}");
			}
		}

		private void ReportFailedAutosaveWrite()
		{
			string? failure = GameContext.MapAutosaveService.TakeFailedWriteMessage();
			if (failure == null)
			{
				_reportedAutosaveFailure = null;
				return;
			}

			GameContext.Host.EditorHasUnsavedChanges = true;
			_autosaveRetryNotBeforeUtc = DateTime.UtcNow + AutosaveRetryBackoff;

			if (string.Equals(failure, _reportedAutosaveFailure, StringComparison.Ordinal)) return;

			_reportedAutosaveFailure = failure;
			ShowFeedback(string.Format(TranslationServer.Translate("The map could not be written to its folder: {0}"), failure));
		}

		public void SaveMapActionExternal()
		{
			SaveMapAction();
		}

		private void InitializeEditingDirectory()
		{
			if (!GameContext.SessionState.ReturningFromTest)
			{
				EnsureEditingDirectoryReady();
			}

			string editingDirectory = GameContext.EditorService._editingDirectory;
			string initTerrainPath = string.IsNullOrEmpty(editingDirectory) ? string.Empty : Path.Combine(editingDirectory, "terrain.json");
			string initMetadataPath = string.IsNullOrEmpty(editingDirectory) ? string.Empty : Path.Combine(editingDirectory, "metadata.json");

			long terrainTime = string.IsNullOrEmpty(initTerrainPath) ? 0 : GameContext.EditorService.GetMaxTerrainWriteTime(initTerrainPath);
			long metadataTime = string.IsNullOrEmpty(initMetadataPath) ? 0 : GameContext.EditorService.GetLastWriteTimeSafe(initMetadataPath);

			GameContext.EditorService.InitializeEditingDirectoryDomain(terrainTime, metadataTime);
			ScheduleHistoryMaintenanceOnLaunch();

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

		private void EnsureEditingDirectoryReady()
		{
			try
			{
				GameContext.EditorService.EnsureEditingDirectoryDomain();
				string editingDirectory = GameContext.EditorService._editingDirectory;
				if (string.IsNullOrEmpty(editingDirectory)) return;

				Directory.CreateDirectory(editingDirectory);
				CaptureVersionAroundAssetCleanup(editingDirectory, MapVersionKind.BeforeAssetOptimization, TranslationServer.Translate("Before asset cleanup"));
				MapWorkspaceService.SetupWorkspace(editingDirectory, "MapScript");
				CaptureVersionAroundAssetCleanup(editingDirectory, MapVersionKind.AfterAssetOptimization, TranslationServer.Translate("After asset cleanup"));
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed initializing the editing directory: {ex.Message}");
			}
		}

		private static void CaptureVersionAroundAssetCleanup(string editingDirectory, MapVersionKind kind, string label)
		{
			if (!File.Exists(Path.Combine(editingDirectory, "metadata.json"))) return;

			bool firstVersion = kind == MapVersionKind.BeforeAssetOptimization && !GameContext.MapHistoryService.HasExistingVersions(editingDirectory);
			GameContext.MapHistoryService.CaptureVersionSynchronously(
				editingDirectory,
				firstVersion ? MapVersionKind.Initial : kind,
				firstVersion ? TranslationServer.Translate("First version") : label,
				editingDirectory);
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
			MapWorkspaceService.TryReloadGroundTerrainTexturesSafeDomain(() => { GameContext.IEditorAPI.ClearMap(); }
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

		public static bool HasUnsavedChangesStatic()
		{
			string editingDirectory = GameContext.EditorService._editingDirectory;
			if (string.IsNullOrEmpty(editingDirectory)) return false;

			return MapWorkspaceService.HasUnsavedChangesDomain(IsIgnoredPath, editingDirectory);
		}

		public bool HasUnsavedChanges() => HasUnsavedChangesStatic();

		public void RefreshSavedDirectoryHash()
		{
			MapWorkspaceService.RefreshSavedDirectoryHashDomain(IsIgnoredPath);
		}

		private void EstablishLaunchSavedState(Action onCompleted = null)
		{
			if (GameContext.SessionState.EditorSessionResumedAfterTest)
			{
				GameContext.SessionState.EditorSessionResumedAfterTest = false;
				RefreshSavedDirectoryHash();
				onCompleted?.Invoke();
				return;
			}

			if (GameContext.SessionState.ReturningFromTest || !string.IsNullOrEmpty(GameContext.EditorService.PendingCasSourceDirectory))
			{
				onCompleted?.Invoke();
				return;
			}

			RefreshSavedDirectoryHash();
			onCompleted?.Invoke();
		}

		private static readonly TimeSpan AutosaveRetryBackoff = TimeSpan.FromSeconds(10);

		private void OnSyncTimerTimeout()
		{
			var editorService = GameContext.EditorService;
			if (string.IsNullOrEmpty(editorService._editingDirectory) || !Directory.Exists(editorService._editingDirectory)) return;

			editorService.SyncWasmFiles();
			editorService.UpdateWorkspaceModTime();
			AutosaveEditorStateIfNeeded();
		}

		private static bool IsSameDirectory(string firstPath, string secondPath)
		{
			try
			{
				string first = Path.GetFullPath(firstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				string second = Path.GetFullPath(secondPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
			}
			catch (Exception)
			{
				return false;
			}
		}

		public static bool IsIgnoredPath(string relativePath)
		{
			return MapWorkspaceService.IsIgnoredPathDomain(relativePath);
		}

		public static bool IsPathIgnoredByRule(string normalized)
		{
			return MapWorkspaceService.IsPathIgnoredByRuleDomain(normalized);
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

		private void CopyEditingDirectoryToFolder(string targetFolder)
		{
			if (!Directory.Exists(targetFolder))
			{
				Directory.CreateDirectory(targetFolder);
			}

			string tempTerrainPath = Path.Combine(GameContext.EditorService._editingDirectory, "terrain.json");
			GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(tempTerrainPath);

			var allFiles = Directory.GetFiles(GameContext.EditorService._editingDirectory, "*", SearchOption.AllDirectories);
			var filesToCopy = new List<(string Source, string Target)>(allFiles.Length);
			var createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var file in allFiles)
			{
				string relativePath = file.Substring(GameContext.EditorService._editingDirectory.Length + 1);
				if (IsIgnoredPath(relativePath)) continue;
				string targetFile = Path.Combine(targetFolder, relativePath);
				string targetDir = Path.GetDirectoryName(targetFile);
				if (!string.IsNullOrEmpty(targetDir) && createdDirs.Add(targetDir))
				{
					Directory.CreateDirectory(targetDir);
				}

				filesToCopy.Add((file, targetFile));
			}

			Parallel.ForEach(filesToCopy, pair => { PathUtils.CopyFileClearingReadOnlyIfChanged(pair.Source, pair.Target); });

			if (OperatingSystem.IsWindows())
			{
				GameContext.VSCode.SaveRecentMapDir(targetFolder);
			}
		}

		private void AdoptMapFolderAsEditingDirectory(string targetFolder)
		{
			string fullTarget = Path.GetFullPath(targetFolder);
			Directory.CreateDirectory(fullTarget);

			GameContext.EditorService.SetActiveMapDirectory(fullTarget);
			GameContext.EditorService.SetCurrentSourceFolder(fullTarget);
			MapWorkspaceService.SetupWorkspace(fullTarget, "MapScript");
			GameContext.EditorService.StartWorkspaceWatcher(fullTarget);

			defaultLoadFolder = fullTarget;
			defaultSaveFolder = fullTarget;
			mapSaveFolder = fullTarget;
		}

		private async Task SaveMapToFolderAsync(string targetFolder)
		{
			string editingDirectory = GetActiveEditingDirectory();
			if (string.IsNullOrEmpty(editingDirectory) || !Directory.Exists(editingDirectory))
			{
				AdoptMapFolderAsEditingDirectory(targetFolder);
			}

			MapWorkspaceService.EnsureLicenseFile(GameContext.EditorService._editingDirectory);
			MapWorkspaceService.EnsureSourceMapAttributionAttribute(GameContext.EditorService._editingDirectory);
			MapWorkspaceService.EnsureWasmEntryPoint(GameContext.EditorService._editingDirectory);
			string tempTerrainPath = Path.Combine(GameContext.EditorService._editingDirectory, "terrain.json");
			if (GameContext.Host != null)
			{
				GameContext.Host.SaveMapToFile(tempTerrainPath);
				GameContext.Host.EditorHasUnsavedChanges = false;
				InvalidateMetadataCache();
			}

			ShowFeedback(TranslationServer.Translate("Saving map folder..."));

			try
			{
				editingDirectory = GetActiveEditingDirectory();
				bool savesIntoEditingFolder = IsSameDirectory(targetFolder, editingDirectory);

				GameContext.EditorService.IsSyncing = true;
				await Task.Run(() =>
				{
					GameContext.MapAttributionService.UpdateManifestAttribution(editingDirectory);
					if (!savesIntoEditingFolder)
					{
						CopyEditingDirectoryToFolder(targetFolder);
					}
				});

				if (!savesIntoEditingFolder)
				{
					GameContext.EditorService.SetActiveMapDirectory(Path.GetFullPath(targetFolder));
					GameContext.EditorService.SetCurrentSourceFolder(Path.GetFullPath(targetFolder));
					GameContext.EditorService.StartWorkspaceWatcher(Path.GetFullPath(targetFolder));
				}

				MapWorkspaceService.EnsureLicenseFile(targetFolder);
				MapWorkspaceService.EnsureWasmEntryPoint(targetFolder);

				RefreshSavedDirectoryHash();
				GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(tempTerrainPath);
				GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(Path.Combine(GameContext.EditorService._editingDirectory, "metadata.json"));

				ShowFeedback(string.Format(TranslationServer.Translate("Map saved successfully to folder {0}!"), Path.GetFileName(targetFolder)));
				RecordMapVersion(MapVersionKind.ManualSave, TranslationServer.Translate("Save"));
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] SaveMapToFolderAsync failed: {ex}");
				ShowFeedback(string.Format(TranslationServer.Translate("Failed to save map: {0}"), ex.Message));
			}
			finally
			{
				GameContext.EditorService.IsSyncing = false;
			}
		}

		private void SaveMapAction()
		{
			if (GameContext.Host == null) return;

			if (!string.IsNullOrEmpty(mapSaveFolder) && !IsUntitledMapFolder())
			{
				_ = SaveMapToFolderAsync(mapSaveFolder);
				return;
			}

			PromptSaveMapFolder();
		}

		private static bool IsUntitledMapFolder()
		{
			string editingDirectory = GetActiveEditingDirectory();
			if (string.IsNullOrEmpty(editingDirectory)) return false;

			return IsSameDirectory(editingDirectory, MapWorkspaceService.GetUntitledMapDirectory());
		}

		private void SaveAsMapAction()
		{
			if (GameContext.Host == null) return;
			PromptSaveMapFolder();
		}

		public void PromptSaveMapFolder()
		{
			if (GameContext.Host == null) return;

			EnsureMapHasRealName(mapName => OpenSaveMapFolderPicker(GetDefaultDevelopmentMapDirectory(mapName)));
		}

		private void EnsureMapHasRealName(Action<string> onReady)
		{
			string mapName = GetMapNameFromMetadata();
			if (!MapWorkspaceService.IsUntitledMapName(mapName))
			{
				onReady?.Invoke(mapName);
				return;
			}

			ShowMapNamePrompt(named =>
			{
				ApplyMapName(named);
				onReady?.Invoke(named);
			});
		}

		private void ApplyMapName(string mapName)
		{
			string editingDirectory = GetActiveEditingDirectory();
			if (string.IsNullOrEmpty(editingDirectory) || !File.Exists(Path.Combine(editingDirectory, "metadata.json"))) return;

			GameContext.MetadataService.UpdateMetadata(editingDirectory, metadata =>
			{
				metadata.MapProperties ??= new MapInfoMetadata();
				metadata.MapProperties.MapName = mapName;
			});

			GameContext.EditorService.CachedMapName = null;
			GameContext.EditorService.LastMapNameCacheTicks = 0;
			UpdateMapNameHeader();
			_mapSettingsDialog?.LoadMapProperties();
		}

		private void OpenSaveMapFolderPicker(string suggestedDirectory)
		{
			string initialDir = !string.IsNullOrEmpty(suggestedDirectory) ? suggestedDirectory : (!string.IsNullOrEmpty(defaultSaveFolder) ? defaultSaveFolder : GetDocumentsDirectory());
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
			GameContext.EditorService.SetCurrentSourceFolder(fullSelectedPath);
			defaultLoadFolder = fullSelectedPath;
			defaultSaveFolder = fullSelectedPath;
			mapSaveFolder = fullSelectedPath;

			_ = SaveMapToFolderAsync(fullSelectedPath);
		}

		public void LoadMapAction()
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
			if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
			{
				ShowFeedback(TranslationServer.Translate("Map package could not be imported: the file does not exist."));
				return false;
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
				return false;
			}

			var (manifestJson, _) = MapArchiveHelper.ReadManifestFromArchive(archivePath);
			if (string.IsNullOrWhiteSpace(manifestJson))
			{
				ShowFeedback(TranslationServer.Translate("The map package does not contain a readable map."));
				return false;
			}

			GameContext.Host?.ClearMapEntirely();
			GameContext.EditorService.IsSyncing = true;

			ShowFeedback(TranslationServer.Translate("Extracting map package into the editor workspace..."));
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
				GD.PrintErr($"[MapEditorHUD] Failed to extract map package {archivePath}: {ex}");
				ShowFeedback(string.Format(TranslationServer.Translate("Failed to extract the map package: {0}"), ex.Message));
				GameContext.EditorService.IsSyncing = false;
				return false;
			}

			if (!GameContext.EditorService.IsValidMapFolder(workspacePath))
			{
				ShowInvalidMapFolderErrorDialog(workspacePath);
				ShowFeedback(TranslationServer.Translate("The map package does not contain a readable map."));
				GameContext.EditorService.IsSyncing = false;
				return false;
			}

			if (GameContext.EditorService.IsMapFolderCorrupt(workspacePath, out string corruptFile, out string corruptError))
			{
				ShowCorruptMapFileErrorDialog(workspacePath, corruptFile, corruptError);
				ShowFeedback(string.Format(TranslationServer.Translate("Map file '{0}' is corrupt: {1}"), corruptFile, corruptError));
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
				GameContext.EditorService.StartWorkspaceWatcher(workspacePath);
				mapSaveFolder = null;
				defaultSaveFolder = GetDocumentsDirectory();
				RefreshSavedDirectoryHash();
				ShowFeedback(TranslationServer.Translate("Map package imported. Choose a destination folder when you save."));
				return true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapEditorHUD] Failed to import map package {archivePath}: {ex}");
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
				GD.PrintErr($"[MapEditorHUD] Failed to load map folder: {ex.Message}");
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
				GD.PrintErr($"[MapEditorHUD] The scaffolding in '{editingDirectory}' is incomplete because another program is holding a file open: {exception.Message}. The map still opens and the scaffolding is repaired on its next load.");
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
			GameContext.EditorService?.StartWorkspaceWatcher(GameContext.EditorService._editingDirectory);
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
				GD.PrintErr($"[MapEditorHUD] Failed to load map folder: {ex.Message}");
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
				GameContext.EditorService?.StartWorkspaceWatcher(GameContext.EditorService._editingDirectory);
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

		public static void ResetFolderLocations()
		{
			mapSaveFolder = null;
			defaultSaveFolder = GetDocumentsDirectory();
			GameContext.EditorService.LastUsedFolder = null;
			GameContext.EditorService.SetCurrentSourceFolder(null);
			GameContext.EditorService.PendingCasSourceDirectory = null;
			GameContext.EditorService.PendingDefaultSaveFolder = null;
			GameContext.EditorService.ClearEditingDirectoryDomain();
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

		public string GetInitialDirectory()
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
			string mapBackups = Path.GetFullPath(ProjectSettings.GlobalizePath("user://map_backups")).Replace("\\", "/").TrimEnd('/');
			string userDir = Path.GetFullPath(ProjectSettings.GlobalizePath("user://")).Replace("\\", "/").TrimEnd('/');
			string resDir = Path.GetFullPath(ProjectSettings.GlobalizePath("res://")).Replace("\\", "/").TrimEnd('/');

			return GameContext.EditorService.IsRestrictedSaveDirectoryDomain(fullPath, mapBackups, userDir, resDir);
		}


		private NSec.Cryptography.Key? GetSelectedAuthorshipKey()
		{
			return TryGetSelectedAuthorshipKey(out NSec.Cryptography.Key? key, out _, out _) ? key : null;
		}


		private void SaveMapProperties()
		{
			if (GameContext.Host == null) return;

			_mapSettingsDialog?.SaveMapProperties();
		}

		private async Task CompileAndSignMapAsync(string workspace, bool skipAttribution = true, bool isDebug = false)
		{
			_wasmHasErrors = false;

			if (!skipAttribution)
			{
				await GameContext.MapHistoryService.CreateVersionAsync(workspace, MapVersionKind.BeforePublish, TranslationServer.Translate("Before publish"), GameContext.EditorService.CurrentSourceFolder);
			}

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
				var authorshipKey = GetSelectedAuthorshipKey();
				if (authorshipKey == null)
				{
					ShowFeedback(TranslationServer.Translate("Sign in and choose an authorship key to sign this export."));
					GD.PrintErr("[MapEditorHUD] Signing skipped: no authorship key is selected.");
					return;
				}

				string pubKeyStr = Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
				string seedServerUrl = GameContext.Host != null && GodotObject.IsInstanceValid(GameContext.Lobby) ? GameContext.Lobby.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
				string currentUsername = await FetchCurrentUsernameAsync(pubKeyStr, seedServerUrl) ?? GetPublishAuthorName();

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

		public void OpenAssetBrowser(string title, IEnumerable<string> allowedExtensions, Action<IReadOnlyList<AssetBrowserSelection>> onAssetsSelected, bool requireRealmMetadata = false, string? requiredAssetType = null, bool allowMultiSelect = true)
		{
			if (_assetBrowserDialog == null)
			{
				_assetBrowserDialog = new AssetBrowserDialog(this);
			}

			_assetBrowserDialog.OpenForImport(title, allowedExtensions, onAssetsSelected, requireRealmMetadata, requiredAssetType, allowMultiSelect);
		}

		public void OpenImportFromMapDialog(Action? onImportCompleted = null)
		{
			if (_importFromMapDialog == null)
			{
				_importFromMapDialog = new ImportFromMapDialog(this);
			}

			_importFromMapDialog.OpenForWorkspace(onImportCompleted);
		}

		public void SaveUnitObjectAttachment(string unitId, HumanoidBone hand, string attachmentId, HandAttachmentOrientation orientation)
		{
			string wsPath = GameContext.EditorService._editingDirectory;
			GameContext.RegistryService.SaveUnitObjectAttachment(unitId, hand, attachmentId, orientation, wsPath);
		}

		public void SaveUnitObjectAttachment(string unitId, string socket, string attachmentId, HandAttachmentOrientation orientation)
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;
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
				wsPath = GameContext.EditorService._editingDirectory;
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

		public void SaveUnitPseudoSockets(string targetId, List<PseudoSocketAnchor>? pseudoSockets)
		{
			GameContext.RegistryService.SaveUnitPseudoSockets(targetId, pseudoSockets, (t, a) => RestoreUnitPseudoSockets(t, a));
		}

		public void SaveCustomWeaponToMetadata(string weaponId, WeaponMetadata weapon)
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;
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
				string wsPath = GameContext.EditorService._editingDirectory;
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
				string wsPath = GameContext.EditorService._editingDirectory;
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

				GameContext.Host?.LoadUnitMetadata(GameContext.EditorService.GetWorkspacePath());
				if (fieldName != "PortraitModelPath")
				{
					GameContext.Host?.RefreshAllPlacedObjectModels(entityId);
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
			if (GameContext.Host == null) return;

			EnsureMapHasRealName(_ => OpenExportFileDialogForCurrentMap());
		}

		private void OpenExportFileDialogForCurrentMap()
		{
			string wsPath = !string.IsNullOrEmpty(GameContext.EditorService._editingDirectory) ? GameContext.EditorService._editingDirectory : MapWorkspaceService.GetActiveWorkspacePath();

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
			if (GameContext.Host == null) return;

			string mapTitle = GetMapNameFromMetadata();
			string mapVersion = GetMapVersionFromMetadata();
			string manifestBlake3 = GetManifestBlake3();
			destinationPath = GameContext.MapStorageService.ResolveExportDestinationPath(destinationPath, mapTitle, mapVersion, manifestBlake3);
			var (popup, progressBar, statusLabel, closeBtn) = CreateExportUI(destinationPath);
			SetupWasmConsoleForExport();

			try
			{
				string? exportedPath = await PerformExportStepsAsync(progressBar, statusLabel, closeBtn, destinationPath, fullExport);
				if (string.IsNullOrEmpty(exportedPath))
				{
					return;
				}

				FinalizeSuccessfulExport(popup, exportedPath, statusLabel, progressBar);
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
			return UIStyle.CreateExportUI(this, destinationPath);
		}

		private void SetupWasmConsoleForExport()
		{
			GameContext.WasmConsole.Hide();
			_wasmHasErrors = false;
			_exportLogHandler = line => AppendWasmConsoleLog(line);
			GameLog.OnLine += _exportLogHandler;
		}

		private Action<string> _exportLogHandler { get; set; }

		private async Task<string?> PerformExportStepsAsync(ProgressBar progressBar, Label statusLabel, Button closeBtn, string destinationPath, bool fullExport)
		{
			await SaveMapStateAsync(progressBar, statusLabel);

			if (!await VerifyMapAssetsAsync(progressBar, statusLabel, closeBtn)) return null;

			if (!await CompileMapScriptAsync(progressBar, statusLabel, closeBtn)) return null;

			GameContext.MapStorageService.DeduplicateWorkspaceBuildArtifacts(GameContext.EditorService._editingDirectory);

			await OptimizeMapAssetsAsync(progressBar, statusLabel);

			await GenerateMapMetadataAsync(progressBar, statusLabel);

			return await CreateMapArchiveAsync(progressBar, statusLabel, destinationPath, fullExport);
		}

		private async Task SaveMapStateAsync(ProgressBar progressBar, Label statusLabel)
		{
			progressBar.Value = 10;
			statusLabel.Text = TranslationServer.Translate("Saving map state & workspace...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string editingDirectory = GameContext.EditorService._editingDirectory;
			await GameContext.MapStorageService.SaveMapStateForExportAsync(editingDirectory, OperatingSystem.IsWindows());
		}

		private async Task<bool> VerifyMapAssetsAsync(ProgressBar progressBar, Label statusLabel, Button closeBtn)
		{
			progressBar.Value = 15;
			statusLabel.Text = TranslationServer.Translate("Verifying map assets...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string editingDirectory = GameContext.EditorService._editingDirectory;
			var validationResult = GameContext.MapStorageService.ValidateMapAssetsForExport(editingDirectory);

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

			string editingDirectory = GameContext.EditorService._editingDirectory;
			await CompileAndSignMapAsync(editingDirectory, skipAttribution: true);

			if (_wasmHasErrors)
			{
				AbortExport(progressBar, statusLabel, closeBtn, "WASM compilation failed. Export aborted.", null);
				ShowFeedback(TranslationServer.Translate("WASM compilation failed. Export aborted."));
				return false;
			}

			if (!GameContext.MapStorageService.CheckCompiledWasmBinaryExists(editingDirectory))
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

			string editingDirectory = GameContext.EditorService._editingDirectory;
			var storageService = GameContext.MapStorageService;

			await storageService.OptimizeMapAssetsCooperativeAsync(editingDirectory, async (current, total, fileName) =>
			{
				float fraction = total > 0 ? (float)current / total : 1.0f;
				progressBar.Value = 35 + fraction * 20;
				statusLabel.Text = string.Format(TranslationServer.Translate("Optimizing 3D model {0}/{1}: {2}..."), current, total, fileName);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			});

			progressBar.Value = 55;
			statusLabel.Text = TranslationServer.Translate("Converting textures...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			await storageService.ConvertMapTexturesCooperativeAsync(editingDirectory, async (current, total, fileName) =>
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

			string editingDirectory = GameContext.EditorService._editingDirectory;
			if (_minimapController != null)
			{
				await _minimapController.GenerateAndSaveMinimapThumbnailAsync(editingDirectory);
			}

			progressBar.Value = 75;
			statusLabel.Text = TranslationServer.Translate("Generating manifest & indexing asset hashes...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string mapTitle = GetMapNameFromMetadata();
			string mapVersion = GetMapVersionFromMetadata();
			string author = GetPublishAuthorName();

			GameContext.MapStorageService.GenerateMapManifest(editingDirectory, mapTitle, mapVersion, author);
		}


		private async Task<string> CreateMapArchiveAsync(ProgressBar progressBar, Label statusLabel, string destinationPath, bool fullExport)
		{
			progressBar.Value = 80;
			statusLabel.Text = TranslationServer.Translate("Compressing package into .rmap archive...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string editingDirectory = GameContext.EditorService._editingDirectory;
			long lastProgressUpdateTicks = 0;

			return await GameContext.MapStorageService.CreateMapArchiveAsync(editingDirectory, destinationPath, fullExport, (pct, file) =>
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
			GameContext.UserInterface?.ShowConfirmationDialog(
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
				GameLog.OnLine -= _exportLogHandler;
				_exportLogHandler = null;
			}

			if (_wasmHasErrors)
			{
				GameContext.WasmConsole.ShowConsole();
			}
			else
			{
				GameContext.WasmConsole.Hide();
			}
		}

		private Texture2D LoadSwatchTextureInternal(int i)
		{
			string wsPath = GameContext.EditorService._editingDirectory;
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
			if (GameContext.Host == null) return;
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
				GameContext.EditorService.CopyOrProcessTexture(imagePath, outputRtex, GameContext.Host);
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
			GameContext.EditorService.UpdateTextureMetadataAndReload(index, cleanBaseName, outputRtex, GameContext.Host, (cat, name, blake, slot) => UpdateMetadataJsonAsset(cat, name, blake, targetSlot: slot));
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
			return GameContext.EditorService.SanitizeMapName(candidateName);
		}

		public string GetMapVersionFromMetadata(bool forceReload = false)
		{
			string workspacePath = !string.IsNullOrEmpty(GameContext.EditorService._editingDirectory) ? GameContext.EditorService._editingDirectory : MapWorkspaceService.GetActiveWorkspacePath();
			return GameContext.EditorService.GetMapVersionFromMetadata(forceReload, workspacePath);
		}

		public string GetManifestBlake3()
		{
			string workspacePath = !string.IsNullOrEmpty(GameContext.EditorService._editingDirectory) ? GameContext.EditorService._editingDirectory : MapWorkspaceService.GetActiveWorkspacePath();
			string mapTitle = GetMapNameFromMetadata();
			string mapVersion = GetMapVersionFromMetadata();
			string author = GetPublishAuthorName();
			return GameContext.EditorService.GetManifestBlake3(workspacePath, mapTitle, mapVersion, author);
		}

		public void ImportTextureAssetFromExtension(string sourceFilePath, int slotIndex)
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;

				bool success = GameContext.EditorService.ImportTextureAssetFromExtensionDomain(
					sourceFilePath, slotIndex, wsPath, GameContext.Host,
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
				string wsPath = GameContext.EditorService._editingDirectory;
				string metadataPath = Path.Combine(wsPath, "metadata.json");

				string texDir = Path.Combine(wsPath, "Assets", "textures");
				Directory.CreateDirectory(texDir);

				if (File.Exists(metadataPath))
				{
					MapWorkspaceService.NormalizeMetadataTextureEntries(wsPath);
				}

				InvalidateMetadataCache();

				_swatchTextureCache.Clear();
				if (GameContext.Host != null && GameContext.Host.GroundTerrain != null)
				{
					GameContext.Host.GroundTerrain.ReloadTerrainTextures(true);
				}

				SetupTextureSwatches(false);
				RefreshSkyboxList();
				if (File.Exists(metadataPath))
				{
					GameContext.Host?.LoadModelYOffsetsFromMetadataJson(wsPath);
					GameContext.Host?.LoadUnitMetadata(wsPath);
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
				string wsPath = GameContext.EditorService._editingDirectory;
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
				ShowFeedback(string.Format(TranslationServer.Translate("Imported animation {0}"), result.FinalFileName));
			}
			else
			{
				UpdateMetadataJsonAsset("animations", result.FinalFileName, result.NewHash);
				ShowFeedback(TranslationServer.Translate("Animation already imported (identical BLAKE3 hash)."));
			}
		}

		public void ImportDecalAssetFromExtension(string sourceFilePath)
		{
			string wsPath = GameContext.EditorService._editingDirectory;

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
			string wsPath = GameContext.EditorService._editingDirectory;
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
			string wsPath = GameContext.EditorService._editingDirectory;
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
			string wsPath = GameContext.EditorService._editingDirectory;
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
			string wsPath = GameContext.EditorService._editingDirectory;

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

		public void ResetEditingDirectorySyncState()
		{
			GameContext.EditorService.ResetEditingDirectorySyncStateDomain();
			_wasmHasErrors = false;
			_wasmCompileLogPath = "";
		}

		public void GenerateVSCodeFilesExternal()
		{
			GameContext.EditorService.GenerateVSCodeFilesExternalDomain();
		}
	}
}
