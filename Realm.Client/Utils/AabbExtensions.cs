using Godot;

namespace Realm.Client.Utils;

public static class AabbExtensions
{
	public static Vector3[] GetCorners(this Aabb aabb)
	{
		Vector3 min = aabb.Position;
		Vector3 max = aabb.End;
		return new[]
		{
			new Vector3(min.X, min.Y, min.Z),
			new Vector3(min.X, min.Y, max.Z),
			new Vector3(min.X, max.Y, min.Z),
			new Vector3(min.X, max.Y, max.Z),
			new Vector3(max.X, min.Y, min.Z),
			new Vector3(max.X, min.Y, max.Z),
			new Vector3(max.X, max.Y, min.Z),
			new Vector3(max.X, max.Y, max.Z)
		};
	}
}
