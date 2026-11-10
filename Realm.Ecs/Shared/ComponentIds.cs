namespace Realm.Ecs;

/// <summary>
/// Defines deterministic canonical component type identifiers agreed upon by both host and guest simulation runtimes.
/// </summary>
/// <remarks>
///     A map script reads its world through shared linear memory, where a component array is found by the directory entry
///     carrying one of these identifiers and a tag is found by this bit of the entity's component mask. The host sets that
///     mask bit from <c>ComponentType&lt;T&gt;.Id</c>, so every identifier here has to be the identifier the canonical
///     registry assigns to that type; <c>ComponentTypeRegistry</c> refuses to start when one of them has drifted.
/// </remarks>
public static class ComponentIds
{
	/// <summary>Position component identifier.</summary>
	public const int Position = 0;
	/// <summary>Health component identifier.</summary>
	public const int Health = 2;
	/// <summary>Mana component identifier.</summary>
	public const int Mana = 4;
	/// <summary>Player population component identifier.</summary>
	public const int PlayerPopulation = 26;
	/// <summary>Script player component identifier.</summary>
	public const int ScriptPlayer = 33;
	/// <summary>Unit owner player component identifier.</summary>
	public const int UnitOwnerPlayer = 39;
	/// <summary>Attack component identifier.</summary>
	public const int Attack = 45;
	/// <summary>Resistances component identifier (armor points and magic resistance points).</summary>
	public const int Resistances = 50;
	/// <summary>Movement stats component identifier.</summary>
	public const int MovementStats = 59;
	/// <summary>Player resources component identifier.</summary>
	public const int PlayerResources = 71;
	/// <summary>Building tag component identifier.</summary>
	public const int Building = 94;
	/// <summary>Dead tag component identifier.</summary>
	public const int Dead = 97;
	/// <summary>Hero tag component identifier.</summary>
	public const int Hero = 98;
	/// <summary>Invulnerable tag component identifier.</summary>
	public const int Invulnerable = 99;
	/// <summary>Unit tag component identifier.</summary>
	public const int Unit = 105;
	/// <summary>Experience component identifier.</summary>
	public const int Experience = 106;
	/// <summary>Level component identifier.</summary>
	public const int Level = 108;
	/// <summary>Model scale component identifier.</summary>
	public const int ModelScale = 110;
	/// <summary>Rotation around Y-axis component identifier.</summary>
	public const int RotationY = 114;
	/// <summary>Previous position component identifier.</summary>
	public const int PreviousPosition = 1;
	/// <summary>Previous rotation around Y-axis component identifier.</summary>
	public const int PreviousRotationY = 115;
	/// <summary>Commands frozen tag component identifier, marking units that ignore movement and attack orders.</summary>
	public const int CommandsFrozen = 125;
	/// <summary>Animations frozen tag component identifier, marking units that hold their current animation pose.</summary>
	public const int AnimationsFrozen = 127;
	/// <summary>Frozen tag component identifier kept for maps that need the commands-frozen slot.</summary>
	public const int Frozen = 125;
	/// <summary>Overhead bars hidden tag component identifier.</summary>
	public const int OverheadBarsHidden = 126;
	/// <summary>Unit charges component identifier, holding the charges a unit keeps and the time to regain one.</summary>
	public const int UnitCharges = 133;
	/// <summary>Player damage effectiveness component identifier, holding the multiplier a seat's damage carries against each other seat.</summary>
	public const int PlayerDamageEffectiveness = 134;
}
