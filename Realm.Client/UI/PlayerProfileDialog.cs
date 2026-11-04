using System;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Accounts;
using Realm.Shared.Network;

namespace Realm.Client.UI;

/// <summary>
///     The one place a player sees who they are. The username, the multiplayer record and every Steam account action live on
///     this card, so the name is shown and changed in a single spot instead of being asked for again by a second screen. Signed
///     out, the name is the one this machine plays under and stays editable; signed in, the Steam name is what the card states
///     and the field is gone, so there is nothing to edit that the account would not overwrite.
/// </summary>
[GlobalClass]
public partial class PlayerProfileDialog : Control
{
	private const string SteamIconPath = "res://Assets/UI/steam_icon.webp";

	private static readonly Color UserNameColor = new(0.95f, 0.78f, 0.52f);
	private static readonly Color StatValueColor = new(0.9f, 0.85f, 0.72f);
	private static readonly Color VictoryColor = new(0.55f, 0.95f, 0.55f);
	private static readonly Color DefeatColor = new(0.95f, 0.5f, 0.45f);
	private static readonly Color WinRateColor = new(0.6f, 0.85f, 1.0f);
	private static readonly Color SuccessColor = new(0.55f, 0.95f, 0.55f);
	private static readonly Color FailureColor = new(0.95f, 0.6f, 0.4f);

	private Label _userNameValueLabel;
	private LineEdit _userNameEdit;
	private VBoxContainer _statsRows;
	private VBoxContainer _signedOutBox;
	private VBoxContainer _nameChoiceBox;
	private LineEdit _registrationNameEdit;
	private Button _signInButton;
	private VBoxContainer _signedInBox;
	private Label _identityLabel;
	private LineEdit _renameEdit;
	private Button _renameButton;
	private VBoxContainer _stepUpBox;
	private Button _stepUpButton;
	private Button _signOutButton;
	private Label _warningLabel;
	private Label _statusLabel;
	private bool _busy;
	private bool _verifyThenRename;
	private bool _serverRefusesSteam;
	private bool _authorityUnreachable;
	private bool _registrationNameRefused;
	private int _statsRequestGeneration;

	public Action? SignedIn { get; set; }

	public override void _Ready()
	{
		Name = nameof(PlayerProfileDialog);
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		BuildLayout();
		RefreshState();
		RefreshAccount();
	}

	public void RefreshPresentation()
	{
		if (!GodotObject.IsInstanceValid(this)) return;
		RefreshAccount();
	}

	public void Open()
	{
		RefreshState();
		RefreshAccount();
		_ = LoadProvidersAsync();

		if (GameContext.Account.IsSignedIn) _ = GameContext.Account.RefreshAsync();
	}

	private void BuildLayout()
	{
		var backdrop = new Panel { Name = "PlayerProfileBackdrop" };
		backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		backdrop.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		backdrop.AddThemeStyleboxOverride("panel", CreateBackdropStyle());
		AddChild(backdrop);

		var center = new CenterContainer { Name = "PlayerProfileCenter" };
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var card = new PanelContainer { Name = "PlayerProfileCard" };
		card.CustomMinimumSize = new Vector2(560, 0);
		card.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		card.AddThemeStyleboxOverride("panel", CreateCardStyle());
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
		column.AddChild(CreateFactionFlag());
		column.AddChild(CreateUserNameRow());

		_statsRows = new VBoxContainer { Name = "PlayerProfileStatsRows" };
		column.AddChild(_statsRows);

		column.AddChild(CreateSignedOutBox());
		column.AddChild(CreateSignedInBox());

		_warningLabel = CreateBodyLabel(column, string.Empty);
		_warningLabel.Name = "PlayerProfileWarning";
		_warningLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_warningLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);

		_statusLabel = CreateBodyLabel(column, string.Empty);
		_statusLabel.Name = "PlayerProfileStatus";
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;

		Label signedOutExplanationLabel = CreateBodyLabel(column, TranslationServer.Translate("Login only required for multi-player. Offline play is supported without login."));
		signedOutExplanationLabel.Name = "PlayerProfileLoginExplanation";
		signedOutExplanationLabel.HorizontalAlignment = HorizontalAlignment.Center;

		column.AddChild(CreateBackButton());
	}

	private static StyleBox CreateBackdropStyle()
	{
		Texture2D backdropTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/player_profile_bg.webp");
		if (backdropTexture == null) return UIStyle.CreateBgGradient();

		return new StyleBoxTexture { Texture = backdropTexture };
	}

	private static StyleBox CreateCardStyle()
	{
		Texture2D cardTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/player_profile_card_bg.webp");
		if (cardTexture == null) return UIStyle.CreateStonePanel();

		var style = new StyleBoxTexture
		{
			Texture = cardTexture,
			TextureMarginLeft = 40,
			TextureMarginRight = 40,
			TextureMarginTop = 40,
			TextureMarginBottom = 40,
			ContentMarginLeft = 24,
			ContentMarginRight = 24,
			ContentMarginTop = 24,
			ContentMarginBottom = 24
		};

		return style;
	}

	private Label CreateTitleLabel()
	{
		var title = new Label { Name = "PlayerProfileTitle" };
		UIStyle.ApplyTitle(title, TranslationServer.Translate("PLAYER PROFILE"), 26);

		var titleFont = new SystemFont();
		titleFont.FontNames = new string[] { "Cinzel Bold", "Cinzel", "Palatino Linotype", "Garamond", "Georgia", "serif" };
		titleFont.FontWeight = 700;
		title.AddThemeFontOverride("font", titleFont);
		title.AddThemeFontSizeOverride("font_size", 28);
		title.AddThemeColorOverride("font_color", new Color(0.85f, 0.65f, 0.4f));

		return title;
	}

	private TextureRect CreateFactionFlag()
	{
		var factionFlag = new TextureRect { Name = "PlayerProfileFactionFlag" };
		Texture2D flagTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/alliance_flag.webp");
		if (flagTexture == null) flagTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/profile_frame_normal.webp");

		factionFlag.Texture = flagTexture;
		factionFlag.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		factionFlag.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		factionFlag.CustomMinimumSize = new Vector2(180, 180);
		factionFlag.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		factionFlag.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		return factionFlag;
	}

	private HBoxContainer CreateUserNameRow()
	{
		var row = new HBoxContainer { Name = "PlayerProfileUserNameRow" };
		row.Alignment = BoxContainer.AlignmentMode.Center;
		row.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		row.AddThemeConstantOverride("separation", 10);

		var keyLabel = new Label { Name = "PlayerProfileUserNameKey" };
		keyLabel.Text = TranslationServer.Translate("Username") + ":";
		ApplyProfileLabelStyle(keyLabel, UserNameColor, 23);

		_userNameValueLabel = new Label { Name = "PlayerProfileUserNameValue" };
		ApplyProfileLabelStyle(_userNameValueLabel, StatValueColor, 23);

		_userNameEdit = new LineEdit { Name = "PlayerProfileUserNameEdit" };
		_userNameEdit.CustomMinimumSize = new Vector2(230, 34);
		_userNameEdit.MaxLength = LobbyLimits.MaximumPlayerNameLength;
		_userNameEdit.PlaceholderText = TranslationServer.Translate("Guest");
		_userNameEdit.TooltipText = TranslationServer.Translate("The name other players see while you are signed out. Signing in with Steam replaces it with your Steam name.");
		UIStyle.ApplyTextInput(_userNameEdit, 19);
		_userNameEdit.TextSubmitted += _ => CommitUserName();
		_userNameEdit.FocusExited += CommitUserName;

		row.AddChild(keyLabel);
		row.AddChild(_userNameValueLabel);
		row.AddChild(_userNameEdit);

		return row;
	}

	private VBoxContainer CreateSignedOutBox()
	{
		_signedOutBox = new VBoxContainer { Name = "PlayerProfileSignedOut" };
		_signedOutBox.AddThemeConstantOverride("separation", 10);

		_nameChoiceBox = new VBoxContainer { Name = "PlayerProfileNameChoice" };
		_nameChoiceBox.AddThemeConstantOverride("separation", 6);
		_nameChoiceBox.Visible = false;
		_signedOutBox.AddChild(_nameChoiceBox);

		Label nameChoiceHint = CreateBodyLabel(_nameChoiceBox, TranslationServer.Translate("That name cannot be used. Choose the username this account will use:"));
		nameChoiceHint.Name = "PlayerProfileNameChoiceHint";
		nameChoiceHint.HorizontalAlignment = HorizontalAlignment.Center;

		_registrationNameEdit = new LineEdit { Name = "PlayerProfileNameChoiceEdit" };
		_registrationNameEdit.CustomMinimumSize = new Vector2(0, 36);
		_registrationNameEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_registrationNameEdit.MaxLength = DisplayNameRules.MaximumUtf8Bytes;
		_registrationNameEdit.PlaceholderText = TranslationServer.Translate("Username");
		UIStyle.ApplyTextInput(_registrationNameEdit);
		_registrationNameEdit.TextSubmitted += submittedName =>
		{
			_ = SignInWithSteamAsync(false);
		};
		_nameChoiceBox.AddChild(_registrationNameEdit);

		_signInButton = CreateActionButton(_signedOutBox, "SIGN IN WITH STEAM", SteamIconPath);
		_signInButton.Pressed += () => _ = SignInWithSteamAsync(false);

		Label signedOutHintLabel = CreateBodyLabel(_signedOutBox, TranslationServer.Translate("A Steam sign-in proves this name is yours, which verified reviews and map publishing need. Everything else stays available signed out."));
		signedOutHintLabel.Name = "PlayerProfileSignedOutHint";
		signedOutHintLabel.HorizontalAlignment = HorizontalAlignment.Center;

		return _signedOutBox;
	}

	private VBoxContainer CreateSignedInBox()
	{
		_signedInBox = new VBoxContainer { Name = "PlayerProfileSignedIn" };
		_signedInBox.AddThemeConstantOverride("separation", 10);

		_identityLabel = CreateBodyLabel(_signedInBox, string.Empty);
		_identityLabel.Name = "PlayerProfileIdentity";
		_identityLabel.HorizontalAlignment = HorizontalAlignment.Center;

		var renameContainer = new VBoxContainer { Name = "PlayerProfileRename" };
		renameContainer.AddThemeConstantOverride("separation", 6);
		_signedInBox.AddChild(renameContainer);

		_renameEdit = CreateField(renameContainer, "PlayerProfileRename", TranslationServer.Translate("New Username"));
		_renameEdit.MaxLength = DisplayNameRules.MaximumUtf8Bytes;

		_renameButton = CreateActionButton(renameContainer, "CHANGE USERNAME");
		_renameButton.Pressed += () => _ = RenameAsync();

		_stepUpBox = new VBoxContainer { Name = "PlayerProfileStepUp" };
		_stepUpBox.AddThemeConstantOverride("separation", 8);
		_signedInBox.AddChild(_stepUpBox);

		var stepUpLabel = CreateBodyLabel(_stepUpBox, TranslationServer.Translate("Confirm it is you with Steam before changing your username."));
		stepUpLabel.Name = "PlayerProfileStepUpHint";
		stepUpLabel.HorizontalAlignment = HorizontalAlignment.Center;

		_stepUpButton = CreateActionButton(_stepUpBox, "VERIFY WITH STEAM", SteamIconPath);
		_stepUpButton.Pressed += () => _ = BeginVerifyThenRenameAsync();

		_signOutButton = CreateActionButton(_signedInBox, "SIGN OUT");
		_signOutButton.Pressed += () => _ = SignOutAsync();

		return _signedInBox;
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

	private LineEdit CreateField(Node parent, string fieldName, string fieldLabel)
	{
		var label = new Label { Name = $"{fieldName}Label" };
		label.Text = fieldLabel;
		label.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		label.AddThemeFontSizeOverride("font_size", 13);
		parent.AddChild(label);

		var edit = new LineEdit { Name = $"{fieldName}Edit" };
		edit.CustomMinimumSize = new Vector2(0, 36);
		edit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		edit.PlaceholderText = fieldLabel;
		UIStyle.ApplyTextInput(edit);
		parent.AddChild(edit);
		return edit;
	}

	private Button CreateActionButton(Node parent, string textKey, string iconPath = "")
	{
		var button = new Button { Name = $"PlayerProfile{textKey.Replace(" ", string.Empty)}Button" };
		UIStyle.ApplyButtonStyleBoxes(button);
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

	private static void ApplyProfileLabelStyle(Label label, Color color, int fontSize)
	{
		var profileFont = new SystemFont();
		profileFont.FontNames = new string[] { "Cinzel Bold", "Cinzel", "Palatino Linotype", "Garamond", "Georgia", "serif" };
		profileFont.FontWeight = 700;

		label.AddThemeFontOverride("font", profileFont);
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.02f, 0.04f));
		label.AddThemeConstantOverride("outline_size", 5);
		label.AddThemeFontSizeOverride("font_size", fontSize);
	}

	private void CommitUserName()
	{
		string chosenName = _userNameEdit.Text.Trim();
		if (string.Equals(chosenName, GameSettings.PlayerName, StringComparison.Ordinal)) return;

		GameSettings.PlayerName = chosenName;
		GameSettings.Save();
	}

	private void RefreshState()
	{
		if (!GodotObject.IsInstanceValid(this)) return;

		bool signedIn = GameContext.Account.IsSignedIn;
		bool steamAvailable = GameContext.Steam.IsAvailable;

		_userNameValueLabel.Visible = signedIn;
		_userNameEdit.Visible = !signedIn;
		_signedOutBox.Visible = !signedIn;
		_nameChoiceBox.Visible = !signedIn && _registrationNameRefused;
		_signedInBox.Visible = signedIn;
		_signInButton.Visible = steamAvailable;
		_signInButton.Disabled = _busy || _serverRefusesSteam;
		_stepUpBox.Visible = _verifyThenRename;

		if (signedIn)
		{
			AdoptAccountNameAsLocalName();
			_userNameValueLabel.Text = GameContext.Account.EffectiveUserName;
			RefreshWarning();
			return;
		}

		_userNameEdit.Text = GameSettings.PlayerName;

		string steamUserName = GameContext.Steam.UserName.Trim();
		_userNameEdit.PlaceholderText = steamUserName.Length > 0 ? steamUserName : TranslationServer.Translate("Guest");

		RefreshWarning();
	}

	/// <summary>
	///     Makes the name the account goes by the name this machine falls back to when it is signed out, so the local name tracks
	///     the most recent name the player actually used instead of freezing whatever was typed before the first sign-in. The
	///     account's name is canonical already, so it is stored as it stands and never re-validated here.
	/// </summary>
	private static void AdoptAccountNameAsLocalName()
	{
		string accountName = GameContext.Account.DisplayName;
		if (accountName.Length == 0) return;
		if (string.Equals(accountName, GameSettings.PlayerName, StringComparison.Ordinal)) return;

		GameSettings.PlayerName = accountName;
		GameSettings.Save();
	}

	private void RefreshWarning()
	{
		if (GameContext.Account.IsSignedIn)
		{
			_warningLabel.Text = string.Empty;
			return;
		}

		if (!GameContext.Steam.IsAvailable)
		{
			_warningLabel.Text = DescribeUnavailableSteam();
			return;
		}

		if (_serverRefusesSteam)
		{
			_warningLabel.Text = TranslationServer.Translate("This server does not accept Steam sign-in yet, so only unverified play is available.");
			return;
		}

		_warningLabel.Text = _authorityUnreachable ? TranslationServer.Translate("The account authority could not be reached.") : string.Empty;
	}

	private static string DescribeUnavailableSteam()
	{
		return GameContext.Steam.UnavailableReason switch
		{
			SteamUnavailableReason.SteamClientNotRunning => TranslationServer.Translate("Steam is not running, so Realm cannot sign you in. Start the Steam client and reopen the game, or keep playing under your own name."),
			SteamUnavailableReason.NativeLibraryMissing => TranslationServer.Translate("This build does not include the Steamworks library, so it cannot sign you in. Play under your own name, or start a Steam build."),
			SteamUnavailableReason.NativeLibraryMismatch => TranslationServer.Translate("The Steamworks library in this build does not match the game, so sign-in is unavailable. Play under your own name, or start a current Steam build."),
			SteamUnavailableReason.SteamRejectedThisProcess => TranslationServer.Translate("Steam did not accept this copy of the game, so it cannot sign you in. Start the game from your Steam library, or keep playing under your own name."),
			_ => TranslationServer.Translate("Steamworks is not available in this build, so this screen cannot sign you in. Play under your own name, or start the game through Steam.")
		};
	}

	private void RefreshAccount()
	{
		if (GameContext.Account.IsSignedIn)
		{
			string accountId = GameContext.Account.AccountId.ToString(CultureInfo.InvariantCulture);
			_identityLabel.Text = string.Format(TranslationServer.Translate("Signed in with Steam (account {0})"), accountId);

			PopulateStats();
		}
		else
		{
			ClearStats();
		}

		RefreshState();
	}

	private void ClearStats()
	{
		while (_statsRows.GetChildCount() > 0)
		{
			Node row = _statsRows.GetChild(0);
			_statsRows.RemoveChild(row);
			row.QueueFree();
		}

		_statsRequestGeneration++;
	}

	private void PopulateStats()
	{
		ClearStats();
		_ = PopulateStatsAsync(_statsRows, _statsRequestGeneration);
	}

	/// <summary>
	///     Fills in the multiplayer record once the authority has answered it. Nothing is shown when there is no record to show:
	///     signed out, an authority that could not be reached, or a card that was closed and reopened since the request went out.
	///     A newer request for the same card wins, so a rename that asks again never lets an older answer draw the rows twice.
	/// </summary>
	private async Task PopulateStatsAsync(VBoxContainer rows, int generation)
	{
		PlayerMatchStatsDto? stats = await GameContext.Account.GetMatchStatsAsync();
		if (stats == null || !GodotObject.IsInstanceValid(this) || !GodotObject.IsInstanceValid(rows)) return;
		if (generation != _statsRequestGeneration) return;

		var entries = new System.Collections.Generic.List<(string Key, string Value, Color ValueColor)>
		{
			(LocalizationManager.Translate("Matches Played"), stats.MatchesPlayed.ToString("N0", CultureInfo.CurrentCulture), StatValueColor),
			(LocalizationManager.Translate("Victories"), stats.Victories.ToString("N0", CultureInfo.CurrentCulture), VictoryColor),
			(LocalizationManager.Translate("Defeats"), stats.Defeats.ToString("N0", CultureInfo.CurrentCulture), DefeatColor)
		};

		if (stats.WinRatePercent.HasValue)
		{
			entries.Add((LocalizationManager.Translate("Win Rate"), string.Format(CultureInfo.CurrentCulture, "{0:0.#}%", stats.WinRatePercent.Value), WinRateColor));
		}

		foreach (var entry in entries) CreateStatRow(rows, entry.Key, entry.Value, entry.ValueColor);
	}

	private void CreateStatRow(VBoxContainer parent, string key, string value, Color valueColor)
	{
		var row = new HBoxContainer { Name = "PlayerProfileStatRow" };
		row.Alignment = BoxContainer.AlignmentMode.Center;
		row.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		row.AddThemeConstantOverride("separation", 8);

		var keyLabel = new Label { Name = "PlayerProfileStatKey" };
		keyLabel.Text = key + ":";
		keyLabel.HorizontalAlignment = HorizontalAlignment.Right;
		ApplyProfileLabelStyle(keyLabel, UserNameColor, 22);

		var valueLabel = new Label { Name = "PlayerProfileStatValue" };
		valueLabel.Text = value;
		valueLabel.HorizontalAlignment = HorizontalAlignment.Left;
		ApplyProfileLabelStyle(valueLabel, valueColor, 22);

		row.AddChild(keyLabel);
		row.AddChild(valueLabel);
		parent.AddChild(row);
	}

	private async Task LoadProvidersAsync()
	{
		AccountProvidersDto? providers = await GameContext.Account.GetProvidersAsync();
		if (!GodotObject.IsInstanceValid(this)) return;

		_serverRefusesSteam = false;
		_authorityUnreachable = providers == null;
		if (providers != null)
		{
			foreach (AccountProviderInfoDto provider in providers.Providers)
			{
				if (provider.Kind != AccountProviderNames.Steam) continue;

				_serverRefusesSteam = !provider.Available;
				break;
			}
		}

		RefreshState();
	}

	/// <summary>
	///     Mints one ticket on this machine and hands it to the authority with the Steam name, so a name typed while signed out
	///     never outlives the sign-in that replaces it. A ticket is spent by the attempt, so a refused name is retried with a
	///     fresh one; <paramref name="reauthenticate" /> repeats the call for the account already signed in, which is the second
	///     proof a rename asks for.
	/// </summary>
	private async Task SignInWithSteamAsync(bool reauthenticate)
	{
		if (_busy) return;

		if (!GameContext.Steam.IsAvailable)
		{
			ShowStatus(DescribeUnavailableSteam(), UIStyle.ColorGoldDull);
			return;
		}

		string requestedName = reauthenticate
			? GameContext.Account.DisplayName
			: _registrationNameRefused ? _registrationNameEdit.Text.Trim() : GameContext.Steam.UserName.Trim();
		if (requestedName.Length == 0) requestedName = GameContext.Steam.UserName.Trim();

		SetBusy(true);
		ClearStatus();
		ShowStatus(TranslationServer.Translate("Checking your Steam sign-in..."), UIStyle.ColorGoldDull);

		string ticket = await GameContext.Steam.RequestWebApiTicketAsync();
		if (!GodotObject.IsInstanceValid(this)) return;

		if (ticket.Length == 0)
		{
			SetBusy(false);
			ShowStatus(TranslationServer.Translate("Steam did not provide a sign-in ticket. Start the game through Steam and try again."), FailureColor);
			return;
		}

		AccountOperationResult result = await GameContext.Account.SignInWithSteamAsync(requestedName, ticket, GameContext.Steam.AppId);
		SetBusy(false);
		if (!GodotObject.IsInstanceValid(this)) return;

		if (!result.Succeeded)
		{
			if (!reauthenticate && IsDisplayNameRefusal(result.ErrorCode))
			{
				BeginChoosingAnotherName();
				ShowStatus(DescribeErrorCode(result.ErrorCode, result.Message), FailureColor);
				return;
			}

			ShowFailure(result);
			return;
		}

		_registrationNameRefused = false;

		if (_verifyThenRename)
		{
			_verifyThenRename = false;
			RefreshAccount();
			await RenameAsync();
			return;
		}

		RefreshAccount();
		ShowStatus(TranslationServer.Translate("You are signed in with Steam."), SuccessColor);
		SignedIn?.Invoke();
	}

	/// <summary>
	///     The authority refused the persona name as a DisplayName, which is the one case where the Steam name cannot register an
	///     account. The chooser becomes the source of the next attempt, so the player is asked for a name instead of the same
	///     refused persona being sent again on every retry, and the refused persona is offered as the starting point to edit.
	/// </summary>
	private void BeginChoosingAnotherName()
	{
		_registrationNameRefused = true;
		_nameChoiceBox.Visible = true;

		_registrationNameEdit.Text = GameContext.Steam.UserName.Trim();
		_registrationNameEdit.CaretColumn = _registrationNameEdit.Text.Length;
		_registrationNameEdit.GrabFocus();
		_registrationNameEdit.SelectAll();
	}

	private static bool IsDisplayNameRefusal(string errorCode)
	{
		return errorCode is AccountErrorCodes.DisplayNameEmpty
			or AccountErrorCodes.DisplayNameTooShort
			or AccountErrorCodes.DisplayNameTooLong
			or AccountErrorCodes.DisplayNameTooManyBytes
			or AccountErrorCodes.DisplayNameDisallowedCharacter
			or AccountErrorCodes.DisplayNameCombiningMarkWithoutBase
			or AccountErrorCodes.DisplayNameNeedsLetterOrDigit
			or AccountErrorCodes.DisplayNameAlreadyTaken
			or AccountErrorCodes.DisplayNameVisuallySimilar;
	}

	private async Task BeginVerifyThenRenameAsync()
	{
		if (_busy) return;

		_verifyThenRename = true;
		RefreshState();
		await SignInWithSteamAsync(true);
	}

	private async Task RenameAsync()
	{
		if (_busy || !GameContext.Account.IsSignedIn) return;

		SetBusy(true);
		ClearStatus();
		AccountOperationResult result = await GameContext.Account.RenameAsync(_renameEdit.Text);
		SetBusy(false);
		if (!GodotObject.IsInstanceValid(this)) return;

		if (!result.Succeeded)
		{
			if (result.ErrorCode == AccountErrorCodes.StepUpRequired)
			{
				_verifyThenRename = false;
				_stepUpBox.Visible = true;
				ShowStatus(DescribeErrorCode(AccountErrorCodes.StepUpRequired), UIStyle.ColorGoldDull);
				return;
			}

			ShowFailure(result);
			return;
		}

		_renameEdit.Text = string.Empty;
		_stepUpBox.Visible = false;
		RefreshAccount();
		ShowStatus(TranslationServer.Translate("Your username was changed."), SuccessColor);
	}

	private async Task SignOutAsync()
	{
		_verifyThenRename = false;
		_registrationNameRefused = false;
		SetBusy(true);
		await GameContext.Account.SignOutAsync();
		SetBusy(false);
		if (!GodotObject.IsInstanceValid(this)) return;

		ClearStatus();
		RefreshAccount();
	}

	private void ShowFailure(AccountOperationResult result)
	{
		if (result.Status == AccountOperationStatus.Unreachable)
		{
			ShowStatus(TranslationServer.Translate("The account authority could not be reached. Check your connection and try again."), FailureColor);
			return;
		}

		ShowStatus(DescribeErrorCode(result.ErrorCode, result.Message), FailureColor);
	}

	private static string DescribeErrorCode(string code, string fallback = "")
	{
		if (string.IsNullOrEmpty(code)) return string.IsNullOrEmpty(fallback) ? TranslationServer.Translate("The request was refused.") : fallback;

		switch (code)
		{
			case AccountErrorCodes.DisplayNameEmpty:
				return TranslationServer.Translate("A username is required.");
			case AccountErrorCodes.DisplayNameTooShort:
				return TranslationServer.Translate("A username must be at least 3 characters long, or 2 when it is written entirely in Han, kana or Hangul.");
			case AccountErrorCodes.DisplayNameTooLong:
				return TranslationServer.Translate("A username must be at most 20 characters long.");
			case AccountErrorCodes.DisplayNameTooManyBytes:
				return TranslationServer.Translate("A username must be at most 128 bytes when encoded as UTF-8.");
			case AccountErrorCodes.DisplayNameDisallowedCharacter:
				return TranslationServer.Translate("A username may only contain letters, digits, single spaces, hyphens, underscores, periods and apostrophes.");
			case AccountErrorCodes.DisplayNameCombiningMarkWithoutBase:
				return TranslationServer.Translate("A combining mark must follow the character it belongs to.");
			case AccountErrorCodes.DisplayNameNeedsLetterOrDigit:
				return TranslationServer.Translate("A username must contain at least one letter or digit.");
			case AccountErrorCodes.DisplayNameAlreadyTaken:
				return TranslationServer.Translate("That username is already taken.");
			case AccountErrorCodes.DisplayNameVisuallySimilar:
				return TranslationServer.Translate("That username looks too similar to a name another account already uses.");
			case AccountErrorCodes.CredentialsInvalid:
				return TranslationServer.Translate("Steam did not confirm this sign-in. Start the game through Steam and try again.");
			case AccountErrorCodes.StepUpRequired:
				return TranslationServer.Translate("Confirm it is you with Steam to continue.");
			case AccountErrorCodes.SessionInvalid:
			case AccountErrorCodes.SessionRequired:
				return TranslationServer.Translate("Your session is no longer valid. Sign in again.");
			case AccountErrorCodes.AccountNotAuthority:
				return TranslationServer.Translate("This server does not own accounts and refused the change.");
			case AccountErrorCodes.AccountNotFound:
				return TranslationServer.Translate("That account no longer exists.");
			case AccountErrorCodes.AccountStoreUnavailable:
				return TranslationServer.Translate("The account server could not save that. Try again in a moment.");
			default:
				return string.IsNullOrEmpty(fallback) ? TranslationServer.Translate("The request was refused.") : fallback;
		}
	}

	private void SetBusy(bool busy)
	{
		_busy = busy;
		_signInButton.Disabled = busy || _serverRefusesSteam;
		_renameButton.Disabled = busy;
		_signOutButton.Disabled = busy;
		_stepUpButton.Disabled = busy;
	}

	private void ShowStatus(string text, Color color)
	{
		_statusLabel.Text = text;
		_statusLabel.AddThemeColorOverride("font_color", color);
	}

	private void ClearStatus()
	{
		_statusLabel.Text = string.Empty;
	}

	private Button CreateBackButton()
	{
		var button = new Button { Name = "PlayerProfileBackButton" };
		button.Text = TranslationServer.Translate("BACK");
		button.CustomMinimumSize = new Vector2(210, 48);
		button.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		button.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		button.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
		button.Pressed += () =>
		{
			GameContext.UserInterface.PlayClickSound();
			QueueFree();
		};

		Texture2D buttonTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/player_profile_button.webp");
		if (buttonTexture != null)
		{
			var normalStyle = new StyleBoxTexture
			{
				Texture = buttonTexture,
				ContentMarginLeft = 16,
				ContentMarginRight = 16,
				ContentMarginTop = 8,
				ContentMarginBottom = 8
			};

			var hoverStyle = (StyleBoxTexture)normalStyle.Duplicate();
			hoverStyle.ModulateColor = new Color(1.15f, 1.15f, 1.2f);

			var pressedStyle = (StyleBoxTexture)normalStyle.Duplicate();
			pressedStyle.ModulateColor = new Color(0.85f, 0.85f, 0.9f);

			button.AddThemeStyleboxOverride("normal", normalStyle);
			button.AddThemeStyleboxOverride("hover", hoverStyle);
			button.AddThemeStyleboxOverride("pressed", pressedStyle);
			button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		}
		else
		{
			UIStyle.ApplyButtonStyleBoxes(button);
		}

		button.AddThemeFontOverride("font", UIStyle.FontNorseBold);
		button.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		button.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		button.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
		button.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
		button.AddThemeFontSizeOverride("font_size", 18);

		return button;
	}
}
