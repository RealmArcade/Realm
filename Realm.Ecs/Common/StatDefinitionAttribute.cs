namespace Realm.Ecs.Common;

/// <summary>
///     Attribute for defining a stat type's metadata.
/// </summary>
public class StatDefinitionAttribute : DefinitionAttribute
{
	public StatDefinitionAttribute(string id, string? displayName = null, string? description = null)
		: base(id, displayName, description)
	{
	}
}