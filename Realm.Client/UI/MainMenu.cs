using System;
using System.IO;
using System.Text.Json;
using Godot;
using Realm.Client.Network;
using Realm.Shared;
using HttpClient = System.Net.Http.HttpClient;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI;

[GlobalClass]
public partial class MainMenu : Control
{
	public MainMenu()
	{
	}

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
	private Button _websiteButton;
	private Button _discordButton;
	private Button _youtubeButton;
	private Button _donateButton;
	private Button _announcementsButton;
	private Button _bugReportButton;
	private Control _profilePopup;
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
		SetupPlayButton(_playButton, () =>
		{
			GameContext.SessionState.IsMapEditorMode = false;
			GameContext.SessionState.IsTestMode = false;
			LobbyManager.Instance.IsSinglePlayer = false;
			UIManager.Instance.TransitionTo(GameScreen.LobbyBrowser);
		});
		if (_singlePlayerButton != null)
		{
			SetupMenuButton(_singlePlayerButton, "SINGLE PLAYER", () =>
			{
				GameContext.SessionState.IsMapEditorMode = false;
				GameContext.SessionState.IsTestMode = false;
				if (LobbyManager.Instance != null) LobbyManager.Instance.IsSinglePlayer = true;
				UIManager.Instance.TransitionTo(GameScreen.LobbyCreate);
			}, "res://Assets/UI/menu_single_player_button.webp");
			_singlePlayerButton.AddThemeConstantOverride("icon_max_width", 28);
		}

		SetupMenuButton(_mapDiscoveryButton, "MAP DISCOVERY", () => UIManager.Instance.TransitionTo(GameScreen.MapDiscovery), "res://Assets/UI/menu_discovery_button.webp");
		if (_creatorDiscoveryButton != null)
		{
			SetupMenuButton(_creatorDiscoveryButton, "CREATOR DISCOVERY", () => UIManager.Instance.TransitionTo(GameScreen.CreatorDiscovery), "res://Assets/UI/menu_creator_button.webp");
			_creatorDiscoveryButton.AddThemeConstantOverride("icon_max_width", 28);
		}

		SetupMenuButton(_mapEditorButton, "MAP EDITOR", () => OnMapEditorPressed(), "res://Assets/UI/menu_editor_button.webp");
		if (_replaysButton != null)
		{
			SetupMenuButton(_replaysButton, "REPLAYS", () => UIManager.Instance.TransitionTo(GameScreen.ReplayList), "res://Assets/UI/menu_replays_button.webp");
			_replaysButton.AddThemeConstantOverride("icon_max_width", 28);
		}

		if (_storageButton != null)
		{
			SetupMenuButton(_storageButton, "STORAGE", () => UIManager.Instance.TransitionTo(GameScreen.Storage), "res://Assets/UI/menu_storage_button.webp");
			_storageButton.AddThemeConstantOverride("icon_max_width", 28);
		}

		SetupIconButton(_settingsButton, "OPTIONS", "res://Assets/UI/gear_icon.webp", () => UIManager.Instance.OpenSettingsOverlay(), new Vector2(22, 22), true);
		SetupAvatarButton(_profileButton, () => ShowProfilePopup());
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
		SetupSocialLinkButton(_websiteButton, "WEBSITE", "https://www.realm-game.com/", "res://Assets/UI/globe_icon.webp");
		SetupSocialLinkButton(_discordButton, "DISCORD", "http://discord.realm-game.com", "res://Assets/UI/discord_icon.webp");
		SetupSocialLinkButton(_youtubeButton, "YOUTUBE", "https://www.youtube.com/@Realm-Game", "res://Assets/UI/youtube_icon.webp");
		SetupSocialLinkButton(_donateButton, "DONATE", "http://realm-game.com/#donate", "res://Assets/UI/donate_icon.webp");
		SetupSocialLinkButton(_announcementsButton, "ANNOUNCEMENTS", "https://realm-game.com/#subscribe", "res://Assets/UI/email_icon.webp");
		SetupSocialLinkButton(_bugReportButton, "BUG REPORT", "https://github.com/RealmArcade/Realm/issues", "res://Assets/UI/bug_icon.webp");
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
		button.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		button.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
		button.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);

		button.AddThemeColorOverride("font_shadow_color", Color.FromHtml("#8C8171"));
		button.AddThemeConstantOverride("shadow_offset_x", 0);
		button.AddThemeConstantOverride("shadow_offset_y", 2);
		button.AddThemeConstantOverride("icon_max_width", 32);
		button.AddThemeConstantOverride("h_separation", 2);

		button.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		button.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		button.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		Tween scaleTween = null;
		button.MouseEntered += () =>
		{
			PlayHoverSound();
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			button.PivotOffset = button.Size / 2;
			scaleTween.TweenProperty(button, "scale", new Vector2(1.03f, 1.03f), 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};
		button.MouseExited += () =>
		{
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			scaleTween.TweenProperty(button, "scale", Vector2.One, 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};

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
		button.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		button.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
		button.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);

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
		AddChild(versionBox);

		var lbl = new Label();
		lbl.Name = "Lbl_Label";
		lbl.Text = "Version: ";
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
		_outdatedLabel.Text = "Outdated";
		_outdatedLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.35f, 0.35f));
		_outdatedLabel.AddThemeFontOverride("font", _norseFont);
		_outdatedLabel.AddThemeFontSizeOverride("font_size", 14);
		_outdatedLabel.VerticalAlignment = VerticalAlignment.Center;
		_outdatedLabel.Visible = false;
		versionBox.AddChild(_outdatedLabel);

		var downloadBtn = new Button();
		downloadBtn.Name = "DownloadBtn_Button";
		downloadBtn.Text = "DOWNLOAD...";
		downloadBtn.AddThemeFontOverride("font", _norseBoldFont);
		downloadBtn.AddThemeFontSizeOverride("font_size", 12);
		downloadBtn.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
		downloadBtn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		downloadBtn.CustomMinimumSize = new Vector2(110, 32);
		downloadBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		downloadBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		downloadBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		downloadBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
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
			GD.PrintErr($"[MainMenu] Failed to check for updates: {ex.Message}");
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

	private void SetupPlayButton(Button button, Action onClick)
	{
		button.Flat = false;
		button.Icon = null;
		button.CustomMinimumSize = new Vector2(240, 56);
		button.Text = TranslationServer.Translate("PLAY");

		button.AddThemeFontOverride("font", _norseBoldFont);
		button.AddThemeFontSizeOverride("font_size", 20);

		button.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
		button.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		button.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
		button.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);

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

		// Scale-up on hover for premium feel
		Tween scaleTween = null;
		button.MouseEntered += () =>
		{
			PlayHoverSound();
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			button.PivotOffset = button.Size / 2;
			scaleTween.TweenProperty(button, "scale", new Vector2(1.04f, 1.04f), 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};
		button.MouseExited += () =>
		{
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			scaleTween.TweenProperty(button, "scale", Vector2.One, 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};

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

		// Load custom textures
		var frameNormal = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/profile_frame_normal.webp");
		var nameplateNormal = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/profile_nameplate_normal.webp");
		var backdropNormal = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/profile_backdrop_normal.webp");

		// Fetch nodes
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

		// Set up dynamic player data
		nameLabel.Text = "Horald_Topa";
		levelLabel.Text = "LVL 50";

		// Setup custom fonts & styles for labels
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

	private void SetupIconButton(Button button, string text, string iconPath, Action onClick, Vector2? iconSize = null, bool rotateOnHover = false)
	{
		button.Flat = false;
		button.Icon = null;
		button.Text = "";

		button.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		button.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		button.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

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

		BindIconButtonEvents(button, label, iconRect, rotateOnHover, onClick);
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

	private void BindIconButtonEvents(Button button, Label label, TextureRect iconRect, bool rotateOnHover, Action onClick)
	{
		Tween scaleTween = null;
		Tween rotationTween = null;

		button.MouseEntered += () =>
		{
			PlayHoverSound();
			label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			iconRect.Modulate = UIStyle.ColorGold;

			if (rotateOnHover)
			{
				rotationTween?.Kill();
				rotationTween = button.CreateTween();
				rotationTween.TweenProperty(iconRect, "rotation", Mathf.Pi * 0.25f, 0.3f)
					.SetTrans(Tween.TransitionType.Quad)
					.SetEase(Tween.EaseType.Out);
			}

			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			button.PivotOffset = button.Size / 2;
			scaleTween.TweenProperty(button, "scale", new Vector2(1.03f, 1.03f), 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};

		button.MouseExited += () =>
		{
			label.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
			iconRect.Modulate = UIStyle.ColorGoldDull;

			if (rotateOnHover)
			{
				rotationTween?.Kill();
				rotationTween = button.CreateTween();
				rotationTween.TweenProperty(iconRect, "rotation", 0.0f, 0.3f)
					.SetTrans(Tween.TransitionType.Quad)
					.SetEase(Tween.EaseType.Out);
			}

			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			scaleTween.TweenProperty(button, "scale", Vector2.One, 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
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

	private void PopulateRunicPillar(VBoxContainer container)
	{
		container.Visible = false;
	}

	private void PlayHoverSound()
	{
		UIManager.Instance.PlayHoverSound();
	}

	private void PlayClickSound()
	{
		UIManager.Instance.PlayClickSound();
	}

	private void ShowProfilePopup()
	{
		if (_profilePopup != null)
		{
			_profilePopup.QueueFree();
		}

		_profilePopup = CreateProfilePopupBase();
		AddChild(_profilePopup);

		var cardPanel = CreateProfileCardPanel();
		_profilePopup.AddChild(cardPanel);

		var marginContainer = CreateProfileMarginContainer();
		cardPanel.AddChild(marginContainer);

		var vbox = CreateProfileVBox();
		marginContainer.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });
		vbox.AddChild(CreateProfileTitle());
		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });
		vbox.AddChild(CreateProfileFactionFlag());
		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });

		CreateProfileDataRows(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
		vbox.AddChild(CreateProfileBackButton());
	}

	private Panel CreateProfilePopupBase()
	{
		var popup = new Panel();
		popup.Name = "Popup_Panel";
		popup.AnchorLeft = 0.0f;
		popup.AnchorRight = 1.0f;
		popup.AnchorTop = 0.0f;
		popup.AnchorBottom = 1.0f;
		popup.OffsetLeft = 0.0f;
		popup.OffsetRight = 0.0f;
		popup.OffsetTop = 0.0f;
		popup.OffsetBottom = 0.0f;
		popup.GrowHorizontal = GrowDirection.Both;
		popup.GrowVertical = GrowDirection.Both;

		Texture2D profileBgTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/player_profile_bg.webp");

		if (profileBgTexture != null)
		{
			var bgStyle = new StyleBoxTexture();
			bgStyle.Texture = profileBgTexture;
			popup.AddThemeStyleboxOverride("panel", bgStyle);
			popup.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		}
		else
		{
			popup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		}

		return popup;
	}

	private Panel CreateProfileCardPanel()
	{
		Texture2D cardTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/player_profile_card_bg.webp");

		var cardStyle = new StyleBoxTexture();
		if (cardTexture != null)
		{
			cardStyle.Texture = cardTexture;
			cardStyle.TextureMarginLeft = 40;
			cardStyle.TextureMarginRight = 40;
			cardStyle.TextureMarginTop = 40;
			cardStyle.TextureMarginBottom = 40;

			cardStyle.ContentMarginLeft = 24;
			cardStyle.ContentMarginRight = 24;
			cardStyle.ContentMarginTop = 24;
			cardStyle.ContentMarginBottom = 24;
		}
		else
		{
			var stonePanel = UIStyle.CreateStonePanel();
			if (stonePanel is StyleBoxTexture sbt)
			{
				cardStyle = sbt;
			}
		}

		var cardPanel = new Panel();
		cardPanel.Name = "CardPanel_Panel";
		cardPanel.CustomMinimumSize = new Vector2(600, 700);
		cardPanel.AnchorLeft = 0.5f;
		cardPanel.AnchorRight = 0.5f;
		cardPanel.AnchorTop = 0.5f;
		cardPanel.AnchorBottom = 0.5f;
		cardPanel.OffsetLeft = -300.0f;
		cardPanel.OffsetRight = 300.0f;
		cardPanel.OffsetTop = -350.0f;
		cardPanel.OffsetBottom = 350.0f;
		cardPanel.GrowHorizontal = GrowDirection.Both;
		cardPanel.GrowVertical = GrowDirection.Both;
		cardPanel.ClipContents = true;
		cardPanel.AddThemeStyleboxOverride("panel", cardStyle);
		return cardPanel;
	}

	private MarginContainer CreateProfileMarginContainer()
	{
		var marginContainer = new MarginContainer();
		marginContainer.Name = "MarginContainer_MarginContainer";
		marginContainer.AnchorLeft = 0.0f;
		marginContainer.AnchorRight = 1.0f;
		marginContainer.AnchorTop = 0.0f;
		marginContainer.AnchorBottom = 1.0f;
		marginContainer.OffsetLeft = 30.0f;
		marginContainer.OffsetRight = -30.0f;
		marginContainer.OffsetTop = 30.0f;
		marginContainer.OffsetBottom = -30.0f;
		marginContainer.GrowHorizontal = GrowDirection.Both;
		marginContainer.GrowVertical = GrowDirection.Both;
		return marginContainer;
	}

	private VBoxContainer CreateProfileVBox()
	{
		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		vbox.Alignment = BoxContainer.AlignmentMode.Begin;
		return vbox;
	}

	private Label CreateProfileTitle()
	{
		var title = new Label();
		title.Name = "Title_Label";
		UIStyle.ApplyTitle(title, "PLAYER PROFILE", 26);

		var titleFont = new SystemFont();
		titleFont.FontNames = new string[] { "Cinzel Bold", "Cinzel", "Palatino Linotype", "Garamond", "Georgia", "serif" };
		titleFont.FontWeight = 700;
		title.AddThemeFontOverride("font", titleFont);
		title.AddThemeColorOverride("font_color", new Color(0.85f, 0.65f, 0.4f));
		title.AddThemeFontSizeOverride("font_size", 28);

		return title;
	}

	private TextureRect CreateProfileFactionFlag()
	{
		var factionFlag = new TextureRect();
		factionFlag.Name = "FactionFlag_TextureRect";
		Texture2D flagTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/alliance_flag.webp");
		if (flagTexture == null)
		{
			flagTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/profile_frame_normal.webp");
		}

		factionFlag.Texture = flagTexture;
		factionFlag.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		factionFlag.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		factionFlag.CustomMinimumSize = new Vector2(210, 210);
		factionFlag.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		factionFlag.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		return factionFlag;
	}

	private void CreateProfileDataRows(VBoxContainer vbox)
	{
		var profileInfoColor = new Color(0.95f, 0.78f, 0.52f);

		var profileFont = new SystemFont();
		profileFont.FontNames = new string[] { "Cinzel Bold", "Cinzel", "Palatino Linotype", "Garamond", "Georgia", "serif" };
		profileFont.FontWeight = 700;

		var profileDataRows = new (string Key, string Value, Color ValueColor)[]
		{
			("Username", "Horald_Topa", profileInfoColor),
			("Faction", "Human Alliance", profileInfoColor),
			("Matches Played", "142", profileInfoColor),
			("Victories", "89", new Color(0.55f, 0.95f, 0.55f)),
			("Defeats", "53", new Color(0.95f, 0.5f, 0.45f)),
			("Win Rate", "62.7%", new Color(0.6f, 0.85f, 1.0f)),
			("Rank", "Grand Marshal", profileInfoColor)
		};

		foreach (var row in profileDataRows)
		{
			var rowContainer = new HBoxContainer();
			rowContainer.Name = "RowContainer_HBoxContainer";
			rowContainer.Alignment = BoxContainer.AlignmentMode.Center;
			rowContainer.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
			rowContainer.AddThemeConstantOverride("separation", 8);

			var keyLabel = new Label();
			keyLabel.Name = "KeyLabel_Label";
			keyLabel.Text = TranslationServer.Translate(row.Key) + ":";
			keyLabel.HorizontalAlignment = HorizontalAlignment.Right;
			keyLabel.AddThemeFontOverride("font", profileFont);
			keyLabel.AddThemeColorOverride("font_color", profileInfoColor);
			keyLabel.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.02f, 0.04f));
			keyLabel.AddThemeConstantOverride("outline_size", 4);
			keyLabel.AddThemeFontSizeOverride("font_size", 23);
			rowContainer.AddChild(keyLabel);

			var valueLabel = new Label();
			valueLabel.Name = "ValueLabel_Label";
			valueLabel.Text = TranslationServer.Translate(row.Value);
			valueLabel.HorizontalAlignment = HorizontalAlignment.Left;
			valueLabel.AddThemeFontOverride("font", profileFont);
			valueLabel.AddThemeColorOverride("font_color", row.ValueColor);
			valueLabel.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.02f, 0.04f));
			valueLabel.AddThemeConstantOverride("outline_size", 5);
			valueLabel.AddThemeFontSizeOverride("font_size", 23);
			rowContainer.AddChild(valueLabel);

			vbox.AddChild(rowContainer);
			vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
		}
	}

	private Button CreateProfileBackButton()
	{
		var backBtn = new Button();
		backBtn.Name = "BackBtn_Button";

		backBtn.Flat = false;
		backBtn.Text = TranslationServer.Translate("BACK");

		backBtn.Pressed += () =>
		{
			PlayClickSound();
			_profilePopup?.QueueFree();
			_profilePopup = null;
		};
		backBtn.MouseEntered += () => PlayHoverSound();

		Texture2D profileBtnTex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/player_profile_button.webp");

		backBtn.CustomMinimumSize = new Vector2(210, 48);
		backBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		backBtn.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		if (profileBtnTex != null)
		{
			var normalStyle = new StyleBoxTexture();
			normalStyle.Texture = profileBtnTex;
			normalStyle.TextureMarginLeft = 0;
			normalStyle.TextureMarginRight = 0;
			normalStyle.TextureMarginTop = 0;
			normalStyle.TextureMarginBottom = 0;
			normalStyle.ContentMarginLeft = 16;
			normalStyle.ContentMarginRight = 16;
			normalStyle.ContentMarginTop = 8;
			normalStyle.ContentMarginBottom = 8;

			var hoverStyle = (StyleBoxTexture)normalStyle.Duplicate();
			hoverStyle.ModulateColor = new Color(1.15f, 1.15f, 1.2f);

			var pressedStyle = (StyleBoxTexture)normalStyle.Duplicate();
			pressedStyle.ModulateColor = new Color(0.85f, 0.85f, 0.9f);

			backBtn.AddThemeStyleboxOverride("normal", normalStyle);
			backBtn.AddThemeStyleboxOverride("hover", hoverStyle);
			backBtn.AddThemeStyleboxOverride("pressed", pressedStyle);
			backBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		}

		backBtn.AddThemeFontOverride("font", _norseBoldFont);
		backBtn.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		backBtn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		backBtn.AddThemeColorOverride("font_pressed_color", UIStyle.ColorGold);
		backBtn.AddThemeColorOverride("font_focus_color", UIStyle.ColorGold);
		backBtn.AddThemeFontSizeOverride("font_size", 18);

		return backBtn;
	}

	private Control _installWaitingPopup;

	private void OnMapEditorPressed()
	{
		if (OperatingSystem.IsWindows())
		{
			if (VSCodeManager.Instance.IsInstalling)
			{
				ShowInstallWaitingPopup();
				return;
			}

			if (!VSCodeManager.Instance.IsInstalled())
			{
				VSCodeManager.Instance.StartInstallIfNeeded();
				ShowInstallWaitingPopup();
				return;
			}
		}

		UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
	}

	private void ShowInstallWaitingPopup()
	{
		if (_installWaitingPopup != null)
		{
			_installWaitingPopup.QueueFree();
		}

		_installWaitingPopup = new Panel();
		_installWaitingPopup.Name = "InstallWaitingPopup_Panel";
		_installWaitingPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_installWaitingPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(_installWaitingPopup);

		var cardPanel = new Panel();
		cardPanel.Name = "CardPanel_Panel";
		cardPanel.CustomMinimumSize = new Vector2(450, 250);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		_installWaitingPopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(400, 200);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var title = new Label();
		title.Name = "Title_Label";
		UIStyle.ApplyTitle(title, "⚠️ EDITOR INSTALLING", 22);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(title);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });

		var msg = new Label();
		msg.Name = "Msg_Label";
		msg.Text = "The Map Data Editor is currently installing in the background.\nPlease wait for it to complete.";
		msg.HorizontalAlignment = HorizontalAlignment.Center;
		msg.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		msg.AddThemeFontSizeOverride("font_size", 14);
		vbox.AddChild(msg);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 25) });

		var okBtn = new Button();
		okBtn.Name = "OkBtn_Button";
		okBtn.AddThemeConstantOverride("icon_max_width", 0);
		SetupButton(okBtn, "OK", () =>
		{
			_installWaitingPopup.QueueFree();
			_installWaitingPopup = null;
		});
		okBtn.CustomMinimumSize = new Vector2(120, 40);
		okBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(okBtn);

		StartInstallWaitingPolling();
	}

	private void StartInstallWaitingPolling()
	{
		var timer = GetTree().CreateTimer(0.5f);
		timer.Timeout += OnInstallWaitingPollTimeout;
	}

	private void OnInstallWaitingPollTimeout()
	{
		if (_installWaitingPopup == null || !GodotObject.IsInstanceValid(_installWaitingPopup))
		{
			return;
		}

		if (OperatingSystem.IsWindows())
		{
			if (VSCodeManager.Instance.IsInstalled())
			{
				_installWaitingPopup.QueueFree();
				_installWaitingPopup = null;
				UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
				return;
			}

			if (VSCodeManager.Instance.IsInstalling)
			{
				StartInstallWaitingPolling();
			}
		}
	}
}
