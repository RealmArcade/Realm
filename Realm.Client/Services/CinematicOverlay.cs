using Godot;
using Realm.Client.UI;

namespace Realm.Client.Services;

/// <summary>
///     The dressing a cutscene puts over the screen: the two bars that frame it, the line of dialogue under it, and the fade
///     a scene change happens behind. It is one layer over the whole game rather than part of the HUD, because a cutscene
///     frames the HUD as well; it is created when a map first asks for something and it lives exactly as long as the match
///     that asked, so a match that never runs a cinematic never carries it.
/// </summary>
[GlobalClass]
public partial class CinematicOverlay : CanvasLayer
{
	private const float BarHeightFraction = 0.1f;

	private const float SubtitleBottomMarginFraction = 0.135f;

	private const float MinimumBarHeight = 24f;

	private const float MaximumSubtitleWidth = 1100f;

	private const float SubtitleWidthFraction = 0.7f;

	private const float SubtitleFadeMaximumSeconds = 0.35f;

	private Control _topBar = null!;
	private Control _bottomBar = null!;
	private ColorRect _fade = null!;
	private Label _subtitleSpeaker = null!;
	private Label _subtitleLine = null!;
	private Tween? _fadeTween;
	private Tween? _subtitleTween;

	public override void _Ready()
	{
		_topBar = CreateBar("CinematicTopBar_ColorRect", anchoredToBottom: false);
		_bottomBar = CreateBar("CinematicBottomBar_ColorRect", anchoredToBottom: true);
		_fade = CreateFade();
		_subtitleSpeaker = CreateSubtitleLabel("CinematicSubtitleSpeaker_Label", 18, UIStyle.ColorCyanGlow);
		_subtitleLine = CreateSubtitleLabel("CinematicSubtitleLine_Label", 22, Colors.White);

		LayoutBars();
		GetViewport().SizeChanged += OnViewportSizeChanged;
	}

	/// <summary>
	///     Draws or clears the bars above and below the screen.
	/// </summary>
	/// <param name="visible">True to draw the bars.</param>
	public void SetBarsVisible(bool visible)
	{
		if (!IsNodeReady()) return;

		_topBar.Visible = visible;
		_bottomBar.Visible = visible;
		if (visible) LayoutBars();
	}

	/// <summary>
	///     Shows one line of dialogue, with its speaker above it, replacing whatever line was showing.
	/// </summary>
	/// <param name="speaker">Who is speaking, or an empty string for narration.</param>
	/// <param name="line">The line itself, or an empty line to clear the subtitle.</param>
	/// <param name="durationSeconds">How long it stays on screen, or zero to clear it at once.</param>
	public void ShowSubtitle(string speaker, string line, float durationSeconds)
	{
		if (!IsNodeReady()) return;

		KillTween(ref _subtitleTween);

		if (string.IsNullOrWhiteSpace(line) || durationSeconds <= 0f)
		{
			ClearSubtitle();
			return;
		}

		_subtitleSpeaker.Text = speaker ?? string.Empty;
		_subtitleSpeaker.Visible = !string.IsNullOrWhiteSpace(speaker);
		_subtitleLine.Text = line;
		_subtitleLine.Visible = true;
		_subtitleSpeaker.Modulate = Colors.White;
		_subtitleLine.Modulate = Colors.White;
		LayoutSubtitle();

		// The line leaves by itself, so a map showing lines in sequence only has to state the next one.
		float fadeSeconds = Mathf.Min(SubtitleFadeMaximumSeconds, Mathf.Max(durationSeconds * 0.2f, 0.05f));
		_subtitleTween = CreateTween();
		if (_subtitleTween == null) return;

		_subtitleTween.TweenInterval(Mathf.Max(durationSeconds - fadeSeconds, 0f));
		_subtitleTween.TweenProperty(_subtitleSpeaker, "modulate:a", 0f, fadeSeconds);
		_subtitleTween.Parallel().TweenProperty(_subtitleLine, "modulate:a", 0f, fadeSeconds);
		_subtitleTween.TweenCallback(Callable.From(ClearSubtitle));
	}

	/// <summary>
	///     Fades the screen toward black, or back from it, over a stated time.
	/// </summary>
	/// <param name="alpha">How opaque the fade becomes, from 0 for a clear screen to 1 for black.</param>
	/// <param name="durationSeconds">How long the fade takes, or zero to change it at once.</param>
	public void SetFade(float alpha, float durationSeconds)
	{
		if (!IsNodeReady()) return;

		float clampedAlpha = Mathf.Clamp(alpha, 0f, 1f);
		KillTween(ref _fadeTween);

		Color target = new(0f, 0f, 0f, clampedAlpha);
		if (durationSeconds <= 0f)
		{
			_fade.Color = target;
			_fade.Visible = clampedAlpha > 0f;
			return;
		}

		_fade.Visible = true;
		_fadeTween = CreateTween();
		if (_fadeTween == null)
		{
			_fade.Color = target;
			return;
		}

		_fadeTween.TweenProperty(_fade, "color", target, durationSeconds);
		_fadeTween.TweenCallback(Callable.From(() =>
		{
			if (!IsNodeReady()) return;
			_fade.Visible = _fade.Color.A > 0f;
		}));
	}

	/// <summary>
	///     Removes everything the overlay put on screen, which is what a match ending or a released cutscene needs.
	/// </summary>
	public void ClearContent()
	{
		KillTween(ref _fadeTween);
		KillTween(ref _subtitleTween);
		if (!IsNodeReady()) return;

		_topBar.Visible = false;
		_bottomBar.Visible = false;
		_fade.Visible = false;
		_fade.Color = new Color(0f, 0f, 0f, 0f);
		ClearSubtitle();
	}

	private bool IsNodeReady() => GodotObject.IsInstanceValid(_topBar) && GodotObject.IsInstanceValid(_fade) && GodotObject.IsInstanceValid(_subtitleLine);

	private ColorRect CreateBar(string name, bool anchoredToBottom)
	{
		var bar = new ColorRect
		{
			Name = name,
			Color = Colors.Black,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};

		AddChild(bar);
		bar.SetAnchorsPreset(anchoredToBottom ? Control.LayoutPreset.BottomWide : Control.LayoutPreset.TopWide);
		return bar;
	}

	private ColorRect CreateFade()
	{
		var fade = new ColorRect
		{
			Name = "CinematicFade_ColorRect",
			Color = new Color(0f, 0f, 0f, 0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};

		AddChild(fade);
		fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		fade.OffsetLeft = 0f;
		fade.OffsetTop = 0f;
		fade.OffsetRight = 0f;
		fade.OffsetBottom = 0f;
		return fade;
	}

	private Label CreateSubtitleLabel(string name, int fontSize, Color color)
	{
		var label = new Label
		{
			Name = name,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Visible = false
		};

		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		label.AddThemeConstantOverride("outline_size", 6);
		AddChild(label);
		return label;
	}

	private void OnViewportSizeChanged()
	{
		if (!IsNodeReady()) return;

		LayoutBars();
		LayoutSubtitle();
	}

	private void LayoutBars()
	{
		Viewport? viewport = GetViewport();
		if (viewport == null) return;

		float height = Mathf.Max(Mathf.Round(viewport.GetVisibleRect().Size.Y * BarHeightFraction), MinimumBarHeight);

		_topBar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
		_topBar.OffsetLeft = 0f;
		_topBar.OffsetRight = 0f;
		_topBar.OffsetTop = 0f;
		_topBar.OffsetBottom = height;

		_bottomBar.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		_bottomBar.OffsetLeft = 0f;
		_bottomBar.OffsetRight = 0f;
		_bottomBar.OffsetTop = -height;
		_bottomBar.OffsetBottom = 0f;
	}

	private void LayoutSubtitle()
	{
		Viewport? viewport = GetViewport();
		if (viewport == null) return;

		Vector2 size = viewport.GetVisibleRect().Size;
		float width = Mathf.Min(size.X * SubtitleWidthFraction, MaximumSubtitleWidth);
		float left = (size.X - width) * 0.5f;
		float bottomMargin = size.Y * SubtitleBottomMarginFraction;

		LayoutSubtitleLabel(_subtitleLine, left, width, -bottomMargin - 64f, -bottomMargin);
		LayoutSubtitleLabel(_subtitleSpeaker, left, width, -bottomMargin - 96f, -bottomMargin - 64f);
	}

	private void LayoutSubtitleLabel(Label label, float left, float width, float offsetTop, float offsetBottom)
	{
		label.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		label.OffsetLeft = left;
		label.OffsetRight = -left;
		label.OffsetTop = offsetTop;
		label.OffsetBottom = offsetBottom;
		label.CustomMinimumSize = new Vector2(width, 0f);
		label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
	}

	private void ClearSubtitle()
	{
		if (!IsNodeReady()) return;

		_subtitleSpeaker.Visible = false;
		_subtitleLine.Visible = false;
		_subtitleSpeaker.Text = string.Empty;
		_subtitleLine.Text = string.Empty;
	}

	private static void KillTween(ref Tween? tween)
	{
		if (tween != null && GodotObject.IsInstanceValid(tween)) tween.Kill();
		tween = null;
	}
}
