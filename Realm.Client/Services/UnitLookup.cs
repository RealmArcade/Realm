using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Components.Tags;
using UnitId = Realm.Ecs.UnitId;

namespace Realm.Client.Services;

/// <summary>
///     Turning a UnitId into the entity or the script wrapper behind it: the cache of wrappers built for
///     this match, the fallback walk over the host's live unit list, and the count of a player's living
///     units.
/// </summary>
public sealed class UnitLookup
{
	private readonly Dictionary<int, Unit_WasmRuntime> _cache;

	public UnitLookup(Dictionary<int, Unit_WasmRuntime> cache)
	{
		_cache = cache;
	}

	public Unit_WasmRuntime GetUnitWrapper(Entity entity)
	{
		if (!SimulationService.World.IsAlive(entity))
		{
			throw new ArgumentException("Entity is not alive", nameof(entity));
		}

		if (_cache.TryGetValue(entity.Id, out var wrapper))
		{
			return wrapper;
		}

		wrapper = new Unit_WasmRuntime(entity, SimulationService.World);
		_cache[entity.Id] = wrapper;
		return wrapper;
	}

	public bool TryGetEntity(UnitId unit, out Entity entity)
	{
		if (!unit.IsValid)
		{
			entity = Entity.Null;
			return false;
		}

		if (_cache.TryGetValue(unit.Value, out var wrapper) && GameContext.World != null && SimulationService.World.IsAlive(((IEcsEntityWrapper)wrapper).Entity))
		{
			entity = ((IEcsEntityWrapper)wrapper).Entity;
			return true;
		}

		if (GameHost.EntityIdToUnit3D.TryGetValue(unit.Value, out var u) && GodotObject.IsInstanceValid(u) && GameContext.World != null && SimulationService.World.IsAlive(u.Entity))
		{
			entity = u.Entity;
			return true;
		}

		entity = Entity.Null;
		return false;
	}

	public Unit_WasmRuntime? GetUnitWrapper(UnitId unit)
	{
		if (!unit.IsValid) return null;

		if (_cache.TryGetValue(unit.Value, out var wrapper) && GameContext.World != null && SimulationService.World.IsAlive(((IEcsEntityWrapper)wrapper).Entity))
		{
			return wrapper;
		}

		if (GameContext.Host != null)
		{
			var allUnits = GameContext.Host.AllUnits;
			for (int i = 0; i < allUnits.Count; i++)
			{
				var u = allUnits[i];
				if (GodotObject.IsInstanceValid(u) && u.Entity.Id == unit.Value && GameContext.World != null && SimulationService.World.IsAlive(u.Entity))
				{
					return GetUnitWrapper(u.Entity);
				}
			}
		}

		return null;
	}

	public int CountUnitsOwnedByPlayer(ReadOnlySpan<UnitId> units)
	{
		int count = 0;
		for (int i = 0; i < units.Length; i++)
		{
			var wrapper = GetUnitWrapper(units[i]);
			if (wrapper != null && !wrapper.IsDead)
				count++;
		}

		return count;
	}
}
