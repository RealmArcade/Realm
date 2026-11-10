using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Distribution;
using Realm.Shared.Services;

namespace Realm.Client.Utils;


public static partial class MapAssetHelper
{
	public static (bool IsValid, List<string> MissingFiles) ValidateWorkspaceAssets(string workspacePath)
	{
		var missingFiles = new List<string>();
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath))
		{
			missingFiles.Add(string.IsNullOrEmpty(workspacePath) ? "Workspace path is empty" : $"Workspace directory does not exist: {workspacePath}");
			return (false, missingFiles);
		}

		ValidateManifestAssets(workspacePath, missingFiles);
		ValidateMetadataAssets(workspacePath, missingFiles);

		var distinctMissing = missingFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		return (distinctMissing.Count == 0, distinctMissing);
	}

	private static void ValidateManifestAssets(string workspacePath, List<string> missingFiles)
	{
		string manifestPath = Path.Combine(workspacePath, "manifest.json");
		if (!File.Exists(manifestPath)) return;

		try
		{
			var manifest = MapFileService.LoadManifest(workspacePath);
			if (manifest.Assets == null) return;

			var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			MapManifest.FlattenAssetsInto(dict, manifest.Assets);

			foreach (var kvp in dict)
			{
				CheckManifestAsset(workspacePath, kvp.Key, missingFiles);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapAssetHelper] ValidateWorkspaceAssets error reading manifest: {ex.Message}");
		}
	}

	private static void CheckManifestAsset(string workspacePath, string relPathRaw, List<string> missingFiles)
	{
		string relPath = relPathRaw.TrimStart('/', '\\');
		string fullPath = Path.Combine(workspacePath, relPath);
		if (File.Exists(fullPath)) return;

		string fileName = Path.GetFileName(relPath);
		string? resolvedModel = FindModelOnDisk(workspacePath, null, fileName);
		if (!string.IsNullOrEmpty(resolvedModel) && File.Exists(resolvedModel)) return;

		string subFolder = relPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && relPath.Split('/').Length > 2
			? relPath.Split('/')[1]
			: "icons";

		string? resolvedAsset = FindAssetOnDisk(workspacePath, subFolder, relPath);
		if (string.IsNullOrEmpty(resolvedAsset) || !File.Exists(resolvedAsset))
		{
			missingFiles.Add(relPath);
		}
	}

	private static void ValidateMetadataAssets(string workspacePath, List<string> missingFiles)
	{
		string metadataPath = Path.Combine(workspacePath, "metadata.json");
		if (!File.Exists(metadataPath)) return;

		try
		{
			var metadata = MapFileService.LoadMetadata(workspacePath);
			ValidateTemplateModels(workspacePath, metadata, missingFiles);
			ValidateDictionaryAssets(workspacePath, metadata, missingFiles);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapAssetHelper] ValidateWorkspaceAssets error reading metadata: {ex.Message}");
		}
	}

	private static void ValidateTemplateModels(string workspacePath, MapMetadata metadata, List<string> missingFiles)
	{
		if (metadata.Templates == null) return;

		CheckModelPaths(workspacePath, metadata.Templates.Units?.SelectMany(e => new[] { e.ModelPath, e.PortraitModelPath }), "units", missingFiles);
		CheckModelPaths(workspacePath, metadata.Templates.Buildings?.SelectMany(e => new[] { e.ModelPath, e.PortraitModelPath }), "buildings", missingFiles);
		CheckModelPaths(workspacePath, metadata.Templates.Props?.SelectMany(e => new[] { e.ModelPath, e.PortraitModelPath }), "props", missingFiles);
		CheckModelPaths(workspacePath, metadata.Templates.Resources?.SelectMany(e => new[] { e.ModelPath, e.PortraitModelPath }), "resources", missingFiles);
		CheckModelPaths(workspacePath, metadata.Templates.Weapons?.Select(e => e.ProjectileModelPath), "weapons", missingFiles);
		CheckModelPaths(workspacePath, metadata.Templates.Attachments?.Select(e => e.ModelPath), "attachments", missingFiles);
	}

	private static void CheckModelPaths(string workspacePath, IEnumerable<string?>? paths, string subCat, List<string> missingFiles)
	{
		if (paths == null) return;

		foreach (var modelPath in paths)
		{
			CheckSingleModelPath(workspacePath, modelPath, subCat, missingFiles);
		}
	}

	private static void CheckSingleModelPath(string workspacePath, string? modelPath, string subCat, List<string> missingFiles)
	{
		if (string.IsNullOrWhiteSpace(modelPath) || modelPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase) || modelPath.StartsWith("user://", StringComparison.OrdinalIgnoreCase)) return;

		string fileName = Path.GetFileName(modelPath);
		string? diskPath = FindModelOnDisk(workspacePath, subCat, fileName)
		                   ?? FindAssetOnDisk(workspacePath, "decals", fileName)
		                   ?? FindAssetOnDisk(workspacePath, "textures", fileName);

		if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath)) return;

		string expectedRel = $"Assets/models/{subCat}/{fileName}".Replace('\\', '/');
		if (!expectedRel.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && !expectedRel.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			expectedRel = Path.ChangeExtension(expectedRel, ".rmesh");
		}

		missingFiles.Add(expectedRel);
	}

	private static void ValidateDictionaryAssets(string workspacePath, MapMetadata metadata, List<string> missingFiles)
	{
		ValidateAssetDict(workspacePath, metadata.Textures, v => v.TexturePath, "textures", missingFiles);
		ValidateAssetDict(workspacePath, metadata.Decals, v => v.TexturePath, "decals", missingFiles);
		ValidateAssetDict(workspacePath, metadata.VfxSpritesheets, v => v.TexturePath, "vfx_spritesheets", missingFiles);
		ValidateAssetDict(workspacePath, metadata.NoiseTextures, v => v.TexturePath, "noise", missingFiles);
		ValidateAssetDict(workspacePath, metadata.Icons, v => v.TexturePath, "icons", missingFiles);
		ValidateAssetDict(workspacePath, metadata.Skyboxes, v => v.TexturePath, "skyboxes", missingFiles);
		ValidateAssetDict(workspacePath, metadata.Ribbons, v => v.TexturePath, "ribbons", missingFiles);
	}

	private static void ValidateAssetDict<T>(string workspacePath, Dictionary<string, T>? dict, Func<T, string?> pathSelector, string subFolder, List<string> missingFiles)
	{
		if (dict == null) return;
		var keys = dict.Select(k =>
		{
			string? path = k.Value != null ? pathSelector(k.Value) : null;
			return !string.IsNullOrEmpty(path) ? path : k.Key;
		});
		CheckDictionaryAssets(workspacePath, keys, subFolder, missingFiles);
	}

	private static void CheckDictionaryAssets(string workspacePath, IEnumerable<string>? keys, string subFolder, List<string> missingFiles)
	{
		if (keys == null) return;
		foreach (var key in keys)
		{
			if (string.IsNullOrWhiteSpace(key)) continue;

			string? diskPath = FindAssetOnDisk(workspacePath, subFolder, key);
			if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath))
			{
				var (pType, pSlug) = TemplateIDHelper.ParseTemplateID(key);
				string fileToReport = (!string.IsNullOrEmpty(pType) && !string.IsNullOrEmpty(pSlug)) ? pSlug : key;
				if (!Path.HasExtension(fileToReport))
				{
					string defaultExt = GetDefaultExtensionForSubFolder(subFolder);
					fileToReport += defaultExt;
				}

				string expectedRel = subFolder == "other" ? fileToReport : $"Assets/{subFolder}/{fileToReport}".Replace('\\', '/');
				missingFiles.Add(expectedRel);
			}
		}
	}

	private static string GetDefaultExtensionForSubFolder(string subFolder)
	{
		return subFolder switch
		{
			"animations" => ".ranim",
			"audio/sfx" or "audio/music" => ".raud",
			"models/units" or "models/buildings" or "models/props" or "models/items" => ".rmesh",
			_ => ".rtex"
		};
	}

	private static bool IsIgnoredAssetFile(string relativePath)
	{
		foreach (var prefix in IgnoredPrefixes)
		{
			if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
		}

		foreach (var ext in IgnoredExtensions)
		{
			if (relativePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
		}

		if (string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(Path.GetFileName(relativePath), "authorship_key.pem", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
	}

	public static (bool IsValid, List<(string RelativePath, long SizeBytes, double SizeMB)> OversizedFiles) ValidateWorkspaceAssetSizes(string workspacePath, long maxSizeBytes = ContentAddressableStorage.MaximumAssetSizeBytes)
	{
		var oversized = new List<(string RelativePath, long SizeBytes, double SizeMB)>();
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath))
		{
			return (true, oversized);
		}

		string fullDirectoryPath = Path.GetFullPath(workspacePath);
		string[] allFiles = Directory.GetFiles(fullDirectoryPath, "*.*", SearchOption.AllDirectories);

		foreach (string filePath in allFiles)
		{
			string relativePath = Path.GetRelativePath(fullDirectoryPath, filePath).Replace('\\', '/');

			if (IsIgnoredAssetFile(relativePath) && !(relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) && relativePath.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			var fileInfo = new FileInfo(filePath);
			if (fileInfo.Exists && fileInfo.Length > maxSizeBytes)
			{
				double sizeMb = fileInfo.Length / (1024.0 * 1024.0);
				oversized.Add((relativePath, fileInfo.Length, sizeMb));
			}
		}

		return (oversized.Count == 0, oversized);
	}

}
