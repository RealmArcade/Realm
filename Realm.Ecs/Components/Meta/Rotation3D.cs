using System.Numerics;

namespace Realm.Ecs.Components.Meta
{
	/// <summary>
	/// Represents 3D rotation in Euler degrees.
	/// </summary>
	public record struct Rotation3D(Vector3 Value);
}
