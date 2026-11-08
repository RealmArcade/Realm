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
///     this card, so the name is stated once and changed in a single spot. The name is always a label with a pencil beside it,
///     because the username card is the only surface that ever asks for a name: a first sign-in whose Steam persona is already
///     taken, a later rename and the local name a signed-out player plays under all arrive at the same card, so a conflict
///     never looks like a second, unexplained place to type.
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

	private Label _userNameValueLabel = null!;
	private VBoxContainer _statsRows = null!;
	private VBoxContainer _signedOutBox = null!;
	private Button _signInButton = null!;
	private VBoxContainer _signedInBox = null!;
	private Label _identityLabel = null!;
	private Button _signOutButton = null!;
	private Label _warningLabel = null!;
	private Label _statusLabel = null!;
	private UserNameChangeDialog _nameChangeDialog = null!;
	private bool _busy;
	private bool _serverRefusesSteam;
	private bool _authorityUnreachable;
	private int _statsRequestGeneration;

	public Action? SignedIn { get; set; }

	/// <summary>
	///     Raised once a username change is in force, so whoever shows the name outside this card repaints it. A rename is the
	///     authority's and a local name is this machine's, but both are the name other players will see.
	/// </summary>
	public Action? UserNameChanged { get; set; }

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

		column.AddChild(CreateBackButton());
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
		Texture2D? flagTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/alliance_flag.webp");
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

		row.AddChild(keyLabel);
		row.AddChild(_userNameValueLabel);
		row.AddChild(CreateUserNameEditButton());

		return row;
	}

	/// <summary>
	///     The pencil the name is changed with. Signed out it edits the name this machine plays under; signed in it renames the
	///     account. Either way the name is typed into the username card, so the profile card itself never holds an editable copy
	///     of a value the account would overwrite.
	/// </summary>
	private Button CreateUserNameEditButton()
	{
		var button = new Button { Name = "PlayerProfileUserNameEditButton" };
		button.Text = UnicodeIcons.EDIT;
		button.AddThemeConstantOverride("icon_max_width", 0);
		if (UIStyle.FontAwesome != null) button.AddThemeFontOverride("font", UIStyle.FontAwesome);
		button.AddThemeFontSizeOverride("font_size", 15);
		button.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		UIStyle.ApplyGoldHoverColors(button);
		button.CustomMinimumSize = new Vector2(34, 34);
		button.TooltipText = TranslationServer.Translate("Change your username");
		button.FocusMode = FocusModeEnum.None;
		button.MouseDefaultCursorShape = CursorShape.PointingHand;
		button.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		UIStyle.ApplyButtonStyleBoxesClearingFocus(button);
		button.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			OpenUserNameChangeDialog();
		};

		return button;
	}

	private void OpenUserNameChangeDialog()
	{
		bool signedIn = GameContext.Account.IsSignedIn;
		OpenUserNameChangeDialog(
			TranslationServer.Translate("CHANGE USERNAME"),
			signedIn ? GameContext.Account.DisplayName : UnverifiedNameMark.Apply(GameContext.Account.EffectiveUserName),
			signedIn ? GameContext.Account.DisplayName : GameContext.Account.EffectiveUserName,
			signedIn
				? TranslationServer.Translate("At least 3 characters. Letters, digits, single spaces, hyphens, underscores, periods and apostrophes are allowed.")
				: TranslationServer.Translate("This name is stored on this machine only, and other players see it with an unverified mark."),
			ApplyUserNameAsync);
	}

	private void OpenUserNameChangeDialog(string titleText, string currentName, string suggestedName, string hintText, Func<string, Task<UserNameChangeResult>> submitAsync)
	{
		if (_nameChangeDialog != null && GodotObject.IsInstanceValid(_nameChangeDialog)) _nameChangeDialog.QueueFree();

		_nameChangeDialog = new UserNameChangeDialog { Name = "UserNameChangeDialog" };
		AddChild(_nameChangeDialog);
		_nameChangeDialog.Open(titleText, currentName, suggestedName, hintText, submitAsync);
	}

	private VBoxContainer CreateSignedOutBox()
	{
		_signedOutBox = new VBoxContainer { Name = "PlayerProfileSignedOut" };
		_signedOutBox.AddThemeConstantOverride("separation", 10);

		_signInButton = CreateActionButton(_signedOutBox, "SIGN IN WITH STEAM", SteamIconPath);
		_signInButton.Pressed += StartSteamSignIn;

		Label signedOutHintLabel = CreateBodyLabel(_signedOutBox, TranslationServer.Translate("Some features are unavailable when logged out. ⍰ indicates a logged out or un-verified user."));
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

	private void RefreshState()
	{
		if (!GodotObject.IsInstanceValid(this)) return;

		bool signedIn = GameContext.Account.IsSignedIn;
		bool steamAvailable = GameContext.Steam.IsAvailable;

		if (signedIn) AdoptAccountNameAsLocalName();

		_userNameValueLabel.Text = signedIn
			? GameContext.Account.DisplayName
			: UnverifiedNameMark.Apply(GameContext.Account.EffectiveUserName);

		_signedOutBox.Visible = !signedIn;
		_signedInBox.Visible = signedIn;
		_signInButton.Visible = steamAvailable;
		_signInButton.Disabled = _busy || _serverRefusesSteam;

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
			_warningLabel.Text = GameContext.Steam.UnavailableExplanation;
			return;
		}

		if (_serverRefusesSteam)
		{
			_warningLabel.Text = TranslationServer.Translate("This server does not accept Steam sign-in yet, so only unverified play is available.");
			return;
		}

		_warningLabel.Text = _authorityUnreachable ? TranslationServer.Translate("The account authority could not be reached.") : string.Empty;
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

	private void StartSteamSignIn()
	{
		_ = SignInOrAskForANameAsync();
	}

	/// <summary>
	///     Signs in under the Steam persona. A persona that cannot be registered is not a dead end: the username card opens
	///     already holding the refused name, and what the player types there is the name the next attempt registers, so the
	///     conflict is answered by the one surface that ever asks for a name.
	/// </summary>
	private async Task SignInOrAskForANameAsync()
	{
		if (_busy) return;

		AccountOperationResult result = await SignInWithSteamAsync(GameContext.Steam.UserName.Trim(), false);
		if (!GodotObject.IsInstanceValid(this) || result.Succeeded) return;

		if (IsDisplayNameRefusal(result.ErrorCode))
		{
			ClearStatus();
			OpenNameConflictDialog(result);
			return;
		}

		ShowFailure(result);
	}

	/// <summary>
	///     Mints one ticket on this machine and hands it to the authority with the name being registered. A ticket is spent by
	///     the attempt, so a refused name is retried with a fresh one. <paramref name="reauthenticate" /> repeats the call for the
	///     account already signed in, which is the second proof a rename asks for; that path leaves the card as it stands, because
	///     the player is in the middle of a change rather than arriving.
	/// </summary>
	private async Task<AccountOperationResult> SignInWithSteamAsync(string requestedName, bool reauthenticate)
	{
		if (_busy) return AccountOperationResult.Rejected(string.Empty, TranslationServer.Translate("Another account request is still in progress."));

		if (!GameContext.Steam.IsAvailable)
		{
			return AccountOperationResult.Rejected(string.Empty, GameContext.Steam.UnavailableExplanation);
		}

		if (requestedName.Length == 0) requestedName = GameContext.Steam.UserName.Trim();

		SetBusy(true);
		ClearStatus();
		ShowStatus(
			reauthenticate ? TranslationServer.Translate("Confirming your Steam sign-in...") : TranslationServer.Translate("Checking your Steam sign-in..."),
			UIStyle.ColorGoldDull);

		string ticket = await GameContext.Steam.RequestWebApiTicketAsync();
		if (!GodotObject.IsInstanceValid(this)) return AccountOperationResult.Rejected(string.Empty, string.Empty);

		if (ticket.Length == 0)
		{
			SetBusy(false);
			return AccountOperationResult.Rejected(string.Empty, TranslationServer.Translate("Steam did not provide a sign-in ticket. Start the game through Steam and try again."));
		}

		AccountOperationResult result = await GameContext.Account.SignInWithSteamAsync(requestedName, ticket, GameContext.Steam.AppId);
		SetBusy(false);
		if (!GodotObject.IsInstanceValid(this)) return result;

		if (!result.Succeeded) return result;

		RefreshAccount();
		if (reauthenticate) return result;

		ShowStatus(TranslationServer.Translate("You are signed in with Steam."), SuccessColor);
		SignedIn?.Invoke();
		return result;
	}

	/// <summary>
	///     Applies the name the username card collected. Signed out it is the name this machine plays under and is stored as
	///     typed, because nothing about an unverified name is a claim the authority has to accept. Signed in it is a rename,
	///     which the authority may answer with a step-up demand; that proof is asked for and spent here, so the player presses
	///     Change once and the Steam confirmation the attempt already needs finishes the rename instead of becoming a second
	///     button on the card.
	/// </summary>
	private async Task<UserNameChangeResult> ApplyUserNameAsync(string requestedName)
	{
		if (!GameContext.Account.IsSignedIn)
		{
			GameSettings.PlayerName = requestedName;
			GameSettings.Save();
			ClearStatus();
			RefreshState();
			UserNameChanged?.Invoke();
			return UserNameChangeResult.Succeeded;
		}

		AccountOperationResult result = await GameContext.Account.RenameAsync(requestedName);
		if (!GodotObject.IsInstanceValid(this)) return UserNameChangeResult.Refused(TranslationServer.Translate("The request was refused."));

		if (result.ErrorCode == AccountErrorCodes.StepUpRequired)
		{
			AccountOperationResult proof = await SignInWithSteamAsync(GameContext.Account.DisplayName, true);
			if (!GodotObject.IsInstanceValid(this)) return UserNameChangeResult.Refused(TranslationServer.Translate("The request was refused."));
			if (!proof.Succeeded) return UserNameChangeResult.Refused(DescribeErrorCode(proof.ErrorCode, proof.Message));

			result = await GameContext.Account.RenameAsync(requestedName);
			if (!GodotObject.IsInstanceValid(this)) return UserNameChangeResult.Refused(TranslationServer.Translate("The request was refused."));
		}

		if (!result.Succeeded) return UserNameChangeResult.Refused(DescribeErrorCode(result.ErrorCode, result.Message));

		ClearStatus();
		RefreshAccount();
		ShowStatus(TranslationServer.Translate("Your username was changed."), SuccessColor);
		UserNameChanged?.Invoke();
		return UserNameChangeResult.Succeeded;
	}

	private void OpenNameConflictDialog(AccountOperationResult refusal)
	{
		string refusedName = GameContext.Steam.UserName.Trim();
		string hintText = string.Format(
			TranslationServer.Translate("{0} Choose the username this account will use."),
			DescribeErrorCode(refusal.ErrorCode, refusal.Message));

		OpenUserNameChangeDialog(
			TranslationServer.Translate("CHOOSE YOUR USERNAME"),
			refusedName.Length > 0 ? refusedName : GameContext.Account.EffectiveUserName,
			refusedName,
			hintText,
			RegisterWithChosenNameAsync);
	}

	/// <summary>
	///     Answers a first sign-in the authority refused on the name, when nobody pressed anything: the card is already open
	///     because the launch attempt got this far, and the username dialog is the surface that refusal is resolved on. What the
	///     player types there registers the account, exactly as it does from the sign-in button.
	/// </summary>
	public void OpenNameConflict(AccountOperationResult refusal)
	{
		OpenNameConflictDialog(refusal);
	}

	private async Task<UserNameChangeResult> RegisterWithChosenNameAsync(string chosenName)
	{
		AccountOperationResult result = await SignInWithSteamAsync(chosenName, false);
		if (!GodotObject.IsInstanceValid(this)) return UserNameChangeResult.Refused(TranslationServer.Translate("The request was refused."));
		if (result.Succeeded) return UserNameChangeResult.Succeeded;

		return UserNameChangeResult.Refused(DescribeErrorCode(result.ErrorCode, result.Message));
	}

	public static bool IsDisplayNameRefusal(string errorCode)
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

	private async Task SignOutAsync()
	{
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
		_signOutButton.Disabled = busy;
		UIStyle.ApplyButtonText(
			_signInButton,
			busy ? TranslationServer.Translate("SIGNING IN...") : TranslationServer.Translate("SIGN IN WITH STEAM"),
			14);
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
		button.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		button.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			QueueFree();
		};

		Texture2D? buttonTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/player_profile_button.webp");
		if (buttonTexture != null)
		{
			UIStyle.ApplyTintedArtworkButton(button, "res://Assets/UI/player_profile_button.webp",
				new Color(1f, 1f, 1f), new Color(1.15f, 1.15f, 1.2f), new Color(0.85f, 0.85f, 0.9f),
				16, 16, 8, 8);
			button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		}
		else
		{
			UIStyle.ApplyButtonStyleBoxesClearingFocus(button);
		}

		button.AddThemeFontOverride("font", UIStyle.FontNorseBold);
		button.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		UIStyle.ApplyGoldHoverAndFocusColors(button);
		button.AddThemeFontSizeOverride("font_size", 18);

		return button;
	}
}
