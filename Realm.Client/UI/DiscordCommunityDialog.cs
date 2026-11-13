using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI;

/// <summary>
///     The community invite. It states the verification phrase the server asks for before the player leaves the game for the
///     browser, so the phrase is readable while they join. The phrase itself is literal text the server compares against, so it
///     is never translated and is assembled from characters to keep it out of the localization catalog.
/// </summary>
[GlobalClass]
public partial class DiscordCommunityDialog : Control
{
	public const string InviteUrl = "http://discord.realm-game.com";

	private const int CardWidth = 560;
	private const int CardInsetX = 30;
	private const int CardInsetY = 26;
	private const int CardInsetBottom = 22;
	private const int StonePanelPaddingX = 20;
	private const int ContentWidth = CardWidth - 2 * (CardInsetX + StonePanelPaddingX);
	private const int CloseButtonWidth = 132;
	private const int JoinButtonHeight = 46;
	private const int JoinButtonIconWidth = 22;
	private const int PhrasePlateInsetX = 20;
	private const int PhrasePlateInsetY = 14;

	private static readonly Color BodyTextColor = new(0.88f, 0.85f, 0.78f);
	private static readonly char[] UntranslatedVerificationPhrase = { '4', '2' };

	private Button _joinButton = null!;

	public override void _Ready()
	{
		Name = "DiscordCommunityDialog";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		BuildLayout();
	}

	public void Open()
	{
		Visible = true;
		_joinButton.GrabFocus();
	}

	private void BuildLayout()
	{
		var backdrop = new ColorRect { Name = "DiscordCommunityDialogBackdrop", Color = new Color(0.02f, 0.02f, 0.04f, 0.82f) };
		UIStyle.ConfigureModalBackdrop(backdrop);
		backdrop.GuiInput += OnBackdropInput;
		AddChild(backdrop);

		var center = new CenterContainer { Name = "DiscordCommunityDialogCenter" };
		UIStyle.ConfigureModalContentLayer(center);
		AddChild(center);

		var card = new PanelContainer { Name = "DiscordCommunityDialogCard" };
		card.CustomMinimumSize = new Vector2(CardWidth, 0);
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		center.AddChild(card);

		var margin = new MarginContainer { Name = "DiscordCommunityDialogMargin" };
		margin.AddThemeConstantOverride("margin_left", CardInsetX);
		margin.AddThemeConstantOverride("margin_right", CardInsetX);
		margin.AddThemeConstantOverride("margin_top", CardInsetY);
		margin.AddThemeConstantOverride("margin_bottom", CardInsetBottom);
		card.AddChild(margin);

		var column = new VBoxContainer { Name = "DiscordCommunityDialogColumn" };
		column.AddThemeConstantOverride("separation", 12);
		margin.AddChild(column);

		var titleLabel = new Label { Name = "DiscordCommunityDialogTitle" };
		UIStyle.ApplyTitle(titleLabel, TranslationServer.Translate("Join the Realm Community"), 28);
		titleLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		titleLabel.CustomMinimumSize = new Vector2(ContentWidth, 0);
		titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(titleLabel);

		Label subtitleLabel = CreateBodyLabel(column, TranslationServer.Translate("Connect with other players, mapmakers, and creators."));
		subtitleLabel.Name = "DiscordCommunityDialogSubtitle";
		subtitleLabel.HorizontalAlignment = HorizontalAlignment.Center;

		var divider = new HSeparator { Name = "DiscordCommunityDialogDivider" };
		divider.AddThemeStyleboxOverride("separator", UIStyle.CreateHairlineSeparator());
		column.AddChild(divider);

		var sectionLabel = new Label { Name = "DiscordCommunityDialogSectionLabel", Text = TranslationServer.Translate("Community verification (CAPTCHA)") };
		sectionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		sectionLabel.CustomMinimumSize = new Vector2(ContentWidth, 0);
		sectionLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		sectionLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		sectionLabel.AddThemeConstantOverride("outline_size", 3);
		sectionLabel.AddThemeFontSizeOverride("font_size", 15);
		column.AddChild(sectionLabel);

		Label phraseHintLabel = CreateBodyLabel(column, TranslationServer.Translate("Use this captcha phrase when joining the discord server"));
		phraseHintLabel.Name = "DiscordCommunityDialogPhraseHint";

		column.AddChild(CreatePhrasePlate());

		_joinButton = new Button { Name = "DiscordCommunityDialogJoinButton" };
		_joinButton.CustomMinimumSize = new Vector2(0, JoinButtonHeight);
		_joinButton.AddThemeConstantOverride("icon_max_width", JoinButtonIconWidth);
		_joinButton.Icon = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/discord_icon.webp");
		UIStyle.ApplyButtonStyleBoxesClearingFocus(_joinButton);
		UIStyle.ApplyButtonText(_joinButton, TranslationServer.Translate("Join our Discord"), 17);
		_joinButton.Pressed += OpenInvite;
		_joinButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		column.AddChild(_joinButton);

		var footer = new HBoxContainer { Name = "DiscordCommunityDialogFooter" };
		footer.Alignment = BoxContainer.AlignmentMode.End;
		column.AddChild(footer);

		var closeButton = new Button { Name = "DiscordCommunityDialogCloseButton" };
		closeButton.CustomMinimumSize = new Vector2(CloseButtonWidth, 34);
		closeButton.AddThemeConstantOverride("icon_max_width", 0);
		closeButton.FocusMode = FocusModeEnum.None;
		UIStyle.ApplyButtonStyleBoxesClearingFocus(closeButton);
		UIStyle.ApplyButtonText(closeButton, TranslationServer.Translate("CLOSE"), 14);
		closeButton.Pressed += Close;
		closeButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		footer.AddChild(closeButton);
	}

	private Control CreatePhrasePlate()
	{
		var plate = new PanelContainer { Name = "DiscordCommunityDialogPhrasePlate", CustomMinimumSize = new Vector2(ContentWidth, 0) };
		plate.AddThemeStyleboxOverride("panel", UIStyle.CreateBorderPlate(UIStyle.ColorGoldDull, 1, new Color(0.05f, 0.05f, 0.07f, 0.7f), 6));

		var inset = new MarginContainer { Name = "DiscordCommunityDialogPhraseInset" };
		inset.AddThemeConstantOverride("margin_left", PhrasePlateInsetX);
		inset.AddThemeConstantOverride("margin_right", PhrasePlateInsetX);
		inset.AddThemeConstantOverride("margin_top", PhrasePlateInsetY);
		inset.AddThemeConstantOverride("margin_bottom", PhrasePlateInsetY);
		plate.AddChild(inset);

		var phraseLabel = new Label { Name = "DiscordCommunityDialogPhraseLabel", HorizontalAlignment = HorizontalAlignment.Center };
		phraseLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		phraseLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		phraseLabel.AddThemeConstantOverride("outline_size", 4);
		phraseLabel.AddThemeFontSizeOverride("font_size", 26);
		phraseLabel.Text = new string(UntranslatedVerificationPhrase);
		inset.AddChild(phraseLabel);

		return plate;
	}

	private static Label CreateBodyLabel(Container parent, string text)
	{
		var label = new Label { Name = "DiscordCommunityDialogBodyLabel" };
		label.Text = text;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.CustomMinimumSize = new Vector2(ContentWidth, 0);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.AddThemeColorOverride("font_color", BodyTextColor);
		label.AddThemeFontSizeOverride("font_size", 14);
		parent.AddChild(label);
		return label;
	}

	private void OpenInvite()
	{
		GameContext.UserInterface?.PlayClickSound();
		OS.ShellOpen(InviteUrl);
	}

	private void OnBackdropInput(InputEvent inputEvent)
	{
		if (inputEvent is InputEventMouseButton mouseButton && mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
		{
			Close();
		}
	}

	private void Close()
	{
		GameContext.UserInterface?.PlayClickSound();
		QueueFree();
	}
}
