namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the boundary coordinates of a zone defined by a script.
/// </summary>
public struct ZoneBounds
{
	public float MinX;
	public float MinZ;
	public float MaxX;
	public float MaxZ;
	public System.Numerics.Vector3 Center;
}