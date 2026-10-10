using NSec.Cryptography;
using Realm.Shared.Metadata;
using Realm.Shared.Services;
using System.Text;
using System.Text.Json.Nodes;

namespace Realm.Shared.Distribution;

public static class AuthorSignatureHelper
{
    private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;

    public static (string PrivateKeyBase64, string PublicKeyBase64) GenerateKeyPair()
    {
        var creationParameters = new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        };

        using var key = Key.Create(Algorithm, creationParameters);
        byte[] privateBytes = key.Export(KeyBlobFormat.RawPrivateKey);
        byte[] publicBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        return (Convert.ToBase64String(privateBytes), Convert.ToBase64String(publicBytes));
    }

    public static string GetPublicKey(string privateKeyBase64)
    {
        byte[] privateBytes = Convert.FromBase64String(privateKeyBase64);
        using var key = Key.Import(Algorithm, privateBytes, KeyBlobFormat.RawPrivateKey, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        byte[] publicBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        return Convert.ToBase64String(publicBytes);
    }

    public static string SignMessage(string privateKeyBase64, string message)
    {
        byte[] privateBytes = Convert.FromBase64String(privateKeyBase64);
        using var key = Key.Import(Algorithm, privateBytes, KeyBlobFormat.RawPrivateKey);
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        byte[] signatureBytes = Algorithm.Sign(key, messageBytes);
        return Convert.ToBase64String(signatureBytes);
    }

    public static bool VerifySignature(string publicKeyBase64, string message, string signatureBase64)
    {
        try
        {
            byte[] publicBytes = Convert.FromBase64String(publicKeyBase64);
            byte[] signatureBytes = Convert.FromBase64String(signatureBase64);
            var publicKey = PublicKey.Import(Algorithm, publicBytes, KeyBlobFormat.RawPublicKey);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            return Algorithm.Verify(publicKey, messageBytes, signatureBytes);
        }
        catch
        {
            return false;
        }
    }

    public static bool VerifySignatureAny(IEnumerable<string> publicKeys, string message, string signatureBase64)
    {
        if (publicKeys == null || string.IsNullOrWhiteSpace(signatureBase64))
        {
            return false;
        }

        foreach (var key in publicKeys)
        {
            if (!string.IsNullOrWhiteSpace(key) && VerifySignature(key, message, signatureBase64))
            {
                return true;
            }
        }

        return false;
    }

    public static string MergeMetadataHeaders(string? existingMetadataJson, string incomingMetadataJson, bool isAuthorizedOverwrite)
    {
        if (string.IsNullOrWhiteSpace(existingMetadataJson) || isAuthorizedOverwrite)
        {
            var incoming = MapFileService.LoadMetadataFromJson(incomingMetadataJson);
            return MapFileService.SaveMetadataToJson(incoming);
        }

        try
        {
            var existing = MapFileService.LoadMetadataFromJson(existingMetadataJson);
            var incoming = MapFileService.LoadMetadataFromJson(incomingMetadataJson);

            MergeMapProperties(existing, incoming);

            if (!string.IsNullOrEmpty(incoming.GameBuildNumber))
            {
                existing.GameBuildNumber = incoming.GameBuildNumber;
            }

            MergeDependencies(existing, incoming);

            return MapFileService.SaveMetadataToJson(existing);
        }
        catch
        {
            return MapFileService.SaveMetadataToJson(MapFileService.LoadMetadataFromJson(existingMetadataJson));
        }
    }

    private static void MergeMapProperties(MapMetadata existing, MapMetadata incoming)
    {
        if (incoming.MapProperties == null)
        {
            return;
        }

        existing.MapProperties ??= new MapInfoMetadata();
        if (!string.IsNullOrEmpty(incoming.MapProperties.MapName)) existing.MapProperties.MapName = incoming.MapProperties.MapName;
        if (!string.IsNullOrEmpty(incoming.MapProperties.MapDescription)) existing.MapProperties.MapDescription = incoming.MapProperties.MapDescription;
        if (!string.IsNullOrEmpty(incoming.MapProperties.Author)) existing.MapProperties.Author = incoming.MapProperties.Author;
        if (!string.IsNullOrEmpty(incoming.MapProperties.Version)) existing.MapProperties.Version = incoming.MapProperties.Version;
    }

    private static void MergeDependencies(MapMetadata existing, MapMetadata incoming)
    {
        if (incoming.Dependencies == null)
        {
            return;
        }

        existing.Dependencies ??= new List<MapDependencyMetadata>();
        foreach (var dep in incoming.Dependencies)
        {
            if (!existing.Dependencies.Any(d => string.Equals(d.Id, dep.Id, StringComparison.OrdinalIgnoreCase)))
            {
                existing.Dependencies.Add(dep);
            }
        }
    }

    public static string CanonicalizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node == null)
            {
                return json;
            }

            var sortedNode = SortJsonNode(node);
            return sortedNode.ToJsonString();
        }
        catch
        {
            return json;
        }
    }

    private static JsonNode SortJsonNode(JsonNode node)
    {
        return node switch
        {
            JsonObject obj => SortJsonObject(obj),
            JsonArray arr => SortJsonArray(arr),
            _ => node.DeepClone()
        };
    }

    private static JsonObject SortJsonObject(JsonObject obj)
    {
        var sortedObj = new JsonObject();
        foreach (var kvp in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sortedObj[kvp.Key] = kvp.Value != null ? SortJsonNode(kvp.Value.DeepClone()) : null;
        }
        return sortedObj;
    }

    private static JsonArray SortJsonArray(JsonArray arr)
    {
        bool allPrimitives = arr.All(item => item is JsonValue);
        return allPrimitives ? SortPrimitiveArray(arr) : SortComplexArray(arr);
    }

    private static JsonArray SortPrimitiveArray(JsonArray arr)
    {
        var sortedItems = arr
            .Select(item => item?.DeepClone())
            .OrderBy(item => item?.ToJsonString(), StringComparer.Ordinal)
            .ToList();
            
        var sortedArr = new JsonArray();
        foreach (var item in sortedItems)
        {
            sortedArr.Add(item);
        }
        return sortedArr;
    }

    private static JsonArray SortComplexArray(JsonArray arr)
    {
        var sortedArr = new JsonArray();
        foreach (var item in arr)
        {
            sortedArr.Add(item != null ? SortJsonNode(item.DeepClone()) : null);
        }
        return sortedArr;
    }
}
