using System.Buffers.Binary;
using System.Text;
using Realm.Shared.Metadata;

namespace Realm.Shared.Audio;

public readonly record struct RaudEntry(string Name, byte[] Data);

public class RaudFile : RealmContainerFile
{
	public static readonly byte[] MagicBytes = [0x52, 0x41, 0x55, 0x44]; // "RAUD"
	public static byte[] Magic => MagicBytes;
	public const uint CurrentVersion = 2;
	public const uint NamedEntriesVersion = 2;
	public const string Format = "raud";
	public const string Name = "RAUD";

	public static readonly string[] EntryAudioExtensions =
	[
		".ogg", ".opus", ".wav", ".mp3", ".flac", ".aac", ".m4a", ".aiff", ".aif", ".wma"
	];

	public override ReadOnlySpan<byte> ContainerMagic => MagicBytes;
	public override string ContainerFormatName => Name;
	public override string ContainerFormatExtension => Format;
	public override bool DefaultCompressed => false;

	public static bool IsRaudBytes(ReadOnlySpan<byte> bytes) => HasMagic(bytes, MagicBytes);

	public static (string? MetadataJson, List<RaudEntry> Entries, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		var (metadataJson, decompressedPayload, version) = ParsePayload(bytes, MagicBytes, Name, defaultCompressed: false);
		var entries = UnpackEntries(decompressedPayload, version);
		return (metadataJson, entries, version);
	}

	public static (string? MetadataJson, List<RaudEntry> Entries, uint Version) Parse(Stream stream)
	{
		var (metadataJson, decompressedPayload, version) = ParsePayload(stream, MagicBytes, Name, defaultCompressed: false);
		if (decompressedPayload.Length == 0 && string.IsNullOrEmpty(metadataJson)) return (null, new List<RaudEntry>(), 0);
		var entries = UnpackEntries(decompressedPayload, version);
		return (metadataJson, entries, version);
	}

	public static byte[] Build(string? metadataJson, IList<RaudEntry> entries, bool? compressed = null, uint version = CurrentVersion)
	{
		byte[] rawPayloadBytes = PackEntries(entries);
		return BuildContainer(MagicBytes, Format, metadataJson, rawPayloadBytes, compressed, version, defaultCompressed: false);
	}

	public static byte[]? GetTrack(ReadOnlySpan<byte> bytes, int trackIndex = 0)
	{
		if (!IsRaudBytes(bytes)) return null;
		var (_, entries, _) = Parse(bytes);
		return GetTrackFromEntries(entries, trackIndex);
	}

	public static byte[]? GetTrack(Stream stream, int trackIndex = 0)
	{
		var (_, entries, _) = Parse(stream);
		return GetTrackFromEntries(entries, trackIndex);
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

	public static byte[]? GetEntry(ReadOnlySpan<byte> bytes, string? entryName)
	{
		return TryGetEntry(bytes, entryName, out byte[] data) ? data : null;
	}

	public static byte[]? GetEntry(Stream stream, string? entryName)
	{
		var (_, entries, _) = Parse(stream);
		return GetEntryFromEntries(entries, entryName);
	}

	public static byte[]? GetEntryFromFile(string filePath, string? entryName)
	{
		if (!File.Exists(filePath)) return null;
		try
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			return GetEntry(bytes, entryName);
		}
		catch
		{
			return null;
		}
	}

	public static bool TryGetEntry(ReadOnlySpan<byte> bytes, string? entryName, out byte[] data)
	{
		data = Array.Empty<byte>();
		if (!IsRaudBytes(bytes)) return false;

		var (_, entries, _) = Parse(bytes);
		return TryGetEntryFromEntries(entries, entryName, out data);
	}

	public static bool TryGetEntry(Stream stream, string? entryName, out byte[] data)
	{
		data = Array.Empty<byte>();
		var (_, entries, _) = Parse(stream);
		return TryGetEntryFromEntries(entries, entryName, out data);
	}

	public static bool TryGetEntryFromFile(string filePath, string? entryName, out byte[] data)
	{
		data = Array.Empty<byte>();
		if (!File.Exists(filePath)) return false;
		try
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			return TryGetEntry(bytes, entryName, out data);
		}
		catch
		{
			return false;
		}
	}

	public static List<string> GetEntryNames(ReadOnlySpan<byte> bytes)
	{
		if (!IsRaudBytes(bytes)) return new List<string>();
		var (_, entries, _) = Parse(bytes);
		var names = new List<string>(entries.Count);
		foreach (var entry in entries) names.Add(entry.Name);
		return names;
	}

	public static List<string> GetEntryNamesFromFile(string filePath)
	{
		if (!File.Exists(filePath)) return new List<string>();
		try
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			return GetEntryNames(bytes);
		}
		catch
		{
			return new List<string>();
		}
	}

	public static string NormalizeEntryName(string? entryName)
	{
		if (string.IsNullOrWhiteSpace(entryName)) return string.Empty;

		string name = entryName.Trim().Replace('\\', '/');
		int lastSeparator = name.LastIndexOf('/');
		if (lastSeparator >= 0) name = name[(lastSeparator + 1)..];

		string extension = Path.GetExtension(name);
		if (extension.Length > 0 && Array.Exists(EntryAudioExtensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase)))
		{
			name = name[..^extension.Length];
		}

		return name.Trim().ToLowerInvariant();
	}

	public static byte[] PackEntries(IList<RaudEntry>? entries)
	{
		using var rawPayloadStream = new MemoryStream();
		using var rawWriter = new BinaryWriter(rawPayloadStream);

		rawWriter.Write((uint)(entries?.Count ?? 0));
		if (entries != null)
		{
			foreach (var entry in entries)
			{
				byte[] nameBytes = Encoding.UTF8.GetBytes(NormalizeEntryName(entry.Name));
				rawWriter.Write((uint)nameBytes.Length);
				if (nameBytes.Length > 0) rawWriter.Write(nameBytes);

				byte[]? data = entry.Data;
				rawWriter.Write((uint)(data?.Length ?? 0));
				if (data != null && data.Length > 0)
				{
					rawWriter.Write(data);
				}
			}
		}

		rawWriter.Flush();
		return rawPayloadStream.ToArray();
	}

	public static List<RaudEntry> UnpackEntries(ReadOnlySpan<byte> payloadSpan, uint version)
	{
		var entries = new List<RaudEntry>();
		int readOffset = 0;
		if (readOffset + 4 > payloadSpan.Length) return entries;

		uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(payloadSpan.Slice(readOffset, 4));
		readOffset += 4;

		for (int i = 0; i < entryCount; i++)
		{
			string entryName = string.Empty;
			if (version >= NamedEntriesVersion)
			{
				if (readOffset + 4 > payloadSpan.Length) break;
				uint nameLength = BinaryPrimitives.ReadUInt32LittleEndian(payloadSpan.Slice(readOffset, 4));
				readOffset += 4;

				if (readOffset + (int)nameLength > payloadSpan.Length) break;
				entryName = nameLength > 0 ? Encoding.UTF8.GetString(payloadSpan.Slice(readOffset, (int)nameLength)) : string.Empty;
				readOffset += (int)nameLength;
			}

			if (readOffset + 4 > payloadSpan.Length) break;
			uint entryLength = BinaryPrimitives.ReadUInt32LittleEndian(payloadSpan.Slice(readOffset, 4));
			readOffset += 4;

			if (readOffset + (int)entryLength > payloadSpan.Length) break;
			byte[] entryData = payloadSpan.Slice(readOffset, (int)entryLength).ToArray();
			entries.Add(new RaudEntry(entryName, entryData));
			readOffset += (int)entryLength;
		}

		return entries;
	}

	private static byte[]? GetTrackFromEntries(List<RaudEntry> entries, int trackIndex)
	{
		return (trackIndex >= 0 && trackIndex < entries.Count) ? entries[trackIndex].Data : null;
	}

	private static byte[]? GetEntryFromEntries(List<RaudEntry> entries, string? entryName)
	{
		return TryGetEntryFromEntries(entries, entryName, out byte[] data) ? data : null;
	}

	private static bool TryGetEntryFromEntries(List<RaudEntry> entries, string? entryName, out byte[] data)
	{
		data = Array.Empty<byte>();
		if (entries.Count == 0) return false;

		string normalizedRequest = NormalizeEntryName(entryName);
		if (normalizedRequest.Length == 0)
		{
			data = entries[0].Data;
			return data.Length > 0;
		}

		foreach (var entry in entries)
		{
			if (string.Equals(NormalizeEntryName(entry.Name), normalizedRequest, StringComparison.Ordinal))
			{
				data = entry.Data;
				return data.Length > 0;
			}
		}

		return false;
	}
}
