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
	/// States how much one gathering swing takes from a node and how long that swing lasts.
	/// </summary>
	/// <remarks>
	/// A gather order sets the worker going; this states what a swing is worth. The amount is taken from the node and
	/// carried back to a dropoff one swing at a time, so a player is paid exactly what its nodes lost and the trip's
	/// length is what decides the real rate: a near node is worked more often than a far one at the same swing value.
	/// Leaving the amount at zero makes the worker take whatever the node advertises as its harvest rate, and leaving the
	/// seconds at zero makes one swing take a second. The call does nothing for a unit that is not gathering.
	/// </remarks>
	/// <param name="unit">The gathering unit.</param>
	/// <param name="swingAmount">The amount one swing takes from the node, or zero to use the node's own harvest rate.</param>
	/// <param name="swingSeconds">The seconds one swing lasts, or zero for one second.</param>
	void SetUnitGatherSwing(UnitId unit, float swingAmount, float swingSeconds);

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
	/// States the charges a unit keeps and how long it takes to regain one, which is how a map makes a building hold a
	/// limited number of what it fields.
	/// </summary>
	/// <remarks>
	/// The engine owns the count: it regains a charge every <paramref name="rechargeSeconds" /> on its own, so a map
	/// states the parameters once and never advances the timer itself. The count is a component of the unit, read the
	/// same way by the inspector, the overhead plate and the map, and replicated with the unit, so every screen states
	/// the same number without the map publishing it.
	/// <para>
	/// Stating the parameters again keeps the count the unit holds, clamped to the new maximum, and keeps the recharge it
	/// has already served, so a rule that raises the cap mid-match does not hand out charges and a rule that lowers the
	/// recharge time is felt on the charge being regained. Calling it on a unit that carries no charges gives it a count
	/// of zero, because the maximum stated here is what the charge is worth.
	/// </para>
	/// <para>
	/// A maximum of zero removes the charges from the unit, which is how a map says the rule no longer applies to it.
	/// </para>
	/// </remarks>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="maximumCharges">The greatest number of charges the unit may hold, or zero to hold none at all.</param>
	/// <param name="rechargeSeconds">Seconds the unit takes to regain one charge, or zero to regain them at once.</param>
	void SetUnitCharges(UnitId unit, int maximumCharges, float rechargeSeconds);

	/// <summary>
	/// Sets how many charges a unit holds right now, which is how a map spends or grants one.
	/// </summary>
	/// <remarks>
	/// The count is clamped to what the unit states through <see cref="SetUnitCharges" />, and a unit that carries no
	/// charges ignores the call because it has no count to set. Spending the last charge resets the recharge, because the
	/// time served toward a charge the unit already held is not time served toward the next one.
	/// </remarks>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="charges">The number of charges the unit holds, which is clamped to the maximum it states.</param>
	void SetUnitChargeCount(UnitId unit, int charges);

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
