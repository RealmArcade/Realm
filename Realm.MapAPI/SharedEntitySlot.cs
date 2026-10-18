using System.Numerics;
using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Fixed-layout, blittable structure representing shared entity component state in WebAssembly linear memory.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct SharedEntitySlot
{
	/// <summary>
	/// Indicates whether this entity slot is alive and active (1 if alive, 0 if dead or unassigned).
	/// </summary>
	public byte Alive;

	/// <summary>
	/// Indicates whether this entity is a structural building (1 if building, 0 otherwise).
	/// </summary>
	public byte IsBuilding;

	/// <summary>
	/// Indicates whether this entity is a hero unit (1 if hero, 0 otherwise).
	/// </summary>
	public byte IsHero;

	/// <summary>
	/// Indicates whether this entity is invulnerable to damage (1 if invulnerable, 0 otherwise).
	/// </summary>
	public byte Invulnerable;

	/// <summary>
	/// The zero-based player slot index that owns this entity.
	/// </summary>
	public int OwnerPlayerId;

	/// <summary>
	/// The team index assigned to this entity.
	/// </summary>
	public int Team;

	/// <summary>
	/// The current health point value of the entity.
	/// </summary>
	public float Health;

	/// <summary>
	/// The maximum health capacity of the entity.
	/// </summary>
	public float MaxHealth;

	/// <summary>
	/// The current mana points of the entity.
	/// </summary>
	public float Mana;

	/// <summary>
	/// The maximum mana capacity of the entity.
	/// </summary>
	public float MaxMana;

	/// <summary>
	/// The mana regeneration rate of the entity per second.
	/// </summary>
	public float ManaRegen;

	/// <summary>
	/// The base attack damage of the entity.
	/// </summary>
	public float Damage;

	/// <summary>
	/// The attack range of the entity.
	/// </summary>
	public float Range;

	/// <summary>
	/// The armor rating of the entity.
	/// </summary>
	public float Armor;

	/// <summary>
	/// The movement speed of the entity.
	/// </summary>
	public float Speed;

	/// <summary>
	/// The attack speed multiplier of the entity.
	/// </summary>
	public float AttackSpeed;

	/// <summary>
	/// The 3D position vector of the entity in world space coordinates.
	/// </summary>
	public Vector3 Position;

	/// <summary>
	/// The visual model scaling factor of the entity.
	/// </summary>
	public float Scale;

	/// <summary>
	/// The facing orientation angle of the entity in radians.
	/// </summary>
	public float Facing;

	/// <summary>
	/// The experience level of the entity.
	/// </summary>
	public int Level;

	/// <summary>
	/// The total experience points accumulated by the entity.
	/// </summary>
	public float Experience;

	/// <summary>
	/// The experience point bounty awarded upon defeating this entity.
	/// </summary>
	public float XpBounty;

	/// <summary>
	/// The gold resource bounty awarded upon defeating this entity.
	/// </summary>
	public float GoldBounty;
}
