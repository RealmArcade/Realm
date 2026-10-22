using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Network;
using Realm.Client.Utils;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.Client.Services;

public class MapAssetImportService
{
	public IReadOnlyList<MapAssetImportSource> GetImportSources()
	{
		var sources = new List<MapAssetImportSource>();

		foreach (var downloadedMap in GameContext.MapStorageService.GetDownloadedMaps())
		{
			foreach (var version in downloadedMap.Versions)
			{
				if (string.IsNullOrEmpty(version.ManifestFilePath) || !File.Exists(version.ManifestFilePath)) continue;

				sources.Add(new MapAssetImportSource(downloadedMap.Title, version.Version, version.ManifestFilePath));
			}
		}

		return sources;
	}

	public IReadOnlyList<MapAssetImportCandidate> EnumerateAssets(string manifestFilePath, string destinationWorkspacePath)
	{
		var candidates = new List<MapAssetImportCandidate>();
		if (string.IsNullOrEmpty(manifestFilePath) || !File.Exists(manifestFilePath)) return candidates;

		MapManifest manifest;
		try
		{
			manifest = MapManifest.LoadFromFile(manifestFilePath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapAssetImportService] Failed to read manifest '{manifestFilePath}': {ex.Message}");
			return candidates;
		}

		var categoryByFileName = BuildCategoryLookup(manifest);

		foreach (var fileEntry in manifest.Files)
		{
			string relativePath = fileEntry.Key.Replace('\\', '/').TrimStart('/');
			if (string.IsNullOrEmpty(relativePath) || string.IsNullOrEmpty(fileEntry.Value)) continue;

			string fileName = Path.GetFileName(relativePath);
			if (string.IsNullOrEmpty(fileName)) continue;

			string category = categoryByFileName.TryGetValue(fileName, out var resolvedCategory)
				? resolvedCategory
				: MapAssetHelper.NormalizeCategoryKey(GetFolderCategoryKey(relativePath));

			bool alreadyPresent = IsAlreadyPresent(destinationWorkspacePath, relativePath, fileEntry.Value);
			candidates.Add(new MapAssetImportCandidate(category, fileName, relativePath, fileEntry.Value, alreadyPresent));
		}

		candidates.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
		return candidates;
	}

	public MapAssetImportSummary ImportInto(string destinationWorkspacePath, IReadOnlyCollection<MapAssetImportCandidate> candidates)
	{
		var summary = new MapAssetImportSummary();
		if (candidates == null || candidates.Count == 0) return summary;
		if (string.IsNullOrEmpty(destinationWorkspacePath) || !Directory.Exists(destinationWorkspacePath))
		{
			GD.PrintErr($"[MapAssetImportService] Destination workspace does not exist: {destinationWorkspacePath}");
			summary.Missing.AddRange(BuildNames(candidates));
			return summary;
		}

		var manifestEntries = new List<(string Category, string FileName, string Hash)>();

		foreach (var candidate in candidates)
		{
			ImportCandidate(destinationWorkspacePath, candidate, summary, manifestEntries);
		}

		RegisterManifestEntries(destinationWorkspacePath, manifestEntries);
		return summary;
	}

	private void ImportCandidate(string destinationWorkspacePath, MapAssetImportCandidate candidate, MapAssetImportSummary summary, List<(string Category, string FileName, string Hash)> manifestEntries)
	{
		string destinationPath = Path.Combine(destinationWorkspacePath, candidate.RelativePath.Replace('/', Path.DirectorySeparatorChar));

		if (File.Exists(destinationPath))
		{
			if (HasMatchingContent(destinationPath, candidate.Hash))
			{
				manifestEntries.Add((candidate.Category, candidate.FileName, candidate.Hash));
				summary.Skipped.Add(candidate.FileName);
			}
			else
			{
				summary.Conflicts.Add(candidate.FileName);
			}

			return;
		}

		if (!MapAssetManager.ExtractSingleAsset(candidate.RelativePath, candidate.Hash, destinationWorkspacePath))
		{
			summary.Missing.Add(candidate.FileName);
			return;
		}

		if (!TryPrepareImportedAsset(destinationPath, out string registeredHash))
		{
			summary.Failed.Add(candidate.FileName);
			return;
		}

		manifestEntries.Add((candidate.Category, candidate.FileName, registeredHash));
		summary.Imported.Add(candidate.FileName);
	}

	private static bool TryPrepareImportedAsset(string destinationPath, out string registeredHash)
	{
		registeredHash = string.Empty;

		try
		{
			if (!File.Exists(destinationPath))
			{
				return false;
			}

			RealmMetadataHelper.EnsureMetadata(destinationPath);
			string extension = Path.GetExtension(destinationPath).ToLowerInvariant();
			registeredHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(destinationPath), extension);
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapAssetImportService] Failed to prepare '{destinationPath}': {ex.Message}");
			return false;
		}
	}

	private static void RegisterManifestEntries(string destinationWorkspacePath, List<(string Category, string FileName, string Hash)> manifestEntries)
	{
		if (manifestEntries.Count == 0) return;

		try
		{
			var assets = MapAssetHelper.LoadAssets(destinationWorkspacePath);
			foreach (var entry in manifestEntries)
			{
				string normalizedCategory = MapAssetHelper.NormalizeCategoryKey(entry.Category);
				var categoryAssets = assets.GetCategory(normalizedCategory);
				if (categoryAssets == null)
				{
					categoryAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					assets.SetCategory(normalizedCategory, categoryAssets);
				}

				categoryAssets[entry.FileName] = entry.Hash;
			}

			MapAssetHelper.SaveAssetsToManifest(destinationWorkspacePath, assets);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapAssetImportService] Failed to register imported assets in manifest.json: {ex.Message}");
		}
	}

	private static bool IsAlreadyPresent(string destinationWorkspacePath, string relativePath, string hash)
	{
		if (string.IsNullOrEmpty(destinationWorkspacePath)) return false;

		string destinationPath = Path.Combine(destinationWorkspacePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
		return File.Exists(destinationPath) && HasMatchingContent(destinationPath, hash);
	}

	private static bool HasMatchingContent(string filePath, string hash)
	{
		try
		{
			string normalizedExpected = ContentAddressableStorage.NormalizeBlake3Hash(hash);
			string actual = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(filePath), Path.GetExtension(filePath).ToLowerInvariant());
			return string.Equals(ContentAddressableStorage.NormalizeBlake3Hash(actual), normalizedExpected, StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapAssetImportService] Failed to hash '{filePath}': {ex.Message}");
			return false;
		}
	}

	private static Dictionary<string, string> BuildCategoryLookup(MapManifest manifest)
	{
		var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (manifest.Assets == null) return lookup;

		foreach (var categoryEntry in manifest.Assets.GetAllCategories())
		{
			if (string.Equals(categoryEntry.Key, "Other", StringComparison.OrdinalIgnoreCase)) continue;

			foreach (var assetEntry in categoryEntry.Value)
			{
				string fileName = Path.GetFileName(assetEntry.Key.Replace('\\', '/'));
				if (string.IsNullOrEmpty(fileName)) continue;

				lookup[fileName] = MapAssetHelper.NormalizeCategoryKey(categoryEntry.Key);
			}
		}

		return lookup;
	}

	private static string GetFolderCategoryKey(string relativePath)
	{
		string normalized = relativePath.Replace('\\', '/').TrimStart('/');
		const string assetsPrefix = "Assets/";
		if (normalized.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized.Substring(assetsPrefix.Length);
		}

		int slashIndex = normalized.IndexOf('/');
		if (slashIndex <= 0) return string.Empty;

		string folder = normalized.Substring(0, slashIndex);
		int nestedSlashIndex = folder.IndexOf('/');
		return nestedSlashIndex >= 0 ? folder.Substring(0, nestedSlashIndex) : folder;
	}

	private static IEnumerable<string> BuildNames(IEnumerable<MapAssetImportCandidate> candidates)
	{
		foreach (var candidate in candidates)
		{
			yield return candidate.FileName;
		}
	}
}

public readonly record struct MapAssetImportSource(string MapTitle, string MapVersion, string ManifestFilePath);

public class MapAssetImportCandidate
{
	public MapAssetImportCandidate(string category, string fileName, string relativePath, string hash, bool alreadyPresent)
	{
		Category = category;
		FileName = fileName;
		RelativePath = relativePath;
		Hash = hash;
		AlreadyPresent = alreadyPresent;
	}

	public string Category { get; }
	public string FileName { get; }
	public string RelativePath { get; }
	public string Hash { get; }
	public bool AlreadyPresent { get; }
}

public class MapAssetImportSummary
{
	public List<string> Imported { get; } = new();
	public List<string> Skipped { get; } = new();
	public List<string> Missing { get; } = new();
	public List<string> Conflicts { get; } = new();
	public List<string> Failed { get; } = new();

	public int TotalProcessed => Imported.Count + Skipped.Count + Missing.Count + Conflicts.Count + Failed.Count;
}
