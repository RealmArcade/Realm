using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using Environment = System.Environment;
using Key = NSec.Cryptography.Key;
using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;
using static Godot.Control;
using Realm.Client.Utils;

namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		public override void _ExitTree()
		{
			try
			{
				GameContext.MapAutosaveService.FlushPendingWrites(TimeSpan.FromSeconds(3));
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[MapEditorHUD] Could not finish the pending autosave on exit: {exception.Message}");
			}

			GameContext.MetadataService.MetadataSaved -= OnMetadataSaved;
			EditorWorkspaceWatcher.Stop();
			CloseWasmConsoleModal();
			GameContext.UnregisterMapEditorHud(this);

			if (_swatchGrid != null) _swatchGrid.ReleaseOrphanedHighlights();

			_hudFade?.Release();
		}

		public void UpdateFPSVisibility()
		{
			var fpsLabel = (GameContext.Host != null ? GameContext.Host.MainNode?.GetNodeOrNull<Label>("CanvasLayer/FPS") : null);
			if (fpsLabel != null)
			{
				fpsLabel.Visible = GameSettings.DisplayFps;
				fpsLabel.ZIndex = 100;
			}
		}


		public override void _Process(double delta)
		{
			if (GameContext.Host != null)
			{
				_viewModel.UpdateFromHost();
				TerrainStatus.Update();
				UpdateInspectorVisibility();
			}

			UpdateControllers();

			_mapNameUpdateTimer += delta;
			if (_mapNameUpdateTimer >= 0.2)
			{
				_mapNameUpdateTimer = 0.0;
				UpdateMapNameHeader();

				if (Swatches.IsDataStale)
				{
					UpdateTextureLabels();
				}
			}

			UpdateAutoBackup(delta);
			ApplyPendingExternalChanges();
		}

		private MapEditorWorkspaceReload? _workspaceReload;

		private MapEditorWorkspaceReload WorkspaceReload => _workspaceReload ??= new MapEditorWorkspaceReload(
			new WorkspaceReloadHooks(GetActiveEditingDirectory,
				() => GameContext.Host?.EditorHasUnsavedChanges ?? false,
				() =>
				{
					ReadMetadataAndRefreshTextures();
					ViewLoadMapProperties();
					UpdateMapNameHeader();
				},
				() => RecordMapVersion(MapVersionKind.ExternalChange, TranslationServer.Translate("Change made outside the editor")),
				ShowFeedback));

		private MapEditorTerrainStatus? _terrainStatus;

		private MapEditorTerrainStatus TerrainStatus => _terrainStatus ??= new MapEditorTerrainStatus(
			this, text => _viewModel.StatusText = text);

		private void ApplyPendingExternalChanges() => WorkspaceReload.ApplyPending();

		private void UpdateInspectorVisibility()
		{
			if (GameContext.Host is not { } host) return;

			bool hasSelectedObject = GodotObject.IsInstanceValid(host.SelectedEditorObject);
			bool shouldShowInspector = hasSelectedObject && (host.ActiveEditorTool == EditorTool.SelectMove);
			if (_accordionInspector != null && _accordionInspector.Visible != shouldShowInspector)
			{
				_accordionInspector.Visible = shouldShowInspector;
				if (shouldShowInspector)
				{
					if (_contentInspector != null) _contentInspector.Visible = true;
					if (_btnHeaderInspector != null) _btnHeaderInspector.Text = "▼ " + TranslationServer.Translate("Selected Object Inspector");
				}

				_accordionContainer?.QueueSort();
			}
		}

		private void UpdateControllers()
		{
			_topBarController?.Update(_viewModel);
			if (!_isDraggingSlider)
			{
				_brushSettingsController?.Update(_viewModel);
				_placementSettingsController?.Update(_viewModel);
				UpdatePasteRotationExternal(_viewModel.PasteRotation);
				UpdatePasteReflectionExternal(_viewModel.PasteReflection);
			}

			_inspectorController?.Update(_viewModel);
			_pathingPanelController?.Update(_viewModel);
			_minimapController?.Update(_viewModel);
		}

		private void UpdateAutoBackup(double delta)
		{
			if (GameContext.EditorService.CheckAutoBackup(MapHistoryPolicy.AutosaveIntervalMinutes, delta))
			{
				PerformAutoBackup();
			}
		}

		public void ShowFeedbackExternal(string text)
		{
			ShowFeedback(text);
		}

		public void ShowFeedback(string text)
		{
			_topBarController?.ShowFeedback(text);
		}

		public byte GetSelectedWaterProfileIndex()
		{
			if (_waterPanel?.ModeOption == null || _waterPanel.ModeOption.Selected < 0) return 0;
			var meta = _waterPanel.ModeOption.GetItemMetadata(_waterPanel.ModeOption.Selected);
			if (meta.VariantType == Variant.Type.Nil) return 0;
			return (byte)(int)meta;
		}

		public WaterType GetSelectedWaterMode()
		{
			if (_waterPanel?.ModeOption == null || _waterPanel.ModeOption.Selected < 0) return WaterType.None;
			return WaterType.Shallow;
		}


		public void RefreshCoordinateListExternal()
		{
			_instanceManagerDialog?.RefreshIfOpen();
		}


		private MapEditorOverlayMenu? _overlayMenu;

		private MapEditorOverlayMenu OverlayMenu => _overlayMenu ??= new MapEditorOverlayMenu(
			new OverlayMenuHooks(this, () => _viewportPanel, () => _rowPolarConfig));

		private void OpenOverlayModePopup() => OverlayMenu.Open();

		public void UpdateGridOverlayExternal(GridOverlayMode mode) => OverlayMenu.UpdateExternal(mode);

		public void UpdatePolarSubControlsVisibility(GridOverlayMode mode) => OverlayMenu.UpdatePolarSubControls(mode);

		public void UpdateMeasureTelemetry(float eucTiles, float eucWorld, float manhattanTiles, float dx, float dz, float dy, float angleDeg, float slopePct)
		{
			if (_viewportPanel.MeasurementPanel == null || _viewportPanel.MeasurementLabel == null) return;
			_viewportPanel.MeasurementPanel.Visible = true;

			_measureLabelWidth.Apply(_viewportPanel.MeasurementLabel,
				MapEditorTelemetryText.Measure(eucTiles, eucWorld, manhattanTiles, dx, dz, dy, angleDeg, slopePct));
		}

		public void ClearMeasureTelemetry()
		{
			if (_viewportPanel.MeasurementPanel != null)
			{
				_viewportPanel.MeasurementPanel.Visible = false;
			}

			_measureLabelWidth.Reset(_viewportPanel.MeasurementLabel);
		}

		public void UpdatePasteTelemetry(int deltaX, int deltaZ, float distTiles, float distWorld, float angleDeg)
		{
			if (_pastePanel?.Telemetry != null)
			{
				_pastePanel.Telemetry.Visible = true;
				_pastePanel.Telemetry.Text = MapEditorTelemetryText.Paste(deltaX, deltaZ, distTiles, distWorld, angleDeg);
			}
		}

		public void UpdateCameraBoundsOverlayExternal(bool visible) => OverlayMenu.SetCameraBoundsChecked(visible);

		public void UpdateWireframeOverlayExternal(bool enabled) => OverlayMenu.SetWireframeChecked(enabled);

		private void StylePopupButton(Button btn, string text, string tooltip)
		{
			btn.Text = TranslationServer.Translate(text);
			btn.TooltipText = TranslationServer.Translate(tooltip);
			btn.FocusMode = FocusModeEnum.None;
			btn.CustomMinimumSize = new Vector2(0, 24);
			btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btn.AddThemeFontSizeOverride("font_size", 11);
			var font = GetFontAwesomeFont();
			if (font != null)
			{
				btn.AddThemeFontOverride("font", font);
			}

			UIStyle.ApplyStyleBoxStates(btn,
				UIStyle.CreateOptionButtonNormal(),
				UIStyle.CreateOptionButtonHover(),
				UIStyle.CreateOptionButtonPressed());
			btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.82f));
			btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
			btn.Alignment = HorizontalAlignment.Left;
		}

		private void OpenCameraPopup()
		{
			if (_cameraPanel.Popup == null || GameContext.Host == null || _cameraPanel.ToggleButton == null) return;

			UpdateCameraPopupControls();

			var globalRect = _cameraPanel.ToggleButton.GetGlobalRect();
			var popupPos = new Vector2I((int)globalRect.Position.X, (int)(globalRect.Position.Y + globalRect.Size.Y + 2));
			_cameraPanel.Popup.Position = popupPos;
			_cameraPanel.Popup.Popup();
		}

		public void UpdateCameraPopupControls()
		{
			if (GameContext.Host == null) return;
			var camera = GameContext.Host.MainCamera;
			if (camera != null)
			{
				UpdateCameraAngleButtonText(camera.IsTopDown());
				UpdateFreeCameraExternal(camera.IsFreeCamera);
			}
		}

		public void UpdateShadowsExternal(bool disabled)
		{
			if (_viewportPanel.ShadowsCheck != null)
			{
				_viewportPanel.ShadowsCheck.SetPressedNoSignal(!disabled);
			}
		}

		private void SetupEnvWeatherDropdown(OptionButton opt)
		{
			opt.Clear();
			opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.CLOUD} Clear"), 0);
			opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.CLOUD_SHOWERS} Rain"), 1);
			opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.SNOWFLAKE} Snow"), 2);
			opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.SMOG} Fog"), 3);
			StyleOptionButtonPopup(opt);
		}

		private void OpenEnvironmentPopup()
		{
			if (_viewportPanel.EnvironmentPopup == null || GameContext.Host == null || _viewportPanel.EnvironmentToggleButton == null) return;

			UpdateEnvironmentPopupControls();

			var globalRect = _viewportPanel.EnvironmentToggleButton.GetGlobalRect();
			var popupPos = new Vector2I((int)globalRect.Position.X, (int)(globalRect.Position.Y + globalRect.Size.Y + 2));
			_viewportPanel.EnvironmentPopup.Position = popupPos;
			_viewportPanel.EnvironmentPopup.Popup();
		}

		public void UpdateEnvironmentPopupControls()
		{
			if (GameContext.Host == null) return;

			if (_viewportPanel.LightingOption != null)
			{
				int timeIdx = GameContext.Host.TimeOfDayIndex;
				_viewportPanel.LightingOption.Select(Mathf.Clamp(timeIdx, 0, 3));
			}

			if (_viewportPanel.WeatherOption != null && GameContext.EnvironmentService != null)
			{
				string curWeather = GameContext.EnvironmentService.GetCurrentWeather();
				int weatherIdx = (curWeather?.ToLowerInvariant()) switch
				{
					"rain" => 1,
					"snow" => 2,
					"fog" => 3,
					_ => 0
				};
				_viewportPanel.WeatherOption.Select(weatherIdx);
			}

			if (_viewportPanel.ShadowsCheck != null)
			{
				_viewportPanel.ShadowsCheck.SetPressedNoSignal(!GameContext.EditorService.Tools.DisableShadows);
			}
		}

		public void UpdateEnvWeatherSelection(string weatherType)
		{
			if (_viewportPanel.WeatherOption != null)
			{
				int idx = (weatherType?.ToLowerInvariant()) switch
				{
					"rain" => 1,
					"snow" => 2,
					"fog" => 3,
					_ => 0
				};
				_viewportPanel.WeatherOption.Select(idx);
			}
		}

		public void EnsureCameraBoundsVisible()
		{
			if (GameContext.Host != null && !GameContext.EditorService.Tools.CameraBoundsVisible)
			{
				GameContext.EditorService.Tools.CameraBoundsVisible = true;
				GameContext.Host.UpdateCameraBoundsOverlayVisibility();
				UpdateCameraBoundsOverlayExternal(true);
			}
		}

		public void BackToHubAction()
		{
			if (HasUnsavedChanges())
			{
				ShowConfirmationDialog(
					TranslationServer.Translate("You haven't saved yet"),
					onConfirm: () =>
					{
						GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
					},
					confirmText: TranslationServer.Translate("Quit"),
					cancelText: TranslationServer.Translate("Stay")
				);
			}
			else
			{
				GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
			}
		}

		public void HandleQuitRequest()
		{
			if (HasUnsavedChanges())
			{
				ShowConfirmationDialog(
					TranslationServer.Translate("You haven't saved yet"),
					onConfirm: () =>
					{
						GetTree().Quit();
					},
					confirmText: TranslationServer.Translate("Quit"),
					cancelText: TranslationServer.Translate("Stay")
				);
			}
			else
			{
				GetTree().Quit();
			}
		}

		public void SelectPickedUnitOrProp(string id, bool isBuilding)
		{
			if (isBuilding)
			{
				_entityPaletteController?.SelectCategoryItemExternal("Buildings", id);
			}
			else
			{
				_entityPaletteController?.SelectCategoryItemExternal("Units", id);
			}
		}

		public void RefreshEntityPalette()
		{
			_entityPaletteController?.RefreshCurrentCategoryItems();
		}

		public void UpdateRotationExternal(float angle)
		{
			if (!GameContext.EditorService.Placement.SetLastRotation(angle)) return;
			if (_placementPanel?.RotateValue != null) _placementPanel.RotateValue.Text = angle.ToString("F0") + "°";
			if (_placementPanel?.RotateSlider != null && !Mathf.IsEqualApprox((float)_placementPanel.RotateSlider.Value, angle)) _placementPanel.RotateSlider.Value = angle;
		}

		public void UpdatePasteRotationExternal(float angle)
		{
			if (!GameContext.EditorService.Placement.SetLastPasteRotation(angle)) return;
			if (_pastePanel?.RotationLabel != null) _pastePanel.RotationLabel.Text = angle.ToString("F0") + "°";
			if (_pastePanel?.RotationSlider != null && !Mathf.IsEqualApprox((float)_pastePanel.RotationSlider.Value, angle)) _pastePanel.RotationSlider.Value = angle;
		}

		public void UpdateScaleExternal(float scale)
		{
			if (!GameContext.EditorService.Placement.SetLastScale(scale)) return;
			if (_placementPanel?.ScaleValue != null) _placementPanel.ScaleValue.Text = scale.ToString("F1") + "x";
			if (_placementPanel?.ScaleSlider != null && !Mathf.IsEqualApprox((float)_placementPanel.ScaleSlider.Value, scale)) _placementPanel.ScaleSlider.Value = scale;
		}

		public void UpdateBrushSizeExternal(float size)
		{
			if (!GameContext.EditorService.Placement.SetLastBrushSize(size)) return;
			if (_brushPanel?.SizeSlider != null && !Mathf.IsEqualApprox((float)_brushPanel.SizeSlider.Value, size))
			{
				_brushPanel.SizeSlider.Value = Mathf.Round(size);
			}

			if (_brushPanel?.SizeValue != null)
			{
				_brushPanel.SizeValue.Text = Mathf.Round(size).ToString("F0");
			}
		}

		public void UpdateBlockLevelHeightExternal(float step)
		{
			if (_brushPanel?.StepSlider != null)
			{
				_brushPanel.StepSlider.Value = step;
			}

			if (_brushPanel?.StepValue != null)
			{
				_brushPanel.StepValue.Text = step.ToString("F1") + " m";
			}
		}

		public void UpdateExactHeightExternal(float height)
		{
			if (_brushPanel?.HeightSlider != null)
			{
				_brushPanel.HeightSlider.Value = height;
			}

			if (_brushPanel?.HeightValue != null)
			{
				_brushPanel.HeightValue.Text = height.ToString("F1") + "m";
			}
		}

		private TextureSwatchGrid? _swatchGrid;

		/// <summary>The texture palette: its buttons, both selection overlays and the thumbnail cache.</summary>
		public TextureSwatchGrid Swatches => _swatchGrid ??= new TextureSwatchGrid(
			new SwatchGridHooks(
				SelectTerrainTexture,
				SelectCliffTexture,
				() => ChkApplyCliffTexture != null && ChkApplyCliffTexture.ButtonPressed,
				() => ChkApplyGroundTexture != null && ChkApplyGroundTexture.ButtonPressed,
				LoadSwatchTextureInternal,
				UpdateTextureLabels));

		private void HighlightSwatch(Button? selectedSwatch) => Swatches.Highlight(selectedSwatch);

		private void HighlightCliffSwatch(Button? selectedSwatch) => Swatches.HighlightCliff(selectedSwatch);

		private void CheckPostLaunchPrompts()
		{
			if (GameContext.AssetIndexService.IsIndexVersionMismatch())
			{
				_ = ShowAssetIndexRepairModalAsync(() => EstablishLaunchSavedState());
				return;
			}

			var verification = GameContext.AssetIndexService.VerifyIndex();
			if (verification.IsHealthy)
			{
				EstablishLaunchSavedState();
				return;
			}

			_ = RepairFailedAssetIndexEntriesAsync(verification, () => EstablishLaunchSavedState());
		}

		private async Task RepairFailedAssetIndexEntriesAsync(AssetIndexVerificationResult verification, Action onCompleted)
		{
			int repairedCount = 0;
			try
			{
				repairedCount = await Task.Run(() => GameContext.AssetIndexService.RepairIndexedAssets(verification.Failures));
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] Asset index sample repair error: {ex.Message}");
			}

			if (repairedCount > 0)
			{
				GameLog.LogToConsole($"[MapEditorHUD] Repaired {repairedCount} asset index record(s) without rebuilding the whole index.");
				ShowFeedback(string.Format(TranslationServer.Translate("Repaired {0} asset index record(s)."), repairedCount));
			}

			onCompleted?.Invoke();
		}

		private void ShowMapNewerThanGameErrorDialog(string selectedFolder, string? mapVersion, string gameVersion)
		{
			string msg = $"{string.Format(Tr("Map: {0}"), Path.GetFileName(selectedFolder))}\n{string.Format(Tr("Map Build: {0} | Editor Build: {1}"), mapVersion, gameVersion)}\n\n{Tr("This map was created with a newer version of the game. Downgrading maps is not supported.\nPlease update the game to open this map.")}";
			GameContext.UserInterface?.ShowPopupMessage("MAP REQUIRES NEWER GAME VERSION", msg);
		}

		private void ShowGreenlightStatusDialog(string mapTitle, string mapVersion, int verifiedGoodReviews, int totalReviews, double averageRating, bool isGreenlit)
		{
			GreenlightStatusDialog.Show(this, mapTitle, mapVersion, verifiedGoodReviews, totalReviews, averageRating, isGreenlit);
		}






		private EditorConfirmationDialog? _confirmDialog;

		private EditorConfirmationDialog ConfirmDialog => _confirmDialog ??= new EditorConfirmationDialog(
			new EditorConfirmationDialogHooks(this, (button, text, onClick, fontSize, tooltip) => SetupButton(button, text, onClick, fontSize, tooltip)));

		public void ShowConfirmationDialog(string message, Action? onConfirm, string confirmText = "YES", string cancelText = "NO", Action? onCancel = null, Action? onDismissed = null, bool showCancel = true)
		{
			ConfirmDialog.Show(message, onConfirm, confirmText, cancelText, onCancel, onDismissed, showCancel);
		}





		private MapNamePromptDialog? _mapNamePrompt;

		private MapNamePromptDialog MapNamePrompt => _mapNamePrompt ??= new MapNamePromptDialog(
			new MapNamePromptHooks(this,
				(button, text, onClick, fontSize, tooltip) => SetupButton(button, text, onClick, fontSize, tooltip),
				ShowFeedback));

		public void ShowMapNamePrompt(Action<string> onNamed) => MapNamePrompt.Show(onNamed);

		private void ShowWasmConsoleModal()
		{
			GameContext.WasmConsole.ClearLogs();
			GameContext.WasmConsole.ShowConsole();
		}

		public void SetWasmConsoleStatus(string statusText, Color color)
		{
			GameContext.WasmConsole.SetStatus(statusText, color);
		}

		public void CloseWasmConsoleModal()
		{
			if (GameContext.HasWasmConsole)
			{
				GameContext.WasmConsole.Hide();
			}
		}


		private MapEditorHelpPanel? _helpPanel;

		private MapEditorHelpPanel HelpPanel => _helpPanel ??= new MapEditorHelpPanel(
			new HelpPanelHooks(this, (button, text, onClick, fontSize) => SetupButton(button, text, onClick, fontSize)));

		public void ToggleHelpPanelExternal() => HelpPanel.Toggle();

		private CreatorRegistrationDialog? _creatorRegistrationDialog;

		private CreatorRegistrationDialog CreatorRegistration => _creatorRegistrationDialog ??= new CreatorRegistrationDialog(
			new CreatorRegistrationHooks(this,
				(button, text, onClick, fontSize, tooltip) => SetupOptionButton(button, text, onClick, fontSize, tooltip),
				ShowFeedback));

		private void CheckCreatorRegistrationAndPrompt(Key key) => CreatorRegistration.CheckAsync(key);

		public void UpdateMirrorButtonText() => UpdateMirrorDropdowns();

		private void SetupSpokesDropdown(OptionButton opt, Action<int> onSpokesSelected, string tooltip = "")
		{
			if (opt == null) return;
			opt.Clear();
			opt.Set("icon_max_width", 0);
			opt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			opt.ClipText = true;

			for (int i = 0; i < EditorToolCatalog.SpokeCountOptions.Length; i++)
			{
				int count = EditorToolCatalog.SpokeCountOptions[i];
				opt.AddItem($"{UnicodeIcons.COMPASS} " + string.Format(TranslationServer.Translate("SPOKES: {0}"), count), count);
			}

			if (!string.IsNullOrEmpty(tooltip))
			{
				opt.TooltipText = tooltip;
			}

			StyleOptionButtonPopup(opt);

			opt.ItemSelected += (long idx) =>
			{
				int spokeCount = opt.GetItemId((int)idx);
				onSpokesSelected?.Invoke(spokeCount);
			};
		}

		public void UpdateSymmetryFoldsButtonText() => UpdateSymmetrySpokesDropdowns();

		public void UpdatePolarRingSpacingButtonText() => _viewportPanel.RefreshRingSpacingSelection();

		public void UpdatePolarRadialStepButtonText() => _viewportPanel.RefreshRadialStepSelection();



		private void SetupMinimap()
		{
			MinimapHelper.SetupMinimapBackground(_viewportPanel.MinimapArea);
		}

		public void RegenerateMinimap()
		{
			_minimapController?.RegenerateMinimap();
		}

		public void SetScaleDialogTargets(int width, int depth) => ScaleDialog.SetTargets(width, depth);

		private void HookSliders(Node parent)
		{
			if (parent == null) return;
			if (parent is Slider slider)
			{
				if (_hookedSliderInstanceIds.Add(slider.GetInstanceId()))
				{
					slider.DragStarted += () => IsDraggingSlider = true;
					slider.DragEnded += (_) => IsDraggingSlider = false;
				}
			}

			int childCount = parent.GetChildCount();
			for (int i = 0; i < childCount; i++)
			{
				HookSliders(parent.GetChild(i));
			}
		}

		private void SetupIconButton(Button btn, string iconPath, Action onClick, string tooltip = "") =>
			MapEditorStyle.IconButtonFromPath(btn, iconPath, onClick, tooltip);

		private void SetupOptionButton(Button btn, string text, Action? onClick, int fontSize = 11, string tooltip = "") =>
			MapEditorStyle.OptionButton(btn, text, onClick, fontSize, tooltip, GetFontAwesomeFont());

		private void StyleOptionButtonPopup(OptionButton optBtn) => MapEditorStyle.OptionButtonPopup(optBtn, GetFontAwesomeFont());

		private void StylePopupMenu(PopupMenu popup) => MapEditorStyle.PopupMenuTheme(popup, GetFontAwesomeFont());

		private void StyleOptionButtonsInContainer(Control container) => MapEditorStyle.OptionButtonsInContainer(container, GetFontAwesomeFont());

		public void SetupTextureSwatches(bool connectEvents = false) => Swatches.Setup(connectEvents);

		private void EnsureSwatchDataCurrent()
		{
			if (Swatches.IsDataStale) Swatches.RefreshData();
		}


		public MapEditorHudFade HudFade => _hudFade ??= new MapEditorHudFade(
			new HudFadeHooks(this, _topLeftBox, _panelLeft, _panelRight, _topBar, _viewportPanel.MeasurementPanel,
				_screenFrameRect, CardInteraction.Cards, () => EditorSettingsDialog.CurrentSettings.HideHudDuringToolUsage));

		public void Set3DInteractionActive_ViewLogic(bool active) => HudFade.SetInteractionActive(active);

		private void UpdateBlockStepVisibility()
		{
			bool blockModeEnabled = (_brushPanel?.BlockModeCheck != null && _brushPanel.BlockModeCheck.Visible && _brushPanel.BlockModeCheck.ButtonPressed);
			var tool = GameContext.Host != null ? GameContext.Host.ActiveEditorTool : EditorTool.Lower;

			if (_brushPanel?.StepBox != null)
			{
				_brushPanel.StepBox.Visible = blockModeEnabled && (tool == EditorTool.Raise || tool == EditorTool.Lower);
			}

			if (_brushPanel?.HeightBox != null)
			{
				_brushPanel.HeightBox.Visible = (tool == EditorTool.Height);
			}
		}

		private AgreementDialog? _agreementDialog;

		private AgreementDialog Agreement => _agreementDialog ??= new AgreementDialog(
			new AgreementDialogHooks(this,
				(button, text, onClick, fontSize, tooltip) => SetupOptionButton(button, text, onClick, fontSize, tooltip),
				shown => _agreementShownThisSession = shown));

		private void ShowAgreementModal(Action? onAccepted = null) => Agreement.Show(onAccepted);


		public void OpenAbilityVfxDialog(string abilityId, JsonObject? abilityData, Action<JsonObject>? onApplied = null)
		{
			if (_abilityVfxDialog == null)
			{
				_abilityVfxDialog = new AbilityVfxDialog(this);
			}

			_abilityVfxDialog.OpenForAbility(abilityId, abilityData, onApplied);
		}


		private MapEditorOverlayDismissal? _overlayDismissal;

		private MapEditorOverlayDismissal OverlayDismissal => _overlayDismissal ??= new MapEditorOverlayDismissal(
			new OverlayDismissalHooks(this, _scaleMapDialog, CloseScaleMapDialog, () => HelpPanel.Close()));

		private MapEditorKeyboardShortcuts? _keyboardShortcuts;

		private MapEditorKeyboardShortcuts KeyboardShortcuts => _keyboardShortcuts ??= new MapEditorKeyboardShortcuts(
			new KeyboardShortcutHooks(this, CloseCurrentlyOpenDialog));

		public bool CloseCurrentlyOpenDialog() => OverlayDismissal.CloseTopmost();

		public override void _Input(InputEvent @event)
		{
			if (@event is not InputEventKey keyEvent || !keyEvent.Pressed) return;

			KeyboardShortcuts.Handle(keyEvent);
		}

		public void OpenInstanceManagerDialog(string? initialTypeFilter = null)
		{
			if (_instanceManagerDialog == null)
			{
				_instanceManagerDialog = new InstanceManagerDialog(this);
			}

			_instanceManagerDialog.OpenDialog(initialTypeFilter);
		}

		public void ApplyEditorPreferences(EditorPreferencesData prefs)
		{
			if (prefs == null) return;

			MapEditorChromeAppearance.Apply(this, prefs);
			UpdateFPSVisibility();

			if (!prefs.HideHudDuringToolUsage && HudFade.IsActive)
			{
				Set3DInteractionActive_ViewLogic(false);
			}
		}

		public string GetMapNameFromMetadata(bool forceReload = false) => MapEditorMapTitle.Resolve(forceReload);

		public void UpdateMapNameHeader() => MapEditorMapTitle.UpdateHeader(_topToolbar.MapNameLabel);

		private MapEditorLightingTuningPanel? _lightingTuning;

		private MapEditorLightingTuningPanel LightingTuning => _lightingTuning ??= new MapEditorLightingTuningPanel();

		private void SetupLightingTuningUI() => LightingTuning.Build(new LightingTuningHooks(this,
			StyleAccordionHeader, SetupAccordion, dragging => _isDraggingSlider = dragging, ApplyLiveLightingTuning));
	}
}
