using System.Numerics;
using System.Text.Json;

namespace Realm.MapAPI;

/// <summary>
/// Configuration parameters for a Tower Defense AI bot.
/// </summary>
public class TowerDefenseBotConfig
{
	/// <summary>
	/// Gets or sets the collection of designated world coordinates where towers can be constructed.
	/// </summary>
	public List<Vector3> BuildSpots { get; set; } = new();

	/// <summary>
	/// Gets or sets whether the AI is permitted to sell obsolete towers during economic emergencies.
	/// </summary>
	public bool AllowSell { get; set; }

	/// <summary>
	/// Converts this configuration into a JSON dictionary string for injection into the AI bot provider.
	/// </summary>
	/// <returns>A JSON string representation of the configuration parameters.</returns>
	public string ToJson()
	{
		var dict = new Dictionary<string, string>
		{
			{ "BuildSpotsJson", JsonSerializer.Serialize(BuildSpots) },
			{ "AllowSell", AllowSell.ToString() }
		};
		return JsonSerializer.Serialize(dict);
	}
}