using System.Collections.Generic;

namespace Realm.Shared.Accounts;

/// <summary>
///     The typed reason every account endpoint answers a refusal with. A code is stable, translatable and never contains a
///     secret, so the client can show the exact reason a name or a sign-in was refused.
/// </summary>
public static class AccountErrorCodes
{
	public const string DisplayNameEmpty = "display_name_empty";

	public const string DisplayNameTooShort = "display_name_too_short";

	public const string DisplayNameTooLong = "display_name_too_long";

	public const string DisplayNameTooManyBytes = "display_name_too_many_bytes";

	public const string DisplayNameDisallowedCharacter = "display_name_disallowed_character";

	public const string DisplayNameCombiningMarkWithoutBase = "display_name_combining_mark_without_base";

	public const string DisplayNameNeedsLetterOrDigit = "display_name_needs_letter_or_digit";

	public const string DisplayNameAlreadyTaken = "display_name_already_taken";

	public const string DisplayNameVisuallySimilar = "display_name_visually_similar";

	public const string SessionRequired = "session_required";

	public const string SessionInvalid = "session_invalid";

	public const string SessionExpired = "session_expired";

	public const string CredentialsInvalid = "credentials_invalid";

	public const string PasswordTooShort = "password_too_short";

	public const string Throttled = "auth_throttled";

	public const string AccountNotAuthority = "account_not_authority";

	public const string ProviderUnknown = "provider_unknown";

	public const string ProviderUnavailable = "provider_unavailable";

	public const string ProviderAlreadyLinked = "provider_already_linked";

	public const string ProviderLinkOwnedByAnotherAccount = "provider_link_owned_by_another_account";

	public const string AuthorizationStateInvalid = "auth_state_invalid";

	public const string AuthorizationStateExpired = "auth_state_expired";

	public const string AdminProofMalformed = "admin_proof_malformed";

	public const string AdminProofInvalid = "admin_proof_invalid";

	public const string AdminProofStale = "admin_proof_stale";

	public const string AdminProofReplayed = "admin_proof_replayed";

	public const string AccountNotFound = "account_not_found";

	public const string RequestMalformed = "request_malformed";

	private static readonly Dictionary<DisplayNameError, string> DisplayNameErrorCodes = new()
	{
		{ DisplayNameError.Empty, DisplayNameEmpty },
		{ DisplayNameError.TooShort, DisplayNameTooShort },
		{ DisplayNameError.TooLong, DisplayNameTooLong },
		{ DisplayNameError.TooManyBytes, DisplayNameTooManyBytes },
		{ DisplayNameError.DisallowedCharacter, DisplayNameDisallowedCharacter },
		{ DisplayNameError.CombiningMarkWithoutBase, DisplayNameCombiningMarkWithoutBase },
		{ DisplayNameError.NoLettersOrDigits, DisplayNameNeedsLetterOrDigit },
		{ DisplayNameError.AlreadyTaken, DisplayNameAlreadyTaken },
		{ DisplayNameError.VisuallySimilar, DisplayNameVisuallySimilar }
	};

	public static string ForDisplayNameError(DisplayNameError error)
	{
		return DisplayNameErrorCodes.TryGetValue(error, out string? code) ? code : RequestMalformed;
	}
}
