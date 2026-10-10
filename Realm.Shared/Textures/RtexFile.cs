using Realm.Shared.Metadata;
using System.Buffers.Binary;

namespace Realm.Shared.Textures;

public class RtexFile : RealmContainerFile
{
	public static readonly byte[] MagicBytes = [0x52, 0x54, 0x45, 0x58]; // "RTEX"
	public static byte[] Magic => MagicBytes;
	public const uint CurrentVersion = 1;
	public const string Format = "rtex";
	public const string Name = "RTEX";

	public override ReadOnlySpan<byte> ContainerMagic => MagicBytes;
	public override string ContainerFormatName => Name;
	public override string ContainerFormatExtension => Format;
	public override bool DefaultCompressed => false;

	public static bool IsRtexBytes(ReadOnlySpan<byte> bytes) => HasMagic(bytes, MagicBytes);

	public static (string? MetadataJson, List<byte[]> Layers, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		var (metadataJson, decompressedPayload, version) = ParsePayload(bytes, MagicBytes, Name, defaultCompressed: false);
		var layers = UnpackLayers(decompressedPayload);
		return (metadataJson, layers, version);
	}

	public static byte[] Build(string? metadataJson, IList<byte[]> layers, bool? compressed = null, uint version = CurrentVersion)
	{
		byte[] rawPayloadBytes = PackLayers(layers);
		return BuildContainer(MagicBytes, Format, metadataJson, rawPayloadBytes, compressed, version, defaultCompressed: false);
	}

	public static byte[]? GetLayer(ReadOnlySpan<byte> bytes, int layerIndex)
	{
		if (!IsRtexBytes(bytes)) return null;
		var (_, layers, _) = Parse(bytes);
		return (layerIndex >= 0 && layerIndex < layers.Count) ? layers[layerIndex] : null;
	}

	public static byte[]? GetLayer(Stream stream, int layerIndex = 0)
	{
		var (metadataJson, decompressedPayload, _) = ParsePayload(stream, MagicBytes, Name, defaultCompressed: false);
		if (decompressedPayload.Length == 0 && string.IsNullOrEmpty(metadataJson)) return null;
		var layers = UnpackLayers(decompressedPayload);
		return (layerIndex >= 0 && layerIndex < layers.Count) ? layers[layerIndex] : null;
	}

	public static byte[]? GetLayerFromFile(string filePath, int layerIndex = 0)
	{
		if (!File.Exists(filePath)) return null;
		try
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			return GetLayer(bytes, layerIndex);
		}
		catch
		{
			return null;
		}
	}

	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes) => ExtractMetadataFromBytes(bytes, MagicBytes);
	public static string? ExtractMetadata(Stream stream) => ExtractMetadataFromStream(stream, MagicBytes);
	public static string? ExtractMetadataFromFile(string filePath) => ExtractMetadataFromPath(filePath, MagicBytes);

	public static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		return SetContainerMetadata(bytes, MagicBytes, Name, Format, newMetadataJson);
	}

	public static byte[] PackLayers(IList<byte[]>? layers)
	{
		using var rawPayloadStream = new MemoryStream();
		using var rawWriter = new BinaryWriter(rawPayloadStream);

		rawWriter.Write((uint)(layers?.Count ?? 0));
		if (layers != null)
		{
			foreach (var layer in layers)
			{
				rawWriter.Write((uint)(layer?.Length ?? 0));
				if (layer != null && layer.Length > 0)
				{
					rawWriter.Write(layer);
				}
			}
		}
		rawWriter.Flush();
		return rawPayloadStream.ToArray();
	}

	public static List<byte[]> UnpackLayers(ReadOnlySpan<byte> layersSpan)
	{
		var layers = new List<byte[]>();
		int readOffset = 0;
		if (readOffset + 4 <= layersSpan.Length)
		{
			uint layerCount = BinaryPrimitives.ReadUInt32LittleEndian(layersSpan.Slice(readOffset, 4));
			readOffset += 4;

			for (int i = 0; i < layerCount; i++)
			{
				if (readOffset + 4 > layersSpan.Length) break;
				uint layerLength = BinaryPrimitives.ReadUInt32LittleEndian(layersSpan.Slice(readOffset, 4));
				readOffset += 4;

				if (readOffset + (int)layerLength > layersSpan.Length) break;
				byte[] layerData = layersSpan.Slice(readOffset, (int)layerLength).ToArray();
				layers.Add(layerData);
				readOffset += (int)layerLength;
			}
		}

		return layers;
	}
}
