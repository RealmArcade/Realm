using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.MapAPI;
using Realm.Shared.Terrain;

namespace Realm.ListenServer;

/// <summary>
///     The named places a map declares, the rectangles a script defines while it runs, and the pathing it turns on and
///     off. All three are the map's own geography, so the authority answers them from the map it loaded rather than
///     letting each client work them out for itself.
/// </summary>
public partial class ServerGameAPI
{
	private readonly List<RegionPathingOverride> _regionPathingOverrides = new();

	/// <summary>The named regions a map declares, as the map files describe them.</summary>
	private IReadOnlyList<CoordinateSaveData> DeclaredCoordinates => _map.SaveData.Coordinates ?? (IReadOnlyList<CoordinateSaveData>)Array.Empty<CoordinateSaveData>();

	/// <summary>Whether the map declares a region under a name.</summary>
	public bool HasCoordinate(string coordinateName) => FindCoordinate(coordinateName) != null;

	/// <summary>The low corner of a named region. A map that declares no such region answers with nothing.</summary>
	public Vector3 GetCoordinateMin(string coordinateName)
	{
		CoordinateSaveData? coordinate = FindCoordinate(coordinateName);
		if (coordinate == null) return Vector3.Zero;

		return new Vector3(MathF.Min(coordinate.MinX, coordinate.MaxX), 0f, MathF.Min(coordinate.MinZ, coordinate.MaxZ));
	}

	/// <summary>The high corner of a named region.</summary>
	public Vector3 GetCoordinateMax(string coordinateName)
	{
		CoordinateSaveData? coordinate = FindCoordinate(coordinateName);
		if (coordinate == null) return Vector3.Zero;

		return new Vector3(MathF.Max(coordinate.MinX, coordinate.MaxX), 0f, MathF.Max(coordinate.MinZ, coordinate.MaxZ));
	}

	/// <summary>Whether a place falls inside a named region, which is how a map asks what part of it something is standing on.</summary>
	public bool IsPositionInCoordinate(Vector3 position, string coordinateName)
	{
		CoordinateSaveData? coordinate = FindCoordinate(coordinateName);
		if (coordinate == null) return false;

		float minX = MathF.Min(coordinate.MinX, coordinate.MaxX);
		float maxX = MathF.Max(coordinate.MinX, coordinate.MaxX);
		float minZ = MathF.Min(coordinate.MinZ, coordinate.MaxZ);
		float maxZ = MathF.Max(coordinate.MinZ, coordinate.MaxZ);

		return position.X >= minX && position.X <= maxX && position.Z >= minZ && position.Z <= maxZ;
	}

	/// <summary>
	///     Declares a rectangle the match watches, and answers with the number that names it. The zones live in the match's
	///     own state, so a unit entering one is noticed once, by the authority, and every client is told about it.
	/// </summary>
	public int DefineZone(float minX, float minZ, float maxX, float maxZ)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return -1;

		if (!_context.World.Has<ScriptZonesState>(worldEntity)) _context.World.Add(worldEntity, new ScriptZonesState(null));

		ref ScriptZonesState zones = ref _context.World.Get<ScriptZonesState>(worldEntity);
		int handle = zones.Count;
		zones.Add(new ZoneBounds
		{
			MinX = minX,
			MinZ = minZ,
			MaxX = maxX,
			MaxZ = maxZ,
			Center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f)
		});

		return handle;
	}

	/// <summary>The middle of a zone a script defined.</summary>
	public Vector3 GetZoneCenter(int zoneHandle)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return Vector3.Zero;
		if (!_context.World.Has<ScriptZonesState>(worldEntity)) return Vector3.Zero;

		ref ScriptZonesState zones = ref _context.World.Get<ScriptZonesState>(worldEntity);
		if (zoneHandle < 0 || zoneHandle >= zones.Count) return Vector3.Zero;

		return zones.GetZone(zoneHandle).Center;
	}

	/// <summary>
	///     Opens or closes a rectangle of the map to a kind of movement. The authority changes its own terrain pathing and
	///     keeps the change, so the world it walks units through and the world it describes to clients agree.
	/// </summary>
	public void SetRegionPathing(float minX, float minZ, float maxX, float maxZ, PathingFlags pathingFlags, bool blocked)
	{
		ApplyRegionPathing(minX, minZ, maxX, maxZ, (int)pathingFlags, blocked);
		_regionPathingOverrides.Add(new RegionPathingOverride(minX, minZ, maxX, maxZ, (int)pathingFlags, blocked));
	}

	/// <summary>Opens or closes the area a map named, which is what a script does when it unlocks a region by name.</summary>
	public void SetRegionPathing(string coordinateName, PathingFlags pathingFlags, bool blocked)
	{
		CoordinateSaveData? coordinate = FindCoordinate(coordinateName);
		if (coordinate == null) return;

		SetRegionPathing(
			MathF.Min(coordinate.MinX, coordinate.MaxX),
			MathF.Min(coordinate.MinZ, coordinate.MaxZ),
			MathF.Max(coordinate.MinX, coordinate.MaxX),
			MathF.Max(coordinate.MinZ, coordinate.MaxZ),
			pathingFlags,
			blocked);
	}

	/// <summary>Which kinds of movement a rectangle of the map is open to.</summary>
	public PathingFlags GetRegionPathing(float minX, float minZ, float maxX, float maxZ)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return PathingFlags.None;
		if (!_context.World.Has<TerrainState>(worldEntity)) return PathingFlags.None;

		ref TerrainState terrain = ref _context.World.Get<TerrainState>(worldEntity);
		return (PathingFlags)_context.TerrainNavMeshService.GetRegionPathing(in terrain, minX, minZ, maxX, maxZ);
	}

	/// <summary>Which kinds of movement the area a map named is open to.</summary>
	public PathingFlags GetRegionPathing(string coordinateName)
	{
		CoordinateSaveData? coordinate = FindCoordinate(coordinateName);
		if (coordinate == null) return PathingFlags.None;

		return GetRegionPathing(
			MathF.Min(coordinate.MinX, coordinate.MaxX),
			MathF.Min(coordinate.MinZ, coordinate.MaxZ),
			MathF.Max(coordinate.MinX, coordinate.MaxX),
			MathF.Max(coordinate.MinZ, coordinate.MaxZ));
	}

	/// <summary>
	///     Every pathing change a map has made since the match started, in the order it made them. A client bakes its own
	///     navmesh from these rather than being handed the authority's, so both walk units through the same ground.
	/// </summary>
	public IReadOnlyList<RegionPathingOverride> RegionPathingOverrides => _regionPathingOverrides;

	private void ApplyRegionPathing(float minX, float minZ, float maxX, float maxZ, int flags, bool blocked)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return;
		if (!_context.World.Has<TerrainState>(worldEntity)) return;

		ref TerrainState terrain = ref _context.World.Get<TerrainState>(worldEntity);
		_context.TerrainNavMeshService.SetRegionPathing(ref terrain, minX, minZ, maxX, maxZ, flags, blocked);
		_context.TerrainNavMeshService.InvalidateIntersectingPaths(new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f), MathF.Max(maxX - minX, maxZ - minZ) * 0.5f);
	}

	private CoordinateSaveData? FindCoordinate(string coordinateName)
	{
		if (string.IsNullOrWhiteSpace(coordinateName)) return null;

		IReadOnlyList<CoordinateSaveData> coordinates = DeclaredCoordinates;
		for (int index = 0; index < coordinates.Count; index++)
		{
			if (string.Equals(coordinates[index].Name, coordinateName, StringComparison.OrdinalIgnoreCase)) return coordinates[index];
		}

		return null;
	}
}

/// <summary>One pathing change a map made to its own ground, as the client needs it to bake the same navmesh.</summary>
public readonly record struct RegionPathingOverride(float MinX, float MinZ, float MaxX, float MaxZ, int PathingFlags, bool Blocked);
