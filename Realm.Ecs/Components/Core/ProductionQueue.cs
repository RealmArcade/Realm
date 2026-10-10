namespace Realm.Ecs.Components.Core;

/// <summary>
///     Manages the production/training queue for RTS structures.
/// </summary>
public struct ProductionQueue
{
	public ProductionQueue()
	{
	}

	public List<string> UnitIds { get; set; } = new List<string>();
	public float CurrentProgress { get; set; } = 0f;
	public float BuildTime { get; set; } = 5f;
}
