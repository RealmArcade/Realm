using Arch.Core;

namespace Realm.Ecs.Services;

/// <summary>
///     Wraps the active ECS world instance to allow recreation and hot swapping of the simulation world.
/// </summary>
public class WorldAccessor
{
	public World Current { get; set; }
	public long _lastTerrainSyncTime { get; set; } = 0;
	public long _lastMetadataSyncTime { get; set; } = 0;

	public WorldAccessor(World initialWorld)
	{
		Current = initialWorld;
	}
}
