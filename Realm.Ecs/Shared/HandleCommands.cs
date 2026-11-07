namespace Realm.Ecs;

/// <summary>
/// The commands a handle property sends when a script assigns to it, implemented by whoever runs the map script.
/// </summary>
/// <remarks>
/// An assignment through <see cref="UnitId"/>, <see cref="PlayerId"/> or <see cref="ResourceNodeId"/> is a statement of
/// intent rather than a direct write: the property hands the value to <see cref="HandleCommands.Current"/>, which is the
/// map script api of the process running the script, and the host applies it to authoritative state. The effect is
/// therefore visible to script reads on the next tick, not immediately. A command is dropped when no sink is installed,
/// and the host ignores a unit, player slot or node that no longer exists.
/// </remarks>
public interface IHandleCommands
{
	/// <summary>
	/// Sets the unit's current health points.
	/// </summary>
	/// <remarks>
	/// The host clamps the value to the range from zero to <see cref="UnitId.MaxHealth"/> and treats zero as a death:
	/// the unit is destroyed rather than left standing at zero health, so a script cannot use an assignment to park a
	/// unit at zero.
	/// </remarks>
	/// <param name="unit">The unit whose health to change.</param>
	/// <param name="value">Current health in hit points; floored at zero and capped at the unit's maximum.</param>
	void SetUnitHealth(UnitId unit, float value);

	/// <summary>
	/// Sets the unit's maximum health capacity, which is the ceiling <see cref="SetUnitHealth"/> clamps against.
	/// </summary>
	/// <remarks>
	/// Values below one are raised to one, so a unit always has a capacity of at least one hit point. Raising the
	/// capacity does not heal: current health keeps its value. Lowering it clamps current health down to the new
	/// maximum without destroying the unit, so shrinking a bar cannot kill what it measures.
	/// </remarks>
	/// <param name="unit">The unit whose capacity to change.</param>
	/// <param name="value">Maximum health in hit points; raised to one when lower.</param>
	void SetUnitMaxHealth(UnitId unit, float value);

	/// <summary>
	/// Sets the unit's current mana points.
	/// </summary>
	/// <remarks>
	/// The host clamps the value to the range from zero to <see cref="UnitId.MaxMana"/>. Unlike health, reaching zero
	/// is not a death; it only leaves the unit unable to pay ability costs.
	/// </remarks>
	/// <param name="unit">The unit whose mana to change.</param>
	/// <param name="value">Current mana in points; floored at zero and capped at the unit's maximum.</param>
	void SetUnitMana(UnitId unit, float value);

	/// <summary>
	/// Sets the unit's maximum mana capacity, which is the ceiling <see cref="SetUnitMana"/> clamps against.
	/// </summary>
	/// <remarks>
	/// Negative values are treated as zero, giving a unit that can never hold mana. Raising the capacity does not
	/// refill mana; lowering it clamps current mana down to the new maximum.
	/// </remarks>
	/// <param name="unit">The unit whose capacity to change.</param>
	/// <param name="value">Maximum mana in points; floored at zero.</param>
	void SetUnitMaxMana(UnitId unit, float value);

	/// <summary>
	/// Sets how much mana the unit regenerates, in mana points per second.
	/// </summary>
	/// <remarks>
	/// The rate replaces the one the unit already has rather than adding to it, and it is applied as given, so a
	/// negative rate drains mana instead of restoring it. It is read by the mana regeneration system from the next
	/// tick onward.
	/// </remarks>
	/// <param name="unit">The unit whose regeneration rate to change.</param>
	/// <param name="value">Mana points gained per second; negative values drain.</param>
	void SetUnitManaRegen(UnitId unit, float value);

	/// <summary>
	/// Sets the unit's base attack damage, the damage one hit deals before armor, resistances and critical strikes.
	/// </summary>
	/// <remarks>
	/// The value is stored as given, without clamping, so zero is a legal way to make a unit harmless. The unit must
	/// already have an attack: a unit without one ignores the assignment rather than gaining the ability to fight.
	/// </remarks>
	/// <param name="unit">The attacking unit.</param>
	/// <param name="value">Damage per hit in hit points.</param>
	void SetUnitDamage(UnitId unit, float value);

	/// <summary>
	/// Sets the unit's attack range, the distance at which it can start attacking a target.
	/// </summary>
	/// <remarks>
	/// The range is measured in world metres and stored without clamping, so a value at or below the unit's own
	/// radius produces a unit that can only attack targets it is already touching. It applies to the next target
	/// acquisition, not to an attack already in progress.
	/// </remarks>
	/// <param name="unit">The attacking unit.</param>
	/// <param name="value">Attack range in metres.</param>
	void SetUnitRange(UnitId unit, float value);

	/// <summary>
	/// Sets the unit's armor rating, the physical resistance that reduces incoming attack damage.
	/// </summary>
	/// <remarks>
	/// Armor mitigates physical damage only: magic damage, pure damage and damage-over-time effects ignore it. The
	/// value is stored without clamping and may be negative, which increases physical damage taken.
	/// </remarks>
	/// <param name="unit">The unit whose armor to change.</param>
	/// <param name="value">Armor points; negative values are a vulnerability, not an immunity.</param>
	void SetUnitArmor(UnitId unit, float value);

	/// <summary>
	/// Sets the unit's movement speed, in world metres per second.
	/// </summary>
	/// <remarks>
	/// The value replaces the unit's base speed and is stored without clamping; zero is a legal way to pin a unit
	/// without freezing its animation or its orders. Pathing reads the new speed from the next tick, so a unit that
	/// is already moving changes pace rather than stopping.
	/// </remarks>
	/// <param name="unit">The unit whose speed to change.</param>
	/// <param name="value">Movement speed in metres per second.</param>
	void SetUnitSpeed(UnitId unit, float value);

	/// <summary>
	/// Sets the unit's base attack cooldown, the interval between two attacks before attack-speed modifiers apply.
	/// </summary>
	/// <remarks>
	/// The stored quantity is a cooldown in seconds, not an attacks-per-second rate: a larger value attacks more
	/// slowly, and the host floors it at 0.01 seconds so a unit can never divide by zero. Attack-speed effects then
	/// scale the cooldown down, which is why a map that wants a fixed rate should keep modifiers off the unit. The
	/// handle property that reaches this command is named <c>AttackSpeed</c>, which is the historical name for the
	/// same field.
	/// </remarks>
	/// <param name="unit">The attacking unit.</param>
	/// <param name="value">Seconds between attacks; raised to 0.01 when lower.</param>
	void SetUnitAttackSpeed(UnitId unit, float value);

	/// <summary>
	/// Transfers ownership of the unit to a player slot, changing who its team, allies, vision and upkeep belong to.
	/// </summary>
	/// <remarks>
	/// The slot must be one the current match has seated: the host ignores the assignment when the slot has no player
	/// entity, so a map cannot invent an owner. The unit keeps its orders, position and health across the transfer; only
	/// its allegiance changes, and the player's population count includes it from the next tick onward.
	/// </remarks>
	/// <param name="unit">The unit to transfer.</param>
	/// <param name="playerIndex">Zero-based slot index of the new owner.</param>
	void SetUnitOwner(UnitId unit, int playerIndex);

	/// <summary>
	/// Sets the unit's level, the number its overhead plate and progression use.
	/// </summary>
	/// <remarks>
	/// Values below one are raised to one. The assignment also reapplies the level growth curve of the unit's own
	/// template, so a script that levels a unit gets the same attribute gains that template defines rather than a bare
	/// number change; a unit whose template has no curve keeps its attributes. It is not restricted to heroes.
	/// </remarks>
	/// <param name="unit">The unit whose level to change.</param>
	/// <param name="level">The new level; raised to one when lower.</param>
	void SetUnitLevel(UnitId unit, int level);

	/// <summary>
	/// Sets the unit's accumulated experience points.
	/// </summary>
	/// <remarks>
	/// The value is stored as given, without clamping, and is the total the progression system compares against level
	/// thresholds. It does not level the unit by itself: reaching a threshold on a later tick is what grants the level.
	/// </remarks>
	/// <param name="unit">The hero or unit whose experience to change.</param>
	/// <param name="value">Total experience points; negative values are stored as given.</param>
	void SetUnitExperience(UnitId unit, float value);

	/// <summary>
	/// Sets the unit's visual scale multiplier, resizing both the model and the collision shape it occupies.
	/// </summary>
	/// <remarks>
	/// One is the size authored in the template. The value is applied without validation, so zero collapses the model
	/// and its collision shape to nothing and negative values mirror it; a map that wants a unit larger or smaller
	/// should stay above zero. The change is visible to every client from the next snapshot onward.
	/// </remarks>
	/// <param name="unit">The unit to resize.</param>
	/// <param name="value">Scale factor applied to the model and collision shape.</param>
	void SetUnitScale(UnitId unit, float value);

	/// <summary>
	/// Makes the unit immune to all damage, or removes that immunity.
	/// </summary>
	/// <remarks>
	/// The flag suppresses damage from every source, including attacks, abilities and script damage, without affecting
	/// orders or movement, so it composes with <see cref="SetUnitFrozen"/> to build invulnerable scenery. Setting it
	/// again is a no-op rather than a stacked count, and false removes it cleanly.
	/// </remarks>
	/// <param name="unit">The unit to make immune.</param>
	/// <param name="value">True for immunity to all damage; false to take damage normally again.</param>
	void SetUnitInvulnerable(UnitId unit, bool value);

	/// <summary>
	/// Suspends or resumes the unit's animation playback, holding the model in one pose.
	/// </summary>
	/// <remarks>
	/// Locomotion, attack, idle and death playback stop together with procedural sway, but the unit can still move,
	/// attack and cast, so it slides while holding its pose. Combine this with <see cref="SetUnitCommandsFrozen"/> to
	/// build a statue. The flag lives on the unit entity and survives level changes and element swaps until the entity
	/// is destroyed or the animation is unfrozen.
	/// </remarks>
	/// <param name="unit">The unit whose animation to hold.</param>
	/// <param name="value">True to hold the current pose; false to resume playback.</param>
	void SetUnitAnimationsFrozen(UnitId unit, bool value);

	/// <summary>
	/// Makes the unit ignore movement, attack and hold orders, or accepts them again.
	/// </summary>
	/// <remarks>
	/// A commands-frozen unit keeps its position, never acquires targets and ignores move, stop, hold and attack orders,
	/// and clients hide those command buttons for it. Ability casting stays available, so map-authored abilities keep
	/// working. Existing orders and targets are cleared when the unit freezes, and the flag lives on the unit entity, so
	/// it survives level changes and element swaps until the entity is destroyed or the unit is unfrozen.
	/// </remarks>
	/// <param name="unit">The unit to freeze orders on.</param>
	/// <param name="value">True to ignore movement and attack orders; false to accept them again.</param>
	void SetUnitCommandsFrozen(UnitId unit, bool value);

	/// <summary>
	/// Pins the unit in place, or releases it again, by freezing its animation and its orders together.
	/// </summary>
	/// <remarks>
	/// This sets both <see cref="SetUnitAnimationsFrozen"/> and <see cref="SetUnitCommandsFrozen"/> and clears the
	/// unit's current path, so a frozen unit never moves, never turns in response to an order and holds one pose. The
	/// call is idempotent and composes with <see cref="SetUnitInvulnerable"/>, facing and the health-bar visibility
	/// flags. The state lives on the unit entity and survives level changes and swaps until the entity is destroyed or
	/// the unit is unfrozen.
	/// </remarks>
	/// <param name="unit">The unit to pin in place.</param>
	/// <param name="value">True to freeze the unit; false to let it act normally again.</param>
	void SetUnitFrozen(UnitId unit, bool value);

	/// <summary>
	/// Shows or hides the unit's overhead health and mana bars.
	/// </summary>
	/// <remarks>
	/// Hidden bars stay hidden in every display mode, including the modes that show bars for every living unit, which is
	/// how a map makes scenery units that never advertise hit points. The flag lives on the unit entity and is gone when
	/// the entity is destroyed.
	/// </remarks>
	/// <param name="unit">The unit whose bars to hide.</param>
	/// <param name="value">True to never draw bars for the unit; false to draw them according to the player's setting.</param>
	void SetUnitHideHealthAndManaBars(UnitId unit, bool value);

	/// <summary>
	/// Sets the direction the unit faces, as an absolute angle around the vertical axis.
	/// </summary>
	/// <remarks>
	/// The angle is in radians and is not normalised, so any real value is accepted and equivalent angles behave
	/// equivalently. It is a pose, not an order: the next move or attack order is free to turn the unit again, and a
	/// commands-frozen unit keeps the pose because it accepts no such orders.
	/// </remarks>
	/// <param name="unit">The unit to turn.</param>
	/// <param name="facingRadians">Facing angle in radians around the vertical axis.</param>
	void SetUnitFacing(UnitId unit, float facingRadians);

	/// <summary>
	/// Sets the unit's display name, the instance name that distinguishes it from other units of the same template.
	/// </summary>
	/// <remarks>
	/// The name is stored on the unit entity, so it follows the entity through level changes and element swaps and is
	/// drawn wherever the unit's name is shown. A null value is stored as an empty name rather than clearing the
	/// component, and an empty name is what <see cref="GetUnitInstanceName"/> returns for a unit that has never been named.
	/// </remarks>
	/// <param name="unit">The unit to name.</param>
	/// <param name="value">The new display name; null is treated as empty.</param>
	void SetUnitName(UnitId unit, string value);

	/// <summary>
	/// Reads the unit's display name, the instance name set through <see cref="SetUnitName"/> or by its spawn.
	/// </summary>
	/// <remarks>
	/// The read is answered by the host rather than from the handle's own copy of the world, so it reflects the state
	/// the authority had when the call was made. A unit that is dead, unknown to this process or never named answers an
	/// empty string, which makes an empty result the test for "no such unit as far as the script is concerned" as well.
	/// </remarks>
	/// <param name="unit">The unit to read.</param>
	/// <returns>The unit's display name, or an empty string when it has none or no longer exists.</returns>
	string GetUnitInstanceName(UnitId unit);

	void SetPlayerComputerControlled(int playerIndex, bool isProxy);

	void SetPlayerTeam(int playerIndex, int teamIndex);

	void SetPlayerGold(int playerIndex, float amount);

	void SetPlayerWood(int playerIndex, float amount);

	void SetPlayerStone(int playerIndex, float amount);

	void SetPlayerMaxPopulation(int playerIndex, int max);

	void SetPlayerKills(int playerIndex, int kills);

	void SetResourceNodeAmount(ResourceNodeId node, float value);

	float GetResourceNodeAmount(ResourceNodeId node);
}

/// <summary>
/// Holds the command sink that handle properties reach, which is the map script api of the process running them.
/// </summary>
public static class HandleCommands
{
	/// <summary>
	/// Gets or sets the sink that handle property assignments in this process send their command to. It is null until a
	/// host has assigned one, and an assignment made while it is null is dropped.
	/// </summary>
	public static IHandleCommands? Current { get; set; }
}
