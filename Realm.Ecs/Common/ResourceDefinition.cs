namespace Realm.Ecs.Common;

/// <summary>
///     Represents a Resource Definition.
/// </summary>
public class ResourceDefinition : Definition
{
	public ResourceDefinition(string id, string? displayName = null, string? description = null)
		: base(id, displayName, description)
	{
	}
}
