using System;

namespace Realm.Shared.Distribution;

/// <summary>
///     The proof a creator key presents when it claims a name. It binds the claimed name, the key that claims it and the
///     public profile the registration publishes, so a peer relaying a registration and anyone reading it in transit cannot
///     change the donation link or contact details that other nodes then display.
/// </summary>
public static class CreatorRegistrationProof
{
	public const string PayloadPrefix = "creator_registration";

	public static string BuildPayload(string username, string publicKey, string? donationLink, string? contactInfo)
	{
		return $"{PayloadPrefix}:{NormalizeValue(username)}:{NormalizeValue(publicKey)}:{NormalizeValue(donationLink)}:{NormalizeValue(contactInfo)}";
	}

	public static string CreateSignature(string privateKeyBase64, string username, string publicKey, string? donationLink, string? contactInfo)
	{
		return AuthorSignatureHelper.SignMessage(privateKeyBase64, BuildPayload(username, publicKey, donationLink, contactInfo));
	}

	public static bool Verify(string publicKeyBase64, string username, string? donationLink, string? contactInfo, string signature)
	{
		if (string.IsNullOrWhiteSpace(publicKeyBase64) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(signature)) return false;

		return AuthorSignatureHelper.VerifySignature(publicKeyBase64, BuildPayload(username, publicKeyBase64, donationLink, contactInfo), signature);
	}

	private static string NormalizeValue(string? value)
	{
		return value?.Trim() ?? string.Empty;
	}
}
