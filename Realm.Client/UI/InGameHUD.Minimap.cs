// The minimap's per-frame and toggle surface: the shroud timer the frame loop pumps, the redraw and
// terrain/colour-scheme toggles, and the camera-centring helpers the minimap and the portrait share.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI.InGame;
using Realm.Client.Utils;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Vector3 = Godot.Vector3;
using static Godot.Control;
using static Godot.CanvasItem;


namespace Realm.Client.UI;

public partial class InGameHUD
{
	private void UpdateShroudMinimapTimer(double delta)
	{
		_shroudMinimapRefreshTimer += (float)delta;
		if (_shroudMinimapRefreshTimer >= 0.1f)
		{
			_shroudMinimapRefreshTimer = 0f;
			QueueMinimapRedraw();
		}
	}

	public void QueueMinimapRedraw() => MinimapCapture.QueueRedraw();

	private const float FeedbackHoldSeconds = 0.5f;

	private const float FeedbackFadeSeconds = 1.5f;

	private const int FeedbackWarningOutlineSize = 6;

	/// <summary>
	///     The colour a refused action is stated in: the one red the player already meets in a warning dialog, so a hint about
	///     something that did not happen reads the same wherever it appears.
	/// </summary>
	private static readonly Color FeedbackWarningColor = new(1.0f, 0.25f, 0.2f);

	private static readonly Color FeedbackWarningOutlineColor = new(0.0f, 0.0f, 0.0f, 0.85f);

	private Tween? _feedbackTween;

	public void ShowFeedbackText(string text, Color color)
	{
		ShowFeedbackMessage(text, color, isWarning: false);
	}

	/// <summary>
	///     States why something the player just asked for did not happen - an unaffordable price, a full queue, an order the
	///     match refused. It is drawn in the warning red with an outline that keeps it readable over any battlefield, and the
	///     warning sound carries it to a player whose eyes are on the fight rather than on the middle of the screen: a hint
	///     about a click that did nothing is worth nothing if it can be missed.
	/// </summary>
	public void ShowFeedbackWarning(string text)
	{
		ShowFeedbackMessage(text, FeedbackWarningColor, isWarning: true);
		GameContext.UserInterface?.PlayWarningSound();
	}

	private void ShowFeedbackMessage(string text, Color color, bool isWarning)
	{
		// One message is on screen at a time, so the fade the previous one is running is ended first. A player who presses an
		// unaffordable button twice was otherwise told the second time by a message already on its way out.
		_feedbackTween?.Kill();
		_feedbackTween = null;

		_feedbackLabel.Text = UIHelper.StripBbCode(TranslationServer.Translate(text));
		_feedbackLabel.AddThemeColorOverride("font_color", color);
		_feedbackLabel.AddThemeConstantOverride("outline_size", isWarning ? FeedbackWarningOutlineSize : 0);
		_feedbackLabel.AddThemeColorOverride("font_outline_color", FeedbackWarningOutlineColor);

		// The colour is stated once, by the font. Tinting the whole label with it as well drew a red message in squared red,
		// which is darker than the colour asked for and hides the outline that keeps it legible.
		_feedbackLabel.Modulate = Colors.White;

		_feedbackTween = CreateTween();
		_feedbackTween.TweenProperty(_feedbackLabel, "modulate:a", 0.0f, FeedbackFadeSeconds).SetDelay(FeedbackHoldSeconds);
	}

	public void ToggleMinimapTerrain()
	{
		_viewModel.ShowMinimapTerrain = !_viewModel.ShowMinimapTerrain;
	}

	public void ToggleMinimapColorScheme()
	{
		_viewModel.MinimapColorScheme = _viewModel.MinimapColorScheme == MinimapColorScheme.TeamColors
			? MinimapColorScheme.PlayerColors
			: MinimapColorScheme.TeamColors;

		string schemeName = _viewModel.MinimapColorScheme == MinimapColorScheme.TeamColors
			? TranslationServer.Translate("Team Colors")
			: TranslationServer.Translate("Player Colors");

		ShowFeedbackText(string.Format(TranslationServer.Translate("Minimap Colors: {0}"), schemeName), UIStyle.ColorGold);
		QueueMinimapRedraw();
	}

	private void SetupPortrait() => PortraitFrameSetup.Prepare(this);

	private void CenterCameraOnSelectedUnit()
	{
		var selected = GameContext.Host?.SelectedUnits;
		if (selected != null && selected.Count > 0)
		{
			int idx = GameContext.Host != null ? GameContext.Host.CycleSelectionIndex : 0;
			if (idx >= 0 && idx < selected.Count)
				CenterCameraOnUnit(selected[idx]);
		}
	}

	private void CenterCameraOnUnit(Unit3D unit)
	{
		if (unit == null || !GodotObject.IsInstanceValid(unit)) return;
		if (_camera3D != null && GodotObject.IsInstanceValid(_camera3D))
		{
			_camera3D.GlobalPosition = new Vector3(unit.GlobalPosition.X, _camera3D.GlobalPosition.Y, unit.GlobalPosition.Z + 25f);
			ShowFeedbackText($"Camera → {unit.UnitId.ToUpper()}", new Color(0.9f, 0.8f, 0.5f));
		}
	}
}
