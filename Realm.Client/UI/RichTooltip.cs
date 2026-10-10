using Godot;
using System.Text.RegularExpressions;

namespace Realm.Client.UI;

public partial class RichTooltip : PanelContainer
{
	[GeneratedRegex(@"<color=[""']?(#[0-9a-fA-F]{6,8})[""']?>", RegexOptions.IgnoreCase)]
	private static partial Regex ColorOpenRegex();

	[GeneratedRegex(@"</color>", RegexOptions.IgnoreCase)]
	private static partial Regex ColorCloseRegex();

	[GeneratedRegex(@"<b>", RegexOptions.IgnoreCase)]
	private static partial Regex BoldOpenRegex();

	[GeneratedRegex(@"</b>", RegexOptions.IgnoreCase)]
	private static partial Regex BoldCloseRegex();

	[GeneratedRegex(@"<i>", RegexOptions.IgnoreCase)]
	private static partial Regex ItalicOpenRegex();

	[GeneratedRegex(@"</i>", RegexOptions.IgnoreCase)]
	private static partial Regex ItalicCloseRegex();

	public static Control Create(string text)
	{
		var panel = new RichTooltip();
		panel.MouseFilter = MouseFilterEnum.Ignore;
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));

		var margin = new MarginContainer();
		margin.MouseFilter = MouseFilterEnum.Ignore;
		margin.AddThemeConstantOverride("margin_left", 8);
		margin.AddThemeConstantOverride("margin_right", 8);
		margin.AddThemeConstantOverride("margin_top", 6);
		margin.AddThemeConstantOverride("margin_bottom", 6);
		panel.AddChild(margin);

		var label = new RichTextLabel();
		label.MouseFilter = MouseFilterEnum.Ignore;
		label.BbcodeEnabled = true;
		label.FitContent = true;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.CustomMinimumSize = new Vector2(260, 0);
		label.Text = (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) ? (text ?? "") : ConvertToBbCode(text);
		margin.AddChild(label);

		return panel;
	}

	public static string ConvertToBbCode(string text)
	{
		if (string.IsNullOrEmpty(text)) return "";
		if (text.IndexOf('<') < 0) return text;

		string converted = ColorOpenRegex().Replace(text, "[color=$1]");
		converted = ColorCloseRegex().Replace(converted, "[/color]");
		converted = BoldOpenRegex().Replace(converted, "[b]");
		converted = BoldCloseRegex().Replace(converted, "[/b]");
		converted = ItalicOpenRegex().Replace(converted, "[i]");
		converted = ItalicCloseRegex().Replace(converted, "[/i]");

		return converted;
	}
}
