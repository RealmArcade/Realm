namespace Realm.Ecs.Definitions;

/// <summary>
///     Represents the camera boundary properties of a map.
/// </summary>
public class MapProperties
{
	public string? Name { get; set; }
	public string? MapName { get; set; }
	public string? MapDescription { get; set; }
	public string? Author { get; set; }
	public string? Description { get; set; }
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
	public string[]? HowToPlayInstructions { get; set; }
	public string? HowToPlayObjective { get; set; }
	public string? Version { get; set; }
	public object[]? Changelog { get; set; }
	public object[]? PlayerSlots { get; set; }
	public object[]? Teams { get; set; }
	public string[]? Tags { get; set; }
	public object? Assets { get; set; }
	public string? MapType { get; set; }
}