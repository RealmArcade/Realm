using System;
using System.Collections.Generic;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;

namespace Realm.ListenServer;

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

	public void Attach(ServerGameAPI api)
	{
		_api = api;
		_knownUnitIds.Clear();

		foreach (Entity entity in _world.Query(in UnitQuery))
		{
			_knownUnitIds.Add(entity.Id);
		}
	}

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
			_knownUnitIds.Remove(_vanishedUnitIds[index]);
		}

		for (int index = 0; index < _appearedUnitIds.Count; index++)
		{
			_api.RaiseUnitCreated(_appearedUnitIds[index]);
		}
	}
}
