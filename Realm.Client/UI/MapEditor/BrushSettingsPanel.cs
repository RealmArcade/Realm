using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the brush panel needs from the editor: its node tree, the shared accordion and button styling, the
/// symmetry dropdown, status feedback, the brush-shape broadcast, and the three refreshes a block-mode toggle
/// kicks off.
/// </summary>
public sealed record BrushSettingsHooks(
	Node Owner,
	Action<Button> StyleAccordionHeader,
	Action<Button, Control, string> SetupAccordion,
	Action<Button, string, Action?, int, string> SetupOptionButton,
	Action<OptionButton, string> SetupMirrorDropdown,
	Action<string> Feedback,
	Action<bool> BrushShapeBroadcast,
	Action BlockModeChanged,
	Action<bool> SetSliderDragging);

/// <summary>
/// The brush accordion in the editor's right panel: brush size, strength, shape and symmetry, block mode with its
/// step height, and the exact height slider. It owns the accordion and every control in it; the value plumbing
/// stays in <see cref="MapEditorBrushSettings"/>, which the editor hands these controls to.
/// </summary>
public sealed class BrushSettingsPanel
{
	private const string BasePath = "RightSlidePanel/RightScroll/AccordionContainer/BrushAccordion";

	public VBoxContainer Accordion { get; private set; } = null!;
	public Button Header { get; private set; } = null!;
	public VBoxContainer Content { get; private set; } = null!;
	public Slider SizeSlider { get; private set; } = null!;
	public Label SizeValue { get; private set; } = null!;
	public Control? SizeBox { get; private set; }
	public Slider StrengthSlider { get; private set; } = null!;
	public Label StrengthValue { get; private set; } = null!;
	public Button ShapeButton { get; private set; } = null!;
	public OptionButton MirrorOption { get; private set; } = null!;
	public CheckBox BlockModeCheck { get; private set; } = null!;
	public Control StepBox { get; private set; } = null!;
	public Slider StepSlider { get; private set; } = null!;
	public Label StepValue { get; private set; } = null!;
	public Control HeightBox { get; private set; } = null!;
	public Slider HeightSlider { get; private set; } = null!;
	public Label HeightValue { get; private set; } = null!;

	public void Build(BrushSettingsHooks hooks)
	{
		BuildAccordion(hooks);
		BuildSizeControls(hooks);
		BuildStrengthControls(hooks);
		BuildShapeAndSymmetry(hooks);
		BuildBlockMode(hooks);
		BuildStepControls(hooks);
		BuildHeightControls(hooks);
	}

	private void BuildAccordion(BrushSettingsHooks hooks)
	{
		Accordion = hooks.Owner.GetNode<VBoxContainer>(BasePath);
		Header = hooks.Owner.GetNode<Button>(BasePath + "/BtnHeaderBrush");
		Content = hooks.Owner.GetNode<VBoxContainer>(BasePath + "/ContentBrush");
		hooks.StyleAccordionHeader(Header);
		hooks.SetupAccordion(Header, Content, TranslationServer.Translate("Global Brush Properties"));
	}

	private void BuildSizeControls(BrushSettingsHooks hooks)
	{
		SizeSlider = hooks.Owner.GetNode<Slider>(BasePath + "/ContentBrush/BrushSizeBox/SldBrushSize");
		SizeValue = hooks.Owner.GetNode<Label>(BasePath + "/ContentBrush/BrushSizeBox/Header/LblBrushSizeValue");
		SizeBox = hooks.Owner.GetNodeOrNull<Control>(BasePath + "/ContentBrush/BrushSizeBox");
		var lblSizeTitle = hooks.Owner.GetNodeOrNull<Label>(BasePath + "/ContentBrush/BrushSizeBox/Header/LblSizeTitle");
		if (lblSizeTitle != null) lblSizeTitle.Text = TranslationServer.Translate("Size (Shift + Mouse_Wheel)");
		SizeSlider.DragStarted += () => hooks.SetSliderDragging(true);
		SizeSlider.DragEnded += (valueChanged) => hooks.SetSliderDragging(false);
	}

	private void BuildStrengthControls(BrushSettingsHooks hooks)
	{
		StrengthSlider = hooks.Owner.GetNode<Slider>(BasePath + "/ContentBrush/BrushStrengthBox/SldBrushStrength");
		StrengthValue = hooks.Owner.GetNode<Label>(BasePath + "/ContentBrush/BrushStrengthBox/Header/LblBrushStrengthValue");
		var lblStrengthTitle = hooks.Owner.GetNodeOrNull<Label>(BasePath + "/ContentBrush/BrushStrengthBox/Header/LblStrengthTitle");
		if (lblStrengthTitle != null) lblStrengthTitle.Text = TranslationServer.Translate("Intensity (Ctrl + Mouse_Wheel)");
		StrengthSlider.DragStarted += () => hooks.SetSliderDragging(true);
		StrengthSlider.DragEnded += (valueChanged) => hooks.SetSliderDragging(false);
	}

	private void BuildShapeAndSymmetry(BrushSettingsHooks hooks)
	{
		ShapeButton = hooks.Owner.GetNode<Button>(BasePath + "/ContentBrush/ShapeMirrorGrid/BtnBrushShape");
		hooks.SetupOptionButton(ShapeButton, $"{UnicodeIcons.SQUARE} BRUSH: SQUARE", () =>
		{
			if (GameContext.Host != null)
			{
				GameContext.Host.EditorBrushIsSquare = !GameContext.Host.EditorBrushIsSquare;
				GameContext.Host.UpdateBrushMesh();
				hooks.BrushShapeBroadcast(GameContext.Host.EditorBrushIsSquare);
			}
		}, 11, "Toggle brush shape between circular and square (B)");

		MirrorOption = hooks.Owner.GetNode<OptionButton>(BasePath + "/ContentBrush/ShapeMirrorGrid/OptMirrorMode");
		hooks.SetupMirrorDropdown(MirrorOption, "Select terrain and object mirroring symmetry mode");
	}

	private void BuildBlockMode(BrushSettingsHooks hooks)
	{
		BlockModeCheck = hooks.Owner.GetNode<CheckBox>(BasePath + "/ContentBrush/ChkBlockMode");
		BlockModeCheck.Toggled += (toggled) =>
		{
			if (GameContext.Host != null)
				GameContext.Host.EditorBlockMode = toggled;

			hooks.Feedback(toggled ? TranslationServer.Translate("Block Mode: Enabled") : TranslationServer.Translate("Block Mode: Disabled"));
			hooks.BlockModeChanged();
		};
		BlockModeCheck.ButtonPressed = true;
	}

	private void BuildStepControls(BrushSettingsHooks hooks)
	{
		StepBox = hooks.Owner.GetNode<Control>(BasePath + "/ContentBrush/StepBox");
		StepSlider = hooks.Owner.GetNode<Slider>(BasePath + "/ContentBrush/StepBox/SldBlockStep");
		StepSlider.TooltipText = TranslationServer.Translate("Block step height per stroke (Alt + MouseWheel Scroll)");
		StepSlider.DragStarted += () => hooks.SetSliderDragging(true);
		StepSlider.DragEnded += (valueChanged) => hooks.SetSliderDragging(false);
		StepValue = hooks.Owner.GetNode<Label>(BasePath + "/ContentBrush/StepBox/Header/LblBlockStepValue");
		var lblStepTitle = hooks.Owner.GetNodeOrNull<Label>(BasePath + "/ContentBrush/StepBox/Header/LblStepTitle");
		if (lblStepTitle != null) lblStepTitle.Text = TranslationServer.Translate("Step Height (Alt + Mouse_Wheel)");
	}

	private void BuildHeightControls(BrushSettingsHooks hooks)
	{
		HeightBox = hooks.Owner.GetNode<Control>(BasePath + "/ContentBrush/HeightBox");
		HeightSlider = hooks.Owner.GetNode<Slider>(BasePath + "/ContentBrush/HeightBox/SldHeight");
		HeightSlider.MinValue = TerrainCell.MIN_Y;
		HeightSlider.MaxValue = TerrainCell.MAX_Y;
		HeightSlider.DragStarted += () => hooks.SetSliderDragging(true);
		HeightSlider.DragEnded += (valueChanged) => hooks.SetSliderDragging(false);
		HeightValue = hooks.Owner.GetNode<Label>(BasePath + "/ContentBrush/HeightBox/Header/LblHeightValue");
		var lblHeightTitle = hooks.Owner.GetNodeOrNull<Label>(BasePath + "/ContentBrush/HeightBox/Header/LblHeightTitle");
		if (lblHeightTitle != null) lblHeightTitle.Text = TranslationServer.Translate("Height (Alt + Mouse_Wheel)");
	}

	public void StyleControls()
	{
		if (Content != null)
		{
			MapEditorStyle.SubContainer(Content, "Brush Properties");

			MapEditorStyle.ValueBadge(SizeValue);
			MapEditorStyle.ValueBadge(StrengthValue);
			MapEditorStyle.ValueBadge(StepValue);
			MapEditorStyle.ValueBadge(HeightValue);

			MapEditorStyle.CheckBoxRow(BlockModeCheck);

			if (ShapeButton != null)
			{
				ShapeButton.CustomMinimumSize = new Vector2(0, 32);
				ShapeButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				ShapeButton.AddThemeFontSizeOverride("font_size", 10);
			}

			if (MirrorOption != null)
			{
				MirrorOption.CustomMinimumSize = new Vector2(0, 32);
				MirrorOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				MirrorOption.AddThemeFontSizeOverride("font_size", 10);
			}
		}
	}
}
