using Realm.Ecs.Common;
using System.Reflection;

namespace Realm.Ecs.Services;

/// <summary>
///     Manages the definitions of all game elements (tags, resources, stats) by discovering them
///     from attributes on structs in specified assemblies.
/// </summary>
public class DefinitionManager
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private readonly Dictionary<string, (ResourceDefinition Definition, Type ComponentType)> _resources = new();
	private readonly Dictionary<string, (StatDefinition Definition, Type ComponentType)> _stats = new();
	private readonly Dictionary<string, (TagDefinition Definition, Type ComponentType)> _tags = new();

	public DefinitionManager(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
		var assembly = Assembly.GetExecutingAssembly();

		LoadDefinitions<TagDefinitionAttribute, TagDefinition>(assembly, _tags);
		LoadDefinitions<ResourceDefinitionAttribute, ResourceDefinition>(assembly, _resources);
		LoadDefinitions<StatDefinitionAttribute, StatDefinition>(assembly, _stats);
	}

	private static void LoadDefinitions<TAttribute, TDefinition>(Assembly assembly,
		Dictionary<string, (TDefinition Definition, Type ComponentType)> dictionary)
		where TAttribute : DefinitionAttribute
		where TDefinition : Definition
	{
		var types = assembly.GetTypes()
			.Where(t => t.IsValueType && t.GetCustomAttribute<TAttribute>() != null);

		foreach (var type in types)
		{
			var attribute = type.GetCustomAttribute<TAttribute>();
			if (attribute != null)
			{
				TDefinition? definition = null;

				if (attribute is TagDefinitionAttribute tagAttr)
					definition =
						(TDefinition)(object)new TagDefinition(tagAttr.Id, tagAttr.DisplayName, tagAttr.Description);
				else if (attribute is ResourceDefinitionAttribute resourceAttr)
					definition = (TDefinition)(object)new ResourceDefinition(resourceAttr.Id, resourceAttr.DisplayName,
						resourceAttr.Description, resourceAttr.IconPath);
				else if (attribute is StatDefinitionAttribute statAttr)
					definition =
						(TDefinition)(object)new StatDefinition(statAttr.Id, statAttr.DisplayName,
							statAttr.Description);

				if (definition != null) dictionary[attribute.Id.ToLowerInvariant()] = (definition, type);
			}
		}
	}

	public bool IsValidTag(string tagId)
	{
		return _tags.ContainsKey(tagId.ToLowerInvariant());
	}

	public (TagDefinition Definition, Type ComponentType)? GetTag(string tagId)
	{
		return _tags.TryGetValue(tagId.ToLowerInvariant(), out var tagInfo) ? tagInfo : null;
	}

	public bool IsValidResource(string resourceId)
	{
		return _resources.ContainsKey(resourceId.ToLowerInvariant());
	}

	public (ResourceDefinition Definition, Type ComponentType)? GetResource(string resourceId)
	{
		return _resources.TryGetValue(resourceId.ToLowerInvariant(), out var resourceInfo) ? resourceInfo : null;
	}

	public bool IsValidStat(string statId)
	{
		return _stats.ContainsKey(statId.ToLowerInvariant());
	}

	public (StatDefinition Definition, Type ComponentType)? GetStat(string statId)
	{
		return _stats.TryGetValue(statId.ToLowerInvariant(), out var statInfo) ? statInfo : null;
	}
}