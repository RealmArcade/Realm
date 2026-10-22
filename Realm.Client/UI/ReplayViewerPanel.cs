using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using static Godot.Control;
using static Godot.CanvasItem;

namespace Realm.Client.UI;

[GlobalClass]
public partial class ReplayViewerPanel : PanelContainer
{
	public override void _EnterTree()
	{
		Realm.Client.Services.GameContext.EnsureServices();
	}

	private Button _playPauseBtn;
	private Button _speedBtn05;
	private Button _speedBtn1;
	private Button _speedBtn2;
	private Button _speedBtn4;
	private Button _speedBtn8;
	private HSlider _scrubber;
	private Label _timeLabel;
	private OptionButton _perspectiveOpt;
	private Button _tetherBtn;
	private Button _quitBtn;

	private bool _isDraggingScrubber = false;
	private bool _suppressScrubberSignal = false;
	private bool _reachedEnd = false;
	private int _lastCurrentSec = -1;
	private int _lastTotalSec = -1;
	private bool? _lastIsPlaying = null;
	private float _lastPlaybackSpeed = -1f;
	private static StyleBoxFlat? _activeSpeedStyle;


	public override void _Ready()
	{
		SetupNodes();
		ConfigureThemeOverrides();
		BindEvents();
		SetupScrubber();

		ApplyButtonStyles();
		PopulatePerspectives();
		UpdateSpeedButtonHighlights();
		UpdatePlayPauseButtonText();

		AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());
	}

	private void SetupNodes()
	{
		_playPauseBtn = GetNode<Button>("MarginContainer/VBox/ControlsRow/PlayPauseButton");
		_speedBtn05 = GetNode<Button>("MarginContainer/VBox/ControlsRow/Speed05");
		_speedBtn1 = GetNode<Button>("MarginContainer/VBox/ControlsRow/Speed1");
		_speedBtn2 = GetNode<Button>("MarginContainer/VBox/ControlsRow/Speed2");
		_speedBtn4 = GetNode<Button>("MarginContainer/VBox/ControlsRow/Speed4");
		_speedBtn8 = GetNode<Button>("MarginContainer/VBox/ControlsRow/Speed8");
		_scrubber = GetNode<HSlider>("MarginContainer/VBox/ScrubberRow/Scrubber");
		_timeLabel = GetNode<Label>("MarginContainer/VBox/ScrubberRow/TimeLabel");
		_perspectiveOpt = GetNode<OptionButton>("MarginContainer/VBox/ControlsRow/PerspectiveOpt");
		_tetherBtn = GetNode<Button>("MarginContainer/VBox/ControlsRow/TetherButton");
		_quitBtn = GetNode<Button>("MarginContainer/VBox/ControlsRow/QuitButton");
	}

	private void ConfigureThemeOverrides()
	{
		_playPauseBtn.AddThemeConstantOverride("icon_max_width", 0);
		_speedBtn05.AddThemeConstantOverride("icon_max_width", 0);
		_speedBtn1.AddThemeConstantOverride("icon_max_width", 0);
		_speedBtn2.AddThemeConstantOverride("icon_max_width", 0);
		_speedBtn4.AddThemeConstantOverride("icon_max_width", 0);
		_speedBtn8.AddThemeConstantOverride("icon_max_width", 0);
		_tetherBtn.AddThemeConstantOverride("icon_max_width", 0);
		_quitBtn.AddThemeConstantOverride("icon_max_width", 0);
		_perspectiveOpt.AddThemeConstantOverride("icon_max_width", 0);
	}

	private void BindEvents()
	{
		_playPauseBtn.Pressed += OnPlayPausePressed;
		_playPauseBtn.MouseEntered += PlayHoverSound;

		_speedBtn05.Pressed += () => SetSpeed(0.5f);
		_speedBtn1.Pressed += () => SetSpeed(1.0f);
		_speedBtn2.Pressed += () => SetSpeed(2.0f);
		_speedBtn4.Pressed += () => SetSpeed(4.0f);
		_speedBtn8.Pressed += () => SetSpeed(8.0f);

		_speedBtn05.MouseEntered += PlayHoverSound;
		_speedBtn1.MouseEntered += PlayHoverSound;
		_speedBtn2.MouseEntered += PlayHoverSound;
		_speedBtn4.MouseEntered += PlayHoverSound;
		_speedBtn8.MouseEntered += PlayHoverSound;
		_tetherBtn.MouseEntered += PlayHoverSound;
		_quitBtn.MouseEntered += PlayHoverSound;

		_tetherBtn.Pressed += OnTetherPressed;
		_quitBtn.Pressed += OnQuitPressed;
	}

	private void PlayHoverSound()
	{
		GameContext.UserInterface?.PlayHoverSound();
	}

	private void SetupScrubber()
	{
		int totalTicks = GameContext.ReplayPlayback.TotalTicks;
		_scrubber.MinValue = 0;
		_scrubber.MaxValue = totalTicks > 1 ? totalTicks - 1 : 1;
		_scrubber.Step = 1;

		_scrubber.DragStarted += OnScrubberDragStarted;
		_scrubber.DragEnded += OnScrubberDragEnded;
		_scrubber.ValueChanged += OnScrubberValueChanged;
	}

	private void OnScrubberDragStarted()
	{
		_isDraggingScrubber = true;
		GameContext.ReplayPlayback.IsPlaying = false;
	}

	private void OnScrubberDragEnded(bool valueHasChanged)
	{
		_isDraggingScrubber = false;
		if (valueHasChanged)
		{
			GameContext.ReplayPlayback.ScrubTo((int)_scrubber.Value);
		}

		GameContext.ReplayPlayback.IsPlaying = true;
		_reachedEnd = false;
	}

	private void OnScrubberValueChanged(double val)
	{
		if (_suppressScrubberSignal || _isDraggingScrubber)
		{
			return;
		}

		GameContext.ReplayPlayback.ScrubTo((int)val);
		_reachedEnd = false;
	}

	private void ApplyButtonStyles()
	{
		foreach (var btn in new Button[] { _playPauseBtn, _speedBtn05, _speedBtn1, _speedBtn2, _speedBtn4, _speedBtn8, _tetherBtn, _quitBtn })
		{
			btn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
			btn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
			btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		}
	}

	private void OnPlayPausePressed()
	{
		GameContext.UserInterface?.PlayClickSound();
		bool nowPlaying = !GameContext.ReplayPlayback.IsPlaying;
		if (nowPlaying && _reachedEnd)
		{
			GameContext.ReplayPlayback.ScrubTo(0);
			_reachedEnd = false;
		}

		GameContext.ReplayPlayback.IsPlaying = nowPlaying;
		UpdatePlayPauseButtonText();
	}

	private void SetSpeed(float speed)
	{
		GameContext.UserInterface?.PlayClickSound();
		GameContext.ReplayPlayback.PlaybackSpeed = speed;
		UpdateSpeedButtonHighlights();
	}

	private void UpdatePlayPauseButtonText()
	{
		bool isPlaying = GameContext.ReplayPlayback.IsPlaying;
		_lastIsPlaying = isPlaying;
		_playPauseBtn.Text = Tr(isPlaying ? "PAUSE" : "PLAY");
	}

	private void UpdateSpeedButtonHighlights()
	{
		float speed = GameContext.ReplayPlayback.PlaybackSpeed;
		_lastPlaybackSpeed = speed;
		if (_activeSpeedStyle == null)
		{
			_activeSpeedStyle = new StyleBoxFlat
			{
				BgColor = UIStyle.ColorCyanGlow,
				BorderColor = UIStyle.ColorGold
			};
			_activeSpeedStyle.SetBorderWidthAll(1);
			_activeSpeedStyle.CornerRadiusTopLeft = 4;
			_activeSpeedStyle.CornerRadiusTopRight = 4;
			_activeSpeedStyle.CornerRadiusBottomLeft = 4;
			_activeSpeedStyle.CornerRadiusBottomRight = 4;
		}

		_speedBtn05.AddThemeStyleboxOverride("normal", speed == 0.5f ? _activeSpeedStyle : UIStyle.CreateButtonNormal());
		_speedBtn1.AddThemeStyleboxOverride("normal", speed == 1.0f ? _activeSpeedStyle : UIStyle.CreateButtonNormal());
		_speedBtn2.AddThemeStyleboxOverride("normal", speed == 2.0f ? _activeSpeedStyle : UIStyle.CreateButtonNormal());
		_speedBtn4.AddThemeStyleboxOverride("normal", speed == 4.0f ? _activeSpeedStyle : UIStyle.CreateButtonNormal());
		_speedBtn8.AddThemeStyleboxOverride("normal", speed == 8.0f ? _activeSpeedStyle : UIStyle.CreateButtonNormal());
	}

	private void PopulatePerspectives()
	{
		_perspectiveOpt.Clear();
		_perspectiveOpt.AddItem(Tr("Omniscient"), 0);

		var players = GameContext.ReplayPlayback.Header?.Players;
		if (players != null)
		{
			for (int i = 0; i < players.Count; i++)
			{
				_perspectiveOpt.AddItem(players[i].Name, i + 1);
			}
		}

		_perspectiveOpt.ItemSelected += (idx) =>
		{
			GameContext.UserInterface?.PlayClickSound();
			if (idx == 0)
			{
				GameContext.ReplayPlayback.SpectatorPerspective = -1;
			}
			else if (players != null && idx - 1 < players.Count)
			{
				GameContext.ReplayPlayback.SpectatorPerspective = players[(int)idx - 1].PeerId;
			}
		};
	}

	private void OnTetherPressed()
	{
		GameContext.UserInterface?.PlayClickSound();
		var camera = GameContext.Host?.MainCamera ?? GetTree().Root.GetNodeOrNull<CameraControl>("Main/Camera3D");
		if (camera == null) return;

		if (camera.FollowTarget != null)
		{
			camera.FollowTarget = null;
			_tetherBtn.Text = Tr("TETHER CAMERA");
		}
		else
		{
			Unit3D targetUnit = null;
			if (GameContext.Host != null && GameContext.Host.AllUnits.Count > 0)
			{
				foreach (var unit in GameContext.Host.AllUnits)
				{
					if (unit != null && GodotObject.IsInstanceValid(unit))
					{
						targetUnit = unit;
						break;
					}
				}
			}

			if (targetUnit != null)
			{
				camera.FollowTarget = targetUnit;
				_tetherBtn.Text = Tr("UNTETHER CAMERA");
			}
		}
	}

	private void OnQuitPressed()
	{
		GameContext.UserInterface?.PlayClickSound();
		GameContext.ReplayPlayback.StopReplay();
		GameContext.Host?.StopRecording();
		GameContext.Host?.ResetWorldAndState();
		if (GameContext.Lobby != null)
		{
			GameContext.Lobby.IsGameStarted = false;
			GameContext.Lobby.ActiveMapName = "";
		}

		GetTree().ChangeSceneToFile("res://GameHost.tscn");
	}

	public override void _Process(double delta)
	{
		if (!GameContext.ReplayPlayback.IsPlayingReplay) return;

		bool isPlaying = GameContext.ReplayPlayback.IsPlaying;
		int currentTick = GameContext.ReplayPlayback.CurrentTick;
		int totalTicks = GameContext.ReplayPlayback.TotalTicks;

		if (isPlaying && currentTick >= totalTicks - 1 && !_reachedEnd)
		{
			_reachedEnd = true;
			GameContext.ReplayPlayback.IsPlaying = false;
			isPlaying = false;
		}

		if (!_isDraggingScrubber)
		{
			if ((int)_scrubber.Value != currentTick)
			{
				_suppressScrubberSignal = true;
				_scrubber.Value = currentTick;
				_suppressScrubberSignal = false;
			}
		}

		int currentSec = currentTick / 30;
		int totalSec = totalTicks / 30;

		if (currentSec != _lastCurrentSec || totalSec != _lastTotalSec)
		{
			_lastCurrentSec = currentSec;
			_lastTotalSec = totalSec;
			string currentTime = string.Format("{0:D2}:{1:D2}", currentSec / 60, currentSec % 60);
			string totalTime = string.Format("{0:D2}:{1:D2}", totalSec / 60, totalSec % 60);
			_timeLabel.Text = $"{currentTime} / {totalTime}";
		}

		if (_lastIsPlaying != isPlaying)
		{
			UpdatePlayPauseButtonText();
		}

		if (_lastPlaybackSpeed != GameContext.ReplayPlayback.PlaybackSpeed)
		{
			UpdateSpeedButtonHighlights();
		}
	}

	public static void ClearCache()
	{
		_activeSpeedStyle = null;
	}
}
