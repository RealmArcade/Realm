using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Core;

namespace Realm.Client.Services;

/// <summary>
/// The editor's symmetry settings: the pivot and fold count the mirrored paste and brush paths use, plus the
/// mirrored transform queries built on them. The editor holds one and passes it down, so the settings live
/// with the maths that reads them instead of sitting loose among the editor's other state.
/// </summary>
public sealed class EditorSymmetry
{
	private int _folds = 4;

	public Vector2 Pivot { get; set; } = Vector2.Zero;

	public int Folds
	{
		get => _folds;
		set => _folds = Math.Clamp(value, 2, 32);
	}

	public List<MirroredTransform> GetTransforms(
		Godot.Vector3 pos,
		float rotation,
		MirrorMode mirrorMode,
		Vector2? pivot = null,
		int? folds = null) =>
		EditorGridSnapping.GetMirroredTransforms(pos, rotation, mirrorMode, pivot ?? Pivot, folds ?? Folds);

	public List<Godot.Vector3> GetPositions(Godot.Vector3 pos, MirrorMode mirrorMode) =>
		EditorGridSnapping.GetMirroredPositions(pos, mirrorMode, Pivot, Folds);
}
