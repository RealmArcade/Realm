using Blake3;
using Realm.Shared.Animation;
using Realm.Shared.Audio;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static System.Net.Mime.MediaTypeNames;

namespace Realm.Shared.Metadata;

public static class RealmMetadataHelper
{
    public const string AssetAgreementWarning = "The Realm Platform UGC Agreement states that files cannot be used outside the Realm Platform unless you are the original author of the asset. Do you understand?";
    public const string MixamoLicensingWarning = "Export not available due to licensing. Download animations from https://www.mixamo.com";

    public static bool SupportsMetadata(string extensionOrPath)
    {
        string ext = Path.GetExtension(extensionOrPath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) && extensionOrPath.StartsWith('.')) ext = extensionOrPath.ToLowerInvariant();
        if (ext is ".rtex" or ".ranim" or ".rmesh" or ".raud" or ".rkey") return true;

        if (!File.Exists(extensionOrPath)) return false;

        return CheckFileMagicBytes(extensionOrPath);
    }

    private static bool CheckFileMagicBytes(string filePath)
    {
        try
        {
            Span<byte> magic = stackalloc byte[4];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Read(magic) != 4) return false;

            return magic.SequenceEqual(RmeshFile.MagicBytes) ||
                magic.SequenceEqual(Realm.Shared.Textures.RtexFile.MagicBytes) ||
                magic.SequenceEqual(RanimFile.MagicBytes) ||
                magic.SequenceEqual(RaudFile.MagicBytes) ||
                magic.SequenceEqual(RkeyFile.MagicBytes);
        }
        catch { return false; }
    }

    public static string? ExtractMetadata(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string? meta = ext switch
        {
            ".rmesh" => ExtractMetadataFromRmesh(filePath),
            ".rtex" => ExtractMetadataFromRtex(filePath),
            ".ranim" => ExtractMetadataFromRanim(filePath),
            ".raud" => ExtractMetadataFromRaud(filePath),
            ".rkey" => ExtractMetadataFromRkey(filePath),
            _ => null
        };
        if (meta != null) return meta;

        return ExtractMetadataFromMagicBytes(filePath);
    }

    private static string? ExtractMetadataFromMagicBytes(string filePath)
    {
        try
        {
            Span<byte> magic = stackalloc byte[4];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Read(magic) != 4) return null;

            if (magic.SequenceEqual(RmeshFile.MagicBytes)) return ExtractMetadataFromRmesh(filePath);
            if (magic.SequenceEqual(Realm.Shared.Textures.RtexFile.MagicBytes)) return ExtractMetadataFromRtex(filePath);
            if (magic.SequenceEqual(RanimFile.MagicBytes)) return ExtractMetadataFromRanim(filePath);
            if (magic.SequenceEqual(RaudFile.MagicBytes)) return ExtractMetadataFromRaud(filePath);
            if (magic.SequenceEqual(RkeyFile.MagicBytes)) return ExtractMetadataFromRkey(filePath);
        }
        catch { }
        return null;
    }

    public static bool HasRealmMetadata(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        try
        {
            string? meta = ExtractMetadata(filePath);
            return !string.IsNullOrWhiteSpace(meta);
        }
        catch
        {
            return false;
        }
    }

    public static bool EnsureMetadata(string filePath, string? defaultMetadataJson = null)
    {
        if (!File.Exists(filePath)) return false;
        if (HasRealmMetadata(filePath)) return true;

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string canonicalBlake3 = ComputeBlake3(filePath);

        JsonObject metaObj = ParseOrCreateMetadata(defaultMetadataJson, ext);
        EnsureIsCompressedProperty(metaObj, ext);
        metaObj["blake3"] = canonicalBlake3;

        try
        {
            return AddMetadata(filePath, metaObj.ToJsonString());
        }
        catch
        {
            return false;
        }
    }

    public static JsonObject ParseOrCreateMetadata(string? existingMetadataJson, string ext)
    {
        if (!string.IsNullOrWhiteSpace(existingMetadataJson))
        {
            try
            {
                return JsonNode.Parse(existingMetadataJson)?.AsObject() ?? new JsonObject();
            }
            catch { }
        }

        return new JsonObject
        {
            ["created_utc"] = DateTime.UtcNow.ToString("O"),
            ["format"] = ext.TrimStart('.')
        };
    }

    private static void EnsureIsCompressedProperty(JsonObject metaObj, string ext)
    {
        if (metaObj.ContainsKey("is_compressed")) return;

        if (ext is ".rmesh" or ".ranim")
        {
            metaObj["is_compressed"] = true;
        }
        else if (ext is ".rtex" or ".raud")
        {
            metaObj["is_compressed"] = false;
        }
    }

    private static readonly Dictionary<string, ReadOnlySet<string>> ValidAssetTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".rtex"] = (new[] { "Decal", "Icon", "Noise", "Ribbon", "Skybox", "Spritesheet", "Terrain", "vfx_radial", "vfx_vertical" }).Select(NormalizeAssetType).ToHashSet<string>(StringComparer.InvariantCulture).AsReadOnly(),
        [".rmesh"] = (new[] { "Character", "Building", "Prop", "Item" }).Select(NormalizeAssetType).ToHashSet<string>(StringComparer.InvariantCulture).AsReadOnly(),
        [".ranim"] = (new[] { "Animation" }).Select(NormalizeAssetType).ToHashSet<string>(StringComparer.InvariantCulture).AsReadOnly(),
        [".raud"] = (new[] { "Music", "SoundEffect" }).Select(NormalizeAssetType).ToHashSet<string>(StringComparer.InvariantCulture).AsReadOnly(),
        [".gdshader"] = (new[] { "Shader" }).Select(NormalizeAssetType).ToHashSet<string>(StringComparer.InvariantCulture).AsReadOnly()
    };

    private static ReadOnlySet<string> ValidAssetTypes = ValidAssetTypesByExtension.Values.SelectMany(x => x).ToHashSet(StringComparer.OrdinalIgnoreCase).AsReadOnly();

    public static string NormalizeAssetType(string? assetType)
    {
        return (assetType ?? "").Trim().ToLowerInvariant().Replace("_", "");
    }

    public static string GetCanonicalType(string? assetType)
    {
        var result = NormalizeAssetType(assetType);

        if (TryStripSuffix(result, out var strippedType))
        {
            return strippedType;
        }

        result = MapLegacyAssetType(result);

        if (!ValidAssetTypes.Contains(result))
        {
            return "";
        }

        return NormalizeAssetType(result);
    }

    private static bool TryStripSuffix(string result, out string strippedType)
    {
        strippedType = "";
        if (!result.EndsWith("s")) return false;
        
        var withoutSuffix = result.Substring(0, result.Length - 1);
        if (ValidAssetTypes.Any(x => x.Contains(withoutSuffix)))
        {
            strippedType = withoutSuffix;
            return true;
        }
        return false;
    }

    private static string MapLegacyAssetType(string type)
    {
        return type switch
        {
            "units" => "character",
            "attachments" => "item",
            "projectiles" => "item",
            "noisetextures" => "noise",
            "sfx" => "soundeffect",
            "skyboxes" => "skybox",
            "textures" => "terrain",
            _ => type
        };
    }

    public static string GetExtension(string extensionOrPath)
    {
        string ext = Path.GetExtension(extensionOrPath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) && extensionOrPath.StartsWith('.')) ext = extensionOrPath.ToLowerInvariant();
        return ext;
    }

    public static ReadOnlySet<string> GetValidAssetTypesForExtension(string extensionOrPath)
    {
        string ext = GetExtension(extensionOrPath);
        if (ValidAssetTypesByExtension.TryGetValue(ext, out var types))
        {
            return types;
        }
        return (new HashSet<string>()).AsReadOnly();
    }

    public static string GetDefaultAssetTypeForExtension(string extension)
    {
        return GetValidAssetTypesForExtension(extension).FirstOrDefault() ?? "";
    }

    public static bool IsValidAssetTypeForExtension(string extensionOrPath, string? assetType, out string canonicalType, out ReadOnlySet<string> validTypes)
    {
        canonicalType = GetCanonicalType(assetType);

        string ext = GetExtension(extensionOrPath);
        validTypes = GetValidAssetTypesForExtension(ext);

        if (!validTypes.Contains(canonicalType))
        {
            canonicalType = GetDefaultAssetTypeForExtension(ext);
            return false;
        }

        return true;
    }

    public static string? ExtractAssetType(string filePath)
    {
        string? metaJson = ExtractMetadata(filePath);
        if (string.IsNullOrEmpty(metaJson)) return null;

        return TryExtractAssetTypeFromJson(filePath, metaJson);
    }

    private static string? TryExtractAssetTypeFromJson(string filePath, string metaJson)
    {
        try
        {
            if (JsonNode.Parse(metaJson) is not JsonObject obj) return null;
            
            string? typeVal = GetAssetTypeFromObject(obj);

            if (!string.IsNullOrEmpty(typeVal) && IsValidAssetTypeForExtension(filePath, typeVal, out string canonical, out _))
            {
                return canonical;
            }
        }
        catch { }
        return null;
    }

    private static string? GetAssetTypeFromObject(JsonObject obj)
    {
        return obj["asset_type"]?.ToString()
            ?? obj["AssetType"]?.ToString()
            ?? obj["default_asset_type"]?.ToString();
    }

    public static bool SetAssetType(string filePath, string assetType)
    {
        if (!File.Exists(filePath)) return false;
        if (!IsValidAssetTypeForExtension(filePath, assetType, out string canonical, out _))
        {
            return false;
        }

        string? existingMeta = ExtractMetadata(filePath);
        JsonObject metaObj;
        if (!string.IsNullOrEmpty(existingMeta))
        {
            try
            {
                metaObj = JsonNode.Parse(existingMeta)?.AsObject() ?? new JsonObject();
            }
            catch
            {
                metaObj = new JsonObject();
            }
        }
        else
        {
            metaObj = new JsonObject();
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
            metaObj["format"] = ext.TrimStart('.');
        }

        metaObj["asset_type"] = canonical;
        metaObj["blake3"] = ComputeBlake3(filePath);
        return AddMetadata(filePath, metaObj.ToJsonString());
    }

    public static string? ExtractAuthor(string filePath)
    {
        string? metaJson = ExtractMetadata(filePath);
        if (string.IsNullOrEmpty(metaJson)) return null;
        try
        {
            var node = JsonNode.Parse(metaJson);
            return node?["author"]?.ToString() ?? node?["Author"]?.ToString();
        }
        catch { }
        return null;
    }

    public static bool SetAuthor(string filePath, string author)
    {
        if (!File.Exists(filePath)) return false;
        string? existingMeta = ExtractMetadata(filePath);
        JsonObject metaObj;
        if (!string.IsNullOrEmpty(existingMeta))
        {
            try
            {
                metaObj = JsonNode.Parse(existingMeta)?.AsObject() ?? new JsonObject();
            }
            catch
            {
                metaObj = new JsonObject();
            }
        }
        else
        {
            metaObj = new JsonObject();
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
            metaObj["format"] = ext.TrimStart('.');
        }

        metaObj["author"] = author;
        metaObj["blake3"] = ComputeBlake3(filePath);
        return AddMetadata(filePath, metaObj.ToJsonString());
    }

    public static string? ExtractPreferredFileName(string filePath)
    {
        string? metaJson = ExtractMetadata(filePath);
        if (string.IsNullOrEmpty(metaJson)) return null;
        try
        {
            var node = JsonNode.Parse(metaJson);
            return node?["preferred_file_name"]?.ToString() ?? node?["preferredFileName"]?.ToString();
        }
        catch { }
        return null;
    }

    public static bool SetPreferredFileName(string filePath, string preferredFileName)
    {
        if (!File.Exists(filePath)) return false;
        string? existingMeta = ExtractMetadata(filePath);
        JsonObject metaObj;
        if (!string.IsNullOrEmpty(existingMeta))
        {
            try
            {
                metaObj = JsonNode.Parse(existingMeta)?.AsObject() ?? new JsonObject();
            }
            catch
            {
                metaObj = new JsonObject();
            }
        }
        else
        {
            metaObj = new JsonObject();
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
            metaObj["format"] = ext.TrimStart('.');
        }

        metaObj["preferred_file_name"] = preferredFileName;
        metaObj["blake3"] = ComputeBlake3(filePath);
        return AddMetadata(filePath, metaObj.ToJsonString());
    }

    public static bool ExtractSupportsTeamColorFromMetadataJson(string? metaJson)
    {
        if (string.IsNullOrEmpty(metaJson)) return false;
        try
        {
            if (JsonNode.Parse(metaJson) is not JsonObject obj) return false;

            var booleanKeys = new[]
            {
                "team_color", "teamColor",
                "supports_team_color", "supportsTeamColor",
                "player_color", "playerColor",
                "has_player_color_mask", "hasPlayerColorMask",
                "has_player_color", "hasPlayerColor"
            };

            if (TryExtractBooleanTeamColor(obj, booleanKeys, out bool supportsColor))
            {
                return supportsColor;
            }

            var stringKeys = new[]
            {
                "chroma_key", "chromaKey",
                "target_hex", "targetHex"
            };

            return HasValidChromaKey(obj, stringKeys);
        }
        catch { }
        return false;
    }

    private static bool TryExtractBooleanTeamColor(JsonObject obj, string[] keys, out bool result)
    {
        result = false;
        foreach (var key in keys)
        {
            if (obj.TryGetPropertyValue(key, out var val) && val != null)
            {
                if (val.GetValueKind() == JsonValueKind.True) { result = true; return true; }
                if (val.GetValueKind() == JsonValueKind.False) { result = false; return true; }
                if (bool.TryParse(val.ToString(), out bool b)) { result = b; return true; }
            }
        }
        return false;
    }

    private static bool HasValidChromaKey(JsonObject obj, string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetPropertyValue(key, out var val) && val != null)
            {
                if (val.GetValueKind() == JsonValueKind.True) return true;
                if (val.GetValueKind() == JsonValueKind.False) return false;

                string s = val.ToString().Trim();
                if (!string.IsNullOrEmpty(s) && !string.Equals(s, "none", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public static bool? ExtractSupportsTeamColorWithMetadata(string? metaJson, string filePath)
    {
        if (!string.IsNullOrEmpty(metaJson))
        {
            if (ExtractSupportsTeamColorFromMetadataJson(metaJson))
            {
                return true;
            }
            try
            {
                var node = JsonNode.Parse(metaJson);
                if (node is JsonObject obj)
                {
                    if (obj.TryGetPropertyValue("team_color", out var tcVal) && tcVal != null && tcVal.GetValueKind() == JsonValueKind.False)
                    {
                        return false;
                    }
                }
            }
            catch { }
        }

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is ".rmesh" or ".glb" or ".gltf")
        {
            if (File.Exists(filePath))
            {
                return GlbPlayerColorProcessor.DetectSupportsTeamColor(filePath);
            }
        }

        return null;
    }

    public static bool? ExtractSupportsTeamColor(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        string? metaJson = ExtractMetadata(filePath);
        return ExtractSupportsTeamColorWithMetadata(metaJson, filePath);
    }

    public static bool SetSupportsTeamColor(string filePath, bool supportsTeamColor)
    {
        if (!File.Exists(filePath)) return false;
        string? existingMeta = ExtractMetadata(filePath);
        JsonObject metaObj;
        if (!string.IsNullOrEmpty(existingMeta))
        {
            try
            {
                metaObj = JsonNode.Parse(existingMeta)?.AsObject() ?? new JsonObject();
            }
            catch
            {
                metaObj = new JsonObject();
            }
        }
        else
        {
            metaObj = new JsonObject();
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
            metaObj["format"] = ext.TrimStart('.');
        }

        metaObj["team_color"] = supportsTeamColor;
        metaObj["blake3"] = ComputeBlake3(filePath);
        return AddMetadata(filePath, metaObj.ToJsonString());
    }

    public static string? ExtractChromaKey(string filePath)
    {
        string? metaJson = ExtractMetadata(filePath);
        return ExtractChromaKeyFromMetadataJson(metaJson);
    }

    public static string? ExtractChromaKeyFromMetadataJson(string? metaJson)
    {
        if (string.IsNullOrEmpty(metaJson)) return null;
        try
        {
            var node = JsonNode.Parse(metaJson);
            if (node is not JsonObject obj) return null;

            var keys = new[] { "chroma_key", "chromaKey", "target_hex", "targetHex" };
            foreach (var key in keys)
            {
                if (obj.TryGetPropertyValue(key, out var val) && val != null)
                {
                    return val.ToString();
                }
            }
        }
        catch { }
        return null;
    }

    public static bool SetChromaKey(string filePath, string chromaKey)
    {
        if (!File.Exists(filePath)) return false;
        string? existingMeta = ExtractMetadata(filePath);
        JsonObject metaObj;
        if (!string.IsNullOrEmpty(existingMeta))
        {
            try
            {
                metaObj = JsonNode.Parse(existingMeta)?.AsObject() ?? new JsonObject();
            }
            catch
            {
                metaObj = new JsonObject();
            }
        }
        else
        {
            metaObj = new JsonObject();
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
            metaObj["format"] = ext.TrimStart('.');
        }

        metaObj["chroma_key"] = chromaKey;
        metaObj["blake3"] = ComputeBlake3(filePath);
        return AddMetadata(filePath, metaObj.ToJsonString());
    }

    public static List<string> ExtractTags(string filePath)
    {
        var result = new List<string>();
        string? metaJson = ExtractMetadata(filePath);
        if (string.IsNullOrEmpty(metaJson)) return result;
        try
        {
            var node = JsonNode.Parse(metaJson);
            if (node is JsonObject obj && obj["tags"] is JsonArray tagsArray)
            {
                foreach (var tagNode in tagsArray)
                {
                    string? tag = tagNode?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(tag) && !result.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(tag);
                    }
                }
            }
        }
        catch { }
        return result;
    }

    public static bool SetTags(string filePath, IEnumerable<string> tags)
    {
        if (!File.Exists(filePath)) return false;
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is not (".rtex" or ".ranim" or ".rmesh" or ".raud")) return false;

        string? existingMeta = ExtractMetadata(filePath);
        JsonObject metaObj;
        if (!string.IsNullOrEmpty(existingMeta))
        {
            try
            {
                metaObj = JsonNode.Parse(existingMeta)?.AsObject() ?? new JsonObject();
            }
            catch
            {
                metaObj = new JsonObject();
            }
        }
        else
        {
            metaObj = new JsonObject();
            metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
            metaObj["format"] = ext.TrimStart('.');
        }

        var cleanTags = (tags ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => (JsonNode)JsonValue.Create(t)!)
            .ToArray();

        metaObj["tags"] = new JsonArray(cleanTags);
        metaObj["blake3"] = ComputeBlake3(filePath);
        return AddMetadata(filePath, metaObj.ToJsonString());
    }

    public static bool AddMetadata(string filePath, string realmMetadataJson)
    {
        if (!File.Exists(filePath)) return false;
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        switch (ext)
        {
            case ".rmesh":
                AddMetadataToRmesh(filePath, realmMetadataJson);
                return true;
            case ".rtex":
                AddMetadataToRtex(filePath, realmMetadataJson);
                return true;
            case ".ranim":
                AddMetadataToRanim(filePath, realmMetadataJson);
                return true;
            case ".raud":
                AddMetadataToRaud(filePath, realmMetadataJson);
                return true;
            case ".rkey":
                AddMetadataToRkey(filePath, realmMetadataJson);
                return true;
            default:
                throw new NotSupportedException($"Unsupported file format '{ext}' for metadata. Supported formats: .rmesh, .rtex, .raud, .ranim, .rkey");
        }
    }

    public static bool RemoveMetadata(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        switch (ext)
        {
            case ".rmesh":
                RemoveMetadataFromRmesh(filePath);
                return true;
            case ".rtex":
                RemoveMetadataFromRtex(filePath);
                return true;
            case ".ranim":
                RemoveMetadataFromRanim(filePath);
                return true;
            case ".raud":
                RemoveMetadataFromRaud(filePath);
                return true;
            case ".rkey":
                RemoveMetadataFromRkey(filePath);
                return true;
            default:
                throw new NotSupportedException($"Unsupported file format '{ext}' for metadata. Supported formats: .rmesh, .rtex, .raud, .ranim, .rkey");
        }
    }

    public static string? ExtractMetadataFromRmesh(string filePath)
    {
        return RmeshFile.ExtractMetadataFromFile(filePath);
    }

    public static void AddMetadataToRmesh(string filePath, string realmMetadataJson)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        byte[] updated = RmeshFile.SetMetadata(bytes, realmMetadataJson);
        File.WriteAllBytes(filePath, updated);
    }

    public static void RemoveMetadataFromRmesh(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        byte[] updated = RmeshFile.SetMetadata(bytes, null);
        File.WriteAllBytes(filePath, updated);
    }

    public static string? ExtractMetadataFromRaud(string filePath)
    {
        return RaudFile.ExtractMetadataFromFile(filePath);
    }

    public static void AddMetadataToRaud(string filePath, string realmMetadataJson)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        byte[] updated = RaudFile.SetMetadata(bytes, realmMetadataJson);
        File.WriteAllBytes(filePath, updated);
    }

    public static void RemoveMetadataFromRaud(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        byte[] updated = RaudFile.SetMetadata(bytes, null);
        File.WriteAllBytes(filePath, updated);
    }

    public static string? ExtractMetadataFromRtex(string filePath)
    {
        return Realm.Shared.Textures.RtexFile.ExtractMetadataFromFile(filePath);
    }

    public static void AddMetadataToRtex(string filePath, string realmMetadataJson)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        byte[] updated = Realm.Shared.Textures.RtexFile.SetMetadata(bytes, realmMetadataJson);
        File.WriteAllBytes(filePath, updated);
    }

    public static void RemoveMetadataFromRtex(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        byte[] updated = Realm.Shared.Textures.RtexFile.SetMetadata(bytes, null);
        File.WriteAllBytes(filePath, updated);
    }

    public static string? ExtractMetadataFromRkey(string filePath)
    {
        return RkeyFile.ExtractMetadataFromFile(filePath);
    }

    public static void AddMetadataToRkey(string filePath, string realmMetadataJson)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        byte[] updated = RkeyFile.SetMetadata(bytes, realmMetadataJson);
        File.WriteAllBytes(filePath, updated);
    }

    public static void RemoveMetadataFromRkey(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        byte[] updated = RkeyFile.SetMetadata(bytes, null);
        File.WriteAllBytes(filePath, updated);
    }

    public static bool ExtractIsCompressed(string? metadataJson)
    {
        return TryExtractIsCompressed(metadataJson, out bool isCompressed) && isCompressed;
    }

    public static bool TryExtractIsCompressed(string? metadataJson, out bool isCompressed)
    {
        isCompressed = false;
        if (string.IsNullOrWhiteSpace(metadataJson)) return false;
        try
        {
            if (JsonNode.Parse(metadataJson) is not JsonObject obj) return false;

            var keys = new[] { "is_compressed", "IsCompressed" };
            return ParseIsCompressedValue(obj, keys, out isCompressed);
        }
        catch { }
        return false;
    }

    private static bool ParseIsCompressedValue(JsonObject obj, string[] keys, out bool isCompressed)
    {
        isCompressed = false;
        foreach (var key in keys)
        {
            if (obj.TryGetPropertyValue(key, out var val) && val != null)
            {
                if (val.GetValueKind() == JsonValueKind.True) { isCompressed = true; return true; }
                if (val.GetValueKind() == JsonValueKind.False) { isCompressed = false; return true; }
                if (bool.TryParse(val.ToString(), out bool b)) { isCompressed = b; return true; }
            }
        }
        return false;
    }

    public static string? ExtractMetadataFromRanim(string filePath)
    {
        return RanimFile.ExtractMetadataFromFile(filePath);
    }

    public static string? ExtractMetadataFromRanimBytes(ReadOnlySpan<byte> bytes)
    {
        return RanimFile.ExtractMetadata(bytes);
    }

    public static void AddMetadataToRanim(string filePath, string realmMetadataJson)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        byte[] updated = AddMetadataToRanimBytes(bytes, realmMetadataJson);
        File.WriteAllBytes(filePath, updated);
    }

    public static byte[] AddMetadataToRanimBytes(byte[] bytes, string realmMetadataJson)
    {
        return RanimFile.SetMetadata(bytes, realmMetadataJson);
    }

    public static void RemoveMetadataFromRanim(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        byte[] updated = RemoveMetadataFromRanimBytes(bytes);
        File.WriteAllBytes(filePath, updated);
    }

    public static byte[] RemoveMetadataFromRanimBytes(byte[] bytes)
    {
        return RanimFile.SetMetadata(bytes, null);
    }

    public static bool IsRanimBytes(ReadOnlySpan<byte> bytes)
    {
        return RanimFile.IsRanimBytes(bytes);
    }

    public static byte[] StripMetadataEphemeral(byte[] bytes, string? extensionOrPath = null)
    {
        if (bytes == null || bytes.Length == 0) return bytes ?? Array.Empty<byte>();

        string extension = GetExtensionOrDefault(extensionOrPath);

        return TryStripFormatMetadata(extension, bytes);
    }

    private static string GetExtensionOrDefault(string? extensionOrPath)
    {
        if (string.IsNullOrEmpty(extensionOrPath)) return string.Empty;
        return Path.GetExtension(extensionOrPath).ToLowerInvariant();
    }

    private static byte[] TryStripFormatMetadata(string extension, byte[] bytes)
    {
        try
        {
            if (ShouldProcessFormat(extension, bytes, ".rmesh", RmeshFile.IsRmeshBytes)) return StripFormatMetadata(bytes, RmeshFile.Magic, RmeshFile.SetMetadata);
            if (ShouldProcessFormat(extension, bytes, ".raud", RaudFile.IsRaudBytes)) return StripFormatMetadata(bytes, RaudFile.Magic, RaudFile.SetMetadata);
            if (ShouldProcessFormat(extension, bytes, ".rtex", Realm.Shared.Textures.RtexFile.IsRtexBytes)) return StripFormatMetadata(bytes, Realm.Shared.Textures.RtexFile.Magic, Realm.Shared.Textures.RtexFile.SetMetadata);
            if (ShouldProcessFormat(extension, bytes, ".ranim", RanimFile.IsRanimBytes)) return StripFormatMetadata(bytes, RanimFile.Magic, RanimFile.SetMetadata);
            if (ShouldProcessFormat(extension, bytes, ".rkey", RkeyFile.IsRkeyBytes)) return StripFormatMetadata(bytes, RkeyFile.Magic, RkeyFile.SetMetadata);
        }
        catch
        {
            // fallback
        }

        return bytes;
    }

    public delegate bool IsFormatBytesDelegate(ReadOnlySpan<byte> bytes);
    public delegate byte[] SetMetadataDelegate(ReadOnlySpan<byte> bytes, string? metadataJson);

    private static bool ShouldProcessFormat(string extension, byte[] bytes, string formatExt, IsFormatBytesDelegate isFormatBytes)
    {
        return extension == formatExt || ((string.IsNullOrEmpty(extension) || extension == ".bin") && isFormatBytes(bytes));
    }

    private static byte[] StripFormatMetadata(byte[] bytes, ReadOnlySpan<byte> magic, SetMetadataDelegate setMetadata)
    {
        if (RealmContainerHeader.TryReadHeader(bytes, magic, out _, out _, out int payloadOffset))
        {
            return bytes.AsSpan(payloadOffset).ToArray();
        }
        return setMetadata(bytes, null);
    }

    public static string ComputeBlake3(byte[] bytes, string? extensionOrPath = null)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return Hasher.Hash(ReadOnlySpan<byte>.Empty).ToString();
        }

        byte[] canonicalBytes = StripMetadataEphemeral(bytes, extensionOrPath);
        var hash = Hasher.Hash(canonicalBytes);
        return hash.ToString();
    }

    public static string ComputeBlake3(string filePath)
    {
        if (!File.Exists(filePath)) return string.Empty;
        byte[] bytes = File.ReadAllBytes(filePath);
        return ComputeBlake3(bytes, filePath);
    }

    public static string ComputeBlake3(Stream stream, string? extensionOrPath = null)
    {
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return ComputeBlake3(memoryStream.ToArray(), extensionOrPath);
    }

    public static string ComputeCanonicalAssetIdentifier(byte[] bytes, string extensionOrPath)
    {
        string extension = Path.GetExtension(extensionOrPath).ToLowerInvariant();
        string blake3Hash = ComputeBlake3(bytes, extension);
        return string.IsNullOrEmpty(extension) ? blake3Hash : $"{blake3Hash}{extension}";
    }

    public static bool SyncBlake3Metadata(string filePath, string? precomputedCanonicalBlake3 = null)
    {
        if (!File.Exists(filePath)) return false;
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is not (".rtex" or ".ranim" or ".rmesh" or ".raud" or ".rkey")) return false;

        try
        {
            string? existingMeta = ExtractMetadata(filePath);
            JsonObject metaObj = ParseOrCreateMetadata(existingMeta, ext);

            if (IsBlake3UpToDate(metaObj, filePath, ref precomputedCanonicalBlake3))
            {
                return true;
            }

            string canonicalBlake3 = !string.IsNullOrEmpty(precomputedCanonicalBlake3) ? precomputedCanonicalBlake3 : ComputeBlake3(filePath);
            metaObj["blake3"] = canonicalBlake3;
            return AddMetadata(filePath, metaObj.ToJsonString());
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBlake3UpToDate(JsonObject metaObj, string filePath, ref string? precomputedCanonicalBlake3)
    {
        if (metaObj.TryGetPropertyValue("blake3", out var existingB3) && existingB3 != null)
        {
            string existingHashStr = existingB3.ToString();
            if (!string.IsNullOrEmpty(precomputedCanonicalBlake3) && string.Equals(existingHashStr, precomputedCanonicalBlake3, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.IsNullOrEmpty(precomputedCanonicalBlake3))
            {
                string canonical = ComputeBlake3(filePath);
                if (string.Equals(existingHashStr, canonical, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                precomputedCanonicalBlake3 = canonical;
            }
        }
        return false;
    }

    public static byte[] SyncBlake3MetadataBytes(byte[] bytes, string extensionOrPath)
    {
        if (bytes == null || bytes.Length == 0) return bytes ?? Array.Empty<byte>();
        
        string ext = NormalizeExtension(extensionOrPath);
        if (!IsSupportedBlake3Extension(ext)) return bytes;

        return TrySyncBlake3ForBytes(bytes, ext);
    }

    private static string NormalizeExtension(string extensionOrPath)
    {
        string ext = Path.GetExtension(extensionOrPath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) && extensionOrPath.StartsWith('.')) ext = extensionOrPath.ToLowerInvariant();
        return ext;
    }

    private static bool IsSupportedBlake3Extension(string ext)
    {
        return ext is ".rtex" or ".ranim" or ".rmesh" or ".raud" or ".rkey";
    }

    private static byte[] TrySyncBlake3ForBytes(byte[] bytes, string ext)
    {
        try
        {
            string canonicalBlake3 = ComputeBlake3(bytes, ext);
            string? existingMeta = ExtractMetadataFromBytes(bytes, ext);
            JsonObject metaObj = ParseOrCreateMetadata(existingMeta, ext);

            if (IsBlake3Matching(metaObj, canonicalBlake3))
            {
                return bytes;
            }

            metaObj["blake3"] = canonicalBlake3;
            return SetMetadataToBytes(bytes, ext, metaObj.ToJsonString());
        }
        catch
        {
            return bytes;
        }
    }

    private static bool IsBlake3Matching(JsonObject metaObj, string canonicalBlake3)
    {
        if (!metaObj.TryGetPropertyValue("blake3", out var existingB3) || existingB3 == null) return false;
        return string.Equals(existingB3.ToString(), canonicalBlake3, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractMetadataFromBytes(byte[] bytes, string ext)
    {
        return ext switch
        {
            ".rmesh" => RmeshFile.ExtractMetadata(bytes),
            ".rtex" => RtexFile.ExtractMetadata(bytes),
            ".ranim" => RanimFile.ExtractMetadata(bytes),
            ".raud" => RaudFile.ExtractMetadata(bytes),
            ".rkey" => RkeyFile.ExtractMetadata(bytes),
            _ => null
        };
    }

    private static byte[] SetMetadataToBytes(byte[] bytes, string ext, string newMetaJson)
    {
        return ext switch
        {
            ".rmesh" => RmeshFile.SetMetadata(bytes, newMetaJson),
            ".rtex" => RtexFile.SetMetadata(bytes, newMetaJson),
            ".ranim" => RanimFile.SetMetadata(bytes, newMetaJson),
            ".raud" => RaudFile.SetMetadata(bytes, newMetaJson),
            ".rkey" => RkeyFile.SetMetadata(bytes, newMetaJson),
            _ => bytes
        };
    }
}
