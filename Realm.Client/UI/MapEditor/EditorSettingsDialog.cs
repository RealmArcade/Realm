using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class EditorSettingsDialog : FloatingDialogBase
{
	public EditorSettingsDialog() { }

	private const string SettingsFilePath = "user://editor_settings.json";

	public static EditorPreferencesData CurrentSettings { get; private set; } = new EditorPreferencesData();

	private EditorPreferencesData _snapshot = new EditorPreferencesData();

	private CheckBox _chkHideChromeBorder = null!;
	private CheckBox _chkHideHudDuringToolUsage = null!;

	private HSlider _sldPanelOpacity = null!;
	private Label _lblPanelOpacity = null!;


	public EditorSettingsDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Map Editor Usability Settings"), new Vector2(510, 500))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_usability_settings.webp", 55, 15, 60, 60);
		if (TitleLabel?.GetParent() is MarginContainer titleMargin)
		{
			titleMargin.AddThemeConstantOverride("margin_top", -42);
		}
		if (CloseButton?.GetParent() is MarginContainer closeMargin)
		{
			closeMargin.AddThemeConstantOverride("margin_top", -32);
		}
		SetFooterApplyOnly("APPLY");
		LoadSettingsFromFile();
		BuildControls();
	}

	private void BuildControls()
	{
		var scrollBody = CreateScrollBody(400);
		var contentVBox = new VBoxContainer();
		contentVBox.Name = "ContentVBox_VBoxContainer";
		contentVBox.AddThemeConstantOverride("separation", 12);
		contentVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scrollBody.AddChild(contentVBox);

		var topInfoSpacer = new Control();
		topInfoSpacer.Name = "TopInfoSpacer_Control";
		topInfoSpacer.CustomMinimumSize = new Vector2(0, 18);
		contentVBox.AddChild(topInfoSpacer);

		BuildInterfaceSection(contentVBox);
		BuildDeveloperToolsSection(contentVBox);
	}

	private void BuildInterfaceSection(VBoxContainer contentVBox)
	{
		AddSectionHeader(contentVBox, "🖥️ " + TranslationServer.Translate("INTERFACE & OVERLAYS"), new Color(0.95f, 0.8f, 0.4f));

		_chkHideChromeBorder = AddCheckBox(
			contentVBox,
			TranslationServer.Translate("Hide Chrome Border Overlay"),
			CurrentSettings.HideChromeBorderOverlay,
			(val) =>
			{
				CurrentSettings.HideChromeBorderOverlay = val;
				ApplyLiveSettings();
			},
			TranslationServer.Translate("Hides the decorative border frames and chrome overlays around editor panels for a cleaner workspace")
		);

		_chkHideHudDuringToolUsage = AddCheckBox(
			contentVBox,
			TranslationServer.Translate("Hide HUD during tool usage"),
			CurrentSettings.HideHudDuringToolUsage,
			(val) =>
			{
				CurrentSettings.HideHudDuringToolUsage = val;
				ApplyLiveSettings();
			},
			TranslationServer.Translate("Temporarily hides side panels and UI overlays while actively painting, sculpting, or using 3D tools")
		);

		(_sldPanelOpacity, _lblPanelOpacity) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Panel Opacity:"),
			0.3f,
			1.0f,
			0.05f,
			CurrentSettings.PanelOpacity,
			(val) =>
			{
				CurrentSettings.PanelOpacity = val;
				ApplyLiveSettings();
			},
			"0.00",
			140f
		);
	}

	private void BuildDeveloperToolsSection(VBoxContainer contentVBox)
	{
		var spacer2 = new Control();
		spacer2.Name = "Spacer2_Control";
		spacer2.CustomMinimumSize = new Vector2(0, 10);
		contentVBox.AddChild(spacer2);

		AddSectionHeader(contentVBox, "🛠️ " + TranslationServer.Translate("DEVELOPER & EDITOR TOOLS"), new Color(0.6f, 0.85f, 0.95f));

		var btnManageStoresRow = new HBoxContainer();
		btnManageStoresRow.Name = "BtnManageStoresRow_HBoxContainer";
		btnManageStoresRow.AddThemeConstantOverride("separation", 10);
		contentVBox.AddChild(btnManageStoresRow);

		var btnManageStores = new Button();
		btnManageStores.Name = "BtnManageStores_Button";
		btnManageStores.Set("icon_max_width", 0);
		btnManageStores.Text = "🗄️ " + TranslationServer.Translate("MANAGE BACKUP STORAGE");
		btnManageStores.TooltipText = TranslationServer.Translate("See how much space each map's history uses and remove the ones you no longer need");
		btnManageStores.FocusMode = FocusModeEnum.None;
		btnManageStores.CustomMinimumSize = new Vector2(220, 32);
		btnManageStores.Pressed += () => Hud?.OpenMapHistoryStoresDialog();
		btnManageStoresRow.AddChild(btnManageStores);

		var btnRepairRow = new HBoxContainer();
		btnRepairRow.Name = "BtnRepairRow_HBoxContainer";
		btnRepairRow.AddThemeConstantOverride("separation", 10);
		contentVBox.AddChild(btnRepairRow);

		var btnRepairAssetIndex = new Button();
		btnRepairAssetIndex.Name = "BtnRepairAssetIndex_Button";
		btnRepairAssetIndex.Set("icon_max_width", 0);
		btnRepairAssetIndex.Text = "🗄️ " + TranslationServer.Translate("Repair Asset Index");
		btnRepairAssetIndex.TooltipText = TranslationServer.Translate("Completely deletes and re-creates the asset index database from CAS");
		btnRepairAssetIndex.FocusMode = FocusModeEnum.None;
		btnRepairAssetIndex.CustomMinimumSize = new Vector2(240, 32);
		btnRepairAssetIndex.Pressed += async () =>
		{
			btnRepairAssetIndex.Disabled = true;
			if (Hud != null)
			{
				await Hud.ShowAssetIndexRepairModalAsync();
			}
			else
			{
				await Task.Run(() => GameContext.AssetIndexService.RebuildIndexFromCas());
			}

			btnRepairAssetIndex.Disabled = false;
		};
		btnRepairRow.AddChild(btnRepairAssetIndex);

		var btnReinstallRow = new HBoxContainer();
		btnReinstallRow.Name = "BtnReinstallRow_HBoxContainer";
		btnReinstallRow.AddThemeConstantOverride("separation", 10);
		contentVBox.AddChild(btnReinstallRow);

		var btnReinstallVSCode = new Button();
		btnReinstallVSCode.Name = "BtnReinstallVSCode_Button";
		btnReinstallVSCode.Set("icon_max_width", 0);
		btnReinstallVSCode.Text = "🔄 " + TranslationServer.Translate("Reinstall / Repair VS Codium");
		btnReinstallVSCode.TooltipText = TranslationServer.Translate("Forces a fresh download and installation of VS Code and editor dependencies into application user data");
		btnReinstallVSCode.FocusMode = FocusModeEnum.None;
		btnReinstallVSCode.CustomMinimumSize = new Vector2(240, 32);
		btnReinstallVSCode.Pressed += () =>
		{
			btnReinstallVSCode.Disabled = true;
			btnReinstallVSCode.Text = "⏳ " + TranslationServer.Translate("Reinstalling...");
			if (OperatingSystem.IsWindows())
			{
				GameContext.VSCode.ForceReinstall();
			}

			Hud?.ShowFeedback(TranslationServer.Translate("Reinstalling VS Code dependencies in background..."));
		};
		btnReinstallRow.AddChild(btnReinstallVSCode);
	}

	public override void OpenDialog()
	{
		_snapshot = CurrentSettings.Clone();
		SyncControls();
		base.OpenDialog();
	}

	private void SyncControls()
	{
		if (_chkHideChromeBorder != null) _chkHideChromeBorder.ButtonPressed = CurrentSettings.HideChromeBorderOverlay;
		if (_chkHideHudDuringToolUsage != null) _chkHideHudDuringToolUsage.ButtonPressed = CurrentSettings.HideHudDuringToolUsage;
		if (_sldPanelOpacity != null)
		{
			_sldPanelOpacity.Value = CurrentSettings.PanelOpacity;
			_lblPanelOpacity.Text = $"{CurrentSettings.PanelOpacity:F2}";
		}

	}

	public void ApplyLiveSettings()
	{
		if (_lblPanelOpacity != null) _lblPanelOpacity.Text = $"{CurrentSettings.PanelOpacity:F2}";

		Hud?.ApplyEditorPreferences(CurrentSettings);
	}

	protected override void OnApply()
	{
		SaveSettingsToFile();
		Hud?.ShowFeedback(TranslationServer.Translate("Editor settings saved."));
		CloseDialog();
	}

	protected override void OnCancel()
	{
		if (_snapshot != null)
		{
			CurrentSettings = _snapshot.Clone();
			ApplyLiveSettings();
		}

		base.OnCancel();
	}

	private static void LoadSettingsFromFile()
	{
		try
		{
			string fullPath = ProjectSettings.GlobalizePath(SettingsFilePath);
			if (!File.Exists(fullPath)) return;

			string json = File.ReadAllText(fullPath);
			var loaded = JsonSerializer.Deserialize<EditorPreferencesData>(json);
			if (loaded == null) return;

			CurrentSettings = loaded;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[EditorSettingsDialog] Load error: {ex.Message}");
		}
	}

	private static void SaveSettingsToFile()
	{
		try
		{
			string fullPath = ProjectSettings.GlobalizePath(SettingsFilePath);
			string dir = Path.GetDirectoryName(fullPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			string json = JsonSerializer.Serialize(CurrentSettings, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(fullPath, json);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[EditorSettingsDialog] Save error: {ex.Message}");
		}
	}
}