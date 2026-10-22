using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.UI;

namespace Realm.Client.Utils;

public static class UIHelper
{
	public static void SetupPillarButton(Button btn, string text, Action onClick)
	{
		btn.Flat = false;
		btn.Text = text;
		btn.AddThemeConstantOverride("icon_max_width", 0);
		btn.AddThemeFontSizeOverride("font_size", 24);
		btn.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btn.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			onClick?.Invoke();
		};
		btn.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
	}

	public static string GetUnitCostTooltip(string unitId, HotkeySpec hotkey, string actionPrefix)
	{
		string name = unitId.ToUpper();
		float gold = 0, wood = 0, stone = 0;
		int pop = 0;
		string desc = "";
		if (Realm.Client.Core.GameHost.TryGetUnitOrBuildingMetadata(unitId, out var meta))
		{
			if (!string.IsNullOrEmpty(meta.Name)) name = meta.Name;
			gold = meta.CostGold;
			wood = meta.CostWood;
			stone = meta.CostStone;
			pop = meta.PopCost;
			desc = meta.Description ?? "";
		}

		string costStr = $"Cost: {gold} Gold";
		if (wood > 0) costStr += $", {wood} Wood";
		if (stone > 0) costStr += $", {stone} Stone";
		if (pop > 0) costStr += $", {pop} Pop";

		string translatedActionPrefix = TranslationServer.Translate(actionPrefix);
		
		string tooltipFormat = !hotkey.IsNone
			? (string.IsNullOrEmpty(desc) ? "[{0}] {4} {1} ({2})" : "[{0}] {4} {1} ({2}) — {3}")
			: (string.IsNullOrEmpty(desc) ? "{4} {1} ({2})" : "{4} {1} ({2}) — {3}");

		return string.IsNullOrEmpty(desc)
			? string.Format(tooltipFormat, hotkey, name, costStr, "", translatedActionPrefix)
			: string.Format(tooltipFormat, hotkey, name, costStr, desc, translatedActionPrefix);
	}
}
