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

			if (GodotObject.IsInstanceValid(_swatchHighlightPanel) && _swatchHighlightPanel.GetParent() == null)
			{
				_swatchHighlightPanel.QueueFree();
			}

			if (GodotObject.IsInstanceValid(_swatchCliffHighlightPanel) && _swatchCliffHighlightPanel.GetParent() == null)
			{
				_swatchCliffHighlightPanel.QueueFree();
			}

			if (_hudFadeTween != null && _hudFadeTween.IsValid())
			{
				_hudFadeTween.Kill();
			}

			foreach (var (ctrl, filter) in _savedMouseFilters)
			{
				if (GodotObject.IsInstanceValid(ctrl))
				{
					ctrl.MouseFilter = filter;
				}
			}

			_savedMouseFilters.Clear();
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
				UpdateTerrainStatus();
				UpdateInspectorVisibility();
			}

			UpdateControllers();

			_mapNameUpdateTimer += delta;
			if (_mapNameUpdateTimer >= 0.2)
			{
				_mapNameUpdateTimer = 0.0;
				UpdateMapNameHeader();

				if (!ReferenceEquals(_swatchDataService, GameContext.EditorService))
				{
					UpdateTextureLabels();
				}
			}

			UpdateAutoBackup(delta);
			ApplyPendingExternalChanges();
		}

		private void ApplyPendingExternalChanges()
		{
			if (!EditorWorkspaceWatcher.HasPendingExternalChanges) return;
			if (GameContext.Host == null) return;
			if (GameContext.EditorService.IsSyncing || GameContext.EditorService.IsPaused) return;
			if (FloatingDialogBase.HasAnyDialogOpen) return;
			if (Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Right)) return;
			if (GameContext.MapAutosaveService.HasPendingWrite) return;

			string editingDirectory = GetActiveEditingDirectory();
			if (string.IsNullOrEmpty(editingDirectory) || !File.Exists(Path.Combine(editingDirectory, "metadata.json"))) return;
			if (!EditorWorkspaceWatcher.TryTakePendingExternalChanges(out bool metadataChanged, out bool terrainChanged)) return;

			if (metadataChanged)
			{
				ReadMetadataAndRefreshTextures();
				ViewLoadMapProperties();
				UpdateMapNameHeader();
				ShowFeedback(TranslationServer.Translate("metadata.json changed outside the editor — reloaded."));
			}

			if (!terrainChanged) return;

			RecordMapVersion(MapVersionKind.ExternalChange, TranslationServer.Translate("Change made outside the editor"));

			if (!GameContext.Host.EditorHasUnsavedChanges)
			{
				ReloadTerrainFromDisk(editingDirectory);
				return;
			}

			ShowFeedback(TranslationServer.Translate("The terrain files changed outside the editor - your map was left as it is. The other version is in MAP VERSIONS."));
		}

		private void ReloadTerrainFromDisk(string editingDirectory)
		{
			if (GameContext.Host is not { } host) return;

			string terrainPath = Path.Combine(editingDirectory, "terrain.json");
			if (!File.Exists(terrainPath)) return;

			GameContext.EditorService.IsSyncing = true;
			try
			{
				bool loaded = host.LoadMapFromFile(terrainPath, ensureGlbOptimized: false);
				if (loaded)
				{
					host.EditorHasUnsavedChanges = false;
					ShowFeedback(TranslationServer.Translate("The terrain changed outside the editor — reloaded."));
				}
				else
				{
					ShowFeedback(TranslationServer.Translate("The changed terrain files could not be read, so the map in this window was left untouched."));
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] Reloading terrain from disk failed: {ex.Message}");
			}
			finally
			{
				GameContext.EditorService.IsSyncing = false;
			}
		}

		private float _lastStatusX = float.NaN;
		private float _lastStatusY = float.NaN;
		private float _lastStatusZ = float.NaN;
		private EditorTool? _lastActiveTool = null;
		private string? _lastActivePlaceId = null;

		private void UpdateTerrainStatus()
		{
			if (GameContext.Host is not { } host) return;

			var mousePos = GetViewport().GetMousePosition();
			if (host.TryRaycastTerrainFromMousePosition(mousePos, out Vector3 pos))
			{
				float roundX = (float)Math.Round(pos.X, 1);
				float roundY = (float)Math.Round(pos.Y, 1);
				float roundZ = (float)Math.Round(pos.Z, 1);
				EditorTool currentTool = host.ActiveEditorTool;
				string currentPlaceId = host.ActivePlaceId;

				if (roundX != _lastStatusX || roundY != _lastStatusY || roundZ != _lastStatusZ || currentTool != _lastActiveTool || currentPlaceId != _lastActivePlaceId)
				{
					_lastStatusX = roundX;
					_lastStatusY = roundY;
					_lastStatusZ = roundZ;
					_lastActiveTool = currentTool;
					_lastActivePlaceId = currentPlaceId;
					GameContext.EditorService.CheckAndUpdateRaycast(pos);
					_viewModel.StatusText = host.GetTerrainStatusString(pos);
				}
			}
		}

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

		private void ApplyThemeStyles()
		{
			ApplyPanelStyles();
			ApplyAccordionStyles();
			ApplyContentBoxStyles();
			ApplyScrollContainerStyles();
			ApplyActionButtonsStyles();
			ApplyToolButtonsStyles();
			ApplyInspectorButtonsStyles();
			ApplyCheckBoxStyles();
			ApplySubContainerStyles();
			ApplyTopBarStyles();
			ApplyStatusLabelStyles();
		}

		private void ApplyPanelStyles()
		{
			if (_panelLeft != null)
			{
				_panelLeft.MouseFilter = MouseFilterEnum.Ignore;
				_panelLeft.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
			}

			if (_panelRight != null)
			{
				_panelRight.MouseFilter = MouseFilterEnum.Ignore;
				_panelRight.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
			}

			var leftScroll = GetNodeOrNull<ScrollContainer>("LeftSlidePanel/LeftScroll");
			if (leftScroll != null) leftScroll.MouseFilter = MouseFilterEnum.Ignore;

			var rightScroll = GetNodeOrNull<ScrollContainer>("RightSlidePanel/RightScroll");
			if (rightScroll != null) rightScroll.MouseFilter = MouseFilterEnum.Ignore;

			var leftVBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
			if (leftVBox != null)
			{
				leftVBox.MouseFilter = MouseFilterEnum.Ignore;
				leftVBox.AddThemeConstantOverride("separation", 14);
			}

			var rightVBox = GetNodeOrNull<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer");
			if (rightVBox != null)
			{
				rightVBox.MouseFilter = MouseFilterEnum.Ignore;
				rightVBox.AddThemeConstantOverride("separation", 14);
			}
		}

		/// <summary>
		/// The sidebar's cards, in the order they appear. Every card pass walks this one list, so a new card is styled by
		/// all of them instead of being added to each pass by hand.
		/// </summary>
		private MapEditorCard[] Cards() => new[]
		{
			new MapEditorCard(_accordionFile, _filePanel.Content, 300f),
			new MapEditorCard(_viewportPanel.Accordion, _viewportPanel.Content, 0f, false),
			new MapEditorCard(_toolPanel.Accordion, _toolPanel.Content, 320f),
			new MapEditorCard(_brushPanel?.Accordion, _brushPanel?.Content, 300f),
			new MapEditorCard(_waterPanel?.Accordion, _waterPanel?.Content, 200f),
			new MapEditorCard(_accordionToolSettings, _contentToolSettings, 320f),
			new MapEditorCard(_placementPanel.Accordion, _placementPanel.Content, 320f),
			new MapEditorCard(_accordionInspector, _contentInspector, 300f),
		};

		private void ApplyAccordionStyles()
		{
			foreach (var card in Cards()) MapEditorCardTheme.StyleAccordion(card.Accordion);
		}

		private void ApplyContentBoxStyles()
		{
			foreach (var card in Cards()) MapEditorCardTheme.StyleContent(card.Content);
		}

		private void ApplyScrollContainerStyles()
		{
			foreach (var card in Cards()) MapEditorCardTheme.SetupScroll(card.Content, card.ScrollMaxHeight, card.AllowExpand);
		}

		/// <summary>
		/// The two row buttons in the editor's own chrome. The file card's buttons take the same look from
		/// <see cref="FileOperationsPanel.StyleOperations"/>, which runs with the other panel passes.
		/// </summary>
		private void ApplyActionButtonsStyles()
		{
			if (_topToolbar.TestMapButton != null) StyleRowButton(_topToolbar.TestMapButton);
			StyleRowButton(_btnRandomGen);
		}

		private void ApplyToolButtonsStyles() => _toolPanel.StyleButtons();
		private void ApplyInspectorButtonsStyles() => _inspectorView.StyleButtons();
		/// <summary>
		/// The checkboxes the editor itself owns: the terrain flags on the pathing card. Every other checkbox in the
		/// sidebar belongs to a panel that styles its own rows.
		/// </summary>
		private void ApplyCheckBoxStyles()
		{
			StyleCheckBoxRow(_chkShallowWater);
			StyleCheckBoxRow(_chkDeepWater);
			StyleCheckBoxRow(_chkFlying);
			StyleCheckBoxRow(_chkGround);
			StyleCheckBoxRow(_chkBuildable);
		}

		/// <summary>
		/// The plates the editor itself owns, on the tool-settings card. The brush, placement and paste panels plate their
		/// own sections in their own style passes.
		/// </summary>
		private void ApplySubContainerStyles()
		{
			StyleSubContainer(_containerTextureSettings, "Texture Paint Palette");
			StyleSubContainer(_containerPathingSettings, "Pathing Masks");
			StyleSubContainer(_containerPlacementSettings, "Placement Controls");
			StyleSubContainer(_containerEyedropperSettings, "Eyedropper Sample Filter");
			StyleSubContainer(_containerCategorySelector, "Entity Categories");
		}

		private StyleBox CreateTopBarStylebox()
		{
			var posTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_pos.webp");
			if (posTexture != null)
			{
				var posStyle = new StyleBoxTexture();
				posStyle.Texture = posTexture;
				posStyle.TextureMarginLeft = 0;
				posStyle.TextureMarginRight = 0;
				posStyle.TextureMarginTop = 0;
				posStyle.TextureMarginBottom = 0;
				posStyle.ContentMarginLeft = 16;
				posStyle.ContentMarginRight = 16;
				posStyle.ContentMarginTop = 6;
				posStyle.ContentMarginBottom = 6;
				return posStyle;
			}

			var alphaStyle = new StyleBoxFlat();
			alphaStyle.BgColor = new Color(0.12f, 0.14f, 0.18f, 0.75f);
			alphaStyle.BorderColor = UIStyle.ColorGold;
			alphaStyle.SetBorderWidthAll(1);
			alphaStyle.SetCornerRadiusAll(6);
			alphaStyle.ContentMarginLeft = 16;
			alphaStyle.ContentMarginRight = 16;
			alphaStyle.ContentMarginTop = 6;
			alphaStyle.ContentMarginBottom = 6;
			return alphaStyle;
		}

		private void ApplyTopBarStyles()
		{
			var titleLbl = _topBar?.GetNodeOrNull<Label>("HBox/TitleLabel") ?? GetNodeOrNull<Label>("BottomCenterBar/TopBar/HBox/TitleLabel");
			if (titleLbl != null) titleLbl.Visible = false;

			StyleBox barStyle = CreateTopBarStylebox();

			if (_topBar != null)
			{
				_topBar.AddThemeStyleboxOverride("panel", barStyle);
			}

			if (_viewportPanel.MeasurementPanel != null)
			{
				_viewportPanel.MeasurementPanel.AddThemeStyleboxOverride("panel", barStyle);
			}

			var hBox = _topBar?.GetNodeOrNull<HBoxContainer>("HBox") ?? GetNodeOrNull<HBoxContainer>("BottomCenterBar/TopBar/HBox");
			if (hBox != null)
			{
				hBox.Alignment = BoxContainer.AlignmentMode.Center;
			}
		}

		private void ApplyStatusLabelStyles()
		{
			if (_topToolbar.StatusLabel != null)
			{
				_topToolbar.StatusLabel.HorizontalAlignment = HorizontalAlignment.Left;
				_topToolbar.StatusLabel.VerticalAlignment = VerticalAlignment.Center;
				_topToolbar.StatusLabel.AddThemeFontSizeOverride("font_size", 13);
				_topToolbar.StatusLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
				var fontStatus = GetFontAwesomeFont();
				if (fontStatus != null)
				{
					_topToolbar.StatusLabel.AddThemeFontOverride("font", fontStatus);
				}
			}

			if (_viewportPanel.MeasurementLabel != null)
			{
				_viewportPanel.MeasurementLabel.HorizontalAlignment = HorizontalAlignment.Left;
				_viewportPanel.MeasurementLabel.VerticalAlignment = VerticalAlignment.Center;
				_viewportPanel.MeasurementLabel.AddThemeFontSizeOverride("font_size", 13);
				_viewportPanel.MeasurementLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
				var fontStatus = GetFontAwesomeFont();
				if (fontStatus != null)
				{
					_viewportPanel.MeasurementLabel.AddThemeFontOverride("font", fontStatus);
				}
			}

			UIStyle.ApplyTitle(_topToolbar.FeedbackLabel, "", 24);
		}

		public void RefreshCoordinateListExternal()
		{
			_instanceManagerDialog?.RefreshIfOpen();
		}


		private void OpenOverlayModePopup()
		{
			if (_viewportPanel.OverlayPopup == null || GameContext.Host == null || _viewportPanel.GridToggleButton == null) return;

			var mode = GameContext.EditorService.Tools.GridMode;
			bool isGrid = mode == GridOverlayMode.Grid || mode == GridOverlayMode.Both;
			bool isPolar = mode == GridOverlayMode.Polar || mode == GridOverlayMode.Both;
			bool isWireframe = GetViewport()?.DebugDraw == Viewport.DebugDrawEnum.Wireframe;

			SetPopupItemChecked(0, isGrid);
			SetPopupItemChecked(1, isPolar);
			SetPopupItemChecked(2, GameContext.EditorService.Tools.CameraBoundsVisible);
			SetPopupItemChecked(3, isWireframe);

			var globalRect = _viewportPanel.GridToggleButton.GetGlobalRect();
			var popupPos = new Vector2I((int)globalRect.Position.X, (int)(globalRect.Position.Y + globalRect.Size.Y + 2));
			_viewportPanel.OverlayPopup.Position = popupPos;
			_viewportPanel.OverlayPopup.Popup();
		}

		private void SetPopupItemChecked(int itemIndexInList, bool isChecked)
		{
			int idx = _viewportPanel.OverlayPopup.GetItemIndex(itemIndexInList);
			if (idx >= 0) _viewportPanel.OverlayPopup.SetItemChecked(idx, isChecked);
		}

		public void UpdateGridOverlayExternal(GridOverlayMode mode)
		{
			if (_viewportPanel.GridToggleButton != null)
			{
				_viewportPanel.GridToggleButton.Text = UnicodeIcons.BORDER_ALL;
				_viewportPanel.GridToggleButton.TooltipText = TranslationServer.Translate("Overlay");
			}

			if (_viewportPanel.OverlayPopup != null)
			{
				bool isGrid = mode == GridOverlayMode.Grid || mode == GridOverlayMode.Both;
				bool isPolar = mode == GridOverlayMode.Polar || mode == GridOverlayMode.Both;
				int gridIdx = _viewportPanel.OverlayPopup.GetItemIndex(0);
				int polarIdx = _viewportPanel.OverlayPopup.GetItemIndex(1);
				if (gridIdx >= 0) _viewportPanel.OverlayPopup.SetItemChecked(gridIdx, isGrid);
				if (polarIdx >= 0) _viewportPanel.OverlayPopup.SetItemChecked(polarIdx, isPolar);
			}

			UpdatePolarSubControlsVisibility(mode);
		}

		public void UpdatePolarSubControlsVisibility(GridOverlayMode mode)
		{
			MirrorMode mirrorMode = GameContext.Host?.EditorMirrorMode ?? MirrorMode.None;
			bool isPolar = GameContext.EditorService.IsPolarGridActive(mode, mirrorMode);
			bool showResetPivot = isPolar || mirrorMode != MirrorMode.None;

			if (_viewportPanel.RingSpacingOption != null) _viewportPanel.RingSpacingOption.Visible = isPolar;
			if (_viewportPanel.RadialStepOption != null) _viewportPanel.RadialStepOption.Visible = isPolar;
			if (_viewportPanel.ResetPivotButton != null) _viewportPanel.ResetPivotButton.Visible = showResetPivot;
			if (_rowPolarConfig != null)
			{
				_rowPolarConfig.Visible = showResetPivot;
			}
		}

		public void UpdateMeasureTelemetry(float eucTiles, float eucWorld, float manhattanTiles, float dx, float dz, float dy, float angleDeg, float slopePct)
		{
			if (_viewportPanel.MeasurementPanel == null || _viewportPanel.MeasurementLabel == null) return;
			_viewportPanel.MeasurementPanel.Visible = true;

			string slopeStr;
			float absDx = Mathf.Abs(dx);
			float absDz = Mathf.Abs(dz);
			if (absDx < 0.01f && absDz < 0.01f)
			{
				slopeStr = "0:0";
			}
			else if (absDx < 0.01f)
			{
				slopeStr = TranslationServer.Translate("Vertical");
			}
			else if (absDz < 0.01f)
			{
				slopeStr = TranslationServer.Translate("Horizontal (1:0)");
			}
			else
			{
				float ratio = absDz / absDx;
				if (Mathf.Abs(ratio - Mathf.Round(ratio)) < 0.05f)
				{
					slopeStr = $"{Mathf.Round(ratio):0}:1";
				}
				else if (Mathf.Abs(1.0f / ratio - Mathf.Round(1.0f / ratio)) < 0.05f)
				{
					slopeStr = $"1:{Mathf.Round(1.0f / ratio):0}";
				}
				else
				{
					slopeStr = $"{ratio:F1}:1";
				}
			}

			string text = $"{UnicodeIcons.RULER} " + string.Format(
				TranslationServer.Translate("MEASURE: Manhattan: {0:F1}T | Euclidean: {1:F1}T ({2:F1}m) | Angle: {3:F1}° | Slope: {4}"),
				manhattanTiles, eucTiles, eucWorld, angleDeg, slopeStr);

			if (Mathf.Abs(dy) > 0.01f)
			{
				text += string.Format(TranslationServer.Translate(" | Elev: {0:+0.0;-0.0;0.0}m ({1:F1}%)"), dy, slopePct);
			}

			_measureLabelWidth.Apply(_viewportPanel.MeasurementLabel, text);
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
				_pastePanel.Telemetry.Text = string.Format("ΔX: {0:+0;-0;0}, ΔZ: {1:+0;-0;0} | Pivot: {2:F1}T ({3:F1}m) @ {4:F1}°",
					deltaX, deltaZ, distTiles, distWorld, angleDeg);
			}
		}

		public void UpdateCameraBoundsOverlayExternal(bool visible)
		{
			if (_viewportPanel.OverlayPopup != null)
			{
				int camBoundsIdx = _viewportPanel.OverlayPopup.GetItemIndex(2);
				if (camBoundsIdx >= 0) _viewportPanel.OverlayPopup.SetItemChecked(camBoundsIdx, visible);
			}
		}

		public void UpdateWireframeOverlayExternal(bool enabled)
		{
			if (_viewportPanel.OverlayPopup != null)
			{
				int wireframeIdx = _viewportPanel.OverlayPopup.GetItemIndex(3);
				if (wireframeIdx >= 0) _viewportPanel.OverlayPopup.SetItemChecked(wireframeIdx, enabled);
			}
		}

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

			btn.AddThemeStyleboxOverride("normal", UIStyle.CreateOptionButtonNormal());
			btn.AddThemeStyleboxOverride("hover", UIStyle.CreateOptionButtonHover());
			btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateOptionButtonPressed());
			btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
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

		private void HighlightSwatch(Button? selectedSwatch)
		{
			if (!GodotObject.IsInstanceValid(_swatchHighlightPanel))
			{
				_swatchHighlightPanel = new Panel();
				_swatchHighlightPanel.Name = "SwatchHighlightPanel";
				_swatchHighlightPanel.MouseFilter = MouseFilterEnum.Ignore;
				_swatchHighlightPanel.SetAnchorsPreset(LayoutPreset.FullRect);
				_swatchHighlightPanel.GrowHorizontal = GrowDirection.Both;
				_swatchHighlightPanel.GrowVertical = GrowDirection.Both;

				var style = new StyleBoxFlat();
				style.BgColor = new Color(0, 0, 0, 0);
				style.BorderColor = UIStyle.ColorCyanGlow;
				style.SetBorderWidthAll(3);
				style.SetCornerRadiusAll(4);
				_swatchHighlightPanel.AddThemeStyleboxOverride("panel", style);
			}

			if (selectedSwatch != null)
			{
				if (_swatchHighlightPanel.GetParent() != selectedSwatch)
				{
					_swatchHighlightPanel.GetParent()?.RemoveChild(_swatchHighlightPanel);
					selectedSwatch.AddChild(_swatchHighlightPanel);
				}
				_swatchHighlightPanel.Visible = true;
			}
			else
			{
				_swatchHighlightPanel.Visible = false;
			}
		}

		private void HighlightCliffSwatch(Button? selectedSwatch)
		{
			if (!GodotObject.IsInstanceValid(_swatchCliffHighlightPanel))
			{
				_swatchCliffHighlightPanel = new Panel();
				_swatchCliffHighlightPanel.Name = "SwatchCliffHighlightPanel";
				_swatchCliffHighlightPanel.MouseFilter = MouseFilterEnum.Ignore;
				_swatchCliffHighlightPanel.SetAnchorsPreset(LayoutPreset.FullRect);
				_swatchCliffHighlightPanel.GrowHorizontal = GrowDirection.Both;
				_swatchCliffHighlightPanel.GrowVertical = GrowDirection.Both;

				var style = new StyleBoxFlat();
				style.BgColor = new Color(0, 0, 0, 0);
				style.BorderColor = new Color(0.9f, 0.45f, 0.1f);
				style.SetBorderWidthAll(3);
				style.SetCornerRadiusAll(4);
				_swatchCliffHighlightPanel.AddThemeStyleboxOverride("panel", style);
			}

			if (selectedSwatch != null)
			{
				if (_swatchCliffHighlightPanel.GetParent() != selectedSwatch)
				{
					_swatchCliffHighlightPanel.GetParent()?.RemoveChild(_swatchCliffHighlightPanel);
					selectedSwatch.AddChild(_swatchCliffHighlightPanel);
				}
				_swatchCliffHighlightPanel.Visible = true;
			}
			else
			{
				_swatchCliffHighlightPanel.Visible = false;
			}
		}

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

		private void UpdateModuleSwitchButtons()
		{
			if (_topToolbar.ModuleOption != null)
			{
				_topToolbar.ModuleOption.Selected = (int)_activeModule;
			}
		}


		private void SetupAccordion(Button headerBtn, Control contentControl, string titleText)
		{
			string upperTitle = TranslationServer.Translate(titleText).ToString().ToUpperInvariant();
			headerBtn.Text = upperTitle + (contentControl.Visible ? $"  {UnicodeIcons.CARET_DOWN}" : $"  {UnicodeIcons.CARET_RIGHT}");
			var font = GetFontAwesomeFont();
			if (font != null)
			{
				headerBtn.AddThemeFontOverride("font", font);
			}

			StyleSubContainer(contentControl);
		}

		private void StyleRowButton(Button btn) => MapEditorStyle.RowButton(btn);

		private Control? GetContentTarget(Control? contentControl) => MapEditorStyle.ContentTarget(contentControl);

		private void StyleSubContainer(Control boxNode, string? headerText = null) => MapEditorStyle.SubContainer(boxNode, headerText);

		private void StyleCheckBoxRow(CheckBox chk) => MapEditorStyle.CheckBoxRow(chk);

		private void StyleMapEditorTopButton(Button btn)
		{
			if (btn == null) return;
			var tex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_button.webp");
			if (tex != null)
			{
				var normalStyle = new StyleBoxTexture();
				normalStyle.Texture = tex;
				normalStyle.TextureMarginLeft = 0;
				normalStyle.TextureMarginRight = 0;
				normalStyle.TextureMarginTop = 0;
				normalStyle.TextureMarginBottom = 0;
				normalStyle.ContentMarginLeft = 12;
				normalStyle.ContentMarginRight = 12;
				normalStyle.ContentMarginTop = 6;
				normalStyle.ContentMarginBottom = 6;

				var hoverStyle = new StyleBoxTexture();
				hoverStyle.Texture = tex;
				hoverStyle.ModulateColor = new Color(1.25f, 1.2f, 1.0f, 1.0f);
				hoverStyle.TextureMarginLeft = 0;
				hoverStyle.TextureMarginRight = 0;
				hoverStyle.TextureMarginTop = 0;
				hoverStyle.TextureMarginBottom = 0;
				hoverStyle.ContentMarginLeft = 12;
				hoverStyle.ContentMarginRight = 12;
				hoverStyle.ContentMarginTop = 6;
				hoverStyle.ContentMarginBottom = 6;

				var pressedStyle = new StyleBoxTexture();
				pressedStyle.Texture = tex;
				pressedStyle.ModulateColor = new Color(0.85f, 0.8f, 0.7f, 1.0f);
				pressedStyle.TextureMarginLeft = 0;
				pressedStyle.TextureMarginRight = 0;
				pressedStyle.TextureMarginTop = 0;
				pressedStyle.TextureMarginBottom = 0;
				pressedStyle.ContentMarginLeft = 12;
				pressedStyle.ContentMarginRight = 12;
				pressedStyle.ContentMarginTop = 6;
				pressedStyle.ContentMarginBottom = 6;

				btn.AddThemeStyleboxOverride("normal", normalStyle);
				btn.AddThemeStyleboxOverride("hover", hoverStyle);
				btn.AddThemeStyleboxOverride("pressed", pressedStyle);
			}

			btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		}

		private void StyleCompactMapEditorTopButton(Button btn, int horizontalMargin = 4)
		{
			if (btn == null) return;
			var tex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_button.webp");
			if (tex != null)
			{
				var normalStyle = new StyleBoxTexture();
				normalStyle.Texture = tex;
				normalStyle.TextureMarginLeft = 0;
				normalStyle.TextureMarginRight = 0;
				normalStyle.TextureMarginTop = 0;
				normalStyle.TextureMarginBottom = 0;
				normalStyle.ContentMarginLeft = horizontalMargin;
				normalStyle.ContentMarginRight = horizontalMargin;
				normalStyle.ContentMarginTop = 6;
				normalStyle.ContentMarginBottom = 6;

				var hoverStyle = new StyleBoxTexture();
				hoverStyle.Texture = tex;
				hoverStyle.ModulateColor = new Color(1.25f, 1.2f, 1.0f, 1.0f);
				hoverStyle.TextureMarginLeft = 0;
				hoverStyle.TextureMarginRight = 0;
				hoverStyle.TextureMarginTop = 0;
				hoverStyle.TextureMarginBottom = 0;
				hoverStyle.ContentMarginLeft = horizontalMargin;
				hoverStyle.ContentMarginRight = horizontalMargin;
				hoverStyle.ContentMarginTop = 6;
				hoverStyle.ContentMarginBottom = 6;

				var pressedStyle = new StyleBoxTexture();
				pressedStyle.Texture = tex;
				pressedStyle.ModulateColor = new Color(0.85f, 0.8f, 0.7f, 1.0f);
				pressedStyle.TextureMarginLeft = 0;
				pressedStyle.TextureMarginRight = 0;
				pressedStyle.TextureMarginTop = 0;
				pressedStyle.TextureMarginBottom = 0;
				pressedStyle.ContentMarginLeft = horizontalMargin;
				pressedStyle.ContentMarginRight = horizontalMargin;
				pressedStyle.ContentMarginTop = 6;
				pressedStyle.ContentMarginBottom = 6;

				btn.AddThemeStyleboxOverride("normal", normalStyle);
				btn.AddThemeStyleboxOverride("hover", hoverStyle);
				btn.AddThemeStyleboxOverride("pressed", pressedStyle);
			}

			btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		}


		private MapEditorCardInteraction? _cardInteraction;

		/// <summary>Dragging, collapsing and resetting the sidebar cards.</summary>
		internal MapEditorCardInteraction CardInteraction =>
			_cardInteraction ??= new MapEditorCardInteraction(new CardInteractionHooks(this, ShowFeedback));

		private void MakeCardDraggable(Control cardNode, Button headerBtn, Control? contentControl = null, string? titleText = null) =>
			CardInteraction.Register(cardNode, headerBtn, contentControl, titleText);

		private void ExpandAccordion(Button headerBtn, Control contentControl, string titleText) =>
			CardInteraction.Expand(headerBtn, contentControl, titleText);

		public void ResetAllPanelPositions() => CardInteraction.ResetAll();

		private void ToggleLeftPanel()
		{
			SetLeftPanelExpanded(!_leftPanelExpanded);
		}

		private void ToggleRightPanel()
		{
			SetRightPanelExpanded(!_rightPanelExpanded);
		}

		private void SetLeftPanelExpanded(bool expand)
		{
			_leftPanelExpanded = expand;
			var tween = CreateTween();
			float targetLeft = expand ? 0.0f : -260.0f;
			float targetRight = expand ? 260.0f : 0.0f;
			tween.TweenProperty(_panelLeft, "offset_left", targetLeft, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			tween.TweenProperty(_panelLeft, "offset_right", targetRight, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			_btnLeftTab.Text = expand ? "◀" : "▶";
		}

		private void SetRightPanelExpanded(bool expand)
		{
			_rightPanelExpanded = expand;
			var tween = CreateTween();
			float targetLeft = expand ? -300.0f : 0.0f;
			float targetRight = expand ? 0.0f : 300.0f;
			tween.TweenProperty(_panelRight, "offset_left", targetLeft, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			tween.TweenProperty(_panelRight, "offset_right", targetRight, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			_btnRightTab.Text = expand ? "▶" : "◀";
		}


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

		private void SetupIconButton(Button btn, string iconPath, Action onClick, string tooltip = "")
		{
			btn.Flat = false;
			btn.Text = "";
			btn.Icon = null;
			btn.CustomMinimumSize = new Vector2(36, 32);
			btn.FocusMode = FocusModeEnum.None;
			btn.AddThemeConstantOverride("icon_max_width", 0);

			UIStyle.ApplyButtonStyleBoxesClearingFocus(btn);

			foreach (Node child in btn.GetChildren())
			{
				if (child is TextureRect) child.QueueFree();
			}

			var iconRect = new TextureRect();
			iconRect.Name = "IconRect";
			iconRect.Texture = TrimmedTextureLoader.LoadTrimmedTexture(iconPath);
			iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			iconRect.MouseFilter = MouseFilterEnum.Ignore;
			iconRect.AnchorLeft = 0;
			iconRect.AnchorTop = 0;
			iconRect.AnchorRight = 1;
			iconRect.AnchorBottom = 1;
			iconRect.OffsetLeft = 7;
			iconRect.OffsetTop = 5;
			iconRect.OffsetRight = -7;
			iconRect.OffsetBottom = -5;
			iconRect.Modulate = UIStyle.ColorGoldDull;
			btn.AddChild(iconRect);

			btn.MouseEntered += () => iconRect.Modulate = UIStyle.ColorGold;
			btn.MouseExited += () => iconRect.Modulate = UIStyle.ColorGoldDull;
			btn.ButtonDown += () => iconRect.Modulate = UIStyle.ColorCyanGlow;
			btn.ButtonUp += () => iconRect.Modulate = btn.IsHovered() ? UIStyle.ColorGold : UIStyle.ColorGoldDull;

			if (!string.IsNullOrEmpty(tooltip))
			{
				btn.TooltipText = TranslationServer.Translate(tooltip);
			}

			btn.Pressed += () =>
			{
				GameContext.UserInterface?.PlayClickSound();
				onClick?.Invoke();
			};
		}

		private void SetupOptionButton(Button btn, string text, Action? onClick, int fontSize = 11, string tooltip = "")
		{
			if (btn == null) return;
			btn.Text = text;
			var font = GetFontAwesomeFont();
			if (font != null)
			{
				btn.AddThemeFontOverride("font", font);
			}

			btn.CustomMinimumSize = new Vector2(0, 30);
			btn.AddThemeStyleboxOverride("normal", UIStyle.CreateOptionButtonNormal());
			btn.AddThemeStyleboxOverride("hover", UIStyle.CreateOptionButtonHover());
			btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateOptionButtonPressed());
			btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
			btn.AddThemeFontSizeOverride("font_size", fontSize);
			btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.82f));
			btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
			btn.FocusMode = FocusModeEnum.None;
			btn.MouseFilter = MouseFilterEnum.Stop;
			if (!string.IsNullOrEmpty(tooltip))
			{
				btn.TooltipText = TranslationServer.Translate(tooltip);
			}

			if (onClick != null)
			{
				btn.Pressed += () =>
				{
					GameContext.UserInterface?.PlayClickSound();
					onClick?.Invoke();
				};
			}
		}

		private void StyleOptionButtonPopup(OptionButton optBtn) => MapEditorStyle.OptionButtonPopup(optBtn, GetFontAwesomeFont());

		private void StylePopupMenu(PopupMenu popup) => MapEditorStyle.PopupMenuTheme(popup, GetFontAwesomeFont());

		private void StyleOptionButtonsInContainer(Control container) => MapEditorStyle.OptionButtonsInContainer(container, GetFontAwesomeFont());

		public void SetupTextureSwatches(bool connectEvents = false)
		{
			_swatchTextureCache.Clear();

			var slots = RefreshSwatchDataFromWorkspace();
			if (slots != null)
			{
				RebuildSwatchesUI(slots, connectEvents);
			}

			UpdateTextureLabels();
		}

		private EditorService? _swatchDataService { get; set; }

		private void EnsureSwatchDataCurrent()
		{
			var editorService = GameContext.EditorService;
			if (ReferenceEquals(_swatchDataService, editorService)) return;

			RefreshSwatchDataFromWorkspace();
		}

		private SwatchSlotInfo[]? RefreshSwatchDataFromWorkspace()
		{
			_swatchDataService = GameContext.EditorService;

			try
			{
				GameContext.EditorService.Swatches.Clear();

				string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
				var metadata = MapFileService.LoadMetadata(wsPath);
				var slots = TextureSwatchSlots.ResolveSlots(metadata?.Textures, wsPath);

				GameContext.EditorService.Swatches.Populate(slots, wsPath);
				return slots;
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] Error in SetupTextureSwatches: {ex.Message}");
				return null;
			}
		}


		private void RebuildSwatchesUI(SwatchSlotInfo[] slots, bool connectEvents)
		{
			if (_gridSwatches == null) return;

			EnsureScrollSwatchesContainerExists();
			ConfigureGridSwatchesContainer_ViewLogic();
			CreateSwatchButtons_ViewLogic(slots, connectEvents);
		}

		private void EnsureScrollSwatchesContainerExists()
		{
			if (_scrollSwatches == null && _gridSwatches.GetParent() is VBoxContainer parentVBox)
			{
				int gridIndex = _gridSwatches.GetIndex();
				parentVBox.RemoveChild(_gridSwatches);
				_scrollSwatches = new ScrollContainer();
				_scrollSwatches.Name = "ScrollSwatches";
				_scrollSwatches.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
				_scrollSwatches.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
				_scrollSwatches.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				_scrollSwatches.AddChild(_gridSwatches);
				parentVBox.AddChild(_scrollSwatches);
				parentVBox.MoveChild(_scrollSwatches, gridIndex);
			}
		}

		private void ConfigureGridSwatchesContainer_ViewLogic()
		{
			if (_gridSwatches is GridContainer gridSwatchesContainer)
			{
				gridSwatchesContainer.Columns = 6;
			}

			foreach (Node child in _gridSwatches.GetChildren())
			{
				_gridSwatches.RemoveChild(child);
				child.QueueFree();
			}

			_swatchButtons.Clear();
		}

		private void CreateSwatchButtons_ViewLogic(SwatchSlotInfo[] slots, bool connectEvents)
		{
			int visibleCount = 0;
			for (int i = 0; i < TextureSwatchSlots.MaxSlots; i++)
			{
				var slot = slots[i];
				var btn = CreateSwatchButton_ViewLogic(slot);

				if (!slot.IsFiller && !string.IsNullOrEmpty(slot.BaseName))
				{
					btn.Visible = true;
					visibleCount++;
					SetupSwatchVisuals_ViewLogic(btn, slot.SlotIndex);
				}
				else
				{
					btn.Visible = false;
				}

				btn.GuiInput += (@event) => HandleSwatchInput_ViewLogic(@event, slot.SlotIndex, btn);

				_swatchButtons.Add(btn);
				_gridSwatches.AddChild(btn);
			}

			UpdateSwatchesScrollHeight_ViewLogic(visibleCount);
		}

		private Button CreateSwatchButton_ViewLogic(SwatchSlotInfo slot)
		{
			var btn = new Button();
			btn.Name = $"Swatch{slot.SlotIndex + 1}";
			btn.Flat = false;
			btn.ExpandIcon = true;
			btn.FocusMode = FocusModeEnum.None;
			btn.CustomMinimumSize = new Vector2(40, 40);
			btn.Set("icon_max_width", 0);
			UIStyle.ApplyButtonStyleBoxes(btn);
			return btn;
		}

		private void SetupSwatchVisuals_ViewLogic(Button btn, int slotIndex)
		{
			Texture2D? tex = GetSwatchTexture(slotIndex);
			if (tex != null)
			{
				var texRect = new TextureRect();
				texRect.Name = "TexRect_TextureRect";
				texRect.Texture = tex;
				texRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				texRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
				texRect.MouseFilter = MouseFilterEnum.Ignore;
				texRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
				texRect.GrowHorizontal = GrowDirection.Both;
				texRect.GrowVertical = GrowDirection.Both;
				btn.AddChild(texRect);
			}
			else
			{
				var colorBox = new ColorRect();
				colorBox.Name = "ColorBox_ColorRect";
				colorBox.Color = GameContext.EditorService.Swatches.Colors[slotIndex];
				colorBox.MouseFilter = MouseFilterEnum.Ignore;
				colorBox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
				colorBox.GrowHorizontal = GrowDirection.Both;
				colorBox.GrowVertical = GrowDirection.Both;
				btn.AddChild(colorBox);
			}

			btn.TooltipText = $"{slotIndex}: {GameContext.EditorService.Swatches.DisplayNames[slotIndex]}";
		}

		private void HandleSwatchInput_ViewLogic(InputEvent @event, int slotIndex, Button btn)
		{
			if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
			{
				if (mouseEvent.ButtonIndex == MouseButton.Left)
				{
					bool isShift = Input.IsKeyPressed(Godot.Key.Shift);
					bool cliffChecked = ChkApplyCliffTexture != null && ChkApplyCliffTexture.ButtonPressed;
					bool groundUnchecked = ChkApplyGroundTexture == null || !ChkApplyGroundTexture.ButtonPressed;

					if (isShift || (cliffChecked && groundUnchecked))
					{
						SelectCliffTexture(slotIndex);
					}
					else
					{
						SelectTerrainTexture(slotIndex, btn);
					}
				}
				else if (mouseEvent.ButtonIndex == MouseButton.Right)
				{
					SelectCliffTexture(slotIndex);
				}
			}
		}

		private void UpdateSwatchesScrollHeight_ViewLogic(int visibleCount)
		{
			if (_scrollSwatches == null) return;

			const int maxVisibleSwatchesBeforeScroll = 36;
			int totalColumns = 6;
			int numRows = (int)MathF.Ceiling((float)visibleCount / totalColumns);
			if (numRows < 1) numRows = 1;
			int maxRows = maxVisibleSwatchesBeforeScroll / totalColumns;
			int displayedRows = Math.Min(numRows, maxRows);

			float rowHeight = 40f;
			float vSeparation = 6f;
			if (_gridSwatches is GridContainer gc && gc.HasThemeConstantOverride("v_separation"))
			{
				vSeparation = gc.GetThemeConstant("v_separation");
			}

			float targetHeight = displayedRows * rowHeight + Math.Max(0, displayedRows - 1) * vSeparation;

			_scrollSwatches.CustomMinimumSize = new Vector2(0, targetHeight);
			_scrollSwatches.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
			_scrollSwatches.VerticalScrollMode = visibleCount > maxVisibleSwatchesBeforeScroll
				? ScrollContainer.ScrollMode.Auto
				: ScrollContainer.ScrollMode.Disabled;
		}


		public void Set3DInteractionActive_ViewLogic(bool active)
		{
			if (_is3DInteractionActive == active) return;
			_is3DInteractionActive = active;

			if (!EditorSettingsDialog.CurrentSettings.HideHudDuringToolUsage && active)
			{
				return;
			}

			UpdateMouseFilters_ViewLogic(active);
			AnimateHudFade(active);
		}

		private void UpdateMouseFilters_ViewLogic(bool active)
		{
			if (active)
			{
				_savedMouseFilters.Clear();
				ApplyMouseFilterIgnoreRecursive(_panelLeft);
				ApplyMouseFilterIgnoreRecursive(_panelRight);
				ApplyMouseFilterIgnoreRecursive(_topLeftBox);
				if (GodotObject.IsInstanceValid(_topBar))
				{
					ApplyMouseFilterIgnoreRecursive(_topBar);
				}

				foreach (var card in CardInteraction.Cards)
				{
					if (GodotObject.IsInstanceValid(card))
					{
						ApplyMouseFilterIgnoreRecursive(card);
					}
				}
			}
			else
			{
				foreach (var (ctrl, filter) in _savedMouseFilters)
				{
					if (GodotObject.IsInstanceValid(ctrl))
					{
						ctrl.MouseFilter = filter;
					}
				}

				_savedMouseFilters.Clear();
			}
		}

		private void AnimateHudFade(bool active)
		{
			if (_hudFadeTween != null && _hudFadeTween.IsValid())
			{
				_hudFadeTween.Kill();
			}

			_hudFadeTween = CreateTween();
			_hudFadeTween.SetParallel(true);
			float targetAlpha = (active && EditorSettingsDialog.CurrentSettings.HideHudDuringToolUsage) ? 0.0f : 1.0f;
			float duration = 0.35f;

			TweenHudElement(_topLeftBox, targetAlpha, duration);
			TweenHudElement(_panelLeft, targetAlpha, duration);
			TweenHudElement(_panelRight, targetAlpha, duration);
			TweenHudElement(_topBar, targetAlpha, duration);
			TweenHudElement(_viewportPanel.MeasurementPanel, targetAlpha, duration);
			TweenHudElement(_screenFrameRect, targetAlpha, duration);

			foreach (var card in CardInteraction.Cards)
			{
				TweenHudElement(card, targetAlpha, duration);
			}
		}

		private void TweenHudElement(Control element, float targetAlpha, float duration)
		{
			if (GodotObject.IsInstanceValid(element))
			{
				_hudFadeTween.TweenProperty(element, "modulate:a", targetAlpha, duration)
					.SetTrans(Tween.TransitionType.Cubic)
					.SetEase(Tween.EaseType.Out);
			}
		}

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


		public bool CloseCurrentlyOpenDialog()
		{
			if (TryCloseConfirmationOverlay()) return true;
			if (TryCloseNodeOverlay("GenerationOverlay")) return true;
			if (TryCloseScaleMapDialog()) return true;
			if (TryCloseNodeOverlay("PublishInstructionsOverlay")) return true;
			if (TryCloseNodeOverlay("CreatorRegistrationOverlay")) return true;
			if (TryCloseAgreementOverlay()) return true;
			if (TryCloseHelpOverlayPanel()) return true;

			if (FloatingDialogBase.HasAnyDialogOpen)
			{
				return FloatingDialogBase.CloseTopmostDialog();
			}

			return false;
		}

		private bool TryCloseConfirmationOverlay()
		{
			var confirmOverlay = GetNodeOrNull<Control>("ConfirmationOverlay") ?? GameContext.UserInterface?.GetNodeOrNull<Control>("ConfirmationOverlay");
			if (confirmOverlay is { } overlay && IsValidOverlay(overlay))
			{
				if (overlay.HasMeta("CancelAction"))
				{
					var action = overlay.GetMeta("CancelAction").AsCallable();
					action.Call();
				}
				else
				{
					confirmOverlay.QueueFree();
				}

				return true;
			}

			return false;
		}

		private bool TryCloseNodeOverlay(string nodeName)
		{
			var overlay = GetNodeOrNull<Control>(nodeName);
			if (IsValidOverlay(overlay))
			{
				overlay.QueueFree();
				return true;
			}

			return false;
		}

		private bool TryCloseScaleMapDialog()
		{
			if (IsValidOverlay(_scaleMapDialog))
			{
				CloseScaleMapDialog();
				return true;
			}

			return false;
		}

		private bool TryCloseAgreementOverlay()
		{
			var agreementOverlay = GetNodeOrNull<Control>("AgreementOverlay");
			if (IsValidOverlay(agreementOverlay))
			{
				agreementOverlay.QueueFree();
				GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
				return true;
			}

			return false;
		}

		private bool TryCloseHelpOverlayPanel() => HelpPanel.Close();
		private bool IsValidOverlay(Node? overlay)
		{
			return overlay != null && GodotObject.IsInstanceValid(overlay) && overlay.IsInsideTree() && !overlay.IsQueuedForDeletion();
		}

		public override void _Input(InputEvent @event)
		{
			if (@event is not InputEventKey keyEvent || !keyEvent.Pressed)
			{
				return;
			}

			if (HandleEscapeKey(keyEvent)) return;
			if (HandleTabKey(keyEvent)) return;
			if (HandleConsoleKey(keyEvent)) return;
			HandleArrowKeys(keyEvent);
		}

		private bool HandleEscapeKey(InputEventKey keyEvent)
		{
			if (!keyEvent.Echo && keyEvent.Keycode == Godot.Key.Escape)
			{
				if (GameContext.IsSettingsMenuOpen)
				{
					return true;
				}

				if (CloseCurrentlyOpenDialog())
				{
					GetViewport().SetInputAsHandled();
					return true;
				}
			}

			return false;
		}

		private bool HandleTabKey(InputEventKey keyEvent)
		{
			if (keyEvent.Keycode == Godot.Key.Tab)
			{
				if (FloatingDialogBase.HasAnyDialogOpen)
				{
					return true;
				}

				GetViewport().SetInputAsHandled();
				return true;
			}

			return false;
		}

		private bool HandleConsoleKey(InputEventKey keyEvent)
		{
			if (!keyEvent.Echo && keyEvent.Keycode == Godot.Key.Quoteleft)
			{
				if (IsFocusOwnerTextInput()) return true;

				if (WasmConsoleWindow.IsSinglePlayerOrTestMode())
				{
					GameContext.WasmConsole.ToggleVisibility();
					GetViewport().SetInputAsHandled();
					return true;
				}
			}

			return false;
		}

		private void HandleArrowKeys(InputEventKey keyEvent)
		{
			if (keyEvent.Keycode == Godot.Key.Up || keyEvent.Keycode == Godot.Key.Down ||
			    keyEvent.Keycode == Godot.Key.Left || keyEvent.Keycode == Godot.Key.Right)
			{
				if (IsFocusOwnerTextInput()) return;
				GetViewport().SetInputAsHandled();
			}
		}

		private bool IsFocusOwnerTextInput()
		{
			var focusOwner = GetViewport().GuiGetFocusOwner();
			return focusOwner != null && (focusOwner is LineEdit || focusOwner is TextEdit);
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

			UpdateScreenFrameVisibility(prefs);
			UpdatePanelsModulation(prefs);
			UpdateFPSVisibility();

			if (!prefs.HideChromeBorderOverlay)
			{
				ApplyPanelOpacity(prefs.PanelOpacity);
			}

			if (!prefs.HideHudDuringToolUsage && _is3DInteractionActive)
			{
				Set3DInteractionActive_ViewLogic(false);
			}
		}

		private void UpdateScreenFrameVisibility(EditorPreferencesData prefs)
		{
			var screenFrame = GetNodeOrNull<TextureRect>("MapEditorScreenFrame");
			if (screenFrame != null)
			{
				screenFrame.Visible = !prefs.HideChromeBorderOverlay;
			}
		}

		private void UpdatePanelsModulation(EditorPreferencesData prefs)
		{
			var leftPanel = GetNodeOrNull<Panel>("LeftSlidePanel");
			var rightPanel = GetNodeOrNull<Panel>("RightSlidePanel");
			var topBar = GetNodeOrNull<PanelContainer>("BottomCenterBar/TopBar");
			var minimap = GetNodeOrNull<PanelContainer>("MinimapFrame");

			if (prefs.HideChromeBorderOverlay)
			{
				if (leftPanel != null) leftPanel.SelfModulate = new Color(1, 1, 1, 0.45f);
				if (rightPanel != null) rightPanel.SelfModulate = new Color(1, 1, 1, 0.45f);
				if (topBar != null) topBar.SelfModulate = new Color(1, 1, 1, 0.0f);
				if (minimap != null) minimap.SelfModulate = new Color(1, 1, 1, 0.5f);
			}
			else
			{
				if (leftPanel != null) leftPanel.SelfModulate = Colors.White;
				if (rightPanel != null) rightPanel.SelfModulate = Colors.White;
				if (topBar != null) topBar.SelfModulate = Colors.White;
				if (minimap != null) minimap.SelfModulate = Colors.White;
			}
		}

		private void ApplyPanelOpacity(float opacity)
		{
			var leftPanel = GetNodeOrNull<Panel>("LeftSlidePanel");
			var rightPanel = GetNodeOrNull<Panel>("RightSlidePanel");

			if (leftPanel != null) leftPanel.Modulate = new Color(1, 1, 1, opacity);
			if (rightPanel != null) rightPanel.Modulate = new Color(1, 1, 1, opacity);
		}


		public string GetMapNameFromMetadata(bool forceReload = false)
		{
			long now = Environment.TickCount64;
			if (!forceReload && !string.IsNullOrEmpty(GameContext.EditorService.CachedMapName) && (now - GameContext.EditorService.LastMapNameCacheTicks < 2000))
			{
				return GameContext.EditorService.CachedMapName;
			}

			try
			{
				string workspacePath = !string.IsNullOrEmpty(GameContext.EditorService._editingDirectory) ? GameContext.EditorService._editingDirectory : MapWorkspaceService.GetActiveWorkspacePath();

				if (GameContext.EditorService.MapIdentity.TryGetNameFromManifest(workspacePath, out string manifestMapName))
				{
					return GameContext.EditorService.MapIdentity.UpdateCachedName(manifestMapName, now);
				}

				if (GameContext.EditorService.MapIdentity.TryGetNameFromMetadata(workspacePath, out string metadataMapName))
				{
					return GameContext.EditorService.MapIdentity.UpdateCachedName(metadataMapName, now);
				}

				if (GameContext.EditorService.MapIdentity.TryGetNameFromActiveHost(out string activeMapName))
				{
					return GameContext.EditorService.MapIdentity.UpdateCachedName(activeMapName, now);
				}

				return GameContext.EditorService.MapIdentity.UpdateCachedName(TranslationServer.Translate("Unnamed Map"), now);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] Error getting map name: {ex.Message}");
				return GameContext.EditorService.MapIdentity.UpdateCachedName(TranslationServer.Translate("Unnamed Map"), now);
			}
		}


		public void UpdateMapNameHeader()
		{
			if (_topToolbar.MapNameLabel == null) return;
			string mapName = GetMapNameFromMetadata();
			string displayMapName = MapNaming.IsUntitledMapName(mapName)
				? TranslationServer.Translate("Untitled Map")
				: mapName;
			bool hasUnsaved = GameContext.Host?.EditorHasUnsavedChanges ?? false;

			string displayText = hasUnsaved ? $"🗺️ {displayMapName} *" : $"🗺️ {displayMapName}";
			string tooltipText = hasUnsaved
				? $"{displayMapName} * ({TranslationServer.Translate("Unsaved changes — Press Ctrl+S to save")})"
				: $"{displayMapName} ({TranslationServer.Translate("All changes saved")})";

			if (_topToolbar.MapNameLabel.Text != displayText)
			{
				_topToolbar.MapNameLabel.Text = displayText;
			}

			if (_topToolbar.MapNameLabel.TooltipText != tooltipText)
			{
				_topToolbar.MapNameLabel.TooltipText = tooltipText;
			}
		}

		private void SetupLightingTuningUI()
		{
			var leftSlidePanel = GetNodeOrNull<Control>("LeftSlidePanel");
			if (leftSlidePanel != null)
			{
				leftSlidePanel.CustomMinimumSize = new Vector2(325, 0);
				leftSlidePanel.OffsetRight = 325.0f;
			}

			var leftVBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
			if (leftVBox == null) return;

			var lightingAccordion = new VBoxContainer();
			lightingAccordion.Name = "LightingTuningAccordion";

			_btnHeaderLightingTuning = new Button();
			_btnHeaderLightingTuning.Name = "BtnHeaderLightingTuning";
			lightingAccordion.AddChild(_btnHeaderLightingTuning);

			_contentLightingTuning = new VBoxContainer();
			_contentLightingTuning.Name = "ContentLightingTuning";
			lightingAccordion.AddChild(_contentLightingTuning);

			leftVBox.AddChild(lightingAccordion);

			StyleAccordionHeader(_btnHeaderLightingTuning);
			SetupAccordion(_btnHeaderLightingTuning, _contentLightingTuning, "💡 Lighting Tuning (Live Override)");

			CreateSectionHeader(_contentLightingTuning, "--- TERRAIN TEXTURE BLENDING ---");
			_sldHeightBlendSoftness = CreateSliderRow(_contentLightingTuning, "Blend Softness", 0.001f, 0.20f, 0.002f, GameContext.EnvironmentService.TuneHeightBlendSoftness, val =>
			{
				GameContext.EnvironmentService.TuneHeightBlendSoftness = val;
				ApplyLiveLightingTuning();
			}, "0.00#");
			_sldBlendNoiseStrength = CreateSliderRow(_contentLightingTuning, "Blend Noise Str", 0f, 1f, 0.02f, GameContext.EnvironmentService.TuneBlendNoiseStrength, val =>
			{
				GameContext.EnvironmentService.TuneBlendNoiseStrength = val;
				ApplyLiveLightingTuning();
			});
			_sldBlendNoiseScale = CreateSliderRow(_contentLightingTuning, "Blend Noise Scl", 0.01f, 0.5f, 0.005f, GameContext.EnvironmentService.TuneBlendNoiseScale, val =>
			{
				GameContext.EnvironmentService.TuneBlendNoiseScale = val;
				ApplyLiveLightingTuning();
			}, "0.00#");

			lightingAccordion.Visible = false;
		}

		private Label CreateSectionHeader(VBoxContainer parent, string titleText)
		{
			var lbl = new Label();
			lbl.Name = "Lbl_Label";
			lbl.Text = titleText;
			lbl.AddThemeFontSizeOverride("font_size", 11);
			lbl.Modulate = new Color(0.95f, 0.85f, 0.35f);
			parent.AddChild(lbl);
			return lbl;
		}
	}
}