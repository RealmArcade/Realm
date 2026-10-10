using System.Numerics;

namespace Realm.Ecs.Services;

[System.Runtime.CompilerServices.InlineArray(256)]
public struct WaypointBuffer
{
	private Vector3 _element0;
	public const int Length = 256;
}