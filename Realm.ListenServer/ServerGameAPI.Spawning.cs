using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.Shared.Abilities;
using Realm.Shared.Maps;
using Realm.Shared.Terrain;
using Realm.MapAPI;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	private readonly Dictionary<int, ServerTimer> _timers = new();
	private int _nextTimerHandle = 1;

	private struct ServerTimer
	{
		public float Interval;
		public float Remaining;
		public bool Repeating;
	}

	private const float DefaultProjectileSpeed = 25f;

	/// <summary>
	///     A projectile an ability fired as its own visual, in flight. It carries the ability that launched it, so the map
	///     hears about the landing through OnSpellImpact and nothing else in the game has to care: an ordinary attack
	///     flies no projectile here and costs this list nothing.
	/// </summary>
	private struct AbilityProjectile
	{
		public Vector3 Position;
		public Vector3 Direction;
		public Vector3 Target;
		public float RemainingDistance;
		public float Speed;
		public UnitId Caster;
		public string AbilityId;
		public int PlayerIndex;
	}

	private readonly List<AbilityProjectile> _abilityProjectiles = new();
	private readonly List<AbilityProjectile> _landedProjectiles = new();

	/// <summary>
	///     Flies an ability's projectile as that ability's own visual: the client draws it and the engine advances the same
	///     flight at the speed the weapon template carries, so its landing and the landing the player sees are one moment.
	/// </summary>
	private void LaunchAbilityProjectile(UnitId caster, AbilityDefinition definition, Vector3 from, Vector3 to)
	{
		if (string.IsNullOrEmpty(definition.ProjectileId)) return;

		// The weapon template owns the speed, so nothing here repeats a number the templates already define.
		float speed = _map.Catalog.FindWeapon(definition.ProjectileId)?.ProjectileSpeed ?? 0f;
		if (speed <= 0f) speed = DefaultProjectileSpeed;

		Vector3 delta = to - from;
		float distance = delta.Length();
		if (distance <= 0.001f)
		{
			SpawnProjectile(definition.ProjectileId, from, to, speed);
			RaiseSpellImpact(caster, definition.Id, to);
			return;
		}

		SpawnProjectile(definition.ProjectileId, from, to, speed);

		_abilityProjectiles.Add(new AbilityProjectile
		{
			Position = from,
			Direction = delta / distance,
			Target = to,
			RemainingDistance = distance,
			Speed = speed,
			Caster = caster,
			AbilityId = definition.Id,
			PlayerIndex = ResolveProjectilePlayerIndex(caster)
		});
	}

	private void AdvanceAbilityProjectiles(float deltaSeconds)
	{
		if (_abilityProjectiles.Count == 0) return;

		for (int index = _abilityProjectiles.Count - 1; index >= 0; index--)
		{
			AbilityProjectile projectile = _abilityProjectiles[index];
			float travelled = projectile.Speed * deltaSeconds;
			if (travelled < projectile.RemainingDistance)
			{
				projectile.RemainingDistance -= travelled;
				projectile.Position += projectile.Direction * travelled;
				_abilityProjectiles[index] = projectile;
				continue;
			}

			_abilityProjectiles.RemoveAt(index);
			_landedProjectiles.Add(projectile);
		}

		if (_landedProjectiles.Count == 0) return;

		// Landing is reported after the flight list is settled: a map handler is free to cast again from inside it.
		for (int index = 0; index < _landedProjectiles.Count; index++)
		{
			AbilityProjectile landed = _landedProjectiles[index];
			OnSpellImpact?.Invoke(landed.Caster, landed.AbilityId, landed.Target, landed.PlayerIndex);
		}

		_landedProjectiles.Clear();
	}

	private void RaiseSpellImpact(UnitId caster, string abilityId, Vector3 position)
	{
		OnSpellImpact?.Invoke(caster, abilityId ?? string.Empty, position, ResolveProjectilePlayerIndex(caster));
	}

	private int ResolveProjectilePlayerIndex(UnitId caster)
	{
		Entity casterEntity = ResolveUnit(caster);
		if (casterEntity != Entity.Null && _context.World.TryGet<UnitOwnerPlayer>(casterEntity, out UnitOwnerPlayer owner)) return owner.Value;

		return -1;
	}

	public UnitId SpawnUnitForPlayer(string unitTypeId, Vector2 position, int playerIndex, bool executeSpawnShader = true, bool bypassPopulation = false)
	{
		if (string.IsNullOrEmpty(unitTypeId)) return UnitId.Invalid;

		PlayerEntity owner = _map.GetPlayerEntity(playerIndex);
		if (owner.Value == Entity.Null)
		{
			_log.Write($"the map asked for '{unitTypeId}' for player {playerIndex}, which this map does not seat");
			return UnitId.Invalid;
		}

		return SpawnUnit(unitTypeId, new Vector3(position.X, 0f, position.Y), owner, playerIndex, bypassPopulation);
	}

	private UnitId SpawnUnit(string unitTypeId, Vector3 position, PlayerEntity owner, int playerIndex, bool bypassPopulation)
	{
		float groundHeight = ReadTerrainHeight(position);
		var placed = new Vector3(position.X, groundHeight, position.Z);

		Entity spawned = UnitEntityFactory.CreateFromMetadata(_context.World, _map.Catalog, unitTypeId, placed, 0f, 0f, owner, bypassPopulation);
		if (spawned == Entity.Null)
		{
			_log.Write($"the map asked for '{unitTypeId}', which its templates do not define");
			return UnitId.Invalid;
		}

		_context.World.SetOrAdd(spawned, new UnitOwnerPlayer(playerIndex));
		_log.Write($"spawned '{unitTypeId}' as entity {spawned.Id} for player {playerIndex}");

		if (Metrics != null)
		{
			Metrics.RecordUnitSpawned(playerIndex);
			if (_map.Catalog.FindUnitOrBuilding(unitTypeId)?.IsHero == true)
			{
				Metrics.RecordHeroPick(playerIndex, unitTypeId);
			}
		}

		return new UnitId(spawned.Id);
	}

	private float ReadTerrainHeight(Vector3 position)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return position.Y;
		if (!_context.World.TryGet<TerrainState>(worldEntity, out TerrainState terrain)) return position.Y;

		_context.TerrainNavMeshService.GetHeightAndNormal(in terrain, position.X, position.Z, out float height, out Vector3 normal);
		return height;
	}

	public void AdvanceTimers(float deltaSeconds)
	{
		AdvanceAbilityProjectiles(deltaSeconds);

		if (_timers.Count == 0) return;

		_nextDueTimers.Clear();
		foreach (KeyValuePair<int, ServerTimer> entry in _timers)
		{
			ServerTimer timer = entry.Value;
			timer.Remaining -= deltaSeconds;
			if (timer.Remaining > 0f)
			{
				_timers[entry.Key] = timer;
				continue;
			}

			if (timer.Repeating)
			{
				timer.Remaining += timer.Interval;
				if (timer.Remaining <= 0f) timer.Remaining = timer.Interval;
				_timers[entry.Key] = timer;
			}
			else
			{
				_timers.Remove(entry.Key);
			}

			_nextDueTimers.Add(entry.Key);
		}

		for (int index = 0; index < _nextDueTimers.Count; index++)
		{
			OnTimerExpired?.Invoke(_nextDueTimers[index]);
		}
	}

	private readonly List<int> _nextDueTimers = new();

	public int ScheduleTimer(float delay)
	{
		int handle = _nextTimerHandle++;
		float seconds = Math.Max(0f, delay);
		_timers[handle] = new ServerTimer { Interval = seconds, Remaining = seconds, Repeating = false };
		return handle;
	}

	public int ScheduleRepeatingTimer(float interval)
	{
		int handle = _nextTimerHandle++;
		float seconds = Math.Max(0.01f, interval);
		_timers[handle] = new ServerTimer { Interval = seconds, Remaining = seconds, Repeating = true };
		return handle;
	}

	public void CancelTimer(int timerHandle)
	{
		_timers.Remove(timerHandle);
	}

	private readonly Dictionary<int, Vector3> _resolvedSpawnAnchors = new();

	public int GetSpawnAnchorCount() => _map.SaveData.SpawnAnchors?.Count ?? 0;

	/// <summary>
	///     Answers where one player slot begins, picking the point the first time it is asked and holding it for the rest
	///     of the match: a seat is one place, so a map that reads it twice must not be answered twice differently.
	/// </summary>
	/// <param name="playerSlot">The one-based player slot the map's own numbering states.</param>
	/// <returns>The point that slot begins at, snapped to the terrain.</returns>
	public Vector3 GetSpawnAnchor(int playerSlot)
	{
		if (_resolvedSpawnAnchors.TryGetValue(playerSlot, out Vector3 resolved)) return resolved;

		CoordinateSaveData? seat = FindTeamSpawnCoordinate(playerSlot) ?? FindSpawnAnchorCoordinate(playerSlot);
		Vector3 point = seat == null ? RandomPointOnBoard() : RandomPointInCoordinate(seat);
		_resolvedSpawnAnchors[playerSlot] = point;

		return point;
	}

	/// <summary>
	///     Finds the coordinate the lobby's map seated one player's team in, which is what a board that plays its teams
	///     as sides states instead of seating each player itself. Every player of a team reads the same coordinate, so a
	///     side begins together inside it while each seat still picks its own point there.
	/// </summary>
	/// <param name="playerSlot">The one-based player slot the map's own numbering states.</param>
	/// <returns>The coordinate that slot's team begins inside, or nothing when the lobby assigned its team none.</returns>
	private CoordinateSaveData? FindTeamSpawnCoordinate(int playerSlot)
	{
		if (_teamSpawnCoordinates.Length == 0) return null;

		int team = ReadPlayerTeam(playerSlot);
		if (team <= 0 || team >= _teamSpawnCoordinates.Length) return null;

		return FindCoordinate(_teamSpawnCoordinates[team]);
	}

	/// <summary>
	///     Reads the team a player slot plays on out of the world's player table, which is what the lobby seats a slot on
	///     before any script runs.
	/// </summary>
	/// <param name="playerSlot">The one-based player slot.</param>
	/// <returns>The team number, or zero when the slot plays on no team or is not a player.</returns>
	private int ReadPlayerTeam(int playerSlot)
	{
		if (!TryReadPlayersState(out ScriptPlayersState players)) return 0;

		return players.IsValidSlot(playerSlot) ? players.Get(playerSlot).Team : 0;
	}

	/// <summary>
	///     Finds the coordinate the board seats one player inside. An anchor whose coordinate this board does not author
	///     seats nobody, which is the same answer a player the board states no anchor for gets.
	/// </summary>
	/// <param name="playerSlot">The one-based player slot the map's own numbering states.</param>
	/// <returns>The coordinate the slot begins inside, or nothing when the board states no usable anchor for it.</returns>
	private CoordinateSaveData? FindSpawnAnchorCoordinate(int playerSlot)
	{
		List<SpawnAnchorSaveData>? anchors = _map.SaveData.SpawnAnchors;
		if (anchors == null) return null;

		for (int index = 0; index < anchors.Count; index++)
		{
			SpawnAnchorSaveData anchor = anchors[index];
			if (anchor.Player != playerSlot) continue;

			return FindCoordinate(anchor.CoordinateName);
		}

		return null;
	}

	/// <summary>
	///     Picks where inside a seat's coordinate that seat begins. The whole rectangle is fair game rather than its
	///     centre, so two boards that state the same ground do not seat everyone on the identical step.
	/// </summary>
	/// <param name="coordinate">The coordinate the seat begins inside.</param>
	/// <returns>The picked point, snapped to the terrain.</returns>
	private Vector3 RandomPointInCoordinate(CoordinateSaveData coordinate)
	{
		float minX = MathF.Min(coordinate.MinX, coordinate.MaxX);
		float maxX = MathF.Max(coordinate.MinX, coordinate.MaxX);
		float minZ = MathF.Min(coordinate.MinZ, coordinate.MaxZ);
		float maxZ = MathF.Max(coordinate.MinZ, coordinate.MaxZ);

		return SnapToGround(SeededRandom.Shared.NextSingle() * (maxX - minX) + minX, SeededRandom.Shared.NextSingle() * (maxZ - minZ) + minZ);
	}

	/// <summary>
	///     Picks where a player the board states no anchor for begins: anywhere on the board the match is played on. A
	///     board that names no seat for a player still starts that player somewhere rather than refusing to seat them.
	/// </summary>
	/// <returns>The picked point, snapped to the terrain.</returns>
	private Vector3 RandomPointOnBoard()
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return Vector3.Zero;
		if (!_context.World.TryGet<TerrainState>(worldEntity, out TerrainState terrain)) return Vector3.Zero;
		if (terrain.Width <= 0 || terrain.Depth <= 0) return Vector3.Zero;

		float halfWidth = terrain.Width * 0.5f * terrain.QuadSize;
		float halfDepth = terrain.Depth * 0.5f * terrain.QuadSize;

		return SnapToGround(SeededRandom.Shared.NextSingle() * halfWidth * 2f - halfWidth, SeededRandom.Shared.NextSingle() * halfDepth * 2f - halfDepth);
	}

	private Vector3 SnapToGround(float x, float z)
	{
		var point = new Vector3(x, 0f, z);
		return new Vector3(point.X, ReadTerrainHeight(point), point.Z);
	}
}
