using Realm.Ecs.Common;

namespace Realm.Ecs.Services;

/// <summary>
///     Demonstrates how player-specific resource data is used with archetype definition data.
/// </summary>
public class PlayerResourceService
{
	public float ResourceCap { get; set; } = ResourceConstants.ResourceCap;
	public ResourceId GoldResourceId { get; set; }
	public ResourceId WoodResourceId { get; set; }
	public ResourceId StoneResourceId { get; set; }

	public PlayerResourceService()
	{
	}
}