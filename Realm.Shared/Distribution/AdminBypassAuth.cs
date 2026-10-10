namespace Realm.Shared.Distribution;

public static class AdminBypassAuth
{
    public static (string PrivateKeyBase64, string PublicKeyBase64) GenerateAdminKeyPair()
    {
        return AuthorSignatureHelper.GenerateKeyPair();
    }

    public static string CreateBypassToken(string adminPrivateKeyBase64, string mapTitle, string mapVersion)
    {
        long timestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string payload = $"{mapTitle.Trim().ToLowerInvariant()}:{mapVersion.Trim().ToLowerInvariant()}:{timestampUnixSeconds}";
        string signature = AuthorSignatureHelper.SignMessage(adminPrivateKeyBase64, payload);
        return $"{payload}|{signature}";
    }

    public static bool VerifyBypassToken(string adminPublicKeyBase64, string mapTitle, string mapVersion, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(adminPublicKeyBase64))
        {
            return false;
        }

        string[] parts = token.Split('|');
        if (parts.Length != 2)
        {
            return false;
        }

        string payload = parts[0];
        string signature = parts[1];

        string[] payloadParts = payload.Split(':');
        if (payloadParts.Length != 3)
        {
            return false;
        }

        string tokenMapTitle = payloadParts[0];
        string tokenMapVersion = payloadParts[1];
        if (!long.TryParse(payloadParts[2], out long tokenTimestamp))
        {
            return false;
        }

        if (!string.Equals(tokenMapTitle, mapTitle.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(tokenMapVersion, mapVersion.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        long currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(currentTimestamp - tokenTimestamp) > 86400)
        {
            return false;
        }

        return AuthorSignatureHelper.VerifySignature(adminPublicKeyBase64, payload, signature);
    }

    public static bool VerifyBypassToken(System.Collections.Generic.IEnumerable<string> adminPublicKeys, string mapTitle, string mapVersion, string? token)
    {
        if (adminPublicKeys == null || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        foreach (var adminPublicKey in adminPublicKeys)
        {
            if (!string.IsNullOrWhiteSpace(adminPublicKey) && VerifyBypassToken(adminPublicKey, mapTitle, mapVersion, token))
            {
                return true;
            }
        }

        return false;
    }
}
