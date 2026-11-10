using System;
using Godot;
using Realm.Client.Services;
using Realm.Ecs.Components.Core;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the symmetry setters need from the editor: the dropdown refreshes they trigger, the viewport accordion they
/// may expand, and the feedback line. The setters themselves change editor state through EditorService, so they do
/// not need the editor's panels - only its refresh paths.
/// </summary>
public sealed record SymmetryHooks(
	Action RefreshMirrorDropdowns,
	Action RefreshSymmetrySubControls,
	Action RefreshSpokeLabel,
	Action RefreshRingLabel,
	Action ExpandViewportAccordion,
	Action<string> Feedback);

/// <summary>
/// The four actions behind the symmetry controls: mirror mode, symmetry spokes, polar ring spacing and polar spokes.
/// Each writes its value through EditorService and then refreshes only the surfaces that read it, which is why the
/// refreshes arrive as callbacks rather than the controller reaching into the panels that show them.
/// </summary>
public sealed class MapEditorSymmetry
{
	private readonly SymmetryHooks _hooks;

	public MapEditorSymmetry(SymmetryHooks hooks)
	{
		_hooks = hooks;
	}

	public void SetMirrorMode(MirrorMode mode)
	{
		GameContext.EditorService.SetMirrorModeDomain(mode);
		_hooks.RefreshMirrorDropdowns();
		_hooks.RefreshSymmetrySubControls();
		_hooks.RefreshSpokeLabel();
		if (mode == MirrorMode.Rotational)
		{
			// Rotational symmetry is driven from the viewport's polar controls, so the accordion they live in opens
			// the first time the mode that depends on them is chosen.
			_hooks.ExpandViewportAccordion();
		}

		string modeName = mode == MirrorMode.Both ? "QUAD" : mode.ToString().ToUpperInvariant();
		_hooks.Feedback(string.Format(TranslationServer.Translate("Mirroring: {0}"), modeName));
	}

	public void SetSymmetrySpokes(int spokes)
	{
		GameContext.EditorService.SetSymmetrySpokesDomain(spokes);
		// The spoke label also carries the fold count in the rotational dropdown entry, hence both refreshes.
		_hooks.RefreshSpokeLabel();
		_hooks.RefreshMirrorDropdowns();
		_hooks.Feedback(string.Format(TranslationServer.Translate("Symmetry Spokes: {0}-way"), spokes));
	}

	public void SetPolarRingSpacing(float spacing)
	{
		GameContext.EditorService.SetPolarRingSpacingDomain(spacing);
		_hooks.RefreshRingLabel();
		_hooks.Feedback(string.Format(TranslationServer.Translate("Polar Ring Spacing: {0:F0} tiles"), spacing));
	}

	/// <summary>
	/// The polar spoke count is the same number as the symmetry fold count while rotational symmetry is active, so in
	/// that mode this defers to the symmetry setter instead of writing a value the mode would ignore.
	/// </summary>
	public void SetPolarSpokesView(int spokes)
	{
		if (GameContext.Host == null) return;
		if (GameContext.Host.EditorMirrorMode == MirrorMode.Rotational)
		{
			SetSymmetrySpokes(spokes);
			return;
		}

		GameContext.EditorService.SetPolarSpokesDomain(spokes);
		_hooks.RefreshSpokeLabel();
		_hooks.Feedback(string.Format(TranslationServer.Translate("Polar Spokes: {0}-way"), spokes));
	}
}
