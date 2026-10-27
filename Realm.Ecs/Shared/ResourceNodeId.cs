namespace Realm.Ecs;

/// <summary>
/// Identifies a harvestable resource node by its index in the world's resource table.
/// </summary>
public readonly unsafe record struct ResourceNodeId(int Value)
{
	public static implicit operator ResourceNodeId(int value) => new(value);

	public static explicit operator int(ResourceNodeId id) => id.Value;

	public static readonly ResourceNodeId Invalid = new(-1);

	public bool IsValid => Value >= 0;

	public int Index => Value;

	public int Id => Value;

	public override string ToString() => Value.ToString();

	public float ResourceAmount
	{
		get => HandleCommands.Current?.GetResourceNodeAmount(this) ?? 0f;
		set => HandleCommands.Current?.SetResourceNodeAmount(this, value);
	}
}
