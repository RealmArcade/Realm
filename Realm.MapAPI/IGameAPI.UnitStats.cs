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
	/// Makes a player-owned unit refuse its player's orders while the map script keeps commanding it, or releases it again.
	/// </summary>
	/// <remarks>
	/// A locked unit keeps its owner, its abilities and its position, but its player cannot move, attack, hold, patrol or
	/// gather with it, and the client draws every ability card it owns as disabled so the player cannot cast from it. The
	/// map is unaffected: orders and casts it issues itself still apply, which is what lets a ruleset run an autonomous army
	/// that the player watches and inspects but cannot steer. This differs from <see cref="SetUnitCommandsFrozen"/>, which
	/// stops the unit acting at all, and from hiding its abilities, which leaves the player able to move it.
	/// </remarks>
	/// <param name="unit">The unit to lock to its map.</param>
	/// <param name="value">True to refuse player orders; false to accept them again.</param>
	void SetUnitPlayerOrdersLocked(UnitId unit, bool value);

	/// <summary>
	/// States how long one gathering swing lasts, which is the cadence a worker works a node at.
	/// </summary>
	/// <remarks>
	/// A gather order sets the worker going; this states how long each of its swings takes. What a swing takes from the
	/// node is decided by the node's yield and the worker's own attack, so a map overrides the rhythm rather than the
	/// amount, and leaving the seconds at zero falls back to the unit's attack interval. The call does nothing for a unit
	/// that is not gathering.
	/// </remarks>
	/// <param name="unit">The gathering unit.</param>
	/// <param name="swingSeconds">The seconds one swing lasts, or zero to swing at the unit's own attack interval.</param>
	void SetUnitGatherInterval(UnitId unit, float swingSeconds);

	/// <summary>
	/// States the share of each delivered swing the map keeps, so a ruleset can tax a worker's income.
	/// </summary>
	/// <remarks>
	/// The tax is taken from the delivery, never from the swing: the node still loses exactly the amount the building
/// advertises, and only what reaches the player's purse is reduced. The fraction of a resource a taxed delivery leaves
/// behind is carried into that worker's next delivery rather than rounded away, so a run pays the total its advertised
/// rate states. Zero pays every delivery in full, which is what a worker nobody taxed pays; values are clamped between
/// zero and 0.99 so a delivery always pays something unless the map pays nothing. The call does nothing for a unit that
/// is not gathering. Taxing each worker at its own building's rank among the player's buildings is what makes a
/// progressive, marginal income tax: earlier buildings keep the share they were given, and a new building pays less
/// than the one before it without changing what the earlier ones pay.
	/// </remarks>
	/// <param name="unit">The gathering unit whose deliveries are taxed.</param>
	/// <param name="taxRate">The fraction kept from each delivered swing, from zero to 0.99.</param>
	void SetUnitGatherTaxRate(UnitId unit, float taxRate);

	/// <summary>
	/// States how fast a unit mends a building, as a fraction of the building's maximum health per second.
	/// </summary>
	/// <remarks>
	/// A rate above zero is what lets a unit accept a repair order at all, so a ruleset decides which units mend and how
	/// fast by stating a rate per unit rather than the engine assuming a builder. Because the rate is a share of the
	/// building's maximum health, a hero spends the same time raising any building from zero to whole, and a rate that a
	/// buff or a relic raises mends every building faster. A rate of zero, or a call with no rate, takes the ability
	/// away and ends any mending the unit was doing. The rate belongs to the repairer, so two units can mend the same
	/// building at different speeds.
	/// </remarks>
	/// <param name="unit">The unit that mends buildings.</param>
	/// <param name="healthPercentPerSecond">The fraction of the building's maximum health restored each second.</param>
	void SetUnitRepairRate(UnitId unit, float healthPercentPerSecond);

	/// <summary>
	/// Answers how fast a unit mends buildings, as a fraction of the building's maximum health per second.
	/// </summary>
	/// <param name="unit">The unit whose repair rate is read.</param>
	/// <returns>The fraction restored each second, or zero when the unit cannot repair.</returns>
	float GetUnitRepairRate(UnitId unit);

	/// <summary>
	/// Answers a unit's current spell power, the multiplier its ability damage carries after its attributes and buffs
	/// have been resolved.
	/// </summary>
	/// <remarks>
	/// Spell power belongs to the caster rather than to one ability, so a rule that scales a burn, a heal or a shield
	/// from it reads the same number every ability reads. It is a multiplier added to one, which is how the engine applies
	/// it to authored ability damage; a unit with no spell power answers zero.
	/// </remarks>
	/// <param name="unit">The unit whose spell power is read.</param>
	/// <returns>The spell power multiplier, or zero when the unit has none.</returns>
	float GetUnitSpellPower(UnitId unit);

	/// <summary>
	/// States whether a building falls dormant when its health runs out instead of being destroyed.
	/// </summary>
	/// <remarks>
	/// A repairable building at zero health stays in the world: it is invulnerable, it earns nothing, it recharges
	/// nothing, it produces nothing and it attacks nothing, while the charges and the production queue it already holds
	/// are kept. A repairing unit raises it back toward full health, and the building only starts working again once it
	/// is whole. A building the map never marked repairable is destroyed at zero health, which is what every building did
	/// before a map asked for this, so a ruleset that has no repair has nothing to configure.
	/// </remarks>
	/// <param name="unit">The building.</param>
	/// <param name="repairable">True to let the building fall dormant instead of dying at zero health.</param>
	void SetUnitRepairable(UnitId unit, bool repairable);

	/// <summary>
	/// States whether a unit is a resource worker for the worker collision rule.
	/// </summary>
	/// <remarks>
	/// Workers body-block the workers of their own player and pass through the workers of every other player, so a
	/// gathering crew never jams on itself while rival crews walk through one another. Only worker against worker is
	/// decided here: a worker still collides with soldiers, heroes and buildings like any other unit, and a match that
	/// never marks a worker keeps every unit blocking every other unit. The flag belongs to the unit, so two workers of
	/// the same player body-block while two workers of different players ghost, whatever their teams say.
	/// </remarks>
	/// <param name="unit">The unit to mark or unmark.</param>
	/// <param name="worker">True to make the unit a worker; false to give it ordinary unit collision again.</param>
	void SetUnitWorker(UnitId unit, bool worker);

	/// <summary>
	///     Assigns the resource a worker gathers, which is how the building that raised it states what its crew works.
	/// </summary>
	/// <remarks>
	///     The assignment is the script's own instruction to the worker, not an order: the map reads it back with
	///     <see cref="GetUnitWorkerAssignment"/> and issues the gather order to the matching node, so one worker template
	///     can be sent to gold by one building and to wood by another. A resource name the map never uses is kept as stated,
	///     which lets a package add its own resource without an engine change. An empty name clears the assignment.
	/// </remarks>
	/// <param name="unit">The worker to assign.</param>
	/// <param name="resourceType">The resource type to gather, such as gold or wood, or an empty string to clear it.</param>
	void SetUnitWorkerAssignment(UnitId unit, string resourceType);

	/// <summary>
	///     Answers the resource a worker was assigned to gather.
	/// </summary>
	/// <param name="unit">The worker whose assignment is read.</param>
	/// <returns>The assigned resource type, or an empty string when the unit has no assignment.</returns>
	string GetUnitWorkerAssignment(UnitId unit);

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
	/// units' places, and it is gone when the entity is destroyed. A value of zero draws zero, because a count that has
	/// run out is still something the board states, and a negative value hides the badge.
	/// </remarks>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The number to draw, from 0 to 999, or a negative value to draw no badge at all.</param>
	/// <param name="color">The tint of the badge, in the same 0-1 channel range as every other colour a map gives.</param>
	void SetUnitOverheadBadge(UnitId unit, int value, System.Numerics.Vector3 color);

	/// <summary>
	/// States the multiplier the damage one player deals carries against another, which is how a rule makes a side of a
	/// contest temporarily less effective without changing the units themselves.
	/// </summary>
	/// <remarks>
	/// The multiplier is applied to every blow the attacker's units land on the defender's units and its buildings, after
	/// the victim's resistances have been resolved, so it scales what a fight costs rather than what armor it meets. A pair
	/// a rule never states stays at full effectiveness, so a ruleset that ignores this answer fights exactly as the engine
	/// does.
	/// </remarks>
	/// <param name="attackingPlayerIndex">Zero-based player slot index whose damage is scaled.</param>
	/// <param name="defendingPlayerIndex">Zero-based player slot index the scaled damage is dealt to.</param>
	/// <param name="multiplier">The damage multiplier, where one is full effectiveness and zero deals nothing.</param>
	void SetPlayerDamageEffectiveness(int attackingPlayerIndex, int defendingPlayerIndex, float multiplier);

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
