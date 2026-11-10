using Realm.Ecs;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	/// <summary>Gets the parameters the lobby's host configured for this match, empty when the host left them to the map.</summary>
	public MatchOptionSet MatchOptions { get; } = new();

	public int GetMatchOption(MatchOption matchOption)
	{
		return MatchOptions.Get(matchOption);
	}

	public void RegisterPlacementRadius(string definitionKey, float radius)
	{
		if (string.IsNullOrEmpty(definitionKey)) return;
		_context.MapRules.RegisterPlacementRadius(definitionKey, radius);
	}

	public void RegisterResourceDropoffBuilding(string definitionKey)
	{
		if (string.IsNullOrEmpty(definitionKey)) return;
		_context.MapRules.RegisterResourceDropoffBuilding(definitionKey);
	}

	public void RegisterResourceWorkerUnit(string unitKey)
	{
		if (string.IsNullOrEmpty(unitKey)) return;
		_context.MapRules.RegisterResourceWorkerUnit(unitKey);
	}

	public void RegisterPrimaryBuilding(string definitionId)
	{
		if (string.IsNullOrEmpty(definitionId)) return;
		_context.MapRules.RegisterPrimaryBuilding(definitionId);
	}

	public void RegisterResourceNodeType(string nodeDefinitionId, string resourceType)
	{
		if (string.IsNullOrEmpty(nodeDefinitionId) || string.IsNullOrEmpty(resourceType)) return;
		_context.MapRules.RegisterResourceNodeType(nodeDefinitionId, resourceType);
	}

	public void EnablePassiveResourceIncome()
	{
		_context.MapRules.EnablePassiveResourceIncome();
	}

	public void SetUnitBarVisibility(bool healthBar, bool manaBar, bool levelBadge)
	{
		_context.MapRules.SetUnitBarVisibility(healthBar, manaBar, levelBadge);
	}
}
