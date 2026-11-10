// Scene construction: identity, icon, visual-effect and sound rows, the cast-test toolbar and the 3D
// preview viewport.

using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Services;
using Realm.Client.VFX;
using Realm.Shared.Audio;
using Realm.Shared.Textures;
using Realm.Client.Utils;


namespace Realm.Client.UI.MapEditor;

public partial class AbilityVfxDialog
{
	private void BuildControls()
	{
		Add3DPreviewViewport(BodyContainer, new Vector2(480, 230));

		Setup3DEnvironment();

		var topControlsVBox = CreateTopControlsColumn();

		var presetRow = AddCameraPresetToolbar(topControlsVBox, includeBack: false);

		var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		presetRow.AddChild(spacer);

		AddButton(presetRow, "🔥 " + TranslationServer.Translate("Cast Test"), () => TriggerCastTest(), "Simulate casting ability VFX & sound", 10, new Vector2(90, 22));

		var playbackRow = new HBoxContainer();
		playbackRow.Name = "PlaybackRow_HBoxContainer";
		playbackRow.AddThemeConstantOverride("separation", 6);

		AddButton(playbackRow, "▶ " + TranslationServer.Translate("Play"), () => PlayVfxAnimation(), "Play VFX", 10, new Vector2(50, 22));
		AddButton(playbackRow, "⏸ " + TranslationServer.Translate("Pause"), () => PauseVfxAnimation(), "Pause VFX", 10, new Vector2(50, 22));
		AddButton(playbackRow, "⏹ " + TranslationServer.Translate("Stop"), () => StopVfxAnimation(), "Stop VFX", 10, new Vector2(50, 22));

		var lblSpeed = new Label();
		lblSpeed.Name = "LblSpeed_Label";
		lblSpeed.Text = TranslationServer.Translate("Speed:");
		lblSpeed.AddThemeFontSizeOverride("font_size", 10);
		lblSpeed.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		playbackRow.AddChild(lblSpeed);

		var sldSpeed = new HSlider();
		sldSpeed.Name = "SldSpeed_HSlider";
		sldSpeed.MinValue = 0.25;
		sldSpeed.MaxValue = 2.50;
		sldSpeed.Step = 0.05;
		sldSpeed.Value = 1.0;
		sldSpeed.CustomMinimumSize = new Vector2(90, 0);
		sldSpeed.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		sldSpeed.ValueChanged += (val) =>
		{
			_playbackSpeed = (float)val;
			if (_vfxInstance != null && GodotObject.IsInstanceValid(_vfxInstance))
			{
				_vfxInstance.SetSpeedScale(_playbackSpeed);
			}
		};
		playbackRow.AddChild(sldSpeed);

		topControlsVBox.AddChild(playbackRow);

		var scrollBody = CreateScrollBody(340);
		var configVBox = new VBoxContainer();
		configVBox.Name = "ConfigVBox_VBoxContainer";
		configVBox.AddThemeConstantOverride("separation", 10);
		configVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollBody.AddChild(configVBox);

		AddSectionHeader(configVBox, "🆔 " + TranslationServer.Translate("IDENTITY"), new Color(0.95f, 0.8f, 0.4f));

		var rowId = BuildIdentityRow(configVBox, "ability/", out _lblObjectTypePrefix);

		_txtSlug = CreateSlugField("ability_slug");
		_txtSlug.TextChanged += (val) =>
		{
			if (_isUpdatingUI) return;
			_slug = TemplateIDHelper.ToSnakeCase(val);
			_abilityId = TemplateIDHelper.NormalizeTemplateID("ability", _slug);
		};
		rowId.AddChild(_txtSlug);
		

		_txtName = AddTextInput(
			configVBox,
			TranslationServer.Translate("Display Name:"),
			_abilityName,
			(val) =>
			{
				if (_isUpdatingUI) return;
				_abilityName = val ?? "";
			},
			TranslationServer.Translate("Ability display name..."),
			140f
		);

		AddSectionHeader(configVBox, "🎨 " + TranslationServer.Translate("ABILITY ICON"), new Color(0.95f, 0.8f, 0.4f));

		var iconRow = new HBoxContainer();
		iconRow.Name = "IconRow_HBoxContainer";
		iconRow.AddThemeConstantOverride("separation", 8);

		var iconPanel = new PanelContainer();
		iconPanel.Name = "IconPanel_PanelContainer";
		iconPanel.CustomMinimumSize = new Vector2(48, 48);
		iconPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

		_iconPreviewRect = new TextureRect();
		_iconPreviewRect.Name = "IconPreviewRect_TextureRect";
		_iconPreviewRect.CustomMinimumSize = new Vector2(44, 44);
		_iconPreviewRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_iconPreviewRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		iconPanel.AddChild(_iconPreviewRect);
		iconRow.AddChild(iconPanel);

		var iconDropdownVBox = new VBoxContainer();
		iconDropdownVBox.Name = "IconDropdownVBox_VBoxContainer";
		iconDropdownVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		(_txtIconPath, _setIconPathValue) = AddAssetFilterDropdown(
			iconDropdownVBox,
			TranslationServer.Translate("Icon Texture:"),
			_currentIconPath,
			(all) => ScanAvailableAssets("icons", all),
			(val) =>
			{
				_currentIconPath = val ?? string.Empty;
				UpdateIconPreview(_currentIconPath);
			},
			TranslationServer.Translate("Select or search icon from metadata..."),
			100f
		);

		iconRow.AddChild(iconDropdownVBox);
		configVBox.AddChild(iconRow);

		AddSectionHeader(configVBox, "✨ " + TranslationServer.Translate("VISUAL EFFECT (VFX)"), new Color(0.35f, 0.75f, 0.9f));

		(_txtVisualEffect, _setVisualEffectValue) = AddAssetFilterDropdown(
			configVBox,
			TranslationServer.Translate("Visual Effect (VFX):"),
			_currentVisualEffect,
			(all) => ScanAvailableAssets("vfx", all),
			(val) =>
			{
				_currentVisualEffect = val ?? string.Empty;
				ReloadVfx();
			},
			TranslationServer.Translate("Select or search VFX preset, custom VFX, or texture..."),
			140f
		);


		AddSectionHeader(configVBox, "🎯 " + TranslationServer.Translate("AREA OF EFFECT (AOE)"), new Color(0.4f, 0.85f, 0.5f));

		(_sldAoeRadius, _lblAoeRadiusVal) = AddSlider(
			configVBox,
			TranslationServer.Translate("AoE Radius:"),
			0.0f,
			20.0f,
			0.25f,
			_currentAoeRadius,
			(val) =>
			{
				_currentAoeRadius = val;
				UpdateAoEIndicator(_currentAoeRadius);
			},
			"0.00m",
			140f
		);

		AddSectionHeader(configVBox, "🔊 " + TranslationServer.Translate("AUDIO & CAST SOUND"), new Color(0.9f, 0.6f, 0.35f));

		(_txtCastSound, _setCastSoundValue) = AddAssetFilterDropdown(
			configVBox,
			TranslationServer.Translate("Cast Sound:"),
			_currentCastSound,
			(all) => ScanAvailableAssets("audio", all),
			(val) => { _currentCastSound = val ?? string.Empty; },
			TranslationServer.Translate("Select or search audio event..."),
			140f,
			false,
			(soundVal) => PlaySoundFile(soundVal)
		);
	}

}