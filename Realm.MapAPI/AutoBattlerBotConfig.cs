using System.Numerics;
using System.Text.Json;

namespace Realm.MapAPI;

/// <summary>
/// Configuration parameters for an Auto-Battler AI bot.
/// </summary>
public class AutoBattlerBotConfig
{
	/// <summary>
	/// Gets or sets the collection of board tile coordinates available for deployed combat units.
	/// </summary>
	public List<Vector3> BoardSlots { get; set; } = new();

	/// <summary>
	/// Gets or sets the collection of bench tile coordinates available for reserve units.
	/// </summary>
	public List<Vector3> BenchSlots { get; set; } = new();

	/// <summary>
	/// Gets or sets the gold cost required to reroll the shop offerings.
	/// </summary>
	public int RerollCost { get; set; } = 2;

	/// <summary>
	/// Gets or sets the gold cost required to purchase shop experience / level up.
	/// </summary>
	public int LevelUpCost { get; set; } = 4;

	/// <summary>
	/// Converts this configuration into a JSON dictionary string for injection into the AI bot provider.
	/// </summary>
	/// <returns>A JSON string representation of the configuration parameters.</returns>
	public string ToJson()
	{
		var dict = new Dictionary<string, string>
		{
			{ "BoardSlotsJson", JsonSerializer.Serialize(BoardSlots) },
			{ "BenchSlotsJson", JsonSerializer.Serialize(BenchSlots) },
			{ "RerollCost", RerollCost.ToString() },
			{ "LevelUpCost", LevelUpCost.ToString() }
		};
		return JsonSerializer.Serialize(dict);
	}
}