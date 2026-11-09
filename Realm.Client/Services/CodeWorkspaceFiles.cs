using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Network;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     Which files in a map workspace or an extracted source map count as C# code, and the read that
///     turns them into the import service's source files. The manifest is preferred; anything it does
///     not carry is read from the map folder, and the editor's scratch and template files are skipped.
/// </summary>
public static class CodeWorkspaceFiles
{

	public static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"bin",
		"obj",
		".git",
		".vs",
		".godot",
		".vscode",
		".sidecarcache",
		".backups",
		"backups"
	};

	private static readonly HashSet<string> ExcludedTemplateFileNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"MapScript.cs",
		"Coordinates.cs",
		"WasmEntryPoint.cs",
		"WasmEntryPoint.g.cs",
		"MapTemplates.g.cs",
		MapCodeImportService.SourceMapAttributeTypeName + ".cs"
	};


	public static SourceCodeIndex LoadSourceIndex(MapAssetImportSource source)
	{
		List<SourceFile> sourceFiles = LoadSourceFiles(source);
		return SourceCodeIndex.Build(sourceFiles);
	}

	private static List<SourceFile> LoadSourceFiles(MapAssetImportSource source)
	{
		var sourceFiles = new List<SourceFile>();
		if (string.IsNullOrEmpty(source.ManifestFilePath)) return sourceFiles;

		MapManifest manifest;
		try
		{
			manifest = MapManifest.LoadFromFile(source.ManifestFilePath);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapCodeImportService] Failed to read source manifest '{source.ManifestFilePath}': {exception.Message}");
			return sourceFiles;
		}

		string sourceDirectory = Path.GetDirectoryName(source.ManifestFilePath) ?? string.Empty;
		string? extractionDirectory = null;

		try
		{
			foreach (var fileEntry in manifest.Files)
			{
				string relativePath = NormalizeCodeRelativePath(fileEntry.Key);
				if (!IsCodeFilePath(relativePath)) continue;
				if (string.IsNullOrEmpty(fileEntry.Value)) continue;

				string text;
				string directPath = MapCodeImportService.GetAbsolutePath(sourceDirectory, relativePath);

				if (File.Exists(directPath))
				{
					text = File.ReadAllText(directPath);
				}
				else
				{
					extractionDirectory ??= Path.Combine(Path.GetTempPath(), "realm_code_import", Guid.NewGuid().ToString("N"));
					if (!MapAssetManager.ExtractSingleAsset(relativePath, fileEntry.Value, extractionDirectory)) continue;

					string extractedPath = MapCodeImportService.GetAbsolutePath(extractionDirectory, relativePath);
					if (!File.Exists(extractedPath)) continue;

					text = File.ReadAllText(extractedPath);
				}

				AddSourceFile(sourceFiles, relativePath, text);
			}

			LoadSourceFilesFromDisk(sourceDirectory, sourceFiles);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapCodeImportService] Failed to load source code files: {exception.Message}");
		}
		finally
		{
			if (extractionDirectory != null && Directory.Exists(extractionDirectory))
			{
				try
				{
					Directory.Delete(extractionDirectory, true);
				}
				catch
				{
				}
			}
		}

		if (sourceFiles.Count == 0)
		{
			GD.PushWarning($"[MapCodeImportService] No C# source files were found for '{source.MapTitle}' at '{source.ManifestFilePath}'.");
		}

		return sourceFiles;
	}

	private static void LoadSourceFilesFromDisk(string sourceDirectory, List<SourceFile> sourceFiles)
	{
		if (string.IsNullOrEmpty(sourceDirectory) || !Directory.Exists(sourceDirectory)) return;

		foreach (string filePath in EnumerateCodeFiles(sourceDirectory, includeTemplateFiles: true))
		{
			string relativePath = Path.GetRelativePath(sourceDirectory, filePath).Replace('\\', '/');
			if (!IsCodeFilePath(relativePath)) continue;
			if (ContainsSourceFile(sourceFiles, relativePath)) continue;

			try
			{
				AddSourceFile(sourceFiles, relativePath, File.ReadAllText(filePath));
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[MapCodeImportService] Failed to read source file '{filePath}': {exception.Message}");
			}
		}
	}

	private static void AddSourceFile(List<SourceFile> sourceFiles, string relativePath, string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return;
		if (ContainsSourceFile(sourceFiles, relativePath)) return;

		sourceFiles.Add(new SourceFile(relativePath, text, !IsImportableCodePath(relativePath)));
	}

	private static bool ContainsSourceFile(List<SourceFile> sourceFiles, string relativePath)
	{
		for (int index = 0; index < sourceFiles.Count; index++)
		{
			if (string.Equals(sourceFiles[index].RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	private static string NormalizeCodeRelativePath(string manifestKey)
	{
		string relativePath = manifestKey.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
			? manifestKey.Substring(6)
			: manifestKey;
		return relativePath.TrimStart('/', '\\').Replace('\\', '/');
	}

	private static bool IsCodeFilePath(string relativePath)
	{
		if (!relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return false;
		if (relativePath.EndsWith(".template.cs", StringComparison.OrdinalIgnoreCase)) return false;

		string[] segments = relativePath.Split('/');
		for (int index = 0; index < segments.Length - 1; index++)
		{
			if (CodeWorkspaceFiles.IgnoredDirectoryNames.Contains(segments[index])) return false;
		}

		return true;
	}

	public static IEnumerable<string> EnumerateCodeFiles(string workspacePath, bool includeTemplateFiles)
	{
		var directories = new Stack<string>();
		directories.Push(workspacePath);

		while (directories.Count > 0)
		{
			string directory = directories.Pop();
			string[] childDirectories;
			string[] childFiles;

			try
			{
				childDirectories = Directory.GetDirectories(directory);
				childFiles = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"could not read a directory while collecting the map's code files, so the import is incomplete: {ex.GetType().Name}: {ex.Message}");
				continue;
			}

			foreach (string childDirectory in childDirectories)
			{
				if (CodeWorkspaceFiles.IgnoredDirectoryNames.Contains(Path.GetFileName(childDirectory))) continue;
				directories.Push(childDirectory);
			}

			foreach (string childFile in childFiles)
			{
				if (!includeTemplateFiles && IsExcludedTemplateFile(childFile, workspacePath)) continue;
				yield return childFile;
			}
		}
	}

	private static bool IsExcludedTemplateFile(string filePath, string workspaceRoot)
	{
		string relativePath = Path.GetRelativePath(workspaceRoot, filePath).Replace('\\', '/');
		if (relativePath.Contains('/')) return false;

		string fileName = Path.GetFileName(relativePath);
		return CodeWorkspaceFiles.ExcludedTemplateFileNames.Contains(fileName)
		       || fileName.EndsWith(".template.cs", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsImportableCodePath(string relativePath)
	{
		if (!relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return false;
		if (relativePath.EndsWith(".template.cs", StringComparison.OrdinalIgnoreCase)) return false;

		string[] segments = relativePath.Split('/');
		for (int index = 0; index < segments.Length - 1; index++)
		{
			if (CodeWorkspaceFiles.IgnoredDirectoryNames.Contains(segments[index])) return false;
		}

		if (segments.Length == 1 && CodeWorkspaceFiles.ExcludedTemplateFileNames.Contains(segments[0])) return false;

		return true;
	}
}
