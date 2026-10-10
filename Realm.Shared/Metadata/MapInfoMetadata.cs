using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Shared.Metadata;

public class MapInfoMetadata
{
	public string? MapName { get; set; }
	public string? MapDescription { get; set; }
	public string? Author { get; set; }
	public string? SuggestedPlayers { get; set; }
	public string? MinimapImage { get; set; }
	public string? ShroudType { get; set; }
	public string? WeatherType { get; set; }
	public float? TerrainBaseHeight { get; set; }
	public float? ShadowIntensity { get; set; }
	public int? MapWidth { get; set; }
	public int? MapHeight { get; set; }
	public int? PlayableWidth { get; set; }
	public int? PlayableHeight { get; set; }
	public float? CameraBoundsLeft { get; set; }
	public float? CameraBoundsRight { get; set; }
	public float? CameraBoundsTop { get; set; }
	public float? CameraBoundsBottom { get; set; }
	public string? LoadingImage { get; set; }
	public string? LoadingMusic { get; set; }
	public string? LoadingTitle { get; set; }
	public string? LoadingSubtitle { get; set; }
	public string? LoadingBodyText { get; set; }
	public List<string>? HowToPlayInstructions { get; set; }
	public string? HowToPlayObjective { get; set; }
	public string? Version { get; set; }
	public List<MapChangelogEntry>? Changelog { get; set; }
	public List<MapPlayerSlotConfig>? PlayerSlots { get; set; }
	public List<MapTeamConfig>? Teams { get; set; }
	public List<string>? Tags { get; set; }
	public string? MapType { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}