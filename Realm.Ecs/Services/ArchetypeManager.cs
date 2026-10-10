using Realm.Ecs.Archetypes;

namespace Realm.Ecs.Services;

/// <summary>
///     Loads and manages all game archetypes from definition files.
/// </summary>
public class ArchetypeManager
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private readonly Dictionary<string, UnitArchetype> _unitArchetypes = new();

	public ArchetypeManager(WorldAccessor ecsWorldAccessor, List<UnitArchetype> units, DefinitionManager definitionManager)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
		foreach (var archetype in units)
		{
			foreach (var cost in archetype.ResourceCosts)
				if (!definitionManager.IsValidResource(cost.ResourceTypeId.Value))
					throw new ArgumentException(
						$"Unit Archetype '{archetype.Id}' references an invalid ResourceTypeId: '{cost.ResourceTypeId.Value}'");

			foreach (var capabilityId in archetype.Capabilities)
				if (!definitionManager.IsValidTag(capabilityId))
					throw new ArgumentException(
						$"Unit Archetype '{archetype.Id}' references an invalid CapabilityId: '{capabilityId}'");

			_unitArchetypes[archetype.Id] = archetype;
		}
	}

	public UnitArchetype? GetUnitArchetype(string id)
	{
		return _unitArchetypes.TryGetValue(id, out var archetype) ? archetype : null;
	}

	public IEnumerable<UnitArchetype> GetAllUnitArchetypes()
	{
		return _unitArchetypes.Values;
	}
}