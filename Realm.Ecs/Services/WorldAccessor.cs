using Arch.Core;

namespace Realm.Ecs.Services;

/// <summary>
///     Wraps the active ECS world instance to allow recreation and hot swapping of the simulation world.
/// </summary>
public class WorldAccessor
{
	public float ResourceCap { get; set; } = Realm.Ecs.Common.ResourceConstants.ResourceCap;
	public string? _cachedMapName { get; set; }
	public string? _cachedMapVersion { get; set; }
	public string CurrentDirectoryBlake3 { get; set; } = string.Empty;
	public World Current { get; set; }
	public long _lastTerrainSyncTime { get; set; } = 0;
	public long _lastMetadataSyncTime { get; set; } = 0;
	public bool IsGameOver { get; set; }
	public Arch.Core.Entity WorldEntity { get; set; } = Arch.Core.Entity.Null;
	public System.Collections.Generic.Dictionary<(int PlayerIndex, string TechId), int> PlayerTechLevels { get; set; } = new();

	public WorldAccessor(World initialWorld)
	{
		Current = initialWorld;
	}
}
