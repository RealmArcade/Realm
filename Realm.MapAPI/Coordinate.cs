using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Represents a 3D bounding region defined by a minimum and maximum coordinate.
/// </summary>
public readonly struct Coordinate
{
	/// <summary>
	/// The minimum corner of the bounding region.
	/// </summary>
	public readonly Vector3 Min;

	/// <summary>
	/// The maximum corner of the bounding region.
	/// </summary>
	public readonly Vector3 Max;

	/// <summary>
	/// The center point of the bounding region.
	/// </summary>
	public readonly Vector3 Center;

	/// <summary>
	/// Initializes a new instance of the <see cref="Coordinate"/> struct.
	/// </summary>
	/// <param name="min">The minimum corner of the bounding region.</param>
	/// <param name="max">The maximum corner of the bounding region.</param>
	public Coordinate(Vector3 min, Vector3 max)
	{
		Min = min;
		Max = max;
		Center = (min + max) / 2f;
	}
}