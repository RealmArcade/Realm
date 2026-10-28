using System;
using System.Collections.Generic;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;

namespace Realm.ListenServer;

/// <summary>
///     Tells a map about the units that enter the authority's world after the match started. Units the map placed while
///     it loaded are the map's own setting, not something it has to react to, so they are taken as already known; every
///     unit after that - one a building trained, one a construction site became, one another script effect summoned -
///     is announced once, in the tick it appeared.
/// </summary>
public class ServerSimulationEvents
{
	private static readonly QueryDescription UnitQuery = QueryCache.AllPositionAndUnitOwnerPlayerNoneDeadQuery;

	private readonly EcsWorld _world;
	private readonly HashSet<int> _knownUnitIds = new();
	private readonly List<int> _appearedUnitIds = new();
	private readonly List<int> _vanishedUnitIds = new();
	private ServerGameAPI? _api;

	public ServerSimulationEvents(EcsWorld world)
	{
		_world = world;
	}

	/// <summary>
	///     Points the bridge at the map that should hear about new units and takes the world as it stands, so a unit the
	///     map placed during loading is not announced as one the match created.
	/// </summary>
	public void Attach(ServerGameAPI api)
	{
		_api = api;
		_knownUnitIds.Clear();

		foreach (Entity entity in _world.Query(in UnitQuery))
		{
			_knownUnitIds.Add(entity.Id);
		}
	}

	/// <summary>Announces the units that appeared since the last tick.</summary>
	public void Step()
	{
		if (_api == null) return;

		_appearedUnitIds.Clear();
		_vanishedUnitIds.Clear();

		foreach (Entity entity in _world.Query(in UnitQuery))
		{
			if (_knownUnitIds.Add(entity.Id)) _appearedUnitIds.Add(entity.Id);
		}

		foreach (int knownUnitId in _knownUnitIds)
		{
			if (!_world.IsAlive(new Entity(knownUnitId))) _vanishedUnitIds.Add(knownUnitId);
		}

		for (int index = 0; index < _vanishedUnitIds.Count; index++)
		{
			// A unit that died was already reported when it was killed, so a unit that simply stopped being owned -
			// destroyed by the map, or cleared out of the world - is only forgotten here rather than reported twice.
			_knownUnitIds.Remove(_vanishedUnitIds[index]);
		}

		for (int index = 0; index < _appearedUnitIds.Count; index++)
		{
			_api.RaiseUnitCreated(_appearedUnitIds[index]);
		}
	}
}
