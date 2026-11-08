// Extracted from UIStyle.cs (task 7 role split).
using System;
using System.Collections.Generic;
using Godot;
using SkiaSharp;
using Realm.Client.Utils;
using Realm.Client.Services;


namespace Realm.Client.UI;

public static partial class UIStyle
{
public static void ApplyTitle(Label label, string text, int fontSize = 36)
	{
		label.Text = TranslationServer.Translate(text);
		var font = FontNorseBold;
		if (font != null)
		{
			label.AddThemeFontOverride("font", font);
		}

		label.AddThemeColorOverride("font_color", ColorGold);
		label.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.08f, 0.1f));
		label.AddThemeConstantOverride("outline_size", Mathf.Clamp(Mathf.RoundToInt(fontSize * 0.16f), 2, 6));
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
	}
}
