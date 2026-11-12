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

/// <summary>the asset type tables and the normalisation and validation over them.</summary>
public static partial class RealmMetadataHelper
	{
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
}
