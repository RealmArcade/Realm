using System.Buffers.Binary;
using Realm.Shared.Metadata;

namespace Realm.Shared.ModelOptimization;

public class RmeshFile : RealmContainerFile
{
	public static readonly byte[] MagicBytes = [0x52, 0x4D, 0x53, 0x48]; // "RMSH"
	public static byte[] Magic => MagicBytes;
	public const uint CurrentVersion = 1;
	public const string Format = "rmesh";
	public const string Name = "RMESH";
	public const int SourceProfileImageQuality = 80;

	public static readonly byte[] SourceProfileImageTrailerMagic = [0x52, 0x4D, 0x53, 0x50]; // "RMSP"
	public const ushort SourceProfileImageTrailerVersion = 1;
	public const int SourceProfileImageTrailerHeaderLength = 10;

	public override ReadOnlySpan<byte> ContainerMagic => MagicBytes;
	public override string ContainerFormatName => Name;
	public override string ContainerFormatExtension => Format;
	public override bool DefaultCompressed => true;

	public static bool IsRmeshBytes(ReadOnlySpan<byte> bytes)
	{
		return HasMagic(bytes, MagicBytes);
	}

	public static (string? MetadataJson, byte[] GlbBytes, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		var (metadataJson, decompressedPayload, version) = ParsePayload(bytes, MagicBytes, Name, defaultCompressed: true);
		byte[] glbBytes = ExtractGlbFromDecompressedPayload(decompressedPayload);
		return (metadataJson, glbBytes, version);
	}

	public static byte[] Build(string? metadataJson, byte[] glbBytes, bool? compressed = null, uint version = CurrentVersion, byte[]? sourceProfileImageWebp = null)
	{
		byte[] payload = ComposeMeshPayload(glbBytes ?? Array.Empty<byte>(), sourceProfileImageWebp);
		return BuildContainer(MagicBytes, Format, metadataJson, payload, compressed, version, defaultCompressed: true);
	}

	public static bool TryGetSourceProfileImageWebp(ReadOnlySpan<byte> rmeshBytes, out byte[] imageWebpBytes)
	{
		imageWebpBytes = Array.Empty<byte>();
		if (!IsRmeshBytes(rmeshBytes)) return false;

		var (_, decompressedPayload, _) = ParsePayload(rmeshBytes, MagicBytes, Name, defaultCompressed: true);
		return TryReadSourceProfileImageFromPayload(decompressedPayload, out imageWebpBytes);
	}

	public static byte[]? GetSourceProfileImageWebpFromFile(string filePath)
	{
		if (!File.Exists(filePath)) return null;

		try
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			return TryGetSourceProfileImageWebp(bytes, out byte[] imageBytes) ? imageBytes : null;
		}
		catch
		{
			return null;
		}
	}

	public static bool TryReadSourceProfileImageFromPayload(ReadOnlySpan<byte> decompressedPayload, out byte[] imageWebpBytes)
	{
		imageWebpBytes = Array.Empty<byte>();
		if (!TryGetGlbLength(decompressedPayload, out int glbLength)) return false;
		if ((long)glbLength + SourceProfileImageTrailerHeaderLength > decompressedPayload.Length) return false;

		ReadOnlySpan<byte> trailer = decompressedPayload.Slice(glbLength);
		if (!trailer.Slice(0, 4).SequenceEqual(SourceProfileImageTrailerMagic)) return false;

		ushort trailerVersion = BinaryPrimitives.ReadUInt16LittleEndian(trailer.Slice(4, 2));
		if (trailerVersion != SourceProfileImageTrailerVersion) return false;

		uint imageLength = BinaryPrimitives.ReadUInt32LittleEndian(trailer.Slice(6, 4));
		if (imageLength == 0 || SourceProfileImageTrailerHeaderLength + (long)imageLength > trailer.Length) return false;

		imageWebpBytes = trailer.Slice(SourceProfileImageTrailerHeaderLength, (int)imageLength).ToArray();
		return true;
	}

	private static byte[] ComposeMeshPayload(byte[] glbBytes, byte[]? sourceProfileImageWebp)
	{
		int imageLength = sourceProfileImageWebp?.Length ?? 0;
		int trailerLength = imageLength > 0 ? SourceProfileImageTrailerHeaderLength + imageLength : 0;
		byte[] payload = new byte[glbBytes.Length + trailerLength];

		Buffer.BlockCopy(glbBytes, 0, payload, 0, glbBytes.Length);

		if (trailerLength > 0)
		{
			Span<byte> trailer = payload.AsSpan(glbBytes.Length);
			SourceProfileImageTrailerMagic.CopyTo(trailer);
			BinaryPrimitives.WriteUInt16LittleEndian(trailer.Slice(4, 2), SourceProfileImageTrailerVersion);
			BinaryPrimitives.WriteUInt32LittleEndian(trailer.Slice(6, 4), (uint)imageLength);
			sourceProfileImageWebp!.CopyTo(trailer.Slice(SourceProfileImageTrailerHeaderLength));
		}

		return payload;
	}

	private const uint GlbMagic = 0x46546C67; // 'glTF'

	private static bool TryGetGlbLength(ReadOnlySpan<byte> payload, out int glbLength)
	{
		glbLength = 0;
		if (payload.Length < 12) return false;
		if (BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4)) != GlbMagic) return false;

		uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));
		if (declaredLength == 0 || declaredLength > payload.Length) return false;

		glbLength = (int)declaredLength;
		return true;
	}

	public static byte[]? GetGlbBytes(ReadOnlySpan<byte> bytes)
	{
		if (!IsRmeshBytes(bytes)) return null;
		var (_, glbBytes, _) = Parse(bytes);
		return glbBytes;
	}

	public static byte[]? GetGlbBytes(Stream stream)
	{
		var (metadataJson, decompressedPayload, _) = ParsePayload(stream, MagicBytes, Name, defaultCompressed: true);
		if (decompressedPayload.Length == 0 && string.IsNullOrEmpty(metadataJson)) return null;
		return ExtractGlbFromDecompressedPayload(decompressedPayload);
	}

	public static byte[]? GetGlbBytesFromFile(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		try
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			return GetGlbBytes(bytes);
		}
		catch
		{
			return null;
		}
	}


	private static byte[] ExtractGlbFromDecompressedPayload(byte[] payload)
	{
		if (TryGetGlbLength(payload, out int glbLength))
		{
			if (glbLength == payload.Length) return payload;

			byte[] glbBytesFromGlbHeader = new byte[glbLength];
			Buffer.BlockCopy(payload, 0, glbBytesFromGlbHeader, 0, glbLength);
			return glbBytesFromGlbHeader;
		}

		if (payload.Length >= 8)
		{
			uint lengthPrefixedGlbLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4));
			if (lengthPrefixedGlbLength > 0 && lengthPrefixedGlbLength <= (uint)(payload.Length - 4))
			{
				byte[] lengthPrefixedGlbBytes = new byte[lengthPrefixedGlbLength];
				Buffer.BlockCopy(payload, 4, lengthPrefixedGlbBytes, 0, (int)lengthPrefixedGlbLength);
				return lengthPrefixedGlbBytes;
			}
		}

		return payload;
	}
}