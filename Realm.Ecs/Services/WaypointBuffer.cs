using System.Numerics;
using System.Runtime.CompilerServices;

namespace Realm.Ecs.Services;

/// <summary>
/// An inline fixed buffer of 256 Vector3 pathfinding waypoints for navigation.
/// </summary>
[InlineArray(256)]
public struct WaypointBuffer
{
	private Vector3 _element0;
	public const int Length = 256;
}