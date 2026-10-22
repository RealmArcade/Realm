using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Shared;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.Services;

namespace Realm.Client.UI;

[GlobalClass]
public partial class ReplayListPanel : Control
{
	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

	private VBoxContainer _listContainer;
	private Button _backBtn;
	private Button _deleteAllBtn;
	private Label _titleLabel;
	private Label _noReplaysLabel;

	public override void _Ready()
	{
		InitializeNodes();
		ConnectSignals();
		ApplyStyles();
		SetupBackgrounds();

		Resized += UpdatePanelSize;
		UpdatePanelSize();

		PopulateReplaysList();
	}

	private void InitializeNodes()
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
	}

	private void ConnectSignals()
	{
		_backBtn.Pressed += OnBackPressed;
		_backBtn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();

		_deleteAllBtn.Pressed += OnDeleteAllPressed;
		_deleteAllBtn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}

	private void ApplyStyles()
	{
		UIStyle.ApplyTitle(_titleLabel, "REPLAYS", 28);
		UIStyle.ApplyButtonText(_backBtn, "BACK", 14);
		_backBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_backBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_backBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_backBtn.AddThemeConstantOverride("icon_max_width", 0);
		_backBtn.MouseFilter = MouseFilterEnum.Stop;

		UIStyle.ApplyButtonText(_deleteAllBtn, "DELETE ALL", 14);
		_deleteAllBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_deleteAllBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_deleteAllBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_deleteAllBtn.AddThemeConstantOverride("icon_max_width", 0);
		_deleteAllBtn.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f));
		_deleteAllBtn.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.5f, 0.5f));
		_deleteAllBtn.AddThemeColorOverride("font_pressed_color", new Color(0.9f, 0.3f, 0.3f));
	}

	private void SetupBackgrounds()
	{
		var bgTexture = GetNodeOrNull<TextureRect>("BackgroundTexture");
		if (bgTexture != null)
		{
			bgTexture.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/replays_bg.webp");
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
			panelTexture.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/replays_panel.webp");
			panelTexture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			panelTexture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			panelTexture.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		}

		var listFrame = GetNodeOrNull<PanelContainer>("CenterContainer/MainFrame/ContentMargin/VBox/ListFrame")
		                ?? GetNodeOrNull<PanelContainer>("CenterContainer/MainFrame/VBox/ListFrame");
		listFrame?.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
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
		GameContext.UserInterface?.PlayClickSound();
		GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
	}

	private void PopulateReplaysList()
	{
		ClearListContainer();

		string replayDir = ProjectSettings.GlobalizePath("user://replays");
		EnsureDirectoryExists(replayDir);

		var files = Directory.GetFiles(replayDir, "*.rep");
		var listFrame = GetNodeOrNull<PanelContainer>("CenterContainer/MainFrame/ContentMargin/VBox/ListFrame")
		                ?? GetNodeOrNull<PanelContainer>("CenterContainer/MainFrame/VBox/ListFrame");

		if (files.Length == 0)
		{
			SetNoReplaysState(listFrame);
			return;
		}

		SetHasReplaysState(listFrame);

		var sortedFiles = new List<string>(files);
		sortedFiles.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));

		int addedCount = ProcessAndAddReplayFiles(sortedFiles);

		if (addedCount == 0)
		{
			SetNoReplaysState(listFrame);
		}
	}

	private void ClearListContainer()
	{
		foreach (Node child in _listContainer.GetChildren())
		{
			_listContainer.RemoveChild(child);
			child.QueueFree();
		}
	}

	private void EnsureDirectoryExists(string dirPath)
	{
		if (!Directory.Exists(dirPath))
		{
			Directory.CreateDirectory(dirPath);
		}
	}

	private void SetNoReplaysState(PanelContainer listFrame)
	{
		_noReplaysLabel.Visible = true;
		_deleteAllBtn.Visible = false;
		if (listFrame != null) listFrame.Visible = false;
	}

	private void SetHasReplaysState(PanelContainer listFrame)
	{
		_noReplaysLabel.Visible = false;
		_deleteAllBtn.Visible = true;
		if (listFrame != null) listFrame.Visible = true;
	}

	private int ProcessAndAddReplayFiles(List<string> sortedFiles)
	{
		int addedCount = 0;
		foreach (var filePath in sortedFiles)
		{
			if (TryProcessReplayFile(filePath))
			{
				addedCount++;
			}
		}

		return addedCount;
	}

	private bool TryProcessReplayFile(string filePath)
	{
		try
		{
			int totalTicks;
			var header = ReplayPlaybackManager.ReadReplayHeader(filePath, out totalTicks);
			if (header == null)
			{
				return false;
			}

			var row = CreateReplayRow(filePath, header, totalTicks);
			_listContainer.AddChild(row);
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReplayListPanel] Error processing replay file '{filePath}': {ex}");
			return false;
		}
	}

	private PanelContainer CreateReplayRow(string path, ReplayHeader header, int totalTicks)
	{
		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
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
		hBox.Name = "HBox_HBoxContainer";
		hBox.AddThemeConstantOverride("separation", 20);
		panel.AddChild(hBox);

		var infoVBox = new VBoxContainer();
		infoVBox.Name = "InfoVBox_VBoxContainer";
		infoVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		hBox.AddChild(infoVBox);

		var mapLabel = new Label();
		mapLabel.Name = "MapLabel_Label";
		string mapName = (header.MapName ?? "UNKNOWN").ToUpper();
		mapLabel.Text = mapName;
		mapLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		mapLabel.AddThemeFontSizeOverride("font_size", 16);
		mapLabel.VerticalAlignment = VerticalAlignment.Center;
		infoVBox.AddChild(mapLabel);

		var metaLabel = new Label();
		metaLabel.Name = "MetaLabel_Label";
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
		playBtn.Name = "PlayBtn_Button";
		playBtn.CustomMinimumSize = new Vector2(100, 40);
		playBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		UIStyle.ApplyButtonText(playBtn, "WATCH", 13);
		playBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		playBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		playBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		playBtn.AddThemeConstantOverride("icon_max_width", 0);

		playBtn.Pressed += () => OnPlayReplayPressed(path);
		playBtn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		hBox.AddChild(playBtn);

		var deleteBtn = new Button();
		deleteBtn.Name = "DeleteBtn_Button";
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
		deleteBtn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		hBox.AddChild(deleteBtn);

		return panel;
	}

	private void ShowVersionMismatchPopup(string replayVersion, string currentVersion, string path)
	{
		bool versionExistsLocally = File.Exists(LobbyManager.GetVersionExecutablePath(replayVersion));

		string promptText = versionExistsLocally
			? Tr("The running game version doesn't match the replay file. Re-launch game with correct version?")
			: Tr("The required game version is not installed. Download it?");
		string message = $"{string.Format(Tr("Replay Version: {0}"), replayVersion)}\n{string.Format(Tr("Current Version: {0}"), currentVersion)}\n\n{promptText}";

		GameContext.UserInterface?.ShowConfirmationDialog(message, () =>
		{
			if (versionExistsLocally)
			{
				string targetExe = LobbyManager.GetVersionExecutablePath(replayVersion);
				OS.CreateProcess(targetExe, new string[] { });
				GetTree().Quit();
			}
			else
			{
				OS.ShellOpen("https://github.com/RealmArcade/Realm/releases");
			}
		}, confirmText: versionExistsLocally ? "RELAUNCH" : "DOWNLOAD", titleOverride: "VERSION MISMATCH");
	}

	private void OnPlayReplayPressed(string path)
	{
		GameContext.UserInterface?.PlayClickSound();

		int totalTicks;
		var header = ReplayPlaybackManager.ReadReplayHeader(path, out totalTicks);
		if (header != null && !string.IsNullOrEmpty(header.GameVersion) && header.GameVersion != RealmVersion.GameBinaryVersion)
		{
			ShowVersionMismatchPopup(header.GameVersion, RealmVersion.GameBinaryVersion, path);
			return;
		}

		bool ok = GameContext.ReplayPlayback.LoadReplay(path);
		if (ok)
		{
			if (GameContext.Lobby != null)
			{
				if (GameContext.ReplayPlayback.Header != null)
				{
					GameContext.Lobby.ActiveMapName = GameContext.ReplayPlayback.Header.MapName ?? "melee";
				}

				GameContext.Lobby.IsGameStarted = true;
			}

			GetTree().ChangeSceneToFile("res://GameHost.tscn");
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
			GameContext.UserInterface?.ShowConfirmationDialog(message, onConfirm);
	}
}
