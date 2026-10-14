namespace Realm.Ecs.Common;

/// <summary>
///     Represents a Tag Definition.
/// </summary>
public class TagDefinition : Definition
{
	public TagDefinition(string id, string? displayName = null, string? description = null)
		: base(id, displayName, description)
	{
	}
}