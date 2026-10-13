using System.Numerics;
using System.Runtime.Loader;
using Realm.Client.Core;
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
		if (GameContext.World?.TryGet<ScriptZonesState>(ECSContext.WorldEntity, out var zonesState) == true)
		{
			int handle = zonesState.Zones.Count;
			float cx = (minX + maxX) * 0.5f;
			float cz = (minZ + maxZ) * 0.5f;
			zonesState.Zones.Add(new ZoneBounds
			{
				MinX = minX,
				MinZ = minZ,
				MaxX = maxX,
				MaxZ = maxZ,
				Center = new Vector3(cx, 0f, cz)
			});
			return handle;
		}

		return -1;
	}

	public Vector3 GetZoneCenter(int zoneHandle)
	{
		if (GameContext.World?.TryGet<ScriptZonesState>(ECSContext.WorldEntity, out var zonesState) == true
		    && zoneHandle >= 0 && zoneHandle < zonesState.Zones.Count)
		{
			return zonesState.Zones[zoneHandle].Center;
		}

		return Vector3.Zero;
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