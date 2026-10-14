namespace Realm.Ecs.Common;

/// <summary>
///     Represents a Stat Definition.
/// </summary>
public class StatDefinition : Definition
{
	public StatDefinition(string id, string? displayName = null, string? description = null)
		: base(id, displayName, description)
	{
	}
}