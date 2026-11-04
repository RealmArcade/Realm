using System;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Accounts;

namespace Realm.Client.UI;

/// <summary>
///     The in-game account screen. It never invents a name: every value it shows comes from the account authority, and every
///     refusal it reports is the typed code the authority answered with. Signing in proves the player's Steam identity with a
///     ticket Steamworks mints on their own machine, so the screen opens no browser and asks for no password; a player who stays
///     signed out keeps playing under the local name from the settings screen, which reaches Tier 0 and Tier 1 features only.
/// </summary>
[GlobalClass]
public partial class AccountDialog : Control
{
	private const string SteamIconPath = "res://Assets/UI/steam_icon.webp";

	private Label _titleLabel;
	private Label _statusLabel;
	private Label _warningLabel;
	private Label _accountIdentityLabel;
	private VBoxContainer _signedOutBox;
	private LineEdit _displayNameEdit;
	private Button _steamButton;
	private VBoxContainer _signedInBox;
	private LineEdit _renameEdit;
	private Button _renameButton;
	private Button _signOutButton;
	private VBoxContainer _stepUpBox;
	private Button _stepUpButton;
	private bool _busy;
	private bool _verifyThenRename;

	public Action? SignedIn { get; set; }

	public override void _Ready()
	{
		Name = "AccountDialog";
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
		Visible = true;
		ClearStatus();
		PrefillDisplayNameFromSteam();
		RefreshState();
		RefreshAccount();
		_ = LoadProvidersAsync();

		if (GameContext.Account.IsSignedIn) _ = GameContext.Account.RefreshAsync();
	}

	private void BuildLayout()
	{
		var backdrop = new ColorRect { Name = "AccountDialogBackdrop", Color = new Color(0.02f, 0.02f, 0.04f, 0.82f) };
		backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		AddChild(backdrop);

		var center = new CenterContainer { Name = "AccountDialogCenter" };
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var card = new PanelContainer { Name = "AccountDialogCard" };
		card.CustomMinimumSize = new Vector2(520, 0);
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateButtonNormal());
		center.AddChild(card);

		var margin = new MarginContainer { Name = "AccountDialogMargin" };
		margin.AddThemeConstantOverride("margin_left", 28);
		margin.AddThemeConstantOverride("margin_right", 28);
		margin.AddThemeConstantOverride("margin_top", 22);
		margin.AddThemeConstantOverride("margin_bottom", 22);
		card.AddChild(margin);

		var column = new VBoxContainer { Name = "AccountDialogColumn" };
		column.AddThemeConstantOverride("separation", 10);
		margin.AddChild(column);

		_titleLabel = CreateTitleLabel(column);

		_signedOutBox = new VBoxContainer { Name = "AccountDialogSignedOut" };
		_signedOutBox.AddThemeConstantOverride("separation", 8);
		column.AddChild(_signedOutBox);

		_displayNameEdit = CreateField(_signedOutBox, "DisplayNameField", TranslationServer.Translate("UserName"), out _);
		_displayNameEdit.MaxLength = DisplayNameRules.MaximumGraphemeClusters;

		var nameHintLabel = CreateBodyLabel(_signedOutBox, TranslationServer.Translate("This is the name other players see once you sign in."));
		nameHintLabel.Name = "AccountDialogNameHint";

		_steamButton = CreateActionButton(_signedOutBox, "CONTINUE WITH STEAM", SteamIconPath);
		_steamButton.Pressed += () => _ = ConnectWithSteamAsync(false);

		var unverifiedLabel = CreateBodyLabel(_signedOutBox, TranslationServer.Translate("You can also stay signed out and play under the name from your settings. Verified reviews and map publishing need a Steam sign-in."));
		unverifiedLabel.Name = "AccountDialogUnverifiedHint";

		_signedInBox = new VBoxContainer { Name = "AccountDialogSignedIn" };
		_signedInBox.AddThemeConstantOverride("separation", 10);
		column.AddChild(_signedInBox);

		_accountIdentityLabel = CreateBodyLabel(_signedInBox, string.Empty);
		_accountIdentityLabel.Name = "AccountDialogIdentity";

		_renameEdit = CreateField(_signedInBox, "RenameField", TranslationServer.Translate("New UserName"), out _);
		_renameEdit.MaxLength = DisplayNameRules.MaximumGraphemeClusters;
		_renameButton = CreateActionButton(_signedInBox, "CHANGE USERNAME");
		_renameButton.Pressed += () => _ = RenameAsync();

		_stepUpBox = new VBoxContainer { Name = "AccountDialogStepUp" };
		_stepUpBox.AddThemeConstantOverride("separation", 8);
		_signedInBox.AddChild(_stepUpBox);

		var stepUpLabel = CreateBodyLabel(_stepUpBox, TranslationServer.Translate("Confirm it is you with Steam before changing your username."));
		stepUpLabel.Name = "AccountDialogStepUpHint";

		_stepUpButton = CreateActionButton(_stepUpBox, "VERIFY WITH STEAM", SteamIconPath);
		_stepUpButton.Pressed += () => _ = BeginVerifyThenRenameAsync();

		_signOutButton = CreateActionButton(_signedInBox, "SIGN OUT");
		_signOutButton.Pressed += () => _ = SignOutAsync();

		var loginExplanationLabel = CreateBodyLabel(column, TranslationServer.Translate("Login only required for multi-player. Offline play is supported without login."));
		loginExplanationLabel.Name = "AccountDialogLoginExplanation";
		loginExplanationLabel.HorizontalAlignment = HorizontalAlignment.Center;

		_warningLabel = CreateBodyLabel(column, string.Empty);
		_warningLabel.Name = "AccountDialogWarning";
		_warningLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);

		_statusLabel = CreateBodyLabel(column, string.Empty);
		_statusLabel.Name = "AccountDialogStatus";

		var closeButton = CreateActionButton(column, "CLOSE");
		closeButton.Pressed += Close;
	}

	private Label CreateTitleLabel(VBoxContainer parent)
	{
		var label = new Label { Name = "AccountDialogTitle" };
		label.Text = TranslationServer.Translate("ACCOUNT");
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		label.AddThemeFontSizeOverride("font_size", 24);
		parent.AddChild(label);
		return label;
	}

	private void PrefillDisplayNameFromSteam()
	{
		if (GameContext.Account.IsSignedIn) return;
		if (_displayNameEdit == null || _displayNameEdit.Text.Length > 0) return;

		string personaName = GameContext.Steam.GetPersonaName();
		if (personaName.Length == 0) return;

		_displayNameEdit.Text = personaName;
	}

	private LineEdit CreateField(VBoxContainer parent, string fieldName, string fieldLabel, out Label fieldLabelNode)
	{
		var label = new Label { Name = $"AccountDialog{fieldName}Label" };
		label.Text = fieldLabel;
		fieldLabelNode = label;
		label.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		label.AddThemeFontSizeOverride("font_size", 13);
		parent.AddChild(label);

		var edit = new LineEdit { Name = $"AccountDialog{fieldName}Edit" };
		edit.CustomMinimumSize = new Vector2(0, 36);
		edit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		edit.PlaceholderText = fieldLabel;
		parent.AddChild(edit);
		return edit;
	}

	private Label CreateBodyLabel(VBoxContainer parent, string text)
	{
		var label = new Label { Name = "AccountDialogBodyLabel" };
		label.Text = text;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.CustomMinimumSize = new Vector2(440, 0);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		label.AddThemeFontSizeOverride("font_size", 13);
		parent.AddChild(label);
		return label;
	}

	private Button CreateActionButton(VBoxContainer parent, string textKey, string iconPath = "")
	{
		var button = new Button { Name = $"AccountDialog{textKey.Replace(" ", string.Empty)}Button" };
		UIStyle.ApplyButtonStyleBoxes(button);
		UIStyle.ApplyButtonText(button, textKey, 14);
		button.CustomMinimumSize = new Vector2(0, 40);
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

	private void RefreshState()
	{
		bool signedIn = GameContext.Account.IsSignedIn;
		bool steamAvailable = GameContext.Steam.IsAvailable;

		_signedOutBox.Visible = !signedIn;
		_signedInBox.Visible = signedIn;
		_steamButton.Visible = steamAvailable;
		_stepUpBox.Visible = _verifyThenRename;

		_titleLabel.Text = signedIn ? TranslationServer.Translate("ACCOUNT") : TranslationServer.Translate("SIGN IN TO REALM");

		if (!signedIn && !steamAvailable)
		{
			_warningLabel.Text = TranslationServer.Translate("Steamworks is not available in this build, so this screen cannot sign you in. Start the game through Steam, or keep playing under your own name.");
		}
	}

	private void RefreshAccount()
	{
		if (GameContext.Account.IsSignedIn)
		{
			string displayName = GameContext.Account.DisplayName;
			string accountId = GameContext.Account.AccountId.ToString(System.Globalization.CultureInfo.InvariantCulture);
			_accountIdentityLabel.Text = string.Format(TranslationServer.Translate("Signed in as {0} (account {1})"), displayName, accountId);
			_stepUpBox.Visible = _verifyThenRename;
		}

		RefreshState();
	}

	private async Task LoadProvidersAsync()
	{
		AccountProvidersDto? providers = await GameContext.Account.GetProvidersAsync();
		if (!GodotObject.IsInstanceValid(this)) return;
		if (providers == null)
		{
			_warningLabel.Text = TranslationServer.Translate("The account authority could not be reached.");
			return;
		}

		foreach (AccountProviderInfoDto provider in providers.Providers)
		{
			if (provider.Kind != AccountProviderNames.Steam) continue;
			if (provider.Available) return;

			_steamButton.Disabled = true;
			_warningLabel.Text = TranslationServer.Translate("This server does not accept Steam sign-in yet, so only unverified play is available.");
			return;
		}
	}

	/// <summary>
	///     Mints one ticket on this machine, hands it to the authority and adopts the session it answers. A ticket is spent by
	///     the attempt, so a refused name is retried with a fresh one; <paramref name="reauthenticate" /> repeats the same call for
	///     the account already signed in, which is the second proof a rename asks for.
	/// </summary>
	private async Task ConnectWithSteamAsync(bool reauthenticate)
	{
		if (_busy) return;

		if (!GameContext.Steam.IsAvailable)
		{
			ShowStatus(TranslationServer.Translate("Steamworks is not available in this build, so this screen cannot sign you in. Start the game through Steam, or keep playing under your own name."), UIStyle.ColorGoldDull);
			return;
		}

		string displayName = reauthenticate ? GameContext.Account.DisplayName : _displayNameEdit.Text.Trim();

		SetBusy(true);
		ClearStatus();
		ShowStatus(TranslationServer.Translate("Checking your Steam sign-in..."), UIStyle.ColorGoldDull);

		string ticket = await GameContext.Steam.RequestWebApiTicketAsync();
		if (!GodotObject.IsInstanceValid(this)) return;

		if (ticket.Length == 0)
		{
			SetBusy(false);
			ShowStatus(TranslationServer.Translate("Steam did not provide a sign-in ticket. Start the game through Steam and try again."), new Color(0.95f, 0.6f, 0.4f));
			return;
		}

		AccountOperationResult result = await GameContext.Account.SignInWithSteamAsync(displayName, ticket, GameContext.Steam.AppId);
		SetBusy(false);
		if (!GodotObject.IsInstanceValid(this)) return;

		if (!result.Succeeded)
		{
			ShowFailure(result);
			return;
		}

		if (_verifyThenRename)
		{
			_verifyThenRename = false;
			RefreshAccount();
			await RenameAsync();
			return;
		}

		_displayNameEdit.Text = string.Empty;
		RefreshAccount();
		ShowStatus(TranslationServer.Translate("You are signed in with Steam."), new Color(0.55f, 0.95f, 0.55f));
		SignedIn?.Invoke();
	}

	private async Task BeginVerifyThenRenameAsync()
	{
		if (_busy) return;

		_verifyThenRename = true;
		RefreshState();
		await ConnectWithSteamAsync(true);
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
		ShowStatus(TranslationServer.Translate("Your username was changed."), new Color(0.55f, 0.95f, 0.55f));
	}

	private async Task SignOutAsync()
	{
		_verifyThenRename = false;
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
			ShowStatus(TranslationServer.Translate("The account authority could not be reached. Check your connection and try again."), new Color(0.95f, 0.6f, 0.4f));
			return;
		}

		ShowStatus(DescribeErrorCode(result.ErrorCode, result.Message), new Color(0.95f, 0.6f, 0.4f));
	}

	private static string DescribeErrorCode(string code, string fallback = "")
	{
		if (string.IsNullOrEmpty(code)) return string.IsNullOrEmpty(fallback) ? TranslationServer.Translate("The request was refused.") : fallback;

		switch (code)
		{
			case AccountErrorCodes.DisplayNameEmpty:
				return TranslationServer.Translate("A username is required.");
			case AccountErrorCodes.DisplayNameTooShort:
				return TranslationServer.Translate("A username must be at least 3 characters long.");
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
			default:
				return string.IsNullOrEmpty(fallback) ? TranslationServer.Translate("The request was refused.") : fallback;
		}
	}

	private void SetBusy(bool busy)
	{
		_busy = busy;
		_steamButton.Disabled = busy;
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

	private void Close()
	{
		QueueFree();
	}
}
