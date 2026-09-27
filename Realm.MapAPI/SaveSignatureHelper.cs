using System;
using System.Security.Cryptography;
using System.Text;

namespace Realm.MapAPI;

/// <summary>
/// Provides cryptographic signature calculation and verification utilities to protect save payloads against tampering.
/// </summary>
public static class SaveSignatureHelper
{
    private const string SaltPrefix = "Realm.ArcadeSave.v1:";

    /// <summary>
    /// Computes an HMAC-SHA256 hexadecimal signature bound to the specified map name, player name, schema version, and payload content.
    /// </summary>
    /// <param name="mapName">The name of the map associated with the save.</param>
    /// <param name="playerName">The player name or identifier bound to the save.</param>
    /// <param name="dataVersion">The schema version number.</param>
    /// <param name="payloadJson">The raw JSON string of the save payload.</param>
    /// <returns>A lowercase hexadecimal signature string.</returns>
    public static string ComputeSignature(string mapName, string playerName, int dataVersion, string payloadJson)
    {
        string keyString = $"{SaltPrefix}{mapName.Trim().ToLowerInvariant()}:{playerName.Trim().ToLowerInvariant()}";
        byte[] keyBytes = Encoding.UTF8.GetBytes(keyString);

        using var hmac = new HMACSHA256(keyBytes);
        string message = $"{dataVersion}:{payloadJson}";
        byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));

        return Convert.ToHexStringLower(hashBytes);
    }

    /// <summary>
    /// Verifies whether the provided signature matches the computed signature for the specified map name, player name, version, and payload content.
    /// </summary>
    /// <param name="mapName">The name of the map associated with the save.</param>
    /// <param name="playerName">The player name or identifier bound to the save.</param>
    /// <param name="dataVersion">The schema version number.</param>
    /// <param name="payloadJson">The raw JSON string of the save payload.</param>
    /// <param name="signature">The expected signature string to verify against.</param>
    /// <returns><see langword="true"/> if the signature is valid and untampered; otherwise, <see langword="false"/>.</returns>
    public static bool VerifySignature(string mapName, string playerName, int dataVersion, string payloadJson, string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        string expected = ComputeSignature(mapName, playerName, dataVersion, payloadJson);
        return string.Equals(expected, signature.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
