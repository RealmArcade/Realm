using Arch.Core;
using Realm.Ecs.Archetypes;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using System.Numerics;
using System.Reflection;

namespace Realm.Ecs.Services;

/// <summary>
///     A generic factory for creating entities from archetypes using reflection.
///     This has been optimized to cache reflection results for performance.
/// </summary>
public class EntityFactory
{
	private static readonly MethodInfo SetComponentMethodInfo = typeof(World).GetMethod(nameof(World.Set)) ?? throw new InvalidOperationException("World.Set method not found");
	private static readonly MethodInfo AddMethodInfo = typeof(World).GetMethod(nameof(World.Add)) ?? throw new InvalidOperationException("World.Add method not found");
	private static readonly PropertyInfo[] ArchetypeComponentProperties = typeof(UnitArchetype).GetProperties(BindingFlags.Public | BindingFlags.Instance)
		.Where(p => p.Name is not "Id" and not "Name" and not "Description" and not "Capabilities"
			and not "Abilities" && p.PropertyType.IsValueType)
		.ToArray();

	private readonly ArchetypeManager _archetypeManager;
	private readonly DefinitionManager _definitionManager;
	private readonly Dictionary<Type, MethodInfo> _setComponentCache = new();
	private readonly Dictionary<Type, MethodInfo> _addMethodCache = new();
	private readonly WorldAccessor _ecsWorldAccessor;

	public EntityFactory(WorldAccessor ecsWorldAccessor, ArchetypeManager archetypeManager, DefinitionManager definitionManager)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
		_archetypeManager = archetypeManager;
		_definitionManager = definitionManager;
		CacheComponentSetters();
	}

	private void CacheComponentSetters()
	{
		var componentProperties = typeof(UnitArchetype).GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.Name is not "Id" and not "Name" and not "Description" and not "Capabilities"
				and not "Abilities" && p.PropertyType.IsValueType);

		foreach (var prop in componentProperties)
			_setComponentCache[prop.PropertyType] = SetComponentMethodInfo.MakeGenericMethod(prop.PropertyType);
	}

	public Entity SpawnUnit(string archetypeId, Vector3 position)
	{
		var archetype = _archetypeManager.GetUnitArchetype(archetypeId);
		if (archetype == null) throw new ArgumentException($"Archetype with ID '{archetypeId}' not found.");

		var entity = _ecsWorldAccessor.Current.Create();

		foreach (var prop in ArchetypeComponentProperties)
		{
			var componentValue = prop.GetValue(archetype);
			if (componentValue != null)
				if (_setComponentCache.TryGetValue(prop.PropertyType, out var setter))
					setter.Invoke(_ecsWorldAccessor.Current, new[] { entity, componentValue });
		}

		_ecsWorldAccessor.Current.Set(entity, new DefinitionId(archetypeId));
		_ecsWorldAccessor.Current.Set(entity, new Name(archetype.Name));
		_ecsWorldAccessor.Current.Set(entity, new Position(position));

		foreach (var capabilityId in archetype.Capabilities)
		{
			var tagInfo = _definitionManager.GetTag(capabilityId);
			if (tagInfo.HasValue)
			{
				var (_, tagType) = tagInfo.Value;

				if (tagType != null && tagType.IsValueType)
				{
					if (!_addMethodCache.TryGetValue(tagType, out var genericAdd))
					{
						genericAdd = AddMethodInfo.MakeGenericMethod(tagType);
						_addMethodCache[tagType] = genericAdd;
					}
					genericAdd.Invoke(_ecsWorldAccessor.Current, new object[] { entity });
				}
				else
				{
					Console.WriteLine(
						$"Warning: Capability ID '{capabilityId}' has a definition but does not map to a valid tag struct.");
				}
			}
			else
			{
				Console.WriteLine(
					$"Warning: Capability ID '{capabilityId}' is not defined in TagManager and cannot be added.");
			}
		}

		return entity;
	}
}