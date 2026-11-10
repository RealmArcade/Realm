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
				GameLog.LogError($"[MapEditorHUD] Could not write the current map before restoring a version: {ex.Message}");
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
			string? wsPath = string.IsNullOrEmpty(targetPath) ? MapWorkspaceService.GetActiveWorkspacePath() : (Directory.Exists(targetPath) ? targetPath : Path.GetDirectoryName(targetPath));
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
				GameLog.LogError($"[MapEditorHUD] Autosave failed: {ex.Message}");
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

			bool editsHaveSettled = (now - EditorWorkspaceWatcher.LastEditorChangeUtc).TotalMilliseconds >= AutosaveQuietMilliseconds;
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
				GameLog.LogError($"[MapEditorHUD] Autosave write failed: {ex.Message}");
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

			if (GameContext.Host is { } host) host.EditorHasUnsavedChanges = true;
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
			syncTimer.Name = "SyncTimer_Timer";
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
				GameLog.LogError($"Failed initializing the editing directory: {ex.Message}");
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
				GameLog.LogError($"LoadMapProperties error: {ex.Message}");
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

		private void EstablishLaunchSavedState(Action? onCompleted = null)
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
			return EditorWorkspace.GetDefaultDevelopmentMapDirectory(name, docs);
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

			return EditorWorkspace.IsRestrictedSaveDirectory(fullPath, mapBackups, userDir, resDir);
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

		private async Task CompileWorkspaceScriptAsync(string workspace, bool isDebug = false)
		{
			_wasmHasErrors = false;

			bool compiledSuccessfully = await PerformCompilationStepAsync(workspace, isDebug);
			if (compiledSuccessfully)
			{
				GameContext.MapStorageService.DeduplicateWorkspaceBuildArtifacts(workspace);
			}
		}

		private async Task<bool> PerformCompilationStepAsync(string workspace, bool isDebug = false)
		{
			return await GameContext.EditorService.PerformCompilationStepAsyncDomain(workspace, FindProjectFile, TryBypassCompilation, CheckMapApiDependency, ExecuteDotnetPublishAsync, msg => { _wasmHasErrors = true; SetWasmConsoleStatus($"❌ WASM Compilation Failed: {msg}", new Color(1.0f, 0.3f, 0.3f)); GameLog.LogError($"[MapEditorHUD] Trigger compilation failed: {msg}"); }, err => AppendWasmConsoleLog($"[COMPILER EXCEPTION] {err}"), isDebug);
		}

		private string? FindProjectFile(string workspace)
		{
			return EditorMapBuild.FindProjectFile(workspace, msg => { _wasmHasErrors = true; SetWasmConsoleStatus("❌ " + msg, new Color(1.0f, 0.3f, 0.3f)); AppendWasmConsoleLog(msg); GameLog.LogError(msg); });
		}

		private bool TryBypassCompilation(string workspace, bool isDebug)
		{
			return EditorMapBuild.TryBypassCompilation(workspace, FindExistingWasmFile, HasNewerSourceFiles, () => { _wasmHasErrors = false; SetWasmConsoleStatus("✓ WASM Compilation Bypassed (Unchanged)", UIStyle.ColorCyanGlow); AppendWasmConsoleLog($"[INFO] The script sources are unchanged since the last {(isDebug ? "debug" : "release")} build. Bypassing compilation using the WASM binary already in bin/script."); }, isDebug);
		}

		private string? FindExistingWasmFile(string workspace)
		{
			return MapWorkspaceService.FindExistingWasmFileDomain(workspace);
		}

		private bool HasNewerSourceFiles(string workspace, DateTime wasmTime)
		{
			string? currentSourceFolder = GameContext.EditorService.CurrentSourceFolder;
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
			if (!DotnetSdkResolver.IsSdkAvailable())
			{
				_wasmHasErrors = true;
				SetWasmConsoleStatus("⏳ " + TranslationServer.Translate("WASM Compilation Waiting: the map compiler is still installing"), UIStyle.ColorCyanGlow);
				AppendWasmConsoleLog("[WAIT] " + TranslationServer.Translate("The .NET SDK that compiles map scripts is not installed yet, so the download was started in the background. Terrain and object editing work without it."));
				AppendWasmConsoleLog("[WAIT] " + TranslationServer.Translate("Choose Test Map again once the map compiler has finished installing. If the download cannot start, check your internet connection and reopen the Map Editor to retry."));
				DotnetSdkInstaller.EnsureInstalledInBackground();
				return false;
			}

			var result = await GameContext.MapStorageService.ExecuteDotnetPublishDomainAsync(workspace, csproj,
				(output) => AppendWasmConsoleLog(output),
				(error) => AppendWasmConsoleLog(error),
				isDebug);

			if (!result.success)
			{
				_wasmHasErrors = true;
				SetWasmConsoleStatus($"❌ WASM Compilation Failed (exit code {result.exitCode})", new Color(1.0f, 0.3f, 0.3f));
				AppendWasmConsoleLog($"[ERROR] dotnet publish failed with exit code {result.exitCode}");
				GameLog.LogError($"[MapEditorHUD] Map script compilation failed (exit code {result.exitCode})");
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

		private async Task SignMapMetadataAsync(string workspace, string mapTitle, string mapVersion, string author)
		{
			NSec.Cryptography.Key? authorshipKey = GetSelectedAuthorshipKey();
			if (authorshipKey == null)
			{
				ShowFeedback(TranslationServer.Translate("Sign in and choose an authorship key to sign this export."));
				AppendWasmConsoleLog("[WARN] The map was left unsigned: no authorship key is selected.");
				return;
			}

			string publicKeyBase64 = Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
			string seedServerUrl = GameContext.Host != null && GodotObject.IsInstanceValid(GameContext.Lobby) ? GameContext.Lobby.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();
			bool signed = await GameContext.MapStorageService.SignMapMetadataAsync(workspace, mapTitle, mapVersion, author, authorshipKey, publicKeyBase64, seedServerUrl);
			AppendWasmConsoleLog(signed ? "[INFO] metadata.json carries its author key, its contributor list and its signature." : "[WARN] metadata.json could not be signed.");
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
			return GameContext.EditorService.MapIdentity.SanitizeName(candidateName);
		}

		public string GetMapVersionFromMetadata(bool forceReload = false)
		{
			string workspacePath = !string.IsNullOrEmpty(GameContext.EditorService._editingDirectory) ? GameContext.EditorService._editingDirectory : MapWorkspaceService.GetActiveWorkspacePath();
			return GameContext.EditorService.MapIdentity.GetVersionFromMetadata(forceReload, workspacePath);
		}

		public string GetManifestBlake3()
		{
			string workspacePath = !string.IsNullOrEmpty(GameContext.EditorService._editingDirectory) ? GameContext.EditorService._editingDirectory : MapWorkspaceService.GetActiveWorkspacePath();
			string mapTitle = GetMapNameFromMetadata();
			string mapVersion = GetMapVersionFromMetadata();
			string author = GetPublishAuthorName();
			return GameContext.EditorService.MapIdentity.GetManifestBlake3(workspacePath, mapTitle, mapVersion, author);
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
