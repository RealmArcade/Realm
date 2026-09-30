using Godot;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Realm.Godot.Services;

public class EditorPreferencesData
{
	public bool HideChromeBorderOverlay { get; set; } = false;
	public bool HideHudDuringToolUsage { get; set; } = true;
	public float PanelOpacity { get; set; } = 0.95f;
	public int AutoBackupIntervalMinutes { get; set; } = 30;
	public int MaxBackupSnapshots { get; set; } = 3;

	public EditorPreferencesData Clone()
	{
		return new EditorPreferencesData
		{
			HideChromeBorderOverlay = this.HideChromeBorderOverlay,
			HideHudDuringToolUsage = this.HideHudDuringToolUsage,
			PanelOpacity = this.PanelOpacity,
			AutoBackupIntervalMinutes = this.AutoBackupIntervalMinutes,
			MaxBackupSnapshots = this.MaxBackupSnapshots
		};
	}
}

public partial class EditorSettingsDialog : FloatingDialogBase
{
	private const string SettingsFilePath = "user://editor_settings.json";

	public static EditorPreferencesData CurrentSettings { get; private set; } = new EditorPreferencesData();

	private EditorPreferencesData _snapshot = new EditorPreferencesData();

	private CheckBox _chkHideChromeBorder;
	private CheckBox _chkHideHudDuringToolUsage;

	private HSlider _sldPanelOpacity;
	private Label _lblPanelOpacity;
	private HSlider _sldMaxBackups;
	private Label _lblMaxBackups;
	private OptionButton _optAutoBackup;

	public EditorSettingsDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Map Editor Usability Settings"), new Vector2(460, 440))
	{
		LoadSettingsFromFile();
		BuildControls();
	}

	private void BuildControls()
	{
		var scrollBody = CreateScrollBody(460);
		var contentVBox = new VBoxContainer();
		contentVBox.AddThemeConstantOverride("separation", 10);
		contentVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(contentVBox);

		// SECTION 1: INTERFACE & OVERLAYS
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

		// SECTION 2: BACKUPS & WORKFLOW
		AddSectionHeader(contentVBox, "💾 " + TranslationServer.Translate("BACKUPS & WORKFLOW"), new Color(0.7f, 0.95f, 0.6f));

		string[] autoBackupOptions = new[]
		{
			TranslationServer.Translate("15 minutes").ToString(),
			TranslationServer.Translate("30 minutes").ToString(),
			TranslationServer.Translate("1 hour").ToString()
		};

		int autoBackupIdx = CurrentSettings.AutoBackupIntervalMinutes switch
		{
			15 => 0,
			30 => 1,
			60 => 2,
			_ => 1
		};

		_optAutoBackup = AddOptionDropdown(
			contentVBox,
			TranslationServer.Translate("Auto-Backup Interval:"),
			autoBackupOptions,
			autoBackupIdx,
			(idx) =>
			{
				CurrentSettings.AutoBackupIntervalMinutes = idx switch
				{
					0 => 15,
					1 => 30,
					2 => 60,
					_ => 30
				};
				ApplyLiveSettings();
			},
			140f
		);

		(_sldMaxBackups, _lblMaxBackups) = AddSlider(
			contentVBox,
			TranslationServer.Translate("Max Backup Snapshots:"),
			1.0f,
			20.0f,
			1.0f,
			CurrentSettings.MaxBackupSnapshots,
			(val) =>
			{
				CurrentSettings.MaxBackupSnapshots = (int)val;
				ApplyLiveSettings();
			},
			"0",
			140f
		);

		var btnBackupsRow = new HBoxContainer();
		btnBackupsRow.AddThemeConstantOverride("separation", 10);
		contentVBox.AddChild(btnBackupsRow);

		var btnOpenBackups = new Button();
		btnOpenBackups.Set("icon_max_width", 0);
		btnOpenBackups.Text = "📁 " + TranslationServer.Translate("Open map_backups folder");
		btnOpenBackups.TooltipText = TranslationServer.Translate("Reveals the map_backups folder in Windows Explorer");
		btnOpenBackups.FocusMode = FocusModeEnum.None;
		btnOpenBackups.CustomMinimumSize = new Vector2(220, 32);
		btnOpenBackups.Pressed += OpenMapBackupsFolder;
		btnBackupsRow.AddChild(btnOpenBackups);

		AddSectionHeader(contentVBox, "🛠️ " + TranslationServer.Translate("DEVELOPER & EDITOR TOOLS"), new Color(0.6f, 0.85f, 0.95f));

		var btnRepairRow = new HBoxContainer();
		btnRepairRow.AddThemeConstantOverride("separation", 10);
		contentVBox.AddChild(btnRepairRow);

		var btnRepairAssetIndex = new Button();
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
				await Task.Run(() => AssetIndexService.Instance.RebuildIndexFromCas());
			}
			btnRepairAssetIndex.Disabled = false;
		};
		btnRepairRow.AddChild(btnRepairAssetIndex);

		var btnReinstallRow = new HBoxContainer();
		btnReinstallRow.AddThemeConstantOverride("separation", 10);
		contentVBox.AddChild(btnReinstallRow);

		var btnReinstallVSCode = new Button();
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
				VSCodeManager.Instance.ForceReinstall();
			}
			Hud?.ShowFeedback(TranslationServer.Translate("Reinstalling VS Code dependencies in background..."));
		};
		btnReinstallRow.AddChild(btnReinstallVSCode);
	}

	private void OpenMapBackupsFolder()
	{
		string targetFolder = Path.Combine(OS.GetUserDataDir(), "map_backups");

		if (!Directory.Exists(targetFolder))
		{
			Directory.CreateDirectory(targetFolder);
		}

		string fullPath = Path.GetFullPath(targetFolder);
		if (OperatingSystem.IsWindows())
		{
			try
			{
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = $"\"{fullPath}\"",
					UseShellExecute = true
				});
				return;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[EditorSettingsDialog] Failed to open explorer.exe: {ex.Message}");
			}
		}

		OS.ShellOpen(fullPath);
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
		if (_optAutoBackup != null)
		{
			_optAutoBackup.Selected = CurrentSettings.AutoBackupIntervalMinutes switch
			{
				15 => 0,
				30 => 1,
				60 => 2,
				_ => 1
			};
		}
		if (_sldMaxBackups != null)
		{
			_sldMaxBackups.Value = CurrentSettings.MaxBackupSnapshots;
			_lblMaxBackups.Text = $"{CurrentSettings.MaxBackupSnapshots}";
		}
	}

	public void ApplyLiveSettings()
	{
		if (_lblPanelOpacity != null) _lblPanelOpacity.Text = $"{CurrentSettings.PanelOpacity:F2}";
		if (_lblMaxBackups != null) _lblMaxBackups.Text = $"{CurrentSettings.MaxBackupSnapshots}";

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
			if (File.Exists(fullPath))
			{
				string json = File.ReadAllText(fullPath);
				var loaded = JsonSerializer.Deserialize<EditorPreferencesData>(json);
				if (loaded != null)
				{
					var jNode = JsonNode.Parse(json)?.AsObject();
					if (jNode != null && !jNode.ContainsKey(nameof(EditorPreferencesData.AutoBackupIntervalMinutes)) && jNode.ContainsKey("AutoSaveIntervalMinutes"))
					{
						int legacyVal = (int)(jNode["AutoSaveIntervalMinutes"] ?? 30);
						loaded.AutoBackupIntervalMinutes = legacyVal switch
						{
							<= 15 => 15,
							<= 30 => 30,
							_ => 60
						};
					}
					CurrentSettings = loaded;
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[EditorSettingsDialog] Load error: {ex.Message}");
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
			GD.PrintErr($"[EditorSettingsDialog] Save error: {ex.Message}");
		}
	}
}
