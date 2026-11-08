using Godot;
using System;

namespace Realm.Client.UI.InGame;

/// <summary>
///     The two containers the HUD creates rather than finds: the top-right dock the match's own panels stack in (the
///     countdown and the scoreboard), and the control-group strip down the left edge. Both are empty boxes positioned
///     by anchors, so the whole of them is a couple of offsets.
/// </summary>
public static class CustomUiDock
{
	public sealed record Parts(VBoxContainer Panel, HBoxContainer ControlGroups, CountdownOverlayPanel Countdown);

	/// <param name="host">The HUD, which both containers become children of.</param>
	/// <param name="onCountdownCancel">Asked for when a player cancels the countdown they see.</param>
	public static Parts Create(Control host, Action onCountdownCancel)
	{
		HBoxContainer controlGroups = CreateControlGroupStrip();
		VBoxContainer panel = CreateDock();

		// The strip is added first so it stays behind the dock; both are anchor-positioned and never overlap.
		host.AddChild(controlGroups);
		host.AddChild(panel);

		return new Parts(panel, controlGroups, new CountdownOverlayPanel(host, panel, onCountdownCancel));
	}

	private static HBoxContainer CreateControlGroupStrip()
	{
		var container = new HBoxContainer();
		container.Name = "ControlGroupsContainer";
		container.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
		container.OffsetLeft = 20;
		container.OffsetTop = 80;
		container.AddThemeConstantOverride("separation", 8);
		return container;
	}

	private static VBoxContainer CreateDock()
	{
		var panel = new VBoxContainer();
		panel.Name = "CustomUIPanel";
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
		panel.GrowHorizontal = Control.GrowDirection.Begin;
		panel.OffsetLeft = -440;
		panel.OffsetRight = -20;
		panel.OffsetTop = 20;
		panel.CustomMinimumSize = new Vector2(420, 0);
		panel.AddThemeConstantOverride("separation", 10);
		return panel;
	}
}
