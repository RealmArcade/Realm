using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the paste panel needs from the editor: its node tree to look the scene controls up in, the shared button
/// and symmetry styling, the two cycles that own paste anchor and reflection state, and the status/drag plumbing.
/// </summary>
public sealed record PasteSettingsHooks(
	Node Owner,
	Action<Button, string, Action?, int, string> SetupOptionButton,
	Action<OptionButton, string> SetupMirrorDropdown,
	Action CycleReflection,
	Action CycleAnchor,
	Action<bool> BrushShapeBroadcast,
	Action<string> Feedback,
	Action<bool> SetSliderDragging);

/// <summary>
/// The paste section of the editor's tool settings: which layers a paste covers, the rotation slider, the
/// clipboard brush shape and symmetry, the reflection and anchor cycles, and the telemetry line. It builds and
/// owns those controls; the state they drive stays where it already lives, in the editor's tool settings and on
/// the clipboard, so this class only reads and writes through the hooks it is given.
/// </summary>
public sealed class PasteSettingsPanel
{
	private const string ContainerPath =
		"RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPaste";

	public VBoxContainer Container { get; private set; } = null!;
	public Label RotationLabel { get; private set; } = null!;
	public HSlider RotationSlider { get; private set; } = null!;
	public Button ClipboardBrushShape { get; private set; } = null!;
	public OptionButton ClipboardMirrorMode { get; private set; } = null!;
	public Button PasteReflection { get; private set; } = null!;
	public Button PasteAnchor { get; private set; } = null!;
	public Label Telemetry { get; private set; } = null!;

	public void Build(PasteSettingsHooks hooks)
	{
		BuildLayerToggles(hooks);
		BuildRotationAndClipboardControls(hooks);
		AssembleOptionsBox();
	}

	private void BuildLayerToggles(PasteSettingsHooks hooks)
	{
		Container = hooks.Owner.GetNode<VBoxContainer>(ContainerPath);
		var chkPasteHeights = hooks.Owner.GetNode<CheckBox>(ContainerPath + "/PasteOptionsBox/ChkPasteHeights");
		chkPasteHeights.Text = TranslationServer.Translate("HeightMap");
		chkPasteHeights.ButtonPressed = true;
		var chkPasteTextures = hooks.Owner.GetNode<CheckBox>(ContainerPath + "/PasteOptionsBox/ChkPasteTextures");
		chkPasteTextures.Text = TranslationServer.Translate("Textures");
		chkPasteTextures.ButtonPressed = true;
		var chkPasteEntities = hooks.Owner.GetNode<CheckBox>(ContainerPath + "/PasteOptionsBox/ChkPasteEntities");
		chkPasteEntities.Text = TranslationServer.Translate("Objects");
		chkPasteEntities.ButtonPressed = true;
		var chkPastePathing = hooks.Owner.GetNode<CheckBox>(ContainerPath + "/PasteOptionsBox/ChkPastePathing");
		chkPastePathing.Text = TranslationServer.Translate("Pathing");
		chkPastePathing.ButtonPressed = true;

		chkPasteTextures.Toggled += (toggled) =>
		{
			if (GameContext.Host != null) GameContext.EditorService.Tools.PasteTextures = toggled;
		};
		chkPasteHeights.Toggled += (toggled) =>
		{
			if (GameContext.Host != null) GameContext.EditorService.Tools.PasteHeights = toggled;
		};
		chkPasteEntities.Toggled += (toggled) =>
		{
			if (GameContext.Host != null) GameContext.EditorService.Tools.PasteEntities = toggled;
		};
		chkPastePathing.Toggled += (toggled) =>
		{
			if (GameContext.Host != null) GameContext.Host.PasteOptionPathing = toggled;
		};
	}

	private void BuildRotationAndClipboardControls(PasteSettingsHooks hooks)
	{
		RotationLabel = hooks.Owner.GetNode<Label>(ContainerPath + "/PasteOptionsBox/PasteRotationBox/Header/LblPasteRotationValue");
		RotationSlider = hooks.Owner.GetNode<HSlider>(ContainerPath + "/PasteOptionsBox/PasteRotationBox/SldPasteRotation");
		RotationSlider.ValueChanged += (val) =>
		{
			float fVal = (float)val;
			RotationLabel.Text = fVal.ToString("F0") + "°";
			if (GameContext.Host != null)
			{
				GameContext.EditorService.Tools.PasteRotation = fVal;
			}
		};
		RotationSlider.DragStarted += () => hooks.SetSliderDragging(true);
		RotationSlider.DragEnded += (valueChanged) => hooks.SetSliderDragging(false);

		ClipboardBrushShape = new Button();
		ClipboardBrushShape.Name = "BtnClipboardBrushShape";
		ClipboardBrushShape.Set("icon_max_width", 0);
		hooks.SetupOptionButton(ClipboardBrushShape, GameContext.Host != null && !GameContext.Host.EditorBrushIsSquare ? $"{UnicodeIcons.CIRCLE} BRUSH: CIRCLE" : $"{UnicodeIcons.SQUARE} BRUSH: SQUARE", () =>
		{
			if (GameContext.Host != null)
			{
				GameContext.Host.EditorBrushIsSquare = !GameContext.Host.EditorBrushIsSquare;
				hooks.BrushShapeBroadcast(GameContext.Host.EditorBrushIsSquare);
				hooks.Feedback(GameContext.Host.EditorBrushIsSquare ? TranslationServer.Translate("Brush Shape: SQUARE") : TranslationServer.Translate("Brush Shape: CIRCLE"));
			}
		}, 10, "Toggle square / circular selection and clipboard brush shape");

		ClipboardMirrorMode = new OptionButton();
		ClipboardMirrorMode.Name = "OptClipboardMirrorMode";
		ClipboardMirrorMode.Set("icon_max_width", 0);
		hooks.SetupMirrorDropdown(ClipboardMirrorMode, "Select terrain, clipboard and object mirroring symmetry mode");

		PasteReflection = new Button();
		PasteReflection.Name = "BtnPasteReflection";
		PasteReflection.Set("icon_max_width", 0);
		hooks.SetupOptionButton(PasteReflection, $"{UnicodeIcons.ARROWS_H} REFLECT: NONE", hooks.CycleReflection, 11, "Cycle reflection mode for paste operation (None, Horizontal, Vertical)");

		PasteAnchor = new Button();
		PasteAnchor.Name = "BtnPasteAnchor";
		PasteAnchor.Set("icon_max_width", 0);
		hooks.SetupOptionButton(PasteAnchor, $"{UnicodeIcons.MOUSE_POINTER} ANCHOR: CENTER", hooks.CycleAnchor, 11, "Cycle pivot / anchor tile used to align pasted selection (Center, Corners)");
	}

	private void AssembleOptionsBox()
	{
		Telemetry = new Label();
		Telemetry.Name = "LblPasteTelemetry";
		Telemetry.AddThemeFontSizeOverride("font_size", 10);
		Telemetry.AddThemeColorOverride("font_color", new Color(0.3f, 0.85f, 1.0f));
		Telemetry.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		Telemetry.CustomMinimumSize = new Vector2(200, 0);
		Telemetry.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		Telemetry.Text = "";

		var pasteOptionsBox = Container.GetNodeOrNull<VBoxContainer>("PasteOptionsBox");
		if (pasteOptionsBox != null)
		{
			var shapeMirrorGrid = new GridContainer();
			shapeMirrorGrid.Name = "ClipboardShapeMirrorGrid";
			shapeMirrorGrid.Columns = 2;
			shapeMirrorGrid.AddThemeConstantOverride("h_separation", 6);
			shapeMirrorGrid.AddThemeConstantOverride("v_separation", 4);
			shapeMirrorGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			shapeMirrorGrid.AddChild(ClipboardBrushShape);
			shapeMirrorGrid.AddChild(ClipboardMirrorMode);
			pasteOptionsBox.AddChild(shapeMirrorGrid);

			pasteOptionsBox.AddChild(PasteReflection);
			pasteOptionsBox.AddChild(PasteAnchor);
			pasteOptionsBox.AddChild(Telemetry);
		}
	}
}
