namespace Realm.Ecs.Common;

/// <summary>
///     A common base class for serializable definitions like resources or stats.
/// </summary>
public class Definition
{
	public Definition(string id, string? displayName = null, string? description = null)
	{
		Id = id;
		DisplayName = displayName ?? id;
		Description = description ?? string.Empty;
	}

	public string Id { get; set; }
	public string DisplayName { get; set; }
	public string Description { get; set; }
}