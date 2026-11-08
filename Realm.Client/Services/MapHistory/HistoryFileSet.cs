using System;
using System.Collections.Generic;
using System.IO;
using Realm.Shared.Services;

namespace Realm.Client.Services.MapHistory;

/// <summary>
///     Which files a map folder offers to history, and reading one of them without catching a file
///     mid-write. The exclusion rules are the workspace's ignored paths plus the editor's own scratch files
///     and the repository's per-user artefacts.
/// </summary>
internal static class HistoryFileSet
{
	private static readonly string[] _historyOnlyDirectoryNames = { ".vscode" };

	private static readonly string[] _historyOnlyFileSuffixes =
	{
		".pem", ".rkey", ".user", ".suo", AtomicFileWriter.TemporarySuffix
	};

	internal static List<string> CollectMapFiles(string mapDirectory)
	{
		var relativePaths = new List<string>();
		if (!Directory.Exists(mapDirectory)) return relativePaths;

		CollectMapFilesInto(new DirectoryInfo(mapDirectory), string.Empty, relativePaths, 0);
		relativePaths.Sort(StringComparer.Ordinal);
		return relativePaths;
	}

	internal static void CollectMapFilesInto(DirectoryInfo directory, string prefix, List<string> relativePaths, int depth)
	{
		if (depth > 64) return;

		foreach (FileInfo file in directory.GetFiles())
		{
			string relativePath = prefix + file.Name;
			if (IsExcludedFromHistory(relativePath)) continue;
			relativePaths.Add(relativePath);
		}

		foreach (DirectoryInfo childDirectory in directory.GetDirectories())
		{
			string relativePath = prefix + childDirectory.Name + "/";
			if (IsExcludedFromHistory(relativePath)) continue;
			CollectMapFilesInto(childDirectory, relativePath, relativePaths, depth + 1);
		}
	}

	internal static bool IsExcludedFromHistory(string relativePath)
	{
		string normalized = relativePath.Replace('\\', '/');
		if (string.IsNullOrWhiteSpace(normalized)) return true;
		if (MapWorkspaceService.IsIgnoredPathDomain(normalized)) return true;

		string trimmed = normalized.TrimEnd('/');
		foreach (string directoryName in _historyOnlyDirectoryNames)
		{
			if (trimmed.Equals(directoryName, StringComparison.OrdinalIgnoreCase)) return true;
			if (trimmed.StartsWith(directoryName + "/", StringComparison.OrdinalIgnoreCase)) return true;
			if (trimmed.Contains("/" + directoryName + "/", StringComparison.OrdinalIgnoreCase)) return true;
		}

		foreach (string suffix in _historyOnlyFileSuffixes)
		{
			if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	internal static bool TryReadStableContent(string absolutePath, out byte[] content)
	{
		content = Array.Empty<byte>();
		for (int attempt = 0; attempt < 3; attempt++)
		{
			try
			{
				var before = new FileInfo(absolutePath);
				if (!before.Exists) return false;

				long lengthBefore = before.Length;
				long writeTicksBefore = before.LastWriteTimeUtc.Ticks;
				byte[] readContent = File.ReadAllBytes(absolutePath);
				var after = new FileInfo(absolutePath);
				if (!after.Exists) return false;

				if (readContent.Length == lengthBefore &&
				    after.Length == lengthBefore &&
				    after.LastWriteTimeUtc.Ticks == writeTicksBefore)
				{
					content = readContent;
					return true;
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}

			System.Threading.Thread.Sleep(50);
		}

		return false;
	}

	internal static bool IsScriptSourcePath(string relativePath)
	{
		return relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
		       relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
	}

	internal static bool FileLengthMatches(string path, int length)
	{
		try
		{
			return new FileInfo(path).Length == length;
		}
		catch (IOException)
		{
			return false;
		}
	}

	internal static bool FileContentMatches(string path, byte[] content)
	{
		try
		{
			byte[] existing = File.ReadAllBytes(path);
			return existing.AsSpan().SequenceEqual(content);
		}
		catch (IOException)
		{
			return false;
		}
	}
}
