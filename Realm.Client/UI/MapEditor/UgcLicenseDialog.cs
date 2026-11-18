using System;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

/// <summary>
///     The agreement a creator reads and accepts before their first publish. It summarises the UGC licence, links the full
///     text and Valve's Workshop agreement, and reports the answer once so the publish can continue or stop.
/// </summary>
[GlobalClass]
public partial class UgcLicenseDialog : Control
{
	private const int CardWidth = 680;

	private static readonly Color BodyColor = new(0.88f, 0.86f, 0.8f);

	private Action<bool>? _onAnswered;

	public UgcLicenseDialog() { }

	public void Initialize(Action<bool> onAnswered)
	{
		_onAnswered = onAnswered;

		Name = "UgcLicenseDialog";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		ZIndex = 1150;
		MouseFilter = MouseFilterEnum.Stop;

		BuildLayout();
	}

	private void BuildLayout()
	{
		var backdrop = new ColorRect { Name = "UgcLicenseDialogBackdrop", Color = new Color(0.02f, 0.02f, 0.04f, 0.85f) };
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		AddChild(backdrop);

		var center = new CenterContainer { Name = "UgcLicenseDialogCenter" };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var card = new PanelContainer { Name = "UgcLicenseDialogCard" };
		card.CustomMinimumSize = new Vector2(CardWidth, 0);
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		center.AddChild(card);

		var margin = new MarginContainer { Name = "UgcLicenseDialogMargin" };
		margin.AddThemeConstantOverride("margin_left", 28);
		margin.AddThemeConstantOverride("margin_right", 28);
		margin.AddThemeConstantOverride("margin_top", 24);
		margin.AddThemeConstantOverride("margin_bottom", 22);
		card.AddChild(margin);

		var column = new VBoxContainer { Name = "UgcLicenseDialogColumn" };
		column.AddThemeConstantOverride("separation", 12);
		margin.AddChild(column);

		var title = new Label { Name = "UgcLicenseDialogTitle" };
		UIStyle.ApplyTitle(title, TranslationServer.Translate("BEFORE YOU PUBLISH"), 24);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		title.CustomMinimumSize = new Vector2(CardWidth - 84, 0);
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddChild(title);

		var scroll = new ScrollContainer { Name = "UgcLicenseDialogScroll", CustomMinimumSize = new Vector2(CardWidth - 84, 300) };
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		column.AddChild(scroll);

		var body = new RichTextLabel { Name = "UgcLicenseDialogBody", BbcodeEnabled = true };
		body.Text = BuildSummaryText();
		body.FitContent = true;
		body.ScrollActive = false;
		body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		body.CustomMinimumSize = new Vector2(CardWidth - 110, 0);
		body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		body.AddThemeFontSizeOverride("normal_font_size", 13);
		body.AddThemeColorOverride("default_color", BodyColor);
		scroll.AddChild(body);

		var linkRow = new HBoxContainer { Name = "UgcLicenseDialogLinks" };
		linkRow.Alignment = BoxContainer.AlignmentMode.Center;
		linkRow.AddThemeConstantOverride("separation", 14);
		column.AddChild(linkRow);

		linkRow.AddChild(CreateLink("ReadFullLicense_Button", TranslationServer.Translate("READ THE FULL LICENCE"), MetadataService.UgcLicenseUrl));
		linkRow.AddChild(CreateLink("WorkshopLegal_Button", TranslationServer.Translate("Workshop Legal Agreement"), "https://steamcommunity.com/workshop/workshoplegalagreement"));

		var buttonRow = new HBoxContainer { Name = "UgcLicenseDialogButtons" };
		buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
		buttonRow.AddThemeConstantOverride("separation", 18);
		column.AddChild(buttonRow);

		var acceptButton = new Button { Name = "UgcLicenseAccept_Button", CustomMinimumSize = new Vector2(190, 40) };
		acceptButton.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(acceptButton);
		UIStyle.ApplyButtonText(acceptButton, TranslationServer.Translate("I ACCEPT"), 14);
		acceptButton.Pressed += () => Answer(true);
		buttonRow.AddChild(acceptButton);

		var cancelButton = new Button { Name = "UgcLicenseCancel_Button", CustomMinimumSize = new Vector2(140, 40) };
		cancelButton.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(cancelButton);
		UIStyle.ApplyButtonText(cancelButton, TranslationServer.Translate("CANCEL"), 14);
		cancelButton.Pressed += () => Answer(false);
		buttonRow.AddChild(cancelButton);
	}

	private static string BuildSummaryText()
	{
		return TranslationServer.Translate("You are about to publish this map to the Steam Workshop, under your Steam account.") + "\n\n"
		       + TranslationServer.Translate("You keep ownership of what you made. Publishing grants the game's publisher the right to host, mirror, archive, distribute and display your map, and it survives you deleting the Workshop item.") + "\n\n"
		       + TranslationServer.Translate("It also grants every other player the right to mirror, fork and maintain your map for use with the official game, as long as they credit you, link the original item, keep the same terms, and do not ship it as a standalone product.") + "\n\n"
		       + TranslationServer.Translate("Assets you imported from another creator may not be exported into a standalone product without that creator's written permission. A map built from another map's references carries that map's content, so it counts as imported too.") + "\n\n"
		       + TranslationServer.Translate("Steam's own terms also apply to the item, and Steam decides who can see it.") + "\n\n"
		       + string.Format(TranslationServer.Translate("This acceptance is recorded on this machine with the licence version ({0}) and the date. You will be asked again only if the licence changes."), MetadataService.UgcLicenseVersion);
	}

	private static Button CreateLink(string name, string text, string url)
	{
		var button = new Button { Name = name, Text = text };
		button.Set("icon_max_width", 0);
		button.FocusMode = FocusModeEnum.None;
		button.AddThemeFontSizeOverride("font_size", 12);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(button);
		button.Pressed += () => OS.ShellOpen(url);
		return button;
	}

	private void Answer(bool accepted)
	{
		GameContext.UserInterface?.PlayClickSound();
		Action<bool>? answered = _onAnswered;
		_onAnswered = null;
		QueueFree();
		answered?.Invoke(accepted);
	}
}
