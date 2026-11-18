using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared;

namespace Realm.Client.UI;

/// <summary>
///     The one identity screen. Steam is the account, so this shows the player's own Steam name, avatar and SteamID64
///     and nothing has to be signed into, renamed or signed out of: a copy of the game that owns Realm through Steam is
///     already the person using it, and a copy running without Steam says so here instead of offering a login.
/// </summary>
public partial class PlayerProfileDialog : Control
{
	private const string SteamIconPath = "res://Assets/UI/steam_icon.webp";

	private static readonly Color UserNameColor = new(0.95f, 0.78f, 0.52f);
	private static readonly Color StatValueColor = new(0.9f, 0.85f, 0.72f);

	private Label _userNameValueLabel = null!;
	private TextureRect _avatarView = null!;
	private VBoxContainer _identityRows = null!;
	private Label _warningLabel = null!;
	private Button _steamProfileButton = null!;

	/// <summary>Raised when this screen closed and the menu behind it should read the identity again.</summary>
	public Action? SignedIn { get; set; }

	public override void _Ready()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		BuildLayout();
		RefreshPresentation();
	}

	public void RefreshPresentation()
	{
		if (!GodotObject.IsInstanceValid(this) || _userNameValueLabel == null) return;

		SteamService steam = GameContext.Steam;
		_userNameValueLabel.Text = steam.IsAvailable ? steam.UserName : TranslationServer.Translate("Playing without Steam");

		_avatarView.Texture = steam.IsAvailable ? steam.PlayerAvatar : null;
		_avatarView.Visible = _avatarView.Texture != null;

		FillIdentityRows(steam);
		_steamProfileButton.Visible = steam.IsAvailable;

		_warningLabel.Text = steam.IsAvailable ? string.Empty : steam.UnavailableExplanation;
	}

	/// <summary>Shows the screen, reading the identity again first so it is never a frame behind.</summary>
	public void Open()
	{
		RefreshPresentation();
	}

	private void BuildLayout()
	{
		var backdrop = new Panel { Name = "PlayerProfileBackdrop" };
		backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		backdrop.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		UIStyle.ApplyScreenBackground(backdrop, "res://Assets/UI/player_profile_bg.webp");
		AddChild(backdrop);

		var center = new CenterContainer { Name = "PlayerProfileCenter" };
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var card = new PanelContainer { Name = "PlayerProfileCard" };
		card.CustomMinimumSize = new Vector2(560, 0);
		card.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateArtworkPlate(
			"res://Assets/UI/player_profile_card_bg.webp", 24, 24, 24, 24, 40));
		center.AddChild(card);

		var margin = new MarginContainer { Name = "PlayerProfileMargin" };
		margin.AddThemeConstantOverride("margin_left", 28);
		margin.AddThemeConstantOverride("margin_right", 28);
		margin.AddThemeConstantOverride("margin_top", 22);
		margin.AddThemeConstantOverride("margin_bottom", 22);
		card.AddChild(margin);

		var column = new VBoxContainer { Name = "PlayerProfileColumn" };
		column.AddThemeConstantOverride("separation", 10);
		margin.AddChild(column);

		column.AddChild(CreateTitleLabel());
		CreateIdentityBlock(column);
		CreateWarningLabel(column);
		CreateSteamProfileButton(column);
		CreateLeaderboardButton(column);
		CreateBackButton(column);
	}

	private Label CreateTitleLabel()
	{
		var title = new Label { Name = "PlayerProfileTitle" };
		title.Text = TranslationServer.Translate("PLAYER PROFILE");
		AppyTitleStyle(title);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		return title;
	}

	private static void AppyTitleStyle(Label title)
	{
		UIStyle.ApplyTitle(title, TranslationServer.Translate("PLAYER PROFILE"), 22);
		title.HorizontalAlignment = HorizontalAlignment.Center;
	}

	private Control CreateIdentityBlock(Node parent)
	{
		var row = new HBoxContainer { Name = "PlayerProfileIdentityRow" };
		row.AddThemeConstantOverride("separation", 16);
		parent.AddChild(row);

		_avatarView = new TextureRect
		{
			Name = "PlayerProfileAvatar",
			CustomMinimumSize = new Vector2(72, 72),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
		};
		_avatarView.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		row.AddChild(_avatarView);

		var column = new VBoxContainer { Name = "PlayerProfileIdentityColumn" };
		column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(column);

		_userNameValueLabel = CreateBodyLabel(column, string.Empty);
		_userNameValueLabel.Name = "PlayerProfileUserNameValue";
		_userNameValueLabel.AddThemeFontSizeOverride("font_size", 20);
		_userNameValueLabel.AddThemeColorOverride("font_color", UserNameColor);

		_identityRows = new VBoxContainer { Name = "PlayerProfileIdentityRows" };
		_identityRows.AddThemeConstantOverride("separation", 4);
		column.AddChild(_identityRows);

		return row;
	}

	private void FillIdentityRows(SteamService steam)
	{
		foreach (Node child in _identityRows.GetChildren())
		{
			_identityRows.RemoveChild(child);
			child.QueueFree();
		}

		if (!steam.IsAvailable) return;

		_identityRows.AddChild(CreateIdentityRow(TranslationServer.Translate("SteamID64"), steam.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
		_identityRows.AddChild(CreateIdentityRow(TranslationServer.Translate("App build"), RealmVersion.GameBuildNumber));

		if (!steam.TryReadLifetimeRecord(out SteamLifetimeRecord record))
		{
			_identityRows.AddChild(CreateIdentityRow(TranslationServer.Translate("Steam record"), TranslationServer.Translate("unavailable offline")));
			return;
		}

		_identityRows.AddChild(CreateIdentityRow(TranslationServer.Translate("Matches played"), record.MatchesPlayed.ToString(System.Globalization.CultureInfo.InvariantCulture)));
		_identityRows.AddChild(CreateIdentityRow(TranslationServer.Translate("Matches won"), record.MatchesWon.ToString(System.Globalization.CultureInfo.InvariantCulture)));
		_identityRows.AddChild(CreateIdentityRow(TranslationServer.Translate("Matches lost"), record.MatchesLost.ToString(System.Globalization.CultureInfo.InvariantCulture)));
	}

	private Label CreateIdentityRow(string label, string value)
	{
		var row = new Label { Name = "PlayerProfileIdentityRow_Label" };
		row.Text = string.Format(TranslationServer.Translate("{0}: {1}"), label, value);
		row.AddThemeColorOverride("font_color", StatValueColor);
		row.AddThemeFontSizeOverride("font_size", 12);
		return row;
	}

	private Label CreateWarningLabel(Node parent)
	{
		_warningLabel = CreateBodyLabel(parent, string.Empty);
		_warningLabel.Name = "PlayerProfileWarning";
		_warningLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_warningLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		return _warningLabel;
	}

	private Button CreateSteamProfileButton(Node parent)
	{
		_steamProfileButton = CreateActionButton(parent, "VIEW STEAM PROFILE", SteamIconPath);
		_steamProfileButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			GameContext.Steam.OpenOwnSteamProfile();
		};
		return _steamProfileButton;
	}

	private void CreateLeaderboardButton(Node parent)
	{
		Button button = CreateActionButton(parent, "LEADERBOARDS");
		button.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();

			var dialog = new SteamLeaderboardDialog { Name = "SteamLeaderboardDialog" };
			AddChild(dialog);
		};
	}

	private Button CreateBackButton(Node parent)
	{
		Button button = CreateActionButton(parent, "BACK");
		button.Set("icon_max_width", 0);
		button.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();

			Action? signedIn = SignedIn;
			QueueFree();
			signedIn?.Invoke();
		};
		return button;
	}

	private Label CreateBodyLabel(Node parent, string text)
	{
		var label = new Label { Name = "PlayerProfileBodyLabel" };
		label.Text = text;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.CustomMinimumSize = new Vector2(460, 0);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		label.AddThemeFontSizeOverride("font_size", 13);
		parent.AddChild(label);
		return label;
	}

	private Button CreateActionButton(Node parent, string textKey, string iconPath = "")
	{
		var button = new Button { Name = $"PlayerProfile{textKey.Replace(" ", string.Empty)}Button" };
		UIStyle.ApplyButtonStyleBoxesClearingFocus(button);
		UIStyle.ApplyButtonText(button, TranslationServer.Translate(textKey), 14);
		button.CustomMinimumSize = new Vector2(0, 42);
		button.AddThemeConstantOverride("icon_max_width", 20);

		if (iconPath.Length > 0)
		{
			button.Icon = TrimmedTextureLoader.LoadTrimmedTexture(iconPath);
			button.IconAlignment = HorizontalAlignment.Left;
			button.VerticalIconAlignment = VerticalAlignment.Center;
			button.AddThemeConstantOverride("h_separation", 8);
		}

		parent.AddChild(button);
		return button;
	}
}
