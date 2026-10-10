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

        public override void _ExitTree()
        {
            MetadataService.Instance.MetadataSaved -= OnMetadataSaved;
            _editorService?.StopWorkspaceWatcher();
            CloseWasmConsoleModal();
            if (Instance == this)
            {
                Instance = null;
            }
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
            var fpsLabel = (Realm.Client.Core.GameHost.Instance != null ? Realm.Client.Core.GameHost.Instance.MainNode?.GetNodeOrNull<Label>("CanvasLayer/FPS") : null);
            if (fpsLabel != null)
            {
                fpsLabel.Visible = GameSettings.DisplayFps;
                fpsLabel.ZIndex = 100;
            }
        }


        public override void _Process(double delta)
        {
            if (Realm.Client.Core.GameHost.Instance != null)
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
            }

            UpdateAutoBackup(delta);
        }

        private void UpdateTerrainStatus()
        {
            var mousePos = GetViewport().GetMousePosition();
            if (Realm.Client.Core.GameHost.Instance.TryRaycastTerrainFromMousePosition(mousePos, out Vector3 pos))
            {
                if ((pos - _lastRaycastPos).LengthSquared() > 0.01f)
                {
                    _lastRaycastPos = pos;
                    _viewModel.StatusText = Realm.Client.Core.GameHost.Instance.GetTerrainStatusString(pos);
                }
            }
        }

        private void UpdateInspectorVisibility()
        {
            bool hasSelectedObject = GodotObject.IsInstanceValid(Realm.Client.Core.GameHost.Instance.SelectedEditorObject);
            bool shouldShowInspector = hasSelectedObject && (Realm.Client.Core.GameHost.Instance.ActiveEditorTool == Realm.Client.Core.GameHost.EditorTool.SelectMove);
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
            int intervalMins = Realm.Client.UI.MapEditor.EditorSettingsDialog.CurrentSettings?.AutoBackupIntervalMinutes ?? 30;
            if (intervalMins <= 0) return;
            
            _autoBackupElapsedSeconds += delta;
            double targetSeconds = intervalMins * 60.0;
            if (_autoBackupElapsedSeconds >= targetSeconds)
            {
                _autoBackupElapsedSeconds = 0;
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
            if (_optWaterMode == null || _optWaterMode.Selected < 0) return 0;
            var meta = _optWaterMode.GetItemMetadata(_optWaterMode.Selected);
            if (meta.VariantType == Variant.Type.Nil) return 0;
            return (byte)(int)meta;
        }

        public WaterType GetSelectedWaterMode()
        {
            if (_optWaterMode == null || _optWaterMode.Selected < 0) return WaterType.None;
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
            ApplyValueBadgeStyles();
            ApplyCheckBoxStyles();
            ApplySubContainerStyles();
            ApplyTopBarStyles();
            ApplyStatusLabelStyles();
        }

        private void ApplyPanelStyles()
        {
            if (_panelLeft != null)
            {
                _panelLeft.MouseFilter = Control.MouseFilterEnum.Ignore;
                _panelLeft.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            }
            if (_panelRight != null)
            {
                _panelRight.MouseFilter = Control.MouseFilterEnum.Ignore;
                _panelRight.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            }

            var leftScroll = GetNodeOrNull<ScrollContainer>("LeftSlidePanel/LeftScroll");
            if (leftScroll != null) leftScroll.MouseFilter = Control.MouseFilterEnum.Ignore;

            var rightScroll = GetNodeOrNull<ScrollContainer>("RightSlidePanel/RightScroll");
            if (rightScroll != null) rightScroll.MouseFilter = Control.MouseFilterEnum.Ignore;

            var leftVBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
            if (leftVBox != null)
            {
                leftVBox.MouseFilter = Control.MouseFilterEnum.Ignore;
                leftVBox.AddThemeConstantOverride("separation", 14);
            }

            var rightVBox = GetNodeOrNull<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer");
            if (rightVBox != null)
            {
                rightVBox.MouseFilter = Control.MouseFilterEnum.Ignore;
                rightVBox.AddThemeConstantOverride("separation", 14);
            }
        }

        private void ApplyAccordionStyles()
        {
            if (_accordionFile != null) _accordionFile.CustomMinimumSize = new Vector2(260, 0);
            if (_accordionViewport != null) _accordionViewport.CustomMinimumSize = new Vector2(260, 0);
            if (_accordionTool != null) _accordionTool.CustomMinimumSize = new Vector2(260, 0);
            if (_accordionBrush != null) _accordionBrush.CustomMinimumSize = new Vector2(260, 0);
            if (_accordionToolSettings != null) _accordionToolSettings.CustomMinimumSize = new Vector2(260, 0);
            if (_accordionPlacement != null) _accordionPlacement.CustomMinimumSize = new Vector2(260, 0);
            if (_accordionInspector != null) _accordionInspector.CustomMinimumSize = new Vector2(260, 0);
            if (_accordionWater != null) _accordionWater.CustomMinimumSize = new Vector2(260, 0);

            ApplyCardPanelStyle(_accordionFile);
            ApplyCardPanelStyle(_accordionViewport);
            ApplyCardPanelStyle(_accordionTool);
            ApplyCardPanelStyle(_accordionBrush);
            if (_accordionWater != null) ApplyCardPanelStyle(_accordionWater);
            ApplyCardPanelStyle(_accordionToolSettings);
            ApplyCardPanelStyle(_accordionPlacement);
            ApplyCardPanelStyle(_accordionInspector);
        }

        private void ApplyContentBoxStyles()
        {
            StyleContentBox(_contentFile);
            StyleContentBox(_contentViewport);
            StyleContentBox(_contentTool);
            StyleContentBox(_contentBrush);
            if (_contentWater != null) StyleContentBox(_contentWater);
            StyleContentBox(_contentToolSettings);
            StyleContentBox(_contentPlacement);
            StyleContentBox(_contentInspector);
        }

        private void ApplyScrollContainerStyles()
        {
            SetupCardScrollContainer(_contentFile, 300f);
            SetupCardScrollContainer(_contentViewport, 0f, false);
            SetupCardScrollContainer(_contentTool, 320f);
            SetupCardScrollContainer(_contentBrush, 300f);
            if (_contentWater != null) SetupCardScrollContainer(_contentWater, 200f);
            SetupCardScrollContainer(_contentToolSettings, 320f);
            SetupCardScrollContainer(_contentPlacement, 320f);
            SetupCardScrollContainer(_contentInspector, 300f);
        }

        private void ApplyActionButtonsStyles()
        {
            foreach (var btn in new[] { _btnLoad, _btnSave, _btnSaveMore })
            {
                if (btn == null) continue;
                StyleRowButton(btn);
                btn.AddThemeFontSizeOverride("font_size", 11);
                btn.Alignment = HorizontalAlignment.Center;
                foreach (string styleName in new[] { "normal", "hover", "pressed" })
                {
                    if (btn.GetThemeStylebox(styleName) is StyleBoxFlat styleBox)
                    {
                        var compactBox = (StyleBoxFlat)styleBox.Duplicate();
                        compactBox.ContentMarginLeft = 4;
                        compactBox.ContentMarginRight = 4;
                        btn.AddThemeStyleboxOverride(styleName, compactBox);
                    }
                }
            }
            StyleRowButton(_btnTestMap);
            StyleRowButton(_btnSaveAs);
            StyleRowButton(_btnPublish);
            StyleRowButton(_btnExportMap);
            StyleRowButton(_btnResetMap);
            StyleRowButton(_btnGenerateMap);
            StyleRowButton(_btnImportMinimap);
            StyleRowButton(_btnRandomGen);
            StyleRowButton(_btnEditorSettings);
            if (_btnEditorSettings != null) _btnEditorSettings.Alignment = HorizontalAlignment.Center;
        }

        private void ApplyToolButtonsStyles()
        {
            StyleRowButton(_btnRaise);
            StyleRowButton(_btnLower);
            StyleRowButton(_btnHeight);
            StyleRowButton(_btnSmooth);
            StyleRowButton(_btnPlateau);
            StyleRowButton(_btnRamp);
            StyleRowButton(_btnNoise);
            StyleRowButton(_btnWater);
            StyleRowButton(_btnTextureBrush);
            StyleRowButton(_btnFloodFill);
            StyleRowButton(_btnPathingBrush);
            StyleRowButton(_btnFloodFillPathing);
            StyleRowButton(_btnAddObject);
            StyleRowButton(_btnSelectMove);
            StyleRowButton(_btnDeleteObject);
            StyleRowButton(_btnDrawCoordinate);
            StyleRowButton(_btnCommitCoordinate);
            StyleRowButton(_btnSelectArea);
            StyleRowButton(_btnCut);
            StyleRowButton(_btnCopy);
            StyleRowButton(_btnPaste);
            StyleRowButton(_btnEraseArea);
        }

        private void ApplyInspectorButtonsStyles()
        {
            StyleRowButton(_btnInspectorRotLeft);
            StyleRowButton(_btnInspectorRotRight);
            StyleRowButton(_btnInspectorScaleDown);
            StyleRowButton(_btnInspectorScaleUp);
            StyleRowButton(_btnInspectorScaleReset);
            StyleRowButton(_btnInspectorDelete);
        }

        private void ApplyValueBadgeStyles()
        {
            StyleValueBadge(_lblBrushSizeValue);
            StyleValueBadge(_lblBrushStrengthValue);
            StyleValueBadge(_lblBlockStepValue);
            StyleValueBadge(_lblHeightValue);
            StyleValueBadge(_lblPlacementRotateValue);
            StyleValueBadge(_lblPlacementScaleValue);
            StyleValueBadge(_lblClumpDensityValue);
            StyleValueBadge(_lblClumpScaleVarValue);
            StyleValueBadge(_lblPasteRotation);
        }

        private void ApplyCheckBoxStyles()
        {
            StyleCheckBoxRow(_chkBlockMode);
            StyleCheckBoxRow(_chkShallowWater);
            StyleCheckBoxRow(_chkDeepWater);
            StyleCheckBoxRow(_chkFlying);
            StyleCheckBoxRow(_chkGround);
            StyleCheckBoxRow(_chkBuildable);
            StyleCheckBoxRow(_chkRandomRotation);
            StyleCheckBoxRow(_chkRandomScale);
            StyleCheckBoxRow(_chkClumpMode);
        }

        private void ApplySubContainerStyles()
        {
            StyleSubContainer(_containerTextureSettings, "Texture Paint Palette");
            StyleSubContainer(_containerPathingSettings, "Pathing Masks");
            StyleSubContainer(_containerPlacementSettings, "Placement Controls");
            StyleSubContainer(_densityBox, "Clump Density");
            StyleSubContainer(_scaleVarBox, "Scale Variance");
            StyleSubContainer(_containerEyedropperSettings, "Eyedropper Sample Filter");
            StyleSubContainer(_containerPasteSettings, "Paste Options");
            StyleSubContainer(_containerCategorySelector, "Entity Categories");
        }

        private StyleBox CreateTopBarStylebox()
        {
            var posTexture = GD.Load<Texture2D>("res://Assets/UI/map_editor_pos.png");
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
            alphaStyle.CornerRadiusTopLeft = 6;
            alphaStyle.CornerRadiusTopRight = 6;
            alphaStyle.CornerRadiusBottomLeft = 6;
            alphaStyle.CornerRadiusBottomRight = 6;
            alphaStyle.ContentMarginLeft = 16;
            alphaStyle.ContentMarginRight = 16;
            alphaStyle.ContentMarginTop = 6;
            alphaStyle.ContentMarginBottom = 6;
            return alphaStyle;
        }

        private void ApplyTopBarStyles()
        {
            var titleLbl = _topBar?.GetNodeOrNull<Label>("HBox/TitleLabel") ?? GetNodeOrNull<Label>("TopBar/HBox/TitleLabel");
            if (titleLbl != null) titleLbl.Visible = false;

            StyleBox barStyle = CreateTopBarStylebox();

            if (_topBar != null)
            {
                _topBar.AddThemeStyleboxOverride("panel", barStyle);
            }

            if (_panelMeasurementHUD != null)
            {
                _panelMeasurementHUD.AddThemeStyleboxOverride("panel", barStyle);
            }

            var hBox = _topBar?.GetNodeOrNull<HBoxContainer>("HBox") ?? GetNodeOrNull<HBoxContainer>("TopBar/HBox");
            if (hBox != null)
            {
                hBox.Alignment = BoxContainer.AlignmentMode.Center;
            }
        }

        private void ApplyStatusLabelStyles()
        {
            if (_statusLabel != null)
            {
                _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
                _statusLabel.VerticalAlignment = VerticalAlignment.Center;
                _statusLabel.AddThemeFontSizeOverride("font_size", 13);
                _statusLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
                var fontStatus = GetFontAwesomeFont();
                if (fontStatus != null)
                {
                    _statusLabel.AddThemeFontOverride("font", fontStatus);
                }
            }

            if (_lblMeasureTelemetry != null)
            {
                _lblMeasureTelemetry.HorizontalAlignment = HorizontalAlignment.Center;
                _lblMeasureTelemetry.VerticalAlignment = VerticalAlignment.Center;
                _lblMeasureTelemetry.AddThemeFontSizeOverride("font_size", 13);
                _lblMeasureTelemetry.AddThemeColorOverride("font_color", UIStyle.ColorGold);
                var fontStatus = GetFontAwesomeFont();
                if (fontStatus != null)
                {
                    _lblMeasureTelemetry.AddThemeFontOverride("font", fontStatus);
                }
            }

            UIStyle.ApplyTitle(_feedbackLabel, "", 24);
        }

        public void RefreshCoordinateListExternal()
        {
            if (_coordinateListVBox == null) return;
            foreach (var child in _coordinateListVBox.GetChildren())
            {
                child.QueueFree();
            }
            var coordinates = Realm.Client.Core.GameHost.Instance?.EditorCoordinates;
            if (coordinates == null) return;
            foreach (var coord in coordinates)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 6);
                _coordinateListVBox.AddChild(row);

                var btnSelect = new Button();
                btnSelect.Text = $"{coord.Name}  ({coord.MinX:F0},{coord.MinZ:F0}) → ({coord.MaxX:F0},{coord.MaxZ:F0})";
                btnSelect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                btnSelect.Flat = true;
                btnSelect.Alignment = HorizontalAlignment.Left;
                btnSelect.AddThemeFontSizeOverride("font_size", 11);
                btnSelect.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
                string coordinateName = coord.Name;
                btnSelect.Pressed += () =>
                {
                    EditorApi?.SelectCoordinate(coordinateName);
                };
                row.AddChild(btnSelect);

                var btnDel = new Button();
                btnDel.Set("icon_max_width", 0);
                SetupButton(btnDel, "✕", () =>
                    {
                        EditorApi?.DeleteCoordinate(coordinateName);
                        RefreshCoordinateListExternal();
                    }, 10, $"Delete coordinate '{coordinateName}'");
                btnDel.CustomMinimumSize = new Vector2(28, 24);
                row.AddChild(btnDel);
            }
        }


        private void OpenOverlayModePopup()
        {
            if (_popupOverlayMode == null || Realm.Client.Core.GameHost.Instance == null || _btnToggleGrid == null) return;

            var mode = Realm.Client.Core.GameHost.Instance.EditorGridMode;
            bool isGrid = mode == Realm.Client.Core.GameHost.GridOverlayMode.Grid || mode == Realm.Client.Core.GameHost.GridOverlayMode.Both;
            bool isPolar = mode == Realm.Client.Core.GameHost.GridOverlayMode.Polar || mode == Realm.Client.Core.GameHost.GridOverlayMode.Both;
            bool isWireframe = GetViewport()?.DebugDraw == Viewport.DebugDrawEnum.Wireframe;

            SetPopupItemChecked(0, isGrid);
            SetPopupItemChecked(1, isPolar);
            SetPopupItemChecked(2, Realm.Client.Core.GameHost.Instance.EditorCameraBoundsVisible);
            SetPopupItemChecked(3, isWireframe);

            var globalRect = _btnToggleGrid.GetGlobalRect();
            var popupPos = new Vector2I((int)globalRect.Position.X, (int)(globalRect.Position.Y + globalRect.Size.Y + 2));
            _popupOverlayMode.Position = popupPos;
            _popupOverlayMode.Popup();
        }

        private void SetPopupItemChecked(int itemIndexInList, bool isChecked)
        {
            int idx = _popupOverlayMode.GetItemIndex(itemIndexInList);
            if (idx >= 0) _popupOverlayMode.SetItemChecked(idx, isChecked);
        }

        public void UpdateGridOverlayExternal(Realm.Client.Core.GameHost.GridOverlayMode mode)
        {
            if (_btnToggleGrid != null)
            {
                _btnToggleGrid.Text = UnicodeIcons.BORDER_ALL;
                _btnToggleGrid.TooltipText = TranslationServer.Translate("Overlay");
            }
            if (_popupOverlayMode != null)
            {
                bool isGrid = mode == Realm.Client.Core.GameHost.GridOverlayMode.Grid || mode == Realm.Client.Core.GameHost.GridOverlayMode.Both;
                bool isPolar = mode == Realm.Client.Core.GameHost.GridOverlayMode.Polar || mode == Realm.Client.Core.GameHost.GridOverlayMode.Both;
                int gridIdx = _popupOverlayMode.GetItemIndex(0);
                int polarIdx = _popupOverlayMode.GetItemIndex(1);
                if (gridIdx >= 0) _popupOverlayMode.SetItemChecked(gridIdx, isGrid);
                if (polarIdx >= 0) _popupOverlayMode.SetItemChecked(polarIdx, isPolar);
            }
            UpdatePolarSubControlsVisibility(mode);
        }

        public void UpdatePolarSubControlsVisibility(Realm.Client.Core.GameHost.GridOverlayMode mode)
        {
            bool isPolar = (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.EditorMirrorMode == MirrorMode.Rotational) || mode == Realm.Client.Core.GameHost.GridOverlayMode.Polar || mode == Realm.Client.Core.GameHost.GridOverlayMode.Both;
            if (_rowPolarConfig != null)
            {
                _rowPolarConfig.Visible = isPolar;
            }
        }

        public void UpdateMeasureTelemetry(float eucTiles, float eucWorld, float manhattanTiles, float dx, float dz, float dy, float angleDeg, float slopePct)
        {
            if (_panelMeasurementHUD == null || _lblMeasureTelemetry == null) return;
            _panelMeasurementHUD.Visible = true;

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

            string text = string.Format(
                TranslationServer.Translate($"{UnicodeIcons.RULER} MEASURE: Manhattan: {0:F1}T | Euclidean: {1:F1}T ({2:F1}m) | Angle: {3:F1}° | Slope: {4}"),
                manhattanTiles, eucTiles, eucWorld, angleDeg, slopeStr);

            if (Mathf.Abs(dy) > 0.01f)
            {
                text += string.Format(TranslationServer.Translate(" | Elev: {0:+0.0;-0.0;0.0}m ({1:F1}%)"), dy, slopePct);
            }

            _lblMeasureTelemetry.Text = text;
        }

        public void ClearMeasureTelemetry()
        {
            if (_panelMeasurementHUD != null)
            {
                _panelMeasurementHUD.Visible = false;
            }
        }

        public void UpdatePasteTelemetry(int deltaX, int deltaZ, float distTiles, float distWorld, float angleDeg)
        {
            if (_lblPasteTelemetry != null)
            {
                _lblPasteTelemetry.Visible = true;
                _lblPasteTelemetry.Text = string.Format("ΔX: {0:+0;-0;0}, ΔZ: {1:+0;-0;0} | Pivot: {2:F1}T ({3:F1}m) @ {4:F1}°",
                    deltaX, deltaZ, distTiles, distWorld, angleDeg);
            }
        }

        public void UpdateCameraBoundsOverlayExternal(bool visible)
        {
            if (_popupOverlayMode != null)
            {
                int camBoundsIdx = _popupOverlayMode.GetItemIndex(2);
                if (camBoundsIdx >= 0) _popupOverlayMode.SetItemChecked(camBoundsIdx, visible);
            }
        }

        public void UpdateWireframeOverlayExternal(bool enabled)
        {
            if (_popupOverlayMode != null)
            {
                int wireframeIdx = _popupOverlayMode.GetItemIndex(3);
                if (wireframeIdx >= 0) _popupOverlayMode.SetItemChecked(wireframeIdx, enabled);
            }
        }

        private void StylePopupButton(Button btn, string text, string tooltip)
        {
            btn.Text = TranslationServer.Translate(text);
            btn.TooltipText = TranslationServer.Translate(tooltip);
            btn.FocusMode = Control.FocusModeEnum.None;
            btn.CustomMinimumSize = new Vector2(0, 24);
            btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
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
            if (_popupCamera == null || Realm.Client.Core.GameHost.Instance == null || _btnToggleCamera == null) return;

            UpdateCameraPopupControls();

            var globalRect = _btnToggleCamera.GetGlobalRect();
            var popupPos = new Vector2I((int)globalRect.Position.X, (int)(globalRect.Position.Y + globalRect.Size.Y + 2));
            _popupCamera.Position = popupPos;
            _popupCamera.Popup();
        }

        public void UpdateCameraPopupControls()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;
            var camera = Realm.Client.Core.GameHost.Instance.MainCamera as Realm.Client.CameraControl;
            if (camera != null)
            {
                UpdateCameraAngleButtonText(camera.IsTopDown());
                UpdateFreeCameraExternal(camera.IsFreeCamera);
            }
        }

        public void UpdateShadowsExternal(bool disabled)
        {
            if (_chkEnvShadows != null)
            {
                _chkEnvShadows.SetPressedNoSignal(!disabled);
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
            if (_popupEnvironment == null || Realm.Client.Core.GameHost.Instance == null || _btnToggleEnvironment == null) return;

            UpdateEnvironmentPopupControls();

            var globalRect = _btnToggleEnvironment.GetGlobalRect();
            var popupPos = new Vector2I((int)globalRect.Position.X, (int)(globalRect.Position.Y + globalRect.Size.Y + 2));
            _popupEnvironment.Position = popupPos;
            _popupEnvironment.Popup();
        }

        public void UpdateEnvironmentPopupControls()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;

            if (_optEnvLighting != null)
            {
                int timeIdx = Realm.Client.Core.GameHost.Instance.TimeOfDayIndex;
                _optEnvLighting.Select(Mathf.Clamp(timeIdx, 0, 3));
            }

            if (_optEnvWeather != null && Realm.Client.Core.GameHost.Instance.EnvironmentService != null)
            {
                string curWeather = Realm.Client.Core.GameHost.Instance.EnvironmentService.GetCurrentWeather();
                int weatherIdx = (curWeather?.ToLowerInvariant()) switch
                {
                    "rain" => 1,
                    "snow" => 2,
                    "fog" => 3,
                    _ => 0
                };
                _optEnvWeather.Select(weatherIdx);
            }

            if (_chkEnvShadows != null)
            {
                _chkEnvShadows.SetPressedNoSignal(!Realm.Client.Core.GameHost.Instance.EditorDisableShadows);
            }
        }

        public void UpdateEnvWeatherSelection(string weatherType)
        {
            if (_optEnvWeather != null)
            {
                int idx = (weatherType?.ToLowerInvariant()) switch
                {
                    "rain" => 1,
                    "snow" => 2,
                    "fog" => 3,
                    _ => 0
                };
                _optEnvWeather.Select(idx);
            }
        }

        public void EnsureCameraBoundsVisible()
        {
            if (Realm.Client.Core.GameHost.Instance != null && !Realm.Client.Core.GameHost.Instance.EditorCameraBoundsVisible)
            {
                Realm.Client.Core.GameHost.Instance.EditorCameraBoundsVisible = true;
                Realm.Client.Core.GameHost.Instance.UpdateCameraBoundsOverlayVisibility();
                UpdateCameraBoundsOverlayExternal(true);
            }
        }

        public void UpdatePathingOverlayExternal(bool visible)
        {
        }

        public void BackToHubAction()
        {
            if (HasUnsavedChanges())
            {
                ShowConfirmationDialog(
                    "You haven't saved yet",
                    onConfirm: () =>
                    {
                        WriteCleanExitMarker();
                        Realm.Client.UI.UIManager.Instance.TransitionTo(GameScreen.MainMenu);
                    },
                    confirmText: "Quit",
                    cancelText: "Stay"
                );
            }
            else
            {
                WriteCleanExitMarker();
                Realm.Client.UI.UIManager.Instance.TransitionTo(GameScreen.MainMenu);
            }
        }

        public void HandleQuitRequest()
        {
            if (HasUnsavedChanges())
            {
                ShowConfirmationDialog(
                    "You haven't saved yet",
                    onConfirm: () =>
                    {
                        WriteCleanExitMarker();
                        GetTree().Quit();
                    },
                    confirmText: "Quit",
                    cancelText: "Stay"
                );
            }
            else
            {
                WriteCleanExitMarker();
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
            _entityPaletteController?.SelectCategory(_entityPaletteController.CurrentCategory, triggerAddObject: false);
        }
        public void UpdateRotationExternal(float angle)
        {
            if (Mathf.IsEqualApprox(_lastRotationExternal, angle)) return;
            _lastRotationExternal = angle;
            if (_lblPlacementRotateValue != null) _lblPlacementRotateValue.Text = angle.ToString("F0") + "°";
            if (_sldPlacementRotate != null && !Mathf.IsEqualApprox((float)_sldPlacementRotate.Value, angle)) _sldPlacementRotate.Value = angle;
        }
        public void UpdatePasteRotationExternal(float angle)
        {
            if (Mathf.IsEqualApprox(_lastPasteRotationExternal, angle)) return;
            _lastPasteRotationExternal = angle;
            if (_lblPasteRotation != null) _lblPasteRotation.Text = angle.ToString("F0") + "°";
            if (_sldPasteRotation != null && !Mathf.IsEqualApprox((float)_sldPasteRotation.Value, angle)) _sldPasteRotation.Value = angle;
        }
        public void UpdateScaleExternal(float scale)
        {
            if (Mathf.IsEqualApprox(_lastScaleExternal, scale)) return;
            _lastScaleExternal = scale;
            if (_lblPlacementScaleValue != null) _lblPlacementScaleValue.Text = scale.ToString("F1") + "x";
            if (_sldPlacementScale != null && !Mathf.IsEqualApprox((float)_sldPlacementScale.Value, scale)) _sldPlacementScale.Value = scale;
        }
        public void UpdateBrushSizeExternal(float size)
        {
            if (Mathf.IsEqualApprox(_lastBrushSizeExternal, size)) return;
            _lastBrushSizeExternal = size;
            if (_sldBrushSize != null && !Mathf.IsEqualApprox((float)_sldBrushSize.Value, size))
            {
                _sldBrushSize.Value = Mathf.Round(size);
            }
            if (_lblBrushSizeValue != null)
            {
                _lblBrushSizeValue.Text = Mathf.Round(size).ToString("F0");
            }
        }

        public void UpdateBlockLevelHeightExternal(float step)
        {
            if (_sldBlockStep != null)
            {
                _sldBlockStep.Value = step;
            }
            if (_lblBlockStepValue != null)
            {
                _lblBlockStepValue.Text = step.ToString("F1") + " m";
            }
        }

        public void UpdateExactHeightExternal(float height)
        {
            if (_sldHeight != null)
            {
                _sldHeight.Value = height;
            }
            if (_lblHeightValue != null)
            {
                _lblHeightValue.Text = height.ToString("F1") + "m";
            }
        }

        private void HighlightSwatch(Button selectedSwatch)
        {
            if (!GodotObject.IsInstanceValid(_swatchHighlightPanel))
            {
                _swatchHighlightPanel = new Panel();
                _swatchHighlightPanel.Name = "SwatchHighlightPanel";
                _swatchHighlightPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
                _swatchHighlightPanel.SetAnchorsPreset(LayoutPreset.FullRect);
                _swatchHighlightPanel.GrowHorizontal = GrowDirection.Both;
                _swatchHighlightPanel.GrowVertical = GrowDirection.Both;

                var style = new StyleBoxFlat();
                style.BgColor = new Color(0, 0, 0, 0);
                style.BorderColor = UIStyle.ColorCyanGlow;
                style.SetBorderWidthAll(3);
                style.CornerRadiusTopLeft = 4;
                style.CornerRadiusTopRight = 4;
                style.CornerRadiusBottomLeft = 4;
                style.CornerRadiusBottomRight = 4;
                _swatchHighlightPanel.AddThemeStyleboxOverride("panel", style);
            }

            if (_swatchHighlightPanel.GetParent() != null)
            {
                _swatchHighlightPanel.GetParent().RemoveChild(_swatchHighlightPanel);
            }

            if (selectedSwatch != null)
            {
                selectedSwatch.AddChild(_swatchHighlightPanel);
            }
        }

        private void HighlightCliffSwatch(Button selectedSwatch)
        {
            if (!GodotObject.IsInstanceValid(_swatchCliffHighlightPanel))
            {
                _swatchCliffHighlightPanel = new Panel();
                _swatchCliffHighlightPanel.Name = "SwatchCliffHighlightPanel";
                _swatchCliffHighlightPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
                _swatchCliffHighlightPanel.SetAnchorsPreset(LayoutPreset.FullRect);
                _swatchCliffHighlightPanel.GrowHorizontal = GrowDirection.Both;
                _swatchCliffHighlightPanel.GrowVertical = GrowDirection.Both;

                var style = new StyleBoxFlat();
                style.BgColor = new Color(0, 0, 0, 0);
                style.BorderColor = new Color(0.9f, 0.45f, 0.1f); // Vibrant orange/gold for cliff
                style.SetBorderWidthAll(3);
                style.CornerRadiusTopLeft = 4;
                style.CornerRadiusTopRight = 4;
                style.CornerRadiusBottomLeft = 4;
                style.CornerRadiusBottomRight = 4;
                _swatchCliffHighlightPanel.AddThemeStyleboxOverride("panel", style);
            }

            if (_swatchCliffHighlightPanel.GetParent() != null)
            {
                _swatchCliffHighlightPanel.GetParent().RemoveChild(_swatchCliffHighlightPanel);
            }

            if (selectedSwatch != null)
            {
                selectedSwatch.AddChild(_swatchCliffHighlightPanel);
            }
        }

        private void CheckPostLaunchPrompts()
        {
            if (AssetIndexService.Instance.IsIndexVersionMismatch() || AssetIndexService.Instance.HasIncorrectlyIndexedSample())
            {
                _ = ShowAssetIndexRepairModalAsync(() =>
                {
                    CheckUnsavedSessionOnLaunch(() =>
                    {
                        CheckCreatorRegistrationAndPrompt();
                    });
                });
            }
            else
            {
                CheckUnsavedSessionOnLaunch(() =>
                {
                    CheckCreatorRegistrationAndPrompt();
                });
            }
        }

        private void SetPanelExpanded(string panelPath, string buttonPath, string contentPath, bool expand)
        {
            var btn = GetNodeOrNull<Button>($"{panelPath}/{buttonPath}");
            var content = GetNodeOrNull<Control>($"{panelPath}/{contentPath}");

            if (btn != null && content != null)
            {
                content.Visible = expand;
                btn.Text = expand ? "▲" : "▼";
            }
        }

        private void ShowMapNewerThanGameErrorDialog(string selectedFolder, string mapVersion, string gameVersion)
        {
            var popup = new Panel();
            popup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            popup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
            AddChild(popup);

            var cardPanel = new Panel();
            cardPanel.CustomMinimumSize = new Vector2(540, 260);
            cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
            cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
            popup.AddChild(cardPanel);

            var vbox = new VBoxContainer();
            vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            vbox.CustomMinimumSize = new Vector2(500, 230);
            vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
            cardPanel.AddChild(vbox);

            vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

            var titleLabel = new Label();
            UIStyle.ApplyTitle(titleLabel, Tr("MAP REQUIRES NEWER GAME VERSION"), 18);
            titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.4f, 0.3f));
            vbox.AddChild(titleLabel);

            vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

            var descLabel = new Label();
            descLabel.Text = $"{string.Format(Tr("Map: {0}"), System.IO.Path.GetFileName(selectedFolder))}\n{string.Format(Tr("Map Build: {0} | Editor Build: {1}"), mapVersion, gameVersion)}\n\n{Tr("This map was created with a newer version of the game. Downgrading maps is not supported.\nPlease update the game to open this map.")}";
            descLabel.HorizontalAlignment = HorizontalAlignment.Center;
            descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            descLabel.AddThemeFontSizeOverride("font_size", 13);
            descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
            vbox.AddChild(descLabel);

            vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

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
            UIStyle.ApplyButtonText(closeBtn, Tr("CLOSE"), 14);
            closeBtn.CustomMinimumSize = new Vector2(120, 38);
            buttonRow.AddChild(closeBtn);

            closeBtn.Pressed += () =>
            {
                Realm.Client.UI.UIManager.Instance?.PlayClickSound();
                popup.QueueFree();
            };
        }

        private void ShowGreenlightStatusDialog(string mapTitle, string mapVersion, int verifiedGoodReviews, int totalReviews, double averageRating, bool isGreenlit)
        {
            var overlay = new ColorRect();
            overlay.Name = "GreenlightStatusOverlay";
            overlay.Color = new Color(0, 0, 0, 0.75f);
            overlay.SetAnchorsPreset(LayoutPreset.FullRect);
            overlay.MouseFilter = Control.MouseFilterEnum.Stop;
            overlay.ZIndex = 1000;
            AddChild(overlay);

            var center = new CenterContainer();
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            overlay.AddChild(center);

            var panel = new PanelContainer();
            panel.CustomMinimumSize = new Vector2(640, 440);
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.12f, 0.14f, 0.20f, 0.98f);
            style.BorderWidthTop = 2; style.BorderWidthBottom = 2; style.BorderWidthLeft = 2; style.BorderWidthRight = 2;
            style.BorderColor = isGreenlit ? new Color(0.3f, 0.8f, 0.4f, 1f) : new Color(0.8f, 0.4f, 0.3f, 1f);
            style.CornerRadiusTopLeft = 8; style.CornerRadiusTopRight = 8; style.CornerRadiusBottomLeft = 8; style.CornerRadiusBottomRight = 8;
            panel.AddThemeStyleboxOverride("panel", style);
            center.AddChild(panel);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_top", 22);
            margin.AddThemeConstantOverride("margin_bottom", 22);
            margin.AddThemeConstantOverride("margin_left", 26);
            margin.AddThemeConstantOverride("margin_right", 26);
            panel.AddChild(margin);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 14);
            margin.AddChild(vbox);

            var lblTitle = new Label();
            lblTitle.Text = isGreenlit ? TranslationServer.Translate("MAP GREENLIT FOR PUBLISHING") : TranslationServer.Translate("MAP NOT YET GREENLIT");
            lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
            lblTitle.AddThemeFontSizeOverride("font_size", 20);
            lblTitle.AddThemeColorOverride("font_color", isGreenlit ? new Color(0.4f, 0.9f, 0.5f) : new Color(1f, 0.6f, 0.4f));
            vbox.AddChild(lblTitle);

            var lblDesc = new Label();
            lblDesc.Text = isGreenlit
                ? string.Format(TranslationServer.Translate("'{0}' v{1} has met the community greenlight requirement and can now be published to the public registry."), mapTitle, mapVersion)
                : string.Format(TranslationServer.Translate("'{0}' v{1} is currently in Beta-Testing. Maps must achieve 100 Verified Good Reviews before graduating to the public registry."), mapTitle, mapVersion);
            lblDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            lblDesc.HorizontalAlignment = HorizontalAlignment.Center;
            lblDesc.AddThemeFontSizeOverride("font_size", 13);
            lblDesc.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
            vbox.AddChild(lblDesc);

            var metricsBox = new VBoxContainer();
            metricsBox.AddThemeConstantOverride("separation", 8);
            vbox.AddChild(metricsBox);

            bool reviewsMet = verifiedGoodReviews >= 100;
            var lblReviews = new Label();
            lblReviews.Text = string.Format(TranslationServer.Translate("• Total Verified Good Reviews: {0} / 100 {1}"), verifiedGoodReviews, reviewsMet ? "✓" : "");
            lblReviews.AddThemeColorOverride("font_color", reviewsMet ? new Color(0.4f, 0.9f, 0.5f) : new Color(0.9f, 0.8f, 0.6f));
            lblReviews.AddThemeFontSizeOverride("font_size", 15);
            metricsBox.AddChild(lblReviews);

            if (totalReviews > 0)
            {
                var lblAggregate = new Label();
                lblAggregate.Text = string.Format(TranslationServer.Translate("• Public Aggregate Reviews: {0} (Average Rating: {1:F1} ★)"), totalReviews, averageRating);
                lblAggregate.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.85f));
                lblAggregate.AddThemeFontSizeOverride("font_size", 12);
                metricsBox.AddChild(lblAggregate);
            }

            var tip = new Label();
            tip.Text = TranslationServer.Translate("How Verified Good Reviews are determined:\nA review qualifies as Verified & Good when submitted by a player who:\n  1. Is logged in with a verified account (Steam, Discord, etc.)\n  2. Has played your map for at least 30 minutes across 3 or more completed games\n  3. Gives your map a rating of 3 stars or higher\n\nHost multiplayer lobbies or share your map peer-to-peer to build verified reviews from your community.");
            tip.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            tip.AddThemeFontSizeOverride("font_size", 11);
            tip.AddThemeColorOverride("font_color", new Color(0.65f, 0.7f, 0.8f));
            vbox.AddChild(tip);

            var hbox = new HBoxContainer();
            hbox.Alignment = BoxContainer.AlignmentMode.Center;
            hbox.AddThemeConstantOverride("separation", 20);
            vbox.AddChild(hbox);

            var btnClose = new Button();
            btnClose.Text = TranslationServer.Translate("OK");
            btnClose.CustomMinimumSize = new Vector2(100, 36);
            btnClose.Pressed += () => overlay.QueueFree();
            hbox.AddChild(btnClose);
        }

        public void ShowConfirmationDialog(string message, Action onConfirm, string confirmText = "YES", string cancelText = "NO", Action onCancel = null, Action onDismissed = null, bool showCancel = true)
        {
            var overlay = new ColorRect();
            overlay.Name = "ConfirmationOverlay";
            overlay.Color = new Color(0, 0, 0, 0.65f);
            overlay.SetAnchorsPreset(LayoutPreset.FullRect);
            overlay.MouseFilter = Control.MouseFilterEnum.Stop;
            overlay.ZIndex = 1100;
            if (onDismissed != null)
            {
                overlay.TreeExited += () => onDismissed();
            }
            AddChild(overlay);

            var center = new CenterContainer();
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            overlay.AddChild(center);

            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            panel.CustomMinimumSize = new Vector2(520, 266);
            center.AddChild(panel);

            var bgTex = new TextureRect();
            bgTex.Texture = GD.Load<Texture2D>("res://Assets/UI/map_editor_test.png");
            bgTex.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            bgTex.StretchMode = TextureRect.StretchModeEnum.Scale;
            bgTex.SetAnchorsPreset(LayoutPreset.FullRect);
            panel.AddChild(bgTex);

            var margin = new MarginContainer();
            margin.SetAnchorsPreset(LayoutPreset.FullRect);
            margin.AddThemeConstantOverride("margin_top", 30);
            margin.AddThemeConstantOverride("margin_bottom", 25);
            margin.AddThemeConstantOverride("margin_left", 45);
            margin.AddThemeConstantOverride("margin_right", 45);
            panel.AddChild(margin);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 14);
            vbox.Alignment = BoxContainer.AlignmentMode.Center;
            margin.AddChild(vbox);

            var titleMargin = new MarginContainer();
            titleMargin.AddThemeConstantOverride("margin_top", -12);

            var lblTitle = new Label();
            UIStyle.ApplyTitle(lblTitle, TranslationServer.Translate(showCancel ? "CONFIRMATION REQUIRED" : "NOTIFICATION"), 18);
            lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
            lblTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
            titleMargin.AddChild(lblTitle);
            vbox.AddChild(titleMargin);

            var msgMargin = new MarginContainer();
            msgMargin.AddThemeConstantOverride("margin_top", 12);
            msgMargin.AddThemeConstantOverride("margin_bottom", 0);
            msgMargin.SizeFlagsVertical = SizeFlags.ExpandFill;

            var lblMsg = new Label();
            lblMsg.Text = TranslationServer.Translate(message);
            lblMsg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            lblMsg.HorizontalAlignment = HorizontalAlignment.Center;
            lblMsg.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
            lblMsg.AddThemeFontSizeOverride("font_size", 13);
            lblMsg.SizeFlagsVertical = SizeFlags.ExpandFill;
            msgMargin.AddChild(lblMsg);
            vbox.AddChild(msgMargin);

            var hbox = new HBoxContainer();
            hbox.AddThemeConstantOverride("separation", 24);
            hbox.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            vbox.AddChild(hbox);

            var btnConfirm = new Button();
            btnConfirm.Set("icon_max_width", 0);
            btnConfirm.CustomMinimumSize = new Vector2(110, 34);
            btnConfirm.MouseFilter = Control.MouseFilterEnum.Stop;
            SetupButton(btnConfirm, TranslationServer.Translate(confirmText), () =>
            {
                overlay.QueueFree();
                onConfirm?.Invoke();
            }, 13);
            btnConfirm.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
            hbox.AddChild(btnConfirm);

            if (showCancel)
            {
                var btnCancel = new Button();
                btnCancel.Set("icon_max_width", 0);
                btnCancel.CustomMinimumSize = new Vector2(110, 34);
                btnCancel.MouseFilter = Control.MouseFilterEnum.Stop;
                Action cancelAction = () =>
                {
                    overlay.QueueFree();
                    onCancel?.Invoke();
                };
                overlay.SetMeta("CancelAction", Callable.From(cancelAction));
                SetupButton(btnCancel, TranslationServer.Translate(cancelText), () =>
                {
                    cancelAction();
                }, 13);
                btnCancel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
                hbox.AddChild(btnCancel);
            }
        }

        private void ShowWasmConsoleModal()
        {
            Realm.Client.UI.WasmConsoleWindow.Instance.ClearLogs();
            Realm.Client.UI.WasmConsoleWindow.Instance.ShowConsole();
        }

        public void SetWasmConsoleStatus(string statusText, Color color)
        {
            Realm.Client.UI.WasmConsoleWindow.Instance.SetStatus(statusText, color);
        }

        public void CloseWasmConsoleModal()
        {
            // WasmConsoleWindow remains open as a persistent window across scene transitions.
        }

        public void ToggleHelpPanelExternal()
        {
            if (GodotObject.IsInstanceValid(_helpOverlayPanel))
            {
                _helpOverlayPanel.QueueFree();
                _helpOverlayPanel = null;
                return;
            }

            _helpOverlayPanel = new ColorRect();
            _helpOverlayPanel.Name = "HelpOverlayPanel";
            _helpOverlayPanel.Color = new Color(0, 0, 0, 0.65f);
            _helpOverlayPanel.SetAnchorsPreset(LayoutPreset.FullRect);
            _helpOverlayPanel.MouseFilter = Control.MouseFilterEnum.Stop;
            _helpOverlayPanel.ZIndex = 1000;
            AddChild(_helpOverlayPanel);

            var center = new CenterContainer();
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            _helpOverlayPanel.AddChild(center);

            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            panel.CustomMinimumSize = new Vector2(1024, 770);
            center.AddChild(panel);

            var bgTex = new TextureRect();
            bgTex.Texture = GD.Load<Texture2D>("res://Assets/UI/map_editor_manual.png");
            bgTex.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            bgTex.StretchMode = TextureRect.StretchModeEnum.Scale;
            bgTex.SetAnchorsPreset(LayoutPreset.FullRect);
            panel.AddChild(bgTex);

            var margin = new MarginContainer();
            margin.SetAnchorsPreset(LayoutPreset.FullRect);
            margin.AddThemeConstantOverride("margin_top", 45);
            margin.AddThemeConstantOverride("margin_bottom", 75);
            margin.AddThemeConstantOverride("margin_left", 65);
            margin.AddThemeConstantOverride("margin_right", 65);
            panel.AddChild(margin);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 15);
            margin.AddChild(vbox);

            var titleMargin = new MarginContainer();
            titleMargin.AddThemeConstantOverride("margin_top", -20);
            titleMargin.AddThemeConstantOverride("margin_bottom", 0);

            var lblTitle = new Label();
            UIStyle.ApplyTitle(lblTitle, TranslationServer.Translate("RTS MAP EDITOR REFERENCE MANUAL"), 18);
            lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
            lblTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
            titleMargin.AddChild(lblTitle);
            vbox.AddChild(titleMargin);

            var scroll = new ScrollContainer();
            scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
            scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
            vbox.AddChild(scroll);

            var grid = new GridContainer();
            grid.Columns = 2;
            grid.AddThemeConstantOverride("h_separation", 30);
            grid.AddThemeConstantOverride("v_separation", 10);
            grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            scroll.AddChild(grid);

            AddHelpSectionHeader(grid, TranslationServer.Translate("PRIMARY MODULE SWITCHING"));
            AddHelpShortcutRow(grid, "F1", TranslationServer.Translate("Terrain Module"));
            AddHelpShortcutRow(grid, "F2", TranslationServer.Translate("Texture Module"));
            AddHelpShortcutRow(grid, "F3", TranslationServer.Translate("Pathing Module"));
            AddHelpShortcutRow(grid, "F4", TranslationServer.Translate("Objects Module"));
            AddHelpShortcutRow(grid, "F5", TranslationServer.Translate("Coordinates Module"));
            AddHelpShortcutRow(grid, "F6", TranslationServer.Translate("Clipboard Module"));

            AddHelpSectionHeader(grid, TranslationServer.Translate("CAMERA CONTROLS"));
            AddHelpShortcutRow(grid, "Arrows", TranslationServer.Translate("Pan map camera"));
            AddHelpShortcutRow(grid, "Mouse Scroll", TranslationServer.Translate("Zoom camera in / out"));
            AddHelpShortcutRow(grid, "Middle Mouse Drag", TranslationServer.Translate("Pan camera by dragging"));
            AddHelpShortcutRow(grid, "Shift + Middle Drag", TranslationServer.Translate("Rotate map camera view"));
            AddHelpShortcutRow(grid, "Comma (,) / Period (.)", TranslationServer.Translate("Rotate camera 90 degrees"));
            AddHelpShortcutRow(grid, "F8", TranslationServer.Translate("Toggle Free Camera"));
            AddHelpShortcutRow(grid, "WASD / QE", TranslationServer.Translate("Fly camera in Free Camera mode (RMB drag to look)"));

            AddHelpSectionHeader(grid, TranslationServer.Translate("EDITOR TOOLS"));
            AddHelpShortcutRow(grid, "1", TranslationServer.Translate("Raise Terrain Tool"));
            AddHelpShortcutRow(grid, "2", TranslationServer.Translate("Lower Terrain Tool"));
            AddHelpShortcutRow(grid, "3", TranslationServer.Translate("Smooth Terrain Tool"));
            AddHelpShortcutRow(grid, "4", TranslationServer.Translate("Flatten Terrain Tool"));
            AddHelpShortcutRow(grid, "5", TranslationServer.Translate("Plateau Terrain Tool"));
            AddHelpShortcutRow(grid, "6", TranslationServer.Translate("Ramp Tool"));
            AddHelpShortcutRow(grid, "7", TranslationServer.Translate("Roughen (Noise) Tool"));
            AddHelpShortcutRow(grid, "8", TranslationServer.Translate("Texture Painter Brush"));
            AddHelpShortcutRow(grid, "9", TranslationServer.Translate("Add Objects"));
            AddHelpShortcutRow(grid, "Q", TranslationServer.Translate("Select / Move Tool"));
            AddHelpShortcutRow(grid, "I", TranslationServer.Translate("Eyedropper Picker"));

            AddHelpSectionHeader(grid, TranslationServer.Translate("SCULPTING / PLACEMENT SETTINGS"));
            AddHelpShortcutRow(grid, "[ / ]", TranslationServer.Translate("Increase / decrease brush size"));
            AddHelpShortcutRow(grid, "- / =", TranslationServer.Translate("Increase / decrease brush strength"));
            AddHelpShortcutRow(grid, "Shift + MouseWheel Scroll", TranslationServer.Translate("Quickly change brush size"));
            AddHelpShortcutRow(grid, "Ctrl + MouseWheel Scroll", TranslationServer.Translate("Quickly change brush strength"));
            AddHelpShortcutRow(grid, "B Key", TranslationServer.Translate("Toggle brush shape (Circle / Square)"));
            AddHelpShortcutRow(grid, "V Key", TranslationServer.Translate("Toggle terrain alignment grid lines"));
            AddHelpShortcutRow(grid, "M Key", TranslationServer.Translate("Toggle blocky sculpt mode"));
            AddHelpShortcutRow(grid, "Shift + MouseWheel Scroll", TranslationServer.Translate("Rotate placement/selected object"));
            AddHelpShortcutRow(grid, "Alt + MouseWheel Scroll", TranslationServer.Translate("Fine-tune object scale size"));
            AddHelpShortcutRow(grid, "Ctrl + G", TranslationServer.Translate("Toggle alignment grid snap placement"));
            AddHelpShortcutRow(grid, "Ctrl + D", TranslationServer.Translate("Duplicate / clone selected object"));
            AddHelpShortcutRow(grid, "Delete", TranslationServer.Translate("Delete / erase selected object"));

            AddHelpSectionHeader(grid, TranslationServer.Translate("GENERAL OPERATIONS"));
            AddHelpShortcutRow(grid, "Ctrl + Z / Ctrl + Y", TranslationServer.Translate("Undo / Redo editor actions"));
            AddHelpShortcutRow(grid, "Ctrl + S / Ctrl + O", TranslationServer.Translate("Save Map File / Load Map File"));
            AddHelpShortcutRow(grid, "Escape Key", TranslationServer.Translate("Close open dialog"));

            var bottomMargin = new MarginContainer();
            bottomMargin.SetAnchorsPreset(LayoutPreset.FullRect);
            bottomMargin.AddThemeConstantOverride("margin_bottom", 20);
            bottomMargin.AddThemeConstantOverride("margin_right", 16);
            panel.AddChild(bottomMargin);

            var btnClose = new Button();
            btnClose.Set("icon_max_width", 0);
            btnClose.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            btnClose.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
            btnClose.CustomMinimumSize = new Vector2(165, 38);
            SetupButton(btnClose, TranslationServer.Translate("CLOSE MANUAL"), () =>
            {
                _helpOverlayPanel.QueueFree();
                _helpOverlayPanel = null;
            }, 13);
            bottomMargin.AddChild(btnClose);
        }

        private void AddHelpSectionHeader(GridContainer grid, string title)
        {
            var lbl = new Label();
            lbl.Text = title;
            lbl.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
            lbl.AddThemeFontSizeOverride("font_size", 12);
            grid.AddChild(lbl);

            var empty = new Control();
            grid.AddChild(empty);
        }

        private void AddHelpShortcutRow(GridContainer grid, string keys, string action)
        {
            var lblKeys = new Label();
            lblKeys.Text = keys;
            lblKeys.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
            lblKeys.AddThemeFontSizeOverride("font_size", 11);
            grid.AddChild(lblKeys);

            var lblAction = new Label();
            lblAction.Text = action;
            lblAction.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
            lblAction.AddThemeFontSizeOverride("font_size", 11);
            grid.AddChild(lblAction);
        }


        private async void CheckCreatorRegistrationAndPrompt()
        {
            if (GetNodeOrNull("AgreementOverlay") != null || GetNodeOrNull("CreatorRegistrationOverlay") != null)
            {
                return;
            }

            var key = GetOrGenerateAuthorshipKey();
            string pubKeyStr = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));

            string seedServerUrl = Realm.Client.Core.GameHost.Instance != null && GodotObject.IsInstanceValid(Realm.Client.Network.LobbyManager.Instance) ? Realm.Client.Network.LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();

            try
            {
                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(3);
                    var res = await httpClient.GetAsync(seedServerUrl + "/api/creators/check/" + Uri.EscapeDataString(pubKeyStr));
                    if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        TryShowCreatorRegistrationDialog(pubKeyStr, key);
                    }
                }
            }
            catch (Exception ex)
            {
                GD.Print($"[MapEditorHUD] Offline or registry server unreachable, skipping creator registration prompt: {ex.Message}");
            }
        }

        private void TryShowCreatorRegistrationDialog(string pubKeyStr, NSec.Cryptography.Key key)
        {
            if (GodotObject.IsInstanceValid(this) && IsInsideTree() && GetNodeOrNull("CreatorRegistrationOverlay") == null && GetNodeOrNull("AgreementOverlay") == null)
            {
                ShowCreatorRegistrationDialog(pubKeyStr, key);
            }
        }

        private void ShowCreatorRegistrationDialog(string pubKeyStr, NSec.Cryptography.Key key)
        {
            if (GetNodeOrNull("CreatorRegistrationOverlay") != null) return;

            var overlay = CreateCreatorRegistrationOverlay();
            var vbox = CreateCreatorRegistrationContainer(overlay);

            var lineEdit = new LineEdit { PlaceholderText = TranslationServer.Translate("Enter Username"), Alignment = HorizontalAlignment.Center };
            vbox.AddChild(lineEdit);

            var errLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            errLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
            errLabel.AddThemeFontSizeOverride("font_size", 11);
            vbox.AddChild(errLabel);

            var btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            btnRow.AddThemeConstantOverride("separation", 12);

            var btnCancel = new Button();
            btnCancel.Set("icon_max_width", 0);
            SetupOptionButton(btnCancel, TranslationServer.Translate("Cancel / Skip"), () => overlay.QueueFree(), 13);
            btnCancel.CustomMinimumSize = new Vector2(130, 36);
            btnRow.AddChild(btnCancel);

            var btnRegister = new Button();
            btnRegister.Set("icon_max_width", 0);
            SetupOptionButton(btnRegister, TranslationServer.Translate("Register"), null, 13);
            btnRegister.CustomMinimumSize = new Vector2(150, 36);
            btnRegister.Pressed += () => OnRegisterCreatorPressed(lineEdit, errLabel, btnRegister, overlay, pubKeyStr, key);
            btnRow.AddChild(btnRegister);

            vbox.AddChild(btnRow);
        }

        private ColorRect CreateCreatorRegistrationOverlay()
        {
            var overlay = new ColorRect { Name = "CreatorRegistrationOverlay", Color = new Color(0, 0, 0, 0.75f), MouseFilter = Control.MouseFilterEnum.Stop, ZIndex = 1000 };
            overlay.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(overlay);
            return overlay;
        }

        private VBoxContainer CreateCreatorRegistrationContainer(ColorRect overlay)
        {
            var center = new CenterContainer();
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            overlay.AddChild(center);

            var panel = new PanelContainer { CustomMinimumSize = new Vector2(480, 260) };
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.12f, 0.12f, 0.18f, 0.98f),
                BorderColor = UIStyle.ColorCyanGlow,
                BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
                CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6
            };
            panel.AddThemeStyleboxOverride("panel", style);
            center.AddChild(panel);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_top", 18); margin.AddThemeConstantOverride("margin_bottom", 18);
            margin.AddThemeConstantOverride("margin_left", 20); margin.AddThemeConstantOverride("margin_right", 20);
            panel.AddChild(margin);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 12);
            margin.AddChild(vbox);

            var title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            UIStyle.ApplyTitle(title, TranslationServer.Translate("REGISTER CREATOR PROFILE"), 18);
            title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
            vbox.AddChild(title);

            var desc = new Label
            {
                Text = TranslationServer.Translate("A new cryptographic key pair has been generated for your machine. Please choose a unique display name to lock to this key."),
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            desc.AddThemeFontSizeOverride("font_size", 12);
            desc.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
            vbox.AddChild(desc);

            return vbox;
        }

        private async void OnRegisterCreatorPressed(LineEdit lineEdit, Label errLabel, Button btnRegister, ColorRect overlay, string pubKeyStr, NSec.Cryptography.Key key)
        {
            string username = lineEdit.Text.Trim();
            if (!NameNormalizationHelper.ValidateUsername(username, out var validationError))
            {
                errLabel.Text = TranslationServer.Translate(validationError ?? "Invalid username.");
                return;
            }

            btnRegister.Disabled = true;
            errLabel.Text = TranslationServer.Translate("Registering...");

            try
            {
                await ProcessCreatorRegistration(username, pubKeyStr, key, errLabel, overlay, btnRegister);
            }
            catch (Exception ex)
            {
                errLabel.Text = TranslationServer.Translate("Error: ") + ex.Message;
                btnRegister.Disabled = false;
            }
        }

        private async Task ProcessCreatorRegistration(string username, string pubKeyStr, NSec.Cryptography.Key key, Label errLabel, ColorRect overlay, Button btnRegister)
        {
            byte[] payloadBytes = System.Text.Encoding.UTF8.GetBytes(username + ":" + pubKeyStr);
            byte[] sigBytes = SignatureAlgorithm.Ed25519.Sign(key, payloadBytes);
            string signatureStr = Convert.ToBase64String(sigBytes);

            var regPayload = new { Username = username, PublicKey = pubKeyStr, Signature = signatureStr };
            string seedServerUrl = Realm.Client.Core.GameHost.Instance != null && GodotObject.IsInstanceValid(Realm.Client.Network.LobbyManager.Instance) ? Realm.Client.Network.LobbyManager.Instance.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl();

            using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var content = new StringContent(JsonSerializer.Serialize(regPayload), System.Text.Encoding.UTF8, "application/json");
            var res = await httpClient.PostAsync(seedServerUrl + "/api/creators/register", content);

            if (res.IsSuccessStatusCode)
            {
                string keyDir = ProjectSettings.GlobalizePath("user://appdata/keys/");
                AuthorshipKeyHelper.UpdateUserName(keyDir, username);
                overlay.QueueFree();
                ShowFeedback(TranslationServer.Translate("Creator registered successfully!"));
            }
            else
            {
                await HandleRegistrationFailure(res, errLabel);
                btnRegister.Disabled = false;
            }
        }

        private async Task HandleRegistrationFailure(System.Net.Http.HttpResponseMessage res, Label errLabel)
        {
            string errText = await res.Content.ReadAsStringAsync();
            try
            {
                var errDoc = JsonDocument.Parse(errText);
                if (errDoc.RootElement.TryGetProperty("Message", out var msgProp))
                {
                    errLabel.Text = msgProp.GetString();
                }
                else
                {
                    errLabel.Text = TranslationServer.Translate("Registration failed.");
                }
            }
            catch
            {
                errLabel.Text = TranslationServer.Translate("Registration failed: ") + errText;
            }
        }

        public void UpdateMirrorButtonText() => UpdateMirrorDropdowns();

        private void SetupSpokesDropdown(OptionButton opt, Action<int> onSpokesSelected, string tooltip = "")
        {
            if (opt == null) return;
            opt.Clear();
            opt.Set("icon_max_width", 0);
            opt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            opt.ClipText = true;

            for (int i = 0; i < SpokeCountOptions.Length; i++)
            {
                int count = SpokeCountOptions[i];
                opt.AddItem(string.Format(TranslationServer.Translate($"{UnicodeIcons.COMPASS} SPOKES: {0}"), count), count);
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

        private void UpdateSpokesDropdownSelection(OptionButton opt, int currentSpokes)
        {
            if (opt == null) return;
            for (int i = 0; i < opt.ItemCount; i++)
            {
                if (opt.GetItemId(i) == currentSpokes)
                {
                    if (opt.Selected != i)
                    {
                        opt.Selected = i;
                    }
                    break;
                }
            }
        }

        public void UpdateSymmetryFoldsButtonText() => UpdateSymmetrySpokesDropdowns();

        public void UpdatePolarRingSpacingButtonText()
        {
            if (_optPolarRingSpacing != null && Realm.Client.Core.GameHost.Instance != null)
            {
                float current = Realm.Client.Core.GameHost.Instance.EditorPolarRingSpacing;
                for (int i = 0; i < _optPolarRingSpacing.ItemCount; i++)
                {
                    if (Mathf.IsEqualApprox((float)_optPolarRingSpacing.GetItemId(i), current))
                    {
                        if (_optPolarRingSpacing.Selected != i)
                        {
                            _optPolarRingSpacing.Selected = i;
                        }
                        break;
                    }
                }
            }
        }

        public void UpdatePolarRadialStepButtonText()
        {
            if (_optPolarRadialStep != null && Realm.Client.Core.GameHost.Instance != null)
            {
                int spokes = Realm.Client.Core.GameHost.Instance.EditorMirrorMode == MirrorMode.Rotational
                    ? Realm.Client.Core.GameHost.Instance.EditorSymmetryFolds
                    : Realm.Client.Core.GameHost.Instance.EditorPolarSpokeFolds;

                UpdateSpokesDropdownSelection(_optPolarRadialStep, spokes);

                if (Realm.Client.Core.GameHost.Instance.EditorMirrorMode == MirrorMode.Rotational)
                {
                    _optPolarRadialStep.TooltipText = TranslationServer.Translate("Spokes are automatically locked to Rotational N-Fold symmetry");
                }
                else
                {
                    _optPolarRadialStep.TooltipText = TranslationServer.Translate("Select radial spoke count (2, 3, 4, 5, 6, 8, 12, 16)");
                }
            }
        }

        private void RebuildHUDLayout()
        {
        }

        private void UpdateCardScrollState(Control contentControl, float maxHeight = 300f, bool allowExpandBtn = true)
        {
            if (contentControl == null) return;
            ScrollContainer scroll = contentControl.GetNodeOrNull<ScrollContainer>("CardScroll");
            if (scroll == null) return;

            var targetInner = scroll.GetNodeOrNull<VBoxContainer>("InnerVBox");
            if (targetInner != null)
            {
                targetInner.ForceUpdateTransform();
                float minH = targetInner.GetCombinedMinimumSize().Y;
                Button expandBtn = contentControl.GetNodeOrNull<Button>("BtnExpandHeight");

                if (!allowExpandBtn)
                {
                    if (expandBtn != null) expandBtn.Visible = false;
                    scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
                    scroll.CustomMinimumSize = new Vector2(245, 0);
                    return;
                }

                if (minH > maxHeight)
                {
                    if (expandBtn != null) expandBtn.Visible = true;
                }
                else
                {
                    if (expandBtn != null) expandBtn.Visible = false;
                    scroll.CustomMinimumSize = new Vector2(245, minH);
                }
            }
        }

        private void UpdateModuleSwitchButtons()
        {
            if (_optModule != null)
            {
                _optModule.Selected = (int)_activeModule;
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

        private void ApplyCardPanelStyle(Control accordionNode)
        {
            if (accordionNode == null) return;

            var cardStyle = new StyleBoxFlat();
            cardStyle.BgColor = new Color(0.12f, 0.11f, 0.10f, 0.94f);
            cardStyle.BorderColor = new Color(0.38f, 0.32f, 0.22f, 0.85f);
            cardStyle.SetBorderWidthAll(1);
            cardStyle.CornerRadiusTopLeft = 5;
            cardStyle.CornerRadiusTopRight = 5;
            cardStyle.CornerRadiusBottomLeft = 5;
            cardStyle.CornerRadiusBottomRight = 5;
            cardStyle.ContentMarginLeft = 8;
            cardStyle.ContentMarginRight = 8;
            cardStyle.ContentMarginTop = 6;
            cardStyle.ContentMarginBottom = 8;

            Panel bgPanel = accordionNode.GetNodeOrNull<Panel>("CardBG");
            if (bgPanel == null)
            {
                bgPanel = new Panel();
                bgPanel.Name = "CardBG";
                bgPanel.ShowBehindParent = true;
                bgPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
                bgPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                accordionNode.AddChild(bgPanel);
                accordionNode.MoveChild(bgPanel, 0);
            }
            bgPanel.AddThemeStyleboxOverride("panel", cardStyle);
        }

        private void StyleContentBox(Control contentControl)
        {
            if (contentControl == null) return;

            var contentStyle = new StyleBoxFlat();
            contentStyle.BgColor = new Color(0.10f, 0.09f, 0.08f, 0.95f);
            contentStyle.BorderColor = new Color(0.38f, 0.32f, 0.22f, 0.85f);
            contentStyle.SetBorderWidthAll(1);
            contentStyle.BorderWidthTop = 0;
            contentStyle.CornerRadiusBottomLeft = 4;
            contentStyle.CornerRadiusBottomRight = 4;
            contentStyle.ContentMarginLeft = 6;
            contentStyle.ContentMarginRight = 6;
            contentStyle.ContentMarginTop = 6;
            contentStyle.ContentMarginBottom = 8;

            if (contentControl is PanelContainer pc)
            {
                pc.AddThemeStyleboxOverride("panel", contentStyle);
            }
            else if (contentControl is VBoxContainer vbox)
            {
                vbox.AddThemeConstantOverride("separation", 2);

                Panel bgPanel = vbox.GetNodeOrNull<Panel>("ContentBG");
                if (bgPanel == null)
                {
                    bgPanel = new Panel();
                    bgPanel.Name = "ContentBG";
                    bgPanel.ShowBehindParent = true;
                    bgPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
                    bgPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                    vbox.AddChild(bgPanel);
                    vbox.MoveChild(bgPanel, 0);
                }
                bgPanel.AddThemeStyleboxOverride("panel", contentStyle);
            }
        }

        private void StyleRowButton(Button btn)
        {
            if (btn == null) return;
            btn.Flat = false;
            btn.CustomMinimumSize = new Vector2(0, 32);

            var rowNormal = new StyleBoxFlat();
            rowNormal.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.4f);
            rowNormal.BorderColor = new Color(0.24f, 0.21f, 0.17f, 0.5f);
            rowNormal.BorderWidthBottom = 1;
            rowNormal.ContentMarginLeft = 10;
            rowNormal.ContentMarginRight = 10;

            var rowHover = new StyleBoxFlat();
            rowHover.BgColor = new Color(0.25f, 0.22f, 0.18f, 0.9f);
            rowHover.BorderColor = UIStyle.ColorGold;
            rowHover.BorderWidthBottom = 1;
            rowHover.ContentMarginLeft = 10;
            rowHover.ContentMarginRight = 10;

            var rowPressed = new StyleBoxFlat();
            rowPressed.BgColor = new Color(0.30f, 0.26f, 0.20f, 0.95f);
            rowPressed.BorderColor = UIStyle.ColorGold;
            rowPressed.SetBorderWidthAll(1);
            rowPressed.ContentMarginLeft = 10;
            rowPressed.ContentMarginRight = 10;

            btn.AddThemeStyleboxOverride("normal", rowNormal);
            btn.AddThemeStyleboxOverride("hover", rowHover);
            btn.AddThemeStyleboxOverride("pressed", rowPressed);
            btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            btn.AddThemeFontSizeOverride("font_size", 12);
            btn.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.82f));
            btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
            btn.Alignment = HorizontalAlignment.Left;
        }

        private Control GetContentTarget(Control contentControl)
        {
            if (contentControl == null) return null;
            var inner = contentControl.GetNodeOrNull<Control>("CardScroll/InnerVBox");
            return inner ?? contentControl;
        }


        private void SetupCardScrollContainer(Control contentControl, float maxHeight = 300f, bool allowExpandBtn = true)
        {
            if (contentControl == null) return;

            contentControl.CustomMinimumSize = new Vector2(245, 0);

            ScrollContainer scroll = contentControl.GetNodeOrNull<ScrollContainer>("CardScroll");
            if (scroll == null)
            {
                scroll = CreateCardScrollContainer(allowExpandBtn);
                PopulateCardScrollContainer(contentControl, scroll);
                contentControl.AddChild(scroll);
            }

            var targetInner = scroll.GetNodeOrNull<VBoxContainer>("InnerVBox");
            if (targetInner != null)
            {
                targetInner.ForceUpdateTransform();
                float minH = targetInner.GetCombinedMinimumSize().Y;
                Button expandBtn = contentControl.GetNodeOrNull<Button>("BtnExpandHeight");

                if (!allowExpandBtn)
                {
                    DisableScrollContainerExpansion(scroll, expandBtn);
                    return;
                }

                if (minH > maxHeight)
                {
                    scroll.CustomMinimumSize = new Vector2(245, maxHeight);
                    if (expandBtn != null) expandBtn.Visible = true;
                }
                else
                {
                    scroll.CustomMinimumSize = new Vector2(245, minH);
                    if (expandBtn != null) expandBtn.Visible = false;
                }
            }
        }

        private ScrollContainer CreateCardScrollContainer(bool allowExpandBtn)
        {
            var scroll = new ScrollContainer();
            scroll.Name = "CardScroll";
            scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
            scroll.VerticalScrollMode = allowExpandBtn ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.Disabled;
            scroll.CustomMinimumSize = new Vector2(245, 0);
            return scroll;
        }

        private void PopulateCardScrollContainer(Control contentControl, ScrollContainer scroll)
        {
            var innerVBox = new VBoxContainer();
            innerVBox.Name = "InnerVBox";
            innerVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            innerVBox.AddThemeConstantOverride("separation", 2);

            var children = new global::Godot.Collections.Array<Node>(contentControl.GetChildren());
            foreach (Node child in children)
            {
                if (child is Panel && child.Name == "ContentBG") continue;
                contentControl.RemoveChild(child);
                innerVBox.AddChild(child);
            }

            scroll.AddChild(innerVBox);
        }

        private void DisableScrollContainerExpansion(ScrollContainer scroll, Button expandBtn)
        {
            if (expandBtn != null) expandBtn.Visible = false;
            scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
            scroll.CustomMinimumSize = new Vector2(245, 0);
        }

        private void StyleSubContainer(Control boxNode, string headerText = null)
        {
            if (boxNode == null) return;

            var boxStyle = new StyleBoxFlat();
            boxStyle.BgColor = new Color(0.13f, 0.12f, 0.10f, 0.7f);
            boxStyle.BorderColor = new Color(0.32f, 0.27f, 0.20f, 0.6f);
            boxStyle.SetBorderWidthAll(1);
            boxStyle.CornerRadiusTopLeft = 4;
            boxStyle.CornerRadiusTopRight = 4;
            boxStyle.CornerRadiusBottomLeft = 4;
            boxStyle.CornerRadiusBottomRight = 4;
            boxStyle.ContentMarginLeft = 10;
            boxStyle.ContentMarginRight = 14;
            boxStyle.ContentMarginTop = 6;
            boxStyle.ContentMarginBottom = 6;

            if (boxNode is PanelContainer pc)
            {
                pc.AddThemeStyleboxOverride("panel", boxStyle);
            }
            else if (boxNode is VBoxContainer vbox)
            {
                vbox.AddThemeConstantOverride("separation", 4);
                Panel bgPanel = vbox.GetNodeOrNull<Panel>("SubBoxBG");
                if (bgPanel == null)
                {
                    bgPanel = new Panel();
                    bgPanel.Name = "SubBoxBG";
                    bgPanel.ShowBehindParent = true;
                    bgPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
                    bgPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                    vbox.AddChild(bgPanel);
                    vbox.MoveChild(bgPanel, 0);
                }
                bgPanel.AddThemeStyleboxOverride("panel", boxStyle);
            }

            if (!string.IsNullOrEmpty(headerText))
            {
                Label lblHeader = boxNode.GetNodeOrNull<Label>("Header/LblSubTitle");
                if (lblHeader == null)
                {
                    var headerBox = boxNode.GetNodeOrNull<Control>("Header");
                    if (headerBox != null)
                    {
                        lblHeader = headerBox.GetNodeOrNull<Label>("LblSubTitle");
                    }
                }
                if (lblHeader != null)
                {
                    lblHeader.Text = headerText.ToUpperInvariant();
                    lblHeader.AddThemeFontSizeOverride("font_size", 10);
                    lblHeader.AddThemeColorOverride("font_color", UIStyle.ColorGold);
                }
            }
        }

        private void StyleValueBadge(Label lbl)
        {
            if (lbl == null) return;
            var badgeStyle = new StyleBoxFlat();
            badgeStyle.BgColor = new Color(0.18f, 0.16f, 0.13f, 0.95f);
            badgeStyle.BorderColor = UIStyle.ColorGold;
            badgeStyle.SetBorderWidthAll(1);
            badgeStyle.CornerRadiusTopLeft = 3;
            badgeStyle.CornerRadiusTopRight = 3;
            badgeStyle.CornerRadiusBottomLeft = 3;
            badgeStyle.CornerRadiusBottomRight = 3;
            badgeStyle.ContentMarginLeft = 6;
            badgeStyle.ContentMarginRight = 6;
            badgeStyle.ContentMarginTop = 2;
            badgeStyle.ContentMarginBottom = 2;

            lbl.AddThemeStyleboxOverride("normal", badgeStyle);
            lbl.AddThemeFontSizeOverride("font_size", 11);
            lbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
            lbl.HorizontalAlignment = HorizontalAlignment.Center;
            lbl.VerticalAlignment = VerticalAlignment.Center;
        }

        private void StyleCheckBoxRow(CheckBox chk)
        {
            if (chk == null) return;
            chk.CustomMinimumSize = new Vector2(0, 28);
            chk.AddThemeFontSizeOverride("font_size", 11);
            chk.AddThemeColorOverride("font_color", new Color(0.88f, 0.84f, 0.78f));
            chk.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
            chk.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
        }

        private void StyleMapEditorTopButton(Button btn)
        {
            if (btn == null) return;
            var tex = GD.Load<Texture2D>("res://Assets/UI/map_editor_button.png");
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


        private void MakeCardDraggable(Control cardNode, Button headerBtn, Control contentControl = null, string titleText = null)
        {
            if (cardNode == null || headerBtn == null) return;

            var data = new CardDragData
            {
                CardNode = cardNode,
                HeaderButton = headerBtn,
                ContentControl = contentControl,
                TitleText = titleText
            };
            _cardDragMap[cardNode] = data;

            headerBtn.GuiInput += (@event) => HandleCardGuiInput(@event, cardNode, headerBtn, contentControl, titleText, data);
        }

        private void HandleCardGuiInput(InputEvent @event, Control cardNode, Button headerBtn, Control contentControl, string titleText, CardDragData data)
        {
            if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                HandleCardMouseButtonInput(mb, cardNode, headerBtn, contentControl, titleText, data);
            }
            else if (@event is InputEventMouseMotion mm && data.IsDragging)
            {
                HandleCardMouseMotionInput(mm, cardNode, data);
            }
        }

        private void HandleCardMouseButtonInput(InputEventMouseButton mb, Control cardNode, Button headerBtn, Control contentControl, string titleText, CardDragData data)
        {
            if (mb.Pressed)
            {
                if (mb.DoubleClick)
                {
                    data.IsDragging = false;
                    data.HasMovedSincePress = false;
                    ResetSingleCardPosition(cardNode);
                    ShowFeedback(TranslationServer.Translate("Card position reset to panel layout."));
                    return;
                }
                data.IsDragging = true;
                data.HasMovedSincePress = false;
                data.DragStartMousePos = mb.GlobalPosition;
                data.CardStartPos = cardNode.GlobalPosition;
            }
            else
            {
                if (data.IsDragging)
                {
                    bool moved = data.HasMovedSincePress;
                    data.IsDragging = false;
                    data.HasMovedSincePress = false;

                    if (!moved && contentControl != null)
                    {
                        ToggleAccordionState(headerBtn, contentControl, titleText);
                    }
                }
            }
        }

        private void HandleCardMouseMotionInput(InputEventMouseMotion mm, Control cardNode, CardDragData data)
        {
            Vector2 delta = mm.GlobalPosition - data.DragStartMousePos;
            if (!data.HasMovedSincePress && delta.LengthSquared() > 16.0f)
            {
                data.HasMovedSincePress = true;
                if (!cardNode.TopLevel)
                {
                    cardNode.TopLevel = true;
                }
            }
            if (data.HasMovedSincePress)
            {
                if (!cardNode.TopLevel)
                {
                    cardNode.TopLevel = true;
                }
                cardNode.GlobalPosition = data.CardStartPos + delta;
            }
        }

        private void ExpandAccordion(Button headerBtn, Control contentControl, string titleText)
        {
            if (headerBtn == null || contentControl == null) return;
            if (contentControl.Visible) return;
            ToggleAccordionState(headerBtn, contentControl, titleText);
        }


        private void ToggleAccordionState(Button headerBtn, Control contentControl, string titleText)
        {
            if (headerBtn == null || contentControl == null) return;
            contentControl.Visible = !contentControl.Visible;
            if (!string.IsNullOrEmpty(titleText))
            {
                string upperTitle = TranslationServer.Translate(titleText).ToString().ToUpperInvariant();
                headerBtn.Text = upperTitle + (contentControl.Visible ? $"  {UnicodeIcons.CARET_DOWN}" : $"  {UnicodeIcons.CARET_RIGHT}");
            }

            QueueSortCardContainers(headerBtn);
            QueueSortVBoxContainers();

            Realm.Client.UI.UIManager.Instance?.PlayClickSound();
        }

        private void QueueSortCardContainers(Button headerBtn)
        {
            var cardParent = headerBtn.GetParent() as Control;
            if (cardParent != null)
            {
                cardParent.ForceUpdateTransform();
                (cardParent as Container)?.QueueSort();

                var sidebarContainer = cardParent.GetParent() as Container;
                if (sidebarContainer != null)
                {
                    sidebarContainer.ForceUpdateTransform();
                    sidebarContainer.QueueSort();
                }
            }
        }

        private void QueueSortVBoxContainers()
        {
            var leftVBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
            leftVBox?.ForceUpdateTransform();
            leftVBox?.QueueSort();

            var rightVBox = GetNodeOrNull<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer");
            rightVBox?.ForceUpdateTransform();
            rightVBox?.QueueSort();
        }

        private void ResetSingleCardPosition(Control cardNode)
        {
            if (cardNode == null) return;
            if (_cardDragMap.TryGetValue(cardNode, out var data))
            {
                data.IsDragging = false;
                data.HasMovedSincePress = false;
            }
            cardNode.TopLevel = false;
            cardNode.Position = Vector2.Zero;

            var parentContainer = cardNode.GetParent() as Container;
            if (parentContainer != null)
            {
                parentContainer.QueueSort();
            }
            var leftVBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
            leftVBox?.QueueSort();

            var rightVBox = GetNodeOrNull<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer");
            rightVBox?.QueueSort();
        }

        public void ResetAllPanelPositions()
        {
            ResetSingleCardPosition(_accordionFile);
            ResetSingleCardPosition(_accordionViewport);
            ResetSingleCardPosition(_accordionTool);
            ResetSingleCardPosition(_accordionBrush);
            ResetSingleCardPosition(_accordionToolSettings);
            ResetSingleCardPosition(_accordionPlacement);
            ResetSingleCardPosition(_accordionInspector);

            var leftVBox = GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
            leftVBox?.QueueSort();

            var rightVBox = GetNodeOrNull<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer");
            rightVBox?.QueueSort();

            ShowFeedback(TranslationServer.Translate("All panel positions reset to default layout."));
        }

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

        private void SafeReparent(Node node, Node newParent)
        {
            if (node == null || newParent == null) return;
            var oldParent = node.GetParent();
            if (oldParent == newParent) return;
            oldParent?.RemoveChild(node);
            newParent.AddChild(node);
        }



        private void SetupMinimap()
        {
            MinimapHelper.SetupMinimapBackground(_minimapArea);
        }

        public void RegenerateMinimap()
        {
            _minimapController?.RegenerateMinimap();
        }

        public void SetScaleDialogTargets(int width, int depth)
        {
            _scaleDialogTargetWidth = width;
            _scaleDialogTargetDepth = depth;
        }

        private void UpdateScaleDialogLabels()
        {
            if (_lblScalePreviewWidth != null)
                _lblScalePreviewWidth.Text = $"W: {_scaleDialogTargetWidth}";
            if (_lblScalePreviewHeight != null)
                _lblScalePreviewHeight.Text = $"H: {_scaleDialogTargetDepth}";
        }

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

            btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
            btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
            btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
            btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

            foreach (Node child in btn.GetChildren())
            {
                if (child is TextureRect) child.QueueFree();
            }

            var iconRect = new TextureRect();
            iconRect.Name = "IconRect";
            iconRect.Texture = GD.Load<Texture2D>(iconPath);
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
                Realm.Client.UI.UIManager.Instance?.PlayClickSound();
                onClick?.Invoke();
            };
        }

        private void SetupOptionButton(Button btn, string text, Action onClick, int fontSize = 11, string tooltip = "")
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
            btn.MouseFilter = Control.MouseFilterEnum.Stop;
            if (!string.IsNullOrEmpty(tooltip))
            {
                btn.TooltipText = TranslationServer.Translate(tooltip);
            }
            if (onClick != null)
            {
                btn.Pressed += () =>
                {
                    Realm.Client.UI.UIManager.Instance?.PlayClickSound();
                    onClick?.Invoke();
                };
            }
        }

        private void StyleOptionButtonPopup(OptionButton optBtn)
        {
            if (optBtn == null) return;
            var font = GetFontAwesomeFont();
            if (font != null)
            {
                optBtn.AddThemeFontOverride("font", font);
            }
            StylePopupMenu(optBtn.GetPopup());
        }

        private void StylePopupMenu(PopupMenu popup)
        {
            if (popup == null) return;
            var font = GetFontAwesomeFont();
            if (font != null)
            {
                popup.AddThemeFontOverride("font", font);
            }
            popup.AddThemeFontSizeOverride("font_size", 12);

            var popupStyle = new StyleBoxFlat();
            popupStyle.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
            popupStyle.BorderColor = UIStyle.ColorGold;
            popupStyle.SetBorderWidthAll(1);
            popupStyle.CornerRadiusTopLeft = 4;
            popupStyle.CornerRadiusTopRight = 4;
            popupStyle.CornerRadiusBottomLeft = 4;
            popupStyle.CornerRadiusBottomRight = 4;
            popupStyle.ContentMarginLeft = 8;
            popupStyle.ContentMarginRight = 8;
            popupStyle.ContentMarginTop = 6;
            popupStyle.ContentMarginBottom = 6;

            popup.AddThemeStyleboxOverride("panel", popupStyle);
            popup.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.82f));
            popup.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
        }

        private void StyleOptionButtonsInContainer(Control container)
        {
            if (container == null) return;
            foreach (Node child in container.GetChildren())
            {
                if (child is OptionButton optBtn)
                {
                    StyleOptionButtonPopup(optBtn);
                }
                else if (child is Button btn && !btn.Name.ToString().StartsWith("BtnHeader"))
                {
                    if (btn.CustomMinimumSize.X != 52 || btn.CustomMinimumSize.Y != 52)
                    {
                        btn.AddThemeStyleboxOverride("normal", UIStyle.CreateOptionButtonNormal());
                        btn.AddThemeStyleboxOverride("hover", UIStyle.CreateOptionButtonHover());
                        btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateOptionButtonPressed());
                        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
                        btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.82f));
                        btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
                    }
                }
                else if (child is Control subContainer)
                {
                    StyleOptionButtonsInContainer(subContainer);
                }
            }
        }


        public void SetupTextureSwatches(bool connectEvents = false)
        {
            _swatchTextureCache.Clear();
            _swatchPaths.Clear();
            _swatchDisplayNames.Clear();
            _swatchColors.Clear();

            try
            {
                string wsPath = GetActiveWorkspacePathFromSettings();
                var metadata = Realm.Shared.Services.MapFileService.LoadMetadata(wsPath);
                var slots = Realm.Client.Utils.TextureSwatchSlots.ResolveSlots(metadata?.Textures, wsPath);

                PopulateSwatchData(slots, wsPath);
                RebuildSwatchesUI(slots, connectEvents);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapEditorHUD] Error in SetupTextureSwatches: {ex.Message}");
            }
            
            UpdateTextureLabels();
        }

        private string GetActiveWorkspacePathFromSettings()
        {
            return string.IsNullOrEmpty(_tempWorkspacePath)
                ? ProjectSettings.GlobalizePath(TempWorkspaceGodotPath)
                : _tempWorkspacePath;
        }

        private void PopulateSwatchData(Realm.Client.Utils.SwatchSlotInfo[] slots, string wsPath)
        {
            for (int i = 0; i < Realm.Client.Utils.TextureSwatchSlots.MaxSlots; i++)
            {
                var slot = slots[i];
                if (!slot.IsFiller && !string.IsNullOrEmpty(slot.BaseName))
                {
                    string resolvedPath = ResolveTexturePath(slot, wsPath);
                    _swatchDisplayNames.Add(slot.BaseName);
                    _swatchPaths.Add(resolvedPath);
                    _swatchColors.Add(new Color(0.6f, 0.6f, 0.6f));
                }
                else
                {
                    _swatchDisplayNames.Add($"Slot {i} (Empty)");
                    _swatchPaths.Add("");
                    _swatchColors.Add(new Color(0.2f, 0.2f, 0.2f, 0.5f));
                }
            }
        }

        private string ResolveTexturePath(Realm.Client.Utils.SwatchSlotInfo slot, string wsPath)
        {
            string rtexName = GetRtexName(slot);

            string[] pathsToTry = {
                System.IO.Path.Combine(wsPath, "Assets", "textures", rtexName),
                System.IO.Path.Combine(wsPath, rtexName)
            };

            foreach (var path in pathsToTry)
            {
                if (System.IO.File.Exists(path)) return path;
            }

            string? found = TryFindRtexPath(rtexName);
            if (!string.IsNullOrEmpty(found)) return found;

            return string.Empty;
        }

        private string GetRtexName(Realm.Client.Utils.SwatchSlotInfo slot)
        {
            string rtexName = slot.MetadataNode?.TexturePath ?? slot.FileName ?? (slot.BaseName + ".rtex");
            if (!rtexName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
            {
                rtexName += ".rtex";
            }
            return System.IO.Path.GetFileName(rtexName);
        }

        private string? TryFindRtexPath(string rtexName)
        {
            string? found = PathUtils.FindPath($"Assets/textures/{rtexName}");
            if (!string.IsNullOrEmpty(found) && System.IO.File.Exists(found)) return found;

            string? foundTemplate = PathUtils.FindPath($"MapTemplate/Assets/textures/{rtexName}");
            if (!string.IsNullOrEmpty(foundTemplate) && System.IO.File.Exists(foundTemplate)) return foundTemplate;

            return null;
        }

        private void RebuildSwatchesUI(Realm.Client.Utils.SwatchSlotInfo[] slots, bool connectEvents)
        {
            if (_gridSwatches == null) return;

            EnsureScrollSwatchesContainerExists();
            ConfigureGridSwatchesContainer();
            CreateSwatchButtons(slots, connectEvents);
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
                _scrollSwatches.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                _scrollSwatches.AddChild(_gridSwatches);
                parentVBox.AddChild(_scrollSwatches);
                parentVBox.MoveChild(_scrollSwatches, gridIndex);
            }
        }

        private void ConfigureGridSwatchesContainer()
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

        private void CreateSwatchButtons(Realm.Client.Utils.SwatchSlotInfo[] slots, bool connectEvents)
        {
            int visibleCount = 0;
            for (int i = 0; i < Realm.Client.Utils.TextureSwatchSlots.MaxSlots; i++)
            {
                var slot = slots[i];
                var btn = CreateSwatchButton(slot);
                
                if (!slot.IsFiller && !string.IsNullOrEmpty(slot.BaseName))
                {
                    btn.Visible = true;
                    visibleCount++;
                    SetupSwatchVisuals(btn, slot.SlotIndex);
                }
                else
                {
                    btn.Visible = false;
                }
                
                btn.GuiInput += (@event) => HandleSwatchInput(@event, slot.SlotIndex, btn);

                _swatchButtons.Add(btn);
                _gridSwatches.AddChild(btn);
            }
            
            UpdateSwatchesScrollHeight(visibleCount);
        }

        private Button CreateSwatchButton(Realm.Client.Utils.SwatchSlotInfo slot)
        {
            var btn = new Button();
            btn.Name = $"Swatch{slot.SlotIndex + 1}";
            btn.Flat = false;
            btn.ExpandIcon = true;
            btn.FocusMode = FocusModeEnum.None;
            btn.CustomMinimumSize = new Vector2(40, 40);
            btn.Set("icon_max_width", 0);
            btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
            btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
            btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
            return btn;
        }

        private void SetupSwatchVisuals(Button btn, int slotIndex)
        {
            Texture2D tex = GetSwatchTexture(slotIndex);
            if (tex != null)
            {
                var texRect = new TextureRect();
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
                colorBox.Color = _swatchColors[slotIndex];
                colorBox.MouseFilter = MouseFilterEnum.Ignore;
                colorBox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                colorBox.GrowHorizontal = GrowDirection.Both;
                colorBox.GrowVertical = GrowDirection.Both;
                btn.AddChild(colorBox);
            }
            btn.TooltipText = $"{slotIndex}: {_swatchDisplayNames[slotIndex]}";
        }

        private void HandleSwatchInput(InputEvent @event, int slotIndex, Button btn)
        {
            if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
            {
                if (mouseEvent.ButtonIndex == MouseButton.Left)
                {
                    bool isShift = Input.IsKeyPressed(global::Godot.Key.Shift);
                    bool cliffChecked = _chkApplyCliffTexture != null && _chkApplyCliffTexture.ButtonPressed;
                    bool groundUnchecked = _chkApplyGroundTexture == null || !_chkApplyGroundTexture.ButtonPressed;
                    
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
        
        private void UpdateSwatchesScrollHeight(int visibleCount)
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

        private string GetSwatchName(Color color)
        {
            float epsilon = 0.01f;
            for (int i = 1; i <= _swatchColors.Count; i++)
            {
                Color c = GetSwatchColor(i);
                if (Mathf.Abs(color.R - c.R) < epsilon &&
                    Mathf.Abs(color.G - c.G) < epsilon &&
                    Mathf.Abs(color.B - c.B) < epsilon)
                {
                    string texName = (i >= 1 && i <= _swatchDisplayNames.Count) ? _swatchDisplayNames[i - 1] : "Unknown";
                    return texName;
                }
            }
            return "Custom";
        }

        private Color GetSwatchColor(int index)
        {
            if (index >= 1 && index <= _swatchColors.Count)
            {
                return _swatchColors[index - 1];
            }
            return new Color(1, 1, 1);
        }


        public void Set3DInteractionActive(bool active)
        {
            if (_is3DInteractionActive == active) return;
            _is3DInteractionActive = active;

            if (!Realm.Client.UI.MapEditor.EditorSettingsDialog.CurrentSettings.HideHudDuringToolUsage && active)
            {
                return;
            }

            UpdateMouseFilters(active);
            AnimateHudFade(active);
        }

        private void UpdateMouseFilters(bool active)
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
                foreach (var card in _cardDragMap.Keys)
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
            float targetAlpha = (active && Realm.Client.UI.MapEditor.EditorSettingsDialog.CurrentSettings.HideHudDuringToolUsage) ? 0.0f : 1.0f;
            float duration = 0.35f;

            TweenHudElement(_topLeftBox, targetAlpha, duration);
            TweenHudElement(_panelLeft, targetAlpha, duration);
            TweenHudElement(_panelRight, targetAlpha, duration);
            TweenHudElement(_topBar, targetAlpha, duration);
            TweenHudElement(_panelMeasurementHUD, targetAlpha, duration);
            
            foreach (var card in _cardDragMap.Keys)
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
            bool blockModeEnabled = (_chkBlockMode != null && _chkBlockMode.Visible && _chkBlockMode.ButtonPressed);
            var tool = Realm.Client.Core.GameHost.Instance != null ? Realm.Client.Core.GameHost.Instance.ActiveEditorTool : Realm.Client.Core.GameHost.EditorTool.Lower;

            if (_stepBox != null)
            {
                _stepBox.Visible = blockModeEnabled && (tool == Realm.Client.Core.GameHost.EditorTool.Raise || tool == Realm.Client.Core.GameHost.EditorTool.Lower);
            }

            if (_heightBox != null)
            {
                _heightBox.Visible = (tool == Realm.Client.Core.GameHost.EditorTool.Height);
            }
        }

        private void ShowAgreementModal(Action onAccepted = null)
        {
            var overlay = new ColorRect();
            overlay.Name = "AgreementOverlay";
            overlay.Color = new Color(0, 0, 0, 0.75f);
            overlay.SetAnchorsPreset(LayoutPreset.FullRect);
            overlay.MouseFilter = Control.MouseFilterEnum.Stop;
            overlay.ZIndex = 1000;
            AddChild(overlay);

            var center = new CenterContainer();
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            overlay.AddChild(center);

            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            panel.CustomMinimumSize = new Vector2(1024, 762);
            center.AddChild(panel);

            var bgTexRect = new TextureRect();
            bgTexRect.Texture = GD.Load<Texture2D>("res://Assets/UI/map_editor_agreement.png");
            bgTexRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            bgTexRect.StretchMode = TextureRect.StretchModeEnum.Scale;
            bgTexRect.SetAnchorsPreset(LayoutPreset.FullRect);
            panel.AddChild(bgTexRect);

            var contentOverlay = new Control();
            contentOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
            panel.AddChild(contentOverlay);

            // Top Title Header
            var lblTitle = new Label();
            UIStyle.ApplyTitle(lblTitle, "Realm Creator Agreement", 20);
            lblTitle.Text = TranslationServer.Translate("Realm Creator Agreement").ToString().ToUpperInvariant();
            lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
            lblTitle.Position = new Vector2(140, 48);
            lblTitle.Size = new Vector2(744, 30);
            contentOverlay.AddChild(lblTitle);

            // Intro Line 1 & Line 2
            var lblIntro1 = new Label();
            lblIntro1.Text = TranslationServer.Translate("By publishing content on Realm, you grant us permission to host, distribute, and display your map so people can play it.");
            lblIntro1.HorizontalAlignment = HorizontalAlignment.Left;
            lblIntro1.AddThemeFontSizeOverride("font_size", 12);
            lblIntro1.AddThemeColorOverride("font_color", new Color(0.18f, 0.15f, 0.12f));
            lblIntro1.Position = new Vector2(120, 112);
            lblIntro1.Size = new Vector2(784, 20);
            contentOverlay.AddChild(lblIntro1);

            var lblIntro2 = new Label();
            lblIntro2.Text = TranslationServer.Translate("We want Realm to be a thriving, collaborative arcade. Please respect these rules:");
            lblIntro2.HorizontalAlignment = HorizontalAlignment.Left;
            lblIntro2.AddThemeFontSizeOverride("font_size", 12);
            lblIntro2.AddThemeColorOverride("font_color", new Color(0.18f, 0.15f, 0.12f));
            lblIntro2.Position = new Vector2(120, 132);
            lblIntro2.Size = new Vector2(784, 20);
            contentOverlay.AddChild(lblIntro2);

            // Helper to place text rule card at exact X, Y coordinates
            void AddRuleTextBox(string title, string desc, float posX, float posY, float width, float height)
            {
                var textVBox = new VBoxContainer();
                textVBox.Position = new Vector2(posX, posY);
                textVBox.Size = new Vector2(width, height);
                textVBox.AddThemeConstantOverride("separation", 2);

                var lblTitle = new Label();
                lblTitle.Text = TranslationServer.Translate(title);
                lblTitle.AddThemeFontSizeOverride("font_size", 15);
                lblTitle.AddThemeColorOverride("font_color", new Color(0.72f, 0.35f, 0.12f));
                lblTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                textVBox.AddChild(lblTitle);

                var lblDesc = new Label();
                lblDesc.Text = TranslationServer.Translate(desc);
                lblDesc.AddThemeFontSizeOverride("font_size", 11);
                lblDesc.AddThemeColorOverride("font_color", new Color(0.22f, 0.18f, 0.15f));
                lblDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                lblDesc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                textVBox.AddChild(lblDesc);

                contentOverlay.AddChild(textVBox);
            }

            // Row 1
            AddRuleTextBox("Your Work is Yours",
                "You retain full ownership of your original creations. You aren't signing away your copyright to anyone.",
                252, 185, 235, 125);

            AddRuleTextBox("Monetization",
                "You may ask for donations from your player base, but you may not offer any differences in gameplay compared to non-paying users, other than cosmetic rewards. Pay-to-win is not allowed.",
                655, 185, 240, 125);

            // Row 2
            AddRuleTextBox("Collaboration",
                "By publishing your content on Realm, you allow other creators to open and learn from your work. You also grant them permission to adapt, build upon, and incorporate it into their own creations, provided those new works remain exclusively within the Realm platform.",
                252, 338, 235, 135);

            AddRuleTextBox("Going Solo",
                "Want to turn your map into a standalone game? Go for it! However, you can only take your original work with you. You must replace any content created by other Realm users, unless you obtain their explicit written permission.",
                655, 338, 240, 135);

            // Row 3
            AddRuleTextBox("Give Credit",
                "If you import another creator's work, they still own the original. Never claim their work as your own.",
                252, 518, 235, 125);

            AddRuleTextBox("No Plagiarism or Piracy",
                "Do not upload content you didn’t make or don't have the rights to use. This includes trademarked content from other video games, movies, music, and media.",
                655, 518, 240, 125);

            // Bottom Buttons (Accept & Quit)
            var btnAccept = new Button();
            btnAccept.Set("icon_max_width", 0);
            SetupOptionButton(btnAccept, "Accept", () =>
            {
                _agreementShownThisSession = true;
                if (onAccepted != null)
                {
                    overlay.TreeExited += () => onAccepted();
                }
                overlay.QueueFree();
                CheckCreatorRegistrationAndPrompt();
            }, 13);
            btnAccept.Position = new Vector2(300, 692);
            btnAccept.Size = new Vector2(165, 42);
            contentOverlay.AddChild(btnAccept);

            var btnQuit = new Button();
            btnQuit.Set("icon_max_width", 0);
            SetupOptionButton(btnQuit, "Quit", () =>
            {
                _agreementShownThisSession = false;
                overlay.QueueFree();
                Realm.Client.UI.UIManager.Instance?.TransitionTo(GameScreen.MainMenu);
            }, 13);
            btnQuit.Position = new Vector2(558, 692);
            btnQuit.Size = new Vector2(165, 42);
            contentOverlay.AddChild(btnQuit);
        }

        public void OpenAbilityVfxDialog(string abilityId, System.Text.Json.Nodes.JsonObject abilityData, Action<System.Text.Json.Nodes.JsonObject> onApplied = null)
        {
            if (_abilityVfxDialog == null)
            {
                _abilityVfxDialog = new Realm.Client.UI.MapEditor.AbilityVfxDialog(this);
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

            if (Realm.Client.UI.MapEditor.FloatingDialogBase.HasAnyDialogOpen)
            {
                return Realm.Client.UI.MapEditor.FloatingDialogBase.CloseTopmostDialog();
            }

            return false;
        }

        private bool TryCloseConfirmationOverlay()
        {
            var confirmOverlay = GetNodeOrNull<Control>("ConfirmationOverlay") ?? Realm.Client.UI.UIManager.Instance?.GetNodeOrNull<Control>("ConfirmationOverlay");
            if (IsValidOverlay(confirmOverlay))
            {
                if (confirmOverlay.HasMeta("CancelAction"))
                {
                    var action = confirmOverlay.GetMeta("CancelAction").AsCallable();
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
                Realm.Client.UI.UIManager.Instance?.TransitionTo(GameScreen.MainMenu);
                return true;
            }
            return false;
        }

        private bool TryCloseHelpOverlayPanel()
        {
            if (IsValidOverlay(_helpOverlayPanel))
            {
                _helpOverlayPanel.QueueFree();
                _helpOverlayPanel = null;
                return true;
            }
            return false;
        }

        private bool IsValidOverlay(Node overlay)
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
            if (!keyEvent.Echo && keyEvent.Keycode == global::Godot.Key.Escape)
            {
                if (Realm.Client.UI.SettingsMenu.IsOpen)
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
            if (keyEvent.Keycode == global::Godot.Key.Tab)
            {
                if (Realm.Client.UI.MapEditor.FloatingDialogBase.HasAnyDialogOpen)
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
            if (!keyEvent.Echo && keyEvent.Keycode == global::Godot.Key.Quoteleft)
            {
                if (IsFocusOwnerTextInput()) return true;
                
                if (Realm.Client.UI.WasmConsoleWindow.IsSinglePlayerOrTestMode())
                {
                    Realm.Client.UI.WasmConsoleWindow.Instance.ToggleVisibility();
                    GetViewport().SetInputAsHandled();
                    return true;
                }
            }
            return false;
        }

        private void HandleArrowKeys(InputEventKey keyEvent)
        {
            if (keyEvent.Keycode == global::Godot.Key.Up || keyEvent.Keycode == global::Godot.Key.Down ||
                keyEvent.Keycode == global::Godot.Key.Left || keyEvent.Keycode == global::Godot.Key.Right)
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

        public void OpenInstanceManagerDialog()
        {
            if (_instanceManagerDialog == null)
            {
                _instanceManagerDialog = new Realm.Client.UI.MapEditor.InstanceManagerDialog(this);
            }
            _instanceManagerDialog.OpenDialog();
        }

        public void OpenAuthorSignatureDialog()
        {
            _authorSignatureDialog?.OpenDialog();
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
                Set3DInteractionActive(false);
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
            var topBar = GetNodeOrNull<PanelContainer>("TopBar");
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
            long now = System.Environment.TickCount64;
            if (!forceReload && !string.IsNullOrEmpty(_cachedMapName) && (now - _lastMapNameCacheTicks < 2000))
            {
                return _cachedMapName;
            }

            try
            {
                string workspacePath = !string.IsNullOrEmpty(_tempWorkspacePath) ? _tempWorkspacePath : Realm.Client.Services.MapWorkspaceService.GetActiveWorkspacePath();
                
                if (TryGetMapNameFromManifest(workspacePath, out string manifestMapName))
                {
                    return UpdateCachedMapName(manifestMapName, now);
                }

                if (TryGetMapNameFromMetadata(workspacePath, out string metadataMapName))
                {
                    return UpdateCachedMapName(metadataMapName, now);
                }

                if (TryGetMapNameFromActiveHost(out string activeMapName))
                {
                    return UpdateCachedMapName(activeMapName, now);
                }

                return UpdateCachedMapName("Unnamed Map", now);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapEditorHUD] Error getting map name: {ex.Message}");
                return UpdateCachedMapName("Unnamed Map", now);
            }
        }

        private bool TryGetMapNameFromManifest(string workspacePath, out string mapName)
        {
            var manifest = MapFileService.LoadManifest(workspacePath);
            return TrySanitizeCandidate(manifest?.MapName, out mapName);
        }

        private bool TryGetMapNameFromMetadata(string workspacePath, out string mapName)
        {
            var metadata = MapFileService.LoadMetadata(workspacePath);
            return TrySanitizeCandidate(metadata?.MapProperties?.MapName, out mapName);
        }

        private bool TryGetMapNameFromActiveHost(out string mapName)
        {
            mapName = string.Empty;
            if (!string.IsNullOrEmpty(Realm.Client.Core.GameHost.Instance?.ActiveMapName))
            {
                string candidate = System.IO.Path.GetFileNameWithoutExtension(Realm.Client.Core.GameHost.Instance.ActiveMapName);
                return TrySanitizeCandidate(candidate, out mapName);
            }
            return false;
        }

        private string UpdateCachedMapName(string mapName, long nowTicks)
        {
            _cachedMapName = mapName;
            _lastMapNameCacheTicks = nowTicks;
            return mapName;
        }

        public void UpdateMapNameHeader()
        {
            if (_lblMapNameHeader == null) return;
            string mapName = GetMapNameFromMetadata();
            string displayMapName = mapName == "Untitled Map" ? TranslationServer.Translate("Untitled Map") : mapName;
            bool hasUnsaved = Realm.Client.Core.GameHost.Instance?.EditorHasUnsavedChanges ?? false;

            string displayText = hasUnsaved ? $"🗺️ {displayMapName} *" : $"🗺️ {displayMapName}";
            string tooltipText = hasUnsaved
                ? $"{displayMapName} * ({TranslationServer.Translate("Unsaved changes — Press Ctrl+S to save")})"
                : $"{displayMapName} ({TranslationServer.Translate("All changes saved")})";

            if (_lblMapNameHeader.Text != displayText)
            {
                _lblMapNameHeader.Text = displayText;
            }

            if (_lblMapNameHeader.TooltipText != tooltipText)
            {
                _lblMapNameHeader.TooltipText = tooltipText;
            }
        }

        public void PopulateAnimationPreviewDropdown()
        {
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

            CreateSectionHeader(_contentLightingTuning, "--- TERRAIN CLIFFS & SILHOUETTES ---");
            _sldCliffJitterStrength = CreateSliderRow(_contentLightingTuning, "Cliff Jitter Str", 0f, 2f, 0.02f, _tuneCliffJitterStrength, val => { _tuneCliffJitterStrength = val; ApplyLiveLightingTuning(); });
            _sldCliffJitterScale = CreateSliderRow(_contentLightingTuning, "Cliff Jitter Scl", 0.01f, 0.5f, 0.005f, _tuneCliffJitterScale, val => { _tuneCliffJitterScale = val; ApplyLiveLightingTuning(); }, "0.00#");
            _sldCliffRimNoiseStrength = CreateSliderRow(_contentLightingTuning, "Cliff Rim Str", 0f, 1f, 0.02f, _tuneCliffRimNoiseStrength, val => { _tuneCliffRimNoiseStrength = val; ApplyLiveLightingTuning(); });

            CreateSectionHeader(_contentLightingTuning, "--- TERRAIN TEXTURE BLENDING ---");
            _sldHeightBlendSoftness = CreateSliderRow(_contentLightingTuning, "Blend Softness", 0.001f, 0.20f, 0.002f, _tuneHeightBlendSoftness, val => { _tuneHeightBlendSoftness = val; ApplyLiveLightingTuning(); }, "0.00#");
            _sldBlendNoiseStrength = CreateSliderRow(_contentLightingTuning, "Blend Noise Str", 0f, 1f, 0.02f, _tuneBlendNoiseStrength, val => { _tuneBlendNoiseStrength = val; ApplyLiveLightingTuning(); });
            _sldBlendNoiseScale = CreateSliderRow(_contentLightingTuning, "Blend Noise Scl", 0.01f, 0.5f, 0.005f, _tuneBlendNoiseScale, val => { _tuneBlendNoiseScale = val; ApplyLiveLightingTuning(); }, "0.00#");

            lightingAccordion.Visible = false;
        }

        private void UpdateSliderLabel(Slider slider, float val)
        {
            if (slider != null && slider.HasMeta("val_label"))
            {
                var lbl = slider.GetMeta("val_label").As<Label>();
                if (lbl != null && GodotObject.IsInstanceValid(lbl))
                {
                    string format = slider.HasMeta("val_format") ? slider.GetMeta("val_format").AsString() : "0.0#";
                    lbl.Text = val.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }

        private OptionButton CreateDropdownRow(VBoxContainer parent, string labelText, string[] options, int initialIdx, Action<int> onChanged)
        {
            var row = new HBoxContainer();

            var lblName = new Label();
            lblName.Text = labelText;
            lblName.CustomMinimumSize = new Vector2(110, 0);
            lblName.AddThemeFontSizeOverride("font_size", 11);
            row.AddChild(lblName);

            var opt = new OptionButton();
            opt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            opt.CustomMinimumSize = new Vector2(90, 0);
            opt.AddThemeFontSizeOverride("font_size", 11);
            for (int i = 0; i < options.Length; i++)
            {
                opt.AddItem(options[i], i);
            }
            if (initialIdx >= 0 && initialIdx < options.Length)
            {
                opt.Select(initialIdx);
            }
            opt.ItemSelected += (long index) =>
            {
                onChanged((int)index);
            };
            row.AddChild(opt);

            parent.AddChild(row);
            return opt;
        }

        private HBoxContainer CreateToggleRow(VBoxContainer parent, string labelText, bool initialVal, Action<bool> onChanged)
        {
            var row = new HBoxContainer();
            var chk = new CheckBox();
            chk.Text = labelText;
            chk.ButtonPressed = initialVal;
            chk.AddThemeFontSizeOverride("font_size", 11);
            chk.Toggled += (bool pressed) => onChanged(pressed);
            row.AddChild(chk);
            parent.AddChild(row);
            return row;
        }

        private CheckBox CreateCheckBoxRow(VBoxContainer parent, string labelText, bool initialVal, Action<bool> onChanged)
        {
            var row = new HBoxContainer();
            var chk = new CheckBox();
            chk.Text = labelText;
            chk.ButtonPressed = initialVal;
            chk.FocusMode = Control.FocusModeEnum.None;
            chk.AddThemeFontSizeOverride("font_size", 11);
            chk.Toggled += (bool pressed) => onChanged(pressed);
            row.AddChild(chk);
            parent.AddChild(row);
            return chk;
        }

        private Label CreateSectionHeader(VBoxContainer parent, string titleText)
        {
            var lbl = new Label();
            lbl.Text = titleText;
            lbl.AddThemeFontSizeOverride("font_size", 11);
            lbl.Modulate = new Color(0.95f, 0.85f, 0.35f);
            parent.AddChild(lbl);
            return lbl;
        }
    }
}
