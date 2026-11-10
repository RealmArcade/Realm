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

/// <summary>the per-format metadata readers and writers.</summary>
public static partial class RealmMetadataHelper
	{
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
}
