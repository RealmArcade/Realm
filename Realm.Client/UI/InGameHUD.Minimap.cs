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

	public void ShowFeedbackText(string text, Color color)
	{
		_feedbackLabel.Text = UIHelper.StripBbCode(TranslationServer.Translate(text));
		_feedbackLabel.AddThemeColorOverride("font_color", color);
		_feedbackLabel.Modulate = new Color(color.R, color.G, color.B, 1.0f);

		var tween = CreateTween();
		tween.TweenProperty(_feedbackLabel, "modulate:a", 0.0f, 1.5f).SetDelay(0.5f);
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
