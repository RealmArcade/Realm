using System;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Accounts;

namespace Realm.Client.UI;

/// <summary>
///     The in-game register, sign-in and account screen. It never invents a name: every value it shows comes from the account
///     authority, and every refusal it reports is the typed code the authority answered with.
/// </summary>
[GlobalClass]
public partial class AccountDialog : Control
{
	private enum DialogMode
	{
		SignIn,
		Register,
		ChooseProviderDisplayName,
		Account
	}

	private const string DiscordIconPath = "res://Assets/UI/discord_icon.webp";
	private const string SteamIconPath = "res://Assets/UI/steam_icon.webp";

	private DialogMode _mode = DialogMode.SignIn;

	private Label _titleLabel;
	private Label _statusLabel;
	private Label _warningLabel;
	private Label _accountIdentityLabel;
	private Button _signInTabButton;
	private Button _registerTabButton;
	private VBoxContainer _credentialsBox;
	private LineEdit _displayNameEdit;
	private Label _passwordFieldLabel;
	private LineEdit _passwordEdit;
	private Button _submitButton;
	private VBoxContainer _accountBox;
	private LineEdit _renameEdit;
	private Button _renameButton;
	private Button _signOutButton;
	private Button _discordButton;
	private Button _steamButton;
	private Button _linkDiscordButton;
	private Button _linkSteamButton;
	private VBoxContainer _providerBox;
	private bool _busy;

	private string _registrationTicket = string.Empty;
	private string _providerSuggestedNameErrorCode = string.Empty;

	public Action? SignedIn { get; set; }

	public override void _Ready()
	{
		Name = "AccountDialog";
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		BuildLayout();
		RefreshMode();
		RefreshAccount();
	}

	public void RefreshPresentation()
	{
		if (!GodotObject.IsInstanceValid(this)) return;
		RefreshAccount();
	}

	public void Open()
	{
		_mode = GameContext.Account.IsSignedIn ? DialogMode.Account : DialogMode.SignIn;
		Visible = true;
		ClearStatus();
		RefreshMode();
		RefreshAccount();
		_ = LoadProvidersAsync();
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
		column.AddChild(CreateTabRow());

		_credentialsBox = new VBoxContainer { Name = "AccountDialogCredentials" };
		_credentialsBox.AddThemeConstantOverride("separation", 8);
		column.AddChild(_credentialsBox);

		_displayNameEdit = CreateField(_credentialsBox, "DisplayNameField", TranslationServer.Translate("UserName"), out _);
		_passwordEdit = CreateField(_credentialsBox, "PasswordField", TranslationServer.Translate("Password"), out _passwordFieldLabel);
		_passwordEdit.Secret = true;

		_providerBox = new VBoxContainer { Name = "AccountDialogProviders" };
		_providerBox.AddThemeConstantOverride("separation", 8);
		column.AddChild(_providerBox);

		_discordButton = CreateActionButton(_providerBox, "CONTINUE WITH DISCORD", DiscordIconPath);
		_discordButton.Pressed += () => _ = StartProviderAsync(AccountProviderNames.Discord, false);
		_steamButton = CreateActionButton(_providerBox, "CONTINUE WITH STEAM", SteamIconPath);
		_steamButton.Pressed += () => _ = StartProviderAsync(AccountProviderNames.Steam, false);

		_submitButton = CreateActionButton(column, "SIGN IN");
		_submitButton.Pressed += () => _ = SubmitCredentialsAsync();

		_accountBox = new VBoxContainer { Name = "AccountDialogAccount" };
		_accountBox.AddThemeConstantOverride("separation", 10);
		column.AddChild(_accountBox);

		_accountIdentityLabel = CreateBodyLabel(_accountBox, string.Empty);
		_accountIdentityLabel.Name = "AccountDialogIdentity";

		_renameEdit = CreateField(_accountBox, "RenameField", TranslationServer.Translate("New UserName"), out _);
		_renameButton = CreateActionButton(_accountBox, "CHANGE USERNAME");
		_renameButton.Pressed += () => _ = RenameAsync();

		_linkDiscordButton = CreateActionButton(_accountBox, "LINK DISCORD ACCOUNT", DiscordIconPath);
		_linkDiscordButton.Pressed += () => _ = StartProviderAsync(AccountProviderNames.Discord, true);
		_linkSteamButton = CreateActionButton(_accountBox, "LINK STEAM ACCOUNT", SteamIconPath);
		_linkSteamButton.Pressed += () => _ = StartProviderAsync(AccountProviderNames.Steam, true);

		_signOutButton = CreateActionButton(_accountBox, "SIGN OUT");
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

	private HBoxContainer CreateTabRow()
	{
		var row = new HBoxContainer { Name = "AccountDialogTabs" };
		row.Alignment = BoxContainer.AlignmentMode.Center;
		row.AddThemeConstantOverride("separation", 8);

		_signInTabButton = new Button { Name = "AccountDialogSignInTab" };
		UIStyle.ApplyButtonStyleBoxes(_signInTabButton);
		UIStyle.ApplyButtonText(_signInTabButton, "SIGN IN", 14);
		_signInTabButton.CustomMinimumSize = new Vector2(150, 38);
		_signInTabButton.AddThemeConstantOverride("icon_max_width", 20);
		_signInTabButton.Pressed += () =>
		{
			_mode = DialogMode.SignIn;
			ClearStatus();
			RefreshMode();
		};
		row.AddChild(_signInTabButton);

		_registerTabButton = new Button { Name = "AccountDialogRegisterTab" };
		UIStyle.ApplyButtonStyleBoxes(_registerTabButton);
		UIStyle.ApplyButtonText(_registerTabButton, "REGISTER", 14);
		_registerTabButton.CustomMinimumSize = new Vector2(150, 38);
		_registerTabButton.AddThemeConstantOverride("icon_max_width", 20);
		_registerTabButton.Pressed += () =>
		{
			_mode = DialogMode.Register;
			ClearStatus();
			RefreshMode();
		};
		row.AddChild(_registerTabButton);

		return row;
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

	private void RefreshMode()
	{
		bool signedIn = GameContext.Account.IsSignedIn;
		bool choosingName = _mode == DialogMode.ChooseProviderDisplayName;

		_signInTabButton.Visible = !signedIn;
		_registerTabButton.Visible = !signedIn;
		_credentialsBox.Visible = !signedIn;
		_passwordEdit.Visible = !choosingName;
		_passwordFieldLabel.Visible = !choosingName;
		_submitButton.Visible = !signedIn;
		_accountBox.Visible = signedIn;
		_providerBox.Visible = !signedIn && !choosingName;
		_linkDiscordButton.Visible = signedIn;
		_linkSteamButton.Visible = signedIn;

		_titleLabel.Text = signedIn
			? TranslationServer.Translate("ACCOUNT")
			: _mode == DialogMode.Register
				? TranslationServer.Translate("CREATE ACCOUNT")
				: choosingName
					? TranslationServer.Translate("CHOOSE YOUR USERNAME")
					: TranslationServer.Translate("SIGN IN TO REALM");

		_submitButton.Text = _mode == DialogMode.Register || choosingName
			? TranslationServer.Translate("CREATE ACCOUNT")
			: TranslationServer.Translate("SIGN IN");

	}

	private void RefreshAccount()
	{
		if (GameContext.Account.IsSignedIn)
		{
			string displayName = GameContext.Account.DisplayName;
			string accountId = GameContext.Account.AccountId.ToString(System.Globalization.CultureInfo.InvariantCulture);
			_accountIdentityLabel.Text = string.Format(TranslationServer.Translate("Signed in as {0} (account {1})"), displayName, accountId);
			_linkDiscordButton.Disabled = Array.IndexOf(GameContext.Account.LinkedProviders, AccountProviderNames.Discord) >= 0;
			_linkSteamButton.Disabled = Array.IndexOf(GameContext.Account.LinkedProviders, AccountProviderNames.Steam) >= 0;
		}

		RefreshMode();
	}

	private void ApplyProviderAvailability(AccountProvidersDto? providers)
	{
		if (providers == null)
		{
			_warningLabel.Text = TranslationServer.Translate("The account authority could not be reached.");
			return;
		}

		foreach (AccountProviderInfoDto provider in providers.Providers)
		{
			if (provider.Kind == AccountProviderNames.Discord)
			{
				_discordButton.Disabled = !provider.Available;
				_linkDiscordButton.Disabled |= !provider.Available;
			}
			else if (provider.Kind == AccountProviderNames.Steam)
			{
				_steamButton.Disabled = !provider.Available;
				_linkSteamButton.Disabled |= !provider.Available;
			}
		}

		_warningLabel.Text = string.Empty;
	}

	private static string ProviderLabel(string provider)
	{
		if (string.Equals(provider, AccountProviderNames.Discord, StringComparison.OrdinalIgnoreCase)) return TranslationServer.Translate("Discord");
		if (string.Equals(provider, AccountProviderNames.Steam, StringComparison.OrdinalIgnoreCase)) return TranslationServer.Translate("Steam");
		return provider;
	}

	private async Task LoadProvidersAsync()
	{
		AccountProvidersDto? providers = await GameContext.Account.GetProvidersAsync();
		if (!GodotObject.IsInstanceValid(this)) return;
		ApplyProviderAvailability(providers);
	}

	private async Task SubmitCredentialsAsync()
	{
		if (_busy || GameContext.Account.IsSignedIn) return;

		string displayName = _displayNameEdit.Text.Trim();
		if (_mode == DialogMode.ChooseProviderDisplayName)
		{
			await CompleteProviderRegistrationAsync(displayName);
			return;
		}

		SetBusy(true);
		ClearStatus();
		string password = _passwordEdit.Text;

		AccountOperationResult result = _mode == DialogMode.Register
			? await GameContext.Account.RegisterAsync(displayName, password)
			: await GameContext.Account.SignInAsync(displayName, password);

		SetBusy(false);
		if (!GodotObject.IsInstanceValid(this)) return;

		if (!result.Succeeded)
		{
			ShowFailure(result);
			return;
		}

		_passwordEdit.Text = string.Empty;
		_mode = DialogMode.Account;
		RefreshAccount();
		SignedIn?.Invoke();
	}

	private async Task CompleteProviderRegistrationAsync(string displayName)
	{
		if (_registrationTicket.Length == 0) return;

		SetBusy(true);
		ClearStatus();
		AccountOperationResult result = await GameContext.Account.CompleteProviderRegistrationAsync(_registrationTicket, displayName);
		SetBusy(false);
		if (!GodotObject.IsInstanceValid(this)) return;

		if (!result.Succeeded)
		{
			ShowFailure(result);
			return;
		}

		_registrationTicket = string.Empty;
		_mode = DialogMode.Account;
		RefreshAccount();
		SignedIn?.Invoke();
	}

	private async Task StartProviderAsync(string provider, bool linkToCurrentAccount)
	{
		if (_busy) return;

		SetBusy(true);
		ClearStatus();
		ShowStatus(TranslationServer.Translate("Finish signing in with your browser."), UIStyle.ColorGoldDull);

		AccountOperationResult result = await GameContext.Account.StartProviderSignInAsync(provider, linkToCurrentAccount);
		SetBusy(false);
		if (!GodotObject.IsInstanceValid(this)) return;

		if (!result.Succeeded)
		{
			ShowFailure(result);
			return;
		}

		if (result.RegistrationTicket.Length > 0)
		{
			_registrationTicket = result.RegistrationTicket;
			_mode = DialogMode.ChooseProviderDisplayName;
			_displayNameEdit.Text = result.SuggestedDisplayName;
			_passwordEdit.Text = string.Empty;
			RefreshMode();

			if (result.SuggestedDisplayNameErrorCode.Length > 0)
			{
				ShowStatus(string.Format(TranslationServer.Translate("Your {0} name cannot be used: {1} Choose a different username."),
					ProviderLabel(result.Provider), DescribeErrorCode(result.SuggestedDisplayNameErrorCode)), new Color(0.95f, 0.6f, 0.4f));
			}
			else
			{
				ShowStatus(TranslationServer.Translate("You can keep the suggested name or choose your own."), UIStyle.ColorGoldDull);
			}

			return;
		}

		_registrationTicket = string.Empty;
		_mode = DialogMode.Account;
		RefreshAccount();
		SignedIn?.Invoke();
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
			ShowFailure(result);
			return;
		}

		_renameEdit.Text = string.Empty;
		RefreshAccount();
		ShowStatus(TranslationServer.Translate("Your username was changed."), new Color(0.55f, 0.95f, 0.55f));
	}

	private async Task SignOutAsync()
	{
		SetBusy(true);
		await GameContext.Account.SignOutAsync();
		SetBusy(false);
		if (!GodotObject.IsInstanceValid(this)) return;

		_mode = DialogMode.SignIn;
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
				return TranslationServer.Translate("The username or password is incorrect.");
			case AccountErrorCodes.PasswordTooShort:
				return TranslationServer.Translate("A password must be at least 8 characters long.");
			case AccountErrorCodes.Throttled:
				return TranslationServer.Translate("Too many failed attempts. Try again later.");
			case AccountErrorCodes.SessionInvalid:
			case AccountErrorCodes.SessionRequired:
				return TranslationServer.Translate("Your session is no longer valid. Sign in again.");
			case AccountErrorCodes.ProviderUnavailable:
				return TranslationServer.Translate("That provider is not available on this server.");
			case AccountErrorCodes.ProviderAlreadyLinked:
				return TranslationServer.Translate("This account already links that provider.");
			case AccountErrorCodes.ProviderLinkOwnedByAnotherAccount:
				return TranslationServer.Translate("That provider account already belongs to another Realm account.");
			case AccountErrorCodes.AuthorizationStateExpired:
				return TranslationServer.Translate("The sign-in timed out. Try again.");
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
		_submitButton.Disabled = busy;
		_discordButton.Disabled = busy;
		_steamButton.Disabled = busy;
		_renameButton.Disabled = busy;
		_signOutButton.Disabled = busy;
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
