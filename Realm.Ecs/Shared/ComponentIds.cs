namespace Realm.Ecs;

/// <summary>
/// Defines deterministic canonical component type identifiers agreed upon by both host and guest simulation runtimes.
/// </summary>
public static class ComponentIds
{
	/// <summary>Position component identifier.</summary>
	public const int Position = 0;
	/// <summary>Health component identifier.</summary>
	public const int Health = 1;
	/// <summary>Mana component identifier.</summary>
	public const int Mana = 3;
	/// <summary>Player population component identifier.</summary>
	public const int PlayerPopulation = 24;
	/// <summary>Script player component identifier.</summary>
	public const int ScriptPlayer = 31;
	/// <summary>Unit owner player component identifier.</summary>
	public const int UnitOwnerPlayer = 37;
	/// <summary>Attack component identifier.</summary>
	public const int Attack = 43;
	/// <summary>Resistances component identifier (armor points and magic resistance points).</summary>
	public const int Resistances = 48;
	/// <summary>Movement stats component identifier.</summary>
	public const int MovementStats = 57;
	/// <summary>Player resources component identifier.</summary>
	public const int PlayerResources = 69;
	/// <summary>Building tag component identifier.</summary>
	public const int Building = 92;
	/// <summary>Dead tag component identifier.</summary>
	public const int Dead = 95;
	/// <summary>Hero tag component identifier.</summary>
	public const int Hero = 96;
	/// <summary>Invulnerable tag component identifier.</summary>
	public const int Invulnerable = 97;
	/// <summary>Unit tag component identifier.</summary>
	public const int Unit = 103;
	/// <summary>Experience component identifier.</summary>
	public const int Experience = 104;
	/// <summary>Level component identifier.</summary>
	public const int Level = 106;
	/// <summary>Model scale component identifier.</summary>
	public const int ModelScale = 108;
	/// <summary>Rotation around Y-axis component identifier.</summary>
	public const int RotationY = 112;
	/// <summary>Previous position component identifier.</summary>
	public const int PreviousPosition = 113;
	/// <summary>Previous rotation around Y-axis component identifier.</summary>
	public const int PreviousRotationY = 114;
	/// <summary>Commands frozen tag component identifier, marking units that ignore movement and attack orders.</summary>
	public const int CommandsFrozen = 124;
	/// <summary>Animations frozen tag component identifier, marking units that hold their current animation pose.</summary>
	public const int AnimationsFrozen = 126;
	/// <summary>Frozen tag component identifier kept for maps that need the commands-frozen slot.</summary>
	public const int Frozen = 124;
	/// <summary>Overhead bars hidden tag component identifier.</summary>
	public const int OverheadBarsHidden = 125;
}
