using Godot;

namespace Realm.Client;

public class SavedUnit
{
	public string Id = null!;
	public Vector3 Position;
	public float RotationY;
	public float Scale;
	public int Player;
	public int Level;
}
