using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Shared.Metadata;

public class RkeyFile : RealmContainerFile
{
	public static readonly byte[] MagicBytes = [0x52, 0x4B, 0x45, 0x59];
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
		return Build(keyData, version);
	}

	public static byte[] Build(AuthorshipKeyData keyData, uint version = CurrentVersion)
	{
		var options = new JsonSerializerOptions
		{
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};
		string json = JsonSerializer.Serialize(keyData, options);
		return Build(json, version);
	}

}