using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Network;
using HttpClient = System.Net.Http.HttpClient;
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

	private Panel _bgPanel;
	private Panel _leftPillar;
	private Panel _rightPillar;
	private PanelContainer _centralPanel;

	private TextureRect _gameLogo;
	private Button _playButton;
	private Button _singlePlayerButton;
	private Button _mapDiscoveryButton;
	private Button _creatorDiscoveryButton;
	private Button _mapEditorButton;
	private Button _replaysButton;
	private Button _storageButton;
	private Button _settingsButton;
	private Button _profileButton;
	private Button _quitButton;
	private Button _socialButton;
	private Control _socialPopoverOverlay;
	private Control _socialPopover;
	private Button _wishlistButton;
	private Button _websiteButton;
	private Button _discordButton;
	private Button _youtubeButton;
	private Button _donateButton;
	private Button _announcementsButton;
	private Button _bugReportButton;
	private PlayerProfileDialog _profileDialog;
	private AnnouncementsDialog _announcementsDialog;
	private OptionButton _versionDropdown;
	private Label _outdatedLabel;

	private static Font _norseFont;
	private static Font _norseBoldFont;


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
				GameContext.UserInterface.TransitionTo(GameScreen.LobbyCreate);
			}, "res://Assets/UI/menu_single_player_button.webp");
			_singlePlayerButton.AddThemeConstantOverride("icon_max_width", 28);
		}

		SetupMenuButton(_mapDiscoveryButton, "MAP DISCOVERY", () => GameContext.UserInterface.TransitionTo(GameScreen.MapDiscovery), "res://Assets/UI/menu_discovery_button.webp");
		_mapDiscoveryButton.Visible = !BuildFlags.IS_AUTOBATTLE_FESTIVAL_ACTIVE;
		if (_creatorDiscoveryButton != null)
		{
			SetupMenuButton(_creatorDiscoveryButton, "CREATOR DISCOVERY", () => GameContext.UserInterface.TransitionTo(GameScreen.CreatorDiscovery), "res://Assets/UI/menu_creator_button.webp");
			_creatorDiscoveryButton.AddThemeConstantOverride("icon_max_width", 28);
			_creatorDiscoveryButton.Visible = !BuildFlags.IS_AUTOBATTLE_FESTIVAL_ACTIVE;
		}

		SetupMenuButton(_mapEditorButton, "MAP EDITOR", () => OnMapEditorPressed(), "res://Assets/UI/menu_editor_button.webp");
		if (_replaysButton != null)
		{
			SetupMenuButton(_replaysButton, "REPLAYS", () => GameContext.UserInterface.TransitionTo(GameScreen.ReplayList), "res://Assets/UI/menu_replays_button.webp");
			_replaysButton.AddThemeConstantOverride("icon_max_width", 28);
		}

		if (_storageButton != null)
		{
			SetupMenuButton(_storageButton, "STORAGE", () => GameContext.UserInterface.TransitionTo(GameScreen.Storage), "res://Assets/UI/menu_storage_button.webp");
			_storageButton.AddThemeConstantOverride("icon_max_width", 28);
		}

		SetupIconButton(_settingsButton, "OPTIONS", "res://Assets/UI/gear_icon.webp", () => GameContext.UserInterface.OpenSettingsOverlay(), new Vector2(22, 22));
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

	private void SetupButton(Button button, string text, Action onClick)
	{
		button.Flat = false;
		button.Text = TranslationServer.Translate(text);
		button.AddThemeFontOverride("font", _norseBoldFont);
		button.AddThemeFontSizeOverride("font_size", 18);

		button.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
		UIStyle.ApplyGoldHoverAndFocusColors(button);

		button.AddThemeColorOverride("font_shadow_color", Color.FromHtml("#8C8171"));
		button.AddThemeConstantOverride("shadow_offset_x", 0);
		button.AddThemeConstantOverride("shadow_offset_y", 2);
		button.AddThemeConstantOverride("icon_max_width", 32);
		button.AddThemeConstantOverride("h_separation", 2);

		UIStyle.ApplyButtonStyleBoxesClearingFocus(button);

		ApplyButtonAnimations(button, 1.03f);

		button.Pressed += () =>
		{
			PlayClickSound();
			onClick?.Invoke();
		};
	}

	private void SetupMenuButton(Button button, string text, Action onClick, string texturePath = null, Vector2? minSize = null, string iconPath = null)
	{
		button.Flat = false;
		button.Text = TranslationServer.Translate(text);
		button.CustomMinimumSize = minSize ?? (texturePath != null ? new Vector2(240, 59) : new Vector2(240, 40));

		ApplyMenuButtonIcon(button, iconPath);
		ApplyMenuButtonTextStyles(button);
		ApplyMenuButtonStyleBoxes(button, texturePath);

		button.Pressed += () =>
		{
			PlayClickSound();
			onClick?.Invoke();
		};

		button.MouseEntered += () => PlayHoverSound();
	}

	private void ApplyMenuButtonIcon(Button button, string iconPath)
	{
		if (!string.IsNullOrEmpty(iconPath))
		{
			button.Icon = TrimmedTextureLoader.LoadTrimmedTexture(iconPath);
		}

		if (button.Icon == null) return;

		button.ExpandIcon = true;
		button.IconAlignment = HorizontalAlignment.Left;
		button.VerticalIconAlignment = VerticalAlignment.Center;
		button.AddThemeConstantOverride("icon_max_width", 24);
		button.AddThemeConstantOverride("h_separation", 10);
	}

	private void ApplyMenuButtonTextStyles(Button button)
	{
		button.AddThemeFontOverride("font", _norseBoldFont);
		button.AddThemeFontSizeOverride("font_size", 20);

		button.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
		UIStyle.ApplyGoldHoverAndFocusColors(button);

		button.AddThemeColorOverride("font_outline_color", new Color(0.10f, 0.07f, 0.02f, 1.0f));
		button.AddThemeConstantOverride("outline_size", 8);

		button.AddThemeColorOverride("font_shadow_color", Color.FromHtml("#8C8171"));
		button.AddThemeConstantOverride("shadow_offset_x", 0);
		button.AddThemeConstantOverride("shadow_offset_y", 2);
	}

	private void ApplyMenuButtonStyleBoxes(Button button, string texturePath)
	{
		var normalStyle = CreateMenuButtonStyleBox(texturePath ?? "res://Assets/UI/menu_button_normal.webp", texturePath, null);
		var hoverStyle = CreateMenuButtonStyleBox(texturePath ?? "res://Assets/UI/menu_button_hover.webp", texturePath, new Color(1.12f, 1.10f, 0.96f));
		var pressedStyle = CreateMenuButtonStyleBox(texturePath ?? "res://Assets/UI/menu_button_pressed.webp", texturePath, new Color(0.85f, 0.82f, 0.75f));

		button.AddThemeStyleboxOverride("normal", normalStyle);
		button.AddThemeStyleboxOverride("hover", hoverStyle);
		button.AddThemeStyleboxOverride("pressed", pressedStyle);
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	private StyleBoxTexture CreateMenuButtonStyleBox(string texPath, string customPath, Color? modColor)
	{
		var style = new StyleBoxTexture();
		style.Texture = TrimmedTextureLoader.LoadTrimmedTexture(texPath);
		if (customPath != null && modColor.HasValue) style.ModulateColor = modColor.Value;

		if (customPath != null)
		{
			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;
			style.ContentMarginLeft = 24;
			style.ContentMarginRight = 24;
			style.ContentMarginTop = 6;
			style.ContentMarginBottom = 6;
		}
		else
		{
			style.TextureMarginLeft = 32;
			style.TextureMarginRight = 32;
			style.TextureMarginTop = 10;
			style.TextureMarginBottom = 10;
			style.ContentMarginLeft = 32;
			style.ContentMarginRight = 32;
			style.ContentMarginTop = 10;
			style.ContentMarginBottom = 10;
		}

		return style;
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
		try
		{
			using var client = new HttpClient();
			client.DefaultRequestHeaders.UserAgent.ParseAdd("Realm-Godot-Client");
			var response = await client.GetAsync("https://api.github.com/repos/speige/Realm/releases/latest");
			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				using var doc = JsonDocument.Parse(json);
				if (doc.RootElement.TryGetProperty("tag_name", out var tagProp))
				{
					string latestTag = tagProp.GetString() ?? "";
					if (latestTag.StartsWith("v")) latestTag = latestTag.Substring(1);

					if (latestTag != RealmVersion.GameBinaryVersion)
					{
						_outdatedLabel.Visible = true;
					}
				}
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MainMenu] Failed to check for updates: {ex.Message}");
		}
	}

	private void PopulateVersionDropdown()
	{
		_versionDropdown.Clear();
		_versionDropdown.AddItem(RealmVersion.GameBinaryVersion);
		_versionDropdown.SetItemMetadata(0, "current");

		if (!OS.HasFeature("editor"))
		{
			string baseDir = LobbyManager.GetBaseGameDirectory();
			string versionsDir = Path.Combine(baseDir, "versions");
			if (Directory.Exists(versionsDir))
			{
				var dirs = Directory.GetDirectories(versionsDir);
				foreach (var dir in dirs)
				{
					string ver = Path.GetFileName(dir);
					if (ver != RealmVersion.GameBinaryVersion)
					{
						_versionDropdown.AddItem(ver);
						_versionDropdown.SetItemMetadata(_versionDropdown.ItemCount - 1, ver);
					}
				}
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
		GameContext.Lobby.IsSinglePlayer = false;

		GameContext.UserInterface.TransitionTo(GameScreen.LobbyBrowser);
	}

	private void SetupPlayButton(Button button, Action onClick)
	{
		button.Flat = false;
		button.Icon = null;
		button.CustomMinimumSize = new Vector2(240, 56);
		button.Text = TranslationServer.Translate("PLAY");

		button.AddThemeFontOverride("font", _norseBoldFont);
		button.AddThemeFontSizeOverride("font_size", 20);

		button.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
		UIStyle.ApplyGoldHoverAndFocusColors(button);

		button.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.08f, 0.03f, 1.0f));
		button.AddThemeConstantOverride("outline_size", 8);

		button.AddThemeColorOverride("font_shadow_color", Color.FromHtml("#8C8171"));
		button.AddThemeConstantOverride("shadow_offset_x", 0);
		button.AddThemeConstantOverride("shadow_offset_y", 2);

		var playNormalStyle = new StyleBoxTexture();
		playNormalStyle.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/menu_play_button.webp");
		playNormalStyle.TextureMarginLeft = 0;
		playNormalStyle.TextureMarginRight = 0;
		playNormalStyle.TextureMarginTop = 0;
		playNormalStyle.TextureMarginBottom = 0;
		playNormalStyle.ContentMarginLeft = 24;
		playNormalStyle.ContentMarginRight = 24;
		playNormalStyle.ContentMarginTop = 6;
		playNormalStyle.ContentMarginBottom = 6;

		var playHoverStyle = new StyleBoxTexture();
		playHoverStyle.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/menu_play_button.webp");
		playHoverStyle.ModulateColor = new Color(1.12f, 1.10f, 0.96f);
		playHoverStyle.TextureMarginLeft = 0;
		playHoverStyle.TextureMarginRight = 0;
		playHoverStyle.TextureMarginTop = 0;
		playHoverStyle.TextureMarginBottom = 0;
		playHoverStyle.ContentMarginLeft = 24;
		playHoverStyle.ContentMarginRight = 24;
		playHoverStyle.ContentMarginTop = 6;
		playHoverStyle.ContentMarginBottom = 6;

		var playPressedStyle = new StyleBoxTexture();
		playPressedStyle.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/menu_play_button.webp");
		playPressedStyle.ModulateColor = new Color(0.85f, 0.82f, 0.75f);
		playPressedStyle.TextureMarginLeft = 0;
		playPressedStyle.TextureMarginRight = 0;
		playPressedStyle.TextureMarginTop = 0;
		playPressedStyle.TextureMarginBottom = 0;
		playPressedStyle.ContentMarginLeft = 24;
		playPressedStyle.ContentMarginRight = 24;
		playPressedStyle.ContentMarginTop = 6;
		playPressedStyle.ContentMarginBottom = 6;

		button.AddThemeStyleboxOverride("normal", playNormalStyle);
		button.AddThemeStyleboxOverride("hover", playHoverStyle);
		button.AddThemeStyleboxOverride("pressed", playPressedStyle);
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		ApplyButtonAnimations(button, 1.04f);

		button.Pressed += () =>
		{
			PlayClickSound();
			onClick?.Invoke();
		};
	}

	private void SetupAvatarButton(Button button, Action onClick)
	{
		button.Flat = true;
		button.Text = "";
		button.FocusMode = FocusModeEnum.None;

		button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		var frameNormal = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/profile_frame_normal.webp");
		var nameplateNormal = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/profile_nameplate_normal.webp");
		var backdropNormal = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/profile_backdrop_normal.webp");

		var backdrop = button.GetNode<TextureRect>("Backdrop");
		var avatar = button.GetNode<TextureRect>("Avatar");
		var frame = button.GetNode<TextureRect>("Frame");
		var nameplate = button.GetNode<TextureRect>("Nameplate");
		var nameLabel = button.GetNode<Label>("Nameplate/Label");
		var levelLabel = button.GetNode<Label>("Badge/Label");

		backdrop.Texture = backdropNormal;
		backdrop.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		backdrop.StretchMode = TextureRect.StretchModeEnum.Scale;
		backdrop.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		frame.Texture = frameNormal;
		frame.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		frame.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		frame.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		nameplate.Texture = nameplateNormal;
		nameplate.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		nameplate.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		nameplate.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		avatar.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/avatar_icon.webp");
		avatar.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		avatar.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		avatar.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		ApplyProfileNameLabels();

		var labelFont = new SystemFont();
		labelFont.FontNames = new string[] { "Cinzel Bold", "Cinzel", "Palatino Linotype", "Georgia", "serif" };
		labelFont.FontWeight = 700;

		nameLabel.AddThemeFontOverride("font", labelFont);
		nameLabel.AddThemeFontSizeOverride("font_size", 14);
		nameLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.65f));
		nameLabel.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.05f, 0.05f, 1.0f));
		nameLabel.AddThemeConstantOverride("outline_size", 6);

		levelLabel.AddThemeFontOverride("font", labelFont);
		levelLabel.AddThemeFontSizeOverride("font_size", 12);
		levelLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.55f));
		levelLabel.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.05f, 0.05f, 1.0f));
		levelLabel.AddThemeConstantOverride("outline_size", 5);

		Tween scaleTween = null;
		button.MouseEntered += () =>
		{
			PlayHoverSound();
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			scaleTween.SetParallel(true);
			button.PivotOffset = button.Size / 2;
			scaleTween.TweenProperty(button, "scale", new Vector2(1.04f, 1.04f), 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			scaleTween.TweenProperty(button, "modulate", new Color(1.10f, 1.08f, 0.98f), 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};

		button.MouseExited += () =>
		{
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			scaleTween.SetParallel(true);
			scaleTween.TweenProperty(button, "scale", Vector2.One, 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			scaleTween.TweenProperty(button, "modulate", Colors.White, 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};

		button.Pressed += () =>
		{
			PlayClickSound();
			onClick?.Invoke();
		};
	}

	private void SetupIconButton(Button button, string text, string iconPath, Action onClick, Vector2? iconSize = null)
	{
		button.Flat = false;
		button.Icon = null;
		button.Text = "";

		UIStyle.ApplyButtonStyleBoxesClearingFocus(button);

		foreach (Node child in button.GetChildren())
		{
			if (child.Name == "ButtonContent")
			{
				child.QueueFree();
			}
		}

		var container = CreateIconButtonContainer();

		var iconRect = CreateIconButtonIcon(iconPath, iconSize);
		container.AddChild(iconRect);

		var label = CreateIconButtonLabel(text);
		container.AddChild(label);

		button.AddChild(container);

		BindIconButtonEvents(button, label, iconRect, onClick);
	}

	private HBoxContainer CreateIconButtonContainer()
	{
		var container = new HBoxContainer();
		container.Name = "ButtonContent";
		container.AnchorLeft = 0.0f;
		container.AnchorRight = 1.0f;
		container.AnchorTop = 0.0f;
		container.AnchorBottom = 1.0f;
		container.Alignment = BoxContainer.AlignmentMode.Center;
		container.MouseFilter = MouseFilterEnum.Ignore;
		container.AddThemeConstantOverride("separation", 6);
		return container;
	}

	private TextureRect CreateIconButtonIcon(string iconPath, Vector2? iconSize)
	{
		var iconRect = new TextureRect();
		iconRect.Name = "IconRect_TextureRect";
		iconRect.Texture = TrimmedTextureLoader.LoadTrimmedTexture(iconPath);
		iconRect.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		Vector2 size = iconSize ?? new Vector2(22, 22);
		iconRect.CustomMinimumSize = size;
		iconRect.Size = size;
		iconRect.PivotOffset = size / 2.0f;
		iconRect.MouseFilter = MouseFilterEnum.Ignore;
		iconRect.Modulate = UIStyle.ColorGoldDull;
		return iconRect;
	}

	private Label CreateIconButtonLabel(string text)
	{
		var label = new Label();
		label.Name = "Label_Label";
		label.Text = TranslationServer.Translate(text);
		label.AddThemeFontOverride("font", _norseBoldFont);
		label.AddThemeFontSizeOverride("font_size", 18);
		label.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
		label.AddThemeColorOverride("font_shadow_color", Color.FromHtml("#8C8171"));
		label.AddThemeConstantOverride("shadow_offset_x", 0);
		label.AddThemeConstantOverride("shadow_offset_y", 2);
		label.MouseFilter = MouseFilterEnum.Ignore;
		return label;
	}

	private void BindIconButtonEvents(Button button, Label label, TextureRect iconRect, Action onClick)
	{
				ApplyButtonAnimations(button, 1.03f);

		button.MouseEntered += () =>
		{
			label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			iconRect.Modulate = UIStyle.ColorGold;
		};

		button.MouseExited += () =>
		{
			label.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
			iconRect.Modulate = UIStyle.ColorGoldDull;
		};

		button.ButtonDown += () =>
		{
			label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			iconRect.Modulate = UIStyle.ColorCyanGlow;
		};

		button.ButtonUp += () =>
		{
			label.AddThemeColorOverride("font_color", button.IsHovered() ? UIStyle.ColorGold : Color.FromHtml("#D1C4AE"));
			iconRect.Modulate = button.IsHovered() ? UIStyle.ColorGold : UIStyle.ColorGoldDull;
		};

		button.Pressed += () =>
		{
			PlayClickSound();
			onClick?.Invoke();
		};
	}

	private void ApplyButtonAnimations(Button button, float hoverScale)
	{
		Tween scaleTween = null;
		button.MouseEntered += () =>
		{
			PlayHoverSound();
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			button.PivotOffset = button.Size / 2;
			scaleTween.TweenProperty(button, "scale", new Vector2(hoverScale, hoverScale), 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};
		button.MouseExited += () =>
		{
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			scaleTween.TweenProperty(button, "scale", Vector2.One, 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};
	}

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}

	private void PlayHoverSound()
	{
		GameContext.UserInterface.PlayHoverSound();
	}

	private void PlayClickSound()
	{
		GameContext.UserInterface.PlayClickSound();
	}

	private void OnMapEditorPressed()
	{
		if (OperatingSystem.IsWindows()) GameContext.VSCode.StartInstallIfNeeded();

		GameContext.UserInterface.TransitionTo(GameScreen.MapEditorHUD);
	}

}
