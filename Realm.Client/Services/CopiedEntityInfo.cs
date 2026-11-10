using Godot;

namespace Realm.Client.Services;

public class CopiedEntityInfo
{
	public string Type = null!;
	public string Id = null!;
	public Vector3 RelativePos;
	public float Rotation;
	public float Scale;
	public int Player;
}