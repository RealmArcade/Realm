// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Adds a buff to a unit.
	/// </summary>
	/// <param name="unit">The target unit identifier.</param>
	/// <param name="buffId">The identifier of the buff.</param>
	/// <param name="duration">The duration of the buff in seconds.</param>
	void AddBuff(UnitId unit, string buffId, float duration);

	/// <summary>
	/// Adds one permanent stat modifier to a unit, tagged with a source id so the map can remove it later.
	/// The modifier goes through the same pipeline as upgrades and items, so it survives stat
	/// recalculation instead of being overwritten by the next derived-stat pass.
	/// </summary>
	/// <param name="unit">The target unit identifier.</param>
	/// <param name="statName">The stat key to modify (e.g. "Armor", "Attack", "MaxHp").</param>
	/// <param name="isPercentage">True for an additive percentage, false for a flat bonus.</param>
	/// <param name="value">The modifier value.</param>
	/// <param name="sourceId">Source tag, typically one per map feature, such as "elixir/blood_debt".</param>
	void AddStatModifier(UnitId unit, string statName, bool isPercentage, float value, string sourceId);

	/// <summary>
	/// Removes every stat modifier a unit received from one source id and recalculates its stats.
	/// </summary>
	/// <param name="unit">The target unit identifier.</param>
	/// <param name="sourceId">The source tag passed to <see cref="AddStatModifier"/>.</param>
	void RemoveStatModifiers(UnitId unit, string sourceId);

	/// <summary>
	/// Registers a stat modifier for a buff type.
	/// </summary>
	/// <param name="buffId">The identifier of the buff.</param>
	/// <param name="statName">The name of the stat (e.g. "Armor", "Attack", "MovementSpeed").</param>
	/// <param name="isPercentage">True for percentage multiplier, false for flat bonus.</param>
	/// <param name="value">The modifier value.</param>
	void RegisterBuffModifier(string buffId, string statName, bool isPercentage, float value);

	/// <summary>
	/// Registers the footprint radius used when placing or checking for overlap with a unit, building, or prop.
	/// </summary>
	/// <param name="definitionKey">A case-insensitive fragment that must be contained in the definition identifier for the rule to apply. Rules are evaluated in registration order and the first match wins.</param>
	/// <param name="radius">The base footprint radius in world units, before the object's scale is applied.</param>
	void RegisterPlacementRadius(string definitionKey, float radius);

	/// <summary>
	/// Registers a building definition as a drop-off point where workers deliver gathered resources.
	/// </summary>
	/// <param name="definitionKey">A case-insensitive fragment that must be contained in the building definition identifier.</param>
	void RegisterResourceDropoffBuilding(string definitionKey);

	/// <summary>
	/// Registers a unit definition fragment identifying workers; any building able to produce a matching unit is treated as a resource drop-off point.
	/// </summary>
	/// <param name="unitKey">A case-sensitive fragment that must be contained in the unit definition identifier in a building's build options.</param>
	void RegisterResourceWorkerUnit(string unitKey);

	/// <summary>
	/// Registers a building definition as a player's primary building, which camera shortcuts focus on.
	/// </summary>
	/// <param name="definitionId">The exact, case-insensitive building definition identifier.</param>
	void RegisterPrimaryBuilding(string definitionId);

	/// <summary>
	/// Declares which resource type a harvestable node prop yields.
	/// </summary>
	/// <param name="nodeDefinitionId">The prop definition identifier of the harvestable node (case-insensitive).</param>
	/// <param name="resourceType">The resource type gathered from the node, such as "gold", "wood" or "stone".</param>
	void RegisterResourceNodeType(string nodeDefinitionId, string resourceType);

	/// <summary>
	/// Enables the match's passive resource income, which pays every player gold, wood and stone on a timer.
	/// Nothing is paid on a timer unless a map asks for it, so a map that awards resources from its own script
	/// leaves this off and every resource a player holds comes from that script.
	/// </summary>
	void EnablePassiveResourceIncome();

	/// <summary>
	/// Chooses which parts of every unit's overhead plate the match draws: its health bar, its mana bar and its level
	/// badge. A map that never calls this draws the health bar alone, which is what its metadata declares when it says
	/// nothing. Every combination is valid: the bars stack when both are on, the badge leads them when the level is on,
	/// and asking for the level alone draws the badge by itself.
	/// </summary>
	/// <param name="healthBar">Whether to draw the health bar, the top bar of the plate.</param>
	/// <param name="manaBar">Whether to draw the mana bar, which sits under the health bar.</param>
	/// <param name="levelBadge">Whether to draw the unit's level to the left of the bars.</param>
	void SetUnitBarVisibility(bool healthBar, bool manaBar, bool levelBadge);

	/// <summary>
	/// Executes an ability generically by ID on a unit targeting a position.
	/// </summary>
	/// <param name="unit">The unit casting the ability.</param>
	/// <param name="abilityId">The identifier of the ability.</param>
	/// <param name="targetPosition">The target position in 3D world space.</param>
	void CastAbility(UnitId unit, string abilityId, Vector3 targetPosition);

	/// <summary>
	/// Gets the remaining cooldown of a unit's ability in seconds.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="abilityId">The identifier of the ability.</param>
	/// <returns>Remaining cooldown in seconds, or 0 if ready or not found.</returns>
	float GetAbilityCooldown(UnitId unit, string abilityId);

	/// <summary>
	/// Sets the cooldown of a unit's ability.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="abilityId">The identifier of the ability.</param>
	/// <param name="cooldown">The cooldown duration in seconds.</param>
	void SetAbilityCooldown(UnitId unit, string abilityId, float cooldown);

	/// <summary>
	/// Removes a buff from a unit.
	/// </summary>
	/// <param name="unit">The target unit identifier.</param>
	/// <param name="buffId">The identifier of the buff to remove.</param>
	void RemoveBuff(UnitId unit, string buffId);

	/// <summary>
	/// Gets active modifiers on a unit.
	/// </summary>
	/// <param name="unit">The target unit identifier.</param>
	/// <returns>A collection of modifier description strings.</returns>
	IEnumerable<string> GetModifiers(UnitId unit);
}
