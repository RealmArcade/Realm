using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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

	private CheckBox _chkHideChromeBorder;
	private CheckBox _chkHideHudDuringToolUsage;

	private HSlider _sldPanelOpacity;
	private Label _lblPanelOpacity;
	private OptionButton _optAutoBackup;

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
		BuildWorkflowSection(contentVBox);
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

	private void BuildWorkflowSection(VBoxContainer contentVBox)
	{
		var spacer1 = new Control();
		spacer1.Name = "Spacer1_Control";
		spacer1.CustomMinimumSize = new Vector2(0, 10);
		contentVBox.AddChild(spacer1);

		AddSectionHeader(contentVBox, "💾 " + TranslationServer.Translate("AUTOSAVE & VERSIONS"), new Color(0.7f, 0.95f, 0.6f));

		string[] autosaveOptions = new[]
		{
			TranslationServer.Translate("1 minute").ToString(),
			TranslationServer.Translate("2 minutes").ToString(),
			TranslationServer.Translate("5 minutes").ToString(),
			TranslationServer.Translate("15 minutes").ToString()
		};

		int autosaveIndex = CurrentSettings.AutoBackupIntervalMinutes switch
		{
			1 => 0,
			2 => 1,
			5 => 2,
			15 => 3,
			_ => 2
		};

		_optAutoBackup = AddOptionDropdown(
			contentVBox,
			TranslationServer.Translate("Save a version every:"),
			autosaveOptions,
			autosaveIndex,
			(idx) =>
			{
				CurrentSettings.AutoBackupIntervalMinutes = idx switch
				{
					0 => 1,
					1 => 2,
					2 => 5,
					3 => 15,
					_ => 5
				};
				ApplyLiveSettings();
			},
			140f
		);

		AddDescription(contentVBox, TranslationServer.Translate("Your edits are written to the map folder as you work, and a version you can go back to is kept at this interval. Versions live inside the map folder and are copied to a backup on this computer, so a mistake never costs you the whole map."));

		var btnVersionsRow = new HBoxContainer();
		btnVersionsRow.Name = "BtnVersionsRow_HBoxContainer";
		btnVersionsRow.AddThemeConstantOverride("separation", 10);
		contentVBox.AddChild(btnVersionsRow);

		var btnOpenVersions = new Button();
		btnOpenVersions.Name = "BtnOpenVersions_Button";
		btnOpenVersions.Set("icon_max_width", 0);
		btnOpenVersions.Text = "🕘 " + TranslationServer.Translate("MAP VERSIONS");
		btnOpenVersions.TooltipText = TranslationServer.Translate("Go back to an earlier version of this map, or recover it from a backup");
		btnOpenVersions.FocusMode = FocusModeEnum.None;
		btnOpenVersions.CustomMinimumSize = new Vector2(220, 32);
		btnOpenVersions.Pressed += () => Hud?.OpenMapVersionsDialog();
		btnVersionsRow.AddChild(btnOpenVersions);
	}

	private void BuildDeveloperToolsSection(VBoxContainer contentVBox)
	{
		var spacer2 = new Control();
		spacer2.Name = "Spacer2_Control";
		spacer2.CustomMinimumSize = new Vector2(0, 10);
		contentVBox.AddChild(spacer2);

		AddSectionHeader(contentVBox, "🛠️ " + TranslationServer.Translate("DEVELOPER & EDITOR TOOLS"), new Color(0.6f, 0.85f, 0.95f));

		var btnAuthorSignatureRow = new HBoxContainer();
		btnAuthorSignatureRow.Name = "BtnAuthorSignatureRow_HBoxContainer";
		btnAuthorSignatureRow.AddThemeConstantOverride("separation", 10);
		contentVBox.AddChild(btnAuthorSignatureRow);

		var btnAuthorSignature = new Button();
		btnAuthorSignature.Name = "BtnAuthorSignature_Button";
		btnAuthorSignature.Set("icon_max_width", 0);
		btnAuthorSignature.Text = "✍️ " + TranslationServer.Translate("Author Signature");
		btnAuthorSignature.TooltipText = TranslationServer.Translate("View author identity key, signature details, and backup location");
		btnAuthorSignature.FocusMode = FocusModeEnum.None;
		btnAuthorSignature.CustomMinimumSize = new Vector2(240, 32);
		btnAuthorSignature.Pressed += () => Hud?.OpenAuthorSignatureDialog();
		btnAuthorSignatureRow.AddChild(btnAuthorSignature);

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
		btnReinstallVSCode.Text = "🔄 " + TranslationServer.Translate("Reinstall / Repair VS Code");
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

	private void SyncControls()
	{
		if (_chkHideChromeBorder != null) _chkHideChromeBorder.ButtonPressed = CurrentSettings.HideChromeBorderOverlay;
		if (_chkHideHudDuringToolUsage != null) _chkHideHudDuringToolUsage.ButtonPressed = CurrentSettings.HideHudDuringToolUsage;
		if (_sldPanelOpacity != null)
		{
			_sldPanelOpacity.Value = CurrentSettings.PanelOpacity;
			_lblPanelOpacity.Text = $"{CurrentSettings.PanelOpacity:F2}";
		}

		if (_optAutoBackup != null)
		{
			_optAutoBackup.Selected = CurrentSettings.AutoBackupIntervalMinutes switch
			{
				1 => 0,
				2 => 1,
				5 => 2,
				15 => 3,
				_ => 2
			};
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

			HandleLegacySettings(json, loaded);

			CurrentSettings = loaded;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[EditorSettingsDialog] Load error: {ex.Message}");
		}
	}

	private static void HandleLegacySettings(string json, EditorPreferencesData loaded)
	{
		JsonNode parsedNode = JsonNode.Parse(json);
		if (parsedNode == null) return;

		JsonObject jNode = parsedNode.AsObject();
		if (jNode == null) return;

		if (jNode.ContainsKey(nameof(EditorPreferencesData.AutoBackupIntervalMinutes))) return;
		if (!jNode.ContainsKey("AutoSaveIntervalMinutes")) return;

		int legacyVal = GetLegacyIntervalValue(jNode);
		loaded.AutoBackupIntervalMinutes = GetMigratedBackupInterval(legacyVal);
	}

	private static int GetLegacyIntervalValue(JsonObject jNode)
	{
		if (jNode.TryGetPropertyValue("AutoSaveIntervalMinutes", out JsonNode valueNode))
		{
			if (valueNode != null)
			{
				return (int)valueNode;
			}
		}

		return 30;
	}

	private static int GetMigratedBackupInterval(int legacyVal)
	{
		if (legacyVal <= 15) return 15;
		if (legacyVal <= 30) return 30;
		return 60;
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
			GD.PrintErr($"[EditorSettingsDialog] Save error: {ex.Message}");
		}
	}
}