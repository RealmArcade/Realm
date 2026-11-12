// Screen transitions end to end: the fade overlay, the pre-transition state fixups, the screen music,
// clearing the old screen, instancing and configuring the new one, and the scene lookups that decide
// what to instantiate. The fade callbacks sit at both ends because that is where the order matters.

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

public partial class UIManager
{
	public void TransitionTo(GameScreen screen, bool isVictory = true)
	{
		if (screen == GameScreen.GameOver && (_targetScreen == GameScreen.GameOver || _currentScreen is GameOver))
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
		if (screen == GameScreen.MapEditorHUD)
		{
			GameContext.FXService?.ClearProjectileTrails();
		}

		if (RequiresReplayReset(screen))
		{
			if (GameContext.ReplayPlayback.IsPlayingReplay)
			{
				GameContext.ReplayPlayback.StopReplay();
				GameContext.Host?.ResetWorldAndState();
			}

			GameContext.Host?.StopRecording();
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
			PlayMusicPlaylist(MenuMusicPlaylist);
		}
		else if (screen == GameScreen.InGameHUD)
		{
			PlayMusicPlaylist(BattleMusicPlaylist);
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

	/// <summary>
	///     Plays the tracks of a screen in order and wraps back to the first once the last one finishes, so a screen
	///     keeps its music instead of falling silent when a track ends.
	/// </summary>
	public void PlayMusicPlaylist(string[] playlist)
	{
		if (playlist == null || playlist.Length == 0) return;

		if (IsCurrentPlaylist(playlist) && _musicPlayer != null && _musicPlayer.Playing) return;

		_musicPlaylist = playlist;
		_musicPlaylistIndex = 0;
		PlayCurrentMusicTrack();
	}

	public void StopMusic()
	{
		_musicPlaylist = System.Array.Empty<string>();
		_musicPlaylistIndex = 0;
		_musicPlayer?.Stop();
	}

	private void PlayCurrentMusicTrack()
	{
		if (_musicPlayer == null || _musicPlaylist.Length == 0) return;

		var stream = GD.Load<AudioStream>(_musicPlaylist[_musicPlaylistIndex]);
		if (stream == null) return;

		_musicPlayer.Stream = stream;
		_musicPlayer.Play();
	}

	private bool IsCurrentPlaylist(string[] playlist)
	{
		if (!ReferenceEquals(_musicPlaylist, playlist)) return false;
		return _musicPlayer?.Stream?.ResourcePath == playlist[_musicPlaylistIndex];
	}

	private void OnMusicTrackFinished()
	{
		if (_musicPlaylist.Length == 0) return;

		_musicPlaylistIndex = (_musicPlaylistIndex + 1) % _musicPlaylist.Length;
		PlayCurrentMusicTrack();
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
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;

		ClearCurrentScreen();
		InstantiateTargetScreen();

		_fadeAnim.Play("fade_out");

		var timer = GetTree().CreateTimer(0.3f);
		timer.Timeout += OnFadeOutComplete;
	}

	private void ClearCurrentScreen()
	{
		if (_currentScreen == null) return;

		if (GodotObject.IsInstanceValid(_currentScreen))
		{
			if (_currentScreen is MapEditorHUD)
			{
				GameContext.Host?.ExitMapEditorMode();
			}

			if (_currentScreen.GetParent() == this)
			{
				RemoveChild(_currentScreen);
			}

			_currentScreen.QueueFree();
		}
		_currentScreen = null;
	}

	private void InstantiateTargetScreen()
	{
		PackedScene? targetScene = GetTargetScene(_targetScreen);

		if (targetScene != null)
		{
			_currentScreen = targetScene.Instantiate<Control>();
			AddChild(_currentScreen);

			ConfigureReplayViewer(_currentScreen);
			ConfigureOverlays();
			ConfigureScreenData();
		}
	}

	private void ConfigureReplayViewer(Control screen)
	{
		if (_targetScreen == GameScreen.InGameHUD && GameContext.ReplayPlayback.IsPlayingReplay)
		{
			GameContext.Host?.StopRecording();
			var panelScene = GD.Load<PackedScene>("res://UI/ReplayViewerPanel.tscn");
			if (panelScene != null)
			{
				var panel = panelScene.Instantiate<Control>();
				screen.AddChild(panel);
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
		if (_currentScreen is GameOver gameOver)
		{
			gameOver.SetStatus(_isVictory);
			gameOver.ReportMatchResult();
		}
		else if (_currentScreen is MapDetails mapDetails)
		{
			mapDetails.SetMapData(_selectedMapData);
		}
	}

	private PackedScene? GetSceneOrLoad(PackedScene? cachedScene, string path)
	{
		return cachedScene ?? GD.Load<PackedScene>(path);
	}

	private PackedScene? GetLobbyOrMapScene(GameScreen screen)
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

	private PackedScene? GetOtherScene(GameScreen screen)
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

	private PackedScene? GetTargetScene(GameScreen screen)
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;

		if (screen == GameScreen.MainMenu)
		{
			if (GameContext.Lobby != null) GameContext.Lobby.Disconnect();
			return MainMenuScene ?? GD.Load<PackedScene>("res://UI/MainMenu.tscn");
		}

		if (screen == GameScreen.MapEditorHUD)
		{
			GameContext.Host?.StartMapEditorMode();
			return MapEditorHUDScene ?? GD.Load<PackedScene>("res://UI/MapEditorHUD.tscn");
		}

		return GetLobbyOrMapScene(screen) ?? GetOtherScene(screen);
	}

	private void OnFadeOutComplete()
	{
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;

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


}