namespace Realm.Shared.Accounts;

/// <summary>
///     Why a name a seat stated was not usable as it stood, so it is cleaned and cut rather than carried verbatim. There is no
///     "already taken" or "looks like another name" case, because a name is Steam's persona and nothing claims it.
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

	NoLettersOrDigits = 7
}
