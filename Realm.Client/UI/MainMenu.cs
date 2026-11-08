using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Network;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Accounts;

namespace Realm.Client.UI;

[GlobalClass]
public partial class MainMenu : Control
{
	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

	private Panel _bgPanel = null!;
	private Panel _leftPillar = null!;
	private Panel _rightPillar = null!;
	private PanelContainer _centralPanel = null!;

	private TextureRect _gameLogo = null!;
	private Button _playButton = null!;
	private Button _singlePlayerButton = null!;
	private Button _mapDiscoveryButton = null!;
	private Button _creatorDiscoveryButton = null!;
	private Button _mapEditorButton = null!;
	private Button _replaysButton = null!;
	private Button _storageButton = null!;
	private Button _settingsButton = null!;
	private Button _profileButton = null!;
	private Button _quitButton = null!;
	private Button _socialButton = null!;
	private Control _socialPopoverOverlay = null!;
	private Control _socialPopover = null!;
	private Button _wishlistButton = null!;
	private Button _websiteButton = null!;
	private Button _discordButton = null!;
	private Button _youtubeButton = null!;
	private Button _donateButton = null!;
	private Button _announcementsButton = null!;
	private Button _bugReportButton = null!;
	private PlayerProfileDialog? _profileDialog;
	private AnnouncementsDialog _announcementsDialog = null!;
	private OptionButton _versionDropdown = null!;
	private Label _outdatedLabel = null!;

	private static Font _norseFont = null!;
	private static Font _norseBoldFont = null!;


	private readonly string[] _runes = { "ᚠ", "ᚢ", "ᚦ", "ᚨ", "ᚱ", "ᚲ", "ᚷ", "ᚹ", "ᚺ", "ᚾ", "ᛁ", "ᛃ", "ᛇ", "ᛈ", "ᛉ", "ᛊ", "ᛏ", "ᛒ", "ᛖ", "ᛗ", "ᛚ", "ᛜ", "ᛞ", "ᛟ" };

	public override void _Ready()
	{
		TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		BindNodes();
		StylePanels();
		SetupButtons();
		SetupSocialPopover();

		PopulateRunicPillar(GetNode<VBoxContainer>("LeftPillar/RuneContainer"));
		PopulateRunicPillar(GetNode<VBoxContainer>("RightPillar/RuneContainer"));

		GameContext.Account.Changed += OnAccountChanged;
		RefreshAccountPresentation();
		RestoreAccountSession();
	}

	public override void _ExitTree()
	{
		GameContext.Account.Changed -= OnAccountChanged;
	}

	private void OnAccountChanged()
	{
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;

		Callable.From(RefreshAccountPresentation).CallDeferred();
	}

	/// <summary>
	///     Adopts the session stored at the last sign-in before the player asks for anything. Nothing is asked of the authority
	///     here, so the menu is signed in just as quickly whether or not it is reachable, and the account screen is what confirms
	///     the session afterwards. With no session to adopt, the Steam identity this machine already has is signed in quietly in
	///     the background, so a player who owns the game through Steam never has to find the sign-in button.
	/// </summary>
	private void RestoreAccountSession()
	{
		GameContext.Account.Restore();
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;

		Callable.From(RefreshAccountPresentation).CallDeferred();

		if (!GameContext.Account.IsSignedIn) _ = SignInWithSteamAtLaunchAsync();
	}

	/// <summary>
	///     Signs in with Steam on its own, and says nothing about how it went: the name on the profile button simply stops being
	///     unverified, and a launch where Steamworks is absent, the authority is unreachable or the ticket is refused leaves the
	///     screen exactly as it was. The one outcome that asks for the player is a Steam persona the authority will not register,
	///     which opens the card already showing the username dialog, because that card is the only surface that ever asks for a name.
	/// </summary>
	private async Task SignInWithSteamAtLaunchAsync()
	{
		AccountOperationResult result = await GameContext.Account.SignInWithSteamIdentityAsync();
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;
		if (result.Succeeded) return;
		if (!PlayerProfileDialog.IsDisplayNameRefusal(result.ErrorCode)) return;

		OpenProfileDialog();

		if (_profileDialog != null && GodotObject.IsInstanceValid(_profileDialog)) _profileDialog.OpenNameConflict(result);
	}

	private void RefreshAccountPresentation()
	{
		if (!GodotObject.IsInstanceValid(this)) return;

		ApplyProfileNameLabels();

		if (_profileDialog != null && GodotObject.IsInstanceValid(_profileDialog)) _profileDialog.RefreshPresentation();
	}

	/// <summary>
	///     The profile button states the name this screen plays under and, when no account owns it, that it is unverified, so the
	///     name other players will see is readable without opening the profile screen.
	/// </summary>
	private void ApplyProfileNameLabels()
	{
		if (_profileButton == null) return;

		Label nameLabel = _profileButton.GetNodeOrNull<Label>("Nameplate/Label");
		Label statusLabel = _profileButton.GetNodeOrNull<Label>("Badge/Label");
		bool signedIn = GameContext.Account.IsSignedIn;

		if (nameLabel != null)
		{
			nameLabel.Text = signedIn
				? GameContext.Account.DisplayName
				: UnverifiedNameMark.Apply(GameContext.Account.EffectiveUserName);
		}

		if (statusLabel != null)
		{
			statusLabel.Text = signedIn ? string.Empty : TranslationServer.Translate("SIGNED OUT");
		}
	}

	/// <summary>
	///     Opens the one identity screen, which shows the name, the multiplayer record and the Steam account actions together.
	/// </summary>
	private void OpenProfileDialog()
	{
		if (_profileDialog != null && GodotObject.IsInstanceValid(_profileDialog)) _profileDialog.QueueFree();

		_profileDialog = new PlayerProfileDialog { Name = "PlayerProfileDialog" };
		_profileDialog.SignedIn = () =>
		{
			RefreshAccountPresentation();
			_profileDialog?.QueueFree();
			_profileDialog = null;
		};
		_profileDialog.UserNameChanged = RefreshAccountPresentation;
		AddChild(_profileDialog);
		_profileDialog.Open();
	}

	private void BindNodes()
	{
		_bgPanel = GetNode<Panel>("Background");
		_norseFont ??= GD.Load<Font>("res://Assets/UI/Norse.otf");
		_norseBoldFont ??= GD.Load<Font>("res://Assets/UI/Norse-Bold.otf");
		_leftPillar = GetNode<Panel>("LeftPillar");
		_rightPillar = GetNode<Panel>("RightPillar");
		_centralPanel = GetNode<PanelContainer>("CentralPanel");
		_gameLogo = GetNode<TextureRect>("GameLogo");
		_gameLogo.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/Logo.webp");
		_gameLogo.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		CreateVersionSelector();
		_playButton = GetNode<Button>("CentralPanel/VBoxContainer/PlayButton");
		_singlePlayerButton = GetNodeOrNull<Button>("CentralPanel/VBoxContainer/SinglePlayerButton");
		_mapDiscoveryButton = GetNode<Button>("CentralPanel/VBoxContainer/MapDiscoveryButton");
		_creatorDiscoveryButton = GetNodeOrNull<Button>("CentralPanel/VBoxContainer/CreatorDiscoveryButton");
		_mapEditorButton = GetNode<Button>("CentralPanel/VBoxContainer/MapEditorButton");
		_replaysButton = GetNodeOrNull<Button>("CentralPanel/VBoxContainer/ReplaysButton");
		_storageButton = GetNodeOrNull<Button>("CentralPanel/VBoxContainer/StorageButton");
		_settingsButton = GetNode<Button>("SettingsButton");
		_profileButton = GetNode<Button>("ProfileButton");
		_quitButton = GetNode<Button>("QuitButton");
		_socialButton = GetNodeOrNull<Button>("SocialButton");
		_socialPopoverOverlay = GetNodeOrNull<Control>("SocialPopoverOverlay");
		_socialPopover = GetNodeOrNull<Control>("SocialPopover");
		_wishlistButton = GetNodeOrNull<Button>("SocialPopover/MarginContainer/PopoverVBox/WishlistButton") ?? GetNodeOrNull<Button>("SocialPopover/PopoverVBox/WishlistButton");
		_websiteButton = GetNodeOrNull<Button>("SocialPopover/MarginContainer/PopoverVBox/WebsiteButton") ?? GetNodeOrNull<Button>("SocialPopover/PopoverVBox/WebsiteButton");
		_discordButton = GetNodeOrNull<Button>("SocialPopover/MarginContainer/PopoverVBox/DiscordButton") ?? GetNodeOrNull<Button>("SocialPopover/PopoverVBox/DiscordButton");
		_youtubeButton = GetNodeOrNull<Button>("SocialPopover/MarginContainer/PopoverVBox/YoutubeButton") ?? GetNodeOrNull<Button>("SocialPopover/PopoverVBox/YoutubeButton");
		_donateButton = GetNodeOrNull<Button>("SocialPopover/MarginContainer/PopoverVBox/DonateButton") ?? GetNodeOrNull<Button>("SocialPopover/PopoverVBox/DonateButton");
		_announcementsButton = GetNodeOrNull<Button>("SocialPopover/MarginContainer/PopoverVBox/AnnouncementsButton") ?? GetNodeOrNull<Button>("SocialPopover/PopoverVBox/AnnouncementsButton");
		_bugReportButton = GetNodeOrNull<Button>("SocialPopover/MarginContainer/PopoverVBox/BugReportButton") ?? GetNodeOrNull<Button>("SocialPopover/PopoverVBox/BugReportButton");
	}

	private void StylePanels()
	{
		_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateEntranceBgTexture());
		_leftPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(true));
		_rightPillar.AddThemeStyleboxOverride("panel", UIStyle.CreatePillarPanel(false));
		_centralPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		GetNode<VBoxContainer>("CentralPanel/VBoxContainer").AddThemeConstantOverride("separation", 10);
	}

	private void SetupButtons()
	{
		SetupPlayButton(_playButton, OnPlayPressed);
		if (_singlePlayerButton != null)
		{
			SetupMenuButton(_singlePlayerButton, "SINGLE PLAYER", () =>
			{
				GameContext.SessionState.IsMapEditorMode = false;
				GameContext.SessionState.IsTestMode = false;
				if (GameContext.Lobby != null) GameContext.Lobby.IsSinglePlayer = true;
				NavigateTo(GameScreen.LobbyCreate);
			}, "res://Assets/UI/menu_single_player_button.webp");
			_singlePlayerButton.AddThemeConstantOverride("icon_max_width", 28);
		}

		SetupMenuButton(_mapDiscoveryButton, "MAP DISCOVERY", () => NavigateTo(GameScreen.MapDiscovery), "res://Assets/UI/menu_discovery_button.webp");
		_mapDiscoveryButton.Visible = !BuildFlags.IS_AUTOBATTLE_FESTIVAL_ACTIVE;
		if (_creatorDiscoveryButton != null)
		{
			SetupMenuButton(_creatorDiscoveryButton, "CREATOR DISCOVERY", () => NavigateTo(GameScreen.CreatorDiscovery), "res://Assets/UI/menu_creator_button.webp");
			_creatorDiscoveryButton.AddThemeConstantOverride("icon_max_width", 28);
			_creatorDiscoveryButton.Visible = !BuildFlags.IS_AUTOBATTLE_FESTIVAL_ACTIVE;
		}

		SetupMenuButton(_mapEditorButton, "MAP EDITOR", () => OnMapEditorPressed(), "res://Assets/UI/menu_editor_button.webp");
		if (_replaysButton != null)
		{
			SetupMenuButton(_replaysButton, "REPLAYS", () => NavigateTo(GameScreen.ReplayList), "res://Assets/UI/menu_replays_button.webp");
			_replaysButton.AddThemeConstantOverride("icon_max_width", 28);
		}

		if (_storageButton != null)
		{
			SetupMenuButton(_storageButton, "STORAGE", () => NavigateTo(GameScreen.Storage), "res://Assets/UI/menu_storage_button.webp");
			_storageButton.AddThemeConstantOverride("icon_max_width", 28);
		}

		SetupIconButton(_settingsButton, "OPTIONS", "res://Assets/UI/gear_icon.webp", () => GameContext.UserInterface?.OpenSettingsOverlay(), new Vector2(22, 22));
		SetupAvatarButton(_profileButton, () => OpenProfileDialog());
		SetupButton(_quitButton, "QUIT GAME", () => GetTree().Quit());

		_mapDiscoveryButton.AddThemeConstantOverride("icon_max_width", 28);
		_mapEditorButton.AddThemeConstantOverride("icon_max_width", 28);
		_playButton.AddThemeConstantOverride("icon_max_width", 28);
	}

	private void SetupSocialPopover()
	{
		if (_socialButton == null || _socialPopover == null || _socialPopoverOverlay == null) return;

		SetupIconButton(_socialButton, "", "res://Assets/UI/social_icon.webp", () => ToggleSocialPopover(), new Vector2(32, 32));
		ConfigurePopoverBackground();
		SetupSocialButtons();
		_socialPopoverOverlay.GuiInput += OnSocialPopoverOverlayGuiInput;
	}

	private void ConfigurePopoverBackground()
	{
		var popoverBg = _socialPopover.GetNodeOrNull<TextureRect>("Background");
		if (popoverBg == null) return;
		popoverBg.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/menu_popup.webp");
		popoverBg.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		popoverBg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		popoverBg.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
	}

	private void SetupSocialButtons()
	{
		SetupWishlistButton();
		SetupSocialLinkButton(_websiteButton, "WEBSITE", "https://www.realm-game.com/", "res://Assets/UI/globe_icon.webp");
		SetupSocialLinkButton(_discordButton, "DISCORD", "http://discord.realm-game.com", "res://Assets/UI/discord_icon.webp");
		SetupSocialLinkButton(_youtubeButton, "YOUTUBE", "https://www.youtube.com/@Realm-Game", "res://Assets/UI/youtube_icon.webp");
		SetupSocialLinkButton(_donateButton, "DONATE", "http://realm-game.com/#donate", "res://Assets/UI/donate_icon.webp");
		SetupSocialLinkButton(_bugReportButton, "BUG REPORT", "https://github.com/RealmArcade/Realm/issues", "res://Assets/UI/bug_icon.webp");
		SetupAnnouncementsButton();
	}

	private void SetupWishlistButton()
	{
		if (_wishlistButton == null) return;

		if (!GameContext.Steam.CanOpenStorePage)
		{
			_wishlistButton.Visible = false;
			return;
		}

		SetupMenuButton(_wishlistButton, "WISHLIST ON STEAM", () =>
		{
			GameContext.Steam.OpenFullGameStorePage();
			HideSocialPopover();
		}, "res://Assets/UI/options_menu_button.webp", null, "res://Assets/UI/steam_icon.webp");
	}

	private void SetupAnnouncementsButton()
	{
		if (_announcementsButton == null) return;
		SetupMenuButton(_announcementsButton, "ANNOUNCEMENTS", () =>
		{
			HideSocialPopover();
			OpenAnnouncementsDialog();
		}, "res://Assets/UI/options_menu_button.webp", null, "res://Assets/UI/email_icon.webp");
	}

	private void OpenAnnouncementsDialog()
	{
		if (_announcementsDialog != null && GodotObject.IsInstanceValid(_announcementsDialog))
		{
			_announcementsDialog.QueueFree();
		}

		_announcementsDialog = new AnnouncementsDialog { Name = "AnnouncementsDialog" };
		AddChild(_announcementsDialog);
		_announcementsDialog.Open();
	}

	private void SetupSocialLinkButton(Button button, string text, string url, string iconPath)
	{
		if (button == null) return;
		SetupMenuButton(button, text, () =>
		{
			OS.ShellOpen(url);
			HideSocialPopover();
		}, "res://Assets/UI/options_menu_button.webp", null, iconPath);
	}

	private void OnSocialPopoverOverlayGuiInput(InputEvent @event)
	{
		if (!(@event is InputEventMouseButton mouseEvent)) return;
		if (!mouseEvent.Pressed) return;
		if (mouseEvent.ButtonIndex != MouseButton.Left) return;
		HideSocialPopover();
	}

	private void ToggleSocialPopover()
	{
		if (_socialPopover == null || _socialPopoverOverlay == null) return;
		bool isVisible = !_socialPopover.Visible;
		_socialPopover.Visible = isVisible;
		_socialPopoverOverlay.Visible = isVisible;
	}

	private void HideSocialPopover()
	{
		if (_socialPopover != null) _socialPopover.Visible = false;
		if (_socialPopoverOverlay != null) _socialPopoverOverlay.Visible = false;
	}

	private void CreateVersionSelector()
	{
		var versionBox = new HBoxContainer();
		versionBox.Name = "VersionBox_HBoxContainer";
		versionBox.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
		versionBox.Position = new Vector2(110, 10);
		versionBox.AddThemeConstantOverride("separation", 10);
		versionBox.Visible = !BuildFlags.IS_AUTOBATTLE_FESTIVAL_ACTIVE;
		AddChild(versionBox);

		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = TranslationServer.Translate("Version: ");
		lbl.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
		lbl.AddThemeFontOverride("font", _norseFont);
		lbl.AddThemeFontSizeOverride("font_size", 16);
		lbl.VerticalAlignment = VerticalAlignment.Center;
		versionBox.AddChild(lbl);

		_versionDropdown = new OptionButton();
		_versionDropdown.Name = "VersionDropdown_OptionButton";
		_versionDropdown.CustomMinimumSize = new Vector2(160, 32);
		_versionDropdown.AddThemeFontOverride("font", _norseFont);
		_versionDropdown.AddThemeFontSizeOverride("font_size", 14);
		_versionDropdown.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
		_versionDropdown.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		versionBox.AddChild(_versionDropdown);

		_outdatedLabel = new Label();
		_outdatedLabel.Name = "OutdatedLabel_Label";
		_outdatedLabel.Text = TranslationServer.Translate("Outdated");
		_outdatedLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.35f, 0.35f));
		_outdatedLabel.AddThemeFontOverride("font", _norseFont);
		_outdatedLabel.AddThemeFontSizeOverride("font_size", 14);
		_outdatedLabel.VerticalAlignment = VerticalAlignment.Center;
		_outdatedLabel.Visible = false;
		versionBox.AddChild(_outdatedLabel);

		var downloadBtn = new Button();
		downloadBtn.Name = "DownloadBtn_Button";
		downloadBtn.Text = TranslationServer.Translate("DOWNLOAD...");
		downloadBtn.AddThemeFontOverride("font", _norseBoldFont);
		downloadBtn.AddThemeFontSizeOverride("font_size", 12);
		downloadBtn.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
		downloadBtn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		downloadBtn.CustomMinimumSize = new Vector2(110, 32);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(downloadBtn);
		downloadBtn.Pressed += () => OS.ShellOpen("https://github.com/RealmArcade/Realm/releases");
		versionBox.AddChild(downloadBtn);

		PopulateVersionDropdown();
		CheckForUpdatesAsync();
	}

	private async void CheckForUpdatesAsync()
	{
		if (await GameUpdateService.IsNewerReleasePublishedAsync())
		{
			_outdatedLabel.Visible = true;
		}
	}

	private void PopulateVersionDropdown()
	{
		_versionDropdown.Clear();
		_versionDropdown.AddItem(RealmVersion.GameBinaryVersion);
		_versionDropdown.SetItemMetadata(0, "current");

		if (!OS.HasFeature("editor"))
		{
			foreach (string version in GameUpdateService.GetOtherInstalledVersions(LobbyManager.GetBaseGameDirectory()))
			{
				_versionDropdown.AddItem(version);
				_versionDropdown.SetItemMetadata(_versionDropdown.ItemCount - 1, version);
			}
		}

		_versionDropdown.ItemSelected += OnVersionSelected;
	}

	private void OnVersionSelected(long index)
	{
		string meta = _versionDropdown.GetItemMetadata((int)index).AsString();
		if (meta == "current" || string.IsNullOrEmpty(meta)) return;

		string targetExe = LobbyManager.GetVersionExecutablePath(meta);

		if (File.Exists(targetExe))
		{
			OS.CreateProcess(targetExe, new string[] { });
			GetTree().Quit();
		}
	}

	/// <summary>
	///     Play opens the lobby browser without an account, because browsing lobbies and joining multiplayer matches need no
	///     online identity; a signed-out player plays under the unverified name shown on the profile button. Only publishing a
	///     map and submitting a verified review at the end of a match ask for a sign-in.
	/// </summary>
	private void OnPlayPressed()
	{
		GameContext.SessionState.IsMapEditorMode = false;
		GameContext.SessionState.IsTestMode = false;
		if (GameContext.Lobby is { } lobby) lobby.IsSinglePlayer = false;

		NavigateTo(GameScreen.LobbyBrowser);
	}

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}

	/// <summary>
	///     Switches screens, reporting instead of throwing when no UI manager is registered: during teardown this menu can
	///     outlive the manager, and then a click genuinely has nowhere to go.
	/// </summary>
	private static void NavigateTo(GameScreen screen)
	{
		if (GameContext.UserInterface is { } userInterface)
		{
			userInterface.TransitionTo(screen);
			return;
		}

		GameLog.LogError($"[MainMenu] Cannot open {screen}: no UI manager is registered.");
	}

	private void PlayHoverSound()
	{
		GameContext.UserInterface?.PlayHoverSound();
	}

	private void PlayClickSound()
	{
		GameContext.UserInterface?.PlayClickSound();
	}

	private void OnMapEditorPressed()
	{
		if (OperatingSystem.IsWindows()) GameContext.VSCode.StartInstallIfNeeded();

		NavigateTo(GameScreen.MapEditorHUD);
	}

}
