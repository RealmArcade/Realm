using Godot;
using NSec.Cryptography;
using Realm.EditorAPI;
using Realm.Client.Services;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using PasteReflection = Realm.Ecs.Components.Core.PasteReflection;
using TerrainCell = Realm.Ecs.Components.Terrain.TerrainCell;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;

namespace Realm.Client.UI
{
    public partial class MapEditorHUD
    {

        public static void RequestOpenFromCas(string casSourceDirectory, string? defaultSaveFolder)
        {
            _pendingCasSourceDirectory = casSourceDirectory;
            _pendingDefaultSaveFolder = defaultSaveFolder;
        }

        public void UpdateLastMetadataSyncTime(string? path = null)
        {
            string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                : _tempWorkspacePath;
            string metadataPath = string.IsNullOrEmpty(path) ? System.IO.Path.Combine(wsPath, "metadata.json") : path;
            _lastMetadataSyncTime = Math.Max(GetLastWriteTimeSafe(metadataPath), DateTime.UtcNow.Ticks);
        }

        private void OnMetadataSaved(string targetPath)
        {
            UpdateLastMetadataSyncTime(targetPath);
        }

        public void PerformAutoBackup()
        {
            try
            {
                if (Realm.Client.Core.GameHost.Instance == null || Realm.Client.Core.GameHost.Instance.GroundTerrain == null) return;
                string wsPath = Realm.Client.Services.MapWorkspaceService.GetActiveWorkspacePath();
                if (string.IsNullOrEmpty(wsPath) || !System.IO.Directory.Exists(wsPath)) return;

                string tempTerrainPath = System.IO.Path.Combine(wsPath, "terrain.json");
                Realm.Client.Core.GameHost.Instance.SaveMapToFile(tempTerrainPath, performReload: false);
                _lastTerrainSyncTime = GetMaxTerrainWriteTime(tempTerrainPath);
                _lastMetadataSyncTime = GetLastWriteTimeSafe(System.IO.Path.Combine(wsPath, "metadata.json"));
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
            // _tempWorkspacePath = ProjectSettings.GlobalizePath(TempWorkspaceGodotPath);
            if (!ReturningFromTest)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(_tempWorkspacePath);
                    Realm.Client.Services.MapWorkspaceService.SetupWorkspace(_tempWorkspacePath, "MapScript");
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"Failed initializing temp workspace: {ex.Message}");
                }
            }

            try
            {
                if (System.IO.Directory.Exists(_tempWorkspacePath))
                {
                    foreach (var rmapFile in System.IO.Directory.GetFiles(_tempWorkspacePath, "*.rmap", System.IO.SearchOption.TopDirectoryOnly))
                    {
                        try { System.IO.File.Delete(rmapFile); } catch { }
                    }
                }
            }
            catch { }

            string initTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
            string initMetadataPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");
            _lastTerrainSyncTime = GetMaxTerrainWriteTime(initTerrainPath);
            _lastMetadataSyncTime = GetLastWriteTimeSafe(initMetadataPath);
            _editorService?.StartWorkspaceWatcher(_tempWorkspacePath);

            try
            {
                Realm.Client.Animation.RealmDefaultAnimations.EnsureDefaultTemplateAnimations(_tempWorkspacePath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapEditorHUD] Pre-warming default animations error: {ex.Message}");
            }

            var syncTimer = new global::Godot.Timer();
            syncTimer.WaitTime = 1.0f;
            syncTimer.Autostart = true;
            syncTimer.Timeout += OnSyncTimerTimeout;
            AddChild(syncTimer);

            try
            {
                LoadMapProperties();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"LoadMapProperties error: {ex.Message}");
            }

            if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.GroundTerrain != null)
            {
                try
                {
                    Realm.Client.Core.GameHost.Instance.GroundTerrain.ReloadTerrainTextures(false);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"ReloadTerrainTextures error during workspace init: {ex.Message}. Resetting to blank map.");
                    Realm.Client.Core.GameHost.Instance.ClearMapEntirely();
                }
            }

            if (!string.IsNullOrEmpty(_pendingCasSourceDirectory) && System.IO.Directory.Exists(_pendingCasSourceDirectory))
            {
                string casDir = _pendingCasSourceDirectory;
                string? defaultSave = _pendingDefaultSaveFolder;
                _pendingCasSourceDirectory = null;
                _pendingDefaultSaveFolder = null;

                _ = OpenFromCasAsync(casDir, defaultSave);
            }

        }

        private async System.Threading.Tasks.Task OpenFromCasAsync(string casSourceDirectory, string? defaultSaveFolder)
        {
            bool loaded = await LoadMapFolderAsync(casSourceDirectory);
            if (loaded && !string.IsNullOrEmpty(defaultSaveFolder))
            {
                _lastUsedFolder = defaultSaveFolder;
                _currentSourceFolder = defaultSaveFolder;
                if (!IsRestrictedSaveDirectory(defaultSaveFolder))
                {
                    GameSettings.LastOpenedFolder = defaultSaveFolder;
                    GameSettings.Save();
                }
            }
        }

        public static string ComputeDirectoryBlake3(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath) || !System.IO.Directory.Exists(directoryPath)) return string.Empty;

            try
            {
                var files = System.IO.Directory.GetFiles(directoryPath, "*", System.IO.SearchOption.AllDirectories)
                    .Where(f => !IsIgnoredPath(f.Substring(directoryPath.Length).TrimStart('/', '\\')))
                    .OrderBy(f => System.IO.Path.GetRelativePath(directoryPath, f).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                using var hasher = Blake3.Hasher.New();
                byte[] buffer = new byte[65536];

                foreach (var file in files)
                {
                    try
                    {
                        string relPath = System.IO.Path.GetRelativePath(directoryPath, file).Replace('\\', '/');
                        byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(relPath);
                        hasher.Update(pathBytes);

                        using var fs = System.IO.File.OpenRead(file);
                        int read;
                        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            hasher.Update(new ReadOnlySpan<byte>(buffer, 0, read));
                        }
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[ComputeDirectoryBlake3] Error hashing {file}: {ex.Message}");
                    }
                }

                return hasher.Finalize().ToString();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ComputeDirectoryBlake3] Error scanning directory {directoryPath}: {ex.Message}");
                return string.Empty;
            }
        }

        public static bool HasUnsavedChangesStatic()
        {
            string tempPath = ProjectSettings.GlobalizePath(TempWorkspaceGodotPath);
            if (string.IsNullOrEmpty(tempPath) || !System.IO.Directory.Exists(tempPath))
            {
                return false;
            }
            if (string.IsNullOrEmpty(CurrentDirectoryBlake3))
            {
                return false;
            }
            string currentHash = ComputeDirectoryBlake3(tempPath);
            return !string.Equals(currentHash, CurrentDirectoryBlake3, StringComparison.OrdinalIgnoreCase);
        }

        public bool HasUnsavedChanges() => HasUnsavedChangesStatic();

        public void SaveCurrentDirectoryBlake3()
        {
            if (string.IsNullOrEmpty(_tempWorkspacePath) || !System.IO.Directory.Exists(_tempWorkspacePath)) return;
            try
            {
                string hash = ComputeDirectoryBlake3(_tempWorkspacePath);
                CurrentDirectoryBlake3 = hash;
                string saveFile = ProjectSettings.GlobalizePath("user://editor_last_save.txt");
                System.IO.File.WriteAllText(saveFile, hash);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveCurrentDirectoryBlake3] Error writing editor_last_save.txt: {ex.Message}");
            }
        }

        private void WriteCleanExitMarker()
        {
            try
            {
                SaveCurrentDirectoryBlake3();
                string cleanExitFile = ProjectSettings.GlobalizePath("user://editor_clean_exit.txt");
                System.IO.File.WriteAllText(cleanExitFile, CurrentDirectoryBlake3 ?? string.Empty);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapEditorHUD] Error writing editor_clean_exit.txt: {ex.Message}");
            }
        }

        private void CheckUnsavedSessionOnLaunch(Action onCompleted = null)
        {
            if (ReturningFromTest)
            {
                onCompleted?.Invoke();
                return;
            }

            if (!string.IsNullOrEmpty(_pendingCasSourceDirectory))
            {
                onCompleted?.Invoke();
                return;
            }

            string cleanExitFile = ProjectSettings.GlobalizePath("user://editor_clean_exit.txt");
            if (System.IO.File.Exists(cleanExitFile))
            {
                try
                {
                    System.IO.File.Delete(cleanExitFile);
                }
                catch { }
                SaveCurrentDirectoryBlake3();
                onCompleted?.Invoke();
                return;
            }

            string editorLastSaveFile = ProjectSettings.GlobalizePath("user://editor_last_save.txt");
            if (!System.IO.File.Exists(editorLastSaveFile))
            {
                SaveCurrentDirectoryBlake3();
                onCompleted?.Invoke();
                return;
            }

            string savedHash = System.IO.File.ReadAllText(editorLastSaveFile).Trim();
            string currentHash = ComputeDirectoryBlake3(_tempWorkspacePath);
            CurrentDirectoryBlake3 = savedHash;

            if (!string.IsNullOrEmpty(savedHash) && !currentHash.Equals(savedHash, StringComparison.OrdinalIgnoreCase))
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
                    _isSyncing = true;
                    try
                    {
                        ResetFolderLocations();
                        ClearTempWorkspaceExternal();
                        Realm.Client.Services.MapWorkspaceService.SetupWorkspace(_tempWorkspacePath, "MapScript");
                        Realm.Client.Core.GameHost.Instance?.ClearMapEntirely();
                        string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
                        string metadataPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");
                        _lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
                        _lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
                        SaveCurrentDirectoryBlake3();
                        ReadMetadataAndRefreshTextures();
                        LoadMapProperties();
                        UpdateMapNameHeader();
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[ShowUnsavedSessionModal] Error discarding session: {ex.Message}");
                    }
                    finally
                    {
                        _isSyncing = false;
                    }
                },
                onDismissed: () =>
                {
                    onCompleted?.Invoke();
                }
            );
        }

        private void LoadTempWorkspaceMap()
        {
            string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
            Realm.Client.Services.MapWorkspaceService.EnsureGlbAssetsOptimized(_tempWorkspacePath);
            Realm.Client.Services.MapWorkspaceService.EnsurePngAssetsConverted(_tempWorkspacePath);
            LoadMapProperties();
            ReadMetadataAndRefreshTextures();
            if (Realm.Client.Core.GameHost.Instance != null && System.IO.File.Exists(terrainPath))
            {
                Realm.Client.Core.GameHost.Instance.LoadMapFromFile(terrainPath);
            }
            _lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
            _lastMetadataSyncTime = GetLastWriteTimeSafe(System.IO.Path.Combine(_tempWorkspacePath, "metadata.json"));
            ShowFeedback(TranslationServer.Translate("Restored map workspace from last session!"));
        }

        public void ClearTempWorkspaceExternal()
        {
            if (string.IsNullOrEmpty(_tempWorkspacePath) || !System.IO.Directory.Exists(_tempWorkspacePath)) return;

            try
            {
                ClearDirectoryReadOnly(_tempWorkspacePath);
                foreach (var file in System.IO.Directory.GetFiles(_tempWorkspacePath, "*", System.IO.SearchOption.AllDirectories))
                {
                    var fileAttributes = System.IO.File.GetAttributes(file);
                    if ((fileAttributes & System.IO.FileAttributes.ReadOnly) == System.IO.FileAttributes.ReadOnly)
                    {
                        System.IO.File.SetAttributes(file, fileAttributes & ~System.IO.FileAttributes.ReadOnly);
                    }
                    System.IO.File.Delete(file);
                }

                foreach (var directory in System.IO.Directory.GetDirectories(_tempWorkspacePath))
                {
                    System.IO.Directory.Delete(directory, true);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ClearTempWorkspaceExternal] Error: {ex.Message}");
            }

            _wasmHasErrors = false;
            _wasmCompileLogPath = "";
            _lastTerrainSyncTime = 0;
            _lastMetadataSyncTime = 0;
        }

        private static void ClearDirectoryReadOnly(string targetDir)
        {
            foreach (var file in System.IO.Directory.GetFiles(targetDir, "*", System.IO.SearchOption.AllDirectories))
            {
                var attrs = System.IO.File.GetAttributes(file);
                if ((attrs & System.IO.FileAttributes.ReadOnly) != 0)
                {
                    System.IO.File.SetAttributes(file, attrs & ~System.IO.FileAttributes.ReadOnly);
                }
            }
        }

        public void GenerateVSCodeFilesExternal()
        {
            if (string.IsNullOrEmpty(_tempWorkspacePath)) return;
            string scriptPath = System.IO.Path.Combine(_tempWorkspacePath, "MapScript.cs");
            string unitsPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");
            System.IO.Directory.CreateDirectory(_tempWorkspacePath);
            Realm.Client.Services.MapWorkspaceService.SetupWorkspace(_tempWorkspacePath, "MapScript");
            string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
            _lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
            _lastMetadataSyncTime = GetLastWriteTimeSafe(unitsPath);
        }

        private long GetLastWriteTimeSafe(string path)
        {
            if (!System.IO.File.Exists(path)) return 0;
            return System.IO.File.GetLastWriteTimeUtc(path).Ticks;
        }

        private long GetMaxTerrainWriteTime(string baseTerrainJsonPath)
        {
            long maxTime = GetLastWriteTimeSafe(baseTerrainJsonPath);
            string dir = System.IO.Path.GetDirectoryName(baseTerrainJsonPath);
            if (!string.IsNullOrEmpty(dir))
            {
                maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_heights.exr")));
                maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_water.exr")));
                maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_splat_indices.exr")));
                maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_splat_weights.exr")));
                maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_cliff_splat_indices.exr")));
                maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_cliff_splat_weights.exr")));
                maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_splat_indices.png")));
                maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_splat_weights.png")));
                maxTime = Math.Max(maxTime, GetLastWriteTimeSafe(System.IO.Path.Combine(dir, "terrain_pathing.png")));
            }
            return maxTime;
        }

        private void OnSyncTimerTimeout()
        {
            if (string.IsNullOrEmpty(_tempWorkspacePath) || !System.IO.Directory.Exists(_tempWorkspacePath)) return;
            
            if (CheckAndHandleEditorClosure()) return;

            

            SyncWasmFiles();
            UpdateWorkspaceModTime();
        }

        private bool CheckAndHandleEditorClosure()
        {
            string cleanExitFile = ProjectSettings.GlobalizePath("user://editor_clean_exit.txt");
            if (System.IO.File.Exists(cleanExitFile))
            {
                try { System.IO.File.Delete(cleanExitFile); } catch { }
                return true;
            }
            return false;
        }

        private void SyncWasmFiles()
        {
            try
            {
                var wasmPath = System.IO.Path.Combine(_tempWorkspacePath, "src", "bin", "Release", "net10.0", "wasi-wasm", "native", "Realm.MapProject.wasm");
                var wasmDest = System.IO.Path.Combine(_tempWorkspacePath, "Map.wasm");
                if (System.IO.File.Exists(wasmPath))
                {
                    var fileInfo = new System.IO.FileInfo(wasmPath);
                    if (fileInfo.LastWriteTimeUtc.Ticks > _lastMetadataSyncTime)
                    {
                        System.IO.File.Copy(wasmPath, wasmDest, true);
                        _lastMetadataSyncTime = fileInfo.LastWriteTimeUtc.Ticks;
                    }
                }
            }
            catch { }
        }

        private void UpdateWorkspaceModTime()
        {
            try
            {
                var wsDir = new System.IO.DirectoryInfo(_tempWorkspacePath);
                wsDir.LastWriteTimeUtc = DateTime.UtcNow;
            }
            catch { }
        }

        public static bool IsIgnoredPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;
            string normalized = relativePath.Replace('\\', '/');
            if (normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(".vs/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.vs/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.godot/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".godot", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(".idea/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.idea/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".idea", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) || normalized.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) || normalized.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("map_backups/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/map_backups/", StringComparison.OrdinalIgnoreCase) || normalized.Equals("map_backups", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("map_upgrades/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/map_upgrades/", StringComparison.OrdinalIgnoreCase) || normalized.Equals("map_upgrades", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("backups/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/backups/", StringComparison.OrdinalIgnoreCase) || normalized.Equals("backups", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(".backups/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.backups/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".backups", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(".dotnet/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.dotnet/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".dotnet", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(".wasi/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.wasi/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".wasi", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(".sidecarcache/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.sidecarcache/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".sidecarcache", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(".cache/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/.cache/", StringComparison.OrdinalIgnoreCase) || normalized.Equals(".cache", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                System.IO.Path.GetFileName(normalized).StartsWith("~", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        private static void CopyFileClearingReadOnly(string sourceFile, string targetFile)
        {
            PathUtils.CopyFileClearingReadOnly(sourceFile, targetFile);
        }

        private void CopyFolderToTempWorkspace(string sourceFolder)
        {
            ClearTempWorkspaceExternal();
            if (!System.IO.Directory.Exists(_tempWorkspacePath))
            {
                System.IO.Directory.CreateDirectory(_tempWorkspacePath);
            }

            var allFiles = System.IO.Directory.GetFiles(sourceFolder, "*", System.IO.SearchOption.AllDirectories);
            var filesToProcess = new List<(string Source, string Target, bool IsMutable)>(allFiles.Length);
            var createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in allFiles)
            {
                string relativePath = file.Substring(sourceFolder.Length + 1);
                if (IsIgnoredPath(relativePath)) continue;

                string targetFile = System.IO.Path.Combine(_tempWorkspacePath, relativePath);
                string targetDir = System.IO.Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetDir) && createdDirs.Add(targetDir))
                {
                    System.IO.Directory.CreateDirectory(targetDir);
                }
                filesToProcess.Add((file, targetFile, PathUtils.IsMutableMapFileType(relativePath)));
            }

            System.Threading.Tasks.Parallel.ForEach(filesToProcess, item =>
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

            Realm.Client.Services.MapWorkspaceService.EnsureWitFile(_tempWorkspacePath);
            Realm.Client.Services.MapWorkspaceService.EnsureWasmEntryPoint(_tempWorkspacePath);
            Realm.Client.Services.MapWorkspaceService.EnsureCsproj(_tempWorkspacePath, System.IO.Path.GetFileName(sourceFolder));
            CopyVsCodeFolderFromMapTemplate(_tempWorkspacePath);
        }

        private static void CopyVsCodeFolderFromMapTemplate(string targetWorkspacePath)
        {
            if (string.IsNullOrEmpty(targetWorkspacePath)) return;

            string templateVsCodeDir = Realm.Client.Services.MapWorkspaceService.GetTemplatePath(".vscode");
            if (string.IsNullOrEmpty(templateVsCodeDir) || !System.IO.Directory.Exists(templateVsCodeDir))
            {
                templateVsCodeDir = PathUtils.FindPath("MapTemplate/.vscode");
            }

            if (string.IsNullOrEmpty(templateVsCodeDir) || !System.IO.Directory.Exists(templateVsCodeDir))
            {
                return;
            }

            string targetVsCodeDir = System.IO.Path.Combine(targetWorkspacePath, ".vscode");
            if (!System.IO.Directory.Exists(targetVsCodeDir))
            {
                System.IO.Directory.CreateDirectory(targetVsCodeDir);
            }

            foreach (string file in System.IO.Directory.GetFiles(templateVsCodeDir, "*", System.IO.SearchOption.AllDirectories))
            {
                string relativePath = file.Substring(templateVsCodeDir.Length + 1);
                string destFile = System.IO.Path.Combine(targetVsCodeDir, relativePath);
                PathUtils.CopyFileClearingReadOnly(file, destFile);
            }
        }

        private void CopyTempWorkspaceToFolder(string targetFolder)
        {
            if (!System.IO.Directory.Exists(targetFolder))
            {
                System.IO.Directory.CreateDirectory(targetFolder);
            }

            string tempTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
            _lastTerrainSyncTime = GetMaxTerrainWriteTime(tempTerrainPath);

            var allFiles = System.IO.Directory.GetFiles(_tempWorkspacePath, "*", System.IO.SearchOption.AllDirectories);
            var filesToCopy = new List<(string Source, string Target)>(allFiles.Length);
            var createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in allFiles)
            {
                string relativePath = file.Substring(_tempWorkspacePath.Length + 1);
                if (IsIgnoredPath(relativePath)) continue;
                string targetFile = System.IO.Path.Combine(targetFolder, relativePath);
                string targetDir = System.IO.Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(targetDir) && createdDirs.Add(targetDir))
                {
                    System.IO.Directory.CreateDirectory(targetDir);
                }
                filesToCopy.Add((file, targetFile));
            }

            System.Threading.Tasks.Parallel.ForEach(filesToCopy, pair =>
            {
                PathUtils.CopyFileClearingReadOnly(pair.Source, pair.Target);
            });

            if (OperatingSystem.IsWindows())
            {
                Realm.Client.VSCodeManager.Instance.SaveRecentMapDir(targetFolder);
            }
        }

        private async System.Threading.Tasks.Task SaveMapToFolderAsync(string targetFolder)
        {
            Realm.Client.Services.MapWorkspaceService.EnsureLicenseFile(_tempWorkspacePath);
            string tempTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
            if (Realm.Client.Core.GameHost.Instance != null)
            {
                Realm.Client.Core.GameHost.Instance.SaveMapToFile(tempTerrainPath);
                Realm.Client.Core.GameHost.Instance.EditorHasUnsavedChanges = false;
                InvalidateMetadataCache();
            }

            ShowFeedback(TranslationServer.Translate("Saving map folder..."));

            try
            {
                await System.Threading.Tasks.Task.Run(() => CopyTempWorkspaceToFolder(targetFolder));

                Realm.Client.Services.MapWorkspaceService.EnsureLicenseFile(targetFolder);

                SaveCurrentDirectoryBlake3();
                _lastTerrainSyncTime = GetMaxTerrainWriteTime(tempTerrainPath);
                _lastMetadataSyncTime = GetLastWriteTimeSafe(System.IO.Path.Combine(_tempWorkspacePath, "metadata.json"));
                EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;

                ShowFeedback(string.Format(TranslationServer.Translate("Map saved successfully to folder {0}!"), System.IO.Path.GetFileName(targetFolder)));
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapEditorHUD] SaveMapToFolderAsync failed: {ex}");
                ShowFeedback(string.Format(TranslationServer.Translate("Failed to save map: {0}"), ex.Message));
            }
        }

        private void SaveMapAction()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;

            if (!string.IsNullOrEmpty(_currentSourceFolder) && !IsRestrictedSaveDirectory(_currentSourceFolder) && System.IO.Directory.Exists(_currentSourceFolder))
            {
                _ = SaveMapToFolderAsync(_currentSourceFolder);
                return;
            }

            PromptSaveMapFolder();
        }

        private void SaveAsMapAction()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;
            PromptSaveMapFolder();
        }

        public void PromptSaveMapFolder()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;

            string initialDir = GetInitialDirectory();
            try
            {
                if (!System.IO.Directory.Exists(initialDir))
                {
                    System.IO.Directory.CreateDirectory(initialDir);
                }
            }
            catch { }

            var err = DisplayServer.FileDialogShow(
                TranslationServer.Translate("Save Map Folder"),
                initialDir,
                "",
                false,
                DisplayServer.FileDialogMode.OpenDir,
                System.Array.Empty<string>(),
                Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
                {
                    if (status && selectedPaths.Length > 0)
                    {
                        HandleSaveToFolder(selectedPaths[0]);
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
                try { System.IO.Directory.CreateDirectory(defaultFolder); } catch { }
                HandleSaveToFolder(defaultFolder);
            }
        }

        private void HandleSaveToFolder(string selectedFolder)
        {
            if (string.IsNullOrWhiteSpace(selectedFolder)) return;

            string fullSelectedPath = System.IO.Path.GetFullPath(selectedFolder);

            if (IsRestrictedSaveDirectory(fullSelectedPath))
            {
                ShowFeedback(TranslationServer.Translate("Cannot save to restricted application directory. Please choose a folder in Documents or your workspace."));
                return;
            }

            var parentDir = System.IO.Directory.GetParent(fullSelectedPath);
            while (parentDir != null)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(parentDir.FullName, "manifest.json")))
                {
                    ShowFeedback(TranslationServer.Translate("Cannot save map inside an existing map folder. Please select a separate root directory."));
                    return;
                }
                parentDir = parentDir.Parent;
            }

            if (System.IO.Directory.Exists(fullSelectedPath))
            {
                try
                {
                    foreach (string subDir in System.IO.Directory.EnumerateDirectories(fullSelectedPath))
                    {
                        if (System.IO.File.Exists(System.IO.Path.Combine(subDir, "manifest.json")))
                        {
                            ShowFeedback(TranslationServer.Translate("Found nested map inside selected folder. Cannot save to this folder location."));
                            return;
                        }
                    }
                }
                catch { }
            }

            string rootManifest = System.IO.Path.Combine(fullSelectedPath, "manifest.json");
            bool isSameAsCurrent = !string.IsNullOrEmpty(_currentSourceFolder) &&
                                   string.Equals(fullSelectedPath, System.IO.Path.GetFullPath(_currentSourceFolder), StringComparison.OrdinalIgnoreCase);

            if (System.IO.File.Exists(rootManifest) && !isSameAsCurrent)
            {
                ShowConfirmationDialog(
                    TranslationServer.Translate("An existing map was found in this folder. Overwrite existing map?"),
                    () =>
                    {
                        _lastUsedFolder = fullSelectedPath;
                        _currentSourceFolder = fullSelectedPath;
                        if (!IsRestrictedSaveDirectory(fullSelectedPath))
                        {
                            GameSettings.LastOpenedFolder = fullSelectedPath;
                            GameSettings.Save();
                        }
                        _ = SaveMapToFolderAsync(fullSelectedPath);
                    },
                    confirmText: TranslationServer.Translate("OVERWRITE"),
                    cancelText: TranslationServer.Translate("CANCEL")
                );
                return;
            }

            _lastUsedFolder = fullSelectedPath;
            _currentSourceFolder = fullSelectedPath;
            if (!IsRestrictedSaveDirectory(fullSelectedPath))
            {
                GameSettings.LastOpenedFolder = fullSelectedPath;
                GameSettings.Save();
            }
            _ = SaveMapToFolderAsync(fullSelectedPath);
        }

        public void LoadMapAction()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;

            var err = DisplayServer.FileDialogShow(
                TranslationServer.Translate("Load Map Folder"),
                GetInitialDirectory(),
                "",
                false,
                DisplayServer.FileDialogMode.OpenDir,
                System.Array.Empty<string>(),
                Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
                {
                    if (status && selectedPaths.Length > 0)
                    {
                        string selectedFolder = selectedPaths[0];
                        _lastUsedFolder = selectedFolder;
                        if (!IsRestrictedSaveDirectory(selectedFolder))
                        {
                            GameSettings.LastOpenedFolder = selectedFolder;
                            GameSettings.Save();
                        }
                        _ = LoadMapFolderAsync(selectedFolder);
                    }
                })
            );

            if (err != Error.Ok)
            {
                ShowFeedback(TranslationServer.Translate("Map could not be loaded."));
            }
        }

        private static bool IsValidMapFolder(string folder)
        {
            return System.IO.File.Exists(System.IO.Path.Combine(folder, "metadata.json"))
                   && System.IO.File.Exists(System.IO.Path.Combine(folder, "manifest.json"));
        }

        public bool LoadMapFolder(string selectedFolder)
        {
            if (!System.IO.Directory.Exists(selectedFolder)) return false;

            if (!IsValidMapFolder(selectedFolder))
            {
                ShowInvalidMapFolderErrorDialog(selectedFolder);
                return false;
            }

            var upgradeService = _mapUpgradeService ?? MapUpgradeService.Instance;
            if (upgradeService != null && upgradeService.NeedsUpgrade(selectedFolder, out _, out _))
            {
                _ = LoadMapFolderAsync(selectedFolder);
                return true;
            }

            Realm.Client.Core.GameHost.Instance?.ClearMapEntirely();
            _lastUsedFolder = selectedFolder;
            _currentSourceFolder = selectedFolder;
            if (!IsRestrictedSaveDirectory(selectedFolder))
            {
                GameSettings.LastOpenedFolder = selectedFolder;
                GameSettings.Save();
            }

            ShowFeedback(TranslationServer.Translate("Loading map..."));
            CopyFolderToTempWorkspace(selectedFolder);

            try
            {
                Realm.Client.Services.MapWorkspaceService.EnsureGlbAssetsOptimized(_tempWorkspacePath);
                Realm.Client.Services.MapWorkspaceService.EnsurePngAssetsConverted(_tempWorkspacePath);
                LoadMapProperties();
                ReadMetadataAndRefreshTextures();
                string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
                bool success = Realm.Client.Core.GameHost.Instance?.LoadMapFromFile(terrainPath, ensureGlbOptimized: false) ?? false;

                if (success)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        Realm.Client.VSCodeManager.Instance.SaveRecentMapDir(selectedFolder);
                    }
                    _lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
                    _lastMetadataSyncTime = GetLastWriteTimeSafe(System.IO.Path.Combine(_tempWorkspacePath, "metadata.json"));
                    _editorService?.StartWorkspaceWatcher(_tempWorkspacePath);
                    ShowFeedback(string.Format(TranslationServer.Translate("Map loaded successfully from folder {0}!"), System.IO.Path.GetFileName(selectedFolder)));
                    SaveCurrentDirectoryBlake3();
                }
                else
                {
                    ShowFeedback(TranslationServer.Translate("Failed to load map files from folder!"));
                }

                return success;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapEditorHUD] Failed to load map folder: {ex.Message}");
                return false;
            }
        }

        public async System.Threading.Tasks.Task<bool> LoadMapFolderAsync(string selectedFolder)
        {
            if (!System.IO.Directory.Exists(selectedFolder)) return false;

            if (!IsValidMapFolder(selectedFolder))
            {
                ShowInvalidMapFolderErrorDialog(selectedFolder);
                return false;
            }

            bool canProceed = await PromptAndUpgradeMapIfNeededAsync(selectedFolder);
            if (!canProceed)
            {
                ShowFeedback(TranslationServer.Translate("Map loading cancelled."));
                return false;
            }

            Realm.Client.Core.GameHost.Instance?.ClearMapEntirely();
            _isSyncing = true;
            try
            {
                ModelCache.Clear();
                PathUtils.ClearCache();

                _lastUsedFolder = selectedFolder;
                _currentSourceFolder = selectedFolder;
                if (!IsRestrictedSaveDirectory(selectedFolder))
                {
                    GameSettings.LastOpenedFolder = selectedFolder;
                    GameSettings.Save();
                }

                ShowFeedback(TranslationServer.Translate("Loading map..."));
                await System.Threading.Tasks.Task.Run(() => CopyFolderToTempWorkspace(selectedFolder));

                string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
                string metadataPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");
                _lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
                _lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);

                await Realm.Client.Services.MapWorkspaceService.EnsureGlbAssetsOptimizedCooperativeAsync(_tempWorkspacePath, async (current, total, fileName) =>
                {
                    ShowFeedback(string.Format(TranslationServer.Translate("Optimizing 3D asset {0}/{1}: {2}..."), current, total, fileName));
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                });

                await Realm.Client.Services.MapWorkspaceService.EnsurePngAssetsConvertedCooperativeAsync(_tempWorkspacePath, async (current, total, fileName) =>
                {
                    ShowFeedback(string.Format(TranslationServer.Translate("Converting texture {0}/{1}: {2}..."), current, total, fileName));
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                });

                LoadMapProperties();
                ReadMetadataAndRefreshTextures();
                bool success = Realm.Client.Core.GameHost.Instance?.LoadMapFromFile(terrainPath, ensureGlbOptimized: false) ?? false;

                if (success)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        Realm.Client.VSCodeManager.Instance.SaveRecentMapDir(selectedFolder);
                    }
                    _lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
                    _lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
                    _editorService?.StartWorkspaceWatcher(_tempWorkspacePath);
                    ShowFeedback(string.Format(TranslationServer.Translate("Map loaded successfully from folder {0}!"), System.IO.Path.GetFileName(selectedFolder)));
                    SaveCurrentDirectoryBlake3();
                }
                else
                {
                    ShowFeedback(TranslationServer.Translate("Failed to load map files from folder!"));
                }

                return success;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapEditorHUD] Failed to load map folder: {ex.Message}");
                return false;
            }
            finally
            {
                _isSyncing = false;
            }
        }

        public static void ResetFolderLocations()
        {
            _lastUsedFolder = null;
            _currentSourceFolder = null;
            _pendingCasSourceDirectory = null;
            _pendingDefaultSaveFolder = null;
        }

        public static string GetDocumentsDirectory()
        {
            string docs = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs))
            {
                string userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                docs = !string.IsNullOrEmpty(userProfile) ? System.IO.Path.Combine(userProfile, "Documents") : ProjectSettings.GlobalizePath("user://");
            }
            try
            {
                if (!System.IO.Directory.Exists(docs))
                {
                    System.IO.Directory.CreateDirectory(docs);
                }
            }
            catch { }
            return docs;
        }

        private string GetInitialDirectory()
        {
            if (!string.IsNullOrEmpty(_lastUsedFolder) && !IsRestrictedSaveDirectory(_lastUsedFolder) && System.IO.Directory.Exists(_lastUsedFolder))
            {
                return _lastUsedFolder;
            }
            if (!string.IsNullOrEmpty(_currentSourceFolder) && !IsRestrictedSaveDirectory(_currentSourceFolder) && System.IO.Directory.Exists(_currentSourceFolder))
            {
                return _currentSourceFolder;
            }
            if (!string.IsNullOrEmpty(GameSettings.LastOpenedFolder) && !IsRestrictedSaveDirectory(GameSettings.LastOpenedFolder) && System.IO.Directory.Exists(GameSettings.LastOpenedFolder))
            {
                return GameSettings.LastOpenedFolder;
            }
            return GetDocumentsDirectory();
        }

        public string GetDefaultDevelopmentMapDirectory(string? mapName = null)
        {
            string name = !string.IsNullOrWhiteSpace(mapName) ? mapName : GetMapNameFromMetadata();
            string cleanName = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (string.IsNullOrEmpty(cleanName) || cleanName.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase)) cleanName = "new_map";

            string docs = GetDocumentsDirectory();
            return System.IO.Path.Combine(docs, cleanName);
        }

        public static bool IsRestrictedSaveDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            try
            {
                string fullPath = System.IO.Path.GetFullPath(path).Replace("\\", "/").TrimEnd('/');

                string tempWorkspace = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("user://temp_map_workspace")).Replace("\\", "/").TrimEnd('/');
                string mapBackups = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("user://map_backups")).Replace("\\", "/").TrimEnd('/');

                if (fullPath.Equals(tempWorkspace, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(tempWorkspace + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                if (fullPath.Equals(mapBackups, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(mapBackups + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string userDir = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("user://")).Replace("\\", "/").TrimEnd('/');
                if (fullPath.Equals(userDir, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(userDir + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string resDir = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://")).Replace("\\", "/").TrimEnd('/');
                if (fullPath.Equals(resDir, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(resDir + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch { }

            return false;
        }


        private NSec.Cryptography.Key GetOrGenerateAuthorshipKey()
        {
            string keyDir = ProjectSettings.GlobalizePath("user://appdata/keys/");
            string defaultUsername = Realm.Client.Network.LobbyManager.Instance?.AuthenticatedUsername ?? string.Empty;
            var (key, data, keyPath, createdNew) = AuthorshipKeyHelper.GetOrGenerateKeyInfo(keyDir, defaultUsername);
            if (createdNew)
            {
                ShowFeedback(TranslationServer.Translate("A new authorship key has been generated at ") + keyPath + TranslationServer.Translate(". Please backup this file to retain your authorship identity."));
            }
            return key;
        }




        private void SaveMapProperties()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;

            _mapSettingsDialog?.SaveMapProperties();
        }

        private async System.Threading.Tasks.Task CompileAndSignMapAsync(string workspace, bool skipAttribution = true)
        {
            _wasmHasErrors = false;
            // 1. Compile triggers
            try
            {
                if (System.IO.Directory.Exists(workspace))
                {
                    Realm.Client.Services.MapWorkspaceService.EnsureWitFile(workspace);
                    Realm.Client.Services.MapWorkspaceService.EnsureWasmEntryPoint(workspace);
                    Realm.Client.Services.MapWorkspaceService.EnsureCsproj(workspace, System.IO.Path.GetFileName(workspace));

                    var csprojFiles = System.IO.Directory.GetFiles(workspace, "*.csproj", System.IO.SearchOption.TopDirectoryOnly);
                    if (csprojFiles.Length == 0)
                    {
                        _wasmHasErrors = true;
                        var errorMessage = "[MapEditorHUD] ERROR: No .csproj found in workspace, cannot compile map script";
                        SetWasmConsoleStatus("❌ " + errorMessage, new Color(1.0f, 0.3f, 0.3f));
                        AppendWasmConsoleLog(errorMessage);
                        GD.PrintErr(errorMessage);
                        return;
                    }

                    string csproj = csprojFiles.FirstOrDefault(f => System.IO.Path.GetFileName(f).Equals("MapScript.csproj", System.StringComparison.OrdinalIgnoreCase)) ?? csprojFiles[0];

                    // Check if WASM binary already exists and no .cs files have been modified since it was built
                    string binDir = System.IO.Path.Combine(workspace, "bin");
                    string existingWasm = null;
                    if (System.IO.Directory.Exists(binDir))
                    {
                        var wasmFiles = System.IO.Directory.GetFiles(
                            binDir,
                            "*.wasm",
                            System.IO.SearchOption.AllDirectories
                        ).Where(f => !f.Contains("native") && !f.Contains("obj")).ToList();

                        existingWasm = wasmFiles.FirstOrDefault(f => f.Contains("publish"))
                                       ?? wasmFiles.OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f)).FirstOrDefault();
                    }

                    if (string.IsNullOrEmpty(existingWasm) && !string.IsNullOrEmpty(_currentSourceFolder) && System.IO.Directory.Exists(_currentSourceFolder))
                    {
                        string sourceBinDir = System.IO.Path.Combine(_currentSourceFolder, "bin");
                        if (System.IO.Directory.Exists(sourceBinDir))
                        {
                            var wasmFiles = System.IO.Directory.GetFiles(
                                sourceBinDir,
                                "*.wasm",
                                System.IO.SearchOption.AllDirectories
                            ).Where(f => !f.Contains("native") && !f.Contains("obj")).ToList();

                            existingWasm = wasmFiles.FirstOrDefault(f => f.Contains("publish"))
                                           ?? wasmFiles.OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f)).FirstOrDefault();
                        }
                    }

                    if (!string.IsNullOrEmpty(existingWasm) && System.IO.File.Exists(existingWasm))
                    {
                        DateTime wasmTime = System.IO.File.GetLastWriteTimeUtc(existingWasm);
                        var sourceDirs = new System.Collections.Generic.List<string> { workspace };
                        if (!string.IsNullOrEmpty(_currentSourceFolder) && System.IO.Directory.Exists(_currentSourceFolder) && !_currentSourceFolder.Equals(workspace, System.StringComparison.OrdinalIgnoreCase))
                        {
                            sourceDirs.Add(_currentSourceFolder);
                        }

                        bool hasNewerCsFile = false;
                        foreach (var dir in sourceDirs)
                        {
                            var dependencyFiles = System.IO.Directory.GetFiles(dir, "*.cs", System.IO.SearchOption.AllDirectories)
                                .Where(f =>
                                {
                                    string rel = f.Substring(dir.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                                    return !rel.StartsWith("bin", System.StringComparison.OrdinalIgnoreCase) && !rel.StartsWith("obj", System.StringComparison.OrdinalIgnoreCase);
                                })
                                .Concat(System.IO.Directory.GetFiles(dir, "*.csproj", System.IO.SearchOption.TopDirectoryOnly))
                                .Concat(System.IO.Directory.GetFiles(dir, "metadata.json", System.IO.SearchOption.TopDirectoryOnly))
                                .Concat(System.IO.Directory.Exists(System.IO.Path.Combine(dir, "lib"))
                                    ? System.IO.Directory.GetFiles(System.IO.Path.Combine(dir, "lib"), "*.dll", System.IO.SearchOption.TopDirectoryOnly)
                                    : System.Array.Empty<string>())
                                .Concat(System.IO.Directory.Exists(System.IO.Path.Combine(dir, "wit"))
                                    ? System.IO.Directory.GetFiles(System.IO.Path.Combine(dir, "wit"), "*.wit", System.IO.SearchOption.TopDirectoryOnly)
                                    : System.Array.Empty<string>());

                            if (dependencyFiles.Any(f => System.IO.File.GetLastWriteTimeUtc(f) > wasmTime))
                            {
                                hasNewerCsFile = true;
                                break;
                            }
                        }

                        if (!hasNewerCsFile)
                        {
                            string targetWasmInTemp = System.IO.Path.Combine(workspace, "bin", System.IO.Path.GetFileName(existingWasm));
                            if (!System.IO.File.Exists(targetWasmInTemp))
                            {
                                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetWasmInTemp));
                                System.IO.File.Copy(existingWasm, targetWasmInTemp, true);
                            }

                            _wasmHasErrors = false;
                            SetWasmConsoleStatus("✓ WASM Compilation Bypassed (Unchanged)", UIStyle.ColorCyanGlow);
                            AppendWasmConsoleLog("[INFO] .cs files unchanged since last build. Bypassing compilation using existing WASM binary.");
                            if (skipAttribution) return;
                        }
                    }

                    string mapApiDll = System.IO.Path.Combine(workspace, "lib", "Realm.MapAPI.dll");
                    if (!System.IO.File.Exists(mapApiDll))
                    {
                        _wasmHasErrors = true;
                        SetWasmConsoleStatus("❌ WASM Compilation Failed: Realm.MapAPI.dll missing", new Color(1.0f, 0.3f, 0.3f));
                        AppendWasmConsoleLog("[ERROR] Realm.MapAPI.dll is missing from the workspace (expected at lib/Realm.MapAPI.dll).");
                        AppendWasmConsoleLog("[ERROR] The map script cannot compile without the MapAPI assembly. Reopen the map in the editor to restore template files, then retry Test.");
                        return;
                    }

                    await System.Threading.Tasks.Task.Run(() =>
                    {
                        bool streamHadErrors = false;
                        string resolvedWasiSdk = WasiSdkResolver.ResolveWasiSdkPath();
                        string csprojName = System.IO.Path.GetFileName(csproj);
                        var compileProcess = new System.Diagnostics.Process();
                        compileProcess.StartInfo.FileName = "dotnet";
                        compileProcess.StartInfo.Arguments = $"publish \"{csprojName}\" -c Release -r wasi-wasm -p:WASI_SDK_PATH=\"{resolvedWasiSdk}\"";
                        compileProcess.StartInfo.EnvironmentVariables["WASI_SDK_PATH"] = resolvedWasiSdk;
                        compileProcess.StartInfo.WorkingDirectory = workspace;
                        compileProcess.StartInfo.CreateNoWindow = true;
                        compileProcess.StartInfo.UseShellExecute = false;
                        compileProcess.StartInfo.RedirectStandardOutput = true;
                        compileProcess.StartInfo.RedirectStandardError = true;

                        compileProcess.OutputDataReceived += (s, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                if (e.Data.Contains(": error ") || e.Data.Contains("Build FAILED"))
                                {
                                    streamHadErrors = true;
                                }
                                AppendWasmConsoleLog(e.Data);
                            }
                        };

                        compileProcess.ErrorDataReceived += (s, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                if (e.Data.Contains(": error ") || e.Data.Contains("Build FAILED"))
                                {
                                    streamHadErrors = true;
                                    AppendWasmConsoleLog("[COMPILER ERROR] " + e.Data);
                                }
                                else if (e.Data.Contains(": warning "))
                                {
                                    AppendWasmConsoleLog("[COMPILER WARNING] " + e.Data);
                                }
                                else
                                {
                                    AppendWasmConsoleLog(e.Data);
                                }
                            }
                        };

                        compileProcess.Start();
                        compileProcess.BeginOutputReadLine();
                        compileProcess.BeginErrorReadLine();
                        compileProcess.WaitForExit();

                        if (compileProcess.ExitCode != 0 || streamHadErrors)
                        {
                            _wasmHasErrors = true;
                            SetWasmConsoleStatus($"❌ WASM Compilation Failed (exit code {compileProcess.ExitCode})", new Color(1.0f, 0.3f, 0.3f));
                            AppendWasmConsoleLog($"[ERROR] dotnet publish failed with exit code {compileProcess.ExitCode}");
                            GD.PrintErr($"[MapEditorHUD] Map script compilation failed (exit code {compileProcess.ExitCode})");
                        }
                        else
                        {
                            _wasmHasErrors = false;
                            SetWasmConsoleStatus("✓ WASM Compilation Succeeded", UIStyle.ColorCyanGlow);
                            AppendWasmConsoleLog("[SUCCESS] WASM compilation complete (exit code 0).");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _wasmHasErrors = true;
                SetWasmConsoleStatus($"❌ WASM Compilation Failed: {ex.Message}", new Color(1.0f, 0.3f, 0.3f));
                AppendWasmConsoleLog($"[COMPILER EXCEPTION] {ex}");
                GD.PrintErr($"[MapEditorHUD] Trigger compilation failed: {ex.Message}");
            }

            if (skipAttribution) return;

            // 2. Resolve contributors, attributions, and sign
            try
            {
                var authorshipKey = GetOrGenerateAuthorshipKey();
                string currentUsername = "MapAuthor";
                string pubKeyStr = Convert.ToBase64String(authorshipKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));

                string seedServerUrl = Realm.Client.Core.GameHost.Instance != null && GodotObject.IsInstanceValid(Realm.Client.Network.LobbyManager.Instance) ? Realm.Client.Network.LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();

                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    try
                    {
                        var resTask = httpClient.GetAsync(seedServerUrl + "/api/creators/check/" + Uri.EscapeDataString(pubKeyStr));
                        resTask.Wait();
                        var res = resTask.Result;
                        if (res.IsSuccessStatusCode)
                        {
                            var jsonTask = res.Content.ReadAsStringAsync();
                            jsonTask.Wait();
                            using var creatorDoc = JsonDocument.Parse(jsonTask.Result);
                            if (creatorDoc.RootElement.TryGetProperty("username", out var uProp))
                            {
                                currentUsername = uProp.GetString() ?? currentUsername;
                            }
                        }
                    }
                    catch { }

                    var referencedHashes = new List<string>();
                    var allFiles = System.IO.Directory.GetFiles(workspace, "*", System.IO.SearchOption.AllDirectories);

                    foreach (var file in allFiles)
                    {
                        if (file.EndsWith("metadata.json") || file.EndsWith("manifest.json") || file.EndsWith("authorship_key.pem") || file.EndsWith("authorship_key_DO-NOT-SHARE.rkey") || file.EndsWith(".rkey")) continue;

                        byte[] fileBytes = System.IO.File.ReadAllBytes(file);
                        string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                        string blake3 = RealmMetadataHelper.ComputeBlake3(fileBytes, ext);
                        string hash = string.IsNullOrEmpty(ext) ? blake3 : $"{blake3}{ext}";

                        byte[] hashBytes = System.Text.Encoding.UTF8.GetBytes(hash);
                        byte[] signatureBytes = SignatureAlgorithm.Ed25519.Sign(authorshipKey, hashBytes);
                        string signatureStr = Convert.ToBase64String(signatureBytes);

                        referencedHashes.Add(hash);

                        try
                        {
                            var existsResTask = httpClient.GetAsync(seedServerUrl + "/api/publish_map/asset_author/" + hash);
                            existsResTask.Wait();
                            var existsRes = existsResTask.Result;
                            if (!existsRes.IsSuccessStatusCode)
                            {
                                using var form = new System.Net.Http.MultipartFormDataContent();
                                form.Add(new System.Net.Http.StringContent(hash), "Hash");
                                form.Add(new System.Net.Http.StringContent(signatureStr), "Signature");
                                form.Add(new System.Net.Http.StringContent(currentUsername), "AuthorUsername");
                                form.Add(new System.Net.Http.StringContent(pubKeyStr), "PublicKey");

                                var fileContent = new System.Net.Http.ByteArrayContent(fileBytes);
                                form.Add(fileContent, "File", System.IO.Path.GetFileName(file));

                                var uploadTask = httpClient.PostAsync(seedServerUrl + "/api/publish_map/upload_asset", form);
                                uploadTask.Wait();
                            }
                        }
                        catch { }
                    }

                    string metadataJsonPath = System.IO.Path.Combine(workspace, "metadata.json");
                    if (System.IO.File.Exists(metadataJsonPath))
                    {
                        string metadataJsonContent = System.IO.File.ReadAllText(metadataJsonPath);
                        var options = new JsonSerializerOptions { WriteIndented = true };
                        var mapDoc = JsonNode.Parse(metadataJsonContent) as JsonObject;

                        if (mapDoc != null)
                        {
                            var contributorsList = new HashSet<string>();
                            if (mapDoc.TryGetPropertyValue("Contributors", out var contNode) && contNode is JsonArray arr)
                            {
                                foreach (var node in arr)
                                {
                                    if (node != null) contributorsList.Add(node.GetValue<string>());
                                }
                            }

                            var authorCounts = new System.Collections.Generic.Dictionary<string, int>();
                            foreach (var hash in referencedHashes)
                            {
                                try
                                {
                                    var assetAuthorResTask = httpClient.GetAsync(seedServerUrl + "/api/publish_map/asset_author/" + hash);
                                    assetAuthorResTask.Wait();
                                    var assetAuthorRes = assetAuthorResTask.Result;
                                    if (assetAuthorRes.IsSuccessStatusCode)
                                    {
                                        var assetAuthorJsonTask = assetAuthorRes.Content.ReadAsStringAsync();
                                        assetAuthorJsonTask.Wait();
                                        var assetMeta = JsonNode.Parse(assetAuthorJsonTask.Result);
                                        if (assetMeta != null && assetMeta["AuthorUsername"] != null)
                                        {
                                            string author = assetMeta["AuthorUsername"].GetValue<string>();
                                            if (!string.IsNullOrEmpty(author))
                                            {
                                                contributorsList.Add(author);
                                                if (!authorCounts.ContainsKey(author)) authorCounts[author] = 0;
                                                authorCounts[author]++;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }

                            contributorsList.Add(currentUsername);

                            var newContributorsArr = new JsonArray();
                            foreach (var cont in contributorsList)
                            {
                                newContributorsArr.Add(cont);
                            }
                            mapDoc["Contributors"] = newContributorsArr;

                            var attributionsArr = new JsonArray();
                            var sortedAuthors = authorCounts.Keys.ToList();
                            sortedAuthors.Sort((a, b) => authorCounts[b].CompareTo(authorCounts[a]));
                            foreach (var a in sortedAuthors)
                            {
                                attributionsArr.Add(a);
                            }
                            mapDoc["Attributions"] = attributionsArr;
                            mapDoc["EngineVersion"] = RealmVersion.GameBinaryVersion;

                            mapDoc["author_key"] = pubKeyStr;
                            if (mapDoc.ContainsKey("signature"))
                            {
                                mapDoc.Remove("signature");
                            }

                            string updatedMetadataJson = mapDoc.ToJsonString(options);
                            System.IO.File.WriteAllText(metadataJsonPath, updatedMetadataJson);

                            byte[] mapBytes = System.IO.File.ReadAllBytes(metadataJsonPath);
                            string mapBlake3 = RealmMetadataHelper.ComputeBlake3(mapBytes, ".json");
                            string mapHash = $"{mapBlake3}.json";
                            byte[] mapHashBytes = System.Text.Encoding.UTF8.GetBytes(mapHash);
                            byte[] mapSigBytes = SignatureAlgorithm.Ed25519.Sign(authorshipKey, mapHashBytes);

                            mapDoc["signature"] = Convert.ToBase64String(mapSigBytes);
                            updatedMetadataJson = mapDoc.ToJsonString(options);
                            System.IO.File.WriteAllText(metadataJsonPath, updatedMetadataJson);

                            try
                            {
                                using (var form = new System.Net.Http.MultipartFormDataContent())
                                {
                                    form.Add(new System.Net.Http.StringContent(mapHash), "Hash");
                                    form.Add(new System.Net.Http.StringContent(Convert.ToBase64String(mapSigBytes)), "Signature");
                                    form.Add(new System.Net.Http.StringContent(currentUsername), "AuthorUsername");
                                    form.Add(new System.Net.Http.StringContent(pubKeyStr), "PublicKey");

                                    var fileContent = new System.Net.Http.ByteArrayContent(mapBytes);
                                    form.Add(fileContent, "File", "metadata.json");

                                    var uploadMapTask = httpClient.PostAsync(seedServerUrl + "/api/publish_map/upload_asset", form);
                                    uploadMapTask.Wait();
                                }

                                var publishReq = new
                                {
                                    MapJson = updatedMetadataJson,
                                    ReferencedHashes = referencedHashes,
                                    Signature = Convert.ToBase64String(mapSigBytes),
                                    PublicKey = pubKeyStr
                                };

                                var pubContent = new StringContent(JsonSerializer.Serialize(publishReq), System.Text.Encoding.UTF8, "application/json");
                                var pubTask = httpClient.PostAsync(seedServerUrl + "/api/publish_map", pubContent);
                                pubTask.Wait();
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapEditorHUD] Error signing/compiling map: {ex.Message}");
            }
        }

        public void OpenAssetBrowser(string title, IEnumerable<string> allowedExtensions, Action<string> onAssetSelected, bool requireRealmMetadata = false, string? requiredAssetType = null)
        {
            if (_assetBrowserDialog == null)
            {
                _assetBrowserDialog = new Realm.Client.UI.MapEditor.AssetBrowserDialog(this);
            }
            _assetBrowserDialog.OpenForImport(title, allowedExtensions, onAssetSelected, requireRealmMetadata, requiredAssetType);
        }

        public void OpenAssetBrowser(string title, IEnumerable<string> allowedExtensions, Action<string, string?> onAssetSelected, bool requireRealmMetadata = false, string? requiredAssetType = null)
        {
            if (_assetBrowserDialog == null)
            {
                _assetBrowserDialog = new Realm.Client.UI.MapEditor.AssetBrowserDialog(this);
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
                        ImportTerrainFromMinimapPath(selectedPaths[0]);
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
            string handKey = hand switch
            {
                HumanoidBone.LeftHand => "left_hand",
                HumanoidBone.RightHand => "right_hand",
                HumanoidBone.Chest => "chest",
                HumanoidBone.Hips => "root",
                HumanoidBone.Head => "head",
                HumanoidBone.LeftFoot => "left_foot",
                HumanoidBone.RightFoot => "right_foot",
                _ => "right_hand"
            };
            SaveUnitObjectAttachment(unitId, handKey, attachmentId, orientation);
        }

        public void SaveUnitObjectAttachment(string unitId, string socket, string attachmentId, HandAttachmentOrientation orientation)
        {
            try
            {
                if (string.IsNullOrEmpty(unitId) || string.IsNullOrEmpty(attachmentId)) return;

                string regKey = unitId;
                bool isBuildingMeta = false;
                if (Realm.Client.Core.GameHost.UnitRegistry.ContainsKey(unitId))
                {
                    regKey = unitId;
                }
                else if (Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.ContainsKey(unitId))
                {
                    regKey = unitId;
                    isBuildingMeta = true;
                }
                else
                {
                    string cleanId = System.IO.Path.GetFileNameWithoutExtension(unitId);
                    if (Realm.Client.Core.GameHost.UnitRegistry.ContainsKey(cleanId))
                    {
                        regKey = cleanId;
                    }
                    else if (Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.ContainsKey(cleanId))
                    {
                        regKey = cleanId;
                        isBuildingMeta = true;
                    }
                    else
                    {
                        foreach (var k in Realm.Client.Core.GameHost.UnitRegistry.Keys)
                        {
                            string kStr = k.ToString();
                            if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
                                kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
                                System.IO.Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
                            {
                                regKey = k;
                                break;
                            }
                        }
                        if (Realm.Client.Core.GameHost.BuildingRegistry != null)
                        {
                            foreach (var k in Realm.Client.Core.GameHost.BuildingRegistry.Keys)
                            {
                                string kStr = k.ToString();
                                if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
                                    kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
                                    System.IO.Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
                                {
                                    regKey = k;
                                    isBuildingMeta = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (isBuildingMeta && Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.TryGetValue(regKey, out var bMeta))
                {
                    bMeta.SetObjectAttachment(socket, attachmentId, orientation);
                    Realm.Client.Core.GameHost.BuildingRegistry[regKey] = bMeta;
                }
                else if (Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(regKey, out var uMeta))
                {
                    uMeta.SetObjectAttachment(socket, attachmentId, orientation);
                    Realm.Client.Core.GameHost.UnitRegistry[regKey] = uMeta;
                }

                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
                if (!System.IO.File.Exists(metadataPath)) return;

                MetadataService.Instance.UpdateMetadata(wsPath, meta =>
                {
                    bool updated = meta.UpdateUnit(unitId, u => { u.SetObjectAttachment(socket, attachmentId, orientation); return u; });
                    if (!updated) updated = meta.UpdateBuilding(unitId, b => { b.SetObjectAttachment(socket, attachmentId, orientation); return b; });
                    if (!updated) updated = meta.UpdateUnit(regKey, u => { u.SetObjectAttachment(socket, attachmentId, orientation); return u; });
                    if (!updated) meta.UpdateBuilding(regKey, b => { b.SetObjectAttachment(socket, attachmentId, orientation); return b; });
                });
                _lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);

                if (Realm.Client.Core.GameHost.Instance != null)
                {
                    if (Realm.Client.Core.GameHost.Instance.AllUnits != null)
                    {
                        foreach (var u in Realm.Client.Core.GameHost.Instance.AllUnits)
                        {
                            if (u != null && (u.UnitId == unitId || u.UnitId == regKey || System.IO.Path.GetFileNameWithoutExtension(u.UnitId ?? "").Equals(System.IO.Path.GetFileNameWithoutExtension(unitId), StringComparison.OrdinalIgnoreCase)))
                            {
                                u.ApplyAllConfiguredAttachments();
                            }
                        }
                    }

                    if (Realm.Client.Core.GameHost.Instance.SelectedEditorObject is Realm.Client.Unit3D selUnit)
                    {
                        if (selUnit.UnitId == unitId || selUnit.UnitId == regKey || System.IO.Path.GetFileNameWithoutExtension(selUnit.UnitId ?? "").Equals(System.IO.Path.GetFileNameWithoutExtension(unitId), StringComparison.OrdinalIgnoreCase))
                        {
                            selUnit.ApplyAllConfiguredAttachments();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapEditorHUD] SaveUnitObjectAttachment error: {ex.Message}");
            }
        }

        public void SaveAllUnitObjectAttachments(string targetId, UnitObjectAttachments? attachments)
        {
            RestoreUnitObjectAttachments(targetId, attachments);
        }

        public void SaveCustomWeaponToMetadata(string weaponId, WeaponMetadata weapon)
        {
            try
            {
                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
                if (!System.IO.File.Exists(metadataPath)) return;

                MetadataService.Instance.UpdateMetadata(wsPath, meta =>
                {
                    meta.AddOrUpdateWeapon(weapon);
                });
                _lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
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
                if (string.IsNullOrEmpty(vfxId) || config == null) return;

                Realm.Client.Core.GameHost.VfxRegistry[vfxId] = config.Clone();

                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
                if (!System.IO.File.Exists(metadataPath)) return;

                MetadataService.Instance.UpdateMetadata(wsPath, meta =>
                {
                    meta.AddOrUpdateVfx(config);
                });
                _lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
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
                if (string.IsNullOrEmpty(vfxId)) return;

                if (Realm.Client.Core.GameHost.VfxRegistry != null)
                {
                    Realm.Client.Core.GameHost.VfxRegistry.Remove(vfxId);
                }

                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
                if (!System.IO.File.Exists(metadataPath)) return;

                MetadataService.Instance.UpdateMetadata(wsPath, meta =>
                {
                    meta.RemoveVfx(vfxId);
                });
                _lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
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
                if (string.IsNullOrEmpty(unitId)) return;

                if (Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(unitId, out var uMeta))
                {
                    uMeta.Animations = animations;
                    Realm.Client.Core.GameHost.UnitRegistry[unitId] = uMeta;
                }

                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
                if (!System.IO.File.Exists(metadataPath)) return;

                MetadataService.Instance.UpdateMetadata(wsPath, meta =>
                {
                    meta.UpdateUnit(unitId, u => { u.Animations = animations; return u; });
                });
                _lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
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
                if (string.IsNullOrEmpty(entityId)) return;

                fieldName = string.IsNullOrEmpty(fieldName) ? "ModelPath" : fieldName;
                domain = string.IsNullOrEmpty(domain) ? "units" : domain;

                if (domain.Equals("units", StringComparison.OrdinalIgnoreCase) && Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(entityId, out var uMeta))
                {
                    if (fieldName == "PortraitModelPath")
                    {
                        uMeta.PortraitModelPath = newModelPath;
                    }
                    else
                    {
                        uMeta.ModelPath = newModelPath;
                    }
                    Realm.Client.Core.GameHost.UnitRegistry[entityId] = uMeta;
                }
                else if (domain.Equals("buildings", StringComparison.OrdinalIgnoreCase) && Realm.Client.Core.GameHost.BuildingRegistry.TryGetValue(entityId, out var bMeta))
                {
                    if (fieldName == "PortraitModelPath")
                    {
                        bMeta.PortraitModelPath = newModelPath;
                    }
                    else
                    {
                        bMeta.ModelPath = newModelPath;
                    }
                    Realm.Client.Core.GameHost.BuildingRegistry[entityId] = bMeta;
                }
                else if (domain.Equals("resources", StringComparison.OrdinalIgnoreCase) && Realm.Client.Core.GameHost.ResourceRegistry.TryGetValue(entityId, out var rMeta))
                {
                    if (fieldName == "PortraitModelPath")
                    {
                        rMeta.PortraitModelPath = newModelPath;
                    }
                    else
                    {
                        rMeta.ModelPath = newModelPath;
                    }
                    Realm.Client.Core.GameHost.ResourceRegistry[entityId] = rMeta;
                }
                else if (domain.Equals("props", StringComparison.OrdinalIgnoreCase) && Realm.Client.Core.GameHost.PropRegistry.TryGetValue(entityId, out var pMeta))
                {
                    pMeta.ModelPath = newModelPath;
                    Realm.Client.Core.GameHost.PropRegistry[entityId] = pMeta;
                }

                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
                if (!System.IO.File.Exists(metadataPath)) return;

                MetadataService.Instance.UpdateMetadata(wsPath, meta =>
                {
                    switch (domain.ToLowerInvariant())
                    {
                        case "units":
                            meta.UpdateUnit(entityId, u =>
                            {
                                if (fieldName == "PortraitModelPath") u.PortraitModelPath = newModelPath;
                                else u.ModelPath = newModelPath;
                                return u;
                            });
                            break;
                        case "buildings":
                            meta.UpdateBuilding(entityId, b =>
                            {
                                if (fieldName == "PortraitModelPath") b.PortraitModelPath = newModelPath;
                                else b.ModelPath = newModelPath;
                                return b;
                            });
                            break;
                        case "resources":
                            meta.UpdateResource(entityId, r =>
                            {
                                if (fieldName == "PortraitModelPath") r.PortraitModelPath = newModelPath;
                                else r.ModelPath = newModelPath;
                                return r;
                            });
                            break;
                        case "props":
                            meta.UpdateProp(entityId, p =>
                            {
                                p.ModelPath = newModelPath;
                                return p;
                            });
                            break;
                    }
                });
                _lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);

                Realm.Client.Core.GameHost.Instance?.LoadUnitMetadata(wsPath);
                if (fieldName != "PortraitModelPath")
                {
                    Realm.Client.Core.GameHost.Instance?.RefreshAllPlacedObjectModels(entityId);
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
                if (string.IsNullOrEmpty(abilityId)) return;

                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
                if (!System.IO.File.Exists(metadataPath)) return;

                MetadataService.Instance.UpdateMetadata(wsPath, meta =>
                {
                    meta.UpdateAbility(abilityId, ab =>
                    {
                        ab.VisualEffect = visualEffect;
                        ab.CastSound = castSound;
                        ab.IconPath = iconPath;
                        ab.AreaOfEffectRadius = aoeRadius;
                        return ab;
                    });
                });
                _lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapEditorHUD] SaveCustomAbilityVfxToMetadata error: {ex.Message}");
            }
        }

        private void ExportMapAction()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;

            string wsPath = !string.IsNullOrEmpty(_tempWorkspacePath) ? _tempWorkspacePath : Realm.Client.Services.MapWorkspaceService.GetActiveWorkspacePath();
            var (assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(wsPath);
            if (!assetsValid)
            {
                ShowFeedback(string.Format(TranslationServer.Translate("Export failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
                AppendWasmConsoleLog($"[ERROR] Export failed. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
                return;
            }

            var (sizesValid, oversizedAssets) = MapAssetHelper.ValidateWorkspaceAssetSizes(wsPath);
            if (!sizesValid)
            {
                string oversizedSummary = string.Join("\n", oversizedAssets.Take(4).Select(o => $"• {o.RelativePath} ({o.SizeMB:F2} MB > 15 MB)")) + (oversizedAssets.Count > 4 ? "\n..." : "");
                ShowFeedback(string.Format(TranslationServer.Translate("Export failed: Assets exceed maximum 15 MB size limit:\n{0}"), oversizedSummary));
                AppendWasmConsoleLog($"[ERROR] Export failed. Assets exceed maximum 15 MB size limit:\n{string.Join("\n", oversizedAssets.Select(o => $"  {o.RelativePath} ({o.SizeMB:F2} MB)"))}");
                return;
            }

            string mapTitle = GetMapNameFromMetadata();
            string cleanMapName = string.Join("_", mapTitle.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (string.IsNullOrEmpty(cleanMapName) || cleanMapName.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase)) cleanMapName = "MapExport";

            string mapVersion = GetMapVersionFromMetadata();
            string cleanMapVersion = string.Join("_", mapVersion.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (string.IsNullOrEmpty(cleanMapVersion)) cleanMapVersion = "1.0.0";

            string manifestBlake3 = GetManifestBlake3();
            string normHash = !string.IsNullOrEmpty(manifestBlake3) ? ContentAddressableStorage.NormalizeBlake3Hash(manifestBlake3) : string.Empty;
            string shortHash = normHash.Length >= 4 ? normHash.Substring(0, 4) : (normHash.Length > 0 ? normHash : "0000");

            string defaultFileName = $"{cleanMapName}_{cleanMapVersion}_{shortHash}.rmap";
            string initialDir = GetInitialDirectory();

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
                string fallbackPath = System.IO.Path.Combine(initialDir, defaultFileName);
                _ = ExportMapPackageAsync(fallbackPath);
            }
        }

        public async System.Threading.Tasks.Task ExportMapPackageAsync(string destinationPath, bool fullExport = false)
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;

            if (System.IO.Directory.Exists(destinationPath))
            {
                string mapTitle = GetMapNameFromMetadata();
                string cleanMapName = string.Join("_", mapTitle.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
                if (string.IsNullOrEmpty(cleanMapName) || cleanMapName.Equals("Untitled Map", StringComparison.OrdinalIgnoreCase)) cleanMapName = "MapExport";

                string mapVersion = GetMapVersionFromMetadata();
                string cleanMapVersion = string.Join("_", mapVersion.Split(System.IO.Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
                if (string.IsNullOrEmpty(cleanMapVersion)) cleanMapVersion = "1.0.0";

                string manifestBlake3 = GetManifestBlake3();
                string normHash = !string.IsNullOrEmpty(manifestBlake3) ? ContentAddressableStorage.NormalizeBlake3Hash(manifestBlake3) : string.Empty;
                string shortHash = normHash.Length >= 4 ? normHash.Substring(0, 4) : (normHash.Length > 0 ? normHash : "0000");

                destinationPath = System.IO.Path.Combine(destinationPath, $"{cleanMapName}_{cleanMapVersion}_{shortHash}.rmap");
            }

            var popup = new Panel();
            popup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            popup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
            popup.ZIndex = 1100;
            AddChild(popup);

            var cardPanel = new Panel();
            cardPanel.CustomMinimumSize = new Vector2(560, 260);
            cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
            cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
            popup.AddChild(cardPanel);

            var vbox = new VBoxContainer();
            vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            vbox.CustomMinimumSize = new Vector2(520, 220);
            vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
            vbox.AddThemeConstantOverride("separation", 10);
            cardPanel.AddChild(vbox);

            vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

            var titleLabel = new Label();
            UIStyle.ApplyTitle(titleLabel, "📦 " + TranslationServer.Translate("EXPORTING MAP PACKAGE (.RMAP)"), 20);
            titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
            vbox.AddChild(titleLabel);

            var descLabel = new Label();
            descLabel.Text = string.Format(TranslationServer.Translate("Destination: {0}"), System.IO.Path.GetFileName(destinationPath));
            descLabel.HorizontalAlignment = HorizontalAlignment.Center;
            descLabel.AddThemeFontSizeOverride("font_size", 13);
            descLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
            vbox.AddChild(descLabel);

            vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

            var progressBar = new ProgressBar();
            progressBar.CustomMinimumSize = new Vector2(480, 24);
            progressBar.MinValue = 0;
            progressBar.MaxValue = 100;
            progressBar.Value = 0;
            vbox.AddChild(progressBar);

            var statusLabel = new Label();
            statusLabel.Text = TranslationServer.Translate("Preparing export...");
            statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
            statusLabel.AddThemeFontSizeOverride("font_size", 13);
            statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
            vbox.AddChild(statusLabel);

            vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) });

            var buttonRow = new HBoxContainer();
            buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
            vbox.AddChild(buttonRow);

            var closeBtn = new Button();
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
                Realm.Client.UI.UIManager.Instance?.PlayClickSound();
                if (GodotObject.IsInstanceValid(popup))
                {
                    popup.QueueFree();
                }
            };

            Realm.Client.UI.WasmConsoleWindow.Instance.Hide();
            _wasmHasErrors = false;
            Action<string> logHandler = line => AppendWasmConsoleLog(line);
            Core.WasmRuntime.OnWasmLog += logHandler;

            try
            {
                progressBar.Value = 10;
                statusLabel.Text = TranslationServer.Translate("Saving map state & workspace...");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                string tempTerrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
                Realm.Client.Services.MapWorkspaceService.EnsureLicenseFile(_tempWorkspacePath);
                Realm.Client.Core.GameHost.Instance.SaveMapToFile(tempTerrainPath, performReload: false);
                Realm.Client.Core.GameHost.Instance.EditorHasUnsavedChanges = false;

                if (OperatingSystem.IsWindows())
                {
                    await Realm.Client.VSCodeManager.Instance.SaveAllOpenFilesAsync();
                }

                progressBar.Value = 15;
                statusLabel.Text = TranslationServer.Translate("Verifying map assets...");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                var (assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(_tempWorkspacePath);
                if (!assetsValid)
                {
                    progressBar.Value = 100;
                    statusLabel.Text = "❌ " + TranslationServer.Translate("Missing required asset files. Export aborted.");
                    statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
                    closeBtn.Visible = true;
                    ShowFeedback(string.Format(TranslationServer.Translate("Export failed: Map is missing required asset files:\n{0}"), string.Join(", ", missingAssets.Take(4)) + (missingAssets.Count > 4 ? "..." : "")));
                    AppendWasmConsoleLog($"[ERROR] Export aborted. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
                    return;
                }

                var (sizesValid, oversizedAssets) = MapAssetHelper.ValidateWorkspaceAssetSizes(_tempWorkspacePath);
                if (!sizesValid)
                {
                    progressBar.Value = 100;
                    statusLabel.Text = "❌ " + TranslationServer.Translate("Asset exceeds 15 MB size limit. Export aborted.");
                    statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
                    closeBtn.Visible = true;
                    string oversizedSummary = string.Join("\n", oversizedAssets.Take(4).Select(o => $"• {o.RelativePath} ({o.SizeMB:F2} MB > 15 MB)")) + (oversizedAssets.Count > 4 ? "\n..." : "");
                    ShowFeedback(string.Format(TranslationServer.Translate("Export failed: Assets exceed maximum 15 MB size limit:\n{0}"), oversizedSummary));
                    AppendWasmConsoleLog($"[ERROR] Export aborted. Assets exceed maximum 15 MB size limit:\n{string.Join("\n", oversizedAssets.Select(o => $"  {o.RelativePath} ({o.SizeMB:F2} MB)"))}");
                    return;
                }

                progressBar.Value = 20;
                statusLabel.Text = TranslationServer.Translate("Compiling WASM map script...");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                await CompileAndSignMapAsync(_tempWorkspacePath, skipAttribution: true);

                if (_wasmHasErrors)
                {
                    progressBar.Value = 100;
                    statusLabel.Text = "❌ " + TranslationServer.Translate("WASM compilation failed. Export aborted.");
                    statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
                    closeBtn.Visible = true;
                    ShowFeedback(TranslationServer.Translate("WASM compilation failed. Export aborted."));
                    return;
                }

                var csprojFiles = System.IO.Directory.GetFiles(_tempWorkspacePath, "*.csproj", System.IO.SearchOption.TopDirectoryOnly);
                if (csprojFiles.Length > 0 || System.IO.File.Exists(System.IO.Path.Combine(_tempWorkspacePath, "MapScript.cs")))
                {
                    string binDir = System.IO.Path.Combine(_tempWorkspacePath, "bin");
                    string wasmPath = null;
                    if (System.IO.Directory.Exists(binDir))
                    {
                        var wasmFiles = System.IO.Directory.GetFiles(
                            binDir,
                            "*.wasm",
                            System.IO.SearchOption.AllDirectories
                        ).Where(f => !f.Contains("native") && !f.Contains("obj")).ToList();

                        wasmPath = wasmFiles.FirstOrDefault(f => f.Contains("publish"))
                                   ?? wasmFiles.OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f)).FirstOrDefault();
                    }

                    if (string.IsNullOrEmpty(wasmPath) || !System.IO.File.Exists(wasmPath))
                    {
                        _wasmHasErrors = true;
                        progressBar.Value = 100;
                        statusLabel.Text = "❌ " + TranslationServer.Translate("Compiled WASM binary missing. Export aborted.");
                        statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
                        closeBtn.Visible = true;
                        ShowFeedback(TranslationServer.Translate("Compiled WASM binary missing. Export aborted."));
                        return;
                    }
                }

                progressBar.Value = 35;
                statusLabel.Text = TranslationServer.Translate("Optimizing 3D assets...");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                await Realm.Client.Services.MapWorkspaceService.EnsureGlbAssetsOptimizedCooperativeAsync(_tempWorkspacePath, async (current, total, fileName) =>
                {
                    float fraction = total > 0 ? (float)current / total : 1.0f;
                    progressBar.Value = 35 + fraction * 20;
                    statusLabel.Text = string.Format(TranslationServer.Translate("Optimizing 3D model {0}/{1}: {2}..."), current, total, fileName);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                });

                progressBar.Value = 55;
                statusLabel.Text = TranslationServer.Translate("Converting textures...");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                await Realm.Client.Services.MapWorkspaceService.EnsurePngAssetsConvertedCooperativeAsync(_tempWorkspacePath, async (current, total, fileName) =>
                {
                    float fraction = total > 0 ? (float)current / total : 1.0f;
                    progressBar.Value = 55 + fraction * 15;
                    statusLabel.Text = string.Format(TranslationServer.Translate("Converting texture {0}/{1}: {2}..."), current, total, fileName);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                });

                progressBar.Value = 70;
                statusLabel.Text = TranslationServer.Translate("Generating map thumbnail...");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                if (_minimapController != null)
                {
                    await _minimapController.GenerateAndSaveMinimapThumbnailAsync(_tempWorkspacePath);
                }

                progressBar.Value = 75;
                statusLabel.Text = TranslationServer.Translate("Generating manifest & indexing asset hashes...");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                string mapTitle = GetMapNameFromMetadata();
                string mapVersion = GetMapVersionFromMetadata();
                string metaJsonPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");
                if (System.IO.File.Exists(metaJsonPath))
                {
                    try
                    {
                        var doc = JsonNode.Parse(System.IO.File.ReadAllText(metaJsonPath));
                        if (doc != null)
                        {
                            if (doc["MapProperties"] is JsonObject props)
                            {
                                mapTitle = props["MapTitle"]?.ToString() ?? props["MapName"]?.ToString() ?? mapTitle;
                                mapVersion = props["MapVersion"]?.ToString() ?? props["Version"]?.ToString() ?? mapVersion;
                            }
                            else
                            {
                                mapTitle = doc["MapName"]?.ToString() ?? doc["MapTitle"]?.ToString() ?? mapTitle;
                                mapVersion = doc["Version"]?.ToString() ?? doc["MapVersion"]?.ToString() ?? mapVersion;
                            }
                        }
                    }
                    catch { }
                }

                string author = Realm.Client.Network.LobbyManager.Instance?.AuthenticatedUsername ?? "MapAuthor";
                var manifest = MapManifest.CreateFromDirectory(_tempWorkspacePath, mapTitle, author, mapVersion);
                string manifestJsonPath = System.IO.Path.Combine(_tempWorkspacePath, "manifest.json");
                System.IO.File.WriteAllText(manifestJsonPath, manifest.ToJson());

                progressBar.Value = 80;
                statusLabel.Text = TranslationServer.Translate("Compressing package into .rmap archive...");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                var excludedPaths = !fullExport ? MapNormalizationHelper.GetExcludedRelativePaths(_tempWorkspacePath) : null;
                long lastProgressUpdateTicks = 0;
                await System.Threading.Tasks.Task.Run(() =>
                {
                    MapArchiveHelper.CreateRmapArchive(_tempWorkspacePath, destinationPath, (pct, file) =>
                    {
                        long now = System.Environment.TickCount64;
                        if (now - lastProgressUpdateTicks < 50 && pct < 1.0f)
                        {
                            return;
                        }
                        lastProgressUpdateTicks = now;
                        string fileName = System.IO.Path.GetFileName(file);
                        Callable.From(() =>
                        {
                            if (GodotObject.IsInstanceValid(progressBar) && GodotObject.IsInstanceValid(statusLabel))
                            {
                                progressBar.Value = 80 + pct * 18;
                                statusLabel.Text = string.Format(TranslationServer.Translate("Compressing {0} ({1}%)..."), fileName, (int)(pct * 100));
                            }
                        }).CallDeferred();
                    }, compressionLevel: 1, fullExport: fullExport, excludedRelativePaths: excludedPaths);
                });

                progressBar.Value = 100;
                statusLabel.Text = "✓ " + TranslationServer.Translate("Export completed successfully!");
                statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.3f));
                await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);

                if (GodotObject.IsInstanceValid(popup))
                {
                    popup.QueueFree();
                }

                ShowFeedback(string.Format(TranslationServer.Translate("Map exported successfully to {0}!"), System.IO.Path.GetFileName(destinationPath)));
                Realm.Client.UI.UIManager.Instance?.ShowConfirmationDialog(
                    string.Format(TranslationServer.Translate("Map exported successfully to {0}."), destinationPath),
                    () => { },
                    confirmText: "OK",
                    showCancel: false
                );
            }
            catch (Exception ex)
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
            finally
            {
                Core.WasmRuntime.OnWasmLog -= logHandler;
                if (_wasmHasErrors)
                {
                    Realm.Client.UI.WasmConsoleWindow.Instance.ShowConsole();
                }
                else
                {
                    Realm.Client.UI.WasmConsoleWindow.Instance.Hide();
                }
            }
        }

        private Texture2D LoadSwatchTextureInternal(int i)
        {
            string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                : _tempWorkspacePath;
            if (i >= 0 && i < _swatchDisplayNames.Count && _swatchDisplayNames[i].EndsWith("(Empty)"))
            {
                return null;
            }
            string localRtex = "";
            if (i >= 0 && i < _swatchPaths.Count && !string.IsNullOrEmpty(_swatchPaths[i]) && System.IO.File.Exists(_swatchPaths[i]))
            {
                localRtex = _swatchPaths[i];
            }
            else
            {
                string texName = (i >= 0 && i < _swatchDisplayNames.Count) ? _swatchDisplayNames[i] : $"swatch_{i}";
                if (string.IsNullOrEmpty(texName) || texName.EndsWith("(Empty)")) return null;

                string rtexName = texName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? texName : $"{texName}.rtex";
                try
                {
                    var metadata = Realm.Shared.Services.MapFileService.LoadMetadata(wsPath);
                    if (metadata != null)
                    {
                        var texMeta = metadata.GetTerrainTexture(texName) ?? metadata.GetTerrainTexture($"terrain/{texName}");
                        if (texMeta != null && !string.IsNullOrWhiteSpace(texMeta.TexturePath))
                        {
                            rtexName = texMeta.TexturePath;
                            if (!rtexName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
                            {
                                rtexName += ".rtex";
                            }
                            rtexName = System.IO.Path.GetFileName(rtexName);
                        }
                    }
                }
                catch { }

                localRtex = System.IO.Path.Combine(wsPath, "Assets", "textures", rtexName);
                if (!System.IO.File.Exists(localRtex))
                {
                    localRtex = System.IO.Path.Combine(wsPath, rtexName);
                }
                if (!System.IO.File.Exists(localRtex))
                {
                    string? found = PathUtils.FindPath($"Assets/textures/{rtexName}");
                    if (!string.IsNullOrEmpty(found) && System.IO.File.Exists(found))
                    {
                        localRtex = found;
                    }
                }
                if (!System.IO.File.Exists(localRtex))
                {
                    string? foundTemplate = PathUtils.FindPath($"MapTemplate/Assets/textures/{rtexName}");
                    if (!string.IsNullOrEmpty(foundTemplate) && System.IO.File.Exists(foundTemplate))
                    {
                        localRtex = foundTemplate;
                    }
                }
            }
            if (System.IO.File.Exists(localRtex))
            {
                try
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(localRtex);
                    byte[]? webpBytes = Realm.Shared.Textures.RtexFile.GetLayer(bytes, 0);
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
            }
            if (i >= 0 && i < _swatchPaths.Count && ResourceLoader.Exists(_swatchPaths[i]))
            {
                return GD.Load<Texture2D>(_swatchPaths[i]);
            }
            return null;
        }

        private void ImportTextureAction()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;
            int selectedIdx = Realm.Client.Core.GameHost.Instance.EditorPaintTextureIndex;
            if (selectedIdx < 0 || selectedIdx >= _swatchDisplayNames.Count)
            {
                ShowFeedback(TranslationServer.Translate("Please select a texture slot first"));
                return;
            }

            OpenAssetBrowser("Import Texture Image", new[] { ".rtex" }, (imagePath, preferredName) =>
            {
                if (selectedIdx >= 0 && selectedIdx < _swatchPaths.Count && !string.IsNullOrEmpty(_swatchPaths[selectedIdx]))
                {
                    string cleanPrevName = _swatchDisplayNames[selectedIdx];
                    string msg = string.Format(TranslationServer.Translate("Replacing texture in Slot {0} ({1}) will update all areas of the terrain painted with this slot. Do you want to proceed?"), selectedIdx, cleanPrevName);
                    ShowConfirmationDialog(msg, () =>
                    {
                        ImportTextureFile(imagePath, selectedIdx, preferredName);
                    }, confirmText: "REPLACE", cancelText: "CANCEL");
                }
                else
                {
                    ImportTextureFile(imagePath, selectedIdx, preferredName);
                }
            }, requireRealmMetadata: false);
        }

        private void ImportTextureFile(string imagePath, int index, string? preferredFileName = null)
        {
            string resolvedName = !string.IsNullOrWhiteSpace(preferredFileName) && !AssetIndexService.IsHexHash(System.IO.Path.GetFileNameWithoutExtension(preferredFileName))
                ? preferredFileName
                : (AssetIndexService.Instance?.ResolvePrettyFileName(imagePath, preferredFileName) ?? System.IO.Path.GetFileName(imagePath));
            string cleanBaseName = System.IO.Path.GetFileNameWithoutExtension(resolvedName).ToLowerInvariant().Replace(" ", "_") + ".rtex";
            string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                : _tempWorkspacePath;
            string texDir = System.IO.Path.Combine(wsPath, "Assets", "textures");
            System.IO.Directory.CreateDirectory(texDir);
            string outputRtex = System.IO.Path.Combine(texDir, cleanBaseName);
            ShowFeedback(TranslationServer.Translate("Importing texture..."));
            try
            {
                string ext = System.IO.Path.GetExtension(imagePath);
                if (ext.Equals(".rtex", StringComparison.OrdinalIgnoreCase))
                {
                    System.IO.File.Copy(imagePath, outputRtex, true);
                }
                else if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.GroundTerrain != null)
                {
                    Realm.Client.Core.GameHost.Instance.GroundTerrain.ProcessAndSaveRawTexture(imagePath, outputRtex);
                }

                if (System.IO.File.Exists(outputRtex))
                {
                    byte[] rtexBytes = System.IO.File.ReadAllBytes(outputRtex);
                    string blake3 = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");
                    UpdateMetadataJsonAsset("textures", cleanBaseName, blake3, targetSlot: index);
                }
                if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.GroundTerrain != null)
                {
                    Realm.Client.Core.GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
                }
                SetupTextureSwatches(false);
                ShowFeedback(string.Format(TranslationServer.Translate("Successfully imported custom texture for {0}!"), cleanBaseName));
            }
            catch (Exception ex)
            {
                ShowFeedback(string.Format(TranslationServer.Translate("Failed to import texture: {0}"), ex.Message));
                GD.PrintErr($"Failed to import texture: {ex.Message}");
            }
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
            if (string.IsNullOrWhiteSpace(candidateName))
            {
                return "Untitled Map";
            }

            string sanitized = candidateName.Replace(Realm.Client.Services.MapWorkspaceService.DefaultWorkspaceFolder, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            return string.IsNullOrEmpty(sanitized) ? "Untitled Map" : sanitized;
        }

        private static bool TrySanitizeCandidate(string? candidateName, out string sanitizedMapName)
        {
            sanitizedMapName = string.Empty;
            if (string.IsNullOrWhiteSpace(candidateName))
            {
                return false;
            }

            string cleaned = candidateName.Replace(Realm.Client.Services.MapWorkspaceService.DefaultWorkspaceFolder, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            if (string.IsNullOrEmpty(cleaned))
            {
                return false;
            }

            sanitizedMapName = cleaned;
            return true;
        }

        public string GetMapVersionFromMetadata(bool forceReload = false)
        {
            long now = System.Environment.TickCount64;
            if (!forceReload && !string.IsNullOrEmpty(_cachedMapVersion) && (now - _lastMapNameCacheTicks < 2000))
            {
                return _cachedMapVersion;
            }

            try
            {
                string workspacePath = !string.IsNullOrEmpty(_tempWorkspacePath) ? _tempWorkspacePath : Realm.Client.Services.MapWorkspaceService.GetActiveWorkspacePath();
                var manifest = MapFileService.LoadManifest(workspacePath);
                if (!string.IsNullOrWhiteSpace(manifest.Version))
                {
                    _cachedMapVersion = manifest.Version.Trim();
                    return _cachedMapVersion;
                }

                var metadata = MapFileService.LoadMetadata(workspacePath);
                if (!string.IsNullOrWhiteSpace(metadata.MapProperties?.Version))
                {
                    _cachedMapVersion = metadata.MapProperties.Version.Trim();
                    return _cachedMapVersion;
                }

                _cachedMapVersion = "1.0.0";
                return "1.0.0";
            }
            catch
            {
                _cachedMapVersion = "1.0.0";
                return "1.0.0";
            }
        }

        public string GetManifestBlake3()
        {
            try
            {
                string workspacePath = !string.IsNullOrEmpty(_tempWorkspacePath) ? _tempWorkspacePath : Realm.Client.Services.MapWorkspaceService.GetActiveWorkspacePath();
                string manifestJsonPath = System.IO.Path.Combine(workspacePath, "manifest.json");
                if (System.IO.File.Exists(manifestJsonPath))
                {
                    string manifestBlake3 = RealmMetadataHelper.ComputeBlake3(manifestJsonPath);
                    if (!string.IsNullOrEmpty(manifestBlake3))
                    {
                        return manifestBlake3;
                    }
                }

                if (System.IO.Directory.Exists(workspacePath))
                {
                    string mapTitle = GetMapNameFromMetadata();
                    string mapVersion = GetMapVersionFromMetadata();
                    string author = Realm.Client.Network.LobbyManager.Instance?.AuthenticatedUsername ?? "MapAuthor";
                    var manifest = MapManifest.CreateFromDirectory(workspacePath, mapTitle, author, mapVersion);
                    return manifest.ComputeManifestBlake3();
                }
            }
            catch { }

            return string.Empty;
        }

        public void ImportTextureAssetFromExtension(string sourceFilePath, int slotIndex)
        {
            try
            {
                string cleanBaseName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(" ", "_") + ".rtex";
                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string texDir = System.IO.Path.Combine(wsPath, "Assets", "textures");
                System.IO.Directory.CreateDirectory(texDir);
                string outputRtex = System.IO.Path.Combine(texDir, cleanBaseName);

                string ext = System.IO.Path.GetExtension(sourceFilePath);
                if (ext.Equals(".rtex", StringComparison.OrdinalIgnoreCase))
                {
                    System.IO.File.Copy(sourceFilePath, outputRtex, true);
                }
                else if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.GroundTerrain != null)
                {
                    Realm.Client.Core.GameHost.Instance.GroundTerrain.ProcessAndSaveRawTexture(sourceFilePath, outputRtex);
                }

                if (System.IO.File.Exists(outputRtex))
                {
                    byte[] rtexBytes = System.IO.File.ReadAllBytes(outputRtex);
                    string blake3 = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");
                    UpdateMetadataJsonAsset("textures", cleanBaseName, blake3, targetSlot: slotIndex);
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
                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string metadataPath = System.IO.Path.Combine(wsPath, "metadata.json");

                string texDir = System.IO.Path.Combine(wsPath, "Assets", "textures");
                System.IO.Directory.CreateDirectory(texDir);

                if (System.IO.File.Exists(metadataPath))
                {
                    Realm.Client.Services.MapWorkspaceService.NormalizeMetadataTextureEntries(wsPath);
                }
                InvalidateMetadataCache();

                _swatchTextureCache.Clear();
                if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.GroundTerrain != null)
                {
                    Realm.Client.Core.GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
                }
                SetupTextureSwatches(false);
                RefreshSkyboxList();
                if (System.IO.File.Exists(metadataPath))
                {
                    Realm.Client.Core.GameHost.Instance?.LoadModelYOffsetsFromMetadataJson(wsPath);
                    Realm.Client.Core.GameHost.Instance?.LoadUnitMetadata(wsPath);
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
            OpenAssetBrowser("Select Animation (.ranim)", new[] { ".ranim" }, (path, preferredName) =>
            {
                ImportAnimationAssetFromExtension(path, preferredName);
            });
        }

        public void ImportAnimationAssetFromExtension(string sourceFilePath, string? preferredFileName = null)
        {
            try
            {
                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string ext = System.IO.Path.GetExtension(sourceFilePath).ToLowerInvariant();
                string animsDir = System.IO.Path.Combine(wsPath, "Assets", "animations");
                System.IO.Directory.CreateDirectory(animsDir);

                if (ext == ".glb" || ext == ".gltf" || ext == ".fbx")
                {
                    string originalFileName = !string.IsNullOrWhiteSpace(preferredFileName) && !AssetIndexService.IsHexHash(System.IO.Path.GetFileNameWithoutExtension(preferredFileName))
                        ? System.IO.Path.GetFileNameWithoutExtension(preferredFileName)
                        : (AssetIndexService.Instance?.ResolvePrettyFileName(sourceFilePath, preferredFileName) ?? System.IO.Path.GetFileNameWithoutExtension(sourceFilePath));
                    originalFileName = System.IO.Path.GetFileNameWithoutExtension(originalFileName);
                    var extracted = Realm.Client.Animation.MixamoAnimationImporter.ExtractAnimationsFromFile(sourceFilePath, originalFileName);
                    if (extracted.Count == 0)
                    {
                        ShowFeedback(TranslationServer.Translate("No animations found in file."));
                        return;
                    }

                    int importedCount = 0;
                    int skippedCount = 0;
                    foreach (var (animName, animData) in extracted)
                    {
                        var (savedFileName, blake3, alreadyExisted) = Realm.Client.Animation.MixamoAnimationImporter.SaveAnimationWithDeduplication(animsDir, animName, animData);
                        UpdateMetadataJsonAsset("animations", savedFileName, blake3);
                        if (alreadyExisted) skippedCount++;
                        else importedCount++;
                    }

                    PopulateAnimationPreviewDropdown();
                    if (importedCount > 0)
                    {
                        ShowFeedback(string.Format(TranslationServer.Translate("Successfully imported {0} animation(s) (.ranim)!"), importedCount));
                    }
                    else
                    {
                        ShowFeedback(TranslationServer.Translate("Animation already imported (identical BLAKE3 hash)."));
                    }
                }
                else
                {
                    string resolvedName = !string.IsNullOrWhiteSpace(preferredFileName) && !AssetIndexService.IsHexHash(System.IO.Path.GetFileNameWithoutExtension(preferredFileName))
                        ? preferredFileName
                        : (AssetIndexService.Instance?.ResolvePrettyFileName(sourceFilePath, preferredFileName) ?? System.IO.Path.GetFileName(sourceFilePath));
                    string fileName = resolvedName;
                    byte[] sourceBytes = System.IO.File.ReadAllBytes(sourceFilePath);
                    string newHash = RealmMetadataHelper.ComputeBlake3(sourceBytes, ".ranim");

                    string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
                    string finalFileName = $"{baseName}.ranim";
                    string targetPath = System.IO.Path.Combine(animsDir, finalFileName);

                    if (System.IO.File.Exists(targetPath))
                    {
                        string existingHash = RealmMetadataHelper.ComputeBlake3(System.IO.File.ReadAllBytes(targetPath), ".ranim");
                        if (existingHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
                        {
                            UpdateMetadataJsonAsset("animations", finalFileName, newHash);
                            PopulateAnimationPreviewDropdown();
                            ShowFeedback(TranslationServer.Translate("Animation already imported (identical BLAKE3 hash)."));
                            return;
                        }

                        for (int i = 1; i <= 9999; i++)
                        {
                            string varName = $"{baseName}_{i}.ranim";
                            string varPath = System.IO.Path.Combine(animsDir, varName);
                            if (!System.IO.File.Exists(varPath))
                            {
                                finalFileName = varName;
                                targetPath = varPath;
                                System.IO.File.WriteAllBytes(targetPath, sourceBytes);
                                break;
                            }
                            else
                            {
                                string varHash = RealmMetadataHelper.ComputeBlake3(System.IO.File.ReadAllBytes(varPath), ".ranim");
                                if (varHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
                                {
                                    finalFileName = varName;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        System.IO.File.WriteAllBytes(targetPath, sourceBytes);
                    }

                    UpdateMetadataJsonAsset("animations", finalFileName, newHash);
                    PopulateAnimationPreviewDropdown();
                    ShowFeedback(string.Format(TranslationServer.Translate("Imported animation {0}"), finalFileName));
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ImportAnimationAssetFromExtension error: {ex.Message}");
                ShowFeedback(string.Format(TranslationServer.Translate("Failed to import animation: {0}"), ex.Message));
            }
        }

        public void ImportDecalAssetFromExtension(string sourceFilePath)
        {
            try
            {
                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string baseName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
                string fileName = baseName + ".png";
                string decalsDir = System.IO.Path.Combine(wsPath, "Assets", "decals");
                System.IO.Directory.CreateDirectory(decalsDir);
                string targetPath = System.IO.Path.Combine(decalsDir, fileName);

                var img = Image.LoadFromFile(sourceFilePath);
                if (img != null)
                {
                    img.SavePng(targetPath);
                }
                else
                {
                    System.IO.File.Copy(sourceFilePath, targetPath, true);
                }

                byte[] bytes = System.IO.File.ReadAllBytes(targetPath);
                string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".png");

                UpdateMetadataJsonAsset("decals", fileName, blake3);
                ShowFeedback($"Imported decal {fileName}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ImportDecalAssetFromExtension error: {ex.Message}");
                ShowFeedback($"Failed to import decal: {ex.Message}");
            }
        }

        public void ImportSkyboxAssetFromExtension(string sourceFilePath)
        {
            try
            {
                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string fileName = System.IO.Path.GetFileName(sourceFilePath);
                string skyboxesDir = System.IO.Path.Combine(wsPath, "Assets", "skyboxes");
                System.IO.Directory.CreateDirectory(skyboxesDir);
                string targetPath = System.IO.Path.Combine(skyboxesDir, fileName);

                System.IO.File.Copy(sourceFilePath, targetPath, true);

                byte[] bytes = System.IO.File.ReadAllBytes(targetPath);
                string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, fileName);

                UpdateMetadataJsonAsset("skyboxes", fileName, blake3);
                RefreshSkyboxList();
                ShowFeedback($"Imported skybox {fileName}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ImportSkyboxAssetFromExtension error: {ex.Message}");
                ShowFeedback($"Failed to import skybox: {ex.Message}");
            }
        }

        public void ImportSpritesheetAssetFromExtension(string sourceFilePath, int columns = 4, int rows = 4)
        {
            try
            {
                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string baseName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
                string fileName = baseName + ".png";
                string vfxDir = System.IO.Path.Combine(wsPath, "Assets", "vfx");
                System.IO.Directory.CreateDirectory(vfxDir);
                string targetPath = System.IO.Path.Combine(vfxDir, fileName);

                var img = Image.LoadFromFile(sourceFilePath);
                if (img != null)
                {
                    img.SavePng(targetPath);
                }
                else
                {
                    System.IO.File.Copy(sourceFilePath, targetPath, true);
                }

                byte[] bytes = System.IO.File.ReadAllBytes(targetPath);
                string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".png");

                UpdateMetadataJsonAsset("vfx_spritesheets", fileName, blake3, columns: columns, rows: rows);
                ShowFeedback($"Imported VFX spritesheet {fileName}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ImportSpritesheetAssetFromExtension error: {ex.Message}");
                ShowFeedback($"Failed to import VFX spritesheet: {ex.Message}");
            }
        }

        public void ImportIconAssetFromExtension(string sourceFilePath)
        {
            try
            {
                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string baseName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
                string fileName = baseName + ".png";
                string iconsDir = System.IO.Path.Combine(wsPath, "Assets", "icons");
                System.IO.Directory.CreateDirectory(iconsDir);
                string targetPath = System.IO.Path.Combine(iconsDir, fileName);

                var img = Image.LoadFromFile(sourceFilePath);
                if (img != null)
                {
                    img.SavePng(targetPath);
                }
                else
                {
                    System.IO.File.Copy(sourceFilePath, targetPath, true);
                }

                byte[] bytes = System.IO.File.ReadAllBytes(targetPath);
                string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".png");

                UpdateMetadataJsonAsset("icons", fileName, blake3);
                ShowFeedback($"Imported 2D Icon {fileName}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ImportIconAssetFromExtension error: {ex.Message}");
                ShowFeedback($"Failed to import 2D Icon: {ex.Message}");
            }
        }

        public void ImportAudioAssetFromExtension(string sourceFilePath, string audioType)
        {
            try
            {
                string wsPath = string.IsNullOrEmpty(_tempWorkspacePath)
                    ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                    : _tempWorkspacePath;
                string baseName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
                string fileName = baseName + ".ogg";
                string targetPath = System.IO.Path.Combine(wsPath, fileName);

                if (sourceFilePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                {
                    System.IO.File.Copy(sourceFilePath, targetPath, true);
                }
                else
                {
                    Realm.Shared.Audio.AudioConverter.ConvertToOgg(sourceFilePath, targetPath);
                    if (!System.IO.File.Exists(targetPath) || new System.IO.FileInfo(targetPath).Length == 0)
                    {
                        System.IO.File.Copy(sourceFilePath, targetPath, true);
                    }
                }

                byte[] bytes = System.IO.File.ReadAllBytes(targetPath);
                string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".ogg");

                UpdateMetadataJsonAsset(audioType.ToLowerInvariant() == "music" ? "music" : "sfx", fileName, blake3);
                ShowFeedback($"Imported audio {fileName} ({audioType})");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ImportAudioAssetFromExtension error: {ex.Message}");
                ShowFeedback($"Failed to import audio asset: {ex.Message}");
            }
        }
    }
}
