using System;
using System.Globalization;

namespace Realm.Shared.Distribution;

/// <summary>
///     The proof an account presents when it takes a creator name that is already registered to a key. Signing it with the
///     registered private key is the only evidence that the account belongs to that creator, so the proof binds the account and
///     the canonical name being claimed and cannot be re-pointed at another account or another name.
/// </summary>
public static class CreatorNameClaimProof
{
	public const string PayloadPrefix = "creator_name_claim";

	public static string BuildPayload(long accountId, string canonicalUsername)
	{
		return $"{PayloadPrefix}:{accountId.ToString(CultureInfo.InvariantCulture)}:{(canonicalUsername ?? string.Empty).Trim()}";
	}

	public static string CreateSignature(string privateKeyBase64, long accountId, string canonicalUsername)
	{
		return AuthorSignatureHelper.SignMessage(privateKeyBase64, BuildPayload(accountId, canonicalUsername));
	}

	public static bool Verify(string publicKeyBase64, long accountId, string canonicalUsername, string signature)
	{
		if (string.IsNullOrWhiteSpace(publicKeyBase64) || string.IsNullOrWhiteSpace(canonicalUsername) || string.IsNullOrWhiteSpace(signature)) return false;

		return AuthorSignatureHelper.VerifySignature(publicKeyBase64, BuildPayload(accountId, canonicalUsername), signature);
	}
}
