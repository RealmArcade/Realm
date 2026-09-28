using Godot;
using System;
using System.Threading.Tasks;
using Realm.Godot.ReplaySystem;
using Realm.Godot.Services;
using Realm.Shared;

public partial class UIManager : Control
{
	public static UIManager Instance { get; private set; }

	[Export] public PackedScene MainMenuScene;
	[Export] public PackedScene LobbyBrowserScene;
	[Export] public PackedScene LobbyRoomScene;
	[Export] public PackedScene SettingsScene;
	[Export] public PackedScene InGameHUDScene;
	[Export] public PackedScene GameOverScene;
	[Export] public PackedScene MapDiscoveryScene;
	[Export] public PackedScene MapDetailsScene;
	[Export] public PackedScene MapEditorHUDScene;
	[Export] public PackedScene ReplayListScene;
	[Export] public PackedScene LobbyCreateScene;
	[Export] public PackedScene StorageScene;

	private Control _currentScreen;
	private ColorRect _fadeOverlay;
	private AnimationPlayer _fadeAnim;
	private Label _watermark;
	private GameScreen _targetScreen;
	private bool _isVictory = true; // State passed to Game Over screen
	private bool _transitionInProgress = false;
	private GameScreen? _queuedScreen;
	private bool _queuedIsVictory;

	private MapData _selectedMapData;

	public void TransitionToMapDetails(MapData mapData)
	{
		_selectedMapData = mapData;
		TransitionTo(GameScreen.MapDetails);
	}

	private AudioStreamPlayer _musicPlayer;
	private AudioStreamPlayer _sfxPlayer;

	public override async void _Ready()
	{
		Instance = this;
		MouseFilter = MouseFilterEnum.Ignore;


		_musicPlayer = new AudioStreamPlayer();
		AddChild(_musicPlayer);

		_sfxPlayer = new AudioStreamPlayer();
		AddChild(_sfxPlayer);

		await ApplyStartupSettings();


		CreateFadeOverlay();

#if DEBUG
		_watermark = new Label();
		_watermark.Text = string.Format(TranslationServer.Translate("Realm {0}"), RealmVersion.GameBinaryVersion);
		_watermark.AddThemeColorOverride("font_color", new Color(1.0f, 1.0f, 1.0f, 0.5f));
		_watermark.AddThemeColorOverride("font_outline_color", new Color(0.0f, 0.0f, 0.0f, 0.8f));
		_watermark.AddThemeConstantOverride("outline_size", 4);
		_watermark.AddThemeFontSizeOverride("font_size", 14);
		_watermark.HorizontalAlignment = HorizontalAlignment.Right;
		_watermark.VerticalAlignment = VerticalAlignment.Bottom;
		_watermark.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
		_watermark.OffsetLeft = -250;
		_watermark.OffsetTop = -30;
		_watermark.OffsetRight = -10;
		_watermark.OffsetBottom = -10;
		_watermark.GrowHorizontal = GrowDirection.Begin;
		_watermark.GrowVertical = GrowDirection.Begin;
		_watermark.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(_watermark);
#endif

		GetTree().AutoAcceptQuit = false;

		if (LobbyManager.Instance != null && LobbyManager.Instance.IsGameStarted)
		{
			TransitionTo(GameScreen.InGameHUD);
		}
		else
		{
			TransitionTo(GameScreen.MainMenu);
		}
	}

	public override void _Notification(int what)
	{
		base._Notification(what);
		if (what == unchecked((int)NotificationWMCloseRequest))
		{
			if (MapEditorHUD.Instance != null && GodotObject.IsInstanceValid(MapEditorHUD.Instance) && MapEditorHUD.Instance.IsInsideTree())
			{
				MapEditorHUD.Instance.HandleQuitRequest();
			}
			else if (MapEditorHUD.IsTestMode && MapEditorHUD.HasUnsavedChangesStatic())
			{
				ShowConfirmationDialog(
					"You haven't saved yet",
					onConfirm: () => GetTree().Quit(),
					confirmText: "Quit",
					cancelText: "Stay"
				);
			}
			else
			{
				GetTree().Quit();
			}
		}
	}

	private bool _isApplyingWindowSettings = false;

	public async Task ApplyWindowSettings(WindowMode mode, int resIdx)
	{
		if (_isApplyingWindowSettings) return;
		_isApplyingWindowSettings = true;

		try
		{
			var tree = GetTree();
			if (tree == null) return;

			int currentScreen = GameSettings.GetSafeScreenIndex();
			Vector2I screenSize = DisplayServer.ScreenGetSize(currentScreen);
			Rect2I usable = DisplayServer.ScreenGetUsableRect(currentScreen);

			Vector2I targetRes;
			if (resIdx >= 0 && GameSettings.Resolutions != null && resIdx < GameSettings.Resolutions.Count)
			{
				targetRes = GameSettings.Resolutions[resIdx];
			}
			else if (GameSettings.ResolutionIdx >= 0 && GameSettings.Resolutions != null && GameSettings.ResolutionIdx < GameSettings.Resolutions.Count)
			{
				targetRes = GameSettings.Resolutions[GameSettings.ResolutionIdx];
			}
			else if (GameSettings.Resolutions != null && GameSettings.Resolutions.Count > 0)
			{
				targetRes = GameSettings.Resolutions[0];
			}
			else
			{
				targetRes = screenSize;
			}

			var currentMode = DisplayServer.WindowGetMode();

			switch (mode)
			{
				case WindowMode.Fullscreen:
					DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
					DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
					break;

				case WindowMode.Borderless:
					if (currentMode != DisplayServer.WindowMode.Windowed)
					{
						DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
						await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
						await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
					}

					currentScreen = GameSettings.GetSafeScreenIndex();
					screenSize = DisplayServer.ScreenGetSize(currentScreen);
					Vector2I screenPos = DisplayServer.ScreenGetPosition(currentScreen);

					DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
					DisplayServer.WindowSetPosition(screenPos);
					DisplayServer.WindowSetSize(screenSize);
					break;

				case WindowMode.Windowed:
				default:
					if (currentMode != DisplayServer.WindowMode.Windowed)
					{
						DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
						await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
						await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
					}

					currentScreen = GameSettings.GetSafeScreenIndex();
					usable = DisplayServer.ScreenGetUsableRect(currentScreen);

					DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);

					if (targetRes.X >= usable.Size.X || targetRes.Y >= usable.Size.Y)
					{
						DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);
					}
					else
					{
						DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
						Vector2I windowedPos = usable.Position + (usable.Size - targetRes) / 2;
						windowedPos.X = Math.Max(windowedPos.X, usable.Position.X);
						windowedPos.Y = Math.Max(windowedPos.Y, usable.Position.Y);

						DisplayServer.WindowSetSize(targetRes);
						DisplayServer.WindowSetPosition(windowedPos);
					}
					break;
			}
		}
		finally
		{
			_isApplyingWindowSettings = false;
		}
	}

	private async Task ApplyStartupSettings()
	{
		GameSettings.Load();
		GameSettings.ApplyGraphicsSettings(this);
		LocalizationManager.SetupTranslations();

		await ApplyWindowSettings(GameSettings.WindowModeIdx, GameSettings.ResolutionIdx);

		if (GameSettings.Vsync)
		{
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
		}
		else
		{
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		}

		UpdateAudioVolumes();
	}

	public void UpdateAudioVolumes()
	{
		if (_musicPlayer != null)
		{
			float master = GameSettings.MasterVolume / 100f;
			float music = GameSettings.MusicVolume / 100f;
			float combined = master * music;
			_musicPlayer.VolumeDb = combined <= 0f ? -80f : Mathf.LinearToDb(combined);
		}

		if (_sfxPlayer != null)
		{
			float master = GameSettings.MasterVolume / 100f;
			float sfx = GameSettings.SfxVolume / 100f;
			float combined = master * sfx;
			_sfxPlayer.VolumeDb = combined <= 0f ? -80f : Mathf.LinearToDb(combined);
		}
	}

	private void CreateFadeOverlay()
	{
		_fadeOverlay = new ColorRect();
		_fadeOverlay.Color = new Color(0, 0, 0, 1); // Black
		_fadeOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_fadeOverlay.MouseFilter = MouseFilterEnum.Ignore; // Let clicks pass when transparent
		AddChild(_fadeOverlay);

		_fadeAnim = new AnimationPlayer();
		AddChild(_fadeAnim);


		var library = new AnimationLibrary();
		

		var animFadeIn = new Animation();
		int trackId = animFadeIn.AddTrack(Animation.TrackType.Value);
		animFadeIn.TrackSetPath(trackId, $"{_fadeOverlay.GetPath()}:color");
		animFadeIn.TrackInsertKey(trackId, 0.0f, new Color(0, 0, 0, 0));
		animFadeIn.TrackInsertKey(trackId, 0.3f, new Color(0, 0, 0, 1));
		library.AddAnimation("fade_in", animFadeIn);


		var animFadeOut = new Animation();
		trackId = animFadeOut.AddTrack(Animation.TrackType.Value);
		animFadeOut.TrackSetPath(trackId, $"{_fadeOverlay.GetPath()}:color");
		animFadeOut.TrackInsertKey(trackId, 0.0f, new Color(0, 0, 0, 1));
		animFadeOut.TrackInsertKey(trackId, 0.3f, new Color(0, 0, 0, 0));
		library.AddAnimation("fade_out", animFadeOut);

		_fadeAnim.AddAnimationLibrary("", library);
		_fadeOverlay.Color = new Color(0, 0, 0, 0); // Start clear
	}

	public void TransitionTo(GameScreen screen, bool isVictory = true)
	{
		if (screen == GameScreen.GameOver && (_targetScreen == GameScreen.GameOver || _currentScreen is GameOver))
		{
			return;
		}
		_targetScreen = screen;
		_isVictory = isVictory;

		if (screen == GameScreen.GameOver || screen == GameScreen.MainMenu || screen == GameScreen.MapEditorHUD || screen == GameScreen.LobbyBrowser || screen == GameScreen.LobbyRoom || screen == GameScreen.ReplayList)
		{
			if (ReplayPlaybackManager.Instance.IsPlayingReplay)
			{
				ReplayPlaybackManager.Instance.StopReplay();
				GameHost.Instance?.ResetWorldAndState();
			}
			GameHost.Instance?.StopRecording();
		}

		if (_transitionInProgress)
		{
			_queuedScreen = screen;
			_queuedIsVictory = isVictory;
			return;
		}

		_transitionInProgress = true;


		_fadeOverlay.MouseFilter = MouseFilterEnum.Stop;


		_fadeAnim.Play("fade_in");


		if (screen == GameScreen.MainMenu || screen == GameScreen.LobbyBrowser || screen == GameScreen.LobbyRoom || screen == GameScreen.Settings || screen == GameScreen.MapDiscovery || screen == GameScreen.CreatorDiscovery || screen == GameScreen.MapDetails || screen == GameScreen.Storage)
		{
			PlayMusic("res://Assets/Audio/Music/enchanted_realm.ogg");
		}
		else if (screen == GameScreen.InGameHUD)
		{
			PlayMusic("res://Assets/Audio/Music/battle_anthem.ogg");
		}
		else if (screen == GameScreen.GameOver)
		{
			StopMusic();
			if (_sfxPlayer != null)
			{
				var soundFile = isVictory ? "res://Assets/Audio/UI/victory_theme_sting.ogg" : "res://Assets/Audio/UI/defeat_drone_low.ogg";
				_sfxPlayer.Stream = GD.Load<AudioStream>(soundFile);
				_sfxPlayer.Play();
			}
		}
		

		var timer = GetTree().CreateTimer(0.3f);
		timer.Timeout += OnFadeInComplete;
	}

	public void PlayMusic(string path)
	{
		if (_musicPlayer != null)
		{
			if (_musicPlayer.Stream?.ResourcePath == path && _musicPlayer.Playing)
				return;
			
			var stream = GD.Load<AudioStream>(path);
			if (stream != null)
			{
				_musicPlayer.Stream = stream;
				_musicPlayer.Play();
			}
		}
	}

	public void StopMusic()
	{
		_musicPlayer?.Stop();
	}

	public void PlayClickSound()
	{
		if (_sfxPlayer != null)
		{
			var stream = GD.Load<AudioStream>("res://Assets/Audio/UI/click_confirm_heavy.ogg");
			if (stream != null)
			{
				_sfxPlayer.Stream = stream;
				_sfxPlayer.Play();
			}
		}
	}

	public void PlayHoverSound()
	{
		if (_sfxPlayer != null)
		{
			var stream = GD.Load<AudioStream>("res://Assets/Audio/UI/hover_highlight_sparkle.ogg");
			if (stream != null)
			{
				_sfxPlayer.Stream = stream;
				_sfxPlayer.Play();
			}
		}
	}

	public void PlayWarningSound()
	{
		if (_sfxPlayer != null)
		{
			var stream = GD.Load<AudioStream>("res://Assets/Audio/UI/alert_warning_buzz.ogg");
			if (stream != null)
			{
				_sfxPlayer.Stream = stream;
				_sfxPlayer.Play();
			}
		}
	}

	private void OnFadeInComplete()
	{

		if (_currentScreen != null)
		{
			if (_currentScreen is MapEditorHUD)
			{
				GameHost.Instance?.ExitMapEditorMode();
			}
			_currentScreen.QueueFree();
			_currentScreen = null;
		}


		PackedScene targetScene = null;
		switch (_targetScreen)
		{
			case GameScreen.MainMenu:
				targetScene = MainMenuScene ?? GD.Load<PackedScene>("res://UI/MainMenu.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				if (LobbyManager.Instance != null)
				{
					LobbyManager.Instance.Disconnect();
				}
				break;
			case GameScreen.LobbyBrowser:
				targetScene = LobbyBrowserScene ?? GD.Load<PackedScene>("res://UI/LobbyBrowser.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
			case GameScreen.LobbyCreate:
				targetScene = LobbyCreateScene ?? GD.Load<PackedScene>("res://UI/LobbyCreate.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
			case GameScreen.LobbyRoom:
				targetScene = LobbyRoomScene ?? GD.Load<PackedScene>("res://UI/LobbyRoom.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
			case GameScreen.Settings:
				targetScene = SettingsScene ?? GD.Load<PackedScene>("res://UI/SettingsMenu.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
			case GameScreen.InGameHUD:
				targetScene = InGameHUDScene ?? GD.Load<PackedScene>("res://UI/InGameHUD.tscn");

				Input.MouseMode = Input.MouseModeEnum.Visible; // Keep visible for HUD interaction
				break;
			case GameScreen.GameOver:
				targetScene = GameOverScene ?? GD.Load<PackedScene>("res://UI/GameOver.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
						case GameScreen.CreatorDiscovery:
				targetScene = GD.Load<PackedScene>("res://UI/CreatorDiscovery.tscn");
				break;
			case GameScreen.MapDiscovery:
				targetScene = MapDiscoveryScene ?? GD.Load<PackedScene>("res://UI/MapDiscovery.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
			case GameScreen.MapDetails:
				targetScene = MapDetailsScene ?? GD.Load<PackedScene>("res://UI/MapDetails.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
			case GameScreen.MapEditorHUD:
				targetScene = MapEditorHUDScene ?? GD.Load<PackedScene>("res://UI/MapEditorHUD.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;

				GameHost.Instance?.StartMapEditorMode();
				break;
			case GameScreen.ReplayList:
				targetScene = ReplayListScene ?? GD.Load<PackedScene>("res://UI/ReplayListPanel.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
			case GameScreen.Storage:
				targetScene = StorageScene ?? GD.Load<PackedScene>("res://UI/StorageMenu.tscn");
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
		}

		if (targetScene != null)
		{
			_currentScreen = targetScene.Instantiate<Control>();
			AddChild(_currentScreen);

			if (_targetScreen == GameScreen.InGameHUD && ReplayPlaybackManager.Instance.IsPlayingReplay)
			{
				GameHost.Instance?.StopRecording();
				var panelScene = GD.Load<PackedScene>("res://UI/ReplayViewerPanel.tscn");
				if (panelScene != null)
				{
					var panel = panelScene.Instantiate<Control>();
					_currentScreen.AddChild(panel);
				}
			}
			

			MoveChild(_fadeOverlay, GetChildCount() - 1);
#if DEBUG
			if (_watermark != null)
			{
				MoveChild(_watermark, GetChildCount() - 1);
			}
#endif


			if (_currentScreen is GameOver gameOver)
			{
				gameOver.SetStatus(_isVictory);
			}
			else if (_currentScreen is MapDetails mapDetails)
			{
				mapDetails.SetMapData(_selectedMapData);
			}
		}


		_fadeAnim.Play("fade_out");
		
		var timer = GetTree().CreateTimer(0.3f);
		timer.Timeout += () => 
		{
			_fadeOverlay.MouseFilter = MouseFilterEnum.Ignore; // Allow clicks again
			_transitionInProgress = false;
			if (_queuedScreen.HasValue)
			{
				GameScreen next = _queuedScreen.Value;
				bool vic = _queuedIsVictory;
				_queuedScreen = null;
				TransitionTo(next, vic);
			}
		};
	}


	public void OpenSettingsOverlay()
	{
		if (SettingsMenu.IsOpen) return;

		var settingsPopup = GD.Load<PackedScene>("res://UI/SettingsMenu.tscn").Instantiate<SettingsMenu>();
		settingsPopup.IsOverlay = true;
		AddChild(settingsPopup);
		MoveChild(settingsPopup, GetChildCount() - 1);
		if (_fadeOverlay != null)
		{
			MoveChild(_fadeOverlay, GetChildCount() - 1); // Keep fade overlay at the very top
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Keycode == Key.F8)
		{
			GetViewport().SetInputAsHandled();
		}
	}

	public void ShowConfirmationDialog(string message, Action onConfirm, string confirmText = "YES", string cancelText = "NO", Action onCancel = null, bool showCancel = true)
	{
		var overlay = new ColorRect();
		overlay.Name = "ConfirmationOverlay";
		overlay.Color = new Color(0, 0, 0, 0.5f);
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
		vbox.AddThemeConstantOverride("separation", 15);
		panel.AddChild(vbox);

		var lblTitle = new Label();
		UIStyle.ApplyTitle(lblTitle, TranslationServer.Translate(showCancel ? "CONFIRMATION REQUIRED" : "NOTIFICATION"), 18);
		lblTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(lblTitle);

		var lblMsg = new Label();
		lblMsg.Text = TranslationServer.Translate(message);
		lblMsg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblMsg.HorizontalAlignment = HorizontalAlignment.Center;
		lblMsg.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		lblMsg.AddThemeFontSizeOverride("font_size", 13);
		vbox.AddChild(lblMsg);

		var hbox = new HBoxContainer();
		hbox.AddThemeConstantOverride("separation", 20);
		hbox.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(hbox);

		var btnConfirm = new Button();
		btnConfirm.Set("icon_max_width", 0);
		UIStyle.ApplyButtonText(btnConfirm, TranslationServer.Translate(confirmText), 13);
		btnConfirm.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		btnConfirm.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		btnConfirm.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		btnConfirm.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		btnConfirm.Pressed += () =>
		{
			overlay.QueueFree();
			onConfirm?.Invoke();
		};
		hbox.AddChild(btnConfirm);

		if (showCancel)
		{
			var btnCancel = new Button();
			btnCancel.Set("icon_max_width", 0);
			UIStyle.ApplyButtonText(btnCancel, TranslationServer.Translate(cancelText), 13);
			btnCancel.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
			btnCancel.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
			btnCancel.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
			btnCancel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
			Action cancelAction = () =>
			{
				overlay.QueueFree();
				onCancel?.Invoke();
			};
			overlay.SetMeta("CancelAction", Callable.From(cancelAction));
			btnCancel.Pressed += () =>
			{
				cancelAction();
			};
			hbox.AddChild(btnCancel);
		}
	}

	public void PromptAndImportMapArchive(Action<string, string>? onSuccess = null)
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Map Package (.rmap)"),
			OS.GetSystemDir(OS.SystemDir.Documents),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.rmap ; Realm Map Package (*.rmap)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					string selectedPath = selectedPaths[0];
					_ = ImportMapWithProgressAsync(selectedPath, onSuccess);
				}
			})
		);
	}

	public async Task<(bool Success, string Message, string? MapTitle, string? MapVersion)> ImportMapWithProgressAsync(
		string archivePath,
		Action<string, string>? onSuccess = null)
	{
		var overlay = new ColorRect();
		overlay.Name = "ImportMapProgressOverlay";
		overlay.Color = new Color(0, 0, 0, 0.65f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.ZIndex = 1100;
		AddChild(overlay);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(500, 220);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 12);
		panel.AddChild(vbox);

		var titleLabel = new Label();
		UIStyle.ApplyTitle(titleLabel, "📥 " + TranslationServer.Translate("IMPORTING MAP ARCHIVE"), 20);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var fileNameLabel = new Label();
		fileNameLabel.Text = System.IO.Path.GetFileName(archivePath);
		fileNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		fileNameLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		fileNameLabel.AddThemeFontSizeOverride("font_size", 13);
		vbox.AddChild(fileNameLabel);

		var progressBar = new ProgressBar();
		progressBar.CustomMinimumSize = new Vector2(440, 22);
		progressBar.MinValue = 0;
		progressBar.MaxValue = 100;
		progressBar.Value = 0;
		vbox.AddChild(progressBar);

		var statusLabel = new Label();
		statusLabel.Text = TranslationServer.Translate("Extracting and validating archive contents...");
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AddThemeFontSizeOverride("font_size", 13);
		statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(statusLabel);

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var mapStorageService = ServiceLocator.Get<MapStorageService>();
		(bool Success, string Message, string? MapTitle, string? MapVersion) result;

		try
		{
			result = await Task.Run(() => mapStorageService.ImportMapAsync(
				archivePath,
				progress =>
				{
					Callable.From(() =>
					{
						if (GodotObject.IsInstanceValid(progressBar))
						{
							progressBar.Value = Mathf.Clamp(progress * 100.0, 0, 100);
						}
						if (GodotObject.IsInstanceValid(statusLabel))
						{
							statusLabel.Text = string.Format(TranslationServer.Translate("Importing assets... {0}%"), (int)(progress * 100));
						}
					}).CallDeferred();
				}
			));
		}
		catch (Exception ex)
		{
			result = (false, ex.Message, null, null);
		}
		finally
		{
			if (GodotObject.IsInstanceValid(overlay))
			{
				overlay.QueueFree();
			}
		}

		if (result.Success)
		{
			onSuccess?.Invoke(result.MapTitle ?? string.Empty, result.MapVersion ?? string.Empty);

			ShowConfirmationDialog(
				TranslationServer.Translate("Map imported successfully."),
				() => { },
				confirmText: "OK",
				showCancel: false
			);
		}
		else
		{
			ShowConfirmationDialog(
				string.Format(TranslationServer.Translate("Import failed: {0}"), result.Message),
				() => { },
				confirmText: "OK",
				showCancel: false
			);
		}

		return result;
	}
}
