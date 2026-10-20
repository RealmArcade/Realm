using Godot;

namespace Realm.Client.Core;

public struct MinimapRayResult
{
	public Vector3 HitPos;
	public Unit3D ClickedUnit;
	public Prop3D ClickedProp;
}