using System;
using System.IO;

namespace Realm.Shared.Distribution;

/// <summary>
///     Removes a directory tree whose files may be marked read-only. A map's cached copies carry that mark: content
///     shared with the asset store by hard link, and anything the map's references copied in, is read-only because an
///     edit to it would be reverted. It protects a file from being changed, not the copy from being discarded, so the
///     tree is cleared of it and the removal retried before the attempt is reported as failed.
/// </summary>
public static class DirectoryDeletion
{
	/// <summary>Removes a directory and everything under it.</summary>
	/// <param name="directoryPath">The directory to remove.</param>
	/// <param name="errorMessage">Why the directory is still there, or empty when it is gone.</param>
	/// <returns>Whether the directory is gone afterwards.</returns>
	public static bool TryDelete(string directoryPath, out string errorMessage)
	{
		errorMessage = string.Empty;
		if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath)) return true;

		if (TryDeleteOnce(directoryPath, out errorMessage)) return true;

		ClearReadOnlyAttributes(directoryPath);
		if (TryDeleteOnce(directoryPath, out errorMessage)) return true;

		return false;
	}

	private static bool TryDeleteOnce(string directoryPath, out string errorMessage)
	{
		errorMessage = string.Empty;

		try
		{
			Directory.Delete(directoryPath, true);
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			errorMessage = exception.Message;
			return false;
		}
	}

	private static void ClearReadOnlyAttributes(string directoryPath)
	{
		try
		{
			foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
			{
				FileAttributes attributes = File.GetAttributes(filePath);
				if ((attributes & FileAttributes.ReadOnly) == 0) continue;

				File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}
}
