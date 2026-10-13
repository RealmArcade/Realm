using System.Numerics;
using System.Runtime.CompilerServices;

namespace Realm.Ecs.Services;

[InlineArray(256)]
public struct WaypointBuffer
{
	private Vector3 _element0;
	public const int Length = 256;
}