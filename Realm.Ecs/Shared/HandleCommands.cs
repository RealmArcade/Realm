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

	void SetUnitOwner(UnitId unit, int playerIndex);

	void SetUnitLevel(UnitId unit, int level);

	void SetUnitExperience(UnitId unit, float value);

	void SetUnitScale(UnitId unit, float value);

	void SetUnitInvulnerable(UnitId unit, bool value);

	void SetUnitAnimationsFrozen(UnitId unit, bool value);

	void SetUnitCommandsFrozen(UnitId unit, bool value);

	void SetUnitFrozen(UnitId unit, bool value);

	void SetUnitHideHealthAndManaBars(UnitId unit, bool value);

	void SetUnitFacing(UnitId unit, float facingRadians);

	void SetUnitName(UnitId unit, string value);

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
