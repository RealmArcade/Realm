namespace Realm.Ecs;

/// <summary>
/// Identifies a dropped loot item by the entity index it occupies in the world.
/// </summary>
public readonly record struct LootItemId(int Value)
{
	/// <summary>
	/// Implicitly converts a 32-bit integer identifier into a <see cref="LootItemId"/>.
	/// </summary>
	/// <param name="value">The raw integer loot item identifier.</param>
	public static implicit operator LootItemId(int value) => new(value);

	/// <summary>
	/// Explicitly converts a <see cref="LootItemId"/> into a 32-bit integer identifier.
	/// </summary>
	/// <param name="id">The loot item identifier.</param>
	public static explicit operator int(LootItemId id) => id.Value;

	/// <summary>
	/// Gets an invalid or unassigned loot item identifier.
	/// </summary>
	public static readonly LootItemId Invalid = new(0);

	/// <summary>
	/// Gets a value indicating whether this identifier represents a valid loot item index.
	/// </summary>
	public bool IsValid => Value > 0;

	/// <summary>
	/// Gets the raw integer identifier.
	/// </summary>
	public int UniqueId => Value;

	/// <summary>
	/// Gets the raw integer identifier (alias for <see cref="UniqueId"/>).
	/// </summary>
	public int Id => Value;

	/// <summary>
	/// Formats the loot item identifier as a string.
	/// </summary>
	/// <returns>A string representation of the loot item identifier.</returns>
	public override string ToString() => Value.ToString();
}
