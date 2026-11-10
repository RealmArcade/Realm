// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Retrieves the preferred language code of the specified player (e.g. "en", "es").
	/// </summary>
	/// <param name="playerIndex">Zero-based player index.</param>
	/// <returns>ISO language code string.</returns>
	string GetPlayerLanguage(int playerIndex);

	/// <summary>
	/// Translates a localization key using the player's preferred language with English fallback.
	/// </summary>
	/// <param name="key">Localization translation key.</param>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>Translated text string.</returns>
	string Translate(string key, int playerIndex);

	/// <summary>
	/// Adds an ability to the specified unit type definition.
	/// </summary>
	/// <param name="unitTypeId">The type identifier of the unit/structure.</param>
	/// <param name="abilityId">The identifier of the ability to add.</param>
	void AddUnitTypeAbility(string unitTypeId, string abilityId);

	/// <summary>
	/// Retrieves the ordered list of ability identifiers configured for the specified unit or building type.
	/// </summary>
	/// <param name="unitTypeId">The unit or building type identifier.</param>
	/// <returns>A read-only list of ability identifiers configured for the unit type (never null).</returns>
	IReadOnlyList<string> GetUnitTypeAbilities(string unitTypeId);

	/// <summary>
	/// Retrieves the authored display name for the specified unit or building type.
	/// </summary>
	/// <param name="unitTypeId">The unit or building type identifier.</param>
	/// <returns>The localized or authored display name, or a fallback string if unauthored.</returns>
	string GetUnitName(string unitTypeId);

	/// <summary>
	/// Retrieves the authored display name for the specified ability type.
	/// </summary>
	/// <param name="abilityId">The ability type identifier.</param>
	/// <returns>The localized or authored display name, or a fallback string if unauthored.</returns>
	string GetAbilityName(string abilityId);

	/// <summary>
	/// Resolves the text that a string identifier stands for, using the host's text table when the sandbox
	/// has never registered that identifier itself.
	/// </summary>
	/// <param name="stringId">The content-hash identifier to resolve, as found in shared component memory.</param>
	/// <returns>The text, or an empty string when no table entry exists for the identifier.</returns>
	string GetStringText(int stringId);

	/// <summary>
	/// Enables or disables automated combat AI for a specific unit instance.
	/// </summary>
	/// <param name="unit">The unit to modify.</param>
	/// <param name="enabled">True to enable autonomous combat AI; false to disable.</param>
	void EnableUnitAutoCombat(UnitId unit, bool enabled);

	/// <summary>
	/// Writes string content to a file in the whitelisted, map-specific subfolder in AppData saved_data directory.
	/// The target location is sandboxed to user://saved_data/{mapName}/{fileName}.
	/// </summary>
	/// <param name="fileName">The name of the file to write to. Must be a safe, relative file name without directory traversal characters.</param>
	/// <param name="content">The string content to write to the file.</param>
	void WriteSavedData(string fileName, string content);

	/// <summary>
	/// Reads string content from a file in the whitelisted, map-specific subfolder in AppData saved_data directory.
	/// The target location is sandboxed to user://saved_data/{sourceMapName}/{fileName}.
	/// </summary>
	/// <param name="fileName">The name of the file to read from. Must be a safe, relative file name without directory traversal characters.</param>
	/// <param name="sourceMapName">The name of the source map to read saved data from, or empty string to use the active map.</param>
	/// <returns>The string content read from the file, or an empty string if the file does not exist or has invalid path characters.</returns>
	string ReadSavedData(string fileName, string sourceMapName = "");

	/// <summary>
	/// Triggered when a unit buys or receives an item from a shop or altar.
	/// </summary>
	event Action<UnitId, string>? OnItemSold;

	/// <summary>
	/// Triggered when a structure finishes construction.
	/// </summary>
	event Action<UnitId>? OnConstructionFinished;

	/// <summary>
	/// Triggered when a unit is issued an order.
	/// </summary>
	event Action<UnitId, string, Vector3>? OnUnitOrdered;

	/// <summary>
	/// Gets the research level of a technology upgrade for the specified player slot.
	/// </summary>
	int GetPlayerTechLevel(int playerIndex, string techId);

	/// <summary>
	/// Sets the research level of a technology upgrade for the specified player slot.
	/// </summary>
	void SetPlayerTechLevel(int playerIndex, string techId, int level);

	/// <summary>
	/// Adds to the research level of a technology upgrade for the specified player slot.
	/// </summary>
	void AddPlayerTechLevel(int playerIndex, string techId, int delta = 1);

	/// <summary>
	/// Registers an ability with display metadata and execution behavior.
	/// </summary>
	/// <param name="abilityId">Unique identifier of the ability.</param>
	/// <param name="displayName">User-facing name of the ability.</param>
	/// <param name="tooltip">Detailed description displayed on hover.</param>
	/// <param name="iconPath">Resource path to the ability icon texture.</param>
	/// <param name="isInstant">True if the ability executes immediately without ground targeting.</param>
	void RegisterAbility(string abilityId, string displayName, string tooltip, string iconPath = "", bool isInstant = false);

	/// <summary>
	/// Registers an ability with display metadata and execution behavior for a specific player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index whose command card should register the ability.</param>
	/// <param name="abilityId">Unique identifier of the ability.</param>
	/// <param name="displayName">User-facing name of the ability.</param>
	/// <param name="tooltip">Detailed description displayed on hover.</param>
	/// <param name="iconPath">Resource path to the ability icon texture.</param>
	/// <param name="isInstant">True if the ability executes immediately without ground targeting.</param>
	void RegisterPlayerAbility(int playerIndex, string abilityId, string displayName, string tooltip, string iconPath = "", bool isInstant = false);

	/// <summary>
	/// Sets whether an ability executes immediately without requiring ground targeting.
	/// </summary>
	/// <param name="abilityId">Unique identifier of the ability.</param>
	/// <param name="isInstant">True if instant cast, false for ground targeting.</param>
	void SetAbilityInstant(string abilityId, bool isInstant);

	/// <summary>
	/// Sets the visual icon texture path for an ability.
	/// </summary>
	/// <param name="abilityId">Unique identifier of the ability.</param>
	/// <param name="iconPath">Resource path to the icon texture.</param>
	void SetAbilityIcon(string abilityId, string iconPath);

	/// <summary>
	/// Sets the display tooltip for an ability.
	/// </summary>
	void SetAbilityTooltip(string abilityId, string tooltip);

	/// <summary>
	/// Sets the display tooltip for an item.
	/// </summary>
	void SetItemTooltip(string itemId, string tooltip);

	/// <summary>
	/// Publishes one number that an ability's tooltip reads as <c>{{Ability.Key}}</c>. Use this for
	/// values the engine cannot know, such as a scripted elixir's amount and cap, and publish them
	/// from the same fields the effect applies so the card cannot claim a different number.
	/// Keys the engine already owns (ManaCost, Cooldown, Range, Radius, Damage, Healing) are not
	/// overridden.
	/// </summary>
	/// <param name="abilityId">The ability whose card should read this value.</param>
	/// <param name="key">Property name, matched without regard to case.</param>
	/// <param name="value">The number to show.</param>
	void SetAbilityTooltipValue(string abilityId, string key, float value);

	/// <summary>
	/// Publishes one text value that an ability's tooltip reads as <c>{{Ability.Key}}</c>.
	/// </summary>
	/// <param name="abilityId">The ability whose card should read this value.</param>
	/// <param name="key">Property name, matched without regard to case.</param>
	/// <param name="value">The text to show.</param>
	void SetAbilityTooltipText(string abilityId, string key, string value);

	/// <summary>
	/// Publishes one number that a unit's tooltip reads as <c>{{Source.Key}}</c>, for state the
	/// engine does not model itself — a draft slot's element, a carried item's stack count.
	/// </summary>
	/// <param name="unit">The unit the value describes.</param>
	/// <param name="key">Property name, matched without regard to case.</param>
	/// <param name="value">The number to show.</param>
	void SetUnitTooltipValue(UnitId unit, string key, float value);

	/// <summary>
	/// Publishes one text value that a unit's tooltip reads as <c>{{Source.Key}}</c>.
	/// </summary>
	/// <param name="unit">The unit the value describes.</param>
	/// <param name="key">Property name, matched without regard to case.</param>
	/// <param name="value">The text to show.</param>
	void SetUnitTooltipText(UnitId unit, string key, string value);

	/// <summary>
	/// Publishes one number that an item's tooltip reads as <c>{{Item.Key}}</c>.
	/// </summary>
	/// <param name="itemId">The item whose card should read this value.</param>
	/// <param name="key">Property name, matched without regard to case.</param>
	/// <param name="value">The number to show.</param>
	void SetItemTooltipValue(string itemId, string key, float value);

	/// <summary>
	/// Publishes one number that an upgrade's tooltip reads as <c>{{Upgrade.Key}}</c>.
	/// </summary>
	/// <param name="upgradeId">The upgrade whose card should read this value.</param>
	/// <param name="key">Property name, matched without regard to case.</param>
	/// <param name="value">The number to show.</param>
	void SetUpgradeTooltipValue(string upgradeId, string key, float value);

	/// <summary>
	/// Configures the grid position (X, Y) of an ability in the command card interface.
	/// </summary>
	void SetAbilityGridPosition(string abilityId, int x, int y);

	/// <summary>
	/// Configures the hotkey for an ability on the command card. The hotkey is one key name with
	/// optional modifiers joined by '+', for example "W", "F1" or "Shift+Q". Names are the Godot key
	/// names ("Q", "Escape", "F1") and the modifiers "Shift", "Ctrl", "Alt" and "Meta". Invalid
	/// specifications are rejected and reported instead of silently never matching.
	/// </summary>
	/// <param name="abilityId">Unique identifier of the ability.</param>
	/// <param name="hotkey">The key with optional modifiers that triggers the ability.</param>
	void SetAbilityHotkey(string abilityId, string hotkey);

	/// <summary>
	/// Sets whether an ability on a unit is disabled or hidden in the UI.
	/// </summary>
	void SetAbilityState(UnitId unit, string abilityId, bool disabled, bool hidden);

	/// <summary>
	/// Sets the mana cost of a unit's ability.
	/// </summary>
	void SetAbilityManaCost(UnitId unit, string abilityId, float manaCost);
}
