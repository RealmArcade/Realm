using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Shared;
using static Godot.Control;
using static Godot.CanvasItem;

namespace Realm.Client.UI;

[GlobalClass]
public partial class UIManager : Control
{
	public UIManager()
	{
		GameContext.RegisterUserInterface(this);
	}

	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

	[Export] public PackedScene MainMenuScene = null!;
	[Export] public PackedScene LobbyBrowserScene = null!;
	[Export] public PackedScene LobbyRoomScene = null!;
	[Export] public PackedScene SettingsScene = null!;
	[Export] public PackedScene InGameHUDScene = null!;
	[Export] public PackedScene GameOverScene = null!;
	[Export] public PackedScene MapDiscoveryScene = null!;
	[Export] public PackedScene MapDetailsScene = null!;
	[Export] public PackedScene MapEditorHUDScene = null!;
	[Export] public PackedScene ReplayListScene = null!;
	[Export] public PackedScene LobbyCreateScene = null!;
	[Export] public PackedScene StorageScene = null!;

	private Control? _currentScreen;
	private ColorRect _fadeOverlay = null!;
	private AnimationPlayer _fadeAnim = null!;
	private Label _watermark = null!;
	private GameScreen _targetScreen;
	private bool _isVictory = true;
	private bool _transitionInProgress = false;
	private GameScreen? _queuedScreen;
	private bool _queuedIsVictory;

	private MapData _selectedMapData = null!;

	/// <summary>
	///     Whether the first screen has been built. A screen named before this point races the startup transition that shows
	///     the main menu, so anything that wants to send the player somewhere waits for it.
	/// </summary>
	public bool HasVisibleScreen => _currentScreen != null && GodotObject.IsInstanceValid(_currentScreen);

	public void TransitionToMapDetails(MapData mapData)
	{
		_selectedMapData = mapData;
		TransitionTo(GameScreen.MapDetails);
	}

	private AudioStreamPlayer _musicPlayer = null!;
	private AudioStreamPlayer _sfxPlayer = null!;
	private string[] _musicPlaylist = System.Array.Empty<string>();
	private int _musicPlaylistIndex;

	private static readonly string[] MenuMusicPlaylist = { "res://Assets/Audio/Music/enchanted_realm.ogg" };
	private static readonly string[] BattleMusicPlaylist = { "res://Assets/Audio/Music/battle_anthem.ogg" };

	public override void _ExitTree()
	{
		GameContext.UnregisterUserInterface(this);
	}

	/// <summary>
	///     Godot calls a void override, so the awaited startup work lives in <see cref="ReadyAsync" />. An async void
	///     override had no catcher at all: a fault in the startup settings left the manager never transitioning to a
	///     screen, with an empty window and nothing logged.
	/// </summary>
	public override void _Ready()
	{
		_ = ObserveReadyAsync();
	}

	/// <summary>
	///     Runs <see cref="ReadyAsync" /> and logs whatever it throws, so the discarded task above cannot fault.
	/// </summary>
	private async Task ObserveReadyAsync()
	{
		try
		{
			await ReadyAsync();
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[UIManager] Startup failed: {exception}");
		}
	}

	private async Task ReadyAsync()
	{
		GameContext.RegisterUserInterface(this);
		MouseFilter = MouseFilterEnum.Ignore;


		_musicPlayer = new AudioStreamPlayer { Name = "MusicPlayer", Bus = AudioService.MusicBusName };
		_musicPlayer.Finished += OnMusicTrackFinished;
		AddChild(_musicPlayer);

		_sfxPlayer = new AudioStreamPlayer { Name = "SfxPlayer", Bus = AudioService.SfxBusName };
		AddChild(_sfxPlayer);

		await ApplyStartupSettings();


		CreateFadeOverlay();

#if DEBUG
		_watermark = new Label();
		_watermark.Name = "Watermark_Label";
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

		if (GameContext.Lobby != null && GameContext.Lobby.IsGameStarted)
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
		if (what == unchecked((int)Node.NotificationWMCloseRequest) ||
		    what == unchecked((int)Node.NotificationApplicationPaused) ||
		    what == unchecked((int)Node.NotificationApplicationFocusOut))
		{
			LocalizationManager.FlushPendingWrites();
		}

		if (what == unchecked((int)Node.NotificationWMCloseRequest) ||
		    what == (int)Node.NotificationPredelete)
		{
			ClientCacheCleaner.ClearAllCaches();
		}

		if (what == unchecked((int)Node.NotificationWMCloseRequest))
		{
			if (GameContext.MapEditorHud != null && GodotObject.IsInstanceValid(GameContext.MapEditorHud) && GameContext.MapEditorHud.IsInsideTree())
			{
				GameContext.MapEditorHud.HandleQuitRequest();
			}
			else if (GameContext.SessionState.IsTestMode && MapEditorHUD.HasUnsavedChangesStatic())
			{
				ShowConfirmationDialog(
					TranslationServer.Translate("You haven't saved yet"),
					onConfirm: () => GetTree().Quit(),
					confirmText: TranslationServer.Translate("Quit"),
					cancelText: TranslationServer.Translate("Stay")
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
		AudioService.ApplyVolumeSettings();
	}

	private void CreateFadeOverlay()
	{
		_fadeOverlay = new ColorRect();
		_fadeOverlay.Name = "FadeOverlay_ColorRect";
		_fadeOverlay.Color = new Color(0, 0, 0, 1);
		_fadeOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_fadeOverlay.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(_fadeOverlay);

		_fadeAnim = new AnimationPlayer();
		_fadeAnim.Name = "FadeAnim_AnimationPlayer";
		AddChild(_fadeAnim);


		var library = new AnimationLibrary();


		var animFadeIn = new Godot.Animation();
		int trackId = animFadeIn.AddTrack(Godot.Animation.TrackType.Value);
		animFadeIn.TrackSetPath(trackId, $"{_fadeOverlay.GetPath()}:color");
		animFadeIn.TrackInsertKey(trackId, 0.0f, new Color(0, 0, 0, 0));
		animFadeIn.TrackInsertKey(trackId, 0.3f, new Color(0, 0, 0, 1));
		library.AddAnimation("fade_in", animFadeIn);


		var animFadeOut = new Godot.Animation();
		trackId = animFadeOut.AddTrack(Godot.Animation.TrackType.Value);
		animFadeOut.TrackSetPath(trackId, $"{_fadeOverlay.GetPath()}:color");
		animFadeOut.TrackInsertKey(trackId, 0.0f, new Color(0, 0, 0, 1));
		animFadeOut.TrackInsertKey(trackId, 0.3f, new Color(0, 0, 0, 0));
		library.AddAnimation("fade_out", animFadeOut);

		_fadeAnim.AddAnimationLibrary("", library);
		_fadeOverlay.Color = new Color(0, 0, 0, 0);
	}

	public void OpenSettingsOverlay()
	{
		if (GameContext.IsSettingsMenuOpen) return;

		var settingsPopup = GD.Load<PackedScene>("res://UI/SettingsMenu.tscn").Instantiate<SettingsMenu>();
		settingsPopup.IsOverlay = true;
		AddChild(settingsPopup);
		MoveChild(settingsPopup, GetChildCount() - 1);
		if (_fadeOverlay != null)
		{
			MoveChild(_fadeOverlay, GetChildCount() - 1);
		}
	}


	public void ShowPopupMessage(string title, string message, Action? onOkPressed = null)
	{
		ShowConfirmationDialog(message, onOkPressed, confirmText: TranslationServer.Translate("OK"), showCancel: false, titleOverride: title);
	}

	public void ShowConfirmationDialog(string message, Action? onConfirm, string confirmText = "YES", string cancelText = "NO", Action? onCancel = null, bool showCancel = true, string? titleOverride = null)
	{
		var overlay = new ColorRect();
		overlay.Name = "ConfirmationOverlay";
		overlay.Color = new Color(0, 0, 0, 0.5f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(overlay);

		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(400, 200);
		panel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		panel.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		var center = UIStyle.CreateCenteredOverlay();
		overlay.AddChild(center);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 15);
		panel.AddChild(vbox);

		var lblTitle = new Label();
		lblTitle.Name = "LblTitle_Label";
		UIStyle.ApplyTitle(lblTitle, TranslationServer.Translate(titleOverride ?? (showCancel ? "CONFIRMATION REQUIRED" : "NOTIFICATION")), 18);
		lblTitle.AddThemeColorOverride("font_color", titleOverride != null && (titleOverride.Contains("ERROR") || titleOverride.Contains("MISMATCH") || titleOverride.Contains("FAILED") || titleOverride.Contains("CORRUPT")) ? new Color(0.9f, 0.3f, 0.3f) : UIStyle.ColorGold);
		vbox.AddChild(lblTitle);

		var lblMsg = new Label();
		lblMsg.Name = "LblMsg_Label";
		lblMsg.Text = TranslationServer.Translate(message);
		lblMsg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblMsg.CustomMinimumSize = new Vector2(460, 0);
		lblMsg.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblMsg.HorizontalAlignment = HorizontalAlignment.Center;
		lblMsg.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		lblMsg.AddThemeFontSizeOverride("font_size", 13);
		vbox.AddChild(lblMsg);

		var hbox = new HBoxContainer();
		hbox.Name = "Hbox_HBoxContainer";
		hbox.AddThemeConstantOverride("separation", 20);
		hbox.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(hbox);

		var btnConfirm = new Button();
		btnConfirm.Name = "BtnConfirm_Button";
		btnConfirm.Set("icon_max_width", 0);
		UIStyle.ApplyButtonText(btnConfirm, TranslationServer.Translate(confirmText), 13);
		UIStyle.ApplyButtonStyleBoxes(btnConfirm);
		btnConfirm.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		btnConfirm.Pressed += () =>
		{
			overlay.QueueFree();
			onConfirm?.Invoke();
		};
		hbox.AddChild(btnConfirm);

		if (showCancel)
		{
			var btnCancel = UIStyle.CreateBareButton("BtnCancel_Button");
			UIStyle.ApplyButtonText(btnCancel, TranslationServer.Translate(cancelText), 13);
			UIStyle.ApplyButtonStyleBoxes(btnCancel);
			btnCancel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
			Action cancelAction = () =>
			{
				overlay.QueueFree();
				onCancel?.Invoke();
			};
			overlay.SetMeta("CancelAction", Callable.From(cancelAction));
			btnCancel.Pressed += () => { cancelAction(); };
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

		var center = UIStyle.CreateCenteredOverlay();
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(500, 220);
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 12);
		panel.AddChild(vbox);

		var titleLabel = new Label();
		titleLabel.Name = "TitleLabel_Label";
		UIStyle.ApplyTitle(titleLabel, "📥 " + TranslationServer.Translate("IMPORTING MAP ARCHIVE"), 20);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(titleLabel);

		var fileNameLabel = new Label();
		fileNameLabel.Name = "FileNameLabel_Label";
		fileNameLabel.Text = Path.GetFileName(archivePath);
		fileNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		fileNameLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		fileNameLabel.AddThemeFontSizeOverride("font_size", 13);
		vbox.AddChild(fileNameLabel);

		progressBar = UIStyle.CreateProgressBar(440f, 22f);
		vbox.AddChild(progressBar);

		statusLabel = new Label();
		statusLabel.Name = "StatusLabel_Label";
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
		var mapStorageService = GameContext.MapStorageService;
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
				confirmText: TranslationServer.Translate("OK"),
				showCancel: false
			);
		}
		else
		{
			ShowConfirmationDialog(
				string.Format(TranslationServer.Translate("Import failed: {0}"), result.Message),
				() => { },
				confirmText: TranslationServer.Translate("OK"),
				showCancel: false
			);
		}
	}
}