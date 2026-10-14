using System;
using Godot;

namespace Realm.Client.UI;

public static class UIHelper
{
	public static void SetupHUDButton(Button btn, Texture2D icon, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = icon;
		
		string trans = TranslationServer.Translate(tooltip);
		btn.TooltipText = string.IsNullOrEmpty(trans) ? tooltip : trans;

		btn.CustomMinimumSize = new Vector2(44, 44);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 38);

		AddHotkeyLabel(btn, tooltip);

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		if (onClick != null)
		{
			btn.Pressed += () => onClick.Invoke();
		}
	}

	public static void AddHotkeyLabel(Button btn, string tooltip)
	{
		if (tooltip.StartsWith('[') && tooltip.Contains(']'))
		{
			int end = tooltip.IndexOf(']');
			string hotkeyText = tooltip.Substring(1, end - 1);
			var hotkeyLabel = new Label();
			hotkeyLabel.Name = "HotkeyLabel";
			hotkeyLabel.Text = hotkeyText;
			hotkeyLabel.AddThemeFontSizeOverride("font_size", 10);
			hotkeyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			hotkeyLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
			hotkeyLabel.AddThemeConstantOverride("outline_size", 4);
			hotkeyLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
			hotkeyLabel.OffsetLeft = 4;
			hotkeyLabel.OffsetTop = 3;
			hotkeyLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
			btn.AddChild(hotkeyLabel);
		}
	}
}
