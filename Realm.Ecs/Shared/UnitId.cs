namespace Realm.Ecs;

/// <summary>
/// Identifies a unit by the entity index it occupies in the world.
/// </summary>
public readonly unsafe record struct UnitId(int Value)
{
	public static implicit operator UnitId(int value) => new(value);

	public static explicit operator int(UnitId id) => id.Value;

	public static readonly UnitId Invalid = new(0);

	public bool IsValid => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES;

	public int UniqueId => Value;

	public int Id => Value;

	public override string ToString() => Value.ToString();

	public float Health
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Health>(ComponentIds.Health, Value)->Current : 0f;
		set => HandleCommands.Current?.SetUnitHealth(this, value);
	}

	public float MaxHealth
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Health>(ComponentIds.Health, Value)->Max : 0f;
		set => HandleCommands.Current?.SetUnitMaxHealth(this, value);
	}

	public float Mana
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Mana>(ComponentIds.Mana, Value)->Current : 0f;
		set => HandleCommands.Current?.SetUnitMana(this, value);
	}

	public float MaxMana
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Mana>(ComponentIds.Mana, Value)->Max : 0f;
		set => HandleCommands.Current?.SetUnitMaxMana(this, value);
	}

	public float ManaRegen
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Mana>(ComponentIds.Mana, Value)->ManaRegen : 0f;
		set => HandleCommands.Current?.SetUnitManaRegen(this, value);
	}

	public float Damage
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Attack>(ComponentIds.Attack, Value)->Damage : 0f;
		set => HandleCommands.Current?.SetUnitDamage(this, value);
	}

	public float Range
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Attack>(ComponentIds.Attack, Value)->Range : 0f;
		set => HandleCommands.Current?.SetUnitRange(this, value);
	}

	public float Armor
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Resistances>(ComponentIds.Resistances, Value)->Physical : 0f;
		set => HandleCommands.Current?.SetUnitArmor(this, value);
	}

	public float Speed
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<MovementStats>(ComponentIds.MovementStats, Value)->Speed : 0f;
		set => HandleCommands.Current?.SetUnitSpeed(this, value);
	}

	public float AttackSpeed
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Attack>(ComponentIds.Attack, Value)->Cooldown : 0f;
		set => HandleCommands.Current?.SetUnitAttackSpeed(this, value);
	}

	public string Name
	{
		get => HandleCommands.Current?.GetUnitInstanceName(this) ?? string.Empty;
		set => HandleCommands.Current?.SetUnitName(this, value);
	}

	public int Player
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<UnitOwnerPlayer>(ComponentIds.UnitOwnerPlayer, Value)->PlayerIndex : -1;
		set => HandleCommands.Current?.SetUnitOwner(this, value);
	}

	public int Level
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Level>(ComponentIds.Level, Value)->Value : 0;
		set => HandleCommands.Current?.SetUnitLevel(this, value);
	}

	public float Experience
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<Experience>(ComponentIds.Experience, Value)->Value : 0f;
		set => HandleCommands.Current?.SetUnitExperience(this, value);
	}

	public float Scale
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<ModelScale>(ComponentIds.ModelScale, Value)->Value : 1f;
		set => HandleCommands.Current?.SetUnitScale(this, value);
	}

	public bool Invulnerable
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 && EcsSharedMemory.HasComponent(Value, ComponentIds.Invulnerable);
		set => HandleCommands.Current?.SetUnitInvulnerable(this, value);
	}

	public bool AnimationsFrozen
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 && EcsSharedMemory.HasComponent(Value, ComponentIds.AnimationsFrozen);
		set => HandleCommands.Current?.SetUnitAnimationsFrozen(this, value);
	}

	public bool CommandsFrozen
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 && EcsSharedMemory.HasComponent(Value, ComponentIds.CommandsFrozen);
		set => HandleCommands.Current?.SetUnitCommandsFrozen(this, value);
	}

	public bool Frozen
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0
			&& (EcsSharedMemory.HasComponent(Value, ComponentIds.CommandsFrozen) || EcsSharedMemory.HasComponent(Value, ComponentIds.AnimationsFrozen));
		set => HandleCommands.Current?.SetUnitFrozen(this, value);
	}

	public bool HideHealthAndManaBars
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 && EcsSharedMemory.HasComponent(Value, ComponentIds.OverheadBarsHidden);
		set => HandleCommands.Current?.SetUnitHideHealthAndManaBars(this, value);
	}

	public float Facing
	{
		get => Value > 0 && Value < EcsSharedMemory.MAX_ENTITIES && EcsSharedMemory.Alive[Value] != 0 ? EcsSharedMemory.GetComponent<RotationY>(ComponentIds.RotationY, Value)->Value : 0f;
		set => HandleCommands.Current?.SetUnitFacing(this, value);
	}
}
