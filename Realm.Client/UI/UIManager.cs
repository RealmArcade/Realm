using Godot;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Shared;
using System;
using System.Threading.Tasks;

namespace Realm.Client.UI;

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

		if (Network.LobbyManager.Instance != null && Network.LobbyManager.Instance.IsGameStarted)
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
		if (what == unchecked((int)NotificationWMCloseRequest) ||
		    what == unchecked((int)NotificationApplicationPaused) ||
		    what == unchecked((int)NotificationApplicationFocusOut))
		{
			LocalizationManager.FlushPendingWrites();
		}

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
			Vector2I targetRes = GetTargetResolution(resIdx, screenSize);
			var currentMode = DisplayServer.WindowGetMode();

			switch (mode)
			{
				case WindowMode.Fullscreen:
					DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
					DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
					break;
				case WindowMode.Borderless:
					await ApplyBorderlessMode(tree, currentMode);
					break;
				case WindowMode.Windowed:
				default:
					await ApplyWindowedMode(tree, currentMode, targetRes);
					break;
			}
		}
		finally
		{
			_isApplyingWindowSettings = false;
		}
	}

	private Vector2I GetTargetResolution(int resIdx, Vector2I defaultSize)
	{
		if (GameSettings.Resolutions == null || GameSettings.Resolutions.Count == 0)
			return defaultSize;

		if (resIdx >= 0 && resIdx < GameSettings.Resolutions.Count)
			return GameSettings.Resolutions[resIdx];

		if (GameSettings.ResolutionIdx >= 0 && GameSettings.ResolutionIdx < GameSettings.Resolutions.Count)
			return GameSettings.Resolutions[GameSettings.ResolutionIdx];

		return GameSettings.Resolutions[0];
	}

	private async Task ApplyBorderlessMode(SceneTree tree, DisplayServer.WindowMode currentMode)
	{
		if (currentMode != DisplayServer.WindowMode.Windowed)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		}

		int currentScreen = GameSettings.GetSafeScreenIndex();
		Vector2I screenSize = DisplayServer.ScreenGetSize(currentScreen);
		Vector2I screenPos = DisplayServer.ScreenGetPosition(currentScreen);

		DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
		DisplayServer.WindowSetPosition(screenPos);
		DisplayServer.WindowSetSize(screenSize);
	}

	private async Task ApplyWindowedMode(SceneTree tree, DisplayServer.WindowMode currentMode, Vector2I targetRes)
	{
		if (currentMode != DisplayServer.WindowMode.Windowed)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		}

		int currentScreen = GameSettings.GetSafeScreenIndex();
		Rect2I usable = DisplayServer.ScreenGetUsableRect(currentScreen);

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
		

		var animFadeIn = new global::Godot.Animation();
		int trackId = animFadeIn.AddTrack(global::Godot.Animation.TrackType.Value);
		animFadeIn.TrackSetPath(trackId, $"{_fadeOverlay.GetPath()}:color");
		animFadeIn.TrackInsertKey(trackId, 0.0f, new Color(0, 0, 0, 0));
		animFadeIn.TrackInsertKey(trackId, 0.3f, new Color(0, 0, 0, 1));
		library.AddAnimation("fade_in", animFadeIn);


		var animFadeOut = new global::Godot.Animation();
		trackId = animFadeOut.AddTrack(global::Godot.Animation.TrackType.Value);
		animFadeOut.TrackSetPath(trackId, $"{_fadeOverlay.GetPath()}:color");
		animFadeOut.TrackInsertKey(trackId, 0.0f, new Color(0, 0, 0, 1));
		animFadeOut.TrackInsertKey(trackId, 0.3f, new Color(0, 0, 0, 0));
		library.AddAnimation("fade_out", animFadeOut);

		_fadeAnim.AddAnimationLibrary("", library);
		_fadeOverlay.Color = new Color(0, 0, 0, 0); // Start clear
	}

	public void TransitionTo(GameScreen screen, bool isVictory = true)
	{
		if (screen == GameScreen.GameOver && (_targetScreen == GameScreen.GameOver || _currentScreen is Realm.Client.UI.GameOver))
		{
			return;
		}
		_targetScreen = screen;
		_isVictory = isVictory;

		HandlePreTransitionState(screen);

		if (_transitionInProgress)
		{
			_queuedScreen = screen;
			_queuedIsVictory = isVictory;
			return;
		}

		_transitionInProgress = true;
		_fadeOverlay.MouseFilter = MouseFilterEnum.Stop;
		_fadeAnim.Play("fade_in");

		PlayScreenMusic(screen, isVictory);
		
		var timer = GetTree().CreateTimer(0.3f);
		timer.Timeout += OnFadeInComplete;
	}

	private bool RequiresReplayReset(GameScreen screen)
	{
		return screen == GameScreen.GameOver || 
		       screen == GameScreen.MainMenu || 
		       screen == GameScreen.MapEditorHUD || 
		       screen == GameScreen.LobbyBrowser || 
		       screen == GameScreen.LobbyRoom || 
		       screen == GameScreen.ReplayList;
	}

	private void HandlePreTransitionState(GameScreen screen)
	{
		if (RequiresReplayReset(screen))
		{
			if (ReplayPlaybackManager.Instance.IsPlayingReplay)
			{
				ReplayPlaybackManager.Instance.StopReplay();
				Realm.Client.Core.GameHost.Instance?.ResetWorldAndState();
			}
			Realm.Client.Core.GameHost.Instance?.StopRecording();
		}
	}

	private bool IsEnchantedRealmMusicScreen(GameScreen screen)
	{
		return screen == GameScreen.MainMenu || 
		       screen == GameScreen.LobbyBrowser || 
		       screen == GameScreen.LobbyRoom || 
		       screen == GameScreen.Settings || 
		       screen == GameScreen.MapDiscovery || 
		       screen == GameScreen.CreatorDiscovery || 
		       screen == GameScreen.MapDetails || 
		       screen == GameScreen.Storage;
	}

	private void PlayScreenMusic(GameScreen screen, bool isVictory)
	{
		if (IsEnchantedRealmMusicScreen(screen))
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
		ClearCurrentScreen();
		InstantiateTargetScreen();

		_fadeAnim.Play("fade_out");
		
		var timer = GetTree().CreateTimer(0.3f);
		timer.Timeout += OnFadeOutComplete;
	}

	private void ClearCurrentScreen()
	{
		if (_currentScreen == null) return;
		
		if (_currentScreen is MapEditorHUD)
		{
			Realm.Client.Core.GameHost.Instance?.ExitMapEditorMode();
		}
		_currentScreen.QueueFree();
		_currentScreen = null;
	}

	private void InstantiateTargetScreen()
	{
		PackedScene targetScene = GetTargetScene(_targetScreen);

		if (targetScene != null)
		{
			_currentScreen = targetScene.Instantiate<Control>();
			AddChild(_currentScreen);

			ConfigureReplayViewer();
			ConfigureOverlays();
			ConfigureScreenData();
		}
	}

	private void ConfigureReplayViewer()
	{
		if (_targetScreen == GameScreen.InGameHUD && ReplayPlaybackManager.Instance.IsPlayingReplay)
		{
			Realm.Client.Core.GameHost.Instance?.StopRecording();
			var panelScene = GD.Load<PackedScene>("res://UI/ReplayViewerPanel.tscn");
			if (panelScene != null)
			{
				var panel = panelScene.Instantiate<Control>();
				_currentScreen.AddChild(panel);
			}
		}
	}

	private void ConfigureOverlays()
	{
		MoveChild(_fadeOverlay, GetChildCount() - 1);
#if DEBUG
		if (_watermark != null)
		{
			MoveChild(_watermark, GetChildCount() - 1);
		}
#endif
	}

	private void ConfigureScreenData()
	{
		if (_currentScreen is Realm.Client.UI.GameOver gameOver)
		{
			gameOver.SetStatus(_isVictory);
		}
		else if (_currentScreen is Realm.Client.UI.MapDetails mapDetails)
		{
			mapDetails.SetMapData(_selectedMapData);
		}
	}

	private PackedScene GetSceneOrLoad(PackedScene cachedScene, string path)
	{
		return cachedScene ?? GD.Load<PackedScene>(path);
	}

	private PackedScene GetLobbyOrMapScene(GameScreen screen)
	{
		return screen switch
		{
			GameScreen.LobbyBrowser => GetSceneOrLoad(LobbyBrowserScene, "res://UI/LobbyBrowser.tscn"),
			GameScreen.LobbyCreate => GetSceneOrLoad(LobbyCreateScene, "res://UI/LobbyCreate.tscn"),
			GameScreen.LobbyRoom => GetSceneOrLoad(LobbyRoomScene, "res://UI/LobbyRoom.tscn"),
			GameScreen.MapDiscovery => GetSceneOrLoad(MapDiscoveryScene, "res://UI/MapDiscovery.tscn"),
			GameScreen.MapDetails => GetSceneOrLoad(MapDetailsScene, "res://UI/MapDetails.tscn"),
			_ => null
		};
	}

	private PackedScene GetOtherScene(GameScreen screen)
	{
		return screen switch
		{
			GameScreen.Settings => GetSceneOrLoad(SettingsScene, "res://UI/SettingsMenu.tscn"),
			GameScreen.InGameHUD => GetSceneOrLoad(InGameHUDScene, "res://UI/InGameHUD.tscn"),
			GameScreen.GameOver => GetSceneOrLoad(GameOverScene, "res://UI/GameOver.tscn"),
			GameScreen.CreatorDiscovery => GetSceneOrLoad(null, "res://UI/CreatorDiscovery.tscn"),
			GameScreen.ReplayList => GetSceneOrLoad(ReplayListScene, "res://UI/ReplayListPanel.tscn"),
			GameScreen.Storage => GetSceneOrLoad(StorageScene, "res://UI/StorageMenu.tscn"),
			_ => null
		};
	}

	private PackedScene GetTargetScene(GameScreen screen)
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;
		
		if (screen == GameScreen.MainMenu)
		{
			if (Network.LobbyManager.Instance != null) Network.LobbyManager.Instance.Disconnect();
			return MainMenuScene ?? GD.Load<PackedScene>("res://UI/MainMenu.tscn");
		}
		
		if (screen == GameScreen.MapEditorHUD)
		{
			Realm.Client.Core.GameHost.Instance?.StartMapEditorMode();
			return MapEditorHUDScene ?? GD.Load<PackedScene>("res://UI/MapEditorHUD.tscn");
		}

		return GetLobbyOrMapScene(screen) ?? GetOtherScene(screen);
	}

	private void OnFadeOutComplete()
	{
		_fadeOverlay.MouseFilter = MouseFilterEnum.Ignore;
		_transitionInProgress = false;
		if (_queuedScreen.HasValue)
		{
			GameScreen next = _queuedScreen.Value;
			bool vic = _queuedIsVictory;
			_queuedScreen = null;
			TransitionTo(next, vic);
		}
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
		var overlay = CreateImportProgressOverlay(archivePath, out var progressBar, out var statusLabel);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var result = await RunImportTask(archivePath, progressBar, statusLabel);

		if (GodotObject.IsInstanceValid(overlay))
		{
			overlay.QueueFree();
		}

		HandleImportResult(result, onSuccess);
		return result;
	}

	private ColorRect CreateImportProgressOverlay(string archivePath, out ProgressBar progressBar, out Label statusLabel)
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

		progressBar = new ProgressBar();
		progressBar.CustomMinimumSize = new Vector2(440, 22);
		progressBar.MinValue = 0;
		progressBar.MaxValue = 100;
		progressBar.Value = 0;
		vbox.AddChild(progressBar);

		statusLabel = new Label();
		statusLabel.Text = TranslationServer.Translate("Extracting and validating archive contents...");
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AddThemeFontSizeOverride("font_size", 13);
		statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(statusLabel);

		return overlay;
	}

	private async Task<(bool Success, string Message, string? MapTitle, string? MapVersion)> RunImportTask(
		string archivePath, ProgressBar progressBar, Label statusLabel)
	{
		var mapStorageService = ServiceLocator.Get<MapStorageService>();
		try
		{
			return await Task.Run(() => mapStorageService.ImportMapAsync(
				archivePath,
				progress => UpdateImportProgress(progress, progressBar, statusLabel)
			));
		}
		catch (Exception ex)
		{
			return (false, ex.Message, null, null);
		}
	}

	private void UpdateImportProgress(float progress, ProgressBar progressBar, Label statusLabel)
	{
		Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(progressBar))
			{
				progressBar.Value = Mathf.Clamp(progress * 100.0, 0, 100);
			}
			if (GodotObject.IsInstanceValid(statusLabel))
			{
				statusLabel.Text = GetImportPhaseText(progress);
			}
		}).CallDeferred();
	}

	private string GetImportPhaseText(float progress)
	{
		if (progress < 0.30f)
			return TranslationServer.Translate("Extracting to CAS...");
		if (progress < 0.50f)
			return TranslationServer.Translate("Extracting archive...");
		if (progress < 0.90f)
			return string.Format(TranslationServer.Translate("Linking assets... {0}%"), (int)(progress * 100));
		return TranslationServer.Translate("Registering map...");
	}

	private void HandleImportResult((bool Success, string Message, string? MapTitle, string? MapVersion) result, Action<string, string>? onSuccess)
	{
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
	}
}