using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blake3;
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
		return ext is ".rtex" or ".ranim" or ".rmesh" or ".raud" or ".rkey";
	}

	public static string? ExtractMetadata(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		return ext switch
		{
			".rmesh" => ExtractMetadataFromRmesh(filePath),
			".rtex" => ExtractMetadataFromRtex(filePath),
			".ranim" => ExtractMetadataFromRanim(filePath),
			".raud" => ExtractMetadataFromRaud(filePath),
			".rkey" => ExtractMetadataFromRkey(filePath),
			_ => null
		};
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
		JsonObject metaObj;
		if (!string.IsNullOrEmpty(defaultMetadataJson))
		{
			try
			{
				metaObj = JsonNode.Parse(defaultMetadataJson)?.AsObject() ?? new JsonObject();
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

		if (!metaObj.ContainsKey("is_compressed"))
		{
			if (ext is ".rmesh" or ".ranim")
			{
				metaObj["is_compressed"] = true;
			}
			else if (ext is ".rtex" or ".raud")
			{
				metaObj["is_compressed"] = false;
			}
		}

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

	private static readonly Dictionary<string, string[]> ValidAssetTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
	{
		[".rtex"] = new[] { "Decal", "Icon", "Noise", "Ribbon", "Skybox", "Spritesheet", "Terrain", "vfx_radial", "vfx_vertical" },
		[".png"] = new[] { "Decal", "Icon", "Noise", "Ribbon", "Skybox", "Spritesheet", "Terrain", "vfx_radial", "vfx_vertical" },
		[".jpg"] = new[] { "Decal", "Icon", "Noise", "Ribbon", "Skybox", "Spritesheet", "Terrain", "vfx_radial", "vfx_vertical" },
		[".jpeg"] = new[] { "Decal", "Icon", "Noise", "Ribbon", "Skybox", "Spritesheet", "Terrain", "vfx_radial", "vfx_vertical" },
		[".webp"] = new[] { "Decal", "Icon", "Noise", "Ribbon", "Skybox", "Spritesheet", "Terrain", "vfx_radial", "vfx_vertical" },
		[".dds"] = new[] { "Decal", "Icon", "Noise", "Ribbon", "Skybox", "Spritesheet", "Terrain", "vfx_radial", "vfx_vertical" },
		[".tga"] = new[] { "Decal", "Icon", "Noise", "Ribbon", "Skybox", "Spritesheet", "Terrain", "vfx_radial", "vfx_vertical" },
		[".bmp"] = new[] { "Decal", "Icon", "Noise", "Ribbon", "Skybox", "Spritesheet", "Terrain", "vfx_radial", "vfx_vertical" },
		[".rmesh"] = new[] { "Character", "Building", "Prop", "Item" },
		[".glb"] = new[] { "Character", "Building", "Prop", "Item" },
		[".gltf"] = new[] { "Character", "Building", "Prop", "Item" },
		[".fbx"] = new[] { "Character", "Building", "Prop", "Item" },
		[".obj"] = new[] { "Character", "Building", "Prop", "Item" },
		[".ranim"] = new[] { "Animation" },
		[".raud"] = new[] { "Music", "SoundEffect" },
		[".ogg"] = new[] { "Music", "SoundEffect" },
		[".wav"] = new[] { "Music", "SoundEffect" },
		[".mp3"] = new[] { "Music", "SoundEffect" },
		[".flac"] = new[] { "Music", "SoundEffect" },
		[".aac"] = new[] { "Music", "SoundEffect" }
	};

	public static string[] GetValidAssetTypesForExtension(string extensionOrPath)
	{
		string ext = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		if (string.IsNullOrEmpty(ext) && extensionOrPath.StartsWith('.')) ext = extensionOrPath.ToLowerInvariant();
		if (ValidAssetTypesByExtension.TryGetValue(ext, out var types))
		{
			return types;
		}
		return Array.Empty<string>();
	}

	public static bool IsValidAssetTypeForExtension(string extensionOrPath, string? assetType, out string canonicalType, out string[] validTypes)
	{
		validTypes = GetValidAssetTypesForExtension(extensionOrPath);
		canonicalType = string.Empty;
		if (string.IsNullOrWhiteSpace(assetType)) return false;

		string norm = assetType.Trim().Replace("_", "").ToLowerInvariant();

		string ext = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		if (string.IsNullOrEmpty(ext) && extensionOrPath.StartsWith('.')) ext = extensionOrPath.ToLowerInvariant();

		if (ext is ".rtex" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".dds" or ".tga" or ".bmp")
		{
			if (norm.Contains("radial")) { canonicalType = "vfx_radial"; return true; }
			if (norm.Contains("vertical")) { canonicalType = "vfx_vertical"; return true; }
			if (norm.Contains("tile") || norm.Contains("terrain") || norm.Contains("texture") || norm.Contains("splat") || norm.Contains("ground") || norm.Contains("cliff") || norm.Contains("grass") || norm.Contains("dirt") || norm.Contains("rock") || norm.Contains("sand") || norm.Contains("snow") || norm.Contains("mud")) { canonicalType = "Terrain"; return true; }
			if (norm.Contains("decal")) { canonicalType = "Decal"; return true; }
			if (norm.Contains("icon")) { canonicalType = "Icon"; return true; }
			if (norm.Contains("noise")) { canonicalType = "Noise"; return true; }
			if (norm.Contains("ribbon")) { canonicalType = "Ribbon"; return true; }
			if (norm.Contains("skybox")) { canonicalType = "Skybox"; return true; }
			if (norm.Contains("sprite") || norm.Contains("vfx") || norm.Contains("spell")) { canonicalType = "Spritesheet"; return true; }
			return false;
		}
		else if (ext is ".rmesh" or ".glb" or ".gltf" or ".fbx" or ".obj")
		{
			if (norm.Contains("character") || norm.Contains("unit")) { canonicalType = "Character"; return true; }
			if (norm.Contains("building") || norm.Contains("structure")) { canonicalType = "Building"; return true; }
			if (norm.Contains("environment") || norm.Contains("resource") || norm.Contains("prop") || norm.Contains("doodad") || norm.Contains("foliage") || norm.Contains("flora") || norm.Contains("rock") || norm.Contains("tree") || norm.Contains("nature")) { canonicalType = "Prop"; return true; }
			if (norm.Contains("item") || norm.Contains("attachment") || norm.Contains("weapon") || norm.Contains("projectile") || norm.Contains("gear") || norm.Contains("equipment") || norm.Contains("accessory") || norm.Contains("object") || norm.Contains("shield") || norm.Contains("armor")) { canonicalType = "Item"; return true; }
			return false;
		}
		else if (ext is ".ranim")
		{
			canonicalType = "Animation";
			return true;
		}
		else if (ext is ".raud" or ".ogg" or ".wav" or ".mp3" or ".flac" or ".aac")
		{
			if (norm.Contains("music") || norm.Contains("song") || norm.Contains("soundtrack") || norm.Contains("bgm")) { canonicalType = "Music"; return true; }
			if (norm.Contains("sound") || norm.Contains("sfx") || norm.Contains("effect") || norm.Contains("audio") || norm.Contains("fx")) { canonicalType = "SoundEffect"; return true; }
			return false;
		}
		else
		{
			if (norm.Contains("radial")) { canonicalType = "vfx_radial"; return true; }
			if (norm.Contains("vertical")) { canonicalType = "vfx_vertical"; return true; }
			if (norm.Contains("tile") || norm.Contains("terrain") || norm.Contains("texture") || norm.Contains("splat") || norm.Contains("ground")) { canonicalType = "Terrain"; return true; }
			if (norm.Contains("decal")) { canonicalType = "Decal"; return true; }
			if (norm.Contains("icon")) { canonicalType = "Icon"; return true; }
			if (norm.Contains("noise")) { canonicalType = "Noise"; return true; }
			if (norm.Contains("ribbon")) { canonicalType = "Ribbon"; return true; }
			if (norm.Contains("skybox")) { canonicalType = "Skybox"; return true; }
			if (norm.Contains("sprite") || norm.Contains("vfx") || norm.Contains("spell")) { canonicalType = "Spritesheet"; return true; }
			if (norm.Contains("character") || norm.Contains("unit")) { canonicalType = "Character"; return true; }
			if (norm.Contains("building") || norm.Contains("structure")) { canonicalType = "Building"; return true; }
			if (norm.Contains("environment") || norm.Contains("resource") || norm.Contains("prop") || norm.Contains("doodad") || norm.Contains("foliage") || norm.Contains("flora") || norm.Contains("rock") || norm.Contains("tree") || norm.Contains("nature")) { canonicalType = "Prop"; return true; }
			if (norm.Contains("item") || norm.Contains("attachment") || norm.Contains("weapon") || norm.Contains("projectile") || norm.Contains("gear") || norm.Contains("equipment") || norm.Contains("accessory") || norm.Contains("object") || norm.Contains("shield") || norm.Contains("armor")) { canonicalType = "Item"; return true; }
			if (norm.Contains("animation") || norm.Contains("anim")) { canonicalType = "Animation"; return true; }
			if (norm.Contains("music") || norm.Contains("song") || norm.Contains("soundtrack") || norm.Contains("bgm")) { canonicalType = "Music"; return true; }
			if (norm.Contains("sound") || norm.Contains("sfx") || norm.Contains("effect") || norm.Contains("audio") || norm.Contains("fx")) { canonicalType = "SoundEffect"; return true; }
		}

		return false;
	}

	public static string? ExtractAssetType(string filePath)
	{
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return null;
		try
		{
			var node = JsonNode.Parse(metaJson);
			if (node is JsonObject obj)
			{
				string? typeVal = obj["asset_type"]?.ToString()
					?? obj["AssetType"]?.ToString()
					?? obj["type"]?.ToString()
					?? obj["default_asset_type"]?.ToString();
				if (!string.IsNullOrEmpty(typeVal) && IsValidAssetTypeForExtension(filePath, typeVal, out string canonical, out _))
				{
					return canonical;
				}
			}
		}
		catch { }
		return null;
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

	public static bool? ExtractSupportsTeamColor(string filePath)
	{
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return null;
		try
		{
			var node = JsonNode.Parse(metaJson);
			if (node is JsonObject obj)
			{
				if (obj.TryGetPropertyValue("team_color", out var tcVal) && tcVal != null)
				{
					if (tcVal.GetValueKind() == System.Text.Json.JsonValueKind.True) return true;
					if (tcVal.GetValueKind() == System.Text.Json.JsonValueKind.False) return false;
				}
				if (obj.TryGetPropertyValue("chroma_key", out var val) && val != null)
				{
					if (val.GetValueKind() == System.Text.Json.JsonValueKind.True) return true;
					if (val.GetValueKind() == System.Text.Json.JsonValueKind.False) return false;
					if (val.GetValueKind() == System.Text.Json.JsonValueKind.String)
					{
						string s = val.GetValue<string>();
						return !string.IsNullOrWhiteSpace(s);
					}
				}
			}
		}
		catch { }
		return null;
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
			return node?["chroma_key"]?.ToString()
				?? node?["chromaKey"]?.ToString()
				?? node?["target_hex"]?.ToString()
				?? node?["targetHex"]?.ToString();
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
		if (string.IsNullOrWhiteSpace(metadataJson)) return false;
		try
		{
			var node = JsonNode.Parse(metadataJson);
			if (node is JsonObject obj)
			{
				if (obj.TryGetPropertyValue("is_compressed", out var compVal) && compVal != null)
				{
					if (compVal.GetValueKind() == JsonValueKind.True) return true;
					if (compVal.GetValueKind() == JsonValueKind.False) return false;
					if (bool.TryParse(compVal.ToString(), out bool b)) return b;
				}
				if (obj.TryGetPropertyValue("IsCompressed", out var compVal2) && compVal2 != null)
				{
					if (compVal2.GetValueKind() == JsonValueKind.True) return true;
					if (compVal2.GetValueKind() == JsonValueKind.False) return false;
					if (bool.TryParse(compVal2.ToString(), out bool b2)) return b2;
				}
			}
		}
		catch { }
		return false;
	}

	public static bool TryExtractIsCompressed(string? metadataJson, out bool isCompressed)
	{
		isCompressed = false;
		if (string.IsNullOrWhiteSpace(metadataJson)) return false;
		try
		{
			var node = JsonNode.Parse(metadataJson);
			if (node is JsonObject obj)
			{
				if (obj.TryGetPropertyValue("is_compressed", out var compVal) && compVal != null)
				{
					if (compVal.GetValueKind() == JsonValueKind.True) { isCompressed = true; return true; }
					if (compVal.GetValueKind() == JsonValueKind.False) { isCompressed = false; return true; }
					if (bool.TryParse(compVal.ToString(), out bool b)) { isCompressed = b; return true; }
				}
				if (obj.TryGetPropertyValue("IsCompressed", out var compVal2) && compVal2 != null)
				{
					if (compVal2.GetValueKind() == JsonValueKind.True) { isCompressed = true; return true; }
					if (compVal2.GetValueKind() == JsonValueKind.False) { isCompressed = false; return true; }
					if (bool.TryParse(compVal2.ToString(), out bool b2)) { isCompressed = b2; return true; }
				}
			}
		}
		catch { }
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

		string extension = string.Empty;
		if (!string.IsNullOrEmpty(extensionOrPath))
		{
			extension = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		}

		try
		{
			if (extension == ".rmesh" || ((string.IsNullOrEmpty(extension) || extension == ".bin") && RmeshFile.IsRmeshBytes(bytes)))
			{
				if (RealmContainerHeader.TryReadHeader(bytes, RmeshFile.Magic, out _, out _, out int payloadOffset))
				{
					return bytes.AsSpan(payloadOffset).ToArray();
				}
				return RmeshFile.SetMetadata(bytes, null);
			}
			if (extension == ".raud" || ((string.IsNullOrEmpty(extension) || extension == ".bin") && RaudFile.IsRaudBytes(bytes)))
			{
				if (RealmContainerHeader.TryReadHeader(bytes, RaudFile.Magic, out _, out _, out int payloadOffset))
				{
					return bytes.AsSpan(payloadOffset).ToArray();
				}
				return RaudFile.SetMetadata(bytes, null);
			}
			if (extension == ".rtex" || ((string.IsNullOrEmpty(extension) || extension == ".bin") && Realm.Shared.Textures.RtexFile.IsRtexBytes(bytes)))
			{
				if (RealmContainerHeader.TryReadHeader(bytes, Realm.Shared.Textures.RtexFile.Magic, out _, out _, out int payloadOffset))
				{
					return bytes.AsSpan(payloadOffset).ToArray();
				}
				return Realm.Shared.Textures.RtexFile.SetMetadata(bytes, null);
			}
			if (extension == ".ranim" || ((string.IsNullOrEmpty(extension) || extension == ".bin") && RanimFile.IsRanimBytes(bytes)))
			{
				if (RealmContainerHeader.TryReadHeader(bytes, RanimFile.Magic, out _, out _, out int payloadOffset))
				{
					return bytes.AsSpan(payloadOffset).ToArray();
				}
				return RanimFile.SetMetadata(bytes, null);
			}
			if (extension == ".rkey" || ((string.IsNullOrEmpty(extension) || extension == ".bin") && RkeyFile.IsRkeyBytes(bytes)))
			{
				if (RealmContainerHeader.TryReadHeader(bytes, RkeyFile.Magic, out _, out _, out int payloadOffset))
				{
					return bytes.AsSpan(payloadOffset).ToArray();
				}
				return RkeyFile.SetMetadata(bytes, null);
			}
		}
		catch
		{
			return bytes;
		}

		return bytes;
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
			JsonObject metaObj;
			if (!string.IsNullOrWhiteSpace(existingMeta))
			{
				try
				{
					metaObj = JsonNode.Parse(existingMeta) as JsonObject ?? new JsonObject();
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

			string canonicalBlake3 = !string.IsNullOrEmpty(precomputedCanonicalBlake3) ? precomputedCanonicalBlake3 : ComputeBlake3(filePath);
			metaObj["blake3"] = canonicalBlake3;
			return AddMetadata(filePath, metaObj.ToJsonString());
		}
		catch
		{
			return false;
		}
	}

	public static byte[] SyncBlake3MetadataBytes(byte[] bytes, string extensionOrPath)
	{
		if (bytes == null || bytes.Length == 0) return bytes ?? Array.Empty<byte>();
		string ext = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		if (string.IsNullOrEmpty(ext) && extensionOrPath.StartsWith('.')) ext = extensionOrPath.ToLowerInvariant();
		if (ext is not (".rtex" or ".ranim" or ".rmesh" or ".raud" or ".rkey")) return bytes;

		try
		{
			string canonicalBlake3 = ComputeBlake3(bytes, ext);
			string? existingMeta = null;
			if (ext == ".rmesh") existingMeta = RmeshFile.ExtractMetadata(bytes);
			else if (ext == ".rtex") existingMeta = RtexFile.ExtractMetadata(bytes);
			else if (ext == ".ranim") existingMeta = RanimFile.ExtractMetadata(bytes);
			else if (ext == ".raud") existingMeta = RaudFile.ExtractMetadata(bytes);
			else if (ext == ".rkey") existingMeta = RkeyFile.ExtractMetadata(bytes);

			JsonObject metaObj;
			if (!string.IsNullOrWhiteSpace(existingMeta))
			{
				try
				{
					metaObj = JsonNode.Parse(existingMeta) as JsonObject ?? new JsonObject();
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

			if (metaObj.TryGetPropertyValue("blake3", out var existingB3) && existingB3 != null && string.Equals(existingB3.ToString(), canonicalBlake3, StringComparison.OrdinalIgnoreCase))
			{
				return bytes;
			}

			metaObj["blake3"] = canonicalBlake3;
			string newMetaJson = metaObj.ToJsonString();

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
		catch
		{
			return bytes;
		}
	}
}
