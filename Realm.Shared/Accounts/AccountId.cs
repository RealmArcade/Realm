namespace Realm.Shared.Accounts;

/// <summary>
///     Identifies a person across every match, provider and session. It is minted once by the account authority, is positive,
///     and is never reused. It is deliberately unrelated to <c>Realm.Ecs.PlayerId</c>, which is only the 0-24 slot a player
///     occupies in one running match.
/// </summary>
public readonly record struct AccountId(long Value)
{
	public static AccountId None => new(0);

	public bool IsNone => Value == 0;

	public static implicit operator AccountId(long value) => new(value);

	public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

	/// <summary>
	///     The decimal text form used by every payload a browser can read, because a 64-bit integer is not representable
	///     exactly by JavaScript.
	/// </summary>
	public string ToDecimalString() => ToString();
}
