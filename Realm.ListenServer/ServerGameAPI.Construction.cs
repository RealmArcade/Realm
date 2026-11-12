using System;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.MapAPI;
using Realm.Shared.Maps;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	public UnitId SpawnBuildingUnderConstruction(string buildingTypeId, Vector3 position, int playerIndex, float totalConstructionSeconds)
	{
		if (string.IsNullOrEmpty(buildingTypeId)) return UnitId.Invalid;
		if (_map.Catalog.FindBuilding(buildingTypeId) == null)
		{
			_log.Write($"the map asked for building site '{buildingTypeId}', which its templates do not define as a building");
			return UnitId.Invalid;
		}

		PlayerEntity owner = _map.GetPlayerEntity(playerIndex);
		if (owner.Value == Entity.Null)
		{
			_log.Write($"the map asked for a '{buildingTypeId}' site for player {playerIndex}, which this map does not seat");
			return UnitId.Invalid;
		}

		UnitId site = SpawnUnit(buildingTypeId, position, owner, playerIndex, bypassPopulation: false);
		if (!site.IsValid) return site;

		Entity siteEntity = new(site.Value);
		var construction = new ConstructionState(Math.Max(totalConstructionSeconds, 0f));
		_context.World.SetOrAdd(siteEntity, construction);
		_context.World.SetOrAdd(siteEntity, new UnderConstruction());
		_context.World.SetOrAdd(siteEntity, new Invulnerable());

		return site;
	}

	/// <summary>
	///     Turns a unit that already stands into a building site: it takes the amount of work it was given before it
	///     completes, and it is announced through <see cref="OnConstructionFinished" /> like any other site. Whether a
	///     site may be attacked, ordered or worked on stays the map's own decision, so starting a construction writes
	///     nothing but the construction itself.
	/// </summary>
	public void StartUnitConstruction(UnitId unit, float totalConstructionSeconds)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (_context.World.Has<ConstructionState>(entity)) return;

		_context.World.SetOrAdd(entity, new ConstructionState(MathF.Max(totalConstructionSeconds, MinimumConstructionSeconds)));
		_context.World.SetOrAdd(entity, new UnderConstruction());
	}

	/// <summary>
	///     Holds a site short of completion: it takes work up to the fraction it was given and stops there, so it never
	///     becomes a building until the map lets it. Progress the site has already had beyond the new ceiling is taken
	///     back, which keeps what the site shows and what it may take the same number.
	/// </summary>
	public void SetUnitConstructionCeiling(UnitId unit, float finishedFraction)
	{
		Entity entity = ResolveUnit(unit);
		if (entity == Entity.Null) return;
		if (!_context.World.TryGet<ConstructionState>(entity, out ConstructionState construction)) return;

		construction.CompletionCeilingFraction = Math.Clamp(finishedFraction, 0f, 1f);
		construction.Progress = MathF.Min(construction.Progress, construction.ProgressCeiling);
		_context.World.Set(entity, construction);
	}

	/// <summary>
	///     Reveals what a site is being raised as: the site takes the chosen building's template, so every screen draws
	///     that building from the next description it receives, and the work it now needs is the building's own. The
	///     work the site has already taken stays with it rather than being paid again, and it is capped at what the new
	///     construction time allows.
	/// </summary>
	public bool RevealBuildingSite(UnitId site, string buildingTypeId, float totalConstructionSeconds)
	{
		if (string.IsNullOrEmpty(buildingTypeId)) return false;

		Entity entity = ResolveUnit(site);
		if (entity == Entity.Null) return false;
		if (_map.Catalog.FindBuilding(buildingTypeId) == null)
		{
			_log.Write($"the map revealed site {entity.Id} as '{buildingTypeId}', which its templates do not define as a building");
			return false;
		}

		if (!UnitEntityFactory.ApplyTemplate(_context.World, _map.Catalog, entity, buildingTypeId)) return false;

		if (_context.World.TryGet<ConstructionState>(entity, out ConstructionState construction))
		{
			construction.TotalBuildTime = MathF.Max(totalConstructionSeconds, MinimumConstructionSeconds);
			construction.Progress = MathF.Min(construction.Progress, construction.ProgressCeiling);
			_context.World.Set(entity, construction);
		}

		return true;
	}

	private const float MinimumConstructionSeconds = 0.01f;
}
