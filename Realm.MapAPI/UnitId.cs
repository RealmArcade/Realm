using System.Collections.Generic;
using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Represents a strongly-typed, unmanaged entity identifier for a unit instance in the game world,
/// providing zero-allocation fluent access to unit properties and commands.
/// </summary>
public readonly unsafe record struct UnitId(int Value)
{
	/// <summary>
	/// Implicitly converts a 32-bit integer identifier into a <see cref="UnitId"/>.
	/// </summary>
	/// <param name="value">The raw integer unit identifier.</param>
	public static implicit operator UnitId(int value) => new(value);

	/// <summary>
	/// Explicitly converts a <see cref="UnitId"/> into a 32-bit integer identifier.
	/// </summary>
	/// <param name="id">The unit identifier.</param>
	public static explicit operator int(UnitId id) => id.Value;

	/// <summary>
	/// Gets an invalid or unassigned unit identifier.
	/// </summary>
	public static readonly UnitId Invalid = new(0);

	/// <summary>
	/// Gets a value indicating whether this identifier represents a valid unit index.
	/// </summary>
	public bool IsValid => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES;

	/// <summary>
	/// Gets the raw integer identifier.
	/// </summary>
	public int UniqueId => Value;

	/// <summary>
	/// Gets the raw integer identifier (alias for <see cref="UniqueId"/>).
	/// </summary>
	public int Id => Value;

	/// <summary>
	/// Gets or sets the current health point value of the unit.
	/// </summary>
	public float Health
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Health>(ComponentIds.Health, Value)->Current : 0f;
		set => GameAPI.Current.SetUnitHealth(this, value);
	}

	/// <summary>
	/// Gets or sets the maximum health point capacity of the unit.
	/// </summary>
	public float MaxHealth
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Health>(ComponentIds.Health, Value)->Max : 0f;
		set => GameAPI.Current.SetUnitMaxHealth(this, value);
	}

	/// <summary>
	/// Gets or sets the current mana points of the unit.
	/// </summary>
	public float Mana
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Mana>(ComponentIds.Mana, Value)->Current : 0f;
		set => GameAPI.Current.SetUnitMana(this, value);
	}

	/// <summary>
	/// Gets or sets the maximum mana capacity of the unit.
	/// </summary>
	public float MaxMana
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Mana>(ComponentIds.Mana, Value)->Max : 0f;
		set => GameAPI.Current.SetUnitMaxMana(this, value);
	}

	/// <summary>
	/// Gets or sets the mana regeneration rate of the unit per second.
	/// </summary>
	public float ManaRegen
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Mana>(ComponentIds.Mana, Value)->ManaRegen : 0f;
		set => GameAPI.Current.SetUnitManaRegen(this, value);
	}

	/// <summary>
	/// Gets or sets the base attack damage of the unit.
	/// </summary>
	public float Damage
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Attack>(ComponentIds.Attack, Value)->Damage : 0f;
		set => GameAPI.Current.SetUnitDamage(this, value);
	}

	/// <summary>
	/// Gets or sets the attack range of the unit.
	/// </summary>
	public float Range
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Attack>(ComponentIds.Attack, Value)->Range : 0f;
		set => GameAPI.Current.SetUnitRange(this, value);
	}

	/// <summary>
	/// Gets or sets the armor rating of the unit.
	/// </summary>
	public float Armor
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Armor>(ComponentIds.Armor, Value)->FlatArmor : 0f;
		set => GameAPI.Current.SetUnitArmor(this, value);
	}

	/// <summary>
	/// Gets or sets the movement speed of the unit.
	/// </summary>
	public float Speed
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<MovementStats>(ComponentIds.MovementStats, Value)->Speed : 0f;
		set => GameAPI.Current.SetUnitSpeed(this, value);
	}

	/// <summary>
	/// Gets or sets the attack speed multiplier of the unit.
	/// </summary>
	public float AttackSpeed
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Attack>(ComponentIds.Attack, Value)->Cooldown : 0f;
		set => GameAPI.Current.SetUnitAttackSpeed(this, value);
	}

	/// <summary>
	/// Gets the current position of the unit in 3D world space.
	/// </summary>
	public Vector3 Position => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Position>(ComponentIds.Position, Value)->Value : Vector3.Zero;

	/// <summary>
	/// Gets or sets the display name of the unit.
	/// </summary>
	public string Name
	{
		get => GameAPI.Current.GetUnitInstanceName(this);
		set => GameAPI.Current.SetUnitName(this, value);
	}

	/// <summary>
	/// Gets the canonical template identifier of the unit archetype.
	/// </summary>
	public string TemplateID => GameAPI.Current.GetUnitTemplateId(this);

	/// <summary>
	/// Gets the canonical archetype identifier (alias for <see cref="TemplateID"/>).
	/// </summary>
	public string TypeId => TemplateID;

	/// <summary>
	/// Gets the canonical archetype identifier (alias for <see cref="TemplateID"/>).
	/// </summary>
	public string UnitTypeId => TemplateID;

	/// <summary>
	/// Gets the canonical template identifier (alias for <see cref="TemplateID"/>).
	/// </summary>
	public string UnitIdString => TemplateID;

	/// <summary>
	/// Gets or sets the zero-based player slot index that owns this unit.
	/// </summary>
	public int Player
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<UnitOwnerPlayer>(ComponentIds.UnitOwnerPlayer, Value)->PlayerIndex : -1;
		set => GameAPI.Current.SetUnitOwner(this, value);
	}

	/// <summary>
	/// Gets a value indicating whether the unit is a structural building.
	/// </summary>
	public bool IsBuilding => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 && EcsSharedMemory.HasComponent(Value, ComponentIds.Building);

	/// <summary>
	/// Gets a value indicating whether the unit is a Hero unit.
	/// </summary>
	public bool IsHero => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 && EcsSharedMemory.HasComponent(Value, ComponentIds.Hero);

	/// <summary>
	/// Gets or sets the experience level of the unit.
	/// </summary>
	public int Level
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Level>(ComponentIds.Level, Value)->Value : 0;
		set => GameAPI.Current.SetUnitLevel(this, value);
	}

	/// <summary>
	/// Gets or sets the experience points accumulated by the hero unit.
	/// </summary>
	public float Experience
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Experience>(ComponentIds.Experience, Value)->Value : 0f;
		set => GameAPI.Current.SetUnitExperience(this, value);
	}

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
	public bool IsDead => Value <= 0 || Value >= EcsSharedMemory.MAX_ENTITIES || EcsSharedMemory.Alive[Value] == 0;

	/// <summary>
	/// Gets or sets the visual scaling factor of the unit model.
	/// </summary>
	public float Scale
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<ModelScale>(ComponentIds.ModelScale, Value)->Value : 1f;
		set => GameAPI.Current.SetUnitScale(this, value);
	}

	/// <summary>
	/// Gets or sets a value indicating whether the unit is invulnerable to all damage.
	/// </summary>
	public bool Invulnerable
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 && EcsSharedMemory.HasComponent(Value, ComponentIds.Invulnerable);
		set => GameAPI.Current.SetUnitInvulnerable(this, value);
	}

	/// <summary>
	/// Gets or sets a value indicating whether the unit is frozen in place with its animation suspended.
	/// </summary>
	/// <remarks>
	/// A frozen unit keeps its current position and pose: it ignores orders, does not acquire targets or
	/// cast abilities, and plays no locomotion, attack, idle or death animation, including procedural
	/// sway. Setting the property to the value it already has does nothing. The flag lives on the entity,
	/// so it survives level changes, element swaps and unit swaps until the entity is destroyed or the
	/// flag is cleared, and it does not conflict with <see cref="Invulnerable"/>, <see cref="Facing"/> or
	/// <see cref="HideHealthAndManaBars"/>.
	/// </remarks>
	public bool Frozen
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 && EcsSharedMemory.HasComponent(Value, ComponentIds.Frozen);
		set => GameAPI.Current.SetUnitFrozen(this, value);
	}

	/// <summary>
	/// Gets or sets a value indicating whether overhead health and mana bars are hidden for this unit.
	/// </summary>
	public bool HideHealthAndManaBars
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 && EcsSharedMemory.HasComponent(Value, ComponentIds.OverheadBarsHidden);
		set => GameAPI.Current.SetUnitHideHealthAndManaBars(this, value);
	}

	/// <summary>
	/// Gets or sets the facing orientation of the unit in radians.
	/// </summary>
	public float Facing
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<RotationY>(ComponentIds.RotationY, Value)->Value : 0f;
		set => GameAPI.Current.SetUnitFacing(this, value);
	}

	/// <summary>
	/// Orders the unit to move to the specified destination in world coordinates.
	/// </summary>
	/// <param name="destination">The destination position.</param>
	public void MoveTo(Vector3 destination) => GameAPI.Current.IssueMoveOrder(this, destination);

	/// <summary>
	/// Orders the unit to attack-move towards the specified destination.
	/// </summary>
	/// <param name="destination">The target destination.</param>
	public void AttackMove(Vector3 destination) => GameAPI.Current.IssueAttackMoveOrder(this, destination);

	/// <summary>
	/// Orders the unit to attack the specified target unit.
	/// </summary>
	/// <param name="target">The target unit.</param>
	public void Attack(UnitId target) => GameAPI.Current.IssueAttackOrder(this, target);

	/// <summary>
	/// Orders the unit to harvest resources from the specified resource node.
	/// </summary>
	/// <param name="resourceNode">The target resource node.</param>
	public void Gather(ResourceNodeId resourceNode) => GameAPI.Current.IssueGatherOrder(this, resourceNode);

	/// <summary>
	/// Instantly teleports the unit to the specified position in world coordinates.
	/// </summary>
	/// <param name="position">The destination coordinates.</param>
	public void Teleport(Vector3 position) => GameAPI.Current.TeleportUnit(this, position);

	/// <summary>
	/// Orders the unit to stop its current action immediately.
	/// </summary>
	public void Stop() => GameAPI.Current.IssueStopOrder(this);

	/// <summary>
	/// Orders the unit to hold its ground and defend itself without moving.
	/// </summary>
	public void HoldPosition() => GameAPI.Current.IssueHoldPositionOrder(this);

	/// <summary>
	/// Applies a timed status effect buff to the unit.
	/// </summary>
	/// <param name="buffId">The registered buff identifier.</param>
	/// <param name="duration">The duration of the buff in seconds.</param>
	public void AddBuff(string buffId, float duration) => GameAPI.Current.AddBuff(this, buffId, duration);

	/// <summary>
	/// Removes an active status effect buff from the unit.
	/// </summary>
	/// <param name="buffId">The buff identifier to remove.</param>
	public void RemoveBuff(string buffId) => GameAPI.Current.RemoveBuff(this, buffId);

	/// <summary>
	/// Orders the unit to cast an ability at the specified target position.
	/// </summary>
	/// <param name="abilityId">The registered ability identifier.</param>
	/// <param name="targetPosition">The target world coordinates.</param>
	public void CastAbility(string abilityId, Vector3 targetPosition) => GameAPI.Current.CastAbility(this, abilityId, targetPosition);

	/// <summary>
	/// Gets the remaining cooldown of an ability on this unit in seconds.
	/// </summary>
	/// <param name="abilityId">The registered ability identifier.</param>
	/// <returns>The remaining cooldown in seconds.</returns>
	public float GetAbilityCooldown(string abilityId) => GameAPI.Current.GetAbilityCooldown(this, abilityId);

	/// <summary>
	/// Sets the remaining cooldown of an ability on this unit.
	/// </summary>
	/// <param name="abilityId">The registered ability identifier.</param>
	/// <param name="cooldown">The cooldown in seconds.</param>
	public void SetAbilityCooldown(string abilityId, float cooldown) => GameAPI.Current.SetAbilityCooldown(this, abilityId, cooldown);

	/// <summary>
	/// Stuns the unit, disabling movement and attack orders for the specified duration.
	/// </summary>
	/// <param name="duration">The stun duration in seconds.</param>
	public void Stun(float duration) => GameAPI.Current.StunUnit(this, duration);

	/// <summary>
	/// Silences the unit, preventing ability casting for the specified duration.
	/// </summary>
	/// <param name="duration">The silence duration in seconds.</param>
	public void Silence(float duration) => GameAPI.Current.SilenceUnit(this, duration);

	/// <summary>
	/// Adds an item to the unit's inventory.
	/// </summary>
	/// <param name="itemId">The identifier of the item definition to grant.</param>
	/// <returns>True if the item was successfully added; otherwise false.</returns>
	public bool AddItem(string itemId) => GameAPI.Current.AddItemToUnit(this, itemId);

	/// <summary>
	/// Adds an item to the unit's inventory with the specified initial number of charges.
	/// </summary>
	/// <param name="itemId">The identifier of the item definition to grant.</param>
	/// <param name="charges">The initial charge count for the item.</param>
	/// <returns>True if the item was successfully added; otherwise false.</returns>
	public bool AddItem(string itemId, int charges) => AddItem(itemId);

	/// <summary>
	/// Gets the number of charges for the specified item in the unit's inventory.
	/// </summary>
	/// <param name="itemId">The identifier of the item.</param>
	/// <returns>The charge count, or 1 if the item is present without specific charges, or 0 if not present.</returns>
	public int GetItemCharges(string itemId) => HasItem(itemId) ? 1 : 0;

	/// <summary>
	/// Sets the number of charges for the specified item in the unit's inventory.
	/// </summary>
	/// <param name="itemId">The identifier of the item.</param>
	/// <param name="charges">The new charge count.</param>
	public void SetItemCharges(string itemId, int charges) { }

	/// <summary>
	/// Removes an item from the unit's inventory.
	/// </summary>
	/// <param name="itemId">The identifier of the item definition to remove.</param>
	/// <returns>True if the item was found and removed; otherwise false.</returns>
	public bool RemoveItem(string itemId) => GameAPI.Current.RemoveItemFromUnit(this, itemId);

	/// <summary>
	/// Determines whether the unit currently carries the specified item in its inventory.
	/// </summary>
	/// <param name="itemId">The item identifier to check.</param>
	/// <returns>True if the item exists in the unit's inventory; otherwise false.</returns>
	public bool HasItem(string itemId) => GameAPI.Current.UnitHasItem(this, itemId);

	/// <summary>
	/// Retrieves the item archetype identifiers currently held in the unit's inventory.
	/// </summary>
	/// <returns>A read-only collection of item archetype identifiers.</returns>
	public IReadOnlyList<string> GetItems() => GameAPI.Current.GetUnitItems(this);

	/// <summary>
	/// Determines whether the unit currently has an active buff with the specified identifier.
	/// </summary>
	/// <param name="buffId">The buff identifier to check.</param>
	/// <returns>True if the buff is active on the unit; otherwise false.</returns>
	public bool HasBuff(string buffId) => GameAPI.Current.UnitHasBuff(this, buffId);

	/// <summary>
	/// Stores a custom string metadata key-value pair on this unit.
	/// </summary>
	/// <param name="key">The custom data key.</param>
	/// <param name="value">The custom data string value.</param>
	public void SetCustomData(string key, string value) => GameAPI.Current.SetUnitCustomData(this, key, value);

	/// <summary>
	/// Retrieves custom string metadata associated with this unit, or null if not found.
	/// </summary>
	/// <param name="key">The custom data key.</param>
	/// <returns>The stored string value, or null.</returns>
	public string? GetCustomData(string key) => GameAPI.Current.GetUnitCustomData(this, key);

	/// <summary>
	/// Removes custom metadata associated with the specified key on this unit.
	/// </summary>
	/// <param name="key">The custom data key to remove.</param>
	/// <returns>True if data was removed; otherwise false.</returns>
	public bool RemoveCustomData(string key) => GameAPI.Current.RemoveUnitCustomData(this, key);

	/// <summary>
	/// Determines whether custom metadata exists for the specified key on this unit.
	/// </summary>
	/// <param name="key">The custom data key to check.</param>
	/// <returns>True if custom data exists; otherwise false.</returns>
	public bool HasCustomData(string key) => GameAPI.Current.UnitHasCustomData(this, key);

	/// <summary>
	/// Enables or disables automatic ability autocasting for the specified ability on this unit.
	/// </summary>
	/// <param name="abilityId">The registered ability identifier.</param>
	/// <param name="active">True to enable autocast; otherwise false.</param>
	public void SetAutoCast(string abilityId, bool active) => GameAPI.Current.SetAbilityAutoCast(this, abilityId, active);

	/// <summary>
	/// Orders the unit to collect the specified ground loot item.
	/// </summary>
	/// <param name="item">The target loot item to pick up.</param>
	/// <returns>True if the collection command was accepted; otherwise false.</returns>
	public bool CollectLootItem(LootItemId item) => GameAPI.Current.CollectLootItem(item.Value, this);

	/// <summary>
	/// Sets the team or tint color of the unit's material.
	/// </summary>
	/// <param name="color">The RGB color vector.</param>
	public void SetColor(Vector3 color) => GameAPI.Current.SetUnitColor(this, color);

	/// <summary>
	/// Plays the specified skeletal or state animation on the unit model.
	/// </summary>
	/// <param name="animationName">The animation clip name.</param>
	public void SetAnimation(string animationName) => GameAPI.Current.SetUnitAnimation(this, animationName);

	/// <summary>
	/// Sets whether the unit is immune to spell effects and magic abilities.
	/// </summary>
	/// <param name="immune">True for spell immunity; otherwise false.</param>
	public void SetSpellImmune(bool immune) => GameAPI.Current.SetUnitSpellImmune(this, immune);

	/// <summary>
	/// Triggers an elastic deformation mesh impulse visual effect on the unit model.
	/// </summary>
	/// <param name="strength">The peak displacement amplitude of the impulse vibration.</param>
	/// <param name="duration">The total time in seconds over which the impulse decays.</param>
	/// <param name="frequency">The oscillatory speed of the displacement impulse.</param>
	public void TriggerMeshImpulse(float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f) =>
		GameAPI.Current.TriggerMeshImpulse(this, strength, duration, frequency);

	/// <summary>
	/// Kills the unit, executing its death sequence and optionally playing animations/shaders.
	/// </summary>
	/// <param name="executeDespawnShader">Whether to execute the despawn material dissolve shader.</param>
	/// <param name="playDeathAnimation">Whether to play the death skeletal animation.</param>
	public void Kill(bool executeDespawnShader = true, bool playDeathAnimation = true) =>
		GameAPI.Current.KillUnit(this, executeDespawnShader, playDeathAnimation);

	/// <summary>
	/// Instantly destroys the unit entity without death bounty awards.
	/// </summary>
	/// <param name="executeDespawnShader">Whether to execute the despawn material dissolve shader.</param>
	/// <param name="playDeathAnimation">Whether to play the death skeletal animation.</param>
	public void Destroy(bool executeDespawnShader = true, bool playDeathAnimation = true) =>
		GameAPI.Current.DestroyUnit(this, executeDespawnShader, playDeathAnimation);

	/// <summary>
	/// Formats the unit identifier as a string.
	/// </summary>
	/// <returns>A string representation of the unit identifier.</returns>
	public override string ToString() => Value.ToString();
}
