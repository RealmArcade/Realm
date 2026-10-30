using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;

namespace Realm.MapAPI;

/// <summary>
/// Adds the map script surface of a unit: its template, its orders and the effects a map can put on it.
/// </summary>
/// <remarks>
/// The handle itself is a plain index owned by the shared simulation, so the simulation can name this subject
/// without the script surface. What a script reads answers straight out of the world the process is simulating,
/// and everything that changes the world routes through the authoritative host imports.
/// </remarks>
public static unsafe class UnitIdScriptExtensions
{
	extension(UnitId unitId)
	{
	/// <summary>
	/// Gets the magic resistance points of the unit. Magic resistance mitigates magic damage only.
	/// </summary>
	public float MagicResistance
	{
		get => unitId.Value > 0 && unitId.Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[unitId.Value] != 0 ? EcsSharedMemory.GetComponent<Resistances>(ComponentIds.Resistances, unitId.Value)->Magic : 0f;
	}

	/// <summary>
	/// Gets the current position of the unit in 3D world space.
	/// </summary>
	public Vector3 Position => unitId.Value > 0 && unitId.Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[unitId.Value] != 0 ? EcsSharedMemory.GetComponent<Position>(ComponentIds.Position, unitId.Value)->Value : Vector3.Zero;

	/// <summary>
	/// Gets the canonical template identifier of the unit archetype.
	/// </summary>
	public string TemplateID => GameAPI.Current.GetUnitTemplateId(unitId);

	/// <summary>
	/// Gets the canonical archetype identifier (alias for <see cref="TemplateID"/>).
	/// </summary>
	public string TypeId => unitId.TemplateID;

	/// <summary>
	/// Gets the canonical archetype identifier (alias for <see cref="TemplateID"/>).
	/// </summary>
	public string UnitTypeId => unitId.TemplateID;

	/// <summary>
	/// Gets the canonical template identifier (alias for <see cref="TemplateID"/>).
	/// </summary>
	public string UnitIdString => unitId.TemplateID;

	/// <summary>
	/// Gets a value indicating whether the unit is a structural building.
	/// </summary>
	public bool IsBuilding => unitId.Value > 0 && unitId.Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[unitId.Value] != 0 && EcsSharedMemory.HasComponent(unitId.Value, ComponentIds.Building);

	/// <summary>
	/// Gets a value indicating whether the unit is a Hero unit.
	/// </summary>
	public bool IsHero => unitId.Value > 0 && unitId.Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[unitId.Value] != 0 && EcsSharedMemory.HasComponent(unitId.Value, ComponentIds.Hero);

	/// <summary>
	/// Gets the experience point bounty awarded upon defeating this unit.
	/// </summary>
	public float XpBounty => 0f;

	/// <summary>
	/// Gets the gold resource bounty awarded upon defeating this unit.
	/// </summary>
	public float GoldBounty => 0f;

	/// <summary>
	/// Gets a value indicating whether this unit is dead.
	/// </summary>
	public bool IsDead => unitId.Value <= 0 || unitId.Value >= EcsSharedMemory.MAX_ENTITIES || EcsSharedMemory.Alive[unitId.Value] == 0;

	/// <summary>
	/// Orders the unit to move to the specified destination in world coordinates.
	/// </summary>
	/// <param name="destination">The destination position.</param>
	public void MoveTo(Vector3 destination) => GameAPI.Current.IssueMoveOrder(unitId, destination);

	/// <summary>
	/// Orders the unit to attack-move towards the specified destination.
	/// </summary>
	/// <param name="destination">The target destination.</param>
	public void AttackMove(Vector3 destination) => GameAPI.Current.IssueAttackMoveOrder(unitId, destination);

	/// <summary>
	/// Orders the unit to attack the specified target unit.
	/// </summary>
	/// <param name="target">The target unit.</param>
	public void Attack(UnitId target) => GameAPI.Current.IssueAttackOrder(unitId, target);

	/// <summary>
	/// Orders the unit to harvest resources from the specified resource node.
	/// </summary>
	/// <param name="resourceNode">The target resource node.</param>
	public void Gather(ResourceNodeId resourceNode) => GameAPI.Current.IssueGatherOrder(unitId, resourceNode);

	/// <summary>
	/// Instantly teleports the unit to the specified position in world coordinates.
	/// </summary>
	/// <param name="position">The destination coordinates.</param>
	public void Teleport(Vector3 position) => GameAPI.Current.TeleportUnit(unitId, position);

	/// <summary>
	/// Orders the unit to stop its current action immediately.
	/// </summary>
	public void Stop() => GameAPI.Current.IssueStopOrder(unitId);

	/// <summary>
	/// Orders the unit to hold its ground and defend itself without moving.
	/// </summary>
	public void HoldPosition() => GameAPI.Current.IssueHoldPositionOrder(unitId);

	/// <summary>
	/// Applies a timed status effect buff to the unit.
	/// </summary>
	/// <param name="buffId">The registered buff identifier.</param>
	/// <param name="duration">The duration of the buff in seconds.</param>
	public void AddBuff(string buffId, float duration) => GameAPI.Current.AddBuff(unitId, buffId, duration);

	/// <summary>
	/// Removes an active status effect buff from the unit.
	/// </summary>
	/// <param name="buffId">The buff identifier to remove.</param>
	public void RemoveBuff(string buffId) => GameAPI.Current.RemoveBuff(unitId, buffId);

	/// <summary>
	/// Orders the unit to cast an ability at the specified target position.
	/// </summary>
	/// <param name="abilityId">The registered ability identifier.</param>
	/// <param name="targetPosition">The target world coordinates.</param>
	public void CastAbility(string abilityId, Vector3 targetPosition) => GameAPI.Current.CastAbility(unitId, abilityId, targetPosition);

	/// <summary>
	/// Gets the remaining cooldown of an ability on this unit in seconds.
	/// </summary>
	/// <param name="abilityId">The registered ability identifier.</param>
	/// <returns>The remaining cooldown in seconds.</returns>
	public float GetAbilityCooldown(string abilityId) => GameAPI.Current.GetAbilityCooldown(unitId, abilityId);

	/// <summary>
	/// Sets the remaining cooldown of an ability on this unit.
	/// </summary>
	/// <param name="abilityId">The registered ability identifier.</param>
	/// <param name="cooldown">The cooldown in seconds.</param>
	public void SetAbilityCooldown(string abilityId, float cooldown) => GameAPI.Current.SetAbilityCooldown(unitId, abilityId, cooldown);

	/// <summary>
	/// Stuns the unit, disabling movement and attack orders for the specified duration.
	/// </summary>
	/// <param name="duration">The stun duration in seconds.</param>
	public void Stun(float duration) => GameAPI.Current.StunUnit(unitId, duration);

	/// <summary>
	/// Silences the unit, preventing ability casting for the specified duration.
	/// </summary>
	/// <param name="duration">The silence duration in seconds.</param>
	public void Silence(float duration) => GameAPI.Current.SilenceUnit(unitId, duration);

	/// <summary>
	/// Adds an item to the unit's inventory.
	/// </summary>
	/// <param name="itemId">The identifier of the item definition to grant.</param>
	/// <returns>True if the item was successfully added; otherwise false.</returns>
	public bool AddItem(string itemId) => GameAPI.Current.AddItemToUnit(unitId, itemId);

	/// <summary>
	/// Adds an item to the unit's inventory with the specified initial number of charges.
	/// </summary>
	/// <param name="itemId">The identifier of the item definition to grant.</param>
	/// <param name="charges">The initial charge count for the item.</param>
	/// <returns>True if the item was successfully added; otherwise false.</returns>
	public bool AddItem(string itemId, int charges) => GameAPI.Current.AddItemToUnitWithCharges(unitId, itemId, charges);

	/// <summary>
	/// Gets the number of charges for the specified item in the unit's inventory.
	/// </summary>
	/// <param name="itemId">The identifier of the item.</param>
	/// <returns>The charge count, or 0 if the unit does not carry the item.</returns>
	public int GetItemCharges(string itemId) => GameAPI.Current.GetUnitItemCharges(unitId, itemId);

	/// <summary>
	/// Sets the number of charges for the specified item in the unit's inventory.
	/// </summary>
	/// <param name="itemId">The identifier of the item.</param>
	/// <param name="charges">The new charge count; zero removes the item's grants.</param>
	public void SetItemCharges(string itemId, int charges) => GameAPI.Current.SetUnitItemCharges(unitId, itemId, charges);

	/// <summary>
	/// Removes an item from the unit's inventory.
	/// </summary>
	/// <param name="itemId">The identifier of the item definition to remove.</param>
	/// <returns>True if the item was found and removed; otherwise false.</returns>
	public bool RemoveItem(string itemId) => GameAPI.Current.RemoveItemFromUnit(unitId, itemId);

	/// <summary>
	/// Determines whether the unit currently carries the specified item in its inventory.
	/// </summary>
	/// <param name="itemId">The item identifier to check.</param>
	/// <returns>True if the item exists in the unit's inventory; otherwise false.</returns>
	public bool HasItem(string itemId) => GameAPI.Current.UnitHasItem(unitId, itemId);

	/// <summary>
	/// Retrieves the item archetype identifiers currently held in the unit's inventory.
	/// </summary>
	/// <returns>A read-only collection of item archetype identifiers.</returns>
	public IReadOnlyList<string> GetItems() => GameAPI.Current.GetUnitItems(unitId);

	/// <summary>
	/// Determines whether the unit currently has an active buff with the specified identifier.
	/// </summary>
	/// <param name="buffId">The buff identifier to check.</param>
	/// <returns>True if the buff is active on the unit; otherwise false.</returns>
	public bool HasBuff(string buffId) => GameAPI.Current.UnitHasBuff(unitId, buffId);

	/// <summary>
	/// Stores a custom string metadata key-value pair on this unit.
	/// </summary>
	/// <param name="key">The custom data key.</param>
	/// <param name="value">The custom data string value.</param>
	public void SetCustomData(string key, string value) => GameAPI.Current.SetUnitCustomData(unitId, key, value);

	/// <summary>
	/// Retrieves custom string metadata associated with this unit, or null if not found.
	/// </summary>
	/// <param name="key">The custom data key.</param>
	/// <returns>The stored string value, or null.</returns>
	public string? GetCustomData(string key) => GameAPI.Current.GetUnitCustomData(unitId, key);

	/// <summary>
	/// Removes custom metadata associated with the specified key on this unit.
	/// </summary>
	/// <param name="key">The custom data key to remove.</param>
	/// <returns>True if data was removed; otherwise false.</returns>
	public bool RemoveCustomData(string key) => GameAPI.Current.RemoveUnitCustomData(unitId, key);

	/// <summary>
	/// Determines whether custom metadata exists for the specified key on this unit.
	/// </summary>
	/// <param name="key">The custom data key to check.</param>
	/// <returns>True if custom data exists; otherwise false.</returns>
	public bool HasCustomData(string key) => GameAPI.Current.UnitHasCustomData(unitId, key);

	/// <summary>
	/// Enables or disables automatic ability autocasting for the specified ability on this unit.
	/// </summary>
	/// <param name="abilityId">The registered ability identifier.</param>
	/// <param name="active">True to enable autocast; otherwise false.</param>
	public void SetAutoCast(string abilityId, bool active) => GameAPI.Current.SetAbilityAutoCast(unitId, abilityId, active);

	/// <summary>
	/// Orders the unit to collect the specified ground loot item.
	/// </summary>
	/// <param name="item">The target loot item to pick up.</param>
	/// <returns>True if the collection command was accepted; otherwise false.</returns>
	public bool CollectLootItem(LootItemId item) => GameAPI.Current.CollectLootItem(item.Value, unitId);

	/// <summary>
	/// Sets the team or tint color of the unit's material.
	/// </summary>
	/// <param name="color">The RGB color vector.</param>
	public void SetColor(Vector3 color) => GameAPI.Current.SetUnitColor(unitId, color);

	/// <summary>
	/// Plays the specified skeletal or state animation on the unit model.
	/// </summary>
	/// <param name="animationName">The animation clip name.</param>
	public void SetAnimation(string animationName) => GameAPI.Current.SetUnitAnimation(unitId, animationName);

	/// <summary>
	/// Sets whether the unit is immune to spell effects and magic abilities.
	/// </summary>
	/// <param name="immune">True for spell immunity; otherwise false.</param>
	public void SetSpellImmune(bool immune) => GameAPI.Current.SetUnitSpellImmune(unitId, immune);

	/// <summary>
	/// Triggers an elastic deformation mesh impulse visual effect on the unit model.
	/// </summary>
	/// <param name="strength">The peak displacement amplitude of the impulse vibration.</param>
	/// <param name="duration">The total time in seconds over which the impulse decays.</param>
	/// <param name="frequency">The oscillatory speed of the displacement impulse.</param>
	public void TriggerMeshImpulse(float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f) =>
		GameAPI.Current.TriggerMeshImpulse(unitId, strength, duration, frequency);

	/// <summary>
	/// Kills the unit, executing its death sequence and optionally playing animations/shaders.
	/// </summary>
	/// <param name="executeDespawnShader">Whether to execute the despawn material dissolve shader.</param>
	/// <param name="playDeathAnimation">Whether to play the death skeletal animation.</param>
	public void Kill(bool executeDespawnShader = true, bool playDeathAnimation = true) =>
		GameAPI.Current.KillUnit(unitId, executeDespawnShader, playDeathAnimation);

	/// <summary>
	/// Instantly destroys the unit entity without death bounty awards.
	/// </summary>
	/// <param name="executeDespawnShader">Whether to execute the despawn material dissolve shader.</param>
	/// <param name="playDeathAnimation">Whether to play the death skeletal animation.</param>
	public void Destroy(bool executeDespawnShader = true, bool playDeathAnimation = true) =>
		GameAPI.Current.DestroyUnit(unitId, executeDespawnShader, playDeathAnimation);
	}
}
