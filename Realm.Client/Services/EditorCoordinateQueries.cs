using System;
using System.Collections.Generic;
using Realm.Client.Core;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Services;

/// <summary>
///     Answering questions about the map's named editor coordinates: whether a name is taken, the
///     corner a coordinate starts and ends at, and whether a world position falls inside one. The
///     coordinate list is supplied by the caller, so the answers depend only on what it is given.
/// </summary>
public static class EditorCoordinateQueries
{
	public static bool HasCoordinate(List<EditorCoordinate> coordinates, string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return false;
		string searchName = coordinateName.Trim();
		foreach (var r in coordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	public static Vector3 GetCoordinateMin(List<EditorCoordinate> coordinates, string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return Vector3.Zero;
		string searchName = coordinateName.Trim();
		foreach (var r in coordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
				return new Vector3(r.MinX, 0f, r.MinZ);
		}

		return Vector3.Zero;
	}

	public static Vector3 GetCoordinateMax(List<EditorCoordinate> coordinates, string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return Vector3.Zero;
		string searchName = coordinateName.Trim();
		foreach (var r in coordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
				return new Vector3(r.MaxX, 0f, r.MaxZ);
		}

		return Vector3.Zero;
	}

	public static bool IsPositionInCoordinate(List<EditorCoordinate> coordinates, Vector3 position, string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName)) return false;
		string searchName = coordinateName.Trim();
		foreach (var r in coordinates)
		{
			if (r.Name.Equals(searchName, StringComparison.OrdinalIgnoreCase))
			{
				float minX = Math.Min(r.MinX, r.MaxX);
				float maxX = Math.Max(r.MinX, r.MaxX);
				float minZ = Math.Min(r.MinZ, r.MaxZ);
				float maxZ = Math.Max(r.MinZ, r.MaxZ);
				return position.X >= minX && position.X <= maxX && position.Z >= minZ && position.Z <= maxZ;
			}
		}

		return false;
	}
}
