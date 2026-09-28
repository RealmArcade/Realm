using System;
using System.IO;
using System.Text.Json;

namespace Realm.Shared.Metadata;

public class RkeyFile : RealmContainerFile
{
	public static readonly byte[] MagicBytes = [0x52, 0x4B, 0x45, 0x59]; // "RKEY"
	public static byte[] Magic => MagicBytes;
	public const uint CurrentVersion = 1;
	public const string Format = "rkey";
	public const string Name = "RKEY";

	public override ReadOnlySpan<byte> ContainerMagic => MagicBytes;
	public override string ContainerFormatName => Name;
	public override string ContainerFormatExtension => Format;
	public override bool DefaultCompressed => false;

	public static bool IsRkeyBytes(ReadOnlySpan<byte> bytes) => HasMagic(bytes, MagicBytes);

	public static (string? MetadataJson, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		var (metadataJson, _, version) = ParsePayload(bytes, MagicBytes, Name, defaultCompressed: false);
		return (metadataJson, version);
	}

	public static AuthorshipKeyData? ParseKeyData(ReadOnlySpan<byte> bytes)
	{
		var (metadataJson, _) = Parse(bytes);
		if (string.IsNullOrWhiteSpace(metadataJson)) return null;
		return JsonSerializer.Deserialize<AuthorshipKeyData>(metadataJson);
	}

	public static byte[] Build(string? metadataJson, uint version = CurrentVersion)
	{
		using var memoryStream = new MemoryStream();
		using var writer = new BinaryWriter(memoryStream);
		RealmContainerHeader.WriteHeader(writer, MagicBytes, metadataJson, version);
		return memoryStream.ToArray();
	}

	public static byte[] Build(string userName, string publicKeyBase64, string privateKeyBase64, uint version = CurrentVersion)
	{
		var keyData = new AuthorshipKeyData
		{
			UserName = userName,
			PublicKey = publicKeyBase64,
			PrivateKey = privateKeyBase64
		};
		string json = JsonSerializer.Serialize(keyData, new JsonSerializerOptions { WriteIndented = true });
		return Build(json, version);
	}

	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes) => ExtractMetadataFromBytes(bytes, MagicBytes);
	public static string? ExtractMetadata(Stream stream) => ExtractMetadataFromStream(stream, MagicBytes);
	public static string? ExtractMetadataFromFile(string filePath) => ExtractMetadataFromPath(filePath, MagicBytes);

	public static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		return RealmContainerHeader.SetMetadata(bytes, MagicBytes, newMetadataJson, Name);
	}
}
