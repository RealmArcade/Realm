using System;
using System.Collections.Generic;

namespace Realm.Ecs.Services;

/// <summary>
///     The harvesting and placement rules a match runs under, as the map's scripts declared them: which definition
///     yields which resource, which buildings accept a delivery, which units may carry one, which building is a
///     player's primary, and how much room a placement takes. The rules belong to the match, so the client reads the
///     same answers the authority acts on instead of holding a second copy that can drift.
/// </summary>
public class MapRulesCatalog
{
	private const float DefaultPlacementRadius = 1.2f;

	private readonly List<(string DefinitionKey, float Radius)> _placementRadiusRules = new();
	private readonly List<string> _resourceDropoffBuildingKeys = new();
	private readonly List<string> _resourceWorkerUnitKeys = new();
	private readonly List<string> _primaryBuildingIds = new();
	private readonly Dictionary<string, string> _resourceTypeByNodeId = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	///     Answers a building's build options. A building that trains a resource worker counts as a delivery point even
	///     when the map never named it as one, so this is asked before the answer is a no.
	/// </summary>
	public Func<string, string[]?>? BuildOptionsProvider { get; set; }

	/// <summary>Answers the population a unit costs, which the per-player population totals are summed from.</summary>
	public Func<string, int>? PopulationCostProvider { get; set; }

	public IReadOnlyList<(string DefinitionKey, float Radius)> PlacementRadiusRules => _placementRadiusRules;
	public IReadOnlyList<string> ResourceDropoffBuildingKeys => _resourceDropoffBuildingKeys;
	public IReadOnlyList<string> ResourceWorkerUnitKeys => _resourceWorkerUnitKeys;
	public IReadOnlyList<string> PrimaryBuildingIds => _primaryBuildingIds;
	public IReadOnlyDictionary<string, string> ResourceNodeTypes => _resourceTypeByNodeId;

	public void RegisterPlacementRadius(string definitionKey, float radius)
	{
		ArgumentException.ThrowIfNullOrEmpty(definitionKey);
		_placementRadiusRules.Add((definitionKey, radius));
	}

	public float GetPlacementRadius(string placeId, float scale)
	{
		if (string.IsNullOrEmpty(placeId)) return DefaultPlacementRadius * scale;
		foreach (var (definitionKey, radius) in _placementRadiusRules)
		{
			if (placeId.Contains(definitionKey, StringComparison.OrdinalIgnoreCase)) return radius * scale;
		}

		return DefaultPlacementRadius * scale;
	}

	public void RegisterResourceDropoffBuilding(string definitionKey)
	{
		ArgumentException.ThrowIfNullOrEmpty(definitionKey);
		_resourceDropoffBuildingKeys.Add(definitionKey);
	}

	public void RegisterResourceWorkerUnit(string unitKey)
	{
		ArgumentException.ThrowIfNullOrEmpty(unitKey);
		_resourceWorkerUnitKeys.Add(unitKey);
	}

	public bool IsResourceDropoffBuilding(string definitionId)
	{
		if (string.IsNullOrEmpty(definitionId)) return false;

		for (int index = 0; index < _resourceDropoffBuildingKeys.Count; index++)
		{
			if (definitionId.Contains(_resourceDropoffBuildingKeys[index], StringComparison.OrdinalIgnoreCase)) return true;
		}

		string[]? buildOptions = BuildOptionsProvider?.Invoke(definitionId);
		if (buildOptions == null) return false;

		for (int optionIndex = 0; optionIndex < buildOptions.Length; optionIndex++)
		{
			for (int workerIndex = 0; workerIndex < _resourceWorkerUnitKeys.Count; workerIndex++)
			{
				if (buildOptions[optionIndex].Contains(_resourceWorkerUnitKeys[workerIndex], StringComparison.Ordinal)) return true;
			}
		}

		return false;
	}

	public void RegisterPrimaryBuilding(string definitionId)
	{
		ArgumentException.ThrowIfNullOrEmpty(definitionId);
		_primaryBuildingIds.Add(definitionId);
	}

	public bool IsPrimaryBuilding(string definitionId)
	{
		if (string.IsNullOrEmpty(definitionId)) return false;

		for (int index = 0; index < _primaryBuildingIds.Count; index++)
		{
			if (string.Equals(definitionId, _primaryBuildingIds[index], StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	public void RegisterResourceNodeType(string nodeDefinitionId, string resourceType)
	{
		ArgumentException.ThrowIfNullOrEmpty(nodeDefinitionId);
		ArgumentException.ThrowIfNullOrEmpty(resourceType);
		_resourceTypeByNodeId[nodeDefinitionId] = resourceType;
	}

	public string? GetResourceTypeForNode(string nodeDefinitionId)
	{
		return _resourceTypeByNodeId.TryGetValue(nodeDefinitionId, out string? resourceType) ? resourceType : null;
	}

	/// <summary>
	///     Takes one rule a peer published, ignoring one this match already has. A rule that arrived by either route —
	///     a map script declaring it or a peer sending it — is the same rule, so the first arrival wins and a late
	///     duplicate never doubles a list.
	/// </summary>
	public void ApplyCatalogEntry(string channel, string key, string value, float number)
	{
		if (string.IsNullOrEmpty(key)) return;

		switch (channel)
		{
			case "placement_radius":
				if (!HasPlacementRadius(key)) _placementRadiusRules.Add((key, number));
				break;
			case "dropoff":
				if (!ContainsKey(_resourceDropoffBuildingKeys, key)) _resourceDropoffBuildingKeys.Add(key);
				break;
			case "worker":
				if (!ContainsKey(_resourceWorkerUnitKeys, key)) _resourceWorkerUnitKeys.Add(key);
				break;
			case "primary":
				if (!ContainsKey(_primaryBuildingIds, key)) _primaryBuildingIds.Add(key);
				break;
			case "resource_node_type":
				if (!_resourceTypeByNodeId.ContainsKey(key)) _resourceTypeByNodeId[key] = value;
				break;
		}
	}

	private bool HasPlacementRadius(string definitionKey)
	{
		for (int index = 0; index < _placementRadiusRules.Count; index++)
		{
			if (string.Equals(_placementRadiusRules[index].DefinitionKey, definitionKey, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	private static bool ContainsKey(List<string> keys, string key)
	{
		for (int index = 0; index < keys.Count; index++)
		{
			if (string.Equals(keys[index], key, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	/// <summary>The population a definition occupies, or zero when the provider has no answer for it.</summary>
	public int GetPopulationCost(string definitionId)
	{
		if (string.IsNullOrEmpty(definitionId)) return 0;
		return PopulationCostProvider?.Invoke(definitionId) ?? 0;
	}
}
