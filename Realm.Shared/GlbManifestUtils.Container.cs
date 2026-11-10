using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>parsing and building the glb container.</summary>
public static partial class GlbManifestUtils
	{
	public static (JsonNode? Json, byte[]? BinChunk, uint Version) ParseGlb(byte[] glbBytes)
	{
		if (!IsValidGlb(glbBytes)) return (null, null, 0);

		uint version = BitConverter.ToUInt32(glbBytes, 4);
		int currentOffset = 12;

		if (currentOffset + 8 > glbBytes.Length) return (null, null, version);

		uint jsonLength = BitConverter.ToUInt32(glbBytes, currentOffset);
		uint jsonType = BitConverter.ToUInt32(glbBytes, currentOffset + 4);
		if (jsonType != JsonChunkType || currentOffset + 8 + jsonLength > glbBytes.Length) return (null, null, version);

		string jsonString = Encoding.UTF8.GetString(glbBytes, currentOffset + 8, (int)jsonLength);
		var jsonNode = JsonNode.Parse(jsonString);

		int binOffset = currentOffset + 8 + (int)jsonLength;
		byte[]? binChunk = null;
		if (binOffset + 8 <= glbBytes.Length)
		{
			uint binLength = BitConverter.ToUInt32(glbBytes, binOffset);
			uint binType = BitConverter.ToUInt32(glbBytes, binOffset + 4);
			if (binType == BinChunkType && binOffset + 8 + binLength <= glbBytes.Length)
			{
				binChunk = new byte[binLength];
				Array.Copy(glbBytes, binOffset + 8, binChunk, 0, binLength);
			}
			else if (binOffset + 8 < glbBytes.Length)
			{
				int remaining = glbBytes.Length - (binOffset + 8);
				binChunk = new byte[remaining];
				Array.Copy(glbBytes, binOffset + 8, binChunk, 0, remaining);
			}
		}

		return (jsonNode, binChunk, version);
	}

	public static byte[] BuildGlb(JsonNode jsonNode, byte[]? binChunk, uint version = 2)
	{
		byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonNode.ToJsonString());
		int paddedJsonLength = (jsonBytes.Length + 3) & ~3;
		byte[] paddedJson = new byte[paddedJsonLength];
		Array.Copy(jsonBytes, paddedJson, jsonBytes.Length);
		for (int i = jsonBytes.Length; i < paddedJsonLength; i++)
		{
			paddedJson[i] = GlbJsonPaddingSpace;
		}

		int paddedBinLength = binChunk != null ? ((binChunk.Length + 3) & ~3) : 0;
		byte[]? paddedBin = null;
		if (binChunk != null && binChunk.Length > 0)
		{
			paddedBin = new byte[paddedBinLength];
			Array.Copy(binChunk, paddedBin, binChunk.Length);
		}

		using var ms = new MemoryStream();
		using var writer = new BinaryWriter(ms);

		writer.Write(GlbMagic);
		writer.Write(version);

		uint totalLength = 12 + 8 + (uint)paddedJsonLength;
		if (paddedBin != null)
		{
			totalLength += 8 + (uint)paddedBinLength;
		}

		writer.Write(totalLength);

		WriteGlbChunk(writer, JsonChunkType, paddedJson);

		if (paddedBin != null)
		{
			WriteGlbChunk(writer, BinChunkType, paddedBin);
		}

		return ms.ToArray();
	}

	private static void WriteGlbChunk(BinaryWriter writer, uint chunkType, byte[] chunkBytes)
	{
		writer.Write((uint)chunkBytes.Length);
		writer.Write(chunkType);
		writer.Write(chunkBytes);
	}
}
