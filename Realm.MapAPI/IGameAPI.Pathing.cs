// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Dynamically alters the movement-type pathing flags for a designated rectangular region on the terrain navmesh.
	/// </summary>
	/// <param name="minX">The minimum X coordinate in world space.</param>
	/// <param name="minZ">The minimum Z coordinate in world space.</param>
	/// <param name="maxX">The maximum X coordinate in world space.</param>
	/// <param name="maxZ">The maximum Z coordinate in world space.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to allow or block (e.g. Ground, Flying, ShallowWater).</param>
	/// <param name="blocked">True to block/remove the specified pathing flags; false to allow/restore them.</param>
	void SetRegionPathing(float minX, float minZ, float maxX, float maxZ, PathingFlags pathingFlags, bool blocked);

	/// <summary>
	/// Dynamically alters the movement-type pathing flags for a named coordinate region.
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate region defined in the map editor.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to allow or block (e.g. Ground, Flying, ShallowWater).</param>
	/// <param name="blocked">True to block/remove the specified pathing flags; false to allow/restore them.</param>
	void SetRegionPathing(string coordinateName, PathingFlags pathingFlags, bool blocked)
	{
		if (TryGetCoordinate(coordinateName, out var coord))
		{
			SetRegionPathing(coord, pathingFlags, blocked);
		}
	}

	/// <summary>
	/// Dynamically alters the movement-type pathing flags for a coordinate bounding region.
	/// </summary>
	/// <param name="coordinate">The coordinate bounding region.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to allow or block (e.g. Ground, Flying, ShallowWater).</param>
	/// <param name="blocked">True to block/remove the specified pathing flags; false to allow/restore them.</param>
	void SetRegionPathing(Coordinate coordinate, PathingFlags pathingFlags, bool blocked)
	{
		SetRegionPathing(
			MathF.Min(coordinate.Min.X, coordinate.Max.X),
			MathF.Min(coordinate.Min.Z, coordinate.Max.Z),
			MathF.Max(coordinate.Min.X, coordinate.Max.X),
			MathF.Max(coordinate.Min.Z, coordinate.Max.Z),
			pathingFlags,
			blocked);
	}

	/// <summary>
	/// Updates the traversability status of a rectangular world boundary region (alias for <see cref="SetRegionPathing(float, float, float, float, PathingFlags, bool)"/>).
	/// </summary>
	/// <param name="minX">The minimum X coordinate in world space.</param>
	/// <param name="minZ">The minimum Z coordinate in world space.</param>
	/// <param name="maxX">The maximum X coordinate in world space.</param>
	/// <param name="maxZ">The maximum Z coordinate in world space.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to allow or block.</param>
	/// <param name="blocked">True to block the boundary; false to open/restore traversability.</param>
	void UpdateWorldBoundaryStatus(float minX, float minZ, float maxX, float maxZ, PathingFlags pathingFlags, bool blocked)
	{
		SetRegionPathing(minX, minZ, maxX, maxZ, pathingFlags, blocked);
	}

	/// <summary>
	/// Updates the traversability status of a named world boundary region (alias for <see cref="SetRegionPathing(string, PathingFlags, bool)"/>).
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate region defined in the map editor.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to allow or block.</param>
	/// <param name="blocked">True to block the boundary; false to open/restore traversability.</param>
	void UpdateWorldBoundaryStatus(string coordinateName, PathingFlags pathingFlags, bool blocked)
	{
		SetRegionPathing(coordinateName, pathingFlags, blocked);
	}

	/// <summary>
	/// Retrieves the combined active pathing flags across the specified rectangular region.
	/// </summary>
	/// <param name="minX">The minimum X coordinate in world space.</param>
	/// <param name="minZ">The minimum Z coordinate in world space.</param>
	/// <param name="maxX">The maximum X coordinate in world space.</param>
	/// <param name="maxZ">The maximum Z coordinate in world space.</param>
	/// <returns>A bitmask of <see cref="PathingFlags"/> representing traversability across the region.</returns>
	PathingFlags GetRegionPathing(float minX, float minZ, float maxX, float maxZ);

	/// <summary>
	/// Retrieves the combined active pathing flags across a named coordinate region.
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate region defined in the map editor.</param>
	/// <returns>A bitmask of <see cref="PathingFlags"/> representing traversability across the region.</returns>
	PathingFlags GetRegionPathing(string coordinateName)
	{
		if (TryGetCoordinate(coordinateName, out var coord))
		{
			return GetRegionPathing(coord);
		}
		return PathingFlags.None;
	}

	/// <summary>
	/// Retrieves the combined active pathing flags across a coordinate bounding region.
	/// </summary>
	/// <param name="coordinate">The coordinate bounding region.</param>
	/// <returns>A bitmask of <see cref="PathingFlags"/> representing traversability across the region.</returns>
	PathingFlags GetRegionPathing(Coordinate coordinate)
	{
		return GetRegionPathing(
			MathF.Min(coordinate.Min.X, coordinate.Max.X),
			MathF.Min(coordinate.Min.Z, coordinate.Max.Z),
			MathF.Max(coordinate.Min.X, coordinate.Max.X),
			MathF.Max(coordinate.Min.Z, coordinate.Max.Z));
	}

	/// <summary>
	/// Checks whether the specified pathing flags are currently blocked in the designated rectangular region.
	/// </summary>
	/// <param name="minX">The minimum X coordinate in world space.</param>
	/// <param name="minZ">The minimum Z coordinate in world space.</param>
	/// <param name="maxX">The maximum X coordinate in world space.</param>
	/// <param name="maxZ">The maximum Z coordinate in world space.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to test (defaults to Ground).</param>
	/// <returns>True if the specified flags are blocked; false if passable.</returns>
	bool IsRegionPathingBlocked(float minX, float minZ, float maxX, float maxZ, PathingFlags pathingFlags = PathingFlags.Ground)
	{
		return (GetRegionPathing(minX, minZ, maxX, maxZ) & pathingFlags) == 0;
	}

	/// <summary>
	/// Checks whether the specified pathing flags are currently blocked in a named coordinate region.
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate region defined in the map editor.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to test (defaults to Ground).</param>
	/// <returns>True if the specified flags are blocked; false if passable.</returns>
	bool IsRegionPathingBlocked(string coordinateName, PathingFlags pathingFlags = PathingFlags.Ground)
	{
		return (GetRegionPathing(coordinateName) & pathingFlags) == 0;
	}

	/// <summary>
	/// Checks whether the specified pathing flags are currently blocked in a coordinate bounding region.
	/// </summary>
	/// <param name="coordinate">The coordinate bounding region.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to test (defaults to Ground).</param>
	/// <returns>True if the specified flags are blocked; false if passable.</returns>
	bool IsRegionPathingBlocked(Coordinate coordinate, PathingFlags pathingFlags = PathingFlags.Ground)
	{
		return (GetRegionPathing(coordinate) & pathingFlags) == 0;
	}
}
