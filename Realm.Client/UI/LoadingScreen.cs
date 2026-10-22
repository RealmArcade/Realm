using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using static Godot.Control;

namespace Realm.Client.UI;

[GlobalClass]
public partial class LoadingScreen : Control
{
	private const int AssetsPreloadedPerFrame = 2;
	private const double MaximumLoadingSeconds = 30.0;
	private const double MinimumVisibleSeconds = 0.35;

	private readonly MapAssetPreloadService _preloadService = new();

	private ProgressBar _progressBar;
	private Label _titleLabel;
	private Label _statusLabel;
	private Label _failureLabel;

	private double _elapsedSeconds;
	private bool _finished;
	private string _statusMessage = string.Empty;

	public override void _EnterTree()
	{
		GameContext.EnsureServices();
	}

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
		BindNodes();
		StyleLabels();

		_titleLabel.Text = TranslationServer.Translate("LOADING BATTLE");
		_statusLabel.Text = TranslationServer.Translate("Preparing map assets...");
		_progressBar.Value = 0;

		_preloadService.Prepare(ResolveMapDirectory());
		if (_preloadService.TotalAssetCount == 0)
		{
			FinishLoading();
		}
	}

	public override void _Process(double delta)
	{
		if (_finished) return;

		_elapsedSeconds += delta;

		PreloadNextAssets();

		if (_preloadService.TotalAssetCount > 0)
		{
			_progressBar.Value = _preloadService.ProcessedAssetCount * 100.0 / _preloadService.TotalAssetCount;
		}

		if (!string.IsNullOrEmpty(_statusMessage))
		{
			_statusLabel.Text = _statusMessage;
		}

		if (_preloadService.IsComplete || _elapsedSeconds >= MaximumLoadingSeconds)
		{
			FinishLoading();
		}
	}

	public override void _ExitTree()
	{
		_preloadService.ReleaseWarmTextures();
	}

	private void BindNodes()
	{
		_progressBar = GetNode<ProgressBar>("CenterContainer/PanelContainer/VBox/ProgressBar");
		_titleLabel = GetNode<Label>("CenterContainer/PanelContainer/VBox/TitleLabel");
		_statusLabel = GetNode<Label>("CenterContainer/PanelContainer/VBox/StatusLabel");
		_failureLabel = GetNode<Label>("CenterContainer/PanelContainer/VBox/FailureLabel");
		_failureLabel.Text = string.Empty;
		_failureLabel.Visible = false;
	}

	private void StyleLabels()
	{
		_titleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_titleLabel.AddThemeFontSizeOverride("font_size", 34);
		_statusLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.88f, 0.95f));
		_statusLabel.AddThemeFontSizeOverride("font_size", 15);
		_statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_statusLabel.CustomMinimumSize = new Vector2(460, 0);
		_statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_failureLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.65f, 0.3f));
		_failureLabel.AddThemeFontSizeOverride("font_size", 13);
		_failureLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_failureLabel.CustomMinimumSize = new Vector2(460, 0);
		_failureLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var panel = GetNode<PanelContainer>("CenterContainer/PanelContainer");
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
	}

	private void PreloadNextAssets()
	{
		for (int i = 0; i < AssetsPreloadedPerFrame && !_preloadService.IsComplete; i++)
		{
			if (_preloadService.TryPreloadNext(out var progressUpdate))
			{
				_statusMessage = progressUpdate.Message;
			}
		}
	}

	private void FinishLoading()
	{
		if (_finished) return;
		_finished = true;

		_progressBar.Value = 100;
		_statusLabel.Text = TranslationServer.Translate("Assets ready.");
		ReportFailedAssets();

		if (_elapsedSeconds < MinimumVisibleSeconds)
		{
			var timer = GetTree().CreateTimer(MinimumVisibleSeconds - _elapsedSeconds);
			timer.Timeout += TransitionToGame;
			return;
		}

		TransitionToGame();
	}

	private void ReportFailedAssets()
	{
		int failedCount = _preloadService.FailedAssetPaths.Count;
		if (failedCount == 0) return;

		foreach (string failedAssetPath in _preloadService.FailedAssetPaths)
		{
			GD.PushWarning($"[LoadingScreen] Asset failed to load and was skipped: {failedAssetPath}");
		}

		_failureLabel.Visible = true;
		_failureLabel.Text = string.Format(
			TranslationServer.Translate("{0} asset(s) could not be loaded and were skipped."),
			failedCount);
	}

	private void TransitionToGame()
	{
		if (!IsInsideTree()) return;
		GameContext.UserInterface?.TransitionTo(GameScreen.InGameHUD);
	}

	private static string ResolveMapDirectory()
	{
		string mapDirectory = GameContext.MapStorageService.CurrentMapDirectory;
		if (!string.IsNullOrEmpty(mapDirectory) && System.IO.Directory.Exists(mapDirectory))
		{
			return mapDirectory;
		}

		return string.Empty;
	}
}
