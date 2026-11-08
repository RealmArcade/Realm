using System;
using Godot;

namespace Realm.Client.Services;

/// <summary>
/// What the editor's pointer is doing between frames: the area selection being dragged, the object marquee,
/// whether a copied object is being placed, and the random rotation and scale a placement reuses. It is a
/// plain state holder - the editor asks it to start, extend and clear, and reads it back for overlays.
/// </summary>
public sealed class EditorInteractionState
{
	private Vector2I? _selectionStart;
	private Vector2I? _selectionEnd;
	private bool _isSelectingArea;

	private Vector2I? _objectMarqueeStart;
	private Vector2I? _objectMarqueeEnd;
	private bool _isObjectMarqueeSelecting;

	private float _cachedRandomRotation;
	private float _cachedRandomScale = 1.0f;
	private bool _hasCachedRandom;
	private bool _isPastingObject;

	public bool IsPastingObject => _isPastingObject;
	public bool HasCachedRandom => _hasCachedRandom;
	public float CachedRandomRotation => _cachedRandomRotation;
	public float CachedRandomScale => _cachedRandomScale;
	public Vector2I? SelectionStart => _selectionStart;
	public Vector2I? SelectionEnd => _selectionEnd;
	public bool IsSelectingArea => _isSelectingArea;

	public Vector2I? ObjectMarqueeStart => _objectMarqueeStart;
	public Vector2I? ObjectMarqueeEnd => _objectMarqueeEnd;
	public bool IsObjectMarqueeSelecting => _isObjectMarqueeSelecting;

	public void ClearSelection()
	{
		_selectionStart = null;
		_selectionEnd = null;
	}

	public void SetIsPastingObject(bool value)
	{
		_isPastingObject = value;
	}

	public void SetSelectionStart(Vector2I? value)
	{
		_selectionStart = value;
	}

	public void SetSelectionEnd(Vector2I? value)
	{
		_selectionEnd = value;
	}

	public void SetIsSelectingArea(bool value)
	{
		_isSelectingArea = value;
	}

	public void SetObjectMarquee(Vector2I? start, Vector2I? end, bool selecting)
	{
		_objectMarqueeStart = start;
		_objectMarqueeEnd = end;
		_isObjectMarqueeSelecting = selecting;
	}

	public void ClearObjectMarquee()
	{
		_objectMarqueeStart = null;
		_objectMarqueeEnd = null;
		_isObjectMarqueeSelecting = false;
	}

	public void GenerateNewRandomPlacementRotationAndScale()
	{
		_cachedRandomRotation = (float)(GD.Randf() * 360.0);
		_cachedRandomScale = 0.2f + (float)(GD.Randf() * 2.8);
		_hasCachedRandom = true;
	}

	public void InvalidateCachedRandom()
	{
		_hasCachedRandom = false;
	}

	public (int minX, int minZ, int maxX, int maxZ) GetSelectionBounds()
	{
		if (SelectionStart == null || SelectionEnd == null) return (0, 0, 0, 0);
		int minX = Mathf.Min(SelectionStart.Value.X, SelectionEnd.Value.X);
		int minZ = Mathf.Min(SelectionStart.Value.Y, SelectionEnd.Value.Y);
		int maxX = Mathf.Max(SelectionStart.Value.X, SelectionEnd.Value.X);
		int maxZ = Mathf.Max(SelectionStart.Value.Y, SelectionEnd.Value.Y);
		return (minX, minZ, maxX, maxZ);
	}
}
