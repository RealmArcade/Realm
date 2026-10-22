using Godot;
using Realm.Ecs;

namespace Realm.Client;

public struct PropData
{
	public Entity Entity;
	public string PropId;
	public Vector3 Position;
	public float RotationY;
	public float Scale;
	public float YOffset;
}