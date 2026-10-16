namespace Realm.MapAPI;

/// <summary>
/// Identifies built-in HUD (Heads-Up Display) interface elements whose visibility can be toggled per player.
/// </summary>
public enum HudElement
{
	/// <summary>
	/// The terrain minimap display and camera view rectangle in the bottom-left corner.
	/// </summary>
	Minimap,

	/// <summary>
	/// The top resource panel displaying gold, wood, stone, food/population, and game time.
	/// </summary>
	ResourceBar,

	/// <summary>
	/// The command card panel displaying unit actions, abilities, attack/move commands, and build menus.
	/// </summary>
	CommandCard,

	/// <summary>
	/// The unit portrait and stats display showing the active selection's 3D portrait, name, and attributes.
	/// </summary>
	UnitPortrait,

	/// <summary>
	/// The multiplayer in-game chat box and message history overlay.
	/// </summary>
	Chat,

	/// <summary>
	/// The custom map leaderboard panel on the right side of the screen.
	/// </summary>
	Leaderboard,

	/// <summary>
	/// The full-screen summary table panel displaying multi-column statistics and player standings.
	/// </summary>
	SummaryTable,

	/// <summary>
	/// The unit inventory item slots panel.
	/// </summary>
	Inventory,

	/// <summary>
	/// The countdown timer panel displayed at the top of the screen.
	/// </summary>
	CountdownTimer
}
