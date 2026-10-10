namespace Realm.Ecs.Common;

/// <summary>
///     Attribute for defining a resource type's metadata.
/// </summary>
public class ResourceDefinitionAttribute : DefinitionAttribute
{
	public ResourceDefinitionAttribute(string id, string? displayName = null, string? description = null,
		string? iconPath = null)
		: base(id, displayName, description)
	{
		IconPath = iconPath ?? string.Empty;
	}

	public string IconPath { get; set; }
}