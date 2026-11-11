namespace Realm.Ecs.Common;

/// <summary>
///     Shared simulation constants for resource economy, accessible by both ECS services
///     and Godot orchestrators without cross-layer coupling.
/// </summary>
public static class ResourceConstants
{
	public const float ResourceCap = 9999f;

	/// <summary>
	///     How close a gatherer has to stand to a node to mine it. A unit walks until the node's own collision stops it, so
	///     the reach has to clear the largest node the engine places rather than the smallest.
	/// </summary>
	public const float GatherReach = 5f;
	public const float DefaultGoldPerSec = 1.5f;
	public const float DefaultWoodPerSec = 1.0f;
	public const float DefaultStonePerSec = 0.8f;
	public const float HarvestingUpgradeMultiplier = 1.5f;
}