using Godot;
using Realm.Ecs.Components.Core;

namespace Realm.Client.Services;

/// <summary>
///     Remembers the last brush shape, rotation, scale and brush size the editor's sliders were set to, and reports
///     whether a new value actually differs. The HUD writes through here so a re-layout or a reopened panel does not
///     apply the same value twice, and the editor does not record an undo entry for a setting that did not change.
/// </summary>
public sealed class EditorPlacementSettings
{
	private bool _hasLastBrushShape;
	private bool _lastBrushShapeIsSquare;

	public float LastRotation { get; private set; } = float.NaN;

	public float LastPasteRotation { get; private set; } = float.NaN;

	public PasteReflection LastPasteReflection { get; private set; } = (PasteReflection)(-1);

	public float LastScale { get; private set; } = float.NaN;

	public float LastBrushSize { get; private set; } = float.NaN;

	/// <summary>Returns false when the HUD is repeating the shape it already set.</summary>
	public bool TrySetBrushShape(bool isSquare)
	{
		if (_hasLastBrushShape && _lastBrushShapeIsSquare == isSquare) return false;

		_hasLastBrushShape = true;
		_lastBrushShapeIsSquare = isSquare;
		return true;
	}

	public void UpdatePasteReflection(PasteReflection reflection)
	{
		if (LastPasteReflection == reflection) return;

		LastPasteReflection = reflection;
	}

	public bool SetLastRotation(float angle)
	{
		if (Mathf.IsEqualApprox(LastRotation, angle)) return false;

		LastRotation = angle;
		return true;
	}

	public bool SetLastPasteRotation(float angle)
	{
		if (Mathf.IsEqualApprox(LastPasteRotation, angle)) return false;

		LastPasteRotation = angle;
		return true;
	}

	public bool SetLastScale(float scale)
	{
		if (Mathf.IsEqualApprox(LastScale, scale)) return false;

		LastScale = scale;
		return true;
	}

	public bool SetLastBrushSize(float size)
	{
		if (Mathf.IsEqualApprox(LastBrushSize, size)) return false;

		LastBrushSize = size;
		return true;
	}
}
