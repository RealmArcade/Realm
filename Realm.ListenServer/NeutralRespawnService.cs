using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;

namespace Realm.ListenServer;

/// <summary>
///     Keeps the units a board places that nobody owns and that come back after they are killed. A placed unit whose
///     template states a respawn time stands as nobody's unit, so it is hostile to every seat and contestable by all of
///     them, and it returns where it was placed once its recalimed time has run out.
/// </summary>
/// <remarks>
///     The board places these units like any other, through its terrain file, and states how long they take to return on
///     the template in its metadata. Nothing here is specific to one ruleset: a creep camp, a wandering monster and a
///     guarded resource are all the same thing to this service, which is a placed unit its owner never had and a template
///     that tells it to come back. What the kill is worth is paid by <see cref="MatchBountyService" />, which pays every
///     unit's authored bounty without this service having to know it kept that unit.
/// </remarks>
public class NeutralRespawnService
{
	private readonly LoadedMap _map;
	private readonly ServerGameAPI _api;
	private readonly ServerLog _log;
	private readonly List<NeutralPlacement> _placements = new();

	public NeutralRespawnService(LoadedMap map, ServerGameAPI api, ServerLog log)
	{
		_map = map;
		_api = api;
		_log = log;
	}

	/// <summary>Gets the number of placed units this service keeps returning.</summary>
	public int PlacementCount => _placements.Count;

	/// <summary>
	///     Records the units the board placed that nobody owns and that return after death. It runs once the world holds
	///     what the board placed, so the place a unit returns to is the place it was placed at.
	/// </summary>
	public void Start()
	{
		_placements.Clear();

		ReadOnlySpan<UnitId> unowned = _api.GetUnitsOwnedByPlayer(0);
		for (int index = 0; index < unowned.Length; index++)
		{
			UnitId unit = unowned[index];
			if (!IsAlive(unit)) continue;

			Entity entity = new(unit.Value);
			if (!_map.World.TryGet<DefinitionId>(entity, out DefinitionId definition)) continue;

			UnitMetadata? metadata = _map.Catalog.FindUnitOrBuilding(definition.Value);
			if (metadata == null || metadata.RespawnSeconds <= 0f) continue;

			Vector3 position = _map.World.TryGet<Position>(entity, out Position placed) ? placed.Value : Vector3.Zero;

			_placements.Add(new NeutralPlacement
			{
				TemplateId = definition.Value,
				Position = position,
				RespawnSeconds = metadata.RespawnSeconds,
				Living = unit,
				RespawnTimer = 0f
			});
		}
	}

	/// <summary>
	///     Returns every placed unit whose respawn time has run out, one per placement, at the place it was placed.
	/// </summary>
	/// <param name="deltaSeconds">The seconds this tick covers.</param>
	public void Step(float deltaSeconds)
	{
		if (deltaSeconds <= 0f) return;

		for (int index = 0; index < _placements.Count; index++)
		{
			NeutralPlacement placement = _placements[index];
			if (IsAlive(placement.Living)) continue;
			if (placement.RespawnSeconds <= 0f) continue;
			if (placement.RespawnTimer <= 0f) continue;

			placement.RespawnTimer -= deltaSeconds;
			if (placement.RespawnTimer > 0f) continue;

			placement.RespawnTimer = 0f;
			placement.Living = Spawn(placement);
		}
	}

	/// <summary>
	///     Records that a placed unit was destroyed and starts the wait for it to return.
	/// </summary>
	/// <param name="victim">The unit that was destroyed.</param>
	public void OnPlacementDestroyed(UnitId victim)
	{
		int index = IndexOfLiving(victim);
		if (index < 0) return;

		NeutralPlacement placement = _placements[index];
		placement.Living = UnitId.Invalid;
		if (placement.RespawnSeconds > 0f) placement.RespawnTimer = placement.RespawnSeconds;
	}

	private UnitId Spawn(NeutralPlacement placement)
	{
		UnitId unit = _api.SpawnUnitForPlayer(
			placement.TemplateId,
			new Vector2(placement.Position.X, placement.Position.Z),
			0,
			executeSpawnShader: true,
			bypassPopulation: true);

		if (!unit.IsValid)
		{
			_log.Write($"The board places the neutral unit '{placement.TemplateId}' at {placement.Position.X}, {placement.Position.Z} but it could not be spawned.");
		}

		return unit;
	}

	private int IndexOfLiving(UnitId victim)
	{
		for (int index = 0; index < _placements.Count; index++)
		{
			NeutralPlacement placement = _placements[index];
			if (placement.Living.IsValid && placement.Living.Value == victim.Value) return index;
		}

		return -1;
	}

	private bool IsAlive(UnitId unit)
	{
		if (!unit.IsValid) return false;

		Entity entity = new(unit.Value);
		if (!_map.World.IsAlive(entity)) return false;

		return !_map.World.Has<Dead>(entity);
	}

	private class NeutralPlacement
	{
		public string TemplateId = string.Empty;
		public Vector3 Position;
		public float RespawnSeconds;
		public UnitId Living;
		public float RespawnTimer;
	}
}
