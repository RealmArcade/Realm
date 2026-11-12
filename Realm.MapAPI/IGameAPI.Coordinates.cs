// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Defines a named rectangular zone in 2D map space (X/Z axes).
	/// Returns a zone handle that can be used with <see cref="OnUnitEnterZone"/>.
	/// </summary>
	/// <param name="minX">West boundary of the zone.</param>
	/// <param name="minZ">South boundary of the zone.</param>
	/// <param name="maxX">East boundary of the zone.</param>
	/// <param name="maxZ">North boundary of the zone.</param>
	/// <returns>An opaque zone identifier.</returns>
	int DefineZone(float minX, float minZ, float maxX, float maxZ);

	/// <summary>
	/// Returns the center position (in 3D world space) of the zone with the given handle.
	/// The Y component is terrain height at that point.
	/// </summary>
	/// <param name="zoneHandle">Zone handle returned by <see cref="DefineZone"/>.</param>
	Vector3 GetZoneCenter(int zoneHandle);

	/// <summary>
	/// Triggered whenever a unit enters the specified zone.
	/// The callback receives the unit that entered and the zone handle it entered.
	/// </summary>
	event Action<UnitId, int>? OnUnitEnterZone;


	/// <summary>
	/// Associates an integer route-state value with the specified unit.
	/// Useful for tracking which waypoint branch a unit is following.
	/// </summary>
	/// <param name="unit">The unit to tag.</param>
	/// <param name="state">An arbitrary integer value meaningful to the caller.</param>
	void SetUnitRouteState(UnitId unit, int state);

	/// <summary>
	/// Retrieves the integer route-state value previously set on the unit,
	/// or 0 if none has been set.
	/// </summary>
	/// <param name="unit">The unit to query.</param>
	int GetUnitRouteState(UnitId unit);


	/// <summary>
	/// Sets the level of the specified hero unit.
	/// Has no effect on non-hero units.
	/// </summary>
	/// <param name="unit">The hero unit whose level should be changed.</param>
	/// <param name="level">The new level value.</param>
	void SetUnitLevel(UnitId unit, int level);


	/// <summary>
	/// Triggered when a player leaves or disconnects from the game.
	/// The integer argument is the zero-based player slot index.
	/// </summary>
	event Action<int>? OnPlayerLeft;

	/// <summary>
	/// Sets the kill count for the specified player slot directly.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="kills">The new kill count.</param>
	void SetPlayerKills(int playerIndex, int kills);


	/// <summary>
	/// Orders the unit to move to the specified destination without attacking anything en route.
	/// Unlike <see cref="IssueAttackMoveOrder(UnitId, Vector3)"/>, this is a pure positional move command.
	/// </summary>
	/// <param name="unit">The unit to command.</param>
	/// <param name="destination">The destination in world coordinates.</param>
	void IssueMoveOrder(UnitId unit, Vector3 destination);


	/// <summary>
	/// Adds or removes a spell-immunity buff from the specified unit.
	/// Spell-immune units cannot be targeted by magic-type attacks or abilities.
	/// </summary>
	/// <param name="unit">The unit to modify.</param>
	/// <param name="immune">True to make the unit spell-immune; false to remove the immunity.</param>
	void SetUnitSpellImmune(UnitId unit, bool immune);

	/// <summary>
	/// Checks if a named coordinate box exists.
	/// </summary>
	bool HasCoordinate(string coordinateName);

	/// <summary>
	/// Gets the minimum coordinates of a named coordinate box.
	/// </summary>
	Vector3 GetCoordinateMin(string coordinateName);

	/// <summary>
	/// Gets the maximum coordinates of a named coordinate box.
	/// </summary>
	Vector3 GetCoordinateMax(string coordinateName);

	/// <summary>
	/// Gets the bounding coordinates of a named coordinate box drawn in the map editor.
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate box.</param>
	/// <param name="min">The minimum coordinates of the box.</param>
	/// <param name="max">The maximum coordinates of the box.</param>
	/// <returns>True if the coordinate box exists, false otherwise.</returns>
	bool TryGetCoordinate(string coordinateName, out Vector3 min, out Vector3 max)
	{
		if (HasCoordinate(coordinateName))
		{
			min = GetCoordinateMin(coordinateName);
			max = GetCoordinateMax(coordinateName);
			return true;
		}

		min = Vector3.Zero;
		max = Vector3.Zero;
		return false;
	}

	/// <summary>
	/// Checks if a 3D position is inside a named coordinate box.
	/// </summary>
	/// <param name="position">The position to check.</param>
	/// <param name="coordinateName">The name of the coordinate box.</param>
	/// <returns>True if the position is inside the coordinate box, false otherwise.</returns>
	bool IsPositionInCoordinate(Vector3 position, string coordinateName);

	/// <summary>
	/// Gets the bounding coordinate box drawn in the map editor by name.
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate box.</param>
	/// <returns>A <see cref="Coordinate"/> representing the bounding region.</returns>
	Coordinate GetCoordinate(string coordinateName)
	{
		return new Coordinate(GetCoordinateMin(coordinateName), GetCoordinateMax(coordinateName));
	}

	/// <summary>
	/// Gets the bounding coordinates of a named coordinate box drawn in the map editor.
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate box.</param>
	/// <param name="coordinate">The resulting coordinate box if found.</param>
	/// <returns>True if the coordinate box exists, false otherwise.</returns>
	bool TryGetCoordinate(string coordinateName, out Coordinate coordinate)
	{
		if (HasCoordinate(coordinateName))
		{
			coordinate = GetCoordinate(coordinateName);
			return true;
		}

		coordinate = default;
		return false;
	}

	/// <summary>
	/// Checks if a 3D position is inside a coordinate bounding box.
	/// </summary>
	/// <param name="position">The position to check.</param>
	/// <param name="coordinate">The coordinate box to check against.</param>
	/// <returns>True if the position is inside the coordinate box, false otherwise.</returns>
	bool IsPositionInCoordinate(Vector3 position, Coordinate coordinate)
	{
		float minX = MathF.Min(coordinate.Min.X, coordinate.Max.X);
		float maxX = MathF.Max(coordinate.Min.X, coordinate.Max.X);
		float minZ = MathF.Min(coordinate.Min.Z, coordinate.Max.Z);
		float maxZ = MathF.Max(coordinate.Min.Z, coordinate.Max.Z);
		return position.X >= minX && position.X <= maxX && position.Z >= minZ && position.Z <= maxZ;
	}

	/// <summary>
	/// Spawns a new unit for a specific player at the center of the given coordinate region.
	/// </summary>
	/// <param name="unitTypeId">The type identifier of the unit to spawn.</param>
	/// <param name="coordinate">The coordinate region whose center will be used as spawn location.</param>
	/// <param name="playerIndex">The player index who will own the unit.</param>
	/// <param name="executeSpawnShader">True to play the spawn shader effect if configured, false to skip.</param>
	/// <param name="bypassPopulation">True to bypass population cap limits when spawning, false to enforce population limits.</param>
	/// <returns>The spawned <see cref="UnitId"/>.</returns>
	UnitId SpawnUnitForPlayer(string unitTypeId, Coordinate coordinate, int playerIndex, bool executeSpawnShader = true, bool bypassPopulation = false)
	{
		return SpawnUnitForPlayer(unitTypeId, new Vector2(coordinate.Center.X, coordinate.Center.Z), playerIndex, executeSpawnShader, bypassPopulation);
	}

	/// <summary>
	/// Orders a unit to move to the center of the specified coordinate region.
	/// </summary>
	/// <param name="unit">The unit to order.</param>
	/// <param name="destination">The coordinate region to move to.</param>
	void IssueMoveOrder(UnitId unit, Coordinate destination)
	{
		IssueMoveOrder(unit, destination.Center);
	}

	/// <summary>
	/// Orders a unit to attack-move to the center of the specified coordinate region.
	/// </summary>
	/// <param name="unit">The unit to order.</param>
	/// <param name="destination">The coordinate region to attack-move towards.</param>
	void IssueAttackMoveOrder(UnitId unit, Coordinate destination)
	{
		IssueAttackMoveOrder(unit, destination.Center);
	}
}
