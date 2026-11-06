using System;
using System.Text.RegularExpressions;
using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI;

[GlobalClass]
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

	[GeneratedRegex(@"\[img(?:=(\d+)x(\d+))?\]([^\[\]]+)\[/img\]", RegexOptions.IgnoreCase)]
	private static partial Regex ImageTagRegex();

	public static Control Create(string text, in TooltipContext context)
	{
		return Create(TooltipTemplate.Resolve(text, context));
	}

	public static Control Create(string text)
	{
		var panel = new RichTooltip();
		panel.Name = "TooltipPanel_RichTooltip";
		panel.MouseFilter = MouseFilterEnum.Ignore;
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));

		var margin = new MarginContainer();
		margin.Name = "Margin_MarginContainer";
		margin.MouseFilter = MouseFilterEnum.Ignore;
		margin.AddThemeConstantOverride("margin_left", 8);
		margin.AddThemeConstantOverride("margin_right", 8);
		margin.AddThemeConstantOverride("margin_top", 6);
		margin.AddThemeConstantOverride("margin_bottom", 6);
		panel.AddChild(margin);

		var label = new RichTextLabel();
		label.Name = "Label_RichTextLabel";
		label.MouseFilter = MouseFilterEnum.Ignore;
		label.BbcodeEnabled = true;
		label.FitContent = true;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.CustomMinimumSize = new Vector2(260, 0);
		FillContent(label, text);
		margin.AddChild(label);

		return panel;
	}

	/// <summary>
	///     Fills the tooltip label, turning inline icon markup into real textures. The UI icons the tooltips point at
	///     are imported as kept files, so a rich text [img] tag cannot resolve them through the resource loader.
	/// </summary>
	private static void FillContent(RichTextLabel label, string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			label.Text = string.Empty;
			return;
		}

		string content = text.IndexOf('<') < 0 ? text : ConvertToBbCode(text);
		if (content.IndexOf("[img", StringComparison.OrdinalIgnoreCase) < 0)
		{
			label.Text = content;
			return;
		}

		int index = 0;
		foreach (Match match in ImageTagRegex().Matches(content))
		{
			if (match.Index > index) label.AppendText(content[index..match.Index]);

			int width = match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out int parsedWidth) ? parsedWidth : 0;
			int height = match.Groups[2].Success && int.TryParse(match.Groups[2].Value, out int parsedHeight) ? parsedHeight : 0;
			Texture2D? texture = RtexIconLoader.Load(match.Groups[3].Value.Trim());
			if (texture != null) label.AddImage(texture, width, height);

			index = match.Index + match.Length;
		}

		if (index < content.Length) label.AppendText(content[index..]);
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