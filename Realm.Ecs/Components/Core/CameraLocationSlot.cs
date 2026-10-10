using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents a saved camera location and zoom level.
/// </summary>
public struct CameraLocationSlot
{
	public Vector3 Position;
	public float ZoomLevel;
	public bool IsSet;
}