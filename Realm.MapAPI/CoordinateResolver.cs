using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Generic named-point lookup. Maps pass coordinate names; this type does not know MOBA lanes.
/// </summary>
public static class CoordinateResolver
{
	public static bool TryGetCenters(
		Func<string, Vector3?> lookup,
		IReadOnlyList<string> names,
		out Dictionary<string, Vector3> centers,
		out string? missingName)
	{
		centers = new Dictionary<string, Vector3>(names.Count);
		missingName = null;
		foreach (var name in names)
		{
			var point = lookup(name);
			if (point is null)
			{
				missingName = name;
				centers.Clear();
				return false;
			}

			centers[name] = point.Value;
		}

		return true;
	}

	public static bool TryBuildThreePointPath(
		Func<string, Vector3?> lookup,
		Vector3 start,
		string cornerName,
		Vector3 destination,
		out Vector3[] waypoints)
	{
		var corner = lookup(cornerName);
		if (corner is null)
		{
			waypoints = [];
			return false;
		}

		waypoints = [start, corner.Value, destination];
		return true;
	}
}