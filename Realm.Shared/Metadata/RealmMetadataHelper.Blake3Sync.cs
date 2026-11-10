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

/// <summary>syncing the stored hash with the bytes.</summary>
public static partial class RealmMetadataHelper
	{
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
