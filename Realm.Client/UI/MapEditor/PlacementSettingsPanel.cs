using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the placement panel needs from the editor: its node tree, the shared accordion, button and dropdown
/// styling, the snap-state broadcast, and the sidebar refresh a clump-mode toggle triggers.
/// </summary>
public sealed record PlacementSettingsHooks(
	Node Owner,
	Action<Button> StyleAccordionHeader,
	Action<Button, Control, string> SetupAccordion,
	Action<Button, string, Action?, int, string> SetupOptionButton,
	Action<OptionButton, string> SetupMirrorDropdown,
	Action<bool> SnapToGridBroadcast,
	Action ClumpModeChanged);

/// <summary>
/// The placement accordion in the editor's right panel: rotation and scale sliders, grid snap, the placement
/// symmetry dropdown, the unit owner picker, and the random rotation/scale and clump-mode toggles with their
/// density and variance sliders. It owns those controls; the value plumbing stays in
/// <see cref="MapEditorPlacementSettings"/>, which the editor hands them to.
/// </summary>
public sealed class PlacementSettingsPanel
{
	private const string BasePath = "RightSlidePanel/RightScroll/AccordionContainer/PlacementAccordion";
	private const string ContentPath = BasePath + "/ContentPlacement";

	public VBoxContainer Accordion { get; private set; } = null!;
	public Button Header { get; private set; } = null!;
	public VBoxContainer Content { get; private set; } = null!;
	public Slider RotateSlider { get; private set; } = null!;
	public Label RotateValue { get; private set; } = null!;
	public Slider ScaleSlider { get; private set; } = null!;
	public Label ScaleValue { get; private set; } = null!;
	public Button SnapButton { get; private set; } = null!;
	public OptionButton MirrorOption { get; private set; } = null!;
	public HBoxContainer PlayerContainer { get; private set; } = null!;
	public OptionButton PlayerOption { get; private set; } = null!;
	public CheckBox RandomRotationCheck { get; private set; } = null!;
	public CheckBox RandomScaleCheck { get; private set; } = null!;
	public CheckBox ClumpModeCheck { get; private set; } = null!;
	public Control DensityBox { get; private set; } = null!;
	public Slider DensitySlider { get; private set; } = null!;
	public Label DensityValue { get; private set; } = null!;
	public Control ScaleVarBox { get; private set; } = null!;
	public Slider ScaleVarSlider { get; private set; } = null!;
	public Label ScaleVarValue { get; private set; } = null!;

	public void Build(PlacementSettingsHooks hooks)
	{
		BuildAccordion(hooks);
		BuildTransformSliders(hooks);
		BuildSnapAndMirrorControls(hooks);
		BuildPlayerOwnerControl(hooks);
		BuildClumpModeControls(hooks);
	}

	private void BuildAccordion(PlacementSettingsHooks hooks)
	{
		Accordion = hooks.Owner.GetNode<VBoxContainer>(BasePath);
		Header = hooks.Owner.GetNode<Button>(BasePath + "/BtnHeaderPlacement");
		Content = hooks.Owner.GetNode<VBoxContainer>(ContentPath);
		hooks.StyleAccordionHeader(Header);
		hooks.SetupAccordion(Header, Content, TranslationServer.Translate("Placement Config"));
	}

	private void BuildTransformSliders(PlacementSettingsHooks hooks)
	{
		RotateSlider = hooks.Owner.GetNode<Slider>(ContentPath + "/PlacementRotateBox/SldPlacementRotate");
		RotateValue = hooks.Owner.GetNode<Label>(ContentPath + "/PlacementRotateBox/Header/LblPlacementRotateValue");
		ScaleSlider = hooks.Owner.GetNode<Slider>(ContentPath + "/PlacementScaleBox/SldPlacementScale");
		ScaleValue = hooks.Owner.GetNode<Label>(ContentPath + "/PlacementScaleBox/Header/LblPlacementScaleValue");
		var lblPlacementRotateTitle = hooks.Owner.GetNodeOrNull<Label>(ContentPath + "/PlacementRotateBox/Header/LblRotateTitle");
		if (lblPlacementRotateTitle != null) lblPlacementRotateTitle.Text = TranslationServer.Translate("Rotation (Shift + Mouse_Wheel)");
		var lblPlacementScaleTitle = hooks.Owner.GetNodeOrNull<Label>(ContentPath + "/PlacementScaleBox/Header/LblScaleTitle");
		if (lblPlacementScaleTitle != null) lblPlacementScaleTitle.Text = TranslationServer.Translate("Scale (Alt + Mouse_Wheel)");
	}

	private void BuildSnapAndMirrorControls(PlacementSettingsHooks hooks)
	{
		SnapButton = hooks.Owner.GetNode<Button>(ContentPath + "/BtnToggleSnap");
		hooks.SetupOptionButton(SnapButton, $"{UnicodeIcons.TABLE} SNAP TO GRID: OFF", () =>
		{
			if (GameContext.Host != null)
			{
				GameContext.Host.EditorSnapToGrid = !GameContext.Host.EditorSnapToGrid;
				hooks.SnapToGridBroadcast(GameContext.Host.EditorSnapToGrid);
			}
		}, 11, "Toggle snapping objects and placements to the grid");

		MirrorOption = new OptionButton();
		MirrorOption.Name = "OptPlacementMirrorMode";
		MirrorOption.Set("icon_max_width", 0);
		hooks.SetupMirrorDropdown(MirrorOption, "Select terrain and object mirroring symmetry mode");
		if (Content != null)
		{
			Content.AddChild(MirrorOption);
			Content.MoveChild(MirrorOption, SnapButton.GetIndex() + 1);
		}
	}

	private void BuildPlayerOwnerControl(PlacementSettingsHooks hooks)
	{
		PlayerContainer = new HBoxContainer();
		PlayerContainer.Name = "PlacementPlayerOwnerContainer";
		PlayerContainer.AddThemeConstantOverride("separation", 6);
		PlayerContainer.Visible = false;

		var lblPlacementPlayer = new Label();
		lblPlacementPlayer.Name = "LblPlacementPlayer";
		lblPlacementPlayer.Text = TranslationServer.Translate("Player");
		lblPlacementPlayer.CustomMinimumSize = new Vector2(50, 0);
		lblPlacementPlayer.AddThemeFontSizeOverride("font_size", 11);
		PlayerContainer.AddChild(lblPlacementPlayer);

		PlayerOption = new OptionButton();
		PlayerOption.Name = "OptPlacementPlayer";
		PlayerOption.Set("icon_max_width", 0);
		PlayerOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		PlayerOption.AddThemeFontSizeOverride("font_size", 10);
		PlayerOption.TooltipText = TranslationServer.Translate("Owner of the next placed unit. Choose 0 NEUTRAL_TAN (gaia) to place neutral objects.");

		for (int i = 0; i < PlayerColorConfig.Palette.Length; i++)
		{
			PlayerOption.AddItem(PlayerColorConfig.GetPaletteLabel(i), i);
		}

		PlayerOption.ItemSelected += (long index) =>
		{
			GameContext.EditorService.SetPlaceUnitPlayerIndexDomain((int)index);
		};

		PlayerContainer.AddChild(PlayerOption);
		if (Content != null)
		{
			Content.AddChild(PlayerContainer);
			Content.MoveChild(PlayerContainer, MirrorOption.GetIndex() + 1);
		}
	}

	private void BuildClumpModeControls(PlacementSettingsHooks hooks)
	{
		RandomRotationCheck = hooks.Owner.GetNode<CheckBox>(ContentPath + "/ChkRandomRotation");
		RandomScaleCheck = hooks.Owner.GetNode<CheckBox>(ContentPath + "/ChkRandomScale");

		ClumpModeCheck = hooks.Owner.GetNode<CheckBox>(ContentPath + "/ChkClumpMode");
		DensityBox = hooks.Owner.GetNode<Control>(ContentPath + "/DensityBox");
		DensitySlider = hooks.Owner.GetNode<HSlider>(ContentPath + "/DensityBox/SldClumpDensity");
		DensityValue = hooks.Owner.GetNode<Label>(ContentPath + "/DensityBox/Header/LblClumpDensityValue");
		ScaleVarBox = hooks.Owner.GetNode<Control>(ContentPath + "/ScaleVarBox");
		ScaleVarSlider = hooks.Owner.GetNode<HSlider>(ContentPath + "/ScaleVarBox/SldClumpScaleVar");
		ScaleVarValue = hooks.Owner.GetNode<Label>(ContentPath + "/ScaleVarBox/Header/LblClumpScaleVarValue");
		var lblScaleVarTitle = hooks.Owner.GetNodeOrNull<Label>(ContentPath + "/ScaleVarBox/Header/LblScaleVarTitle");
		if (lblScaleVarTitle != null) lblScaleVarTitle.Text = TranslationServer.Translate("Clump Scale Variance");

		ClumpModeCheck.ButtonPressed = false;
		ClumpModeCheck.Toggled += (toggled) =>
		{
			if (DensityBox != null) DensityBox.Visible = toggled;
			if (ScaleVarBox != null) ScaleVarBox.Visible = toggled;
			hooks.ClumpModeChanged();
		};
	}

	/// <summary>
	/// The mirror dropdown is the only control on the placement card that the shared accordion pass does not already
	/// size for a narrow column.
	/// </summary>
	public void StyleControls()
	{
		if (MirrorOption == null) return;
		MirrorOption.CustomMinimumSize = new Vector2(0, 30);
		MirrorOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		MirrorOption.AddThemeFontSizeOverride("font_size", 10);
	}
}
