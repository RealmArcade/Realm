using System.Globalization;
using System.Text;
using Blake3;

namespace Realm.Shared.Maps;

public static class MapContentHash
{
	private static readonly string[] ExcludedDirectoryNames = ["obj", "bin", ".git", ".godot"];
	private static readonly byte[] FieldSeparator = [0];

	/// <summary>
	///     Hashes the authored files of a map folder. <paramref name="excludedFilePath" /> names a file the caller is
	///     writing itself, such as the server's log, so its own output never becomes part of the map's identity.
	/// </summary>
	public static string Compute(string mapFolderPath, string? excludedFilePath = null)
	{
		string root = Path.GetFullPath(mapFolderPath);
		List<string> files = CollectAuthoredFiles(root);
		if (!string.IsNullOrEmpty(excludedFilePath))
		{
			string excluded = Path.GetFullPath(excludedFilePath);
			files.RemoveAll(file => string.Equals(file, excluded, StringComparison.OrdinalIgnoreCase));
		}

		files.Sort(StringComparer.Ordinal);

		using var hasher = Hasher.New();
		byte[] buffer = new byte[81920];

		for (int index = 0; index < files.Count; index++)
		{
			string filePath = files[index];
			string relativePath = Path.GetRelativePath(root, filePath).Replace('\\', '/');
			FeedText(hasher, buffer, relativePath);
			FeedText(hasher, buffer, new FileInfo(filePath).Length.ToString(CultureInfo.InvariantCulture));
			FeedFileContents(hasher, buffer, filePath);
		}

		FeedText(hasher, buffer, files.Count.ToString(CultureInfo.InvariantCulture));
		return hasher.Finalize().ToString();
	}

	private static List<string> CollectAuthoredFiles(string root)
	{
		var files = new List<string>();
		var pending = new Stack<string>();
		pending.Push(root);

		while (pending.Count > 0)
		{
			string directory = pending.Pop();
			foreach (string child in Directory.EnumerateDirectories(directory))
			{
				string name = Path.GetFileName(child);
				bool excluded = false;
				for (int index = 0; index < ExcludedDirectoryNames.Length; index++)
				{
					if (!string.Equals(name, ExcludedDirectoryNames[index], StringComparison.OrdinalIgnoreCase)) continue;
					excluded = true;
					break;
				}

				if (excluded) continue;
				pending.Push(child);
			}

			files.AddRange(Directory.EnumerateFiles(directory));
		}

		return files;
	}

	private static void FeedText(Hasher hasher, byte[] buffer, string value)
	{
		int written = Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
		hasher.Update(new ReadOnlySpan<byte>(buffer, 0, written));
		hasher.Update(new ReadOnlySpan<byte>(FieldSeparator));
	}

	private static void FeedFileContents(Hasher hasher, byte[] buffer, string filePath)
	{
		using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan);
		int read;
		while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
		{
			hasher.Update(new ReadOnlySpan<byte>(buffer, 0, read));
		}
	}
}
