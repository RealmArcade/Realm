using System;
using System.Text;
using System.Text.RegularExpressions;
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
	public const string SupplyIconPath = "res://Assets/UI/Supplies.webp";

	private const int CostIconSizePixels = 16;

	private static readonly Regex BbCodeTagRegex = new(@"\[/?[^\]]*\]", RegexOptions.Compiled);

	/// <summary>
	///     Drops BBCode tags from text bound for a plain control. Template names may carry colour markup, and the
	///     plain labels (feedback toasts, chat lines) would otherwise show the tags themselves.
	/// </summary>
	public static string StripBbCode(string? text)
	{
		return string.IsNullOrEmpty(text) ? string.Empty : BbCodeTagRegex.Replace(text, string.Empty);
	}

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
	public static string BuildHeader(string? name, float gold, float wood, float stone, float mana = 0f, int pop = 0)
	{
		var builder = new StringBuilder(64);
		builder.Append("[b]");
		builder.Append(string.IsNullOrWhiteSpace(name) ? "?" : name);
		builder.Append("[/b]");

		AppendCost(builder, gold, GoldIconPath);
		AppendCost(builder, wood, WoodIconPath);
		AppendCost(builder, stone, StoneIconPath);
		AppendCost(builder, mana, PlaceholderIcons.ManaPath);

		if (pop > 0)
		{
			AppendCost(builder, pop, SupplyIconPath);
			builder.Append(" Pop");
		}

		return builder.ToString();
	}

	/// <summary>
	///     Puts an ability's template name and costs above its description, keeping any resolved tooltip tokens in the
	///     description untouched.
	/// </summary>
	/// <param name="manaCost">
	///     The cost the casting unit actually pays, which a map script can override per unit; the header falls back to
	///     the template's own cost when it is not supplied.
	/// </param>
	/// <param name="disabled">
	///     Whether the map script has currently disabled this ability for the casting unit, which tags the header so the
	///     reason a card cannot be pressed is visible in its tooltip.
	/// </param>
	public static string PrependAbilityHeader(string? abilityId, AbilityDefinition? definition, string? body, float manaCost = -1f, bool disabled = false)
	{
		// Only an ability the map actually names gets a header. The command card also shows engine commands such as
		// the Build menu under a reserved id, and those keep the tooltip the card item was built with. The name is map
		// text, so it is read in the player's own language; any BBCode it carries survives the lookup.
		string name = MapText.TranslateName(definition?.DisplayName);
		if (string.IsNullOrWhiteSpace(name)) return body ?? string.Empty;

		string header = BuildHeader(
			name,
			definition!.CostGold,
			definition.CostWood,
			definition.CostStone,
			manaCost >= 0f ? manaCost : definition.ManaCost);

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
			if (!string.IsNullOrEmpty(meta.Name)) name = MapText.TranslateName(meta.Name);
			gold = meta.CostGold;
			wood = meta.CostWood;
			stone = meta.CostStone;
			pop = meta.PopCost;
			desc = MapText.Translate(meta.Description);
		}

		string translatedActionPrefix = TranslationServer.Translate(actionPrefix);
		string hotkeyPrefix = hotkey.IsNone ? string.Empty : $"[{hotkey}] ";
		string header = $"{hotkeyPrefix}{translatedActionPrefix} {BuildHeader(name, gold, wood, stone, 0f, pop)}";

		return string.IsNullOrEmpty(desc) ? header : $"{header}\n{desc}";
	}
}
