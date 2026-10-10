using System.Numerics;
using System.Text.Json;

namespace Realm.MapAPI;

/// <summary>
/// Configuration parameters for a standard melee or skirmish RTS AI bot.
/// </summary>
public class MeleeBotConfig
{
	/// <summary>
	/// Gets or sets the health percentage (0.0 to 1.0) below which a unit prioritizes tactical retreat.
	/// </summary>
	public float RetreatHealthPercent { get; set; } = 0.25f;

	/// <summary>
	/// Gets or sets the multiplier applied to kiting distance calculation when evading melee threats.
	/// </summary>
	public float KitingDistanceBias { get; set; } = 1.0f;

	/// <summary>
	/// Gets or sets the aggression weight multiplier applied when deciding between engaging enemy forces or holding ground.
	/// </summary>
	public float AggressionBias { get; set; } = 1.0f;

	/// <summary>
	/// Gets or sets the designated map coordinates for strategic expansion bases or resource outposts.
	/// </summary>
	public List<Vector3> ExpansionLocations { get; set; } = new();

	/// <summary>
	/// Gets or sets the designated map coordinates for army rally and defensive regrouping points.
	/// </summary>
	public List<Vector3> RallyPoints { get; set; } = new();

	/// <summary>
	/// Converts this configuration into a JSON dictionary string for injection into the AI bot provider.
	/// </summary>
	/// <returns>A JSON string representation of the configuration parameters.</returns>
	public string ToJson()
	{
		var dict = new Dictionary<string, string>
		{
			{ "RetreatHealthPercent", RetreatHealthPercent.ToString(System.Globalization.CultureInfo.InvariantCulture) },
			{ "KitingDistanceBias", KitingDistanceBias.ToString(System.Globalization.CultureInfo.InvariantCulture) },
			{ "AggressionBias", AggressionBias.ToString(System.Globalization.CultureInfo.InvariantCulture) },
			{ "ExpansionLocationsJson", JsonSerializer.Serialize(ExpansionLocations) },
			{ "RallyPointsJson", JsonSerializer.Serialize(RallyPoints) }
		};
		return JsonSerializer.Serialize(dict);
	}
}