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
			       magic.SequenceEqual(RtexFile.MagicBytes) ||
			       magic.SequenceEqual(RanimFile.MagicBytes) ||
			       magic.SequenceEqual(RaudFile.MagicBytes) ||
			       magic.SequenceEqual(RkeyFile.MagicBytes);
		}
		catch
		{
			return false;
		}
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
			if (magic.SequenceEqual(RtexFile.MagicBytes)) return ExtractMetadataFromRtex(filePath);
			if (magic.SequenceEqual(RanimFile.MagicBytes)) return ExtractMetadataFromRanim(filePath);
			if (magic.SequenceEqual(RaudFile.MagicBytes)) return ExtractMetadataFromRaud(filePath);
			if (magic.SequenceEqual(RkeyFile.MagicBytes)) return ExtractMetadataFromRkey(filePath);
		}
		catch
		{
		}

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
		metaObj[AssetMetadataKeys.Blake3Hash] = canonicalBlake3;

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
			catch
			{
			}
		}

		return new JsonObject
		{
			[AssetMetadataKeys.CreatedUtc] = DateTime.UtcNow.ToString("O"),
			[AssetMetadataKeys.Format] = ext.TrimStart('.')
		};
	}

	private static void EnsureIsCompressedProperty(JsonObject metaObj, string ext)
	{
		if (metaObj.ContainsKey(AssetMetadataKeys.IsCompressed)) return;

		if (ext is ".rmesh" or ".ranim")
		{
			metaObj[AssetMetadataKeys.IsCompressed] = true;
		}
		else if (ext is ".rtex" or ".raud")
		{
			metaObj[AssetMetadataKeys.IsCompressed] = false;
		}
	}

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

	public static bool SetAssetType(string filePath, string assetType)
	{
		if (!IsValidAssetTypeForExtension(filePath, assetType, out string canonical, out _))
		{
			return false;
		}

		return UpdateMetadataField(filePath, meta => meta[AssetMetadataKeys.AssetType] = canonical);
	}

	public static string? ExtractAuthor(string filePath)
	{
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return null;
		try
		{
			var node = JsonNode.Parse(metaJson);
			return node?[AssetMetadataKeys.Author]?.ToString() ?? node?["Author"]?.ToString();
		}
		catch
		{
		}

		return null;
	}

	private static bool UpdateMetadataField(string filePath, Action<JsonObject> updateAction)
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
			metaObj[AssetMetadataKeys.CreatedUtc] = DateTime.UtcNow.ToString("O");
			metaObj[AssetMetadataKeys.Format] = ext.TrimStart('.');
		}

		updateAction(metaObj);
		metaObj[AssetMetadataKeys.Blake3Hash] = ComputeBlake3(filePath);
		return AddMetadata(filePath, metaObj.ToJsonString());
	}

	public static bool SetAuthor(string filePath, string author)
	{
		return UpdateMetadataField(filePath, meta => meta[AssetMetadataKeys.Author] = author);
	}

	public static string? ExtractPreferredFileName(string filePath)
	{
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return null;
		try
		{
			var node = JsonNode.Parse(metaJson);
			return node?[AssetMetadataKeys.PreferredFileName]?.ToString() ?? node?["preferredFileName"]?.ToString();
		}
		catch
		{
		}

		return null;
	}

	public static bool SetPreferredFileName(string filePath, string preferredFileName)
	{
		return UpdateMetadataField(filePath, meta => meta[AssetMetadataKeys.PreferredFileName] = preferredFileName);
	}

	public static bool ExtractSupportsTeamColorFromMetadataJson(string? metaJson)
	{
		if (string.IsNullOrEmpty(metaJson)) return false;
		try
		{
			if (JsonNode.Parse(metaJson) is not JsonObject obj) return false;

			var booleanKeys = new[]
			{
				AssetMetadataKeys.TeamColor, "teamColor",
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
				AssetMetadataKeys.ChromaKey, "chromaKey",
				AssetMetadataKeys.TargetHex, "targetHex"
			};

			return HasValidChromaKey(obj, stringKeys);
		}
		catch
		{
		}

		return false;
	}

	private static bool TryExtractBooleanTeamColor(JsonObject obj, string[] keys, out bool result)
	{
		result = false;
		foreach (var key in keys)
		{
			if (obj.TryGetPropertyValue(key, out var val) && val != null)
			{
				if (val.GetValueKind() == JsonValueKind.True)
				{
					result = true;
					return true;
				}

				if (val.GetValueKind() == JsonValueKind.False)
				{
					result = false;
					return true;
				}

				if (bool.TryParse(val.ToString(), out bool b))
				{
					result = b;
					return true;
				}
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
					if (obj.TryGetPropertyValue(AssetMetadataKeys.TeamColor, out var tcVal) && tcVal != null && tcVal.GetValueKind() == JsonValueKind.False)
					{
						return false;
					}
				}
			}
			catch
			{
			}
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
		return UpdateMetadataField(filePath, meta => meta[AssetMetadataKeys.TeamColor] = supportsTeamColor);
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

			var keys = new[] { AssetMetadataKeys.ChromaKey, "chromaKey", AssetMetadataKeys.TargetHex, "targetHex" };
			foreach (var key in keys)
			{
				if (obj.TryGetPropertyValue(key, out var val) && val != null)
				{
					return val.ToString();
				}
			}
		}
		catch
		{
		}

		return null;
	}

	public static bool SetChromaKey(string filePath, string chromaKey)
	{
		return UpdateMetadataField(filePath, meta => meta[AssetMetadataKeys.ChromaKey] = chromaKey);
	}

	public static List<string> ExtractTags(string filePath)
	{
		var result = new List<string>();
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return result;
		try
		{
			var node = JsonNode.Parse(metaJson);
			if (node is JsonObject obj && obj[AssetMetadataKeys.Tags] is JsonArray tagsArray)
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
		catch
		{
		}

		return result;
	}

	public static bool SetTags(string filePath, IEnumerable<string> tags)
	{
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		if (ext is not (".rtex" or ".ranim" or ".rmesh" or ".raud")) return false;

		return UpdateMetadataField(filePath, metaObj =>
		{
			var cleanTags = (tags ?? Array.Empty<string>())
				.Where(t => !string.IsNullOrWhiteSpace(t))
				.Select(t => t.Trim().ToLowerInvariant())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Select(t => (JsonNode)JsonValue.Create(t)!)
				.ToArray();

			metaObj[AssetMetadataKeys.Tags] = new JsonArray(cleanTags);
		});
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
		return RealmContainerFile.ExtractMetadataFromPath(filePath, RmeshFile.Magic);
	}

	public static void AddMetadataToRmesh(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RealmContainerFile.SetContainerMetadata(bytes, RmeshFile.Magic, RmeshFile.Name, RmeshFile.Format, realmMetadataJson);
		File.WriteAllBytes(filePath, updated);
	}

	public static void RemoveMetadataFromRmesh(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RealmContainerFile.SetContainerMetadata(bytes, RmeshFile.Magic, RmeshFile.Name, RmeshFile.Format, null);
		File.WriteAllBytes(filePath, updated);
	}

	public static string? ExtractMetadataFromRaud(string filePath)
	{
		return RealmContainerFile.ExtractMetadataFromPath(filePath, RaudFile.Magic);
	}

	public static void AddMetadataToRaud(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RealmContainerFile.SetContainerMetadata(bytes, RaudFile.Magic, RaudFile.Name, RaudFile.Format, realmMetadataJson);
		File.WriteAllBytes(filePath, updated);
	}

	public static void RemoveMetadataFromRaud(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RealmContainerFile.SetContainerMetadata(bytes, RaudFile.Magic, RaudFile.Name, RaudFile.Format, null);
		File.WriteAllBytes(filePath, updated);
	}

	public static string? ExtractMetadataFromRtex(string filePath)
	{
		return RealmContainerFile.ExtractMetadataFromPath(filePath, RtexFile.Magic);
	}

	public static void AddMetadataToRtex(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RealmContainerFile.SetContainerMetadata(bytes, RtexFile.Magic, RtexFile.Name, RtexFile.Format, realmMetadataJson);
		File.WriteAllBytes(filePath, updated);
	}

	public static void RemoveMetadataFromRtex(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RealmContainerFile.SetContainerMetadata(bytes, RtexFile.Magic, RtexFile.Name, RtexFile.Format, null);
		File.WriteAllBytes(filePath, updated);
	}

	public static string? ExtractMetadataFromRkey(string filePath)
	{
		return RealmContainerFile.ExtractMetadataFromPath(filePath, RkeyFile.Magic);
	}

	public static void AddMetadataToRkey(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RealmContainerHeader.SetMetadata(bytes, RkeyFile.Magic, realmMetadataJson, RkeyFile.Name);
		File.WriteAllBytes(filePath, updated);
	}

	public static void RemoveMetadataFromRkey(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RealmContainerHeader.SetMetadata(bytes, RkeyFile.Magic, null, RkeyFile.Name);
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

			var keys = new[] { AssetMetadataKeys.IsCompressed, "IsCompressed" };
			return ParseIsCompressedValue(obj, keys, out isCompressed);
		}
		catch
		{
		}

		return false;
	}

	private static bool ParseIsCompressedValue(JsonObject obj, string[] keys, out bool isCompressed)
	{
		isCompressed = false;
		foreach (var key in keys)
		{
			if (obj.TryGetPropertyValue(key, out var val) && val != null)
			{
				if (val.GetValueKind() == JsonValueKind.True)
				{
					isCompressed = true;
					return true;
				}

				if (val.GetValueKind() == JsonValueKind.False)
				{
					isCompressed = false;
					return true;
				}

				if (bool.TryParse(val.ToString(), out bool b))
				{
					isCompressed = b;
					return true;
				}
			}
		}

		return false;
	}

	public static string? ExtractMetadataFromRanim(string filePath)
	{
		return RealmContainerFile.ExtractMetadataFromPath(filePath, RanimFile.Magic);
	}

	public static string? ExtractMetadataFromRanimBytes(ReadOnlySpan<byte> bytes)
	{
		return RealmContainerFile.ExtractMetadataFromBytes(bytes, RanimFile.Magic);
	}

	public static void AddMetadataToRanim(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = AddMetadataToRanimBytes(bytes, realmMetadataJson);
		File.WriteAllBytes(filePath, updated);
	}

	public static byte[] AddMetadataToRanimBytes(byte[] bytes, string realmMetadataJson)
	{
		return RealmContainerFile.SetContainerMetadata(bytes, RanimFile.Magic, RanimFile.Name, RanimFile.Format, realmMetadataJson, configureMetadata: metaObj => metaObj[AssetMetadataKeys.AssetType] ??= AssetType.Animation.ToCanonicalString());
	}

	public static void RemoveMetadataFromRanim(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RemoveMetadataFromRanimBytes(bytes);
		File.WriteAllBytes(filePath, updated);
	}

	public static byte[] RemoveMetadataFromRanimBytes(byte[] bytes)
	{
		return RealmContainerFile.SetContainerMetadata(bytes, RanimFile.Magic, RanimFile.Name, RanimFile.Format, null, configureMetadata: metaObj => metaObj[AssetMetadataKeys.AssetType] ??= AssetType.Animation.ToCanonicalString());
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
			if (ShouldProcessFormat(extension, bytes, ".rmesh", RmeshFile.IsRmeshBytes)) return StripFormatMetadata(bytes, RmeshFile.Magic, (b, m) => RealmContainerFile.SetContainerMetadata(b, RmeshFile.Magic, RmeshFile.Name, RmeshFile.Format, m));
			if (ShouldProcessFormat(extension, bytes, ".raud", RaudFile.IsRaudBytes)) return StripFormatMetadata(bytes, RaudFile.Magic, (b, m) => RealmContainerFile.SetContainerMetadata(b, RaudFile.Magic, RaudFile.Name, RaudFile.Format, m));
			if (ShouldProcessFormat(extension, bytes, ".rtex", RtexFile.IsRtexBytes)) return StripFormatMetadata(bytes, RtexFile.Magic, (b, m) => RealmContainerFile.SetContainerMetadata(b, RtexFile.Magic, RtexFile.Name, RtexFile.Format, m));
			if (ShouldProcessFormat(extension, bytes, ".ranim", RanimFile.IsRanimBytes)) return StripFormatMetadata(bytes, RanimFile.Magic, (b, m) => RealmContainerFile.SetContainerMetadata(b, RanimFile.Magic, RanimFile.Name, RanimFile.Format, m, configureMetadata: metaObj => metaObj[AssetMetadataKeys.AssetType] ??= AssetType.Animation.ToCanonicalString()));
			if (ShouldProcessFormat(extension, bytes, ".rkey", RkeyFile.IsRkeyBytes)) return StripFormatMetadata(bytes, RkeyFile.Magic, (b, m) => RealmContainerHeader.SetMetadata(b, RkeyFile.Magic, m, RkeyFile.Name));
		}
		catch
		{
		}

		return bytes;
	}


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
			metaObj[AssetMetadataKeys.Blake3Hash] = canonicalBlake3;
			return AddMetadata(filePath, metaObj.ToJsonString());
		}
		catch
		{
			return false;
		}
	}

	private static bool IsBlake3UpToDate(JsonObject metaObj, string filePath, ref string? precomputedCanonicalBlake3)
	{
		if (metaObj.TryGetPropertyValue(AssetMetadataKeys.Blake3Hash, out var existingB3) && existingB3 != null)
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

			metaObj[AssetMetadataKeys.Blake3Hash] = canonicalBlake3;
			return SetMetadataToBytes(bytes, ext, metaObj.ToJsonString());
		}
		catch
		{
			return bytes;
		}
	}

	private static bool IsBlake3Matching(JsonObject metaObj, string canonicalBlake3)
	{
		if (!metaObj.TryGetPropertyValue(AssetMetadataKeys.Blake3Hash, out var existingB3) || existingB3 == null) return false;
		return string.Equals(existingB3.ToString(), canonicalBlake3, StringComparison.OrdinalIgnoreCase);
	}

	private static string? ExtractMetadataFromBytes(byte[] bytes, string ext)
	{
		return ext switch
		{
			".rmesh" => RealmContainerFile.ExtractMetadataFromBytes(bytes, RmeshFile.Magic),
			".rtex" => RealmContainerFile.ExtractMetadataFromBytes(bytes, RtexFile.Magic),
			".ranim" => RealmContainerFile.ExtractMetadataFromBytes(bytes, RanimFile.Magic),
			".raud" => RealmContainerFile.ExtractMetadataFromBytes(bytes, RaudFile.Magic),
			".rkey" => RealmContainerFile.ExtractMetadataFromBytes(bytes, RkeyFile.Magic),
			_ => null
		};
	}

	private static byte[] SetMetadataToBytes(byte[] bytes, string ext, string newMetaJson)
	{
		return ext switch
		{
			".rmesh" => RealmContainerFile.SetContainerMetadata(bytes, RmeshFile.Magic, RmeshFile.Name, RmeshFile.Format, newMetaJson),
			".rtex" => RealmContainerFile.SetContainerMetadata(bytes, RtexFile.Magic, RtexFile.Name, RtexFile.Format, newMetaJson),
			".ranim" => RealmContainerFile.SetContainerMetadata(bytes, RanimFile.Magic, RanimFile.Name, RanimFile.Format, newMetaJson, configureMetadata: metaObj => metaObj[AssetMetadataKeys.AssetType] ??= AssetType.Animation.ToCanonicalString()),
			".raud" => RealmContainerFile.SetContainerMetadata(bytes, RaudFile.Magic, RaudFile.Name, RaudFile.Format, newMetaJson),
			".rkey" => RealmContainerHeader.SetMetadata(bytes, RkeyFile.Magic, newMetaJson, RkeyFile.Name),
			_ => bytes
		};
	}
}