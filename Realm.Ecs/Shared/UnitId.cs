namespace Realm.Ecs;

/// <summary>
/// Identifies a unit by the entity index it occupies in the world.
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
	/// Formats the unit identifier as a string.
	/// </summary>
	/// <returns>A string representation of the unit identifier.</returns>
	public override string ToString() => Value.ToString();

	/// <summary>
	/// Gets or sets the current health point value of the unit.
	/// </summary>
	public float Health
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Health>(ComponentIds.Health, Value)->Current : 0f;
		set => HandleCommands.Current?.SetUnitHealth(this, value);
	}

	/// <summary>
	/// Gets or sets the maximum health point capacity of the unit.
	/// </summary>
	public float MaxHealth
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Health>(ComponentIds.Health, Value)->Max : 0f;
		set => HandleCommands.Current?.SetUnitMaxHealth(this, value);
	}

	/// <summary>
	/// Gets or sets the current mana points of the unit.
	/// </summary>
	public float Mana
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Mana>(ComponentIds.Mana, Value)->Current : 0f;
		set => HandleCommands.Current?.SetUnitMana(this, value);
	}

	/// <summary>
	/// Gets or sets the maximum mana capacity of the unit.
	/// </summary>
	public float MaxMana
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Mana>(ComponentIds.Mana, Value)->Max : 0f;
		set => HandleCommands.Current?.SetUnitMaxMana(this, value);
	}

	/// <summary>
	/// Gets or sets the mana regeneration rate of the unit per second.
	/// </summary>
	public float ManaRegen
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Mana>(ComponentIds.Mana, Value)->ManaRegen : 0f;
		set => HandleCommands.Current?.SetUnitManaRegen(this, value);
	}

	/// <summary>
	/// Gets or sets the base attack damage of the unit.
	/// </summary>
	public float Damage
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Attack>(ComponentIds.Attack, Value)->Damage : 0f;
		set => HandleCommands.Current?.SetUnitDamage(this, value);
	}

	/// <summary>
	/// Gets or sets the attack range of the unit.
	/// </summary>
	public float Range
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Attack>(ComponentIds.Attack, Value)->Range : 0f;
		set => HandleCommands.Current?.SetUnitRange(this, value);
	}

	/// <summary>
	/// Gets or sets the armor points of the unit. Armor mitigates physical damage only.
	/// </summary>
	public float Armor
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Resistances>(ComponentIds.Resistances, Value)->Physical : 0f;
		set => HandleCommands.Current?.SetUnitArmor(this, value);
	}

	/// <summary>
	/// Gets or sets the movement speed of the unit.
	/// </summary>
	public float Speed
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<MovementStats>(ComponentIds.MovementStats, Value)->Speed : 0f;
		set => HandleCommands.Current?.SetUnitSpeed(this, value);
	}

	/// <summary>
	/// Gets or sets the attack speed multiplier of the unit.
	/// </summary>
	public float AttackSpeed
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Attack>(ComponentIds.Attack, Value)->Cooldown : 0f;
		set => HandleCommands.Current?.SetUnitAttackSpeed(this, value);
	}

	/// <summary>
	/// Gets or sets the display name of the unit.
	/// </summary>
	public string Name
	{
		get => HandleCommands.Current?.GetUnitInstanceName(this) ?? string.Empty;
		set => HandleCommands.Current?.SetUnitName(this, value);
	}

	/// <summary>
	/// Gets or sets the zero-based player slot index that owns this unit.
	/// </summary>
	public int Player
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<UnitOwnerPlayer>(ComponentIds.UnitOwnerPlayer, Value)->PlayerIndex : -1;
		set => HandleCommands.Current?.SetUnitOwner(this, value);
	}

	/// <summary>
	/// Gets or sets the experience level of the unit.
	/// </summary>
	public int Level
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Level>(ComponentIds.Level, Value)->Value : 0;
		set => HandleCommands.Current?.SetUnitLevel(this, value);
	}

	/// <summary>
	/// Gets or sets the experience points accumulated by the hero unit.
	/// </summary>
	public float Experience
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Experience>(ComponentIds.Experience, Value)->Value : 0f;
		set => HandleCommands.Current?.SetUnitExperience(this, value);
	}

	/// <summary>
	/// Gets or sets the visual scaling factor of the unit model.
	/// </summary>
	public float Scale
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<ModelScale>(ComponentIds.ModelScale, Value)->Value : 1f;
		set => HandleCommands.Current?.SetUnitScale(this, value);
	}

	/// <summary>
	/// Gets or sets a value indicating whether the unit is invulnerable to all damage.
	/// </summary>
	public bool Invulnerable
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 && EcsSharedMemory.HasComponent(Value, ComponentIds.Invulnerable);
		set => HandleCommands.Current?.SetUnitInvulnerable(this, value);
	}

	/// <summary>
	/// Gets or sets a value indicating whether the unit holds its current animation pose.
	/// </summary>
	/// <remarks>
	/// An animations-frozen unit plays no locomotion, attack, idle or death animation, including
	/// procedural sway, but it can still receive movement, attack and ability orders. Combine it with
	/// <see cref="CommandsFrozen"/> to pin a unit in place, or use <see cref="Frozen"/> to set both.
	/// </remarks>
	public bool AnimationsFrozen
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 && EcsSharedMemory.HasComponent(Value, ComponentIds.AnimationsFrozen);
		set => HandleCommands.Current?.SetUnitAnimationsFrozen(this, value);
	}

	/// <summary>
	/// Gets or sets a value indicating whether the unit ignores movement, attack and hold orders.
	/// </summary>
	/// <remarks>
	/// A commands-frozen unit keeps its current position, does not acquire targets and does not respond
	/// to move, stop, hold or attack commands, and clients hide those command buttons for it. Ability
	/// casting and ability buttons stay available, so map-authored abilities such as rerolls keep
	/// working. Setting the property to the value it already has does nothing.
	/// </remarks>
	public bool CommandsFrozen
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 && EcsSharedMemory.HasComponent(Value, ComponentIds.CommandsFrozen);
		set => HandleCommands.Current?.SetUnitCommandsFrozen(this, value);
	}

	/// <summary>
	/// Gets or sets a value indicating whether the unit is frozen in place with its animation suspended.
	/// </summary>
	/// <remarks>
	/// This sets both <see cref="AnimationsFrozen"/> and <see cref="CommandsFrozen"/>. A frozen unit keeps
	/// its current position and pose and ignores orders, and it does not acquire targets. Setting the
	/// property to the value it already has does nothing. The flag lives on the entity, so it survives
	/// level changes, element swaps and unit swaps until the entity is destroyed or the flag is cleared,
	/// and it does not conflict with <see cref="Invulnerable"/>, <see cref="Facing"/> or
	/// <see cref="HideHealthAndManaBars"/>.
	/// </remarks>
	public bool Frozen
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0
			&& (EcsSharedMemory.HasComponent(Value, ComponentIds.CommandsFrozen) || EcsSharedMemory.HasComponent(Value, ComponentIds.AnimationsFrozen));
		set => HandleCommands.Current?.SetUnitFrozen(this, value);
	}

	/// <summary>
	/// Gets or sets a value indicating whether overhead health and mana bars are hidden for this unit.
	/// </summary>
	public bool HideHealthAndManaBars
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 && EcsSharedMemory.HasComponent(Value, ComponentIds.OverheadBarsHidden);
		set => HandleCommands.Current?.SetUnitHideHealthAndManaBars(this, value);
	}

	/// <summary>
	/// Gets or sets the facing orientation of the unit in radians.
	/// </summary>
	public float Facing
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<RotationY>(ComponentIds.RotationY, Value)->Value : 0f;
		set => HandleCommands.Current?.SetUnitFacing(this, value);
	}
}
