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
				string? targetDir = Path.GetDirectoryName(targetFile);
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
			EditorWorkspaceWatcher.Start(fullTarget);

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

			WorkspaceMetadataBootstrapper.EnsureLicenseFile(GameContext.EditorService._editingDirectory);
			MapWorkspaceService.EnsureSourceMapAttributionAttribute(GameContext.EditorService._editingDirectory);
			MapWorkspaceService.EnsureWasmEntryPoint(GameContext.EditorService._editingDirectory);
			string tempTerrainPath = Path.Combine(GameContext.EditorService._editingDirectory, "terrain.json");
			if (GameContext.Host != null)
			{
				GameContext.Host.SaveMapToFile(tempTerrainPath);
				GameContext.Host.EditorHasUnsavedChanges = false;
				InvalidateMetadataCache();
			}

			if (!ApplyMapReferencesOnSave(GameContext.EditorService._editingDirectory))
			{
				return;
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
					EditorWorkspaceWatcher.Start(Path.GetFullPath(targetFolder));
				}

				WorkspaceMetadataBootstrapper.EnsureLicenseFile(targetFolder);
				MapWorkspaceService.EnsureWasmEntryPoint(targetFolder);

				RefreshSavedDirectoryHash();
				GameContext.EditorService.LastTerrainSyncTime = GameContext.EditorService.GetMaxTerrainWriteTime(tempTerrainPath);
				GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(Path.Combine(GameContext.EditorService._editingDirectory, "metadata.json"));

				ShowFeedback(string.Format(TranslationServer.Translate("Map saved successfully to folder {0}!"), Path.GetFileName(targetFolder)));
				RecordMapVersion(MapVersionKind.ManualSave, TranslationServer.Translate("Save"));
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] SaveMapToFolderAsync failed: {ex}");
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
			if (!MapNaming.IsUntitledMapName(mapName))
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
	}
}
