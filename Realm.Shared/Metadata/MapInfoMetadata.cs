using System.Text.Json.Serialization;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("What the map says about itself to a player: its name, author, size, lighting, loading screen, tags, slots and teams.")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class MapInfoMetadata
{
	public const int DefaultPlayablePlayerSlots = 24;
	public const int MaxPlayablePlayerSlots = 24;
	public const int MinPlayablePlayerSlots = 1;

	[SchemaDescription("The title the map is listed under in the lobby.")]
	public string? MapName { get; set; }

	[SchemaDescription("The short introduction shown beside the map in the lobby.")]
	[SchemaControl(RealmSchemaControl.Textarea)]
	public string? MapDescription { get; set; }

	[SchemaDescription("The name the map credits as its author.")]
	public string? Author { get; set; }

	[SchemaDescription("How many players the map is designed for, written as text such as 2-4.")]
	public string? SuggestedPlayers { get; set; }

	[SchemaDescription("Path to the image drawn in the lobby's minimap panel for this map.")]
	[SchemaAsset(new[] { "png", "rtex" }, new[] { "." })]
	public string? MinimapImage { get; set; }

	[SchemaDescription("The kind of fog of war the match uses, such as unexplored shroud or visible-only fog.")]
	public string? ShroudType { get; set; }

	[SchemaDescription("The weather the match starts with, which the weather system animates from.")]
	public string? WeatherType { get; set; }

	[SchemaDescription("The height the flat parts of the map sit at before terrain is sculpted.")]
	[SchemaUnit("m")]
	public float? TerrainBaseHeight { get; set; }

	[SchemaDescription("How dark the shadows the sun casts are.")]
	[SchemaRange(0, 1)]
	public float? ShadowIntensity { get; set; }

	[SchemaDescription("The width of the map in terrain cells.")]
	[SchemaDerived("terrainWidth")]
	public int? MapWidth { get; set; }

	[SchemaDescription("The depth of the map in terrain cells.")]
	[SchemaDerived("terrainDepth")]
	public int? MapHeight { get; set; }

	[SchemaDescription("The width of the area a match is actually played in, in terrain cells.")]
	[SchemaDerived("terrainWidth")]
	public int? PlayableWidth { get; set; }

	[SchemaDescription("The depth of the area a match is actually played in, in terrain cells.")]
	[SchemaDerived("terrainDepth")]
	public int? PlayableHeight { get; set; }

	[SchemaDescription("The west edge of the area the camera may pan to.")]
	[SchemaDerived("cameraBoundsLeft")]
	public float? CameraBoundsLeft { get; set; }

	[SchemaDescription("The east edge of the area the camera may pan to.")]
	[SchemaDerived("cameraBoundsRight")]
	public float? CameraBoundsRight { get; set; }

	[SchemaDescription("The north edge of the area the camera may pan to.")]
	[SchemaDerived("cameraBoundsTop")]
	public float? CameraBoundsTop { get; set; }

	[SchemaDescription("The south edge of the area the camera may pan to.")]
	[SchemaDerived("cameraBoundsBottom")]
	public float? CameraBoundsBottom { get; set; }

	[SchemaDescription("Path to the picture the loading screen shows while this map loads.")]
	[SchemaAsset(new[] { "png", "rtex" }, new[] { "." })]
	public string? LoadingImage { get; set; }

	[SchemaDescription("Path to the music played while this map loads.")]
	[SchemaAsset(new[] { "raud" }, new[] { "Assets/audio/music" })]
	public string? LoadingMusic { get; set; }

	[SchemaDescription("The heading the loading screen shows over the map's name.")]
	public string? LoadingTitle { get; set; }

	[SchemaDescription("The line the loading screen shows under the map's name.")]
	public string? LoadingSubtitle { get; set; }

	[SchemaDescription("The body text the loading screen shows while the map loads, which a map uses to set the scene.")]
	[SchemaControl(RealmSchemaControl.Textarea)]
	public string? LoadingBodyText { get; set; }

	[SchemaDescription("What the map asks the player to do, shown in the how-to-play panel.")]
	[SchemaControl(RealmSchemaControl.Textarea)]
	public string? HowToPlayObjective { get; set; }

	[SchemaDescription("The version the map author gives this map, which the lobby shows beside the name.")]
	[SchemaSensitive]
	public string? Version { get; set; }

	[SchemaDescription("The changes the map has been through, newest first, shown in the map's details.")]
	public List<MapChangelogEntry>? Changelog { get; set; }

	[SchemaDescription("The slots the map offers, each with its own colour, faction and controller.")]
	public List<MapPlayerSlotConfig>? PlayerSlots { get; set; }

	[SchemaDescription("The teams the slots are grouped into for a match on this map.")]
	public List<MapTeamConfig>? Teams { get; set; }

	[SchemaDescription("The labels the lobby filters maps by, such as melee, co-op or tower defence.")]
	[SchemaControl(RealmSchemaControl.Tags)]
	public List<string>? Tags { get; set; }

	[SchemaDescription("The kind of map this is, such as melee, campaign or asset pack.")]
	public string? MapType { get; set; }

	[SchemaDescription("How many computer players a test run fills the empty slots with.")]
	[SchemaControl(RealmSchemaControl.Slider)]
	[SchemaRange(0, 24)]
	public int? TestAiPlayerCount { get; set; }

	[SchemaDescription("Whether a test run fills the empty slots with computer players in a random order instead of the map's own.")]
	public bool? TestRandomizeSlots { get; set; }

	[SchemaDescription("The seed a test run uses for anything random, so the same run can be repeated.")]
	public int? TestRandomSeed { get; set; }

	[SchemaDescription("How many players a match on this map may seat.")]
	[SchemaRange(1, 24)]
	public int? PlayablePlayerSlots { get; set; }

	/// <summary>Whether units draw an overhead health bar. On unless the map says otherwise.</summary>
	[SchemaDescription("Whether units draw a health bar above them.")]
	public bool? ShowUnitHealthBar { get; set; }

	/// <summary>Whether units draw an overhead mana bar beneath the health bar. Off unless the map says otherwise.</summary>
	[SchemaDescription("Whether units draw a mana bar under their health bar.")]
	public bool? ShowUnitManaBar { get; set; }

	/// <summary>Whether units the script gives an overhead badge to draw it to the left of their overhead bars. Off unless the map says otherwise.</summary>
	[SchemaDescription("Whether units the script gives an overhead badge to draw it to the left of their overhead bars.")]
	public bool? ShowUnitLevel { get; set; }

	/// <summary>
	///     The number of inventory slots every unit of the match may fill. Relics and other equipment take slots the way a
	///     gathered piece does, so the number bounds what a unit can carry home. Defaults to six.
	/// </summary>
	[SchemaDescription("How many inventory slots every unit may fill. Relics and other equipment take slots the way a gathered piece does, so this bounds what a unit can carry home. Absent means six.")]
	public int? InventorySlots { get; set; }

	public int GetPlayablePlayerSlots()
	{
		int declaredSlots = PlayablePlayerSlots ?? DefaultPlayablePlayerSlots;
		return Math.Clamp(declaredSlots, MinPlayablePlayerSlots, MaxPlayablePlayerSlots);
	}

}
