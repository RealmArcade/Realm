using System.Numerics;
using System.Text.Json;

namespace Realm.MapAPI;

/// <summary>
/// Configuration parameters for a Hero Arena, MOBA, or Aeon of Strife (AOS) AI bot (e.g., DotA, Footman Frenzy, Hero Battles).
/// </summary>
public class HeroArenaBotConfig
{
	/// <summary>
	/// Gets or sets the world coordinate of the team's home fountain or healing well.
	/// </summary>
	public Vector3 FountainPosition { get; set; } = Vector3.Zero;

	/// <summary>
	/// Gets or sets the radius around the fountain position considered safe healing territory.
	/// </summary>
	public float FountainHealRadius { get; set; } = 15.0f;

	/// <summary>
	/// Gets or sets the health percentage (0.0 to 1.0) below which the hero prioritizes retreating to the fountain.
	/// </summary>
	public float RetreatHealthPercent { get; set; } = 0.30f;

	/// <summary>
	/// Gets or sets the mana percentage (0.0 to 1.0) below which the hero prioritizes retreating or conserving spells.
	/// </summary>
	public float RetreatManaPercent { get; set; } = 0.15f;

	/// <summary>
	/// Gets or sets the collection of designated world coordinates where power runes, shrines, or neutral buffs spawn.
	/// </summary>
	public List<Vector3> RunePositions { get; set; } = new();

	/// <summary>
	/// Gets or sets the prioritized item purchase build order identifiers.
	/// </summary>
	public List<string> ShopBuildOrder { get; set; } = new();

	/// <summary>
	/// Converts this configuration into a JSON dictionary string for injection into the AI bot provider.
	/// </summary>
	/// <returns>A JSON string representation of the configuration parameters.</returns>
	public string ToJson()
	{
		var dict = new Dictionary<string, string>
		{
			{ "FountainPositionJson", JsonSerializer.Serialize(FountainPosition) },
			{ "FountainHealRadius", FountainHealRadius.ToString(System.Globalization.CultureInfo.InvariantCulture) },
			{ "RetreatHealthPercent", RetreatHealthPercent.ToString(System.Globalization.CultureInfo.InvariantCulture) },
			{ "RetreatManaPercent", RetreatManaPercent.ToString(System.Globalization.CultureInfo.InvariantCulture) },
			{ "RunePositionsJson", JsonSerializer.Serialize(RunePositions) },
			{ "ShopBuildOrderJson", JsonSerializer.Serialize(ShopBuildOrder) }
		};
		return JsonSerializer.Serialize(dict);
	}
}