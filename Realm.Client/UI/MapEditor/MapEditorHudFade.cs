using System;
using System.Collections.Generic;
using Godot;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the fade needs from the editor: the node it creates tweens from, the chrome that should step aside while a tool
/// is in use, the cards (which the drag controller owns and keeps up to date), and the preference that decides whether
/// the chrome steps aside at all.
/// </summary>
public sealed record HudFadeHooks(
	Node Owner,
	Control? TopLeftBox,
	Control? PanelLeft,
	Control? PanelRight,
	Control? TopBar,
	Control? MeasurementPanel,
	Control? ScreenFrame,
	IReadOnlyCollection<Control> Cards,
	Func<bool> HideHudDuringToolUsage);

/// <summary>
/// The transient "the pointer is driving the 3D view" state: while a tool drag is in progress the chrome stops taking
/// mouse input and fades out, then both come back when the drag ends. The saved filters and the running tween are the
/// only state, so the first call after construction starts from the resting state.
/// </summary>
public sealed class MapEditorHudFade
{
	private readonly HudFadeHooks _hooks;
	private readonly Dictionary<Control, MouseFilterEnum> _savedFilters = new();
	private Tween? _tween;

	public MapEditorHudFade(HudFadeHooks hooks) => _hooks = hooks;

	/// <summary>
	/// True while the chrome is stepped aside. The editor's hover test asks this rather than the nodes, because the
	/// chrome still occupies the same space and only its input has been taken away.
	/// </summary>
	public bool IsActive { get; private set; }

	/// <summary>
	/// Entering or leaving 3D interaction. Leaving always restores input and visibility even when the preference has
	/// turned fades off in the meantime, which is why the early return only applies on the way in.
	/// </summary>
	public void SetInteractionActive(bool active)
	{
		if (IsActive == active) return;
		IsActive = active;

		if (!_hooks.HideHudDuringToolUsage() && active)
		{
			return;
		}

		UpdateMouseFilters(active);
		Animate(active);
	}

	/// <summary>
	/// Drops the running tween and gives every control its filter back. The editor calls this when it leaves the tree, so
	/// a fade interrupted by a scene change cannot leave the chrome permanently transparent.
	/// </summary>
	public void Release()
	{
		if (_tween != null && _tween.IsValid())
		{
			_tween.Kill();
		}

		RestoreFilters();
	}

	/// <summary>
	/// Takes the chrome's mouse input away while a drag is in flight. Every control whose filter changes is remembered
	/// with its original value, so restoring cannot flatten a control that was deliberately transparent.
	/// </summary>
	private void UpdateMouseFilters(bool active)
	{
		if (active)
		{
			_savedFilters.Clear();
			IgnoreRecursive(_hooks.PanelLeft);
			IgnoreRecursive(_hooks.PanelRight);
			IgnoreRecursive(_hooks.TopLeftBox);
			IgnoreRecursive(_hooks.TopBar);

			foreach (var card in _hooks.Cards)
			{
				IgnoreRecursive(card);
			}
		}
		else
		{
			RestoreFilters();
		}
	}

	private void RestoreFilters()
	{
		foreach (var (ctrl, filter) in _savedFilters)
		{
			if (GodotObject.IsInstanceValid(ctrl))
			{
				ctrl.MouseFilter = filter;
			}
		}

		_savedFilters.Clear();
	}

	private void IgnoreRecursive(Node? node)
	{
		if (node == null || !GodotObject.IsInstanceValid(node)) return;

		if (node is Control ctrl && ctrl.MouseFilter != MouseFilterEnum.Ignore)
		{
			_savedFilters[ctrl] = ctrl.MouseFilter;
			ctrl.MouseFilter = MouseFilterEnum.Ignore;
		}

		foreach (Node child in node.GetChildren())
		{
			IgnoreRecursive(child);
		}
	}

	private void Animate(bool active)
	{
		if (_tween != null && _tween.IsValid())
		{
			_tween.Kill();
		}

		_tween = _hooks.Owner.CreateTween();
		_tween.SetParallel(true);
		float targetAlpha = (active && _hooks.HideHudDuringToolUsage()) ? 0.0f : 1.0f;
		float duration = 0.35f;

		TweenElement(_hooks.TopLeftBox, targetAlpha, duration);
		TweenElement(_hooks.PanelLeft, targetAlpha, duration);
		TweenElement(_hooks.PanelRight, targetAlpha, duration);
		TweenElement(_hooks.TopBar, targetAlpha, duration);
		TweenElement(_hooks.MeasurementPanel, targetAlpha, duration);
		TweenElement(_hooks.ScreenFrame, targetAlpha, duration);

		foreach (var card in _hooks.Cards)
		{
			TweenElement(card, targetAlpha, duration);
		}
	}

	private void TweenElement(Control? element, float targetAlpha, float duration)
	{
		if (!GodotObject.IsInstanceValid(element) || _tween == null) return;

		_tween.TweenProperty(element, "modulate:a", targetAlpha, duration)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
	}
}
