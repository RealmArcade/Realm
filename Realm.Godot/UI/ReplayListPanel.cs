using Godot;
using Realm.Godot.ReplaySystem;
using System;
using System.Collections.Generic;
using System.IO;
using Realm.Shared;

public partial class ReplayListPanel : Control
{
	private VBoxContainer _listContainer;
	private Button _backBtn;
	private Button _deleteAllBtn;
	private Label _titleLabel;
	private Label _noReplaysLabel;

	public override void _Ready()
	{
		_listContainer = GetNodeOrNull<VBoxContainer>("CenterContainer/MainFrame/ContentMargin/VBox/ListFrame/ScrollContainer/ListContainer")
			?? GetNode<VBoxContainer>("CenterContainer/MainFrame/VBox/ListFrame/ScrollContainer/ListContainer");
		_backBtn = GetNodeOrNull<Button>("BackButton")
			?? GetNodeOrNull<Button>("CenterContainer/MainFrame/ContentMargin/VBox/Header/BackButton")
			?? GetNode<Button>("CenterContainer/MainFrame/VBox/Header/BackButton");
		_deleteAllBtn = GetNodeOrNull<Button>("CenterContainer/MainFrame/ContentMargin/VBox/Header/DeleteAllButton")
			?? GetNode<Button>("CenterContainer/MainFrame/VBox/Header/DeleteAllButton");
		_titleLabel = GetNodeOrNull<Label>("CenterContainer/MainFrame/TitleLabel")
			?? GetNodeOrNull<Label>("CenterContainer/MainFrame/ContentMargin/VBox/Header/TitleLabel")
			?? GetNode<Label>("CenterContainer/MainFrame/VBox/Header/TitleLabel");
		_noReplaysLabel = GetNodeOrNull<Label>("CenterContainer/MainFrame/ContentMargin/VBox/NoReplaysLabel")
			?? GetNode<Label>("CenterContainer/MainFrame/VBox/NoReplaysLabel");

		_backBtn.Pressed += OnBackPressed;
		_backBtn.MouseEntered += () => UIManager.Instance?.PlayHoverSound();

		_deleteAllBtn.Pressed += OnDeleteAllPressed;
		_deleteAllBtn.MouseEntered += () => UIManager.Instance?.PlayHoverSound();

		UIStyle.ApplyTitle(_titleLabel, "REPLAYS", 28);
		UIStyle.ApplyButtonText(_backBtn, "BACK", 14);
		_backBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_backBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_backBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_backBtn.AddThemeConstantOverride("icon_max_width", 0);
		_backBtn.MouseFilter = Control.MouseFilterEnum.Stop;

		UIStyle.ApplyButtonText(_deleteAllBtn, "DELETE ALL", 14);
		_deleteAllBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_deleteAllBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_deleteAllBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_deleteAllBtn.AddThemeConstantOverride("icon_max_width", 0);
		_deleteAllBtn.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f));
		_deleteAllBtn.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.5f, 0.5f));
		_deleteAllBtn.AddThemeColorOverride("font_pressed_color", new Color(0.9f, 0.3f, 0.3f));

		var bgTexture = GetNodeOrNull<TextureRect>("BackgroundTexture");
		if (bgTexture != null)
		{
			bgTexture.Texture = GD.Load<Texture2D>("res://Assets/UI/replays_bg.jpg");
			bgTexture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			bgTexture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		}
		else
		{
			var bgPanel = GetNodeOrNull<Panel>("Background");
			bgPanel?.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		}

		var panelTexture = GetNodeOrNull<TextureRect>("CenterContainer/MainFrame/PanelTexture");
		if (panelTexture != null)
		{
			panelTexture.Texture = GD.Load<Texture2D>("res://Assets/UI/replays_panel.png");
			panelTexture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			panelTexture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			panelTexture.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
		}

		var listFrame = GetNodeOrNull<PanelContainer>("CenterContainer/MainFrame/ContentMargin/VBox/ListFrame")
			?? GetNodeOrNull<PanelContainer>("CenterContainer/MainFrame/VBox/ListFrame");
		listFrame?.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());

		Resized += UpdatePanelSize;
		UpdatePanelSize();

		PopulateReplaysList();
	}

	private void UpdatePanelSize()
	{
		Vector2 viewportSize = GetViewportRect().Size;
		float targetAspect = 1.66f;
		float width = Mathf.Clamp(viewportSize.X * 0.82f, 900f, 1500f);
		float height = width / targetAspect;

		if (height > viewportSize.Y * 0.85f)
		{
			height = viewportSize.Y * 0.85f;
			width = height * targetAspect;
		}

		Control mainFrame = GetNodeOrNull<Control>("CenterContainer/MainFrame");
		if (mainFrame != null)
		{
			mainFrame.CustomMinimumSize = new Vector2(width, height);
		}
	}

	private void OnBackPressed()
	{
		UIManager.Instance?.PlayClickSound();
		UIManager.Instance?.TransitionTo(GameScreen.MainMenu);
	}

	private void PopulateReplaysList()
	{
		foreach (Node child in _listContainer.GetChildren())
		{
			_listContainer.RemoveChild(child);
			child.QueueFree();
		}

		string replayDir = ProjectSettings.GlobalizePath("user://replays");
		if (!Directory.Exists(replayDir))
		{
			Directory.CreateDirectory(replayDir);
		}

		var files = Directory.GetFiles(replayDir, "*.rep");
		var listFrame = GetNodeOrNull<PanelContainer>("CenterContainer/MainFrame/ContentMargin/VBox/ListFrame")
			?? GetNodeOrNull<PanelContainer>("CenterContainer/MainFrame/VBox/ListFrame");

		if (files.Length == 0)
		{
			_noReplaysLabel.Visible = true;
			_deleteAllBtn.Visible = false;
			if (listFrame != null) listFrame.Visible = false;
			return;
		}

		_noReplaysLabel.Visible = false;
		_deleteAllBtn.Visible = true;
		if (listFrame != null) listFrame.Visible = true;

		var sortedFiles = new List<string>(files);
		sortedFiles.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));

		int addedCount = 0;
		foreach (var filePath in sortedFiles)
		{
			try
			{
				int totalTicks;
				var header = ReplayPlaybackManager.ReadReplayHeader(filePath, out totalTicks);
				if (header == null)
				{
					continue;
				}

				var row = CreateReplayRow(filePath, header, totalTicks);
				_listContainer.AddChild(row);
				addedCount++;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReplayListPanel] Error processing replay file '{filePath}': {ex}");
			}
		}

		if (addedCount == 0)
		{
			_noReplaysLabel.Visible = true;
			_deleteAllBtn.Visible = false;
			if (listFrame != null) listFrame.Visible = false;
		}
	}

	private PanelContainer CreateReplayRow(string path, ReplayHeader header, int totalTicks)
	{
		var panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(0, 70);
		panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.15f, 0.15f, 0.15f, 0.6f);
		style.SetBorderWidthAll(1);
		style.BorderColor = UIStyle.ColorBronze;
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		style.ContentMarginLeft = 15;
		style.ContentMarginRight = 15;
		style.ContentMarginTop = 10;
		style.ContentMarginBottom = 10;
		panel.AddThemeStyleboxOverride("panel", style);

		var hBox = new HBoxContainer();
		hBox.AddThemeConstantOverride("separation", 20);
		panel.AddChild(hBox);

		var infoVBox = new VBoxContainer();
		infoVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		hBox.AddChild(infoVBox);

		var mapLabel = new Label();
		string mapName = (header.MapName ?? "UNKNOWN").ToUpper();
		mapLabel.Text = mapName;
		mapLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		mapLabel.AddThemeFontSizeOverride("font_size", 16);
		mapLabel.VerticalAlignment = VerticalAlignment.Center;
		infoVBox.AddChild(mapLabel);

		var metaLabel = new Label();
		int totalSec = totalTicks / 30;
		string durationStr = string.Format("{0:D2}:{1:D2}", totalSec / 60, totalSec % 60);
		var dt = DateTimeOffset.FromUnixTimeSeconds(header.Timestamp).LocalDateTime;
		string playersStr = "";
		if (header.Players != null)
		{
			var names = new List<string>();
			foreach (var p in header.Players) names.Add(p.Name);
			playersStr = string.Join(" vs ", names);
		}
		metaLabel.Text = $"{dt:yyyy-MM-dd HH:mm}  |  {durationStr}  |  {playersStr}";
		metaLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		metaLabel.AddThemeFontSizeOverride("font_size", 12);
		metaLabel.VerticalAlignment = VerticalAlignment.Center;
		infoVBox.AddChild(metaLabel);

		var playBtn = new Button();
		playBtn.CustomMinimumSize = new Vector2(100, 40);
		playBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		UIStyle.ApplyButtonText(playBtn, "WATCH", 13);
		playBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		playBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		playBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		playBtn.AddThemeConstantOverride("icon_max_width", 0);
		
		playBtn.Pressed += () => OnPlayReplayPressed(path);
		playBtn.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
		hBox.AddChild(playBtn);

		var deleteBtn = new Button();
		deleteBtn.CustomMinimumSize = new Vector2(100, 40);
		deleteBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		UIStyle.ApplyButtonText(deleteBtn, "DELETE", 13);
		deleteBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		deleteBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		deleteBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		deleteBtn.AddThemeConstantOverride("icon_max_width", 0);
		deleteBtn.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f));
		deleteBtn.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.5f, 0.5f));
		deleteBtn.AddThemeColorOverride("font_pressed_color", new Color(0.9f, 0.3f, 0.3f));

		deleteBtn.Pressed += () => OnDeleteReplayPressed(path);
		deleteBtn.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
		hBox.AddChild(deleteBtn);

		return panel;
	}

	private void ShowVersionMismatchPopup(string replayVersion, string currentVersion, string path)
	{
		var warningPopup = new Panel();
		warningPopup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		warningPopup.AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		AddChild(warningPopup);

		var cardPanel = new Panel();
		cardPanel.CustomMinimumSize = new Vector2(500, 260);
		cardPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		cardPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		warningPopup.AddChild(cardPanel);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		vbox.CustomMinimumSize = new Vector2(450, 220);
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		cardPanel.AddChild(vbox);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, Tr("VERSION MISMATCH"), 20);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		vbox.AddChild(titleLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

		bool versionExistsLocally = System.IO.File.Exists(LobbyManager.GetVersionExecutablePath(replayVersion));

		var descLabel = new Label();
		string promptText = versionExistsLocally 
			? Tr("The running game version doesn't match the replay file. Re-launch game with correct version?") 
			: Tr("The required game version is not installed. Download it?");
		descLabel.Text = $"{string.Format(Tr("Replay Version: {0}"), replayVersion)}\n{string.Format(Tr("Current Version: {0}"), currentVersion)}\n\n{promptText}";
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.AddThemeFontSizeOverride("font_size", 14);
		descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(descLabel);

		vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 15) });
		
		var hBox = new HBoxContainer();
		hBox.Alignment = BoxContainer.AlignmentMode.Center;
		hBox.AddThemeConstantOverride("separation", 20);
		vbox.AddChild(hBox);

		var cancelBtn = new Button();
		cancelBtn.Flat = false;
		cancelBtn.AddThemeConstantOverride("icon_max_width", 0);
		cancelBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		cancelBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		cancelBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		cancelBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(cancelBtn, Tr("CANCEL"), 14);
		cancelBtn.CustomMinimumSize = new Vector2(160, 40);
		cancelBtn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			warningPopup.QueueFree();
		};
		hBox.AddChild(cancelBtn);

		var okBtn = new Button();
		okBtn.Flat = false;
		okBtn.AddThemeConstantOverride("icon_max_width", 0);
		okBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		okBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		okBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		okBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(okBtn, versionExistsLocally ? Tr("RELAUNCH") : Tr("DOWNLOAD"), 14);
		okBtn.CustomMinimumSize = new Vector2(160, 40);
		okBtn.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			warningPopup.QueueFree();
			
			if (versionExistsLocally)
			{
				string targetExe = LobbyManager.GetVersionExecutablePath(replayVersion);
				OS.CreateProcess(targetExe, new string[] {});
				GetTree().Quit();
			}
			else
			{
				OS.ShellOpen("https://github.com/RealmArcade/Realm/releases");
			}
		};
		hBox.AddChild(okBtn);
	}

	private void OnPlayReplayPressed(string path)
	{
		UIManager.Instance?.PlayClickSound();

		int totalTicks;
		var header = ReplayPlaybackManager.ReadReplayHeader(path, out totalTicks);
		if (header != null && !string.IsNullOrEmpty(header.GameVersion) && header.GameVersion != RealmVersion.GameBinaryVersion)
		{
			ShowVersionMismatchPopup(header.GameVersion, RealmVersion.GameBinaryVersion, path);
			return;
		}

		bool ok = ReplayPlaybackManager.Instance.LoadReplay(path);
		if (ok)
		{
			if (LobbyManager.Instance != null)
			{
				if (ReplayPlaybackManager.Instance.Header != null)
				{
					LobbyManager.Instance.ActiveMapName = ReplayPlaybackManager.Instance.Header.MapName ?? "melee";
				}
				LobbyManager.Instance.IsGameStarted = true;
			}
			GetTree().ChangeSceneToFile("res://Main.tscn");
		}
		else
		{
			GD.PrintErr($"Failed to load replay: {path}");
		}
	}

	private void OnDeleteReplayPressed(string path)
	{
		ShowConfirmationDialog("Are you sure you want to delete this replay?", () =>
		{
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReplayListPanel] Failed to delete replay file '{path}': {ex}");
			}
			PopulateReplaysList();
		});
	}

	private void OnDeleteAllPressed()
	{
		ShowConfirmationDialog("Are you sure you want to delete all replays?", () =>
		{
			string replayDir = ProjectSettings.GlobalizePath("user://replays");
			if (Directory.Exists(replayDir))
			{
				var files = Directory.GetFiles(replayDir, "*.rep");
				foreach (var filePath in files)
				{
					try
					{
						if (File.Exists(filePath))
						{
							File.Delete(filePath);
						}
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[ReplayListPanel] Failed to delete replay file '{filePath}': {ex}");
					}
				}
			}
			PopulateReplaysList();
		});
	}

	private void ShowConfirmationDialog(string message, Action onConfirm)
	{
		var overlay = new ColorRect();
		overlay.Name = "ConfirmationOverlay";
		overlay.Color = new Color(0, 0, 0, 0.6f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(overlay);

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(400, 200);
		panel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		panel.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 20);
		panel.AddChild(vbox);

		var lblTitle = new Label();
		UIStyle.ApplyTitle(lblTitle, TranslationServer.Translate("CONFIRMATION REQUIRED"), 18);
		lblTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(lblTitle);

		var lblMsg = new Label();
		lblMsg.Text = TranslationServer.Translate(message);
		lblMsg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblMsg.HorizontalAlignment = HorizontalAlignment.Center;
		lblMsg.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		lblMsg.AddThemeFontSizeOverride("font_size", 14);
		vbox.AddChild(lblMsg);

		var hbox = new HBoxContainer();
		hbox.AddThemeConstantOverride("separation", 30);
		hbox.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(hbox);

		var btnConfirm = new Button();
		btnConfirm.CustomMinimumSize = new Vector2(100, 40);
		btnConfirm.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonText(btnConfirm, "YES", 13);
		btnConfirm.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btnConfirm.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btnConfirm.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btnConfirm.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			overlay.QueueFree();
			onConfirm?.Invoke();
		};
		btnConfirm.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
		hbox.AddChild(btnConfirm);

		var btnCancel = new Button();
		btnCancel.CustomMinimumSize = new Vector2(100, 40);
		btnCancel.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonText(btnCancel, "NO", 13);
		btnCancel.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btnCancel.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btnCancel.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btnCancel.Pressed += () =>
		{
			UIManager.Instance?.PlayClickSound();
			overlay.QueueFree();
		};
		btnCancel.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
		hbox.AddChild(btnCancel);
	}
}
