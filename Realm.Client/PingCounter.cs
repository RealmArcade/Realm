using Godot;
using Realm.Client.Services;
using Realm.Client.UI;

namespace Realm.Client;

[GlobalClass]
public partial class PingCounter : Label
{
	private const float RefreshIntervalSeconds = 0.25f;
	private const float ForceShowDelaySeconds = 1.0f;
	private const float ForceHideDelaySeconds = 2.5f;

	private static readonly Color GreenColor = new(0.25f, 0.9f, 0.35f);
	private static readonly Color YellowColor = new(0.95f, 0.85f, 0.2f);
	private static readonly Color OrangeColor = new(0.95f, 0.55f, 0.15f);
	private static readonly Color RedColor = new(0.95f, 0.2f, 0.2f);

	private float _refreshTimer;
	private float _forceShowTimer;
	private float _forceHideTimer;
	private bool _forcedByLatency;

	public override void _Ready()
	{
		AddThemeColorOverride("font_color", GreenColor);
		AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.08f, 0.1f));
		AddThemeConstantOverride("outline_size", 4);
		AddThemeFontSizeOverride("font_size", 16);
		ZIndex = 100;
		Visible = false;
	}

	public override void _Process(double delta)
	{
		float frameDelta = (float)delta;
		NetworkLatencyService? latency = GameContext.Host != null ? GameContext.NetworkLatencyService : null;
		bool hasSample = latency is { HasSample: true };
		float roundTripMilliseconds = hasSample ? latency!.SmoothedRoundTripMilliseconds : 0f;

		UpdateForcedVisibility(hasSample && roundTripMilliseconds >= NetworkLatencyService.RedThresholdMilliseconds, frameDelta);

		bool shouldShow = hasSample && (GameSettings.DisplayPing || _forcedByLatency);
		if (Visible != shouldShow) Visible = shouldShow;
		if (!shouldShow) return;

		_refreshTimer += frameDelta;
		if (_refreshTimer < RefreshIntervalSeconds) return;

		_refreshTimer = 0f;
		Text = string.Format(TranslationServer.Translate("Ping: {0} ms"), Mathf.RoundToInt(roundTripMilliseconds));
		AddThemeColorOverride("font_color", ResolveColor(roundTripMilliseconds));
	}

	private void UpdateForcedVisibility(bool isRed, float frameDelta)
	{
		if (!isRed)
		{
			_forceShowTimer = 0f;
			if (!_forcedByLatency) return;

			_forceHideTimer += frameDelta;
			if (_forceHideTimer < ForceHideDelaySeconds) return;

			_forceHideTimer = 0f;
			_forcedByLatency = false;
			return;
		}

		_forceHideTimer = 0f;
		if (_forcedByLatency) return;

		_forceShowTimer += frameDelta;
		if (_forceShowTimer < ForceShowDelaySeconds) return;

		_forceShowTimer = 0f;
		_forcedByLatency = true;
	}

	private static Color ResolveColor(float roundTripMilliseconds)
	{
		if (roundTripMilliseconds <= NetworkLatencyService.GreenThresholdMilliseconds) return GreenColor;
		if (roundTripMilliseconds <= NetworkLatencyService.YellowThresholdMilliseconds) return YellowColor;
		if (roundTripMilliseconds <= NetworkLatencyService.RedThresholdMilliseconds) return OrangeColor;
		return RedColor;
	}
}
