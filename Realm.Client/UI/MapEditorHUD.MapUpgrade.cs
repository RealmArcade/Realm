using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using NSec.Cryptography;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.Services.Publishing;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = System.Environment;
using HttpClient = System.Net.Http.HttpClient;
using Key = Godot.Key;
using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using TerrainCell = Realm.Ecs.Components.Terrain.TerrainCell;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;

using static Godot.Control;
using Realm.Client.Utils;

namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		private async Task<bool> PromptAndUpgradeMapIfNeededAsync(string selectedFolder)
		{
			var upgradeService = GameContext.MapUpgradeService ?? GameContext.MapUpgradeService;
			if (upgradeService == null || !upgradeService.NeedsUpgrade(selectedFolder, out string? currentVersion, out string? targetVersion))
			{
				return true;
			}

			if (upgradeService.IsMapNewerThanGame(currentVersion, targetVersion))
			{
				ShowMapNewerThanGameErrorDialog(selectedFolder, currentVersion, targetVersion);
				return false;
			}

			var tcs = new TaskCompletionSource<bool>();
			var dialog = new MapUpgradeDialog();
			dialog.Initialize(selectedFolder, currentVersion, targetVersion);
			AddChild(dialog);

			var progressBar = dialog.Progress;
			var statusLabel = dialog.StatusLabel;
			var upgradeBtn = dialog.UpgradeButton;
			var cancelBtn = dialog.CancelButton;

			cancelBtn.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				dialog.QueueFree();
				tcs.TrySetResult(false);
			};

			upgradeBtn.Pressed += async () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				upgradeBtn.Disabled = true;
				cancelBtn.Disabled = true;
				progressBar.Visible = true;
				statusLabel.Visible = true;

				if (await TryRunMapUpgradeAsync(selectedFolder, upgradeService, progressBar, statusLabel, cancelBtn))
				{
					dialog.QueueFree();
					tcs.TrySetResult(true);
				}
			};

			return await tcs.Task;
		}

		private async Task<bool> TryRunMapUpgradeAsync(
			string selectedFolder, MapUpgradeService upgradeService, ProgressBar progressBar, Label statusLabel, Button cancelBtn)
		{
			var progress = new Progress<MigrationProgressUpdate>(update =>
			{
				progressBar.Value = update.TotalSteps > 0 ? (double)update.StepIndex / update.TotalSteps * 100.0 : 0;
				statusLabel.Text = $"[{update.StepIndex}/{update.TotalSteps}] {update.CurrentMigration}: {update.Message}";
			});

			var versionBeforeUpgrade = await GameContext.MapHistoryService.CreateVersionAsync(selectedFolder, MapVersionKind.BeforeMapUpgrade, TranslationServer.Translate("Before map upgrade"));
			if (!versionBeforeUpgrade.Success)
			{
				statusLabel.Text = $"{TranslationServer.Translate("Could not save a version before the upgrade:")} {versionBeforeUpgrade.ErrorMessage}";
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
				cancelBtn.Disabled = false;
				UIStyle.ApplyButtonText(cancelBtn, Tr("CLOSE"), 14);
				return false;
			}

			var upgradeResult = await upgradeService.UpgradeMapAsync(selectedFolder, progress);
			if (!upgradeResult.Success)
			{
				statusLabel.Text = $"{Tr("Upgrade failed:")} {upgradeResult.ErrorMessage}";
				statusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.3f, 0.3f));
				cancelBtn.Disabled = false;
				UIStyle.ApplyButtonText(cancelBtn, Tr("CLOSE"), 14);
				return false;
			}

			await GameContext.MapHistoryService.CreateVersionAsync(selectedFolder, MapVersionKind.AfterMapUpgrade, TranslationServer.Translate("After map upgrade"));
			statusLabel.Text = Tr("Upgrade completed successfully!");
			statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.3f));
			await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);
			return true;
		}

		private void ShowInvalidMapFolderErrorDialog(string selectedFolder)
		{
			var (popup, vbox) = UIStyle.CreateModalCard(this, new Vector2(520, 240), new Vector2(480, 210));

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

			var titleLabel = new Label();
			titleLabel.Name = "TitleLabel_Label";
			UIStyle.ApplyTitle(titleLabel, Tr("INVALID MAP FOLDER"), 20);
			titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.3f));
			vbox.AddChild(titleLabel);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

			var descLabel = new Label();
			descLabel.Name = "DescLabel_Label";
			descLabel.Text = $"{string.Format(Tr("Folder: {0}"), Path.GetFileName(selectedFolder))}\n\n{Tr("The selected folder is not a valid map. Required files metadata.json and manifest.json were not found.")}";
			descLabel.HorizontalAlignment = HorizontalAlignment.Center;
			descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			descLabel.CustomMinimumSize = new Vector2(480, 0);
			descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			descLabel.AddThemeFontSizeOverride("font_size", 13);
			descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
			vbox.AddChild(descLabel);

			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

			var buttonRow = new HBoxContainer();
			buttonRow.Name = "ButtonRow_HBoxContainer";
			buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
			vbox.AddChild(buttonRow);

				var closeBtn = UIStyle.CreateCloseButton();
				closeBtn.Visible = true;
				closeBtn.CustomMinimumSize = new Vector2(120, 38);
				buttonRow.AddChild(closeBtn);

			closeBtn.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				popup.QueueFree();
			};
		}

		private void ShowCorruptMapFileErrorDialog(string selectedFolder, string fileName, string errorMsg)
		{
			string msg = $"{string.Format(Tr("Folder: {0}"), Path.GetFileName(selectedFolder))}\n\n{string.Format(Tr("The map file '{0}' is corrupt or contains invalid JSON syntax and failed to parse."), fileName)}\n{string.Format(Tr("Error: {0}"), errorMsg)}";
			GameContext.UserInterface?.ShowPopupMessage("CORRUPT MAP FILE", msg);
		}

		private MapPublishController? _publishController;

		/// <summary>
		/// Publishing lives in MapPublishController; the editor supplies the node, its status surfaces and the steps
		/// that need editor state (sign-in, authorship, naming, signing, the WASM compile and the thumbnail).
		/// </summary>
		private void PublishMapActionView(bool fullExport = false)
		{
			_publishController ??= new MapPublishController(new PublishHooks(
				this, ShowFeedback, AppendWasmConsoleLog, () => _wasmHasErrors, hasErrors => _wasmHasErrors = hasErrors,
				SetWasmConsoleStatus, EnsureSignedInForPublishing,
				(out NSec.Cryptography.Key? key) => TryGetSelectedAuthorshipKey(out key, out _, out _),
				CheckAssetSizeValidation, () => GetMapNameFromMetadata(), () => GetMapVersionFromMetadata(),
				SignMapMetadataAsync, GetPublishAuthorName, path => CompileWorkspaceScriptAsync(path),
				_minimapController == null ? null : path => _minimapController.GenerateAndSaveMinimapThumbnailAsync(path),
				InvalidateMetadataCache,
				ShowGreenlightStatusDialog));
			_publishController.RunAsync(fullExport);
		}







		public async Task ShowAssetIndexRepairModalAsync(Action? onCompleted = null)
		{
			var repairDialog = new AssetIndexRepairDialog();
			repairDialog.Initialize();
			AddChild(repairDialog);

			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			try
			{
				await Task.Run(() => GameContext.AssetIndexService.RebuildIndexFromCas(repairDialog.CreateProgressReporter()));
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] AssetIndex rebuild error: {ex.Message}");
			}

			repairDialog.ShowCompleted();

			await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);

			if (GodotObject.IsInstanceValid(repairDialog))
			{
				repairDialog.QueueFree();
			}

			ShowFeedback(TranslationServer.Translate("Asset index database successfully rebuilt from CAS."));
			onCompleted?.Invoke();
		}


		public void AppendWasmConsoleLog(string line)
		{
			GameContext.WasmConsole.AppendLog(line);
			GameLog.LogToConsole("[WASM_BUILD] " + line);
		}
	}
}
