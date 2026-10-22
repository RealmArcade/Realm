using System;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.Services;

namespace Realm.Client.UI;

[GlobalClass]
public partial class SettingsMenu : Control
{
	public SettingsMenu()
	{
		GameContext.RegisterSettings(this);
	}

	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

	[Export] public bool IsOverlay = false;

	private Panel _bgPanel;
	private Panel _mainFrame;
	private PanelContainer _videoPanel;
	private PanelContainer _audioPanel;
	private PanelContainer _controlsPanel;
	private PanelContainer _otherPanel;

	private Label _settingsTitle;
	private Label _videoTitle;
	private Label _audioTitle;
	private Label _controlsTitle;
	private Label _otherTitle;

	private OptionButton _resolutionOpt;
	private OptionButton _qualityOpt;
	private CheckBox _disableShadowsChk;
	private CheckBox _disableDayNightLightingChk;
	private CheckBox _disableWeatherEffectsChk;
	private CheckBox _floatingCombatTextChk;
	private OptionButton _windowModeOpt;
	private OptionButton _vsyncOpt;
	private HSlider _fpsLimitSlider;
	private Label _fpsLimitValLabel;
	private OptionButton _healthBarsOpt;
	private OptionButton _languageOpt;
	private CheckBox _displayFpsChk;
	private CheckBox _unitOutlineChk;
	private CheckBox _recordReplaysChk;
	private CheckBox _seedMapFilesChk;
	private CheckBox _movementPredictionChk;

	private HSlider _masterSlider;
	private HSlider _musicSlider;
	private HSlider _sfxSlider;
	private HSlider _voiceSlider;

	private Label _masterValLabel;
	private Label _musicValLabel;
	private Label _sfxValLabel;
	private Label _voiceValLabel;

	private HSlider _scrollSpeedSlider;
	private HSlider _mouseSensSlider;
	private HSlider _hudScaleSlider;

	private Label _scrollValLabel;
	private Label _sensValLabel;
	private Label _hudScaleValLabel;

	private Button _applyBtn;
	private Button _cancelBtn;
	private Button _resetBtn;

	public override void _Ready()
	{
		GameContext.RegisterSettings(this);
		MouseFilter = MouseFilterEnum.Stop;

		_bgPanel = GetNodeOrNull<Panel>("Background");
		if (_bgPanel != null)
		{
			_bgPanel.MouseFilter = MouseFilterEnum.Stop;
		}

		if (GetNodeOrNull<Control>("OverlayBg") is Control overlay)
		{
			overlay.MouseFilter = MouseFilterEnum.Stop;
		}

		if (GetNodeOrNull<Control>("CenterContainer") is Control center)
		{
			center.MouseFilter = MouseFilterEnum.Stop;
		}

		_mainFrame = GetNode<Panel>("CenterContainer/MainFrame");
		_mainFrame.MouseFilter = MouseFilterEnum.Stop;
		_videoPanel = GetNode<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel");
		_audioPanel = GetNode<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel");
		_controlsPanel = GetNode<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel");
		_otherPanel = GetNode<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/OtherPanel");

		_settingsTitle = GetNode<Label>("CenterContainer/MainFrame/TitlePanel/SettingsTitle");
		_videoTitle = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/TitleBox/HBox/PanelTitle");
		_audioTitle = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/TitleBox/HBox/PanelTitle");
		_controlsTitle = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/TitleBox/HBox/PanelTitle");
		_otherTitle = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/OtherPanel/VBox/TitleBox/HBox/PanelTitle");

		_resolutionOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/ResRow/ResolutionOpt");
		_qualityOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/QualRow/QualityOpt");
		_windowModeOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/ModeRow/WindowModeOpt");
		_vsyncOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/VsyncRow/VsyncOpt");
		_healthBarsOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/HealthBarsRow/HealthBarsOpt");
		_languageOpt = GetNode<OptionButton>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/LanguageRow/LanguageOpt");

		_disableShadowsChk = GetNode<CheckBox>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/DisableShadowsChk");
		_disableDayNightLightingChk = GetNode<CheckBox>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/DisableDayNightLightingChk");
		_disableWeatherEffectsChk = GetNode<CheckBox>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/DisableWeatherEffectsChk");
		_fpsLimitSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/FpsRow/FpsLimitSlider");
		_fpsLimitValLabel = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/FpsRow/FpsLimitValLabel");
		_floatingCombatTextChk = GetNode<CheckBox>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/FloatingCombatTextChk");

		_masterSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/MasterRow/MasterSlider");
		_musicSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/MusicRow/MusicSlider");
		_sfxSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/SfxRow/SfxSlider");
		_voiceSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/VoiceRow/VoiceSlider");

		_masterValLabel = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/MasterRow/MasterValLabel");
		_musicValLabel = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/MusicRow/MusicValLabel");
		_sfxValLabel = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/SfxRow/SfxValLabel");
		_voiceValLabel = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/VoiceRow/VoiceValLabel");

		_displayFpsChk = GetNode<CheckBox>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/DisplayFpsChk");
		_unitOutlineChk = GetNode<CheckBox>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/UnitOutlineChk");
		_recordReplaysChk = GetNode<CheckBox>("CenterContainer/MainFrame/VBoxContainer/OtherPanel/VBox/Grid/RecordReplaysChk");
		_seedMapFilesChk = GetNode<CheckBox>("CenterContainer/MainFrame/VBoxContainer/OtherPanel/VBox/Grid/SeedMapFilesChk");
		_movementPredictionChk = GetNodeOrNull<CheckBox>("CenterContainer/MainFrame/VBoxContainer/OtherPanel/VBox/Grid/MovementPredictionChk");

		_scrollSpeedSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/ScrollRow/ScrollSpeedSlider");
		_mouseSensSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/SensRow/MouseSensSlider");
		_hudScaleSlider = GetNode<HSlider>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/HudScaleRow/HudScaleSlider");

		_scrollValLabel = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/ScrollRow/ScrollValLabel");
		_sensValLabel = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/SensRow/SensValLabel");
		_hudScaleValLabel = GetNode<Label>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/HudScaleRow/HudScaleValLabel");

		_applyBtn = GetNode<Button>("CenterContainer/MainFrame/VBoxContainer/ButtonsRow/ApplyButton");
		_cancelBtn = GetNode<Button>("CenterContainer/MainFrame/VBoxContainer/ButtonsRow/CancelButton");
		_resetBtn = GetNode<Button>("CenterContainer/MainFrame/VBoxContainer/ButtonsRow/ResetButton");

		GetNode<TextureRect>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/TitleBox/HBox/TitleIcon").Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/icon_video.svg");
		GetNode<TextureRect>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/TitleBox/HBox/TitleIcon").Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/icon_audio.svg");
		GetNode<TextureRect>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/TitleBox/HBox/TitleIcon").Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/icon_controls.svg");
		GetNode<TextureRect>("CenterContainer/MainFrame/VBoxContainer/OtherPanel/VBox/TitleBox/HBox/TitleIcon").Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/icon_gameplay.svg");

		ApplyThemeStyles();
		PopulateDropdowns();
		SetupSliders();
		SetupButtons();

		LoadCurrentSettings();
	}

	private void ApplyThemeStyles()
	{
		ApplyBackgroundStyle();
		ApplyContainerStyles();
		ApplyTitleStyles();
		ApplyLabelStyles();
		ApplySeparatorStyles();
		ApplyCheckboxStyles();
	}

	private void ApplyBackgroundStyle()
	{
		if (_bgPanel == null) return;

		_bgPanel.Visible = true;
		Texture2D bgTexture = null;
		string[] bgPaths = new string[]
		{
			"res://Assets/UI/options_bg.webp",
			"res://Assets/UI/menu_background_with_frame.webp"
		};

		foreach (var path in bgPaths)
		{
			if (!ResourceLoader.Exists(path)) continue;
			bgTexture = TrimmedTextureLoader.LoadTrimmedTexture(path);
			if (bgTexture != null) break;
		}

		if (bgTexture != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = bgTexture;
			_bgPanel.AddThemeStyleboxOverride("panel", style);
			_bgPanel.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		}
		else
		{
			_bgPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		}

		if (GetNodeOrNull<ColorRect>("OverlayBg") is ColorRect rect)
		{
			rect.Visible = false;
		}
	}

	private void ApplyContainerStyles()
	{
		if (GetNodeOrNull<Panel>("CenterContainer/ShadowPanel") is Panel shadowPanel)
		{
			var shadowStyle = new StyleBoxFlat();
			shadowStyle.BgColor = new Color(0, 0, 0, 0);
			shadowStyle.ShadowColor = new Color(0, 0, 0, 0.7f);
			shadowStyle.ShadowSize = 24;
			shadowStyle.ShadowOffset = new Vector2(0, 14);
			shadowStyle.CornerRadiusTopLeft = 25;
			shadowStyle.CornerRadiusTopRight = 25;
			shadowStyle.CornerRadiusBottomLeft = 25;
			shadowStyle.CornerRadiusBottomRight = 25;
			shadowPanel.AddThemeStyleboxOverride("panel", shadowStyle);
		}

		_mainFrame.AddThemeStyleboxOverride("panel", UIStyle.CreateLightStonePanel());
		_videoPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		_audioPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		_controlsPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		_otherPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

		if (GetNodeOrNull<Panel>("CenterContainer/MainFrame/TitlePanel") is Panel titlePanel)
		{
			var titleStyle = new StyleBoxFlat();
			titleStyle.BgColor = new Color(0.24f, 0.21f, 0.18f, 1.0f); // Match configurations background
			titleStyle.BorderColor = new Color(0.45f, 0.40f, 0.32f, 1.0f); // Bronze/Gold frame
			titleStyle.SetBorderWidthAll(3);
			titleStyle.CornerRadiusTopLeft = 8;
			titleStyle.CornerRadiusTopRight = 8;
			titleStyle.CornerRadiusBottomLeft = 8;
			titleStyle.CornerRadiusBottomRight = 8;

			// Beautiful drop shadow
			titleStyle.ShadowColor = new Color(0, 0, 0, 0.75f);
			titleStyle.ShadowSize = 12;
			titleStyle.ShadowOffset = new Vector2(0, 6);

			titlePanel.AddThemeStyleboxOverride("panel", titleStyle);
		}

		if (GetNodeOrNull<Panel>("CenterContainer/MainFrame/TitlePanel/TitleInnerPanel") is Panel titleInnerPanel)
		{
			var innerStyle = new StyleBoxFlat();
			innerStyle.BgColor = new Color(0.18f, 0.16f, 0.14f, 1.0f); // Match inset color
			innerStyle.BorderColor = new Color(0.82f, 0.72f, 0.50f, 0.5f); // Thinner semi-transparent gold outline
			innerStyle.SetBorderWidthAll(1);
			innerStyle.CornerRadiusTopLeft = 5;
			innerStyle.CornerRadiusTopRight = 5;
			innerStyle.CornerRadiusBottomLeft = 5;
			innerStyle.CornerRadiusBottomRight = 5;

			titleInnerPanel.AddThemeStyleboxOverride("panel", innerStyle);
		}
	}

	private void ApplyTitleStyles()
	{
		_settingsTitle.Text = TranslationServer.Translate("GAME SETTINGS");
		_settingsTitle.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		_settingsTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_settingsTitle.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.07f, 0.06f));
		_settingsTitle.AddThemeConstantOverride("outline_size", 4);
		_settingsTitle.AddThemeColorOverride("font_shadow_color", new Color(0.0f, 0.0f, 0.0f, 0.7f));
		_settingsTitle.AddThemeConstantOverride("shadow_offset_x", 1);
		_settingsTitle.AddThemeConstantOverride("shadow_offset_y", 2);
		_settingsTitle.AddThemeFontSizeOverride("font_size", 26);
		_settingsTitle.HorizontalAlignment = HorizontalAlignment.Center;
		_settingsTitle.VerticalAlignment = VerticalAlignment.Center;

		// Create header panel style for VIDEO, AUDIO, CONTROLS and OTHER title boxes
		var headerStyle = new StyleBoxFlat();
		headerStyle.BgColor = new Color(0.12f, 0.11f, 0.10f, 0.6f); // Dark translucent charcoal/bronze
		headerStyle.BorderColor = new Color(0.40f, 0.35f, 0.28f, 0.7f); // Antique bronze-gold border
		headerStyle.SetBorderWidthAll(1);
		headerStyle.CornerRadiusTopLeft = 4;
		headerStyle.CornerRadiusTopRight = 4;
		headerStyle.CornerRadiusBottomLeft = 4;
		headerStyle.CornerRadiusBottomRight = 4;
		headerStyle.ContentMarginLeft = 14;
		headerStyle.ContentMarginRight = 14;
		headerStyle.ContentMarginTop = 8;
		headerStyle.ContentMarginBottom = 8;

		if (GetNodeOrNull<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/TitleBox") is PanelContainer videoHeader)
			videoHeader.AddThemeStyleboxOverride("panel", headerStyle);

		if (GetNodeOrNull<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/TitleBox") is PanelContainer audioHeader)
			audioHeader.AddThemeStyleboxOverride("panel", headerStyle);

		if (GetNodeOrNull<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/TitleBox") is PanelContainer controlsHeader)
			controlsHeader.AddThemeStyleboxOverride("panel", headerStyle);

		if (GetNodeOrNull<PanelContainer>("CenterContainer/MainFrame/VBoxContainer/OtherPanel/VBox/TitleBox") is PanelContainer otherHeader)
			otherHeader.AddThemeStyleboxOverride("panel", headerStyle);

		_videoTitle.Text = TranslationServer.Translate("VIDEO");
		_videoTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_videoTitle.AddThemeFontSizeOverride("font_size", 18);
		_videoTitle.HorizontalAlignment = HorizontalAlignment.Left;
		_videoTitle.VerticalAlignment = VerticalAlignment.Center;

		_audioTitle.Text = TranslationServer.Translate("AUDIO");
		_audioTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_audioTitle.AddThemeFontSizeOverride("font_size", 18);
		_audioTitle.HorizontalAlignment = HorizontalAlignment.Left;
		_audioTitle.VerticalAlignment = VerticalAlignment.Center;

		_controlsTitle.Text = TranslationServer.Translate("CONTROLS");
		_controlsTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_controlsTitle.AddThemeFontSizeOverride("font_size", 18);
		_controlsTitle.HorizontalAlignment = HorizontalAlignment.Left;
		_controlsTitle.VerticalAlignment = VerticalAlignment.Center;

		_otherTitle.Text = TranslationServer.Translate("OTHER");
		_otherTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_otherTitle.AddThemeFontSizeOverride("font_size", 18);
		_otherTitle.HorizontalAlignment = HorizontalAlignment.Left;
		_otherTitle.VerticalAlignment = VerticalAlignment.Center;
	}

	private void ApplyLabelStyles()
	{
		string[] labelPaths =
		{
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/ResRow/ResLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/QualRow/QualLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/ModeRow/ModeLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/VsyncRow/VsyncLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/FpsRow/FpsLimitLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/HealthBarsRow/HealthBarsLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/LanguageRow/LanguageLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/MasterRow/MasterLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/MusicRow/MusicLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/SfxRow/SfxLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/VoiceRow/VoiceLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/ScrollRow/ScrollLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/SensRow/SensLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/HudScaleRow/HudScaleLabel"
		};
		foreach (var path in labelPaths)
		{
			var lbl = GetNode<Label>(path);
			lbl.Text = TranslationServer.Translate(lbl.Text);
			lbl.AddThemeColorOverride("font_color", new Color(0.82f, 0.80f, 0.75f));
			lbl.AddThemeFontSizeOverride("font_size", 14);
		}

		string[] valLabelPaths =
		{
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/FpsRow/FpsLimitValLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/MasterRow/MasterValLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/MusicRow/MusicValLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/SfxRow/SfxValLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/VoiceRow/VoiceValLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/ScrollRow/ScrollValLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/SensRow/SensValLabel",
			"CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/HudScaleRow/HudScaleValLabel"
		};
		var valLabelStyle = new StyleBoxFlat();
		valLabelStyle.BgColor = new Color(0.12f, 0.11f, 0.10f, 0.6f); // Dark translucent charcoal/bronze matching subtitles
		valLabelStyle.BorderColor = new Color(0.40f, 0.35f, 0.28f, 0.7f); // Antique bronze-gold border matching subtitles
		valLabelStyle.SetBorderWidthAll(1);
		valLabelStyle.CornerRadiusTopLeft = 4;
		valLabelStyle.CornerRadiusTopRight = 4;
		valLabelStyle.CornerRadiusBottomLeft = 4;
		valLabelStyle.CornerRadiusBottomRight = 4;
		valLabelStyle.ContentMarginLeft = 6;
		valLabelStyle.ContentMarginRight = 6;
		valLabelStyle.ContentMarginTop = 2;
		valLabelStyle.ContentMarginBottom = 2;

		foreach (var path in valLabelPaths)
		{
			var lbl = GetNode<Label>(path);
			lbl.AddThemeStyleboxOverride("normal", valLabelStyle);
			lbl.AddThemeColorOverride("font_color", new Color(0.88f, 0.82f, 0.65f));
			lbl.AddThemeFontSizeOverride("font_size", 14);
			lbl.HorizontalAlignment = HorizontalAlignment.Center;
			lbl.VerticalAlignment = VerticalAlignment.Center;
			lbl.CustomMinimumSize = new Vector2(40, 24);
		}

		_fpsLimitValLabel.CustomMinimumSize = new Vector2(76, 24);
	}

	private void ApplySeparatorStyles()
	{
		var sepStyle = new StyleBoxFlat();
		sepStyle.BgColor = new Color(0.42f, 0.38f, 0.33f, 0.6f);
		sepStyle.ContentMarginTop = 1;
		sepStyle.ContentMarginBottom = 1;

		var separators = new[]
		{
			GetNode<HSeparator>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/VideoPanel/VBox/Separator"),
			GetNode<HSeparator>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/AudioPanel/VBox/Separator"),
			GetNode<HSeparator>("CenterContainer/MainFrame/VBoxContainer/TopRowContainer/RightColumn/ControlsPanel/VBox/Separator"),
			GetNode<HSeparator>("CenterContainer/MainFrame/VBoxContainer/OtherPanel/VBox/Separator")
		};
		foreach (var sep in separators)
		{
			sep.AddThemeStyleboxOverride("separator", sepStyle);
		}
	}

	private void ApplyCheckboxStyles()
	{
		UIStyle.ApplyCheckboxStyle(_disableShadowsChk);
		_disableShadowsChk.Text = TranslationServer.Translate(_disableShadowsChk.Text);
		UIStyle.ApplyCheckboxStyle(_disableDayNightLightingChk);
		_disableDayNightLightingChk.Text = TranslationServer.Translate(_disableDayNightLightingChk.Text);
		UIStyle.ApplyCheckboxStyle(_disableWeatherEffectsChk);
		_disableWeatherEffectsChk.Text = TranslationServer.Translate(_disableWeatherEffectsChk.Text);
		UIStyle.ApplyCheckboxStyle(_floatingCombatTextChk);
		_floatingCombatTextChk.Text = TranslationServer.Translate(_floatingCombatTextChk.Text);
		UIStyle.ApplyCheckboxStyle(_displayFpsChk);
		_displayFpsChk.Text = TranslationServer.Translate(_displayFpsChk.Text);
		UIStyle.ApplyCheckboxStyle(_unitOutlineChk);
		_unitOutlineChk.Text = TranslationServer.Translate(_unitOutlineChk.Text);
		_unitOutlineChk.TooltipText = TranslationServer.Translate(_unitOutlineChk.TooltipText);
		UIStyle.ApplyCheckboxStyle(_recordReplaysChk);
		_recordReplaysChk.Text = TranslationServer.Translate(_recordReplaysChk.Text);
		UIStyle.ApplyCheckboxStyle(_seedMapFilesChk);
		_seedMapFilesChk.Text = TranslationServer.Translate(_seedMapFilesChk.Text);
		_seedMapFilesChk.TooltipText = TranslationServer.Translate(_seedMapFilesChk.TooltipText);
		if (_movementPredictionChk != null)
		{
			UIStyle.ApplyCheckboxStyle(_movementPredictionChk);
			_movementPredictionChk.Text = TranslationServer.Translate(_movementPredictionChk.Text);
		}
	}

	private void PopulateDropdowns()
	{
		PopulateResolutionDropdown();
		PopulateQualityDropdown();
		PopulateWindowModeDropdown();
		PopulateVsyncDropdown();
		PopulateHealthBarsDropdown();
		PopulateLanguageDropdown();

		var dropdowns = new[] { _resolutionOpt, _qualityOpt, _windowModeOpt, _vsyncOpt, _healthBarsOpt, _languageOpt };
		ApplyDropdownStyles(dropdowns);

		SetupWindowModeEvent();
		SetupCheckboxEvents();
		SetupNatTestEvent();
	}

	private void PopulateResolutionDropdown()
	{
		_resolutionOpt.Clear();
		if (GameSettings.Resolutions == null || GameSettings.Resolutions.Count == 0)
		{
			GameSettings.InitializeResolutions();
		}

		for (int i = 0; i < GameSettings.Resolutions.Count; i++)
		{
			var res = GameSettings.Resolutions[i];
			_resolutionOpt.AddItem($"{res.X} x {res.Y}", i);
		}
	}

	private void PopulateQualityDropdown()
	{
		_qualityOpt.Clear();
		_qualityOpt.AddItem(TranslationServer.Translate("Low"), 0);
		_qualityOpt.AddItem(TranslationServer.Translate("Medium"), 1);
		_qualityOpt.AddItem(TranslationServer.Translate("High"), 2);
		_qualityOpt.AddItem(TranslationServer.Translate("Ultra"), 3);
	}

	private void PopulateWindowModeDropdown()
	{
		_windowModeOpt.Clear();
		_windowModeOpt.AddItem(TranslationServer.Translate("Fullscreen"), 0);
		_windowModeOpt.AddItem(TranslationServer.Translate("Windowed"), 1);
		_windowModeOpt.AddItem(TranslationServer.Translate("Borderless"), 2);
	}

	private void PopulateVsyncDropdown()
	{
		_vsyncOpt.Clear();
		_vsyncOpt.AddItem(TranslationServer.Translate("On"), 0);
		_vsyncOpt.AddItem(TranslationServer.Translate("Off"), 1);
	}

	private void PopulateHealthBarsDropdown()
	{
		_healthBarsOpt.Clear();
		_healthBarsOpt.AddItem(TranslationServer.Translate("Hidden"), 0);
		_healthBarsOpt.AddItem(TranslationServer.Translate("Visible"), 1);
		_healthBarsOpt.AddItem(TranslationServer.Translate("Damaged"), 2);
	}

	private void PopulateLanguageDropdown()
	{
		_languageOpt.Clear();
		_languageOpt.AddItem("English", 0);
		_languageOpt.AddItem("Español", 1);
		_languageOpt.AddItem("Français", 2);
		_languageOpt.AddItem("Deutsch", 3);
		_languageOpt.AddItem("Português", 4);
		_languageOpt.AddItem("Русский", 5);
		_languageOpt.AddItem("中文", 6);
		_languageOpt.AddItem("日本語", 7);
		_languageOpt.AddItem("العربية", 8);
		_languageOpt.AddItem("हिन्दी", 9);
	}

	private void SetupWindowModeEvent()
	{
		_windowModeOpt.ItemSelected += OnWindowModeItemSelected;
	}

	private void OnWindowModeItemSelected(long idx)
	{
		var mode = (WindowMode)idx;
		bool isWindowed = mode == WindowMode.Windowed;
		_resolutionOpt.Disabled = !isWindowed;

		if (GameSettings.Resolutions == null || GameSettings.Resolutions.Count == 0)
			return;

		if (!isWindowed)
		{
			_resolutionOpt.Select(0);
		}
		else
		{
			_resolutionOpt.Select(Math.Clamp(GameSettings.ResolutionIdx, 0, GameSettings.Resolutions.Count - 1));
		}
	}

	private void SetupCheckboxEvents()
	{
		_disableShadowsChk.Pressed += () => GameContext.UserInterface.PlayClickSound();
		_disableShadowsChk.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
		_disableDayNightLightingChk.Pressed += () => GameContext.UserInterface.PlayClickSound();
		_disableDayNightLightingChk.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
		_disableWeatherEffectsChk.Pressed += () => GameContext.UserInterface.PlayClickSound();
		_disableWeatherEffectsChk.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
		_floatingCombatTextChk.Pressed += () => GameContext.UserInterface.PlayClickSound();
		_floatingCombatTextChk.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
		_displayFpsChk.Pressed += () => GameContext.UserInterface.PlayClickSound();
		_displayFpsChk.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
		_unitOutlineChk.Pressed += () => GameContext.UserInterface.PlayClickSound();
		_unitOutlineChk.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
		_recordReplaysChk.Pressed += () => GameContext.UserInterface.PlayClickSound();
		_recordReplaysChk.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
		_seedMapFilesChk.Pressed += () => GameContext.UserInterface.PlayClickSound();
		_seedMapFilesChk.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
		if (_movementPredictionChk != null)
		{
			_movementPredictionChk.Pressed += () => GameContext.UserInterface.PlayClickSound();
			_movementPredictionChk.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
			_movementPredictionChk.Toggled += OnMovementPredictionToggled;
		}
	}

	private void OnMovementPredictionToggled(bool pressed)
	{
		if (!pressed) return;
		if (GameSettings.MovementPredictionPerformanceOverride) return;

		var monitor = Realm.Client.Services.GameContext.PredictionPerformanceMonitor;
		if (monitor == null || !monitor.IsMachineTooSlow) return;

		_movementPredictionChk.ButtonPressed = false;
		GameContext.UserInterface?.PlayWarningSound();
		GameContext.UserInterface?.ShowConfirmationDialog(
			string.Format(
				TranslationServer.Translate("This machine measured too slow for movement prediction ({0:F1} ms prediction update, {1:F1} ms average frame time). Prediction may make movement stutter. Lower graphics quality, close background apps, or keep prediction disabled. Enable prediction anyway?"),
				monitor.AveragePredictionCostMs,
				monitor.AverageFrameTimeMs),
			() =>
			{
				GameSettings.MovementPredictionPerformanceOverride = true;
				if (_movementPredictionChk != null)
				{
					_movementPredictionChk.ButtonPressed = true;
				}
			},
			confirmText: TranslationServer.Translate("KEEP ENABLED"),
			cancelText: TranslationServer.Translate("KEEP DISABLED"),
			titleOverride: "PERFORMANCE WARNING");
	}

	private void SetupNatTestEvent()
	{
		if (GameContext.Lobby == null)
			return;

		GameContext.Lobby.NatTestCompleted += OnNatTestCompleted;
	}

	private void OnNatTestCompleted()
	{
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || _seedMapFilesChk == null || !GodotObject.IsInstanceValid(_seedMapFilesChk))
		{
			return;
		}

		bool canHost = GameContext.Lobby != null && GameContext.Lobby.LocalNatType != NatType.Symmetric;
		if (!canHost)
		{
			_seedMapFilesChk.Disabled = true;
			_seedMapFilesChk.ButtonPressed = false;
			_seedMapFilesChk.TooltipText = TranslationServer.Translate("Seeding disabled: Machine cannot host due to STUN test failure or Symmetric NAT.");
		}
		else
		{
			_seedMapFilesChk.Disabled = false;
			_seedMapFilesChk.ButtonPressed = GameSettings.SeedMapFiles;
		}
	}

	private void ApplyDropdownStyles(OptionButton[] dropdowns)
	{
		foreach (var opt in dropdowns)
		{
			opt.Flat = false;
			opt.AddThemeStyleboxOverride("normal", UIStyle.CreateLightDropdownNormal());
			opt.AddThemeStyleboxOverride("hover", UIStyle.CreateLightDropdownHover());
			opt.AddThemeStyleboxOverride("pressed", UIStyle.CreateLightDropdownPressed());
			opt.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

			opt.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
			opt.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.95f, 0.8f));
			opt.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
			opt.AddThemeFontSizeOverride("font_size", 14);

			opt.ItemSelected += (idx) => GameContext.UserInterface.PlayClickSound();
			opt.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
		}
	}

	private void OnWindowModeSelected(long idx)
	{
		var mode = (WindowMode)idx;
		bool isWindowed = mode == WindowMode.Windowed;
		_resolutionOpt.Disabled = !isWindowed;

		if (GameSettings.Resolutions == null || GameSettings.Resolutions.Count == 0) return;

		if (!isWindowed)
		{
			_resolutionOpt.Select(0);
			return;
		}

		_resolutionOpt.Select(Math.Clamp(GameSettings.ResolutionIdx, 0, GameSettings.Resolutions.Count - 1));
	}

	private void SetupSliders()
	{
		_scrollSpeedSlider.MinValue = 0;
		_scrollSpeedSlider.MaxValue = 100;
		_scrollSpeedSlider.Step = 1;

		_mouseSensSlider.MinValue = 0;
		_mouseSensSlider.MaxValue = 100;
		_mouseSensSlider.Step = 1;

		_hudScaleSlider.MinValue = 50;
		_hudScaleSlider.MaxValue = 150;
		_hudScaleSlider.Step = 5;

		_fpsLimitSlider.MinValue = 0;
		_fpsLimitSlider.MaxValue = GameSettings.FpsLimitOptions.Length - 1;
		_fpsLimitSlider.Step = 1;

		var sliders = new[] { _masterSlider, _musicSlider, _sfxSlider, _voiceSlider, _scrollSpeedSlider, _mouseSensSlider, _hudScaleSlider, _fpsLimitSlider };

		var trackStyle = UIStyle.CreateLightSliderTrack();
		var fillStyle = UIStyle.CreateLightSliderFill();

		var grabberTex = UIStyle.CreateSquareStoneGrabberTexture(false);
		var grabberHiTex = UIStyle.CreateSquareStoneGrabberTexture(true);

		foreach (var s in sliders)
		{
			s.AddThemeStyleboxOverride("slider", trackStyle);
			s.AddThemeStyleboxOverride("grabber_area", fillStyle);
			s.AddThemeStyleboxOverride("grabber_area_highlight", fillStyle);
			s.AddThemeIconOverride("grabber", grabberTex);
			s.AddThemeIconOverride("grabber_highlight", grabberHiTex);

			s.MouseEntered += () => GameContext.UserInterface.PlayHoverSound();
			s.DragEnded += (valChanged) => GameContext.UserInterface.PlayClickSound();
		}

		_masterSlider.ValueChanged += (val) =>
		{
			GameSettings.MasterVolume = (float)val;
			_masterValLabel.Text = val.ToString("0");
			GameContext.UserInterface.UpdateAudioVolumes();
		};
		_musicSlider.ValueChanged += (val) =>
		{
			GameSettings.MusicVolume = (float)val;
			_musicValLabel.Text = val.ToString("0");
			GameContext.UserInterface.UpdateAudioVolumes();
		};
		_sfxSlider.ValueChanged += (val) =>
		{
			GameSettings.SfxVolume = (float)val;
			_sfxValLabel.Text = val.ToString("0");
			GameContext.UserInterface.UpdateAudioVolumes();
		};
		_voiceSlider.ValueChanged += (val) =>
		{
			GameSettings.VoiceVolume = (float)val;
			_voiceValLabel.Text = val.ToString("0");
			GameContext.UserInterface.UpdateAudioVolumes();
		};
		_scrollSpeedSlider.ValueChanged += (val) =>
		{
			GameSettings.ScrollSpeed = (float)val;
			_scrollValLabel.Text = (val / 25.0f).ToString("0.0");
		};
		_mouseSensSlider.ValueChanged += (val) =>
		{
			GameSettings.MouseSens = (float)val;
			_sensValLabel.Text = (val / 40.0f).ToString("0.0");
		};
		_hudScaleSlider.ValueChanged += (val) =>
		{
			GameSettings.HudScale = (float)val;
			_hudScaleValLabel.Text = ((val - 100.0f) / 100.0f).ToString("0.0");
		};
		_fpsLimitSlider.ValueChanged += (val) =>
		{
			_fpsLimitValLabel.Text = FormatFpsLimitValue((int)val);
		};
	}

	private static string FormatFpsLimitValue(int optionIndex)
	{
		int clampedIndex = Math.Clamp(optionIndex, 0, GameSettings.FpsLimitOptions.Length - 1);
		int fpsLimit = GameSettings.FpsLimitOptions[clampedIndex];
		return fpsLimit <= 0 ? TranslationServer.Translate("Unlimited") : fpsLimit.ToString();
	}

	private void SetupButtons()
	{
		SetupSettingsButton(_applyBtn, "ᚠ", "APPLY", "ᚠ", ApplySettings);
		SetupSettingsButton(_cancelBtn, "ᛉ", "CANCEL", "ᛉ", CancelSettings);
		SetupSettingsButton(_resetBtn, "᚛", "RESET TO DEFAULT", "᚜", ResetToDefaults);
	}

	private void SetupSettingsButton(Button btn, string leftRune, string keyText, string rightRune, Action onClick)
	{
		btn.Flat = false;
		btn.Text = "";

		var leftLbl = btn.GetNode<Label>("HBox/LeftRune");
		leftLbl.Text = leftRune;
		leftLbl.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		leftLbl.AddThemeFontSizeOverride("font_size", 14);

		var textLbl = btn.GetNode<Label>("HBox/TextLabel");
		textLbl.Text = TranslationServer.Translate(keyText);
		textLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		textLbl.AddThemeFontSizeOverride("font_size", 16);

		var rightLbl = btn.GetNode<Label>("HBox/RightRune");
		rightLbl.Text = rightRune;
		rightLbl.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		rightLbl.AddThemeFontSizeOverride("font_size", 14);

		btn.AddThemeStyleboxOverride("normal", UIStyle.CreateLightButtonNormal());
		btn.AddThemeStyleboxOverride("hover", UIStyle.CreateLightButtonHover());
		btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateLightButtonPressed());
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.MouseEntered += () =>
		{
			GameContext.UserInterface.PlayHoverSound();
			textLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		};
		btn.MouseExited += () => { textLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull); };
		btn.ButtonDown += () => { textLbl.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow); };
		btn.ButtonUp += () => { textLbl.AddThemeColorOverride("font_color", btn.IsHovered() ? UIStyle.ColorGold : UIStyle.ColorGoldDull); };

		btn.Pressed += () =>
		{
			GameContext.UserInterface.PlayClickSound();
			onClick?.Invoke();
		};
	}

	private void LoadCurrentSettings()
	{
		bool isWindowed = GameSettings.WindowModeIdx == WindowMode.Windowed;
		_resolutionOpt.Disabled = !isWindowed;

		if (GameSettings.Resolutions != null && GameSettings.Resolutions.Count > 0)
		{
			if (isWindowed)
			{
				_resolutionOpt.Select(Math.Clamp(GameSettings.ResolutionIdx, 0, GameSettings.Resolutions.Count - 1));
			}
			else
			{
				_resolutionOpt.Select(0);
			}
		}

		_qualityOpt.Select((int)GameSettings.QualityIdx);
		_windowModeOpt.Select((int)GameSettings.WindowModeIdx);
		_vsyncOpt.Select(GameSettings.Vsync ? 0 : 1);

		int fpsLimitOptionIndex = GetFpsLimitOptionIndex();
		_fpsLimitSlider.Value = fpsLimitOptionIndex;
		_fpsLimitValLabel.Text = FormatFpsLimitValue(fpsLimitOptionIndex);

		_masterSlider.Value = GameSettings.MasterVolume;
		_musicSlider.Value = GameSettings.MusicVolume;
		_sfxSlider.Value = GameSettings.SfxVolume;
		_voiceSlider.Value = GameSettings.VoiceVolume;

		_scrollSpeedSlider.Value = GameSettings.ScrollSpeed;
		_mouseSensSlider.Value = GameSettings.MouseSens;
		_hudScaleSlider.Value = GameSettings.HudScale;

		_masterValLabel.Text = _masterSlider.Value.ToString("0");
		_musicValLabel.Text = _musicSlider.Value.ToString("0");
		_sfxValLabel.Text = _sfxSlider.Value.ToString("0");
		_voiceValLabel.Text = _voiceSlider.Value.ToString("0");

		_scrollValLabel.Text = (_scrollSpeedSlider.Value / 25.0f).ToString("0.0");
		_sensValLabel.Text = (_mouseSensSlider.Value / 40.0f).ToString("0.0");
		_hudScaleValLabel.Text = ((_hudScaleSlider.Value - 100.0f) / 100.0f).ToString("0.0");

		_disableShadowsChk.ButtonPressed = GameSettings.ShadowsEnabled;
		_disableDayNightLightingChk.ButtonPressed = GameSettings.DayNightLightingEnabled;
		_disableWeatherEffectsChk.ButtonPressed = GameSettings.WeatherEffectsEnabled;
		_floatingCombatTextChk.ButtonPressed = GameSettings.FloatingCombatText;
		_displayFpsChk.ButtonPressed = GameSettings.DisplayFps;
		_unitOutlineChk.ButtonPressed = GameSettings.OccludedUnitOutlines;
		_recordReplaysChk.ButtonPressed = GameSettings.RecordReplays;

		bool canHost = GameContext.Lobby != null && GameContext.Lobby.LocalNatType != NatType.Symmetric;
		if (!canHost)
		{
			_seedMapFilesChk.Disabled = true;
			_seedMapFilesChk.ButtonPressed = false;
			_seedMapFilesChk.TooltipText = TranslationServer.Translate("Seeding disabled: Machine cannot host due to STUN test failure or Symmetric NAT.");
		}
		else
		{
			_seedMapFilesChk.Disabled = false;
			_seedMapFilesChk.ButtonPressed = GameSettings.SeedMapFiles;
		}

		_healthBarsOpt.Select((int)GameSettings.ShowHealthBars);
		_languageOpt.Select((int)GameSettings.Language);
		if (_movementPredictionChk != null)
		{
			bool movementPredictionAvailable = PathUtils.IsDevelopmentBuild;
			_movementPredictionChk.Visible = movementPredictionAvailable;
			_movementPredictionChk.Disabled = !movementPredictionAvailable;
			_movementPredictionChk.ButtonPressed = movementPredictionAvailable && GameSettings.MovementPrediction;
		}
	}

	private async void ApplySettings()
	{
		if (_applyBtn != null) _applyBtn.Disabled = true;

		try
		{
			var windowMode = (WindowMode)_windowModeOpt.Selected;
			await SaveResolutionSettings(windowMode, _resolutionOpt.Selected);

			bool vsyncEnabled = _vsyncOpt.Selected == 0;
			SaveGraphicsSettings(vsyncEnabled);
			SaveGameplaySettings();

			var newLang = (GameLanguage)_languageOpt.Selected;
			GameSettings.Language = newLang;
			LocalizationManager.UpdateLocale(newLang);

			GameSettings.Save();
			GameSettings.ApplyGraphicsSettings(this);

			if (GameContext.InGameHud != null)
			{
				GameContext.InGameHud.ApplyHUDScale();
				GameContext.InGameHud.UpdateFPSVisibility();
			}

			if (GameContext.MapEditorHud != null)
			{
				GameContext.MapEditorHud.UpdateFPSVisibility();
			}

			GD.Print("Settings Applied successfully!");
			CloseOrTransition();
		}
		finally
		{
			if (_applyBtn != null && GodotObject.IsInstanceValid(_applyBtn))
			{
				_applyBtn.Disabled = false;
			}
		}
	}

	private async Task SaveResolutionSettings(WindowMode windowMode, int resSel)
	{
		if (windowMode == WindowMode.Windowed && resSel >= 0 && GameSettings.Resolutions != null && resSel < GameSettings.Resolutions.Count)
		{
			GameSettings.ResolutionIdx = resSel;
			GameSettings.WindowedResolutionWidth = GameSettings.Resolutions[resSel].X;
			GameSettings.WindowedResolutionHeight = GameSettings.Resolutions[resSel].Y;
		}

		GameSettings.WindowModeIdx = windowMode;

		if (GameContext.UserInterface != null)
		{
			await GameContext.UserInterface.ApplyWindowSettings(windowMode, GameSettings.ResolutionIdx);
		}
	}

	private void SaveGraphicsSettings(bool vsyncEnabled)
	{
		if (vsyncEnabled)
		{
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
		}
		else
		{
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		}

		GameSettings.QualityIdx = (GraphicsQuality)_qualityOpt.Selected;
		GameSettings.Vsync = vsyncEnabled;
		GameSettings.ShadowsEnabled = _disableShadowsChk.ButtonPressed;
		GameSettings.DayNightLightingEnabled = _disableDayNightLightingChk.ButtonPressed;
		GameSettings.WeatherEffectsEnabled = _disableWeatherEffectsChk.ButtonPressed;
		GameSettings.FloatingCombatText = _floatingCombatTextChk.ButtonPressed;

		int fpsOptionIndex = Math.Clamp((int)_fpsLimitSlider.Value, 0, GameSettings.FpsLimitOptions.Length - 1);
		GameSettings.FpsLimit = GameSettings.FpsLimitOptions[fpsOptionIndex];
	}

	private static int GetFpsLimitOptionIndex()
	{
		int index = Array.IndexOf(GameSettings.FpsLimitOptions, GameSettings.FpsLimit);
		return index >= 0 ? index : 0;
	}

	private void SaveGameplaySettings()
	{
		GameSettings.MasterVolume = (float)_masterSlider.Value;
		GameSettings.MusicVolume = (float)_musicSlider.Value;
		GameSettings.SfxVolume = (float)_sfxSlider.Value;
		GameSettings.VoiceVolume = (float)_voiceSlider.Value;

		GameSettings.ScrollSpeed = (float)_scrollSpeedSlider.Value;
		GameSettings.MouseSens = (float)_mouseSensSlider.Value;
		GameSettings.HudScale = (float)_hudScaleSlider.Value;
		GameSettings.DisplayFps = _displayFpsChk.ButtonPressed;
		GameSettings.OccludedUnitOutlines = _unitOutlineChk.ButtonPressed;
		GameSettings.RecordReplays = _recordReplaysChk.ButtonPressed;
		GameSettings.SeedMapFiles = _seedMapFilesChk.ButtonPressed;
		if (_movementPredictionChk != null)
		{
			GameSettings.MovementPrediction = PathUtils.IsDevelopmentBuild && _movementPredictionChk.ButtonPressed;
		}
		GameSettings.ShowHealthBars = (HealthBarMode)_healthBarsOpt.Selected;
	}

	private void CancelSettings()
	{
		GameSettings.Load();
		GameContext.UserInterface.UpdateAudioVolumes();
		if (GameContext.InGameHud != null)
		{
			GameContext.InGameHud.UpdateFPSVisibility();
		}

		if (GameContext.MapEditorHud != null)
		{
			GameContext.MapEditorHud.UpdateFPSVisibility();
		}

		CloseOrTransition();
	}

	private void CloseOrTransition()
	{
		if (IsOverlay)
		{
			QueueFree();
		}
		else
		{
			GameContext.UserInterface.TransitionTo(GameScreen.MainMenu);
		}
	}

	private void ResetToDefaults()
	{
		GameSettings.ResetToDefaults();
		LoadCurrentSettings();
		if (GameContext.InGameHud != null)
		{
			GameContext.InGameHud.UpdateFPSVisibility();
		}

		if (GameContext.MapEditorHud != null)
		{
			GameContext.MapEditorHud.UpdateFPSVisibility();
		}

		GD.Print("Settings reset to defaults.");
	}

	public override void _ExitTree()
	{
		GameContext.UnregisterSettings(this);

		if (GameContext.Lobby != null)
		{
			GameContext.Lobby.NatTestCompleted -= OnNatTestCompleted;
		}

		base._ExitTree();
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton or InputEventMouseMotion)
		{
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey escapeEvent && escapeEvent.Pressed && escapeEvent.Keycode == Key.Escape)
		{
			GetViewport().SetInputAsHandled();
			CancelSettings();
		}
	}
}
