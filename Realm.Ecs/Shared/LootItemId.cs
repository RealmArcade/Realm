namespace Realm.Ecs;

/// <summary>
/// Identifies a dropped loot item by the entity index it occupies in the world.
/// </summary>
public readonly record struct LootItemId(int Value)
{
	public static implicit operator LootItemId(int value) => new(value);

	public static explicit operator int(LootItemId id) => id.Value;

	public static readonly LootItemId Invalid = new(0);

	public bool IsValid => Value > 0;

	public int UniqueId => Value;

	public int Id => Value;

	public override string ToString() => Value.ToString();
}
