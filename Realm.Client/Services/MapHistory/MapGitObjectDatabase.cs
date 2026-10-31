using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Realm.Client.Services.MapHistory;

public enum MapGitObjectType
{
	Commit = 1,
	Tree = 2,
	Blob = 3,
	Tag = 4
}

public class MapGitObjectDatabase
{
	private readonly string _objectsDirectory;
	private readonly Dictionary<string, MapGitPackedLocation> _packedLocations = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<string> _indexedPackFiles = new();
	private readonly Dictionary<string, (MapGitObjectType Type, byte[] Content)> _resolvedCache = new(StringComparer.OrdinalIgnoreCase);

	public MapGitObjectDatabase(string gitDirectory)
	{
		_objectsDirectory = Path.Combine(gitDirectory, "objects");
	}

	public string ObjectsDirectory => _objectsDirectory;

	public static string ComputeObjectId(MapGitObjectType type, ReadOnlySpan<byte> content)
	{
		byte[] header = Encoding.ASCII.GetBytes($"{GetTypeName(type)} {content.Length}\0");
		using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
		hasher.AppendData(header);
		hasher.AppendData(content);
		return Convert.ToHexStringLower(hasher.GetHashAndReset());
	}

	public static string GetTypeName(MapGitObjectType type)
	{
		return type switch
		{
			MapGitObjectType.Commit => "commit",
			MapGitObjectType.Tree => "tree",
			MapGitObjectType.Blob => "blob",
			MapGitObjectType.Tag => "tag",
			_ => "blob"
		};
	}

	public bool Exists(string objectId)
	{
		if (string.IsNullOrEmpty(objectId) || objectId.Length < 3) return false;
		string loosePath = GetLooseObjectPath(objectId);
		if (File.Exists(loosePath)) return true;
		if (_resolvedCache.ContainsKey(objectId)) return true;

		EnsurePackIndexesLoaded();
		return _packedLocations.ContainsKey(objectId);
	}

	public string Write(MapGitObjectType type, byte[] content)
	{
		string objectId = ComputeObjectId(type, content);
		string loosePath = GetLooseObjectPath(objectId);
		if (File.Exists(loosePath))
		{
			return objectId;
		}

		string directory = Path.GetDirectoryName(loosePath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		byte[] header = Encoding.ASCII.GetBytes($"{GetTypeName(type)} {content.Length}\0");
		string temporaryPath = loosePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (var fileStream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
			using (var zlibStream = new ZLibStream(fileStream, CompressionLevel.Fastest, false))
			{
				zlibStream.Write(header);
				zlibStream.Write(content);
			}

			File.Move(temporaryPath, loosePath, true);
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				try
				{
					File.Delete(temporaryPath);
				}
				catch (IOException)
				{
				}
			}
		}

		return objectId;
	}

	public bool TryRead(string objectId, out MapGitObjectType type, out byte[] content)
	{
		type = MapGitObjectType.Blob;
		content = Array.Empty<byte>();

		if (string.IsNullOrEmpty(objectId) || objectId.Length != 40) return false;

		if (_resolvedCache.TryGetValue(objectId, out var cached))
		{
			type = cached.Type;
			content = cached.Content;
			return true;
		}

		string loosePath = GetLooseObjectPath(objectId);
		if (File.Exists(loosePath) && TryReadLooseObject(loosePath, out type, out content))
		{
			_resolvedCache[objectId] = (type, content);
			return true;
		}

		EnsurePackIndexesLoaded();
		if (_packedLocations.TryGetValue(objectId, out MapGitPackedLocation location) && TryReadPackedObject(location, 0, out type, out content))
		{
			_resolvedCache[objectId] = (type, content);
			return true;
		}

		return false;
	}

	public void CopyMissingObjectsTo(MapGitObjectDatabase destination)
	{
		if (!Directory.Exists(_objectsDirectory)) return;

		foreach (string loosePath in Directory.GetFiles(_objectsDirectory, "*", SearchOption.AllDirectories))
		{
			string relativePath = Path.GetRelativePath(_objectsDirectory, loosePath).Replace('\\', '/');
			if (relativePath.Contains("pack/")) continue;
			if (relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
			if (Path.GetFileName(loosePath).Length != 38) continue;

			string objectDirectoryName = Path.GetFileName(Path.GetDirectoryName(loosePath) ?? string.Empty);
			if (objectDirectoryName.Length != 2) continue;
			string objectId = objectDirectoryName + Path.GetFileName(loosePath);
			string destinationPath = destination.GetLooseObjectPath(objectId);
			if (File.Exists(destinationPath)) continue;

			string destinationDirectory = Path.GetDirectoryName(destinationPath);
			if (!string.IsNullOrEmpty(destinationDirectory))
			{
				Directory.CreateDirectory(destinationDirectory);
			}

			File.Copy(loosePath, destinationPath, true);
		}
	}

	public string GetLooseObjectPath(string objectId)
	{
		string directoryName = objectId[..2];
		string fileName = objectId[2..];
		return Path.Combine(_objectsDirectory, directoryName, fileName);
	}

	private static bool TryReadLooseObject(string loosePath, out MapGitObjectType type, out byte[] content)
	{
		type = MapGitObjectType.Blob;
		content = Array.Empty<byte>();

		try
		{
			using var fileStream = new FileStream(loosePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var zlibStream = new ZLibStream(fileStream, CompressionMode.Decompress);
			using var buffer = new MemoryStream();
			zlibStream.CopyTo(buffer);
			return TryParseObjectPayload(buffer.ToArray(), out type, out content);
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static bool TryParseObjectPayload(byte[] payload, out MapGitObjectType type, out byte[] content)
	{
		type = MapGitObjectType.Blob;
		content = Array.Empty<byte>();

		int separatorIndex = Array.IndexOf(payload, (byte)0);
		if (separatorIndex <= 0) return false;

		string header = Encoding.ASCII.GetString(payload, 0, separatorIndex);
		int spaceIndex = header.IndexOf(' ');
		if (spaceIndex <= 0) return false;

		string typeName = header[..spaceIndex];
		type = typeName switch
		{
			"commit" => MapGitObjectType.Commit,
			"tree" => MapGitObjectType.Tree,
			"blob" => MapGitObjectType.Blob,
			"tag" => MapGitObjectType.Tag,
			_ => MapGitObjectType.Blob
		};

		content = payload[(separatorIndex + 1)..];
		return true;
	}

	private void EnsurePackIndexesLoaded()
	{
		string packDirectory = Path.Combine(_objectsDirectory, "pack");
		if (!Directory.Exists(packDirectory)) return;

		foreach (string indexFilePath in Directory.GetFiles(packDirectory, "*.idx"))
		{
			if (_indexedPackFiles.Contains(indexFilePath)) continue;
			_indexedPackFiles.Add(indexFilePath);
			try
			{
				IndexPackFile(indexFilePath);
			}
			catch (Exception)
			{
			}
		}
	}

	private void IndexPackFile(string indexFilePath)
	{
		string packFilePath = Path.ChangeExtension(indexFilePath, ".pack");
		if (!File.Exists(packFilePath)) return;

		byte[] indexBytes = File.ReadAllBytes(indexFilePath);
		if (indexBytes.Length < 8) return;
		if (BinaryPrimitives.ReadUInt32BigEndian(indexBytes.AsSpan(0, 4)) != 0xFF744F63) return;
		if (BinaryPrimitives.ReadUInt32BigEndian(indexBytes.AsSpan(4, 4)) != 2) return;

		int fanoutOffset = 8;
		int totalObjects = (int)BinaryPrimitives.ReadUInt32BigEndian(indexBytes.AsSpan(fanoutOffset + (255 * 4), 4));
		int objectIdOffset = fanoutOffset + (256 * 4);
		int checksumOffset = objectIdOffset + (totalObjects * 20);
		int offsetTableOffset = checksumOffset + (totalObjects * 4);
		int largeOffsetTableOffset = offsetTableOffset + (totalObjects * 4);

		for (int objectIndex = 0; objectIndex < totalObjects; objectIndex++)
		{
			string objectId = Convert.ToHexStringLower(indexBytes.AsSpan(objectIdOffset + (objectIndex * 20), 20));
			uint rawOffset = BinaryPrimitives.ReadUInt32BigEndian(indexBytes.AsSpan(offsetTableOffset + (objectIndex * 4), 4));

			long objectOffset;
			if ((rawOffset & 0x80000000u) != 0)
			{
				int largeIndex = (int)(rawOffset & 0x7FFFFFFFu);
				int largeOffsetPosition = largeOffsetTableOffset + (largeIndex * 8);
				if (largeOffsetPosition + 8 > indexBytes.Length) continue;
				objectOffset = (long)BinaryPrimitives.ReadUInt64BigEndian(indexBytes.AsSpan(largeOffsetPosition, 8));
			}
			else
			{
				objectOffset = rawOffset;
			}

			_packedLocations[objectId] = new MapGitPackedLocation(packFilePath, objectOffset);
		}
	}

	private bool TryReadPackedObject(MapGitPackedLocation location, int depth, out MapGitObjectType type, out byte[] content)
	{
		type = MapGitObjectType.Blob;
		content = Array.Empty<byte>();

		if (depth > 64) return false;

		try
		{
			using var fileStream = new FileStream(location.PackFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			fileStream.Position = location.ObjectOffset;

			int firstByte = fileStream.ReadByte();
			if (firstByte < 0) return false;

			int objectTypeCode = (firstByte >> 4) & 0x7;
			long objectSize = firstByte & 0xF;
			int shift = 4;
			int currentByte = firstByte;

			while ((currentByte & 0x80) != 0)
			{
				currentByte = fileStream.ReadByte();
				if (currentByte < 0) return false;
				objectSize |= (long)(currentByte & 0x7F) << shift;
				shift += 7;
			}

			if (objectTypeCode == 6)
			{
				int distanceByte = fileStream.ReadByte();
				if (distanceByte < 0) return false;
				long baseDistance = distanceByte & 0x7F;
				while ((distanceByte & 0x80) != 0)
				{
					distanceByte = fileStream.ReadByte();
					if (distanceByte < 0) return false;
					baseDistance = ((baseDistance + 1) << 7) | (long)(distanceByte & 0x7F);
				}

				byte[] delta = DecompressRemaining(fileStream, objectSize);
				var baseLocation = new MapGitPackedLocation(location.PackFilePath, location.ObjectOffset - baseDistance);
				if (!TryReadPackedObject(baseLocation, depth + 1, out MapGitObjectType baseType, out byte[] baseContent)) return false;
				content = ApplyDelta(baseContent, delta);
				type = baseType;
				return true;
			}

			if (objectTypeCode == 7)
			{
				byte[] baseIdBytes = new byte[20];
				if (fileStream.Read(baseIdBytes, 0, 20) != 20) return false;
				string baseObjectId = Convert.ToHexStringLower(baseIdBytes);
				byte[] delta = DecompressRemaining(fileStream, objectSize);
				if (!TryRead(baseObjectId, out MapGitObjectType baseType, out byte[] baseContent)) return false;
				content = ApplyDelta(baseContent, delta);
				type = baseType;
				return true;
			}

			if (objectTypeCode is < 1 or > 4) return false;
			type = (MapGitObjectType)objectTypeCode;
			content = DecompressRemaining(fileStream, objectSize);
			return content.Length == objectSize;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static byte[] DecompressRemaining(Stream source, long expectedLength)
	{
		using var zlibStream = new ZLibStream(source, CompressionMode.Decompress, true);
		using var output = new MemoryStream();
		byte[] readBuffer = new byte[81920];

		while (output.Length < expectedLength)
		{
			int read = zlibStream.Read(readBuffer, 0, (int)Math.Min(readBuffer.Length, expectedLength - output.Length));
			if (read <= 0) break;
			output.Write(readBuffer, 0, read);
		}

		return output.ToArray();
	}

	private static byte[] ApplyDelta(byte[] baseContent, byte[] delta)
	{
		int position = 0;
		long baseSize = ReadDeltaSize(delta, ref position);
		long resultSize = ReadDeltaSize(delta, ref position);
		if (baseSize != baseContent.Length) return Array.Empty<byte>();

		byte[] result = new byte[resultSize];
		int resultPosition = 0;

		while (position < delta.Length && resultPosition < result.Length)
		{
			byte instruction = delta[position++];
			if ((instruction & 0x80) != 0)
			{
				int copyOffset = 0;
				int copySize = 0;

				if ((instruction & 0x01) != 0) copyOffset |= delta[position++];
				if ((instruction & 0x02) != 0) copyOffset |= delta[position++] << 8;
				if ((instruction & 0x04) != 0) copyOffset |= delta[position++] << 16;
				if ((instruction & 0x08) != 0) copyOffset |= delta[position++] << 24;
				if ((instruction & 0x10) != 0) copySize |= delta[position++];
				if ((instruction & 0x20) != 0) copySize |= delta[position++] << 8;
				if ((instruction & 0x40) != 0) copySize |= delta[position++] << 16;
				if (copySize == 0) copySize = 0x10000;

				if (copyOffset < 0 || copySize < 0) break;
				if (copyOffset + copySize > baseContent.Length) break;
				if (resultPosition + copySize > result.Length) break;

				Array.Copy(baseContent, copyOffset, result, resultPosition, copySize);
				resultPosition += copySize;
			}
			else if (instruction > 0)
			{
				int insertSize = instruction;
				if (position + insertSize > delta.Length) break;
				if (resultPosition + insertSize > result.Length) break;
				Array.Copy(delta, position, result, resultPosition, insertSize);
				position += insertSize;
				resultPosition += insertSize;
			}
			else
			{
				break;
			}
		}

		return resultPosition == result.Length ? result : Array.Empty<byte>();
	}

	private static long ReadDeltaSize(byte[] delta, ref int position)
	{
		long value = 0;
		int shift = 0;
		byte current;

		do
		{
			if (position >= delta.Length) return 0;
			current = delta[position++];
			value |= (long)(current & 0x7F) << shift;
			shift += 7;
		}
		while ((current & 0x80) != 0);

		return value;
	}

	private readonly record struct MapGitPackedLocation(string PackFilePath, long ObjectOffset)
	{
	}
}
