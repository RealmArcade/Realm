namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	/// <summary>
	///     Declares how much room a building needs around it. Every client and the authority read the rule from the map
	///     itself, so a script only has to say it once.
	/// </summary>
	public void RegisterPlacementRadius(string definitionKey, float radius)
	{
		if (string.IsNullOrEmpty(definitionKey)) return;
		_context.MapRules.RegisterPlacementRadius(definitionKey, radius);
	}

	/// <summary>Declares which buildings a worker may hand its harvest in at.</summary>
	public void RegisterResourceDropoffBuilding(string definitionKey)
	{
		if (string.IsNullOrEmpty(definitionKey)) return;
		_context.MapRules.RegisterResourceDropoffBuilding(definitionKey);
	}

	/// <summary>Declares which units may harvest, which is what makes a worker a worker.</summary>
	public void RegisterResourceWorkerUnit(string unitKey)
	{
		if (string.IsNullOrEmpty(unitKey)) return;
		_context.MapRules.RegisterResourceWorkerUnit(unitKey);
	}

	/// <summary>Declares which building is the one whose loss costs a player the match.</summary>
	public void RegisterPrimaryBuilding(string definitionId)
	{
		if (string.IsNullOrEmpty(definitionId)) return;
		_context.MapRules.RegisterPrimaryBuilding(definitionId);
	}

	/// <summary>Declares what a harvestable node's definition yields when it is worked.</summary>
	public void RegisterResourceNodeType(string nodeDefinitionId, string resourceType)
	{
		if (string.IsNullOrEmpty(nodeDefinitionId) || string.IsNullOrEmpty(resourceType)) return;
		_context.MapRules.RegisterResourceNodeType(nodeDefinitionId, resourceType);
	}
}
