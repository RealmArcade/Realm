using Realm.Ecs.Services;

namespace Realm.Ecs.Common;

/// <summary>
///     Provides extension methods for type-safe ID construction from the DefinitionManager.
/// </summary>
public static class IdExtensions
{
	public static TagId AsTagId(this string id, DefinitionManager manager)
	{
		var tag = manager.GetTag(id);
		if (tag == null)
			throw new ArgumentException($"Tag ID '{id}' is not registered with the DefinitionManager.");
		return new TagId(tag.Value.Definition.Id);
	}

	/// <summary>
	///     Creates a type-safe ResourceId wrapper from a string.
	///     Performs a runtime check to ensure the ID is registered with the DefinitionManager.
	/// </summary>
	public static ResourceId AsResourceId(this string id, DefinitionManager manager)
	{
		var resource = manager.GetResource(id);
		if (resource == null)
			throw new ArgumentException($"Resource ID '{id}' is not registered with the DefinitionManager.");
		return new ResourceId(resource.Value.Definition.Id);
	}

	/// <summary>
	///     Creates a type-safe StatId wrapper from a string.
	///     Performs a runtime check to ensure the ID is registered with the DefinitionManager.
	/// </summary>
	public static StatId AsStatId(this string id, DefinitionManager manager)
	{
		var stat = manager.GetStat(id);
		if (stat == null)
			throw new ArgumentException($"Stat ID '{id}' is not registered with the DefinitionManager.");
		return new StatId(stat.Value.Definition.Id);
	}
}