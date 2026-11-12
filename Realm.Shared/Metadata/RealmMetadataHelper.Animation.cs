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

/// <summary>animation metadata, in files and in bytes.</summary>
public static partial class RealmMetadataHelper
	{
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
}
