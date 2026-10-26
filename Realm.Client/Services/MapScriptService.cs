using System.Numerics;
using System.Runtime.Loader;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Realm.MapAPI;

namespace Realm.Client.Services;

public class MapScriptService
{
	public IMapScript? ActiveMapScript { get; set; }
	public string? PendingMapScriptPath { get; set; }
	public AssemblyLoadContext? MapScriptLoadContext { get; set; }

	public int DefineZone(float minX, float minZ, float maxX, float maxZ)
	{
		var worldEntity = GameContext.Simulation.WorldEntity;
		if (GameContext.World == null || worldEntity == Entity.Null || !GameContext.World.IsAlive(worldEntity)) return -1;
		if (!GameContext.World.Has<ScriptZonesState>(worldEntity)) return -1;

		ref var zonesState = ref GameContext.World.Get<ScriptZonesState>(worldEntity);
		int handle = zonesState.Count;
		float cx = (minX + maxX) * 0.5f;
		float cz = (minZ + maxZ) * 0.5f;
		zonesState.Add(new ZoneBounds
		{
			MinX = minX,
			MinZ = minZ,
			MaxX = maxX,
			MaxZ = maxZ,
			Center = new Vector3(cx, 0f, cz)
		});

		return handle;
	}

	public Vector3 GetZoneCenter(int zoneHandle)
	{
		var worldEntity = GameContext.Simulation.WorldEntity;
		if (GameContext.World == null || worldEntity == Entity.Null || !GameContext.World.IsAlive(worldEntity)) return Vector3.Zero;
		if (!GameContext.World.Has<ScriptZonesState>(worldEntity)) return Vector3.Zero;

		ref var zonesState = ref GameContext.World.Get<ScriptZonesState>(worldEntity);
		if (zoneHandle < 0 || zoneHandle >= zonesState.Count) return Vector3.Zero;

		return zonesState.GetZone(zoneHandle).Center;
	}

	public void ClearAll()
	{
		ActiveMapScript = null;
		PendingMapScriptPath = null;
		if (MapScriptLoadContext != null)
		{
			MapScriptLoadContext.Unload();
			MapScriptLoadContext = null;
		}
	}
}