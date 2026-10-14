namespace Realm.Ecs.Common;

/// <summary>
///     A type-safe wrapper for a Resource ID.
/// </summary>
public readonly record struct ResourceId
{
	public ResourceId(string value)
	{
		Value = value;
	}

	public string Value { get; }
}