// The theme pass. Every group of widgets the menu owns is styled the same way - background, containers,
// titles, labels, separators, checkboxes - and the passes are grouped here so a style change is made in
// one file instead of interleaved with the wiring that follows.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.Services;


namespace Realm.Client.UI;

public partial class SettingsMenu
{
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
		Texture2D? bgTexture = null;
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
			shadowPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateSettingsWindowShadow());
		}

		_mainFrame.AddThemeStyleboxOverride("panel", UIStyle.CreateLightStonePanel());
		_videoPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		_audioPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		_controlsPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		_otherPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

		if (GetNodeOrNull<Panel>("CenterContainer/MainFrame/TitlePanel") is Panel titlePanel)
		{
			titlePanel.AddThemeStyleboxOverride("panel", UIStyle.CreateSettingsTitlePlate());
		}

		if (GetNodeOrNull<Panel>("CenterContainer/MainFrame/TitlePanel/TitleInnerPanel") is Panel titleInnerPanel)
		{
			titleInnerPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateSettingsTitleInnerPlate());
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

		var headerStyle = UIStyle.CreateSettingsSectionHeader();

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
		var valLabelStyle = UIStyle.CreateSettingsValuePlate();

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
		var sepStyle = UIStyle.CreateSettingsSeparator();

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
		UIStyle.ApplyCheckboxStyle(_shadowsChk);
		_shadowsChk.Text = LocalizationManager.Translate(_shadowsChk.Text);
		UIStyle.ApplyCheckboxStyle(_dayNightLightingChk);
		_dayNightLightingChk.Text = LocalizationManager.Translate(_dayNightLightingChk.Text);
		UIStyle.ApplyCheckboxStyle(_weatherEffectsChk);
		_weatherEffectsChk.Text = LocalizationManager.Translate(_weatherEffectsChk.Text);
		UIStyle.ApplyCheckboxStyle(_floatingCombatTextChk);
		_floatingCombatTextChk.Text = LocalizationManager.Translate("Floating Combat Text");
		UIStyle.ApplyCheckboxStyle(_displayFpsChk);
		_displayFpsChk.Text = LocalizationManager.Translate("Display FPS");
		UIStyle.ApplyCheckboxStyle(_displayPingChk);
		_displayPingChk.Text = TranslationServer.Translate(_displayPingChk.Text);
		UIStyle.ApplyCheckboxStyle(_unitOutlineChk);
		_unitOutlineChk.Text = TranslationServer.Translate(_unitOutlineChk.Text);
		_unitOutlineChk.TooltipText = TranslationServer.Translate(_unitOutlineChk.TooltipText);
		UIStyle.ApplyCheckboxStyle(_recordReplaysChk);
		_recordReplaysChk.Text = LocalizationManager.Translate("Record replays");
		UIStyle.ApplyCheckboxStyle(_seedMapFilesChk);
		_seedMapFilesChk.Text = LocalizationManager.Translate("Seed map files to other players");
		_seedMapFilesChk.TooltipText = LocalizationManager.Translate("Volunteers a tiny portion of your internet to help players download maps, reducing server costs. Uncheck this if you have a capped internet data plan or a very slow connection");

		if (_dopplerChk != null)
		{
			UIStyle.ApplyCheckboxStyle(_dopplerChk);
			_dopplerChk.Text = TranslationServer.Translate(_dopplerChk.Text);
		}

		if (_distanceMuffleChk != null)
		{
			UIStyle.ApplyCheckboxStyle(_distanceMuffleChk);
			_distanceMuffleChk.Text = TranslationServer.Translate(_distanceMuffleChk.Text);
		}
	}

}