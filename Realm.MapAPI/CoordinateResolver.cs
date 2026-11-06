using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Generic named-point lookup. Maps pass coordinate names; this type does not know MOBA lanes.
/// </summary>
public static class CoordinateResolver
{
	/// <summary>
	///     Resolves every named point a map script asked for. The lookup is the map's own, so this type never assumes which
	///     coordinates exist or what they mean.
	/// </summary>
	/// <param name="lookup">The map's resolver, returning null for a name it does not define.</param>
	/// <param name="names">Every name that has to resolve. The whole set fails if any one of them does not.</param>
	/// <param name="centers">The resolved positions, keyed by name. Empty when the call fails, so a caller never acts on half a set.</param>
	/// <param name="missingName">The first name the map did not define, or null when every name resolved.</param>
	/// <returns>Whether every name in <paramref name="names" /> resolved.</returns>
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

	/// <summary>
	///     Builds a two-leg route from a start position, through one named point, on to a destination, which is how a script
	///     routes a unit around an obstacle the map names.
	/// </summary>
	/// <param name="lookup">The map's resolver, returning null for a name it does not define.</param>
	/// <param name="start">Where the route begins, in world-space metres.</param>
	/// <param name="cornerName">The name of the point the route turns at.</param>
	/// <param name="destination">Where the route ends, in world-space metres.</param>
	/// <param name="waypoints">Start, corner and destination in that order on success, or empty when the corner does not resolve.</param>
	/// <returns>Whether the corner name resolved. A false result leaves <paramref name="waypoints" /> empty and no path is attempted.</returns>
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