using System;
using System.Globalization;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// Theme overrides shared by the map editor's chrome. These were instance methods on
/// <see cref="MapEditorHUD"/> even though they only read their arguments and the FontAwesome
/// fallback font, which is why they take that font as a parameter here rather than reaching
/// for the HUD through a back-reference.
/// </summary>
public static class MapEditorStyle
{
	/// <summary>
	/// Accordion headers: a textured plate when the map editor header art is available, and a
	/// flat panel of the same proportions when it is not, so an accordion that fails to load
	/// its texture still lines up with the ones that did.
	/// </summary>
	public static void AccordionHeader(Button btn, Font? font)
	{
		if (btn == null) return;
		btn.Flat = false;
		btn.CustomMinimumSize = new Vector2(0, 36);

		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}

		var headerTex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_options_header.webp");
		if (headerTex != null)
		{
			var headerNormal = new StyleBoxTexture();
			headerNormal.Texture = headerTex;
			headerNormal.TextureMarginLeft = 0;
			headerNormal.TextureMarginRight = 0;
			headerNormal.TextureMarginTop = 0;
			headerNormal.TextureMarginBottom = 0;
			headerNormal.ContentMarginLeft = 12;
			headerNormal.ContentMarginRight = 12;
			headerNormal.ContentMarginTop = 6;
			headerNormal.ContentMarginBottom = 6;

			var headerHover = new StyleBoxTexture();
			headerHover.Texture = headerTex;
			headerHover.ModulateColor = new Color(1.2f, 1.15f, 0.9f, 1.0f);
			headerHover.TextureMarginLeft = 0;
			headerHover.TextureMarginRight = 0;
			headerHover.TextureMarginTop = 0;
			headerHover.TextureMarginBottom = 0;
			headerHover.ContentMarginLeft = 12;
			headerHover.ContentMarginRight = 12;
			headerHover.ContentMarginTop = 6;
			headerHover.ContentMarginBottom = 6;

			btn.AddThemeStyleboxOverride("normal", headerNormal);
			btn.AddThemeStyleboxOverride("hover", headerHover);
			btn.AddThemeStyleboxOverride("pressed", headerHover);
		}
		else
		{
			var headerNormal = UIStyle.CreateEditorAccordionHeader();

			btn.AddThemeStyleboxOverride("normal", headerNormal);
			btn.AddThemeStyleboxOverride("hover", headerNormal);
			btn.AddThemeStyleboxOverride("pressed", headerNormal);
		}

		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", 12);
		btn.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		btn.AddThemeColorOverride("font_hover_color", new Color(1f, 0.95f, 0.8f));
		btn.Alignment = HorizontalAlignment.Left;
	}

	/// <summary>
	/// Square icon buttons: takes the glyph, not the button's existing text, because the caller
	/// owns what the icon means and the font is the same FontAwesome set for all of them.
	/// </summary>
	public static void IconButton(Button? btn, string iconText, string tooltipText, Font? font)
	{
		if (btn == null) return;
		btn.Text = iconText;
		btn.TooltipText = tooltipText;
		btn.Flat = false;
		btn.CustomMinimumSize = new Vector2(30, 30);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}

		var btnNormal = UIStyle.CreateEditorIconButtonNormal();
		var btnHover = UIStyle.CreateEditorIconButtonHover();
		var btnPressed = UIStyle.CreateEditorIconButtonPressed();

		btn.AddThemeStyleboxOverride("normal", btnNormal);
		btn.AddThemeStyleboxOverride("hover", btnHover);
		btn.AddThemeStyleboxOverride("pressed", btnPressed);
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", 14);
		btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.82f));
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btn.Alignment = HorizontalAlignment.Center;
	}

	public static void SubContainer(Control boxNode, string? headerText = null)
	{
		if (boxNode == null) return;

		var boxStyle = UIStyle.CreateEditorSubPanel();

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

	public static void ValueBadge(Label lbl)
	{
		if (lbl == null) return;
		var badgeStyle = UIStyle.CreateEditorValueBadge();

		lbl.AddThemeStyleboxOverride("normal", badgeStyle);
		lbl.AddThemeFontSizeOverride("font_size", 11);
		lbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		lbl.HorizontalAlignment = HorizontalAlignment.Center;
		lbl.VerticalAlignment = VerticalAlignment.Center;
	}

	/// <summary>
	/// The top bar's textured plate. The compact variant used for the grouped toolbar buttons is the same plate with
	/// tighter side padding, so both go through here rather than carrying a copy of the four styleboxes each.
	/// </summary>
	public static void TopButton(Button btn, int horizontalMargin = 12)
	{
		if (btn == null) return;

		var tex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_button.webp");
		if (tex != null)
		{
			btn.AddThemeStyleboxOverride("normal", TopButtonPlate(tex, new Color(1, 1, 1, 1), horizontalMargin));
			btn.AddThemeStyleboxOverride("hover", TopButtonPlate(tex, new Color(1.25f, 1.2f, 1.0f, 1.0f), horizontalMargin));
			btn.AddThemeStyleboxOverride("pressed", TopButtonPlate(tex, new Color(0.85f, 0.8f, 0.7f, 1.0f), horizontalMargin));
		}

		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	private static StyleBoxTexture TopButtonPlate(Texture2D tex, Color modulate, int horizontalMargin)
	{
		var style = new StyleBoxTexture();
		style.Texture = tex;
		style.ModulateColor = modulate;
		style.TextureMarginLeft = 0;
		style.TextureMarginRight = 0;
		style.TextureMarginTop = 0;
		style.TextureMarginBottom = 0;
		style.ContentMarginLeft = horizontalMargin;
		style.ContentMarginRight = horizontalMargin;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 6;
		return style;
	}

	/// <summary>
	/// The primary button used in the editor's dialogs: taller than an option button and drawn with the standard button
	/// plates. The click sound lives here because every one of these buttons is a command.
	/// </summary>
	public static void DialogButton(Button btn, string text, Action? onClick, int fontSize, string tooltip, Font? font)
	{
		if (btn == null) return;

		btn.Text = text;
		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}

		btn.CustomMinimumSize = new Vector2(0, 32);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(btn);
		btn.AddThemeFontSizeOverride("font_size", fontSize);
		btn.FocusMode = Control.FocusModeEnum.None;
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

	/// <summary>
	/// The option button used throughout the editor's cards and dialogs: gold on hover, no focus ring, and a translated
	/// tooltip. The tooltip is the only thing it translates — the label is the caller's own text.
	/// </summary>
	public static void OptionButton(Button btn, string text, Action? onClick, int fontSize, string tooltip, Font? font)
	{
		if (btn == null) return;

		btn.Text = text;
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
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.MouseFilter = Control.MouseFilterEnum.Stop;
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

	/// <summary>
	/// A button whose face is an image rather than a glyph: the texture is inset on all four sides and the icon dims to
	/// gold-dull until hovered, so the toolbar reads as dark plates with lit icons. The existing
	/// <see cref="IconButton"/> is the glyph version of this.
	/// </summary>
	public static void IconButtonFromPath(Button btn, string iconPath, Action onClick, string tooltip)
	{
		if (btn == null) return;

		btn.Flat = false;
		btn.Text = "";
		btn.Icon = null;
		btn.CustomMinimumSize = new Vector2(36, 32);
		btn.FocusMode = Control.FocusModeEnum.None;
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
		iconRect.MouseFilter = Control.MouseFilterEnum.Ignore;
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

	/// <summary>
	/// A checkbox that reads as a row in a settings column: a full-height hit area, the panel's small text, and the gold
	/// hover the rest of the editor uses. Shared because the panels and the editor both style checkboxes they own.
	/// </summary>
	public static void CheckBoxRow(CheckBox chk)
	{
		if (chk == null) return;
		chk.CustomMinimumSize = new Vector2(0, 28);
		chk.AddThemeFontSizeOverride("font_size", 11);
		chk.AddThemeColorOverride("font_color", new Color(0.88f, 0.84f, 0.78f));
		UIStyle.ApplyGoldHoverColors(chk);
	}

	public static void OptionButtonsInContainer(Control container, Font? font)
	{
		if (container == null) return;
		foreach (Node child in container.GetChildren())
		{
			if (child is OptionButton optBtn)
			{
				OptionButtonPopup(optBtn, font);
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
				OptionButtonsInContainer(subContainer, font);
			}
		}
	}

	public static Control? ContentTarget(Control? contentControl)
	{
		if (contentControl == null) return null;
		var inner = contentControl.GetNodeOrNull<Control>("CardScroll/InnerVBox");
		return inner ?? contentControl;
	}

	public static void OptionButtonPopup(OptionButton optBtn, Font? font)
	{
		if (optBtn == null) return;
		if (font != null)
		{
			optBtn.AddThemeFontOverride("font", font);
		}

		PopupMenuTheme(optBtn.GetPopup(), font);
	}

	public static void PopupMenuTheme(PopupMenu popup, Font? font)
	{
		if (popup == null) return;
		if (font != null)
		{
			popup.AddThemeFontOverride("font", font);
		}

		popup.AddThemeFontSizeOverride("font_size", 12);

		var popupStyle = UIStyle.CreateEditorPopupPanel();

		popup.AddThemeStyleboxOverride("panel", popupStyle);
		popup.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.82f));
		popup.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
	}

	public static HSlider SliderRow(VBoxContainer parent, string labelText, float min, float max, float step, float initialVal, Action<float> onChanged, Action<bool> onDragState, string format = "0.0#", float labelWidth = 70f)
	{
		var row = new HBoxContainer();
		row.Name = "Row_HBoxContainer";

		var lblName = new Label();
		lblName.Name = "LblName_Label";
		lblName.Text = labelText;
		lblName.CustomMinimumSize = new Vector2(labelWidth, 0);
		lblName.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lblName);

		var slider = new HSlider();
		slider.Name = "Slider_HSlider";
		slider.MinValue = min;
		slider.MaxValue = max;
		slider.Step = step;
		slider.Value = initialVal;
		slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		slider.DragStarted += () => onDragState(true);
		slider.DragEnded += (valueChanged) => onDragState(false);
		row.AddChild(slider);

		var lblVal = new Label();
		lblVal.Name = "LblVal_Label";
		lblVal.Text = initialVal.ToString(format, CultureInfo.InvariantCulture);
		lblVal.CustomMinimumSize = new Vector2(34, 0);
		lblVal.HorizontalAlignment = HorizontalAlignment.Right;
		lblVal.AddThemeFontSizeOverride("font_size", 11);
		row.AddChild(lblVal);

		slider.SetMeta("val_label", lblVal);
		slider.SetMeta("val_format", format);

		slider.ValueChanged += (double val) =>
		{
			float fVal = (float)val;
			lblVal.Text = fVal.ToString(format, CultureInfo.InvariantCulture);
			onChanged(fVal);
		};

		parent.AddChild(row);
		return slider;
	}

	/// <summary>
	/// The tinted plate behind a palette row. Both terrain rows want the same plate in different colours and the
	/// plate is created on first use, so the panel is reused if a later pass runs again.
	/// </summary>
	public static void RowBackground(Control row, Color bgColor, Color borderColor)
	{
		if (row == null) return;

		var style = UIStyle.CreateEditorRowPlate(bgColor, borderColor);

		var panel = row.GetNodeOrNull<Panel>("RowBG");
		if (panel == null)
		{
			panel = new Panel();
			panel.Name = "RowBG";
			panel.ShowBehindParent = true;
			panel.MouseFilter = Control.MouseFilterEnum.Ignore;
			panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			row.AddChild(panel);
			row.MoveChild(panel, 0);
		}

		panel.AddThemeStyleboxOverride("panel", style);
	}
	/// <summary>
	/// The row button used for every list entry in the editor's panels: a flat plate with a bottom rule, a gold hover
	/// and a left-aligned label. Shared because a dozen panels draw their rows with it and they must stay identical.
	/// </summary>
	public static void RowButton(Button btn)
	{
		if (btn == null) return;
		btn.Flat = false;
		btn.CustomMinimumSize = new Vector2(0, 32);

		var rowNormal = UIStyle.CreateEditorRowButtonNormal();
		var rowHover = UIStyle.CreateEditorRowButtonHover();
		var rowPressed = UIStyle.CreateEditorRowButtonPressed();

		btn.AddThemeStyleboxOverride("normal", rowNormal);
		btn.AddThemeStyleboxOverride("hover", rowHover);
		btn.AddThemeStyleboxOverride("pressed", rowPressed);
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", 12);
		btn.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.82f));
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btn.Alignment = HorizontalAlignment.Left;
	}

}
