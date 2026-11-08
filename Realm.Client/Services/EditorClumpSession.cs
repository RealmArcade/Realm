using System.Collections.Generic;
using Godot;

namespace Realm.Client.Services;

/// <summary>
/// One prop clump being placed: whether the drag is still open, the spawn actions it has produced so far,
/// the points and radii used to keep the pieces apart, and the cooldown that gates the next clump.
/// </summary>
internal sealed class EditorClumpSession
{
	private readonly List<IEditorAction> _actions = new();
	private readonly List<(Vector3 Position, float Radius)> _points = new();
	private float _cooldown;

	public bool IsDrawing { get; private set; }

	public IReadOnlyList<(Vector3 Position, float Radius)> Points => _points;

	public void TickCooldown(float delta)
	{
		if (_cooldown > 0.0f)
			_cooldown -= delta;
	}

	public bool CanSpawn => _cooldown <= 0.0f;

	public void SetCooldown(float value) => _cooldown = value;

	public void Begin()
	{
		IsDrawing = true;
		_actions.Clear();
		_points.Clear();
	}

	public void Record(IEditorAction action)
	{
		_actions.Add(action);
		if (action is ObjectSpawnAction objAct && GodotObject.IsInstanceValid(objAct.SpawnedNode))
		{
			float baseRadius = 0.5f;
			_points.Add((objAct.Position, baseRadius * objAct.Scale));
		}
	}

	public CompositeAction? End()
	{
		IsDrawing = false;
		if (_actions.Count > 0)
		{
			return new CompositeAction(_actions);
		}

		return null;
	}

	public void Reset()
	{
		_cooldown = 0.0f;
	}
}
