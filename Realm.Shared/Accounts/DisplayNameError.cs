namespace Realm.Shared.Accounts;

/// <summary>
///     Why a candidate DisplayName was refused. Each value maps to one translatable message, so a rejection never has to be
///     inferred from free text.
/// </summary>
public enum DisplayNameError
{
	None = 0,

	Empty = 1,

	TooShort = 2,

	TooLong = 3,

	TooManyBytes = 4,

	DisallowedCharacter = 5,

	CombiningMarkWithoutBase = 6,

	NoLettersOrDigits = 7,

	AlreadyTaken = 8,

	VisuallySimilar = 9
}
