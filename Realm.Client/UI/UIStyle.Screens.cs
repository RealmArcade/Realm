// Extracted from UIStyle.cs (task 7 role split: per-screen plates).
using System;
using System.Collections.Generic;
using Godot;
using SkiaSharp;
using Realm.Client.Utils;
using Realm.Client.Services;

namespace Realm.Client.UI;

public static partial class UIStyle
{
	/// <summary>The round plate behind a creator avatar that has no picture yet.</summary>
	public static StyleBoxFlat CreateDiscoveryAvatarPlate(int size)
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.08f, 0.09f, 0.12f, 0.9f);
		style.BorderColor = ColorCyanGlowDim;
		style.SetBorderWidthAll(2);
		style.CornerRadiusTopLeft = size / 2;
		style.CornerRadiusTopRight = size / 2;
		style.CornerRadiusBottomLeft = size / 2;
		style.CornerRadiusBottomRight = size / 2;
		return style;
	}

	/// <summary>The plate behind a map thumbnail that has no picture yet.</summary>
	public static StyleBoxFlat CreateDiscoveryThumbnailPlate()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.06f, 0.07f, 0.09f, 0.95f);
		style.BorderColor = new Color(0.25f, 0.27f, 0.32f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(6);
		return style;
	}

	/// <summary>A discovery card. The two screens that draw one want the same shape in different colours.</summary>
	public static StyleBoxFlat CreateDiscoveryCard(Color bgColor, Color borderColor)
	{
		var style = new StyleBoxFlat();
		style.BgColor = bgColor;
		style.BorderColor = borderColor;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(8);
		style.ContentMarginLeft = 18;
		style.ContentMarginRight = 18;
		style.ContentMarginTop = 14;
		style.ContentMarginBottom = 14;
		return style;
	}

	/// <summary>The discovery screen's top bar: bronze rule along the bottom only.</summary>
	public static StyleBoxFlat CreateDiscoveryHeaderPanel()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.08f, 0.09f, 0.11f, 0.95f);
		style.BorderColor = ColorBronze;
		style.BorderWidthBottom = 2;
		style.ContentMarginLeft = 20;
		style.ContentMarginRight = 20;
		style.ContentMarginTop = 12;
		style.ContentMarginBottom = 12;
		return style;
	}

	/// <summary>The gold-edged label that marks a greenlit creator or asset.</summary>
	public static StyleBoxFlat CreateDiscoveryBadge()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.18f, 0.15f, 0.10f, 0.9f);
		style.BorderColor = ColorGoldDull;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 4;
		style.ContentMarginBottom = 4;
		return style;
	}

	/// <summary>The cyan pill that carries a creator's play time.</summary>
	public static StyleBoxFlat CreateDiscoveryChip()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.10f, 0.18f, 0.24f, 0.7f);
		style.BorderColor = ColorCyanGlowDim;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(10);
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		style.ContentMarginTop = 4;
		style.ContentMarginBottom = 4;
		return style;
	}

	/// <summary>The bronze-ruled bar a shortcut group is named under in the hotkey reference.</summary>
	public static StyleBoxFlat CreateHotkeyGroupHeaderPlate()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.18f, 0.16f, 0.14f, 0.85f);
		style.BorderColor = new Color(0.6f, 0.5f, 0.35f, 0.5f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(3);
		style.ContentMarginLeft = 6;
		style.ContentMarginRight = 6;
		style.ContentMarginTop = 3;
		style.ContentMarginBottom = 3;
		return style;
	}

	/// <summary>
	/// The plate behind one shortcut row. Rows alternate between two tints so a long group stays readable, so the tint is
	/// chosen here from the row index rather than picked at the call site.
	/// </summary>
	public static StyleBoxFlat CreateHotkeyRowPlate(int rowIndex)
	{
		var style = new StyleBoxFlat();
		style.BgColor = (rowIndex % 2 == 0)
			? new Color(0.12f, 0.13f, 0.15f, 0.35f)
			: new Color(0.06f, 0.07f, 0.08f, 0.15f);
		style.SetCornerRadiusAll(2);
		style.ContentMarginLeft = 4;
		style.ContentMarginRight = 4;
		style.ContentMarginTop = 2;
		style.ContentMarginBottom = 2;
		return style;
	}

	/// <summary>The steel-ruled plate behind a key name in the hotkey reference.</summary>
	public static StyleBoxFlat CreateHotkeyKeyPlate()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.08f, 0.10f, 0.14f, 0.85f);
		style.BorderColor = new Color(0.35f, 0.45f, 0.55f, 0.5f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(3);
		style.ContentMarginLeft = 4;
		style.ContentMarginRight = 4;
		style.ContentMarginTop = 1;
		style.ContentMarginBottom = 1;
		return style;
	}

	/// <summary>The drop shadow behind the settings menu's light stone frame.</summary>
	public static StyleBoxFlat CreateSettingsWindowShadow()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0, 0, 0, 0);
		style.ShadowColor = new Color(0, 0, 0, 0.7f);
		style.ShadowSize = 24;
		style.ShadowOffset = new Vector2(0, 14);
		style.SetCornerRadiusAll(25);
		return style;
	}

	/// <summary>The dark plate behind the settings menu's window title.</summary>
	public static StyleBoxFlat CreateSettingsTitlePlate()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.24f, 0.21f, 0.18f, 1.0f);
		style.BorderColor = new Color(0.45f, 0.40f, 0.32f, 1.0f);
		style.SetBorderWidthAll(3);
		style.SetCornerRadiusAll(8);
		style.ShadowColor = new Color(0, 0, 0, 0.75f);
		style.ShadowSize = 12;
		style.ShadowOffset = new Vector2(0, 6);
		return style;
	}

	/// <summary>The recessed plate inside the settings menu's window title.</summary>
	public static StyleBoxFlat CreateSettingsTitleInnerPlate()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.18f, 0.16f, 0.14f, 1.0f);
		style.BorderColor = new Color(0.82f, 0.72f, 0.50f, 0.5f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(5);
		return style;
	}

	/// <summary>The plate behind a settings section title ("VIDEO", "AUDIO", ...).</summary>
	public static StyleBoxFlat CreateSettingsSectionHeader()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.12f, 0.11f, 0.10f, 0.6f);
		style.BorderColor = new Color(0.40f, 0.35f, 0.28f, 0.7f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		style.ContentMarginLeft = 14;
		style.ContentMarginRight = 14;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		return style;
	}

	/// <summary>The plate behind a settings row's current value.</summary>
	public static StyleBoxFlat CreateSettingsValuePlate()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.12f, 0.11f, 0.10f, 0.6f);
		style.BorderColor = new Color(0.40f, 0.35f, 0.28f, 0.7f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		style.ContentMarginLeft = 6;
		style.ContentMarginRight = 6;
		style.ContentMarginTop = 2;
		style.ContentMarginBottom = 2;
		return style;
	}

	/// <summary>The rule between settings sections. Thicker and greyer than <see cref="CreateHairlineSeparator"/>.</summary>
	public static StyleBoxFlat CreateSettingsSeparator()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.42f, 0.38f, 0.33f, 0.6f);
		style.ContentMarginTop = 1;
		style.ContentMarginBottom = 1;
		return style;
	}
}
