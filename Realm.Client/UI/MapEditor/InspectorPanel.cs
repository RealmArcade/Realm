using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the inspector needs from the editor: its node tree, the shared button styling, the FontAwesome face, the
/// selection actions its bar fires, and the three steps the editor runs in the middle of the build (dialog
/// creation, water swatches and editor preferences) which the inspector does not own.
/// </summary>
public sealed record InspectorPanelHooks(
	Node Owner,
	Action<Button, string, Action?, int, string> SetupButton,
	Func<Font?> FontAwesomeFont,
	Action<float> RotateSelected,
	Action<float> ScaleSelected,
	Action LocateSelected,
	Action DeleteSelected,
	Action CreateEditorDialogs,
	Action RefreshWaterSwatches,
	Action ApplyEditorPreferences,
	Action EditTemplateRequested,
	Action EditVfxRequested,
	Func<bool> IsUpdatingInspectorUi);

/// <summary>
/// The selected-object inspector in the editor's right panel: the rotate/scale/erase bar, the range-coverage toggle,
/// the player-owner row, the rig status label, and the "Edit Template" / "Edit VFX" actions. It owns those nodes; the
/// dialog creation, water swatch refresh and preference pass that sit between them in the build stay on the editor
/// and arrive as hooks so their position in the child order is unchanged.
/// </summary>
public sealed class InspectorPanel
{
	private const string BasePath = "RightSlidePanel/RightScroll/AccordionContainer/InspectorAccordion/ContentInspector";
	private const string PanelPath = BasePath + "/InspectorPanel";
	private const string VBoxPath = PanelPath + "/VBox";
	private const string GridPath = BasePath + "/InspBox_VBoxContainer/InspGrid_GridContainer";

	public PanelContainer Root { get; private set; } = null!;
	public Label Title { get; private set; } = null!;
	public Label PositionLabel { get; private set; } = null!;
	public Button RotateLeftButton { get; private set; } = null!;
	public Button RotateRightButton { get; private set; } = null!;
	public Button ScaleDownButton { get; private set; } = null!;
	public Button ScaleUpButton { get; private set; } = null!;
	public Button ScaleResetButton { get; private set; } = null!;
	public Button LocateButton { get; private set; } = null!;
	public Button DeleteButton { get; private set; } = null!;
	public Button CoverageButton { get; private set; } = null!;
	public HBoxContainer PlayerOwnerContainer { get; private set; } = null!;
	public OptionButton PlayerOwnerOption { get; private set; } = null!;
	public PanelContainer RigStatusContainer { get; private set; } = null!;
	public Label RigStatusLabel { get; private set; } = null!;
	public Button EditTemplateButton { get; private set; } = null!;
	public Button EditVfxButton { get; private set; } = null!;

	public void Build(InspectorPanelHooks hooks)
	{
		var inspectorVBox = hooks.Owner.GetNode<VBoxContainer>(VBoxPath);

		BuildActionBar(hooks);

		BuildCoverageToggle(hooks, inspectorVBox);

		BuildPlayerOwnerRow(hooks, inspectorVBox);

		BuildRigStatusLabel(inspectorVBox);

		hooks.CreateEditorDialogs();
		hooks.RefreshWaterSwatches();
		hooks.ApplyEditorPreferences();

		BuildActionButtons(hooks, inspectorVBox);
	}

	private void BuildActionBar(InspectorPanelHooks hooks)
	{
		Root = hooks.Owner.GetNode<PanelContainer>(PanelPath);
		Title = hooks.Owner.GetNode<Label>(VBoxPath + "/LblInspectorTitle");
		PositionLabel = hooks.Owner.GetNode<Label>(VBoxPath + "/LblInspectorPos");
		RotateLeftButton = hooks.Owner.GetNode<Button>(GridPath + "/BtnInspectorRotLeft");
		hooks.SetupButton(RotateLeftButton, $"{UnicodeIcons.UNDO} ROT -15°", () => hooks.RotateSelected(-15f), 11, "Rotate object counter-clockwise (Shift + Mouse_Wheel)");
		RotateRightButton = hooks.Owner.GetNode<Button>(GridPath + "/BtnInspectorRotRight");
		hooks.SetupButton(RotateRightButton, $"{UnicodeIcons.ROTATE} ROT +15°", () => hooks.RotateSelected(15f), 11, "Rotate object clockwise (Shift + Mouse_Wheel)");
		ScaleDownButton = hooks.Owner.GetNode<Button>(GridPath + "/BtnInspectorScaleDown");
		hooks.SetupButton(ScaleDownButton, $"{UnicodeIcons.MINUS} SCALE DOWN", () => hooks.ScaleSelected(0.9f), 11, "Shrink object size by 10% (Alt + Mouse_Wheel)");
		ScaleUpButton = hooks.Owner.GetNode<Button>(GridPath + "/BtnInspectorScaleUp");
		hooks.SetupButton(ScaleUpButton, $"{UnicodeIcons.PLUS} SCALE UP", () => hooks.ScaleSelected(1.1f), 11, "Enlarge object size by 10% (Alt + Mouse_Wheel)");
		ScaleResetButton = hooks.Owner.GetNode<Button>(GridPath + "/BtnInspectorScaleReset");
		hooks.SetupButton(ScaleResetButton, $"{UnicodeIcons.UNDO} RESET SCALE", () => hooks.ScaleSelected(-1f), 12, "Reset object scale size to 1.0x");

		LocateButton = hooks.Owner.GetNode<Button>(VBoxPath + "/BtnCenter");
		hooks.SetupButton(LocateButton, $"{UnicodeIcons.CROSSHAIRS_TARGET} LOCATE OBJECT", () => hooks.LocateSelected(), 12, "Center camera on selected object");
		DeleteButton = hooks.Owner.GetNode<Button>(GridPath + "/BtnInspectorDelete");
		hooks.SetupButton(DeleteButton, $"{UnicodeIcons.TRASH_ALT} ERASE", () => hooks.DeleteSelected(), 12, "Erase selected unit, prop, or decal");
	}

	private void BuildCoverageToggle(InspectorPanelHooks hooks, VBoxContainer inspectorVBox)
	{
		CoverageButton = new Button();
		CoverageButton.Name = "BtnShowCoverage_Button";
		CoverageButton.Text = TranslationServer.Translate($"{UnicodeIcons.EYE} RANGES: OFF");
		var fontCoverage = hooks.FontAwesomeFont();
		if (fontCoverage != null) CoverageButton.AddThemeFontOverride("font", fontCoverage);
		CoverageButton.ToggleMode = true;
		CoverageButton.FocusMode = Control.FocusModeEnum.None;
		CoverageButton.AddThemeFontSizeOverride("font_size", 11);
		CoverageButton.ButtonPressed = false;
		CoverageButton.Toggled += (pressed) =>
		{
			if (GameContext.Host != null)
			{
				GameContext.Host.EditorCoverageOverlayEnabled = pressed;
				CoverageButton.Text = pressed ? TranslationServer.Translate($"{UnicodeIcons.EYE} RANGES: ON") : TranslationServer.Translate($"{UnicodeIcons.EYE} RANGES: OFF");
				GameContext.Host.UpdateEditorCoverageOverlay();
			}
		};
		inspectorVBox.AddChild(CoverageButton);
	}

	private void BuildPlayerOwnerRow(InspectorPanelHooks hooks, VBoxContainer inspectorVBox)
	{
		PlayerOwnerContainer = new HBoxContainer();
		PlayerOwnerContainer.Name = "PlayerOwnerContainer";
		PlayerOwnerContainer.Visible = false;

		var lblPlayer = new Label();
		lblPlayer.Name = "LblPlayer_Label";
		lblPlayer.Text = TranslationServer.Translate("Player");
		lblPlayer.CustomMinimumSize = new Vector2(50, 0);
		lblPlayer.AddThemeFontSizeOverride("font_size", 11);
		PlayerOwnerContainer.AddChild(lblPlayer);

		PlayerOwnerOption = new OptionButton();
		PlayerOwnerOption.Name = "OptPlayerOwner";
		PlayerOwnerOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		PlayerOwnerOption.AddThemeFontSizeOverride("font_size", 11);

		for (int i = 0; i < PlayerColorConfig.Palette.Length; i++)
		{
			PlayerOwnerOption.AddItem(PlayerColorConfig.GetPaletteLabel(i), i);
		}

		PlayerOwnerOption.ItemSelected += (long index) =>
		{
			var host = GameContext.Host;
			if (host == null || hooks.IsUpdatingInspectorUi()) return;
			if (host.SelectedEditorObject is Unit3D unit && GodotObject.IsInstanceValid(unit))
			{
				host.SetUnitPlayerExternal(unit, (int)index);
			}
		};

		PlayerOwnerContainer.AddChild(PlayerOwnerOption);
		inspectorVBox.AddChild(PlayerOwnerContainer);
		inspectorVBox.MoveChild(PlayerOwnerContainer, 2);
	}

	private void BuildRigStatusLabel(VBoxContainer inspectorVBox)
	{
		RigStatusContainer = new PanelContainer();
		RigStatusContainer.Name = "RigStatusContainer";
		RigStatusContainer.Visible = false;
		RigStatusLabel = new Label();
		RigStatusLabel.Name = "LblRigStatus";
		RigStatusLabel.AddThemeFontSizeOverride("font_size", 11);
		RigStatusLabel.AutowrapMode = TextServer.AutowrapMode.Word;
		RigStatusLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		RigStatusContainer.AddChild(RigStatusLabel);
		inspectorVBox.AddChild(RigStatusContainer);
	}

	private void BuildActionButtons(InspectorPanelHooks hooks, VBoxContainer inspectorVBox)
	{
		EditTemplateButton = new Button();
		EditTemplateButton.Name = "BtnOpenGlobalOverrides";
		EditTemplateButton.Set("icon_max_width", 0);
		EditTemplateButton.Text = "✏️ " + TranslationServer.Translate("Edit Template");
		EditTemplateButton.AddThemeFontSizeOverride("font_size", 11);
		EditTemplateButton.FocusMode = Control.FocusModeEnum.None;
		EditTemplateButton.CustomMinimumSize = new Vector2(0, 28);
		EditTemplateButton.Visible = false;
		EditTemplateButton.Pressed += () => hooks.EditTemplateRequested();
		inspectorVBox.AddChild(EditTemplateButton);

		EditVfxButton = new Button();
		EditVfxButton.Name = "BtnEditVfx";
		EditVfxButton.Set("icon_max_width", 0);
		EditVfxButton.Text = "✨ " + TranslationServer.Translate("Edit VFX");
		EditVfxButton.AddThemeFontSizeOverride("font_size", 11);
		EditVfxButton.FocusMode = Control.FocusModeEnum.None;
		EditVfxButton.CustomMinimumSize = new Vector2(0, 28);
		EditVfxButton.Visible = false;
		EditVfxButton.Pressed += () => hooks.EditVfxRequested();
		inspectorVBox.AddChild(EditVfxButton);
	}
	/// <summary>Row styling for the inspector's action buttons, which this panel owns.</summary>
	public void StyleButtons()
	{
		MapEditorStyle.RowButton(RotateLeftButton);
		MapEditorStyle.RowButton(RotateRightButton);
		MapEditorStyle.RowButton(ScaleDownButton);
		MapEditorStyle.RowButton(ScaleUpButton);
		MapEditorStyle.RowButton(ScaleResetButton);
		MapEditorStyle.RowButton(DeleteButton);
	}

}
