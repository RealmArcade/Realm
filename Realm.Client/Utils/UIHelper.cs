using System;
using System.Text;
using Godot;
using Realm.Client.Core;
using Realm.Client.UI;
using Realm.Client.Services;
using Realm.Shared.Abilities;

namespace Realm.Client.Utils;

public static class UIHelper
{
	public const string GoldIconPath = "res://Assets/UI/gold_coin.webp";
	public const string WoodIconPath = "res://Assets/UI/wood_logs.webp";
	public const string StoneIconPath = "res://Assets/UI/stone.webp";

	private const int CostIconSizePixels = 16;

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
			GameContext.UserInterface?.PlayClickSound();
			onClick?.Invoke();
		};
		btn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}

	/// <summary>
	///     Builds the first line of a command card tooltip: the template name followed by its resource costs, each
	///     resource drawn with its own icon instead of a written resource name.
	/// </summary>
	public static string BuildHeader(string? name, float gold, float wood, float stone, int pop = 0)
	{
		var builder = new StringBuilder(64);
		builder.Append("[b]");
		builder.Append(string.IsNullOrWhiteSpace(name) ? "?" : name);
		builder.Append("[/b]");

		AppendCost(builder, gold, GoldIconPath);
		AppendCost(builder, wood, WoodIconPath);
		AppendCost(builder, stone, StoneIconPath);

		if (pop > 0)
		{
			builder.Append("  ");
			builder.Append(pop);
			builder.Append(" Pop");
		}

		return builder.ToString();
	}

	/// <summary>
	///     Puts an ability's template name and costs above its description, keeping any resolved tooltip tokens in the
	///     description untouched.
	/// </summary>
	/// <param name="disabled">
	///     Whether the map script has currently disabled this ability for the casting unit, which tags the header so the
	///     reason a card cannot be pressed is visible in its tooltip.
	/// </param>
	public static string PrependAbilityHeader(string? abilityId, AbilityDefinition? definition, string? body, bool disabled = false)
	{
		// Only an ability the map actually names gets a header. The command card also shows engine commands such as
		// the Build menu under a reserved id, and those keep the tooltip the card item was built with.
		string name = definition?.DisplayName ?? string.Empty;
		if (string.IsNullOrWhiteSpace(name)) return body ?? string.Empty;

		string header = BuildHeader(
			name,
			definition!.CostGold,
			definition.CostWood,
			definition.CostStone);

		if (disabled)
		{
			header += " [Disabled]";
		}

		if (string.IsNullOrWhiteSpace(body)) return header;
		return $"{header}\n{body}";
	}

	private static void AppendCost(StringBuilder builder, float amount, string iconPath)
	{
		if (amount <= 0f) return;

		builder.Append("  [img=");
		builder.Append(CostIconSizePixels);
		builder.Append('x');
		builder.Append(CostIconSizePixels);
		builder.Append(']');
		builder.Append(iconPath);
		builder.Append("[/img] ");
		builder.Append(amount.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
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
