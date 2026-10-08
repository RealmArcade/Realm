using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json.Nodes;
using Blake3;

namespace Realm.Shared.Metadata;

public abstract class RealmContainerFile
{
	public const uint DefaultVersion = 1;

	public abstract ReadOnlySpan<byte> ContainerMagic { get; }
	public abstract string ContainerFormatName { get; }
	public abstract string ContainerFormatExtension { get; }
	public virtual bool DefaultCompressed => true;
	public virtual uint FormatVersion => DefaultVersion;

	public bool Matches(ReadOnlySpan<byte> bytes) => HasMagic(bytes, ContainerMagic);

	public (string? MetadataJson, byte[] DecompressedPayload, uint Version) ParseContainer(ReadOnlySpan<byte> bytes)
	{
		return ParsePayload(bytes, ContainerMagic, ContainerFormatName, DefaultCompressed);
	}

	public (string? MetadataJson, byte[] DecompressedPayload, uint Version) ParseContainer(Stream stream)
	{
		return ParsePayload(stream, ContainerMagic, ContainerFormatName, DefaultCompressed);
	}

	public byte[] BuildContainerBytes(
		string? metadataJson,
		ReadOnlySpan<byte> uncompressedPayload,
		bool? compressed = null,
		uint? version = null,
		Action<JsonObject>? configureMetadata = null)
	{
		return BuildContainer(
			ContainerMagic,
			ContainerFormatExtension,
			metadataJson,
			uncompressedPayload,
			compressed,
			version ?? FormatVersion,
			DefaultCompressed,
			configureMetadata);
	}

	public byte[] SetContainerMetadataBytes(ReadOnlySpan<byte> bytes, string? newMetadataJson, Action<JsonObject>? configureMetadata = null)
	{
		return SetContainerMetadata(bytes, ContainerMagic, ContainerFormatName, ContainerFormatExtension, newMetadataJson, configureMetadata);
	}

	public static bool HasMagic(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> expectedMagic)
	{
		return RealmContainerHeader.HasMagic(bytes, expectedMagic);
	}

	public static (string? MetadataJson, byte[] DecompressedPayload, uint Version) ParsePayload(
		ReadOnlySpan<byte> bytes,
		ReadOnlySpan<byte> expectedMagic,
		string formatName,
		bool defaultCompressed = true)
	{
		var (version, metadataJson, offset) = RealmContainerHeader.ReadHeader(bytes, expectedMagic, formatName);
		ReadOnlySpan<byte> payloadSpan = bytes.Slice(offset);
		bool isCompressed = RealmCompressionHelper.IsZstdCompressed(payloadSpan) || RealmMetadataHelper.ExtractIsCompressed(metadataJson);

		byte[] decompressedPayload = isCompressed
			? RealmCompressionHelper.Decompress(payloadSpan)
			: payloadSpan.ToArray();

		return (metadataJson, decompressedPayload, version);
	}

	public static (string? MetadataJson, byte[] DecompressedPayload, uint Version) ParsePayload(
		Stream stream,
		ReadOnlySpan<byte> expectedMagic,
		string formatName,
		bool defaultCompressed = true)
	{
		Span<byte> header = stackalloc byte[RealmContainerHeader.MinimumHeaderLength];
		int bytesRead = 0;
		while (bytesRead < RealmContainerHeader.MinimumHeaderLength)
		{
			int r = stream.Read(header.Slice(bytesRead, RealmContainerHeader.MinimumHeaderLength - bytesRead));
			if (r <= 0) return (null, Array.Empty<byte>(), 0);
			bytesRead += r;
		}

		if (!RealmContainerHeader.HasMagic(header, expectedMagic)) return (null, Array.Empty<byte>(), 0);

		uint version = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4, 4));
		uint metadataLength = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8, 4));
		string? metadataJson = null;
		if (metadataLength > 0)
		{
			byte[] metaBytes = new byte[metadataLength];
			int metaRead = 0;
			while (metaRead < metadataLength)
			{
				int r = stream.Read(metaBytes, metaRead, (int)metadataLength - metaRead);
				if (r <= 0) return (null, Array.Empty<byte>(), 0);
				metaRead += r;
			}
			metadataJson = System.Text.Encoding.UTF8.GetString(metaBytes);
		}

		using var payloadStream = new MemoryStream();
		stream.CopyTo(payloadStream);
		byte[] payload = payloadStream.ToArray();

		bool isCompressed = RealmCompressionHelper.IsZstdCompressed(payload) || RealmMetadataHelper.ExtractIsCompressed(metadataJson);
		byte[] decompressedPayload = isCompressed
			? RealmCompressionHelper.Decompress(payload)
			: payload;

		return (metadataJson, decompressedPayload, version);
	}

	public static (string? MetadataJson, byte[] DecompressedPayload, uint Version) ParsePayloadFromFile(
		string filePath,
		ReadOnlySpan<byte> expectedMagic,
		string formatName,
		bool defaultCompressed = true)
	{
		if (!File.Exists(filePath)) return (null, Array.Empty<byte>(), 0);
		try
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			return ParsePayload(bytes, expectedMagic, formatName, defaultCompressed);
		}
		catch
		{
			return (null, Array.Empty<byte>(), 0);
		}
	}

	public static byte[] BuildContainer(
		ReadOnlySpan<byte> expectedMagic,
		string formatExtension,
		string? metadataJson,
		ReadOnlySpan<byte> uncompressedPayload,
		bool? compressed = null,
		uint version = RealmContainerHeader.DefaultVersion,
		bool defaultCompressed = true,
		Action<JsonObject>? configureMetadata = null)
	{
		bool isCompressed;
		if (compressed.HasValue)
		{
			isCompressed = compressed.Value;
		}
		else if (!string.IsNullOrWhiteSpace(metadataJson) && RealmMetadataHelper.TryExtractIsCompressed(metadataJson, out bool metaCompressed))
		{
			isCompressed = metaCompressed;
		}
		else
		{
			isCompressed = defaultCompressed;
		}

		byte[] payload = isCompressed
			? RealmCompressionHelper.Compress(uncompressedPayload, RealmCompressionHelper.DefaultCompressionLevel)
			: uncompressedPayload.ToArray();

		string canonicalBlake3 = Hasher.Hash(payload).ToString();

		JsonObject metaObj = ParseMetadataSafe(metadataJson);

		if (!metaObj.ContainsKey("created_utc") || metaObj["created_utc"] == null)
		{
			metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
		}

		metaObj["format"] = formatExtension.ToLowerInvariant().TrimStart('.');
		metaObj["is_compressed"] = isCompressed;
		metaObj["blake3"] = canonicalBlake3;

		configureMetadata?.Invoke(metaObj);

		using var memoryStream = new MemoryStream();
		using var writer = new BinaryWriter(memoryStream);

		RealmContainerHeader.WriteHeader(writer, expectedMagic, metaObj.ToJsonString(), version);
		if (payload.Length > 0)
		{
			writer.Write(payload);
		}

		return memoryStream.ToArray();
	}

	public static byte[] SetContainerMetadata(
		ReadOnlySpan<byte> bytes,
		ReadOnlySpan<byte> expectedMagic,
		string formatName,
		string formatExtension,
		string? newMetadataJson,
		Action<JsonObject>? configureMetadata = null)
	{
		var (version, oldMetadataJson, payloadOffset) = RealmContainerHeader.ReadHeader(bytes, expectedMagic, formatName);
		var payloadSpan = bytes.Slice(payloadOffset);

		bool isCompressed = RealmCompressionHelper.IsZstdCompressed(payloadSpan);
		if (!isCompressed && !string.IsNullOrWhiteSpace(oldMetadataJson) && RealmMetadataHelper.TryExtractIsCompressed(oldMetadataJson, out bool oldCompressed))
		{
			isCompressed = oldCompressed;
		}

		JsonObject metaObj = ParseMetadataSafe(newMetadataJson);

		if (!metaObj.ContainsKey("created_utc") || metaObj["created_utc"] == null)
		{
			string? oldCreatedUtc = ExtractCreatedUtcSafe(oldMetadataJson);
			metaObj["created_utc"] = oldCreatedUtc ?? DateTime.UtcNow.ToString("O");
		}

		metaObj["format"] = formatExtension.ToLowerInvariant().TrimStart('.');
		metaObj["is_compressed"] = isCompressed;
		if (!metaObj.ContainsKey("blake3") || string.IsNullOrWhiteSpace(metaObj["blake3"]?.ToString()))
		{
			metaObj["blake3"] = Hasher.Hash(payloadSpan).ToString();
		}

		configureMetadata?.Invoke(metaObj);

		using var memoryStream = new MemoryStream();
		using var writer = new BinaryWriter(memoryStream);

		RealmContainerHeader.WriteHeader(writer, expectedMagic, metaObj.ToJsonString(), version);
		if (payloadSpan.Length > 0)
		{
			writer.Write(payloadSpan);
		}

		return memoryStream.ToArray();
	}

	public static string? ExtractMetadataFromBytes(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> expectedMagic)
	{
		return RealmContainerHeader.ExtractMetadata(bytes, expectedMagic);
	}

	public static string? ExtractMetadataFromStream(Stream stream, ReadOnlySpan<byte> expectedMagic)
	{
		return RealmContainerHeader.ExtractMetadata(stream, expectedMagic);
	}

	public static string? ExtractMetadataFromPath(string filePath, ReadOnlySpan<byte> expectedMagic)
	{
		return RealmContainerHeader.ExtractMetadataFromFile(filePath, expectedMagic);
	}

	private static JsonObject ParseMetadataSafe(string? metadataJson)
	{
		if (string.IsNullOrWhiteSpace(metadataJson)) return new JsonObject();
		try
		{
			return JsonNode.Parse(metadataJson)?.AsObject() ?? new JsonObject();
		}
		catch
		{
			return new JsonObject();
		}
	}

	private static string? ExtractCreatedUtcSafe(string? metadataJson)
	{
		if (string.IsNullOrWhiteSpace(metadataJson)) return null;
		try
		{
			return JsonNode.Parse(metadataJson)?["created_utc"]?.ToString();
		}
		catch
		{
			return null;
		}
	}
}
