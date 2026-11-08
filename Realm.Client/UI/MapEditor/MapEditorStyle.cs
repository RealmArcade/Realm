using System;
using System.Globalization;
using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// Theme overrides shared by the map editor's chrome. These were instance methods on
/// <see cref="MapEditorHUD"/> even though they only read their arguments and the FontAwesome
/// fallback font, which is why they take that font as a parameter here rather than reaching
/// for the HUD through a back-reference.
/// </summary>
internal static class MapEditorStyle
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
			var headerNormal = new StyleBoxFlat();
			headerNormal.BgColor = new Color(0.18f, 0.16f, 0.14f, 0.95f);
			headerNormal.BorderColor = new Color(0.40f, 0.34f, 0.24f, 0.9f);
			headerNormal.SetBorderWidthAll(1);
			headerNormal.CornerRadiusTopLeft = 4;
			headerNormal.CornerRadiusTopRight = 4;
			headerNormal.CornerRadiusBottomLeft = 2;
			headerNormal.CornerRadiusBottomRight = 2;
			headerNormal.ContentMarginLeft = 10;
			headerNormal.ContentMarginRight = 10;

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
	public static void IconButton(Button btn, string iconText, string tooltipText, Font? font)
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

		var btnNormal = new StyleBoxFlat();
		btnNormal.BgColor = new Color(0.16f, 0.15f, 0.13f, 0.85f);
		btnNormal.BorderColor = new Color(0.38f, 0.32f, 0.24f, 0.7f);
		btnNormal.SetBorderWidthAll(1);
		btnNormal.SetCornerRadiusAll(4);

		var btnHover = new StyleBoxFlat();
		btnHover.BgColor = new Color(0.28f, 0.25f, 0.19f, 0.95f);
		btnHover.BorderColor = UIStyle.ColorGold;
		btnHover.SetBorderWidthAll(1);
		btnHover.SetCornerRadiusAll(4);

		var btnPressed = new StyleBoxFlat();
		btnPressed.BgColor = new Color(0.35f, 0.30f, 0.22f, 0.98f);
		btnPressed.BorderColor = UIStyle.ColorGold;
		btnPressed.SetBorderWidthAll(1);

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

		var boxStyle = new StyleBoxFlat();
		boxStyle.BgColor = new Color(0.13f, 0.12f, 0.10f, 0.7f);
		boxStyle.BorderColor = new Color(0.32f, 0.27f, 0.20f, 0.6f);
		boxStyle.SetBorderWidthAll(1);
		boxStyle.SetCornerRadiusAll(4);
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

	public static void ValueBadge(Label lbl)
	{
		if (lbl == null) return;
		var badgeStyle = new StyleBoxFlat();
		badgeStyle.BgColor = new Color(0.18f, 0.16f, 0.13f, 0.95f);
		badgeStyle.BorderColor = UIStyle.ColorGold;
		badgeStyle.SetBorderWidthAll(1);
		badgeStyle.SetCornerRadiusAll(3);
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

		var popupStyle = new StyleBoxFlat();
		popupStyle.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
		popupStyle.BorderColor = UIStyle.ColorGold;
		popupStyle.SetBorderWidthAll(1);
		popupStyle.SetCornerRadiusAll(4);
		popupStyle.ContentMarginLeft = 8;
		popupStyle.ContentMarginRight = 8;
		popupStyle.ContentMarginTop = 6;
		popupStyle.ContentMarginBottom = 6;

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
}
