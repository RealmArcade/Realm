using System;

namespace Realm.Shared.Distribution;

/// <summary>
///     The signed proof an administrator presents to rename any account. It reuses the Ed25519 admin key material the rest of
///     the distribution surface already trusts, and binds the new name to one account and one nonce so a captured proof cannot
///     be re-pointed or replayed.
/// </summary>
public static class AccountAdminRenameProof
{
	/// <summary>
	///     How far the signed timestamp may sit from the authority's clock, in either direction.
	/// </summary>
	public const int MaximumClockSkewSeconds = 300;

	public static string BuildPayload(long accountId, string canonicalDisplayName, string nonce, long timestampUnixSeconds)
	{
		return $"account_rename:{accountId.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{canonicalDisplayName}:{nonce}:{timestampUnixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
	}

	public static string CreateSignature(string adminPrivateKeyBase64, long accountId, string canonicalDisplayName, string nonce, long timestampUnixSeconds)
	{
		string payload = BuildPayload(accountId, canonicalDisplayName, nonce, timestampUnixSeconds);
		return AuthorSignatureHelper.SignMessage(adminPrivateKeyBase64, payload);
	}

	public static bool IsTimestampFresh(long timestampUnixSeconds, DateTime utcNow)
	{
		long now = new DateTimeOffset(utcNow, TimeSpan.Zero).ToUnixTimeSeconds();
		return Math.Abs(now - timestampUnixSeconds) <= MaximumClockSkewSeconds;
	}

	public static bool Verify(string adminPublicKeyBase64, long accountId, string canonicalDisplayName, string nonce, long timestampUnixSeconds, string signature)
	{
		if (string.IsNullOrWhiteSpace(adminPublicKeyBase64) || string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(signature)) return false;

		string payload = BuildPayload(accountId, canonicalDisplayName, nonce, timestampUnixSeconds);
		return AuthorSignatureHelper.VerifySignature(adminPublicKeyBase64, payload, signature);
	}

	/// <summary>
	///     Verifies against the configured admin keys. A presented key is only honoured when it is one of them, so a caller cannot
	///     introduce a key of their own and sign their way past the check.
	/// </summary>
	public static bool VerifyAny(System.Collections.Generic.IEnumerable<string> adminPublicKeys, string presentedPublicKeyBase64, long accountId, string canonicalDisplayName, string nonce, long timestampUnixSeconds, string signature)
	{
		if (!string.IsNullOrWhiteSpace(presentedPublicKeyBase64))
		{
			string presented = presentedPublicKeyBase64.Trim();
			foreach (string adminPublicKey in adminPublicKeys)
			{
				if (!string.Equals(adminPublicKey.Trim(), presented, StringComparison.OrdinalIgnoreCase)) continue;
				return Verify(presented, accountId, canonicalDisplayName, nonce, timestampUnixSeconds, signature);
			}

			return false;
		}

		foreach (string adminPublicKey in adminPublicKeys)
		{
			if (Verify(adminPublicKey, accountId, canonicalDisplayName, nonce, timestampUnixSeconds, signature)) return true;
		}

		return false;
	}
}
