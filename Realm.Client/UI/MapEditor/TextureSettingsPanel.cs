using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the texture accordion needs from the editor: the option-button styling every button in it uses, the label
/// refresh the two apply checkboxes trigger, and the replace-texture dialog, which the editor owns and reuses.
/// </summary>
public sealed record TextureSettingsHooks(
	Action<Button, string, Action, int, string> SetupOptionButton,
	Action RefreshLabels,
	Action OpenReplaceTextureDialog);

/// <summary>
/// The texture paint accordion: the ground/cliff texture labels with their apply checkboxes, the global swap button,
/// the swatch grid and the scroll container that clips it, and the replace-texture button. It builds the controls and
/// exposes them; the swatch population and label refresh stay in the editor, which is where the selected indices live.
/// </summary>
public sealed class TextureSettingsPanel
{
	private readonly TextureSettingsHooks _hooks;

	public TextureSettingsPanel(TextureSettingsHooks hooks)
	{
		_hooks = hooks;
	}

	public Label TerrainLabel { get; private set; } = null!;
	public Label CliffLabel { get; private set; } = null!;
	public CheckBox GroundApply { get; private set; } = null!;
	public CheckBox CliffApply { get; private set; } = null!;
	public HBoxContainer GroundRow { get; private set; } = null!;
	public HBoxContainer CliffRow { get; private set; } = null!;
	public ScrollContainer? SwatchesScroll { get; private set; }
	public Control? SwatchesGrid { get; private set; }
	public Button ReplaceButton { get; private set; } = null!;

	/// <summary>
	/// Builds the accordion inside <paramref name="container"/>. The two apply checkboxes are built, laid out into
	/// their rows and wired here because the "at least one must stay on" rule is a property of this panel, not of the
	/// editor; the swatch grid is only located and wrapped, then left for the editor to fill.
	/// </summary>
	public void Build(VBoxContainer container)
	{
		TerrainLabel = container.GetNode<Label>("LblTerrainTexture");
		CliffLabel = container.GetNode<Label>("LblCliffTexture");

		BuildApplyToggles();

		LayoutTextureRows(container);

		BuildSwapButton(container);

		WrapSwatchGridInScrollContainer(container);

		ReplaceButton = new Button();
		ReplaceButton.Name = "BtnReplaceTexture";
		ReplaceButton.Set("icon_max_width", 0);
		_hooks.SetupOptionButton(ReplaceButton, $"{UnicodeIcons.UPLOAD} REPLACE TEXTURE", _hooks.OpenReplaceTextureDialog, 11,
			"Replace all instances of a texture with another texture across the map");
		container.AddChild(ReplaceButton);
	}

	private void BuildApplyToggles()
	{
		GroundApply = new CheckBox();
		GroundApply.Name = "ChkApplyGroundTexture";
		GroundApply.Text = TranslationServer.Translate("Ground (Q) [shift]");
		GroundApply.ButtonPressed = true;
		GroundApply.FocusMode = FocusModeEnum.None;
		GroundApply.AddThemeFontSizeOverride("font_size", 10);
		UIStyle.ApplyCheckboxStyle(GroundApply);

		CliffApply = new CheckBox();
		CliffApply.Name = "ChkApplyCliffTexture";
		CliffApply.Text = TranslationServer.Translate("Cliff (Q) [control]");
		CliffApply.ButtonPressed = true;
		CliffApply.FocusMode = FocusModeEnum.None;
		CliffApply.AddThemeFontSizeOverride("font_size", 10);
		UIStyle.ApplyCheckboxStyle(CliffApply);

		// Turning one off while the other is already off would leave nothing to paint with, so the last one on wins:
		// the other is switched back on without re-emitting the signal.
		GroundApply.Toggled += toggled =>
		{
			if (!toggled && (CliffApply == null || !CliffApply.ButtonPressed))
			{
				CliffApply?.SetPressedNoSignal(true);
			}

			_hooks.RefreshLabels();
		};

		CliffApply.Toggled += toggled =>
		{
			if (!toggled && (GroundApply == null || !GroundApply.ButtonPressed))
			{
				GroundApply?.SetPressedNoSignal(true);
			}

			_hooks.RefreshLabels();
		};
	}

	/// <summary>
	/// Moves each label out of the container and into a row beside its checkbox, restoring the label's original
	/// index so the rows sit where the labels were instead of at the bottom of the accordion.
	/// </summary>
	private void LayoutTextureRows(VBoxContainer container)
	{
		GroundRow = BuildRow(container, TerrainLabel, GroundApply, "RowGroundTexture");
		CliffRow = BuildRow(container, CliffLabel, CliffApply, "RowCliffTexture");
	}

	private static HBoxContainer BuildRow(VBoxContainer container, Label label, CheckBox checkBox, string rowName)
	{
		var row = new HBoxContainer();
		row.Name = rowName;
		row.AddThemeConstantOverride("separation", 6);

		int labelIndex = label.GetIndex();
		container.RemoveChild(label);
		row.AddChild(label);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.ClipText = true;
		label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		row.AddChild(checkBox);
		container.AddChild(row);
		container.MoveChild(row, labelIndex);
		return row;
	}

	private void BuildSwapButton(VBoxContainer container)
	{
		var btnTextureSwap = container.GetNode<Button>("BtnTextureSwap");
		_hooks.SetupOptionButton(btnTextureSwap, $"{UnicodeIcons.REFRESH} SWAP TEXTURES (GLOBAL)", () =>
		{
			if (GameContext.Host != null)
			{
				GameContext.Host.SwapTexturesExternal(GameContext.EditorService.Tools.PaintTextureIndex, GameContext.EditorService.Tools.CliffPaintTextureIndex);
			}
		}, 11, "Globally swap grass/dirt texture assignment indices (X)");
	}

	/// <summary>
	/// Puts the swatch grid inside a scroll container when the scene ships it unwrapped. The wrap keeps the grid's
	/// position in the accordion, so the palette does not jump to the bottom once it can scroll.
	/// </summary>
	private void WrapSwatchGridInScrollContainer(VBoxContainer container)
	{
		SwatchesScroll = container.GetNodeOrNull<ScrollContainer>("ScrollSwatches");
		SwatchesGrid = container.GetNodeOrNull<Control>("ScrollSwatches/GridSwatches")
		               ?? container.GetNodeOrNull<Control>("GridSwatches");

		if (SwatchesGrid != null && SwatchesScroll == null && SwatchesGrid.GetParent() is VBoxContainer parentVBox)
		{
			int gridIndex = SwatchesGrid.GetIndex();
			parentVBox.RemoveChild(SwatchesGrid);
			SwatchesScroll = new ScrollContainer();
			SwatchesScroll.Name = "ScrollSwatches";
			SwatchesScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
			SwatchesScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
			SwatchesScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			SwatchesScroll.AddChild(SwatchesGrid);
			parentVBox.AddChild(SwatchesScroll);
			parentVBox.MoveChild(SwatchesScroll, gridIndex);
		}
	}
}
