namespace Realm.Ecs.Common;

/// <summary>
///     Attribute for defining a resource type's metadata.
/// </summary>
public class ResourceDefinitionAttribute : DefinitionAttribute
{
	public ResourceDefinitionAttribute(string id, string? displayName = null, string? description = null)
		: base(id, displayName, description)
	{
	}
}
