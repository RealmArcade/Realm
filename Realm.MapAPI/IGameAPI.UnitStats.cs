// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Sets the current health points of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new health value.</param>
	void SetUnitHealth(UnitId unit, float value);

	/// <summary>
	/// Sets the maximum health points capacity of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new maximum health capacity.</param>
	void SetUnitMaxHealth(UnitId unit, float value);

	/// <summary>
	/// Sets the current mana points of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new mana value.</param>
	void SetUnitMana(UnitId unit, float value);

	/// <summary>
	/// Sets the maximum mana capacity of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new maximum mana capacity.</param>
	void SetUnitMaxMana(UnitId unit, float value);

	/// <summary>
	/// Sets the mana regeneration rate of the specified unit per second.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new mana regeneration rate.</param>
	void SetUnitManaRegen(UnitId unit, float value);

	/// <summary>
	/// Sets the base attack damage of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new attack damage.</param>
	void SetUnitDamage(UnitId unit, float value);

	/// <summary>
	/// Sets the attack range of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new attack range.</param>
	void SetUnitRange(UnitId unit, float value);

	/// <summary>
	/// Sets the armor defense rating of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new armor rating.</param>
	void SetUnitArmor(UnitId unit, float value);

	/// <summary>
	/// Sets the movement speed of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new movement speed.</param>
	void SetUnitSpeed(UnitId unit, float value);

	/// <summary>
	/// Sets the attack speed multiplier of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new attack speed multiplier.</param>
	void SetUnitAttackSpeed(UnitId unit, float value);

	/// <summary>
	/// Retrieves the display name of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <returns>The unit display name.</returns>
	string GetUnitInstanceName(UnitId unit);

	/// <summary>
	/// Sets the display name of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new display name.</param>
	void SetUnitName(UnitId unit, string value);

	/// <summary>
	/// Retrieves the canonical archetype template identifier of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <returns>The archetype template identifier string.</returns>
	string GetUnitTemplateId(UnitId unit);

	/// <summary>
	/// Sets the accumulated experience points of the specified hero unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new experience points value.</param>
	void SetUnitExperience(UnitId unit, float value);

	/// <summary>
	/// Sets the visual scaling multiplier of the specified unit model.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new visual scale factor.</param>
	void SetUnitScale(UnitId unit, float value);

	/// <summary>
	/// Sets whether the specified unit is invulnerable to all damage.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">True for invulnerability; otherwise, false.</param>
	void SetUnitInvulnerable(UnitId unit, bool value);

	/// <summary>
	/// Suspends or resumes animation playback for a unit.
	/// </summary>
	/// <remarks>
	/// An animations-frozen unit holds a single pose because locomotion, attack, idle and death
	/// animation playback is suspended together with any procedural sway. It can still move, attack
	/// and cast, so combine this with <see cref="SetUnitCommandsFrozen"/> to build a statue. The state
	/// lives on the unit entity, so it survives tile level changes, element swaps and re-drafting until
	/// the entity is destroyed or unfrozen.
	/// </remarks>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">True to hold the current pose; false to resume animation playback.</param>
	void SetUnitAnimationsFrozen(UnitId unit, bool value);

	/// <summary>
	/// Makes a unit ignore movement, attack and hold orders, or releases it again.
	/// </summary>
	/// <remarks>
	/// A commands-frozen unit keeps its current position, never acquires targets and ignores move, stop,
	/// hold and attack orders, and clients hide those command buttons for it. Ability casting and ability
	/// buttons stay available, so map-authored abilities such as tile rerolls keep working. Existing
	/// orders and targets are cleared when the unit freezes, and unfreezing restores normal behaviour
	/// immediately. The state lives on the unit entity, so it survives tile level changes, element swaps
	/// and re-drafting until the entity is destroyed or unfrozen.
	/// </remarks>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">True to ignore movement and attack orders; false to accept them again.</param>
	void SetUnitCommandsFrozen(UnitId unit, bool value);

	/// <summary>
	/// Pins a unit in place, or releases it again.
	/// </summary>
	/// <remarks>
	/// This sets both <see cref="SetUnitAnimationsFrozen"/> and <see cref="SetUnitCommandsFrozen"/>.
	/// A frozen unit never moves, never rotates in response to orders, never acquires targets, and holds
	/// a single pose. Existing orders and targets are cleared when the unit freezes, and unfreezing
	/// restores normal behaviour immediately. Freezing is idempotent, cheap, and composes with
	/// <see cref="SetUnitInvulnerable"/>, facing and the health-bar visibility flags, so a map can build
	/// statues out of real units. The state lives on the unit entity, so it survives tile level changes,
	/// element swaps, unit swaps and re-drafting until the entity is destroyed or unfrozen.
	/// </remarks>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">True to freeze the unit in place; false to let it act normally again.</param>
	void SetUnitFrozen(UnitId unit, bool value);

	/// <summary>
	/// Shows or hides the overhead health and mana bars for a single unit.
	/// </summary>
	/// <remarks>
	/// Hidden bars stay hidden in every health-bar display mode, including the ones that show bars for
	/// every living unit, so map-authored statues and scenery units never advertise hit points. The flag
	/// lives on the unit entity and is cleared when that entity is destroyed.
	/// </remarks>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">True to never draw bars for the unit; false to draw them according to the player's settings.</param>
	void SetUnitHideHealthAndManaBars(UnitId unit, bool value);

	/// <summary>
	/// Draws a number in a unit's overhead plate, or changes the number and colour it is drawn with.
	/// </summary>
	/// <remarks>
	/// The badge is how a map states a rank on the board itself rather than in a panel: it is drawn in the same plate as
	/// the health and mana bars, sized like a level, and tinted with the colour given here, so a map can colour it by the
	/// player, the element or the unit's role. It draws even for a unit that
	/// <see cref="SetUnitHideHealthAndManaBars"/> hides the bars of, which is what makes a badge for a board square
	/// possible, and it draws only when the map shows badges at all through <see cref="SetUnitBarVisibility"/>.
	/// The badge lives on the unit entity, so it follows the unit through element swaps, level changes and swaps of two
	/// units' places, and it is gone when the entity is destroyed. A value of zero or below hides it.
	/// </remarks>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The number to draw above a zero-hiding threshold, from 1 to 999.</param>
	/// <param name="color">The tint of the badge, in the same 0-1 channel range as every other colour a map gives.</param>
	void SetUnitOverheadBadge(UnitId unit, int value, System.Numerics.Vector3 color);

	/// <summary>
	/// Stores custom string metadata associated with the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="key">The custom data key.</param>
	/// <param name="value">The custom data value.</param>
	void SetUnitCustomData(UnitId unit, string key, string value);

	/// <summary>
	/// Retrieves custom string metadata associated with the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="key">The custom data key.</param>
	/// <returns>The stored string value, or null if not found.</returns>
	string? GetUnitCustomData(UnitId unit, string key);

	/// <summary>
	/// Removes custom string metadata associated with the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="key">The custom data key.</param>
	/// <returns>True if found and removed; otherwise, false.</returns>
	bool RemoveUnitCustomData(UnitId unit, string key);

	/// <summary>
	/// Returns true if custom string metadata exists for the given key on the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="key">The custom data key.</param>
	/// <returns>True if data exists; otherwise, false.</returns>
	bool UnitHasCustomData(UnitId unit, string key);
}
