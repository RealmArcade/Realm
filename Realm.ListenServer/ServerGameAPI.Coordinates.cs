using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.MapAPI;
using Realm.Shared.Terrain;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	private readonly List<RegionPathingOverride> _regionPathingOverrides = new();

	private IReadOnlyList<CoordinateSaveData> DeclaredCoordinates => _map.SaveData.Coordinates ?? (IReadOnlyList<CoordinateSaveData>)Array.Empty<CoordinateSaveData>();

	public bool HasCoordinate(string coordinateName) => FindCoordinate(coordinateName) != null;

	public Vector3 GetCoordinateMin(string coordinateName)
	{
		CoordinateSaveData? coordinate = FindCoordinate(coordinateName);
		if (coordinate == null) return Vector3.Zero;

		return new Vector3(MathF.Min(coordinate.MinX, coordinate.MaxX), 0f, MathF.Min(coordinate.MinZ, coordinate.MaxZ));
	}

	public Vector3 GetCoordinateMax(string coordinateName)
	{
		CoordinateSaveData? coordinate = FindCoordinate(coordinateName);
		if (coordinate == null) return Vector3.Zero;

		return new Vector3(MathF.Max(coordinate.MinX, coordinate.MaxX), 0f, MathF.Max(coordinate.MinZ, coordinate.MaxZ));
	}

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

	public Vector3 GetZoneCenter(int zoneHandle)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return Vector3.Zero;
		if (!_context.World.Has<ScriptZonesState>(worldEntity)) return Vector3.Zero;

		ref ScriptZonesState zones = ref _context.World.Get<ScriptZonesState>(worldEntity);
		if (zoneHandle < 0 || zoneHandle >= zones.Count) return Vector3.Zero;

		return zones.GetZone(zoneHandle).Center;
	}

	public void SetRegionPathing(float minX, float minZ, float maxX, float maxZ, PathingFlags pathingFlags, bool blocked)
	{
		ApplyRegionPathing(minX, minZ, maxX, maxZ, (int)pathingFlags, blocked);
		_regionPathingOverrides.Add(new RegionPathingOverride(minX, minZ, maxX, maxZ, (int)pathingFlags, blocked));
	}

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

	public PathingFlags GetRegionPathing(float minX, float minZ, float maxX, float maxZ)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return PathingFlags.None;
		if (!_context.World.Has<TerrainState>(worldEntity)) return PathingFlags.None;

		ref TerrainState terrain = ref _context.World.Get<TerrainState>(worldEntity);
		return (PathingFlags)_context.TerrainNavMeshService.GetRegionPathing(in terrain, minX, minZ, maxX, maxZ);
	}

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

public readonly record struct RegionPathingOverride(float MinX, float MinZ, float MaxX, float MaxZ, int PathingFlags, bool Blocked);
