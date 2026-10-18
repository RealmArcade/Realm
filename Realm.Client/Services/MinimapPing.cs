using Godot;

namespace Realm.Client.Services;

public struct MinimapPing
{
	public Vector3 WorldPos;
	public float LifeTime;
	public float MaxLifeTime;
}