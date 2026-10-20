using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Represents a strongly-typed, unmanaged identifier for a harvestable resource node in the game world,
/// providing zero-allocation fluent access to resource node state and harvest capacity.
/// </summary>
public readonly record struct ResourceNodeId(int Value)
{
	/// <summary>
	/// Implicitly converts a 32-bit integer index into a <see cref="ResourceNodeId"/>.
	/// </summary>
	/// <param name="value">The zero-based resource node index.</param>
	public static implicit operator ResourceNodeId(int value) => new(value);

	/// <summary>
	/// Explicitly converts a <see cref="ResourceNodeId"/> into a 32-bit integer index.
	/// </summary>
	/// <param name="id">The resource node identifier.</param>
	public static explicit operator int(ResourceNodeId id) => id.Value;

	/// <summary>
	/// Gets an invalid or unassigned resource node identifier.
	/// </summary>
	public static readonly ResourceNodeId Invalid = new(-1);

	/// <summary>
	/// Gets a value indicating whether this identifier represents a valid resource node index.
	/// </summary>
	public bool IsValid => Value >= 0;

	/// <summary>
	/// Gets the zero-based index of this resource node.
	/// </summary>
	public int Index => Value;

	/// <summary>
	/// Gets the zero-based index of this resource node (alias for <see cref="Index"/>).
	/// </summary>
	public int Id => Value;

	/// <summary>
	/// Gets the resource category identifier provided by this node (e.g., "gold", "wood", "stone").
	/// </summary>
	public string ResourceType => GameAPI.Current.GetResourceNodeType(this);

	/// <summary>
	/// Gets the position of the resource node in 3D world space.
	/// </summary>
	public Vector3 Position => GameAPI.Current.GetResourceNodePosition(this);

	/// <summary>
	/// Gets or sets the remaining harvestable resource amount in this node.
	/// </summary>
	public float ResourceAmount
	{
		get => GameAPI.Current.GetResourceNodeAmount(this);
		set => GameAPI.Current.SetResourceNodeAmount(this, value);
	}

	/// <summary>
	/// Gets a value indicating whether the resource node is fully depleted of resources.
	/// </summary>
	public bool IsDepleted => GameAPI.Current.IsResourceNodeDepleted(this);

	/// <summary>
	/// Triggers an elastic deformation mesh impulse visual effect on the resource node model.
	/// </summary>
	/// <param name="strength">The peak displacement amplitude of the impulse vibration.</param>
	/// <param name="duration">The total time in seconds over which the impulse decays.</param>
	/// <param name="frequency">The oscillatory speed of the displacement impulse.</param>
	public void TriggerMeshImpulse(float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f) =>
		GameAPI.Current.TriggerResourceMeshImpulse(this, strength, duration, frequency);

	/// <summary>
	/// Formats the resource node identifier as a string.
	/// </summary>
	/// <returns>A string representation of the resource node index.</returns>
	public override string ToString() => Value.ToString();
}
