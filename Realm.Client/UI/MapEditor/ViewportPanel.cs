using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Ecs.Components.Core;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the viewport panel needs from the editor: its node tree, the shared accordion and button styling, the
/// overlay and environment refreshes its popups broadcast, and the tool and pivot actions behind its toolbar.
/// </summary>
public sealed record ViewportPanelHooks(
	Node Owner,
	Action<Button> StyleAccordionHeader,
	Action<Button, Control, string> SetupAccordion,
	Action<Button, string, Action?, int, string> SetupButton,
	Action<Button, string, Action?, int, string> SetupOptionButton,
	Action<PopupMenu> StylePopupMenu,
	Action OpenOverlayPopup,
	Action OpenEnvironmentPopup,
	Action<OptionButton> SetupLightingDropdown,
	Action<OptionButton> SetupWeatherDropdown,
	Action<GridOverlayMode> UpdateGridOverlay,
	Action<bool> UpdateCameraBoundsOverlay,
	Action<bool> UpdateWireframeOverlay,
	Action<string> ShowFeedback,
	Action<Button> ToggleMeasure,
	Action InitializeEditingDirectory,
	Action<OptionButton> SetupRingSpacingDropdown,
	Action<OptionButton> SetupRadialStepDropdown,
	Action ResetPivot);

/// <summary>
/// The "Viewport &amp; Navigation" accordion in the editor's left panel: its header and content, the toolbar buttons
/// for overlays, environment and the tape measure, the minimap frame and camera indicator, the measurement readout
/// in the bottom bar, and the two popups behind the overlay and environment buttons along with the polar controls.
/// </summary>
public sealed class ViewportPanel
{
	private const string BasePath = "LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion";
	private const string ContentPath = BasePath + "/ContentViewport";
	private const string ToolbarPath = ContentPath + "/BoxViewportToolbar/ViewportIconRow1";

	public VBoxContainer Accordion { get; private set; } = null!;
	public Button Header { get; private set; } = null!;
	public VBoxContainer Content { get; private set; } = null!;
	public Button GridToggleButton { get; private set; } = null!;
	public Button EnvironmentToggleButton { get; private set; } = null!;
	public Button MeasureButton { get; private set; } = null!;
	public PanelContainer MinimapFrame { get; private set; } = null!;
	public Control MinimapArea { get; private set; } = null!;
	public MapEditorCameraIndicator CameraIndicator { get; private set; } = null!;
	public PanelContainer MeasurementPanel { get; private set; } = null!;
	public Label MeasurementLabel { get; private set; } = null!;
	public PopupMenu OverlayPopup { get; private set; } = null!;
	public PopupPanel EnvironmentPopup { get; private set; } = null!;
	public OptionButton LightingOption { get; private set; } = null!;
	public OptionButton WeatherOption { get; private set; } = null!;
	public CheckBox ShadowsCheck { get; private set; } = null!;
	public OptionButton RingSpacingOption { get; private set; } = null!;
	public OptionButton RadialStepOption { get; private set; } = null!;
	public Button ResetPivotButton { get; private set; } = null!;

	public void Build(ViewportPanelHooks hooks)
	{
		BuildAccordion(hooks);

		hooks.InitializeEditingDirectory();

		BuildOverlayModePopup(hooks);

		GridToggleButton = hooks.Owner.GetNode<Button>(ToolbarPath + "/BtnToggleGrid");
		hooks.SetupButton(GridToggleButton, UnicodeIcons.BORDER_ALL, () => hooks.OpenOverlayPopup(), 12, "Overlay");

		BuildEnvironmentPopup(hooks);

		BuildMinimap(hooks);
		BuildMeasurementHud(hooks);
		BuildMeasurementAndPolarControls(hooks);
	}

	private void BuildAccordion(ViewportPanelHooks hooks)
	{
		Accordion = hooks.Owner.GetNode<VBoxContainer>(BasePath);
		Header = hooks.Owner.GetNode<Button>(BasePath + "/BtnHeaderViewport");
		Content = hooks.Owner.GetNode<VBoxContainer>(ContentPath);
		hooks.StyleAccordionHeader(Header);
		hooks.SetupAccordion(Header, Content, TranslationServer.Translate("Viewport & Navigation"));
	}

	private void BuildMinimap(ViewportPanelHooks hooks)
	{
		MinimapFrame = hooks.Owner.GetNode<PanelContainer>(ContentPath + "/MinimapFrame");
		MinimapArea = hooks.Owner.GetNode<Control>(ContentPath + "/MinimapFrame/MinimapArea");
		CameraIndicator = hooks.Owner.GetNode<MapEditorCameraIndicator>(ContentPath + "/MinimapFrame/MinimapArea/CameraIndicator");
	}

	private void BuildMeasurementHud(ViewportPanelHooks hooks)
	{
		var bottomBar = hooks.Owner.GetNode<HBoxContainer>("BottomCenterBar");
		bottomBar.Alignment = BoxContainer.AlignmentMode.Center;
		bottomBar.MouseFilter = Control.MouseFilterEnum.Ignore;

		MeasurementPanel = new PanelContainer();
		MeasurementPanel.Name = "MeasurementHUD";
		MeasurementPanel.CustomMinimumSize = new Vector2(0, 44);
		MeasurementPanel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		MeasurementPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		MeasurementPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
		MeasurementPanel.Visible = false;

		var measureHBox = new HBoxContainer();
		measureHBox.Name = "MeasureHBox";
		measureHBox.Alignment = BoxContainer.AlignmentMode.Center;
		measureHBox.AddThemeConstantOverride("separation", 8);
		measureHBox.MouseFilter = Control.MouseFilterEnum.Ignore;

		MeasurementLabel = new Label();
		MeasurementLabel.Name = "LblMeasureTelemetry";
		MeasurementLabel.HorizontalAlignment = HorizontalAlignment.Left;
		MeasurementLabel.VerticalAlignment = VerticalAlignment.Center;
		MeasurementLabel.AddThemeFontSizeOverride("font_size", 13);
		MeasurementLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);

		measureHBox.AddChild(MeasurementLabel);
		MeasurementPanel.AddChild(measureHBox);

		bottomBar.AddChild(MeasurementPanel);
	}

	private void BuildOverlayModePopup(ViewportPanelHooks hooks)
	{
		OverlayPopup = new PopupMenu();
		OverlayPopup.Name = "PopupOverlayMode";
		OverlayPopup.HideOnCheckableItemSelection = false;
		OverlayPopup.AddCheckItem(TranslationServer.Translate("Grid") + " (V)", 0);
		OverlayPopup.AddCheckItem(TranslationServer.Translate("Polar") + " (V)", 1);
		OverlayPopup.AddCheckItem(TranslationServer.Translate("Camera Bounds"), 2);
		OverlayPopup.AddCheckItem(TranslationServer.Translate("Wireframe") + " (F7)", 3);
		hooks.StylePopupMenu(OverlayPopup);
		OverlayPopup.IdPressed += (long id) =>
		{
			OverlayPopup.Hide();
			if (GameContext.Host == null) return;
			int idx = OverlayPopup.GetItemIndex((int)id);
			if (idx < 0) return;
			bool newChecked = !OverlayPopup.IsItemChecked(idx);
			OverlayPopup.SetItemChecked(idx, newChecked);

			if (id == 0 || id == 1)
			{
				int gridIdx = OverlayPopup.GetItemIndex(0);
				int polarIdx = OverlayPopup.GetItemIndex(1);
				bool gridOn = gridIdx >= 0 && OverlayPopup.IsItemChecked(gridIdx);
				bool polarOn = polarIdx >= 0 && OverlayPopup.IsItemChecked(polarIdx);

				var newMode = (gridOn, polarOn) switch
				{
					(true, true) => GridOverlayMode.Both,
					(true, false) => GridOverlayMode.Grid,
					(false, true) => GridOverlayMode.Polar,
					(false, false) => GridOverlayMode.Off
				};

				GameContext.EditorService.Tools.GridMode = newMode;
				GameContext.Host.UpdateGridOverlayVisibility();
				hooks.UpdateGridOverlay(newMode);
				string modeName = newMode switch
				{
					GridOverlayMode.Off => "OFF",
					GridOverlayMode.Grid => "GRID",
					GridOverlayMode.Polar => "POLAR",
					GridOverlayMode.Both => "GRID + POLAR",
					_ => "OFF"
				};
				hooks.ShowFeedback($"Overlay Mode: {modeName}");
			}
			else if (id == 2)
			{
				GameContext.EditorService.Tools.CameraBoundsVisible = newChecked;
				GameContext.Host.UpdateCameraBoundsOverlayVisibility();
				hooks.UpdateCameraBoundsOverlay(newChecked);
				hooks.ShowFeedback(newChecked
					? TranslationServer.Translate("Camera Bounds: ON")
					: TranslationServer.Translate("Camera Bounds: OFF"));
			}
			else if (id == 3)
			{
				if (GameContext.Host.GroundTerrain != null)
				{
					GameContext.Host.GroundTerrain.ToggleWireframeMode();
					bool isWireframe = hooks.Owner.GetViewport()?.DebugDraw == Viewport.DebugDrawEnum.Wireframe;
					hooks.UpdateWireframeOverlay(isWireframe);
					hooks.ShowFeedback(isWireframe
						? TranslationServer.Translate("Wireframe Mode: ON")
						: TranslationServer.Translate("Wireframe Mode: OFF"));
				}
			}
		};
		hooks.Owner.AddChild(OverlayPopup);
	}

	private void BuildEnvironmentPopup(ViewportPanelHooks hooks)
	{
		EnvironmentPopup = new PopupPanel();
		EnvironmentPopup.Name = "PopupEnvironment";

		var envPopupStyle = new StyleBoxFlat();
		envPopupStyle.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
		envPopupStyle.BorderColor = UIStyle.ColorGold;
		envPopupStyle.SetBorderWidthAll(1);
		envPopupStyle.SetCornerRadiusAll(4);
		envPopupStyle.ContentMarginLeft = 10;
		envPopupStyle.ContentMarginRight = 10;
		envPopupStyle.ContentMarginTop = 10;
		envPopupStyle.ContentMarginBottom = 10;
		EnvironmentPopup.AddThemeStyleboxOverride("panel", envPopupStyle);

		var envVBox = new VBoxContainer();
		envVBox.Name = "EnvVBox";
		envVBox.AddThemeConstantOverride("separation", 6);
		envVBox.CustomMinimumSize = new Vector2(170, 0);

		LightingOption = new OptionButton();
		LightingOption.Name = "OptEnvLighting";
		LightingOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		LightingOption.CustomMinimumSize = new Vector2(0, 24);
		LightingOption.ClipText = true;
		hooks.SetupLightingDropdown(LightingOption);
		LightingOption.ItemSelected += (idx) =>
		{
			SetLighting((int)idx, hooks.ShowFeedback);
			EnvironmentPopup.Hide();
		};
		envVBox.AddChild(LightingOption);

		WeatherOption = new OptionButton();
		WeatherOption.Name = "OptEnvWeather";
		WeatherOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		WeatherOption.CustomMinimumSize = new Vector2(0, 24);
		WeatherOption.ClipText = true;
		hooks.SetupWeatherDropdown(WeatherOption);
		WeatherOption.ItemSelected += (idx) =>
		{
			SetWeather((int)idx, hooks.ShowFeedback);
			EnvironmentPopup.Hide();
		};
		envVBox.AddChild(WeatherOption);

		ShadowsCheck = new CheckBox();
		ShadowsCheck.Name = "ChkEnvShadows";
		ShadowsCheck.Text = TranslationServer.Translate("Shadows");
		ShadowsCheck.TooltipText = TranslationServer.Translate("Toggle shadows in editor (F9)");
		ShadowsCheck.ButtonPressed = true;
		ShadowsCheck.FocusMode = Control.FocusModeEnum.None;
		ShadowsCheck.AddThemeFontSizeOverride("font_size", 11);
		UIStyle.ApplyCheckboxStyle(ShadowsCheck);
		ShadowsCheck.Toggled += (pressed) =>
		{
			if (GameContext.Host != null)
			{
				bool disableShadows = !pressed;
				if (GameContext.EditorService.Tools.DisableShadows != disableShadows)
				{
					GameContext.EditorService.Tools.DisableShadows = disableShadows;
					GameContext.Host.UpdateEditorShadows();
					hooks.ShowFeedback(disableShadows
						? TranslationServer.Translate("Shadows: OFF")
						: TranslationServer.Translate("Shadows: ON"));
				}
			}
			EnvironmentPopup.Hide();
		};
		envVBox.AddChild(ShadowsCheck);

		EnvironmentPopup.AddChild(envVBox);
		hooks.Owner.AddChild(EnvironmentPopup);

		EnvironmentToggleButton = hooks.Owner.GetNode<Button>(ToolbarPath + "/BtnToggleEnvironment");
		EnvironmentToggleButton.Name = "BtnToggleEnvironment";
		EnvironmentToggleButton.Set("icon_max_width", 0);
		hooks.SetupButton(EnvironmentToggleButton, UnicodeIcons.SUN, () => hooks.OpenEnvironmentPopup(), 12, "Environment");
	}

	private void BuildMeasurementAndPolarControls(ViewportPanelHooks hooks)
	{
		MeasureButton = hooks.Owner.GetNode<Button>(ToolbarPath + "/BtnTapeMeasure");
		MeasureButton.Set("icon_max_width", 0);
		hooks.SetupButton(MeasureButton, UnicodeIcons.RULER, () => hooks.ToggleMeasure(MeasureButton), 12, "Tape Measure Tool: Click Point A, then Point B to measure distance & slope (U)");

		RingSpacingOption = hooks.Owner.GetNode<OptionButton>(ContentPath + "/RowPolarConfig/OptPolarRingSpacing");
		RingSpacingOption.Set("icon_max_width", 0);
		hooks.SetupRingSpacingDropdown(RingSpacingOption);

		RadialStepOption = hooks.Owner.GetNode<OptionButton>(ContentPath + "/RowPolarConfig/OptPolarRadialStep");
		RadialStepOption.Set("icon_max_width", 0);
		hooks.SetupRadialStepDropdown(RadialStepOption);

		ResetPivotButton = hooks.Owner.GetNode<Button>(ContentPath + "/RowPolarConfig/BtnResetPivotToCenter");
		ResetPivotButton.Set("icon_max_width", 0);
		hooks.SetupOptionButton(ResetPivotButton, $"{UnicodeIcons.CROSSHAIRS} RESET PIVOT", () => hooks.ResetPivot(), 10, "Reset symmetry and polar overlay center pivot to true map center");
	}

	/// <summary>
	/// The navigation bar row: four icon buttons, the toolbar plate and the spacing of the polar/symmetry controls.
	/// Returns that row because whether it is visible depends on editor state the panel does not hold - the editor
	/// decides when symmetry is active and keeps the row around for later toggles.
	/// </summary>
	public HBoxContainer? StyleNavigationBar(Font? font, Button? cameraToggle)
	{
		var target = MapEditorStyle.ContentTarget(Content);
		if (target == null) return null;

		MapEditorStyle.IconButton(GridToggleButton, UnicodeIcons.BORDER_ALL, "Overlay", font);
		MapEditorStyle.IconButton(EnvironmentToggleButton, UnicodeIcons.SUN, "Environment", font);
		MapEditorStyle.IconButton(cameraToggle, UnicodeIcons.CAMERA, "Configure camera controls (R / C / + / - / F8)", font);
		MapEditorStyle.IconButton(MeasureButton, UnicodeIcons.RULER, "Tape measure distance & slope tool (U)", font);

		MapEditorStyle.SubContainer(target.GetNodeOrNull<Control>("BoxViewportToolbar"), "Navigation Bar");

		var polarRow = target.GetNodeOrNull<HBoxContainer>("RowPolarConfig");

		if (RingSpacingOption != null)
		{
			RingSpacingOption.CustomMinimumSize = new Vector2(0, 26);
			RingSpacingOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			RingSpacingOption.AddThemeFontSizeOverride("font_size", 9);
		}

		if (RadialStepOption != null)
		{
			RadialStepOption.CustomMinimumSize = new Vector2(0, 26);
			RadialStepOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			RadialStepOption.AddThemeFontSizeOverride("font_size", 9);
		}

		if (ResetPivotButton != null)
		{
			ResetPivotButton.CustomMinimumSize = new Vector2(0, 26);
			ResetPivotButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			ResetPivotButton.AddThemeFontSizeOverride("font_size", 9);
		}

		return polarRow;
	}

	/// <summary>
	/// Applies a lighting preset from the environment dropdown. The panel owns the dropdown and already reads editor
	/// state from GameContext elsewhere, so only the feedback line comes in as a callback.
	/// </summary>
	public void SetLighting(int index, Action<string> feedback)
	{
		var envService = GameContext.EnvironmentService;
		if (envService == null) return;
		var (timeName, icon) = envService.SetEnvironmentLightingDomain(index);
		feedback(string.Format(TranslationServer.Translate("Lighting: {0} {1}"), icon, TranslationServer.Translate(timeName)));
	}

	/// <summary>Applies a weather preset the same way, relabelling the service's capitalised answer.</summary>
	public void SetWeather(int index, Action<string> feedback)
	{
		var envService = GameContext.EnvironmentService;
		if (envService == null) return;
		var (applied, icon) = envService.SetEnvironmentWeatherDomain(index);
		feedback(string.Format(TranslationServer.Translate("Weather: {0} {1}"), icon, TranslationServer.Translate(applied.Capitalize())));
	}

	/// <summary>
	/// Reflects the editor's current ring spacing in the dropdown. The value lives on the editor's tools and the
	/// dropdown lives here, so the editor calls this when the spacing changes; selecting without signalling is what
	/// keeps the change from re-entering the setter.
	/// </summary>
	public void RefreshRingSpacingSelection()
	{
		if (RingSpacingOption == null || GameContext.Host == null) return;

		float current = GameContext.EditorService.Tools.PolarRingSpacing;
		for (int i = 0; i < RingSpacingOption.ItemCount; i++)
		{
			if (Mathf.IsEqualApprox((float)RingSpacingOption.GetItemId(i), current))
			{
				if (RingSpacingOption.Selected != i)
				{
					RingSpacingOption.Selected = i;
				}

				break;
			}
		}
	}

	/// <summary>
	/// Reflects the current spoke count. Under rotational symmetry the spokes are the fold count and the dropdown is
	/// locked to it, which is what the tooltip says in that mode.
	/// </summary>
	public void RefreshRadialStepSelection()
	{
		if (RadialStepOption == null || GameContext.Host == null) return;

		int spokes = GameContext.Host.EditorMirrorMode == MirrorMode.Rotational
			? GameContext.Host.EditorSymmetryFolds
			: GameContext.EditorService.Tools.PolarSpokeFolds;

		for (int i = 0; i < RadialStepOption.ItemCount; i++)
		{
			if (RadialStepOption.GetItemId(i) == spokes)
			{
				if (RadialStepOption.Selected != i)
				{
					RadialStepOption.Selected = i;
				}

				break;
			}
		}

		RadialStepOption.TooltipText = GameContext.Host.EditorMirrorMode == MirrorMode.Rotational
			? TranslationServer.Translate("Spokes are automatically locked to Rotational N-Fold symmetry")
			: TranslationServer.Translate("Select radial spoke count (2, 3, 4, 5, 6, 8, 12, 16)");
	}
}
