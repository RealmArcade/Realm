using Realm.Shared.Metadata;
using System.Buffers.Binary;

namespace Realm.Shared.Audio;

public class RaudFile : RealmContainerFile
{
	public static readonly byte[] MagicBytes = [0x52, 0x41, 0x55, 0x44]; // "RAUD"
	public static byte[] Magic => MagicBytes;
	public const uint CurrentVersion = 1;
	public const string Format = "raud";
	public const string Name = "RAUD";

	public override ReadOnlySpan<byte> ContainerMagic => MagicBytes;
	public override string ContainerFormatName => Name;
	public override string ContainerFormatExtension => Format;
	public override bool DefaultCompressed => false;

	public static bool IsRaudBytes(ReadOnlySpan<byte> bytes) => HasMagic(bytes, MagicBytes);

	public static (string? MetadataJson, List<byte[]> Tracks, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		var (metadataJson, decompressedPayload, version) = ParsePayload(bytes, MagicBytes, Name, defaultCompressed: false);
		var tracks = UnpackTracks(decompressedPayload);
		return (metadataJson, tracks, version);
	}

	public static byte[] Build(string? metadataJson, IList<byte[]> tracks, bool? compressed = null, uint version = CurrentVersion)
	{
		byte[] rawPayloadBytes = PackTracks(tracks);
		return BuildContainer(MagicBytes, Format, metadataJson, rawPayloadBytes, compressed, version, defaultCompressed: false);
	}

	public static byte[]? GetTrack(ReadOnlySpan<byte> bytes, int trackIndex = 0)
	{
		if (!IsRaudBytes(bytes)) return null;
		var (_, tracks, _) = Parse(bytes);
		return (trackIndex >= 0 && trackIndex < tracks.Count) ? tracks[trackIndex] : null;
	}

	public static byte[]? GetTrack(Stream stream, int trackIndex = 0)
	{
		var (metadataJson, decompressedPayload, _) = ParsePayload(stream, MagicBytes, Name, defaultCompressed: false);
		if (decompressedPayload.Length == 0 && string.IsNullOrEmpty(metadataJson)) return null;
		var tracks = UnpackTracks(decompressedPayload);
		return (trackIndex >= 0 && trackIndex < tracks.Count) ? tracks[trackIndex] : null;
	}

	public static byte[]? GetTrackFromFile(string filePath, int trackIndex = 0)
	{
		if (!File.Exists(filePath)) return null;
		try
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			return GetTrack(bytes, trackIndex);
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

	public static byte[] PackTracks(IList<byte[]>? tracks)
	{
		using var rawPayloadStream = new MemoryStream();
		using var rawWriter = new BinaryWriter(rawPayloadStream);

		rawWriter.Write((uint)(tracks?.Count ?? 0));
		if (tracks != null)
		{
			foreach (var track in tracks)
			{
				rawWriter.Write((uint)(track?.Length ?? 0));
				if (track != null && track.Length > 0)
				{
					rawWriter.Write(track);
				}
			}
		}
		rawWriter.Flush();
		return rawPayloadStream.ToArray();
	}

	public static List<byte[]> UnpackTracks(ReadOnlySpan<byte> tracksSpan)
	{
		var tracks = new List<byte[]>();
		int readOffset = 0;
		if (readOffset + 4 <= tracksSpan.Length)
		{
			uint trackCount = BinaryPrimitives.ReadUInt32LittleEndian(tracksSpan.Slice(readOffset, 4));
			readOffset += 4;

			for (int i = 0; i < trackCount; i++)
			{
				if (readOffset + 4 > tracksSpan.Length) break;
				uint trackLength = BinaryPrimitives.ReadUInt32LittleEndian(tracksSpan.Slice(readOffset, 4));
				readOffset += 4;

				if (readOffset + (int)trackLength > tracksSpan.Length) break;
				byte[] trackData = tracksSpan.Slice(readOffset, (int)trackLength).ToArray();
				tracks.Add(trackData);
				readOffset += (int)trackLength;
			}
		}

		return tracks;
	}
}
