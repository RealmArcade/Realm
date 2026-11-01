using System;
using System.IO;

namespace Realm.Shared.Services;

public static class AtomicFileWriter
{
	public const string TemporarySuffix = ".realm-save.tmp";

	public static void WriteAllBytes(string targetPath, byte[] contents)
	{
		string temporaryPath = targetPath + TemporarySuffix;
		EnsureDirectoryExists(targetPath);
		try
		{
			File.WriteAllBytes(temporaryPath, contents);
			File.Move(temporaryPath, targetPath, true);
		}
		finally
		{
			DeleteTemporaryFile(temporaryPath);
		}
	}

	public static void WriteAllText(string targetPath, string contents)
	{
		string temporaryPath = targetPath + TemporarySuffix;
		EnsureDirectoryExists(targetPath);
		try
		{
			File.WriteAllText(temporaryPath, contents);
			File.Move(temporaryPath, targetPath, true);
		}
		finally
		{
			DeleteTemporaryFile(temporaryPath);
		}
	}

	private static void EnsureDirectoryExists(string targetPath)
	{
		string? directory = Path.GetDirectoryName(targetPath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}
	}

	private static void DeleteTemporaryFile(string temporaryPath)
	{
		if (!File.Exists(temporaryPath)) return;

		try
		{
			File.Delete(temporaryPath);
		}
		catch (IOException)
		{
		}
	}
}
