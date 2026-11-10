using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blake3;
using Realm.EditorAPI;
using Realm.Shared.Animation;
using Realm.Shared.Audio;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;

namespace Realm.Shared.Metadata;

/// <summary>the canonical type, its file extension and the stored field.</summary>
public static partial class RealmMetadataHelper
	{
	private static readonly Dictionary<string, ReadOnlySet<string>> ValidAssetTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
	{
		[".rtex"] = CreateAssetTypeSet(AssetType.Decal, AssetType.Icon, AssetType.Noise, AssetType.Ribbon, AssetType.Skybox, AssetType.Spritesheet, AssetType.Terrain, AssetType.VfxRadial, AssetType.VfxVertical),
		[".rmesh"] = CreateAssetTypeSet(AssetType.Character, AssetType.Building, AssetType.Prop, AssetType.Item),
		[".ranim"] = CreateAssetTypeSet(AssetType.Animation),
		[".raud"] = CreateAssetTypeSet(AssetType.Music, AssetType.SoundEffect),
		[".gdshader"] = CreateAssetTypeSet(AssetType.Shader)
	};

	private static ReadOnlySet<string> CreateAssetTypeSet(params AssetType[] assetTypes)
	{
		return assetTypes.Select(assetType => assetType.ToCanonicalString()).ToHashSet(StringComparer.OrdinalIgnoreCase).AsReadOnly();
	}

	private static ReadOnlySet<string> ValidAssetTypes = ValidAssetTypesByExtension.Values.SelectMany(x => x).ToHashSet(StringComparer.OrdinalIgnoreCase).AsReadOnly();

	public static AssetType? NormalizeAssetType(string? assetType)
	{
		if (string.IsNullOrWhiteSpace(assetType)) return null;
		return AssetTypeExtensions.TryParse(assetType, out var parsed) ? parsed : null;
	}

	public static AssetType? NormalizeAssetType(AssetType? assetType)
	{
		return assetType;
	}

	public static string GetCanonicalType(string? assetType)
	{
		var norm = NormalizeAssetType(assetType);
		if (norm.HasValue)
		{
			return norm.Value.ToCanonicalString();
		}

		return "";
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

	public static bool IsValidAssetTypeForExtension(string extensionOrPath, AssetType assetType, out ReadOnlySet<string> validTypes)
	{
		string ext = GetExtension(extensionOrPath);
		validTypes = GetValidAssetTypesForExtension(ext);

		foreach (var valid in validTypes)
		{
			if (NormalizeAssetType(valid) == assetType)
			{
				return true;
			}
		}

		return false;
	}

	public static bool IsValidAssetTypeForExtension(string extensionOrPath, string? assetType, out string canonicalType, out ReadOnlySet<string> validTypes)
	{
		var norm = NormalizeAssetType(assetType);
		string ext = GetExtension(extensionOrPath);
		validTypes = GetValidAssetTypesForExtension(ext);

		if (!norm.HasValue || !IsValidAssetTypeForExtension(extensionOrPath, norm.Value, out _))
		{
			canonicalType = GetDefaultAssetTypeForExtension(ext);
			return false;
		}

		canonicalType = norm.Value.ToCanonicalString();
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
		catch
		{
		}

		return null;
	}

	private static string? GetAssetTypeFromObject(JsonObject obj)
	{
		return obj[AssetMetadataKeys.AssetType]?.ToString();
	}
}
