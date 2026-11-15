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
			var (assetsValid, missingAssets) = GameContext.MapStorageService.ValidateWorkspaceAssetsForExportDomain(wsPath);
			if (assetsValid) return true;

			ShowFeedback(string.Format(TranslationServer.Translate("Export failed: Map is missing required asset files:\n{0}"), string.Join(", ", Enumerable.Take(missingAssets, 4)) + (missingAssets.Count > 4 ? "..." : "")));
			AppendWasmConsoleLog($"[ERROR] Export failed. Missing required asset files:\n  {string.Join("\n  ", missingAssets)}");
			return false;
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

		private Action<string>? _exportLogHandler { get; set; }

		private async Task<string?> PerformExportStepsAsync(ProgressBar progressBar, Label statusLabel, Button closeBtn, string destinationPath, bool fullExport)
		{
			List<string> copiedFiles = await SaveMapStateAsync(progressBar, statusLabel);

			try
			{
				if (!await CompileMapScriptAsync(progressBar, statusLabel, closeBtn)) return null;

				if (!await VerifyMapAssetsAsync(progressBar, statusLabel, closeBtn)) return null;

				GameContext.MapStorageService.DeduplicateWorkspaceBuildArtifacts(GameContext.EditorService._editingDirectory);

				await OptimizeMapAssetsAsync(progressBar, statusLabel);

				await GenerateMapMetadataAsync(progressBar, statusLabel);

				return await CreateMapArchiveAsync(progressBar, statusLabel, destinationPath, fullExport);
			}
			finally
			{
				MapReferenceResolver.RestoreReadOnly(copiedFiles);
			}
		}

		/// <summary>
		///     Writes the map state an export archives. The export has to write across the whole folder, so the read-only mark
		///     on the files a save copied in is lifted here and put back once the export is done: the paths lifted are returned
		///     so the export can mark exactly those again.
		/// </summary>
		private async Task<List<string>> SaveMapStateAsync(ProgressBar progressBar, Label statusLabel)
		{
			progressBar.Value = 10;
			statusLabel.Text = TranslationServer.Translate("Saving map state & workspace...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string editingDirectory = GameContext.EditorService._editingDirectory;
			ApplyMapReferencesOnSave(editingDirectory);
			List<string> copiedFiles = MapReferenceResolver.CollectReadOnlyPaths(editingDirectory);
			MapReferenceResolver.ClearReadOnlyRecursive(editingDirectory);
			await GameContext.MapStorageService.SaveMapStateForExportAsync(editingDirectory, OperatingSystem.IsWindows());
			return copiedFiles;
		}

		private async Task<bool> VerifyMapAssetsAsync(ProgressBar progressBar, Label statusLabel, Button closeBtn)
		{
			progressBar.Value = 25;
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

			if (!CheckScriptIsOptimized(editingDirectory))
			{
				AbortExport(progressBar, statusLabel, closeBtn, "The map script is not an optimized release build. Export aborted.", null);
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
			progressBar.Value = 15;
			statusLabel.Text = TranslationServer.Translate("Compiling WASM map script...");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			string editingDirectory = GameContext.EditorService._editingDirectory;
			await CompileWorkspaceScriptAsync(editingDirectory);

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

		private bool CheckScriptIsOptimized(string workspace)
		{
			string? wasmPath = MapStorageService.FindWasmPath(workspace);
			if (string.IsNullOrEmpty(wasmPath) || !MapScriptArtifacts.ContainsDebugInformation(wasmPath)) return true;

			ShowFeedback(TranslationServer.Translate("The compiled map script still carries debug information. Recompile it for release and try again."));
			AppendWasmConsoleLog("[ERROR] The compiled map script still carries DWARF debug information, so it is not a release build.");
			return false;
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

			await SignMapMetadataAsync(editingDirectory, mapTitle, mapVersion, author);

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

		private void AbortExport(ProgressBar progressBar, Label statusLabel, Button closeBtn, string labelMessage, string? logMessage)
		{
			progressBar.Value = 100;
			statusLabel.Text = "❌ " + TranslationServer.Translate(labelMessage);
			statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
			closeBtn.Visible = true;
			if (logMessage != null) AppendWasmConsoleLog(logMessage);
		}

		// Runs after the export itself has already committed, so the work here is only the confirmation. It had no catch:
		// a fault while the tree was tearing down left the success dialog unborn with nothing in the log.
		private async void FinalizeSuccessfulExport(Panel popup, string destinationPath, Label statusLabel, ProgressBar progressBar)
		{
			try
			{
				await ShowExportConfirmationAsync(popup, destinationPath, statusLabel, progressBar);
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[MapEditorHUD] The export confirmation could not be shown: {exception}");
			}
		}

		private async Task ShowExportConfirmationAsync(Panel popup, string destinationPath, Label statusLabel, ProgressBar progressBar)
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
				confirmText: TranslationServer.Translate("OK"),
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
	}
}
