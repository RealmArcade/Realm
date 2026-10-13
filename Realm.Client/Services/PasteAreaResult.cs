using System.Collections.Generic;

namespace Realm.Client.Services;

public struct PasteAreaResult
{
	public bool TerrainModified;
	public bool HeightsModified;
	public bool PathingModified;
	public List<EntitySpawnRequest> SpawnRequests;
}