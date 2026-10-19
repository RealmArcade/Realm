using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using static Godot.Control;
using static Godot.CanvasItem;

namespace Realm.Client.UI;

[GlobalClass]
public partial class ReplayViewerPanel : PanelContainer
{
	public ReplayViewerPanel()
	{
	}

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
		UIManager.Instance?.PlayHoverSound();
	}

	private void SetupScrubber()
	{
		int totalTicks = ReplayPlaybackManager.Instance.TotalTicks;
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
		ReplayPlaybackManager.Instance.IsPlaying = false;
	}

	private void OnScrubberDragEnded(bool valueHasChanged)
	{
		_isDraggingScrubber = false;
		if (valueHasChanged)
		{
			ReplayPlaybackManager.Instance.ScrubTo((int)_scrubber.Value);
		}

		ReplayPlaybackManager.Instance.IsPlaying = true;
		_reachedEnd = false;
	}

	private void OnScrubberValueChanged(double val)
	{
		if (_suppressScrubberSignal || _isDraggingScrubber)
		{
			return;
		}

		ReplayPlaybackManager.Instance.ScrubTo((int)val);
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
		UIManager.Instance?.PlayClickSound();
		bool nowPlaying = !ReplayPlaybackManager.Instance.IsPlaying;
		if (nowPlaying && _reachedEnd)
		{
			ReplayPlaybackManager.Instance.ScrubTo(0);
			_reachedEnd = false;
		}

		ReplayPlaybackManager.Instance.IsPlaying = nowPlaying;
		UpdatePlayPauseButtonText();
	}

	private void SetSpeed(float speed)
	{
		UIManager.Instance?.PlayClickSound();
		ReplayPlaybackManager.Instance.PlaybackSpeed = speed;
		UpdateSpeedButtonHighlights();
	}

	private void UpdatePlayPauseButtonText()
	{
		bool isPlaying = ReplayPlaybackManager.Instance.IsPlaying;
		_lastIsPlaying = isPlaying;
		_playPauseBtn.Text = Tr(isPlaying ? "PAUSE" : "PLAY");
	}

	private void UpdateSpeedButtonHighlights()
	{
		float speed = ReplayPlaybackManager.Instance.PlaybackSpeed;
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

		var players = ReplayPlaybackManager.Instance.Header?.Players;
		if (players != null)
		{
			for (int i = 0; i < players.Count; i++)
			{
				_perspectiveOpt.AddItem(players[i].Name, i + 1);
			}
		}

		_perspectiveOpt.ItemSelected += (idx) =>
		{
			UIManager.Instance?.PlayClickSound();
			if (idx == 0)
			{
				ReplayPlaybackManager.Instance.SpectatorPerspective = -1;
			}
			else if (players != null && idx - 1 < players.Count)
			{
				ReplayPlaybackManager.Instance.SpectatorPerspective = players[(int)idx - 1].PeerId;
			}
		};
	}

	private void OnTetherPressed()
	{
		UIManager.Instance?.PlayClickSound();
		var camera = GameHost.Instance?.MainCamera ?? GetTree().Root.GetNodeOrNull<CameraControl>("Main/Camera3D");
		if (camera == null) return;

		if (camera.FollowTarget != null)
		{
			camera.FollowTarget = null;
			_tetherBtn.Text = Tr("TETHER CAMERA");
		}
		else
		{
			Unit3D targetUnit = null;
			if (GameHost.Instance != null && GameHost.Instance.AllUnits.Count > 0)
			{
				foreach (var unit in GameHost.Instance.AllUnits)
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
		UIManager.Instance?.PlayClickSound();
		ReplayPlaybackManager.Instance.StopReplay();
		GameHost.Instance?.StopRecording();
		GameHost.Instance?.ResetWorldAndState();
		if (LobbyManager.Instance != null)
		{
			LobbyManager.Instance.IsGameStarted = false;
			LobbyManager.Instance.ActiveMapName = "";
		}

		GetTree().ChangeSceneToFile("res://GameHost.tscn");
	}

	public override void _Process(double delta)
	{
		if (!ReplayPlaybackManager.Instance.IsPlayingReplay) return;

		bool isPlaying = ReplayPlaybackManager.Instance.IsPlaying;
		int currentTick = ReplayPlaybackManager.Instance.CurrentTick;
		int totalTicks = ReplayPlaybackManager.Instance.TotalTicks;

		if (isPlaying && currentTick >= totalTicks - 1 && !_reachedEnd)
		{
			_reachedEnd = true;
			ReplayPlaybackManager.Instance.IsPlaying = false;
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

		if (_lastPlaybackSpeed != ReplayPlaybackManager.Instance.PlaybackSpeed)
		{
			UpdateSpeedButtonHighlights();
		}
	}
}
