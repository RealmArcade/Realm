using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Realm.Client.Services.MapHistory;

public readonly record struct MapGitSignature(string Name, string Email, DateTimeOffset When);

public readonly record struct MapGitHeadState(string? BranchName, string? CommitId)
{
	public bool IsDetached => string.IsNullOrEmpty(BranchName);
	public bool HasCommit => !string.IsNullOrEmpty(CommitId);
}

public class MapGitCommitInfo
{
	public string Id { get; init; } = string.Empty;
	public string TreeId { get; init; } = string.Empty;
	public string? ParentId { get; init; }
	public DateTimeOffset CommittedAt { get; init; }
	public string Message { get; init; } = string.Empty;
}

public class MapGitRepository
{
	public const string SafetyReferenceName = "refs/realm/history";
	public const string DefaultBranchName = "main";

	private const uint FileModeRegular = 0x81A4;

	private static readonly UTF8Encoding CommitEncoding = new(false);

	private readonly MapGitObjectDatabase _objects;

	public MapGitRepository(string workTreePath)
	{
		WorkTreePath = Path.GetFullPath(workTreePath);
		GitDirectory = Path.Combine(WorkTreePath, ".git");
		_objects = new MapGitObjectDatabase(GitDirectory);
	}

	public string WorkTreePath { get; }
	public string GitDirectory { get; }
	public bool Exists => Directory.Exists(GitDirectory);
	public MapGitObjectDatabase Objects => _objects;

	public static string FormatTimeZone(DateTimeOffset when)
	{
		TimeSpan offset = when.Offset;
		char sign = offset < TimeSpan.Zero ? '-' : '+';
		offset = offset.Duration();
		return string.Create(CultureInfo.InvariantCulture, $"{sign}{offset.Hours:00}{offset.Minutes:00}");
	}

	public void Initialize()
	{
		Directory.CreateDirectory(GitDirectory);
		Directory.CreateDirectory(Path.Combine(GitDirectory, "objects"));
		Directory.CreateDirectory(Path.Combine(GitDirectory, "refs", "heads"));
		Directory.CreateDirectory(Path.Combine(GitDirectory, "refs", "realm"));

		string headPath = Path.Combine(GitDirectory, "HEAD");
		if (!File.Exists(headPath))
		{
			File.WriteAllText(headPath, $"ref: refs/heads/{DefaultBranchName}\n");
		}

		string configPath = Path.Combine(GitDirectory, "config");
		if (!File.Exists(configPath))
		{
			File.WriteAllText(configPath, "[core]\n\trepositoryformatversion = 0\n\tfilemode = false\n\tbare = false\n\tlogallrefupdates = true\n");
		}

		string attributesPath = Path.Combine(WorkTreePath, ".gitattributes");
		if (!File.Exists(attributesPath))
		{
			File.WriteAllText(attributesPath, "* -text\n");
		}
	}

	public MapGitHeadState ReadHead()
	{
		string headPath = Path.Combine(GitDirectory, "HEAD");
		if (!File.Exists(headPath)) return new MapGitHeadState(null, null);

		string headContent = File.ReadAllText(headPath).Trim();
		if (headContent.StartsWith("ref: ", StringComparison.Ordinal))
		{
			string referenceName = headContent[5..].Trim();
			string branchName = referenceName.StartsWith("refs/heads/", StringComparison.Ordinal)
				? referenceName["refs/heads/".Length..]
				: null;
			return new MapGitHeadState(branchName, ReadReference(referenceName));
		}

		return new MapGitHeadState(null, headContent.Length == 40 ? headContent.ToLowerInvariant() : null);
	}

	public string? ReadReference(string referenceName)
	{
		string referencePath = Path.Combine(GitDirectory, referenceName.Replace('/', Path.DirectorySeparatorChar));
		if (File.Exists(referencePath))
		{
			string content = File.ReadAllText(referencePath).Trim();
			return content.Length == 40 ? content.ToLowerInvariant() : null;
		}

		string packedReferencesPath = Path.Combine(GitDirectory, "packed-refs");
		if (!File.Exists(packedReferencesPath)) return null;

		foreach (string line in File.ReadLines(packedReferencesPath))
		{
			string trimmed = line.Trim();
			if (trimmed.Length == 0 || trimmed[0] is '#' or '^') continue;
			int spaceIndex = trimmed.IndexOf(' ');
			if (spaceIndex != 40) continue;
			if (!trimmed.AsSpan(41).SequenceEqual(referenceName)) continue;
			return trimmed[..40].ToLowerInvariant();
		}

		return null;
	}

	public void WriteReference(string referenceName, string commitId)
	{
		string referencePath = Path.Combine(GitDirectory, referenceName.Replace('/', Path.DirectorySeparatorChar));
		string directory = Path.GetDirectoryName(referencePath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		string temporaryPath = referencePath + ".lock." + Guid.NewGuid().ToString("N");
		File.WriteAllText(temporaryPath, commitId + "\n");
		File.Move(temporaryPath, referencePath, true);
	}

	public void PointHeadAt(string commitId)
	{
		MapGitHeadState head = ReadHead();
		if (head.IsDetached)
		{
			File.WriteAllText(Path.Combine(GitDirectory, "HEAD"), commitId + "\n");
			return;
		}

		WriteReference($"refs/heads/{head.BranchName ?? DefaultBranchName}", commitId);
	}

	public bool TryReadCommit(string commitId, out MapGitCommitInfo commit)
	{
		commit = new MapGitCommitInfo();
		if (string.IsNullOrEmpty(commitId)) return false;
		if (!_objects.TryRead(commitId, out MapGitObjectType type, out byte[] content)) return false;
		if (type != MapGitObjectType.Commit) return false;

		string text = CommitEncoding.GetString(content);
		int separatorIndex = text.IndexOf("\n\n", StringComparison.Ordinal);
		string headerBlock = separatorIndex >= 0 ? text[..separatorIndex] : text;
		string message = separatorIndex >= 0 ? text[(separatorIndex + 2)..].TrimEnd('\n') : string.Empty;

		string treeId = string.Empty;
		string? parentId = null;
		DateTimeOffset committedAt = DateTimeOffset.UnixEpoch;

		foreach (string line in headerBlock.Split('\n'))
		{
			if (line.StartsWith("tree ", StringComparison.Ordinal))
			{
				treeId = line[5..].Trim();
			}
			else if (line.StartsWith("parent ", StringComparison.Ordinal))
			{
				parentId ??= line[7..].Trim();
			}
			else if (line.StartsWith("committer ", StringComparison.Ordinal))
			{
				committedAt = ParseSignatureTimestamp(line);
			}
		}

		if (treeId.Length != 40) return false;

		commit = new MapGitCommitInfo
		{
			Id = commitId.ToLowerInvariant(),
			TreeId = treeId,
			ParentId = parentId,
			CommittedAt = committedAt,
			Message = message
		};

		return true;
	}

	private static DateTimeOffset ParseSignatureTimestamp(string committerLine)
	{
		string[] parts = committerLine.Split(' ');
		if (parts.Length < 3) return DateTimeOffset.UnixEpoch;

		if (!long.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
		{
			return DateTimeOffset.UnixEpoch;
		}

		string timeZoneText = parts[^1];
		TimeSpan offset = TimeSpan.Zero;
		if (timeZoneText.Length == 5 && (timeZoneText[0] == '+' || timeZoneText[0] == '-'))
		{
			if (int.TryParse(timeZoneText.AsSpan(1, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetHours) &&
			    int.TryParse(timeZoneText.AsSpan(3, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetMinutes))
			{
				offset = new TimeSpan(offsetHours, offsetMinutes, 0);
				if (timeZoneText[0] == '-') offset = offset.Negate();
			}
		}

		return DateTimeOffset.FromUnixTimeSeconds(seconds).ToOffset(offset);
	}

	public string WriteCommit(IReadOnlyDictionary<string, string> blobIdByPath, string? parentId, MapGitSignature signature, string message)
	{
		string treeId = WriteTree(blobIdByPath);
		var builder = new StringBuilder();
		builder.Append("tree ").Append(treeId).Append('\n');
		if (!string.IsNullOrEmpty(parentId))
		{
			builder.Append("parent ").Append(parentId).Append('\n');
		}

		string signatureLine = $"{signature.Name} <{signature.Email}> {signature.When.ToUnixTimeSeconds()} {FormatTimeZone(signature.When)}";
		builder.Append("author ").Append(signatureLine).Append('\n');
		builder.Append("committer ").Append(signatureLine).Append('\n');
		builder.Append('\n').Append(message).Append('\n');

		return _objects.Write(MapGitObjectType.Commit, CommitEncoding.GetBytes(builder.ToString()));
	}

	public string WriteBlob(byte[] content)
	{
		return _objects.Write(MapGitObjectType.Blob, content);
	}

	public void WriteIndex(IReadOnlyDictionary<string, string> blobIdByPath)
	{
		string indexPath = Path.Combine(GitDirectory, "index");
		if (File.Exists(indexPath + ".lock")) return;

		var sortedPaths = blobIdByPath.Keys.ToList();
		sortedPaths.Sort(StringComparer.Ordinal);

		using var content = new MemoryStream();
		WriteBigEndianAscii(content, "DIRC");
		WriteBigEndianUInt32(content, 2);
		WriteBigEndianUInt32(content, (uint)sortedPaths.Count);

		foreach (string relativePath in sortedPaths)
		{
			string absolutePath = Path.Combine(WorkTreePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
			long length = 0;
			DateTime lastWriteTimeUtc = DateTime.UnixEpoch;
			DateTime creationTimeUtc = DateTime.UnixEpoch;

			try
			{
				var fileInfo = new FileInfo(absolutePath);
				if (fileInfo.Exists)
				{
					length = fileInfo.Length;
					lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
					creationTimeUtc = fileInfo.CreationTimeUtc;
				}
			}
			catch (IOException)
			{
			}

			long creationSeconds = ToUnixSeconds(creationTimeUtc, out int creationNanoseconds);
			long lastWriteSeconds = ToUnixSeconds(lastWriteTimeUtc, out int lastWriteNanoseconds);

			WriteBigEndianUInt32(content, (uint)creationSeconds);
			WriteBigEndianUInt32(content, (uint)creationNanoseconds);
			WriteBigEndianUInt32(content, (uint)lastWriteSeconds);
			WriteBigEndianUInt32(content, (uint)lastWriteNanoseconds);
			WriteBigEndianUInt32(content, 0u);
			WriteBigEndianUInt32(content, 0u);
			WriteBigEndianUInt32(content, FileModeRegular);
			WriteBigEndianUInt32(content, 0u);
			WriteBigEndianUInt32(content, 0u);
			WriteBigEndianUInt32(content, (uint)length);
			content.Write(Convert.FromHexString(blobIdByPath[relativePath]));

			byte[] pathBytes = CommitEncoding.GetBytes(relativePath);
			int flags = Math.Min(pathBytes.Length, 0xFFF);
			WriteBigEndianUInt16(content, (ushort)flags);
			content.Write(pathBytes);
			content.WriteByte(0);

			int entryLength = 62 + pathBytes.Length + 1;
			int padding = (8 - (entryLength % 8)) % 8;
			for (int paddingIndex = 0; paddingIndex < padding; paddingIndex++)
			{
				content.WriteByte(0);
			}
		}

		byte[] indexBytes = content.ToArray();
		byte[] checksum = SHA1.HashData(indexBytes);
		string temporaryPath = indexPath + ".realm-" + Guid.NewGuid().ToString("N") + ".tmp";

		try
		{
			using (var fileStream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(indexBytes);
				fileStream.Write(checksum);
			}

			File.Move(temporaryPath, indexPath, true);
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
	}

	private static void WriteBigEndianAscii(Stream stream, string text)
	{
		stream.Write(Encoding.ASCII.GetBytes(text));
	}

	private static void WriteBigEndianUInt32(Stream stream, uint value)
	{
		Span<byte> buffer = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
		stream.Write(buffer);
	}

	private static void WriteBigEndianUInt16(Stream stream, ushort value)
	{
		Span<byte> buffer = stackalloc byte[2];
		BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
		stream.Write(buffer);
	}

	private static long ToUnixSeconds(DateTime value, out int nanoseconds)
	{
		try
		{
			long ticks = value.ToUniversalTime().Ticks - DateTime.UnixEpoch.Ticks;
			nanoseconds = (int)((ticks % TimeSpan.TicksPerSecond) * 100);
			return ticks / TimeSpan.TicksPerSecond;
		}
		catch (ArgumentOutOfRangeException)
		{
			nanoseconds = 0;
			return 0;
		}
	}

	public byte[] ReadBlob(string blobId)
	{
		return _objects.TryRead(blobId, out MapGitObjectType type, out byte[] content) && type == MapGitObjectType.Blob
			? content
			: Array.Empty<byte>();
	}

	public IReadOnlyList<MapGitCommitInfo> ReadVersionHistory(int maximumCount)
	{
		var orderedCommits = new List<MapGitCommitInfo>();

		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var startingPoints = new List<string>();
		string? headCommitId = ReadHead().CommitId;
		if (!string.IsNullOrEmpty(headCommitId)) startingPoints.Add(headCommitId);
		string? safetyCommitId = ReadReference(SafetyReferenceName);
		if (!string.IsNullOrEmpty(safetyCommitId) && !visited.Contains(safetyCommitId)) startingPoints.Add(safetyCommitId);

		var pending = new Queue<string>(startingPoints);
		while (pending.Count > 0 && orderedCommits.Count < maximumCount)
		{
			string commitId = pending.Dequeue();
			if (!visited.Add(commitId)) continue;

			if (!TryReadCommit(commitId, out MapGitCommitInfo commit)) continue;
			orderedCommits.Add(commit);

			if (!string.IsNullOrEmpty(commit.ParentId))
			{
				pending.Enqueue(commit.ParentId);
			}
		}

		orderedCommits.Sort((first, second) => second.CommittedAt.CompareTo(first.CommittedAt));
		return orderedCommits;
	}

	public Dictionary<string, string> ReadTreeFiles(string treeId)
	{
		var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		ReadTreeFilesInto(treeId, string.Empty, files, 0);
		return files;
	}

	private void ReadTreeFilesInto(string treeId, string prefix, Dictionary<string, string> files, int depth)
	{
		if (depth > 64) return;
		if (!_objects.TryRead(treeId, out MapGitObjectType type, out byte[] content)) return;
		if (type != MapGitObjectType.Tree) return;

		int position = 0;
		while (position < content.Length)
		{
			int spaceIndex = Array.IndexOf(content, (byte)' ', position);
			if (spaceIndex < 0) return;
			int nullIndex = Array.IndexOf(content, (byte)0, spaceIndex + 1);
			if (nullIndex < 0 || nullIndex + 21 > content.Length) return;

			string mode = Encoding.ASCII.GetString(content, position, spaceIndex - position);
			string name = CommitEncoding.GetString(content, spaceIndex + 1, nullIndex - spaceIndex - 1);
			string entryId = Convert.ToHexStringLower(content.AsSpan(nullIndex + 1, 20));
			position = nullIndex + 21;

			if (mode == "40000")
			{
				ReadTreeFilesInto(entryId, prefix + name + "/", files, depth + 1);
			}
			else
			{
				files[prefix + name] = entryId;
			}
		}
	}

	private string WriteTree(IReadOnlyDictionary<string, string> blobIdByPath)
	{
		var root = new MapGitTreeNode();
		foreach (var pair in blobIdByPath)
		{
			string[] segments = pair.Key.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
			if (segments.Length == 0) continue;

			MapGitTreeNode node = root;
			for (int segmentIndex = 0; segmentIndex < segments.Length - 1; segmentIndex++)
			{
				if (!node.Directories.TryGetValue(segments[segmentIndex], out MapGitTreeNode child))
				{
					child = new MapGitTreeNode();
					node.Directories[segments[segmentIndex]] = child;
				}

				node = child;
			}

			node.Files[segments[^1]] = pair.Value;
		}

		return WriteTreeNode(root);
	}

	private string WriteTreeNode(MapGitTreeNode node)
	{
		var entries = new List<(string SortKey, string Mode, string Name, string ObjectId)>();
		foreach (var directory in node.Directories.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			entries.Add((directory.Key + "/", "40000", directory.Key, WriteTreeNode(directory.Value)));
		}

		foreach (var file in node.Files)
		{
			entries.Add((file.Key, "100644", file.Key, file.Value));
		}

		entries.Sort((first, second) => string.CompareOrdinal(first.SortKey, second.SortKey));

		using var content = new MemoryStream();
		foreach (var entry in entries)
		{
			content.Write(Encoding.ASCII.GetBytes($"{entry.Mode} {entry.Name}"));
			content.WriteByte(0);
			content.Write(Convert.FromHexString(entry.ObjectId));
		}

		return _objects.Write(MapGitObjectType.Tree, content.ToArray());
	}

	public List<string> CollectWorkingTreeFiles(Func<string, bool> isExcluded)
	{
		var relativePaths = new List<string>();
		if (!Directory.Exists(WorkTreePath)) return relativePaths;

		CollectWorkingTreeFilesInto(new DirectoryInfo(WorkTreePath), string.Empty, isExcluded, relativePaths, 0);
		relativePaths.Sort(StringComparer.Ordinal);
		return relativePaths;
	}

	private static void CollectWorkingTreeFilesInto(DirectoryInfo directory, string prefix, Func<string, bool> isExcluded, List<string> relativePaths, int depth)
	{
		if (depth > 64) return;

		foreach (FileInfo file in directory.GetFiles())
		{
			string relativePath = prefix + file.Name;
			if (isExcluded(relativePath)) continue;
			relativePaths.Add(relativePath);
		}

		foreach (DirectoryInfo childDirectory in directory.GetDirectories())
		{
			if (childDirectory.Name.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;

			string relativePath = prefix + childDirectory.Name;
			if (isExcluded(relativePath + "/")) continue;
			CollectWorkingTreeFilesInto(childDirectory, relativePath + "/", isExcluded, relativePaths, depth + 1);
		}
	}

	public static void InitializeBareRepository(string bareRepositoryPath)
	{
		Directory.CreateDirectory(bareRepositoryPath);
		Directory.CreateDirectory(Path.Combine(bareRepositoryPath, "objects"));
		Directory.CreateDirectory(Path.Combine(bareRepositoryPath, "refs", "heads"));
		Directory.CreateDirectory(Path.Combine(bareRepositoryPath, "refs", "realm"));

		string headPath = Path.Combine(bareRepositoryPath, "HEAD");
		if (!File.Exists(headPath))
		{
			File.WriteAllText(headPath, $"ref: refs/heads/{DefaultBranchName}\n");
		}

		string configPath = Path.Combine(bareRepositoryPath, "config");
		if (!File.Exists(configPath))
		{
			File.WriteAllText(configPath, "[core]\n\trepositoryformatversion = 0\n\tfilemode = false\n\tbare = true\n");
		}
	}

	public void PushToBareRepository(string bareRepositoryPath, IEnumerable<string> requiredBlobIds = null)
	{
		InitializeBareRepository(bareRepositoryPath);

		var originObjects = new MapGitObjectDatabase(bareRepositoryPath);
		_objects.CopyMissingObjectsTo(originObjects);

		if (requiredBlobIds != null)
		{
			foreach (string blobId in requiredBlobIds)
			{
				if (string.IsNullOrEmpty(blobId) || originObjects.Exists(blobId)) continue;
				if (!_objects.TryRead(blobId, out MapGitObjectType objectType, out byte[] content)) continue;
				originObjects.Write(objectType, content);
			}
		}

		MapGitHeadState head = ReadHead();
		string? latestCommitId = head.CommitId;
		if (string.IsNullOrEmpty(latestCommitId)) return;

		string originReferencePath = Path.Combine(bareRepositoryPath, "refs", "heads", DefaultBranchName);
		string originReferenceDirectory = Path.GetDirectoryName(originReferencePath);
		if (!string.IsNullOrEmpty(originReferenceDirectory))
		{
			Directory.CreateDirectory(originReferenceDirectory);
		}

		File.WriteAllText(originReferencePath, latestCommitId + "\n");

		string safetyReferencePath = Path.Combine(bareRepositoryPath, SafetyReferenceName.Replace('/', Path.DirectorySeparatorChar));
		string safetyReferenceDirectory = Path.GetDirectoryName(safetyReferencePath);
		if (!string.IsNullOrEmpty(safetyReferenceDirectory))
		{
			Directory.CreateDirectory(safetyReferenceDirectory);
		}

		File.WriteAllText(safetyReferencePath, latestCommitId + "\n");
	}

	private class MapGitTreeNode
	{
		public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
		public Dictionary<string, MapGitTreeNode> Directories { get; } = new(StringComparer.Ordinal);
	}
}
