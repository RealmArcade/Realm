namespace Realm.Ecs.Common;

/// <summary>
///     Represents a Resource Definition.
/// </summary>
public class ResourceDefinition : Definition
{
	public ResourceDefinition(string id, string? displayName = null, string? description = null,
		string? iconPath = null)
		: base(id, displayName, description)
	{
		IconPath = iconPath ?? string.Empty;
	}

	public string IconPath { get; set; }
}