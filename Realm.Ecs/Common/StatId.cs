namespace Realm.Ecs.Common;

/// <summary>
///     A type-safe wrapper for a Stat ID.
/// </summary>
public readonly record struct StatId
{
	public StatId(string value)
	{
		Value = value;
	}

	public string Value { get; }
}