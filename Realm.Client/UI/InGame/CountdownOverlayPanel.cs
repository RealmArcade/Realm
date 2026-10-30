using System;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;

namespace Realm.Client.UI.InGame;

public class CountdownOverlayPanel
{
	private const int CardWidth = 420;
	private const int CardHeight = 560;
	private const float FadeInSeconds = 0.2f;
	private const float FadeOutSeconds = 0.3f;

	private Control _overlayRoot;
	private Label _overlayTitleLabel;
	private Label _overlayNumberLabel;
	private Button _overlayCancelButton;

	private PanelContainer _compactPanel;
	private Label _compactLabel;
	private Button _compactCancelButton;

	private Tween? _fadeTween;
	private Tween? _pulseTween;
	private bool _overlayShown;
	private int _overlaySeconds = int.MinValue;
	private string _overlayTitle = string.Empty;
	private int _compactSeconds = int.MinValue;
	private string _compactTitle = string.Empty;

	public CountdownOverlayPanel(Control hudRoot, VBoxContainer customUIPanel, Action onCancel)
	{
		BuildOverlay(hudRoot, onCancel);
		BuildCompactPanel(customUIPanel, onCancel);
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		bool allowed = GameContext.InGameHud?.IsHudElementVisible(HudElement.CountdownTimer, true) ?? true;
		bool active = viewModel.CountdownActive && allowed;
		bool useOverlay = active && IsPreMatchCountdown();

		if (useOverlay)
		{
			ShowOverlay(viewModel);
		}
		else
		{
			HideOverlay();
		}

		if (active && !useOverlay)
		{
			ShowCompact(viewModel);
		}
		else
		{
			HideCompact();
		}
	}

	private static bool IsPreMatchCountdown()
	{
		LobbyManager? lobby = GameContext.Lobby;
		return lobby != null && !lobby.IsGameStarted;
	}

	private void ShowOverlay(InGameHUDViewModel viewModel)
	{
		if (!_overlayShown) BeginOverlayShow();

		string title = viewModel.CountdownText ?? string.Empty;
		if (!string.Equals(title, _overlayTitle, StringComparison.Ordinal))
		{
			_overlayTitleLabel.Text = title;
			_overlayTitle = title;
		}

		int seconds = ResolveSeconds(viewModel);
		if (seconds == _overlaySeconds) return;

		_overlayNumberLabel.Text = seconds.ToString();
		_overlaySeconds = seconds;
		PlayPulse();
	}

	private void ShowCompact(InGameHUDViewModel viewModel)
	{
		if (!_compactPanel.Visible)
		{
			_compactPanel.Visible = true;
			_compactCancelButton.Visible = CanCancelCountdown();
		}

		int seconds = ResolveSeconds(viewModel);
		string title = viewModel.CountdownText ?? string.Empty;
		if (seconds == _compactSeconds && string.Equals(title, _compactTitle, StringComparison.Ordinal)) return;

		_compactLabel.Text = $"{title}: {seconds}s";
		_compactSeconds = seconds;
		_compactTitle = title;
	}

	private static int ResolveSeconds(InGameHUDViewModel viewModel)
	{
		return (int)Math.Ceiling(Math.Max(0f, viewModel.CountdownDuration));
	}

	private static bool CanCancelCountdown()
	{
		return GameContext.Lobby?.IsHost == true;
	}

	private void HideCompact()
	{
		if (_compactPanel.Visible)
		{
			_compactPanel.Visible = false;
		}
	}

	private void BeginOverlayShow()
	{
		_overlayShown = true;
		_overlaySeconds = int.MinValue;
		_overlayTitle = string.Empty;
		_overlayCancelButton.Visible = CanCancelCountdown();

		_overlayRoot.Visible = true;
		_overlayRoot.Modulate = new Color(1f, 1f, 1f, 0f);
		_overlayRoot.MoveToFront();

		_fadeTween?.Kill();
		_fadeTween = _overlayRoot.CreateTween();
		_fadeTween.TweenProperty(_overlayRoot, "modulate:a", 1f, FadeInSeconds);
	}

	private void HideOverlay()
	{
		if (!_overlayShown) return;

		_overlayShown = false;
		_fadeTween?.Kill();
		_fadeTween = _overlayRoot.CreateTween();
		_fadeTween.TweenProperty(_overlayRoot, "modulate:a", 0f, FadeOutSeconds);
		_fadeTween.TweenCallback(Callable.From(OnOverlayFadedOut));
	}

	private void OnOverlayFadedOut()
	{
		if (_overlayShown) return;

		_overlayRoot.Visible = false;
		_pulseTween?.Kill();
		_pulseTween = null;
		_overlayNumberLabel.Scale = Vector2.One;
	}

	private void PlayPulse()
	{
		_pulseTween?.Kill();
		_overlayNumberLabel.PivotOffset = _overlayNumberLabel.Size / 2f;
		_overlayNumberLabel.Scale = new Vector2(1.18f, 1.18f);
		_pulseTween = _overlayNumberLabel.CreateTween();
		_pulseTween.TweenProperty(_overlayNumberLabel, "scale", Vector2.One, 0.22f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
	}

	private void BuildOverlay(Control hudRoot, Action onCancel)
	{
		_overlayRoot = new Control();
		_overlayRoot.Name = "CountdownOverlay";
		_overlayRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
		_overlayRoot.Visible = false;
		hudRoot.AddChild(_overlayRoot);
		_overlayRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var dimmer = new ColorRect();
		dimmer.Name = "CountdownDimmer";
		dimmer.Color = new Color(0f, 0f, 0f, 0.35f);
		dimmer.MouseFilter = Control.MouseFilterEnum.Ignore;
		_overlayRoot.AddChild(dimmer);
		dimmer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var card = new Panel();
		card.Name = "CountdownCard";
		card.MouseFilter = Control.MouseFilterEnum.Stop;
		card.AnchorLeft = 0.5f;
		card.AnchorTop = 0.5f;
		card.AnchorRight = 0.5f;
		card.AnchorBottom = 0.5f;
		card.OffsetLeft = -CardWidth / 2f;
		card.OffsetTop = -CardHeight / 2f;
		card.OffsetRight = CardWidth / 2f;
		card.OffsetBottom = CardHeight / 2f;
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateGameStartingPanel());
		_overlayRoot.AddChild(card);

		var content = new VBoxContainer();
		content.Name = "CountdownContent";
		card.AddChild(content);
		content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		content.AddChild(new Control { Name = "CountdownTopSpacer", CustomMinimumSize = new Vector2(0, 22) });

		_overlayTitleLabel = new Label();
		_overlayTitleLabel.Name = "CountdownTitleLabel";
		_overlayTitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_overlayTitleLabel.VerticalAlignment = VerticalAlignment.Center;
		_overlayTitleLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_overlayTitleLabel.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.08f, 0.1f));
		_overlayTitleLabel.AddThemeConstantOverride("outline_size", 8);
		_overlayTitleLabel.AddThemeFontSizeOverride("font_size", 26);
		Font titleFont = UIStyle.FontNorseBold;
		if (titleFont != null)
		{
			_overlayTitleLabel.AddThemeFontOverride("font", titleFont);
		}

		content.AddChild(_overlayTitleLabel);

		var numberCenter = new CenterContainer();
		numberCenter.Name = "CountdownNumberCenter";
		numberCenter.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		numberCenter.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

		_overlayNumberLabel = new Label();
		_overlayNumberLabel.Name = "CountdownNumberLabel";
		_overlayNumberLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_overlayNumberLabel.VerticalAlignment = VerticalAlignment.Center;
		_overlayNumberLabel.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_overlayNumberLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
		_overlayNumberLabel.AddThemeConstantOverride("outline_size", 10);
		_overlayNumberLabel.AddThemeFontSizeOverride("font_size", 72);
		numberCenter.AddChild(_overlayNumberLabel);
		content.AddChild(numberCenter);

		_overlayCancelButton = CreateCancelButton("CountdownOverlayCancelButton", new Vector2(180, 48), 16, onCancel);
		_overlayCancelButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		content.AddChild(_overlayCancelButton);

		content.AddChild(new Control { Name = "CountdownBottomSpacer", CustomMinimumSize = new Vector2(0, 18) });
	}

	private void BuildCompactPanel(VBoxContainer customUIPanel, Action onCancel)
	{
		_compactPanel = new PanelContainer();
		_compactPanel.Name = "CountdownPanel";
		_compactPanel.Visible = false;
		_compactPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;

		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0.12f, 0.12f, 0.12f, 0.85f);
		panelStyle.SetBorderWidthAll(1);
		panelStyle.BorderColor = UIStyle.ColorBronze;
		panelStyle.CornerRadiusTopLeft = 4;
		panelStyle.CornerRadiusTopRight = 4;
		panelStyle.CornerRadiusBottomLeft = 4;
		panelStyle.CornerRadiusBottomRight = 4;
		panelStyle.ContentMarginLeft = 8;
		panelStyle.ContentMarginRight = 8;
		panelStyle.ContentMarginTop = 6;
		panelStyle.ContentMarginBottom = 6;
		_compactPanel.AddThemeStyleboxOverride("panel", panelStyle);

		var row = new HBoxContainer();
		row.Name = "CountdownRow";
		row.AddThemeConstantOverride("separation", 8);

		_compactLabel = new Label();
		_compactLabel.Name = "CountdownLabel";
		_compactLabel.Text = string.Empty;
		_compactLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_compactLabel.VerticalAlignment = VerticalAlignment.Center;
		_compactLabel.AddThemeFontSizeOverride("font_size", 14);
		_compactLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		row.AddChild(_compactLabel);

		_compactCancelButton = CreateCancelButton("CountdownCancelButton", new Vector2(0, 24), 12, onCancel);
		row.AddChild(_compactCancelButton);

		_compactPanel.AddChild(row);
		customUIPanel.AddChild(_compactPanel);
	}

	private static Button CreateCancelButton(string nodeName, Vector2 minimumSize, int fontSize, Action onCancel)
	{
		var button = new Button();
		button.Name = nodeName;
		button.CustomMinimumSize = minimumSize;
		button.FocusMode = Control.FocusModeEnum.None;
		button.AddThemeConstantOverride("icon_max_width", 0);
		button.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		button.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		button.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		UIStyle.ApplyButtonText(button, TranslationServer.Translate("CANCEL"), fontSize);
		button.Pressed += () => onCancel();
		return button;
	}
}
