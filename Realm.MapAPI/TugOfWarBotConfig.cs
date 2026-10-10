using System.Numerics;
using System.Text.Json;

namespace Realm.MapAPI;

/// <summary>
/// Configuration parameters for a Tug-of-War or auto-spawning wave combat AI bot (e.g., Castle Fight, Desert Strike, Nexus Wars).
/// </summary>
public class TugOfWarBotConfig
{
	/// <summary>
	/// Gets or sets the collection of designated world coordinates where auto-spawner structures can be constructed.
	/// </summary>
	public List<Vector3> BuildSpots { get; set; } = new();

	/// <summary>
	/// Gets or sets the available spawner unit or structure type identifiers the AI can choose to construct.
	/// </summary>
	public List<string> SpawnerTypes { get; set; } = new();

	/// <summary>
	/// Gets or sets the base gold cost to upgrade the player's periodic income rate.
	/// </summary>
	public int IncomeUpgradeCost { get; set; } = 100;

	/// <summary>
	/// Gets or sets whether the AI is permitted to sell or replace existing spawner structures.
	/// </summary>
	public bool AllowSell { get; set; }

	/// <summary>
	/// Gets or sets the target lane destination or enemy base coordinate that spawned units attack towards.
	/// </summary>
	public Vector3 LaneTarget { get; set; } = Vector3.Zero;

	/// <summary>
	/// Converts this configuration into a JSON dictionary string for injection into the AI bot provider.
	/// </summary>
	/// <returns>A JSON string representation of the configuration parameters.</returns>
	public string ToJson()
	{
		var dict = new Dictionary<string, string>
		{
			{ "BuildSpotsJson", JsonSerializer.Serialize(BuildSpots) },
			{ "SpawnerTypesJson", JsonSerializer.Serialize(SpawnerTypes) },
			{ "IncomeUpgradeCost", IncomeUpgradeCost.ToString() },
			{ "AllowSell", AllowSell.ToString() },
			{ "LaneTargetJson", JsonSerializer.Serialize(LaneTarget) }
		};
		return JsonSerializer.Serialize(dict);
	}
}