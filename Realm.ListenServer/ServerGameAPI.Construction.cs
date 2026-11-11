using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.MapAPI;

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

		return site;
	}
}
