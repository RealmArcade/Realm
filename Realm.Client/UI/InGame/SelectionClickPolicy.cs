using Godot;
using Realm.Client.Services;
using System;
using System.Collections.Generic;

namespace Realm.Client.UI.InGame;

/// <summary>
///     What a click on a unit in the selection strip means. It is separate from the strip because the modifiers are the
///     whole of the behaviour: plain click selects that unit alone, shift removes it, control keeps its type and drops the
///     rest, which is how RTS players expect a unit group to be pruned.
/// </summary>
public static class SelectionClickPolicy
{
	/// <summary>
	///     Applies the click at <paramref name="index" /> in the current selection. Returns false when there is nothing to
	///     act on, so the caller can skip refreshing a HUD that did not change. <paramref name="centerOn" /> is called for
	///     the plain-click case only: a pruned selection is not a camera move.
	/// </summary>
	public static bool Apply(int index, Action<Unit3D> centerOn)
	{
		if (GameContext.Host == null) return false;

		List<Unit3D>? selectedUnits = GameContext.Host.SelectedUnits;
		if (selectedUnits == null || index < 0 || index >= selectedUnits.Count) return false;

		Unit3D clickedUnit = selectedUnits[index];
		bool shiftPressed = Input.IsKeyPressed(Key.Shift);
		bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);

		if (ctrlPressed)
		{
			// Everything of another type goes, in one pass over a copy: deselecting while iterating the live list would
			// walk a collection the deselect is rewriting.
			string targetId = clickedUnit.UnitId;
			var toDeselect = new List<Unit3D>();
			foreach (Unit3D unit in selectedUnits)
			{
				if (unit.UnitId != targetId)
				{
					toDeselect.Add(unit);
				}
			}

			foreach (Unit3D unit in toDeselect)
			{
				GameContext.Host.DeselectUnit(unit);
			}
		}
		else if (shiftPressed)
		{
			GameContext.Host.DeselectUnit(clickedUnit);
		}
		else
		{
			GameContext.Host.SelectOnlyUnit(clickedUnit);
			centerOn(clickedUnit);
		}

		return true;
	}
}
