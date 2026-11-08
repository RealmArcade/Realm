using System;
using System.Collections.Generic;
using System.IO;

namespace Realm.Client.Services.MapHistory;

/// <summary>
///     Writing a restore onto disk so that a failure leaves the folder as it was: every file is staged
///     beside its target first, each original is kept as a backup before it is replaced, and either the
///     backups are deleted on success or every touched path is put back on failure.
/// </summary>
internal static class HistoryRestoreTransaction
{
	private const string RestoreStagingSuffix = ".realm-restore.tmp";
	private const string RestoreBackupSuffix = ".realm-restore.bak";

	internal static bool Apply(
		string mapDirectory,
		List<(string RelativePath, byte[] Content)> filesToWrite,
		List<string> relativePathsToRemove,
		out int filesWritten,
		out int filesRemoved,
		string restoreFailedFormat,
		out string? errorMessage)
	{
		filesWritten = 0;
		filesRemoved = 0;
		errorMessage = null;

		var stagedPaths = new List<string>(filesToWrite.Count);
		var touchedPaths = new List<string>(filesToWrite.Count + relativePathsToRemove.Count);

		try
		{
			foreach (var (relativePath, content) in filesToWrite)
			{
				string absolutePath = Path.Combine(mapDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
				StageRestoreContent(absolutePath, content);
				stagedPaths.Add(absolutePath);
			}

			foreach (string absolutePath in stagedPaths)
			{
				BackUpOriginalFile(absolutePath, touchedPaths);
				File.Move(absolutePath + RestoreStagingSuffix, absolutePath, true);
				if (!touchedPaths.Contains(absolutePath)) touchedPaths.Add(absolutePath);
				filesWritten++;
			}

			foreach (string relativePath in relativePathsToRemove)
		{
				string absolutePath = Path.Combine(mapDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
				if (!File.Exists(absolutePath)) continue;

				BackUpOriginalFile(absolutePath, touchedPaths);
				filesRemoved++;
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			RollBackRestore(touchedPaths, stagedPaths);
			filesWritten = 0;
			filesRemoved = 0;
			errorMessage = string.Format(restoreFailedFormat, exception.Message);
			return false;
		}

		foreach (string touchedPath in touchedPaths)
		{
			DeleteFileQuietly(touchedPath + RestoreBackupSuffix);
		}

		foreach (string stagedPath in stagedPaths)
		{
			DeleteFileQuietly(stagedPath + RestoreStagingSuffix);
		}

		return true;
	}

	internal static void StageRestoreContent(string absolutePath, byte[] content)
	{
		string? directory = Path.GetDirectoryName(absolutePath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		string stagingPath = absolutePath + RestoreStagingSuffix;
		DeleteFileQuietly(stagingPath);
		File.WriteAllBytes(stagingPath, content);
	}

	internal static void BackUpOriginalFile(string absolutePath, List<string> touchedPaths)
	{
		if (!File.Exists(absolutePath)) return;

		string backupPath = absolutePath + RestoreBackupSuffix;
		DeleteFileQuietly(backupPath);
		ClearReadOnlyAttribute(absolutePath);
		File.Move(absolutePath, backupPath, true);
		touchedPaths.Add(absolutePath);
	}

	internal static void RollBackRestore(List<string> touchedPaths, List<string> stagedPaths)
	{
		foreach (string touchedPath in touchedPaths)
		{
			string backupPath = touchedPath + RestoreBackupSuffix;
			try
			{
				File.Delete(touchedPath);
				if (File.Exists(backupPath))
				{
					File.Move(backupPath, touchedPath, true);
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				GameLog.LogError($"[MapHistoryService] Could not put '{touchedPath}' back: {exception.Message}");
			}
		}

		foreach (string stagedPath in stagedPaths)
		{
			DeleteFileQuietly(stagedPath + RestoreStagingSuffix);
		}
	}

	internal static void DeleteFileQuietly(string absolutePath)
	{
		if (!File.Exists(absolutePath)) return;

		try
		{
			ClearReadOnlyAttribute(absolutePath);
			File.Delete(absolutePath);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			GameLog.LogError($"[MapHistoryService] Could not remove '{absolutePath}': {exception.Message}");
		}
	}

	internal static void ClearReadOnlyAttribute(string absolutePath)
	{
		try
		{
			FileAttributes attributes = File.GetAttributes(absolutePath);
			if ((attributes & FileAttributes.ReadOnly) != 0)
			{
				File.SetAttributes(absolutePath, attributes & ~FileAttributes.ReadOnly);
			}
		}
		catch (Exception)
		{
		}
	}
}
