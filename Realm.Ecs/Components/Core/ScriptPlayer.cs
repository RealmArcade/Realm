namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the properties of a single player within a map script.
/// </summary>
public struct ScriptPlayer
{
	public float Gold;
	public float Wood;
	public bool Active;
	public string Name;
	public int KillCount;
}