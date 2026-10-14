namespace Realm.Ecs.Common;

/// <summary>
///     A type-safe wrapper for a Tag ID.
/// </summary>
public readonly record struct TagId
{
	public TagId(string value)
	{
		Value = value;
	}

	public string Value { get; }
}