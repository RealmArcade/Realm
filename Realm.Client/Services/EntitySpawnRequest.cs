using Godot;

namespace Realm.Client.Services;

public struct EntitySpawnRequest
{
	public string Type;
	public string Id;
	public Vector3 Position;
	public float Rotation;
	public float Scale;
	public int Player;
}