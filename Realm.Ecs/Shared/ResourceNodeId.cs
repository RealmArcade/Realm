namespace Realm.Ecs;

/// <summary>
/// Identifies a harvestable resource node by its index in the world's resource table.
/// </summary>
public readonly unsafe record struct ResourceNodeId(int Value)
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
	/// Formats the resource node identifier as a string.
	/// </summary>
	/// <returns>A string representation of the resource node index.</returns>
	public override string ToString() => Value.ToString();

	/// <summary>
	/// Gets or sets the remaining harvestable resource amount in this node.
	/// </summary>
	public float ResourceAmount
	{
		get => HandleCommands.Current?.GetResourceNodeAmount(this) ?? 0f;
		set => HandleCommands.Current?.SetResourceNodeAmount(this, value);
	}
}
