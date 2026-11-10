using System;
using System.IO;
using System.Text.Json;

namespace Realm.Client.Services;

/// <summary>
///     Reading a candidate map folder from disk without opening it: whether a folder holds a map at
///     all, whether it sits inside another map or contains one, and which of its documents fail to
///     parse. Only filesystem state is consulted, so no editor session is needed.
/// </summary>
public static class EditorMapFolderInspector
{
	public static bool IsExistingFolder(string? folder)
	{
		return !string.IsNullOrEmpty(folder) && Directory.Exists(folder);
	}

	public static bool IsNestedInExistingMap(string fullSelectedPath)
	{
		var parentDir = Directory.GetParent(fullSelectedPath);
		while (parentDir != null)
		{
			if (File.Exists(Path.Combine(parentDir.FullName, "manifest.json")))
			{
				return true;
			}

			parentDir = parentDir.Parent;
		}

		return false;
	}

	public static bool ContainsNestedMap(string fullSelectedPath)
	{
		if (!Directory.Exists(fullSelectedPath)) return false;
		try
		{
			foreach (string subDir in Directory.EnumerateDirectories(fullSelectedPath))
			{
				if (File.Exists(Path.Combine(subDir, "manifest.json")))
				{
					return true;
				}
			}
		}
		catch
		{
		}

		return false;
	}

	public static bool IsValidMapFolder(string folder)
	{
		return File.Exists(Path.Combine(folder, "metadata.json"))
			   && File.Exists(Path.Combine(folder, "manifest.json"));
	}

	public static bool IsMapFolderCorrupt(string folder, out string corruptFileName, out string parseErrorMessage)
	{
		corruptFileName = string.Empty;
		parseErrorMessage = string.Empty;

		if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
		{
			return false;
		}

		string[] targetFiles = { "metadata.json", "manifest.json", "terrain.json", "license.json" };

		foreach (string fileName in targetFiles)
		{
			string filePath = Path.Combine(folder, fileName);
			if (!File.Exists(filePath))
			{
				continue;
			}

			try
			{
				string json = File.ReadAllText(filePath);
				if (string.IsNullOrWhiteSpace(json))
				{
					corruptFileName = fileName;
					parseErrorMessage = "File is empty.";
					return true;
				}

				using var doc = JsonDocument.Parse(json);
			}
			catch (Exception ex)
			{
				corruptFileName = fileName;
				parseErrorMessage = ex.Message;
				return true;
			}
		}

		return false;
	}
}
