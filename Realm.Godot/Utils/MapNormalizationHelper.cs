using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.Godot.Services;
using Realm.Shared.Distribution;

namespace Realm.Godot.Utils;

public class GreenlitReferenceSuggestion
{
	public string MapTitle { get; set; } = string.Empty;
	public string MapVersion { get; set; } = string.Empty;
	public List<string> MatchedAssetPaths { get; set; } = new();
	public List<string> MatchedHashes { get; set; } = new();
	public long SavedBytes { get; set; }
	public bool IsSelected { get; set; } = true;
}

public static class MapNormalizationHelper
{
	private class CandidateMapInfo
	{
		public string MapTitle { get; set; } = string.Empty;
		public string MapVersion { get; set; } = "1.0.0";
		public HashSet<string> AssetHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	}

	public static List<GreenlitReferenceSuggestion> FindGreenlitReferenceSuggestions(
		string workspacePath,
		int minMatchingAssets = 3,
		long minMatchingBytes = 65536)
	{
		string targetDirectory = string.IsNullOrEmpty(workspacePath)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: workspacePath;

		if (!Directory.Exists(targetDirectory))
		{
			return new List<GreenlitReferenceSuggestion>();
		}

		string manifestPath = Path.Combine(targetDirectory, "manifest.json");
		MapManifest? currentManifest = null;
		if (File.Exists(manifestPath))
		{
			try
			{
				currentManifest = MapManifest.LoadFromJson(File.ReadAllText(manifestPath));
			}
			catch
			{
			}
		}

		currentManifest ??= MapManifest.CreateFromDirectory(targetDirectory, Path.GetFileName(targetDirectory), "Unknown");

		if (currentManifest.Files == null || currentManifest.Files.Count == 0)
		{
			return new List<GreenlitReferenceSuggestion>();
		}

		string currentMapName = !string.IsNullOrEmpty(currentManifest.MapName) ? currentManifest.MapName : Path.GetFileName(targetDirectory);
		var existingReferences = currentManifest.GreenlitReferences != null
			? new HashSet<string>(currentManifest.GreenlitReferences, StringComparer.OrdinalIgnoreCase)
			: new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		var workspaceHashes = LoadWorkspaceHashes(currentManifest, targetDirectory);
		if (workspaceHashes.Count == 0)
		{
			return new List<GreenlitReferenceSuggestion>();
		}

		var candidateMaps = DiscoverAllCandidateManifests(currentMapName);
		var uncoveredHashes = new HashSet<string>(workspaceHashes.Keys, StringComparer.OrdinalIgnoreCase);
		var suggestions = new List<GreenlitReferenceSuggestion>();

		while (uncoveredHashes.Count > 0)
		{
			var (bestCandidate, bestMatchedHashes, bestMatchedBytes) = FindBestCandidate(
				candidateMaps, existingReferences, suggestions, uncoveredHashes, workspaceHashes, minMatchingAssets, minMatchingBytes);

			if (bestCandidate == null || bestMatchedHashes.Count == 0)
			{
				break;
			}

			var allMatchedPaths = new List<string>();
			foreach (var hash in bestMatchedHashes)
			{
				if (workspaceHashes.TryGetValue(hash, out var hashEntry))
				{
					allMatchedPaths.AddRange(hashEntry.RelativePaths);
				}
				uncoveredHashes.Remove(hash);
			}

			suggestions.Add(new GreenlitReferenceSuggestion
			{
				MapTitle = bestCandidate.MapTitle,
				MapVersion = bestCandidate.MapVersion,
				MatchedAssetPaths = allMatchedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
				MatchedHashes = bestMatchedHashes,
				SavedBytes = bestMatchedBytes,
				IsSelected = true
			});
		}

		return suggestions;
	}

	private static long GetFileSize(MapManifest currentManifest, string targetDirectory, string fileKey, string relativePath)
	{
		if (currentManifest.FileSizes != null && currentManifest.FileSizes.TryGetValue(fileKey, out long foundSize))
		{
			return foundSize;
		}

		string diskPath = Path.Combine(targetDirectory, relativePath);
		return File.Exists(diskPath) ? new FileInfo(diskPath).Length : 0;
	}

	private static Dictionary<string, (List<string> RelativePaths, long Size)> LoadWorkspaceHashes(MapManifest currentManifest, string targetDirectory)
	{
		var workspaceHashes = new Dictionary<string, (List<string> RelativePaths, long Size)>(StringComparer.OrdinalIgnoreCase);
		if (currentManifest.Files == null) return workspaceHashes;

		foreach (var pair in currentManifest.Files)
		{
			string relativePath = pair.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? pair.Key.Substring(6) : pair.Key;
			relativePath = relativePath.TrimStart('/', '\\').Replace('\\', '/');

			if (!relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(pair.Value);
			if (string.IsNullOrEmpty(normalizedHash))
			{
				continue;
			}

			long size = GetFileSize(currentManifest, targetDirectory, pair.Key, relativePath);

			if (!workspaceHashes.TryGetValue(normalizedHash, out var entry))
			{
				entry = (new List<string>(), size);
				workspaceHashes[normalizedHash] = entry;
			}
			entry.RelativePaths.Add(relativePath);
		}

		return workspaceHashes;
	}

	private static (CandidateMapInfo? bestCandidate, List<string> bestMatchedHashes, long bestMatchedBytes) FindBestCandidate(
		List<CandidateMapInfo> candidateMaps,
		HashSet<string> existingReferences,
		List<GreenlitReferenceSuggestion> suggestions,
		HashSet<string> uncoveredHashes,
		Dictionary<string, (List<string> RelativePaths, long Size)> workspaceHashes,
		int minMatchingAssets,
		long minMatchingBytes)
	{
		CandidateMapInfo? bestCandidate = null;
		List<string> bestMatchedHashes = new();
		long bestMatchedBytes = 0;

		foreach (var candidate in candidateMaps)
		{
			if (existingReferences.Contains(candidate.MapTitle) || suggestions.Any(s => string.Equals(s.MapTitle, candidate.MapTitle, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			var matched = candidate.AssetHashes.Where(h => uncoveredHashes.Contains(h)).ToList();
			if (matched.Count == 0)
			{
				continue;
			}

			long matchedBytes = matched
				.Select(h => workspaceHashes.TryGetValue(h, out var entry) ? entry.Size : 0L)
				.Sum();

			if (matched.Count < minMatchingAssets && matchedBytes < minMatchingBytes)
			{
				continue;
			}

			if (bestCandidate == null || matched.Count > bestMatchedHashes.Count || (matched.Count == bestMatchedHashes.Count && matchedBytes > bestMatchedBytes))
			{
				bestCandidate = candidate;
				bestMatchedHashes = matched;
				bestMatchedBytes = matchedBytes;
			}
		}

		return (bestCandidate, bestMatchedHashes, bestMatchedBytes);
	}

	public static HashSet<string> GetExcludedRelativePaths(string workspacePath)
	{
		var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string targetDirectory = string.IsNullOrEmpty(workspacePath)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: workspacePath;

		if (!Directory.Exists(targetDirectory))
		{
			return excluded;
		}

		string manifestPath = Path.Combine(targetDirectory, "manifest.json");
		if (!File.Exists(manifestPath))
		{
			return excluded;
		}

		MapManifest? manifest = null;
		try
		{
			manifest = MapManifest.LoadFromJson(File.ReadAllText(manifestPath));
		}
		catch
		{
		}

		if (manifest == null || manifest.GreenlitReferences == null || manifest.GreenlitReferences.Count == 0 || manifest.Files == null)
		{
			return excluded;
		}

		var referencedHashes = BuildReferencedHashes(manifest);
		if (referencedHashes.Count == 0)
		{
			return excluded;
		}

		GatherExcludedPaths(manifest, referencedHashes, excluded);
		return excluded;
	}

	private static HashSet<string> BuildReferencedHashes(MapManifest manifest)
	{
		var referencedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var candidateMaps = DiscoverAllCandidateManifests(manifest.MapName ?? string.Empty);
		var candidateMapDictionary = candidateMaps.ToDictionary(c => c.MapTitle, c => c, StringComparer.OrdinalIgnoreCase);

		foreach (var referenceTitle in manifest.GreenlitReferences ?? Enumerable.Empty<string>())
		{
			if (candidateMapDictionary.TryGetValue(referenceTitle, out var candidate))
			{
				foreach (var hash in candidate.AssetHashes)
				{
					referencedHashes.Add(hash);
				}
			}
		}

		return referencedHashes;
	}

	private static void GatherExcludedPaths(MapManifest manifest, HashSet<string> referencedHashes, HashSet<string> excluded)
	{
		foreach (var pair in manifest.Files ?? new Dictionary<string, string>())
		{
			string relativePath = pair.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? pair.Key.Substring(6) : pair.Key;
			relativePath = relativePath.TrimStart('/', '\\').Replace('\\', '/');

			if (!relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(pair.Value);
			if (referencedHashes.Contains(normalizedHash))
			{
				excluded.Add(relativePath);
			}
		}
	}

	public static void ApplyGreenlitReferences(string workspacePath, IEnumerable<string> selectedMapTitles)
	{
		string targetDirectory = string.IsNullOrEmpty(workspacePath)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: workspacePath;

		if (!Directory.Exists(targetDirectory))
		{
			return;
		}

		string manifestPath = Path.Combine(targetDirectory, "manifest.json");
		MapManifest? manifest = null;
		if (File.Exists(manifestPath))
		{
			try
			{
				manifest = MapManifest.LoadFromJson(File.ReadAllText(manifestPath));
			}
			catch
			{
			}
		}

		manifest ??= MapManifest.CreateFromDirectory(targetDirectory, Path.GetFileName(targetDirectory), "Unknown");

		manifest.GreenlitReferences ??= new List<string>();
		foreach (var title in selectedMapTitles)
		{
			if (!string.IsNullOrWhiteSpace(title) && !manifest.GreenlitReferences.Contains(title.Trim(), StringComparer.OrdinalIgnoreCase))
			{
				manifest.GreenlitReferences.Add(title.Trim());
			}
		}

		manifest.SaveToFile(manifestPath);
	}

	public static void RemoveGreenlitReference(string workspacePath, string mapTitle)
	{
		string targetDirectory = string.IsNullOrEmpty(workspacePath)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: workspacePath;

		if (!Directory.Exists(targetDirectory))
		{
			return;
		}

		string manifestPath = Path.Combine(targetDirectory, "manifest.json");
		if (File.Exists(manifestPath))
		{
			try
			{
				var manifest = MapManifest.LoadFromJson(File.ReadAllText(manifestPath));
				if (manifest?.GreenlitReferences != null)
				{
					manifest.GreenlitReferences.RemoveAll(r => string.Equals(r, mapTitle, StringComparison.OrdinalIgnoreCase));
					manifest.SaveToFile(manifestPath);
				}
			}
			catch
			{
			}
		}
	}

	public static List<string> GetCurrentGreenlitReferences(string workspacePath)
	{
		string targetDirectory = string.IsNullOrEmpty(workspacePath)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: workspacePath;

		if (!Directory.Exists(targetDirectory))
		{
			return new List<string>();
		}

		string manifestPath = Path.Combine(targetDirectory, "manifest.json");
		if (File.Exists(manifestPath))
		{
			try
			{
				var manifest = MapManifest.LoadFromJson(File.ReadAllText(manifestPath));
				if (manifest?.GreenlitReferences != null)
				{
					return manifest.GreenlitReferences;
				}
			}
			catch
			{
			}
		}

		return new List<string>();
	}

	public static (int unprunedCount, int unnormalizedPotentialCount, long potentialSavedBytes) CheckOptimizationState(string workspacePath)
	{
		int unpruned = 0;
		int unnormalized = 0;
		long savedBytes = 0;

		try
		{
			string targetDirectory = string.IsNullOrEmpty(workspacePath)
				? MapWorkspaceService.GetActiveWorkspacePath()
				: workspacePath;

			if (Directory.Exists(targetDirectory))
			{
				var suggestions = FindGreenlitReferenceSuggestions(targetDirectory);
				unnormalized = suggestions.Count;
				savedBytes = suggestions.Sum(s => s.SavedBytes);
			}
		}
		catch
		{
		}

		return (unpruned, unnormalized, savedBytes);
	}

	private static List<CandidateMapInfo> DiscoverAllCandidateManifests(string currentMapName)
	{
		var results = new Dictionary<string, CandidateMapInfo>(StringComparer.OrdinalIgnoreCase);

		DiscoverFromStorageService(currentMapName, results);
		DiscoverFromAssetIndex(currentMapName, results);
		DiscoverFromResourceMaps(currentMapName, results);

		return results.Values.ToList();
	}

	private static HashSet<string>? GetHashesFromManifestFile(string manifestPath)
	{
		try
		{
			var manifest = MapManifest.LoadFromJson(File.ReadAllText(manifestPath));
			if (manifest?.Files != null && manifest.Files.Count > 0)
			{
				return manifest.Files.Values
					.Select(ContentAddressableStorage.NormalizeBlake3Hash)
					.Where(h => !string.IsNullOrEmpty(h))
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
			}
		}
		catch
		{
		}
		return null;
	}

	private static void UpdateCandidateIfBetter(Dictionary<string, CandidateMapInfo> results, string mapTitle, string mapVersion, HashSet<string> hashes)
	{
		if (!results.TryGetValue(mapTitle, out var existing) || hashes.Count > existing.AssetHashes.Count)
		{
			results[mapTitle] = new CandidateMapInfo
			{
				MapTitle = mapTitle,
				MapVersion = mapVersion,
				AssetHashes = hashes
			};
		}
	}

	private static void DiscoverFromStorageService(string currentMapName, Dictionary<string, CandidateMapInfo> results)
	{
		try
		{
			var storageService = ServiceLocator.TryGet<MapStorageService>() ?? new MapStorageService(null!);
			var downloadedMaps = storageService.GetDownloadedMaps();
			foreach (var map in downloadedMaps)
			{
				if (string.Equals(map.Title, currentMapName, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				foreach (var versionInfo in map.Versions)
				{
					if (string.IsNullOrEmpty(versionInfo.ManifestFilePath) || !File.Exists(versionInfo.ManifestFilePath))
					{
						continue;
					}

					var hashes = GetHashesFromManifestFile(versionInfo.ManifestFilePath);
					if (hashes != null)
					{
						UpdateCandidateIfBetter(results, map.Title, versionInfo.Version, hashes);
					}
				}
			}
		}
		catch
		{
		}
	}

	private static void DiscoverFromAssetIndex(string currentMapName, Dictionary<string, CandidateMapInfo> results)
	{
		try
		{
			var packages = AssetIndexService.Instance.GetDownloadedMapPackages();
			foreach (var package in packages)
			{
				if (string.Equals(package.MapName, currentMapName, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				if (!string.IsNullOrEmpty(package.ManifestPath) && File.Exists(package.ManifestPath))
				{
					var hashes = GetHashesFromManifestFile(package.ManifestPath);
					if (hashes != null)
					{
						UpdateCandidateIfBetter(results, package.MapName, package.MapVersion, hashes);
					}
				}
				else if (package.AssetHashes != null && package.AssetHashes.Count > 0)
				{
					var hashes = package.AssetHashes
						.Select(ContentAddressableStorage.NormalizeBlake3Hash)
						.Where(h => !string.IsNullOrEmpty(h))
						.ToHashSet(StringComparer.OrdinalIgnoreCase);

					UpdateCandidateIfBetter(results, package.MapName, package.MapVersion, hashes);
				}
			}
		}
		catch
		{
		}
	}

	private static void DiscoverFromResourceMaps(string currentMapName, Dictionary<string, CandidateMapInfo> results)
	{
		try
		{
			string resourceMapsDirectory = ProjectSettings.GlobalizePath("res://Maps");
			if (!Directory.Exists(resourceMapsDirectory))
			{
				return;
			}

			var manifestFiles = Directory.GetFiles(resourceMapsDirectory, "manifest.json", SearchOption.AllDirectories);
			foreach (var manifestFile in manifestFiles)
			{
				try
				{
					var manifest = MapManifest.LoadFromJson(File.ReadAllText(manifestFile));
					if (manifest == null || string.IsNullOrEmpty(manifest.MapName) || string.Equals(manifest.MapName, currentMapName, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					if (manifest.Files != null && manifest.Files.Count > 0)
					{
						var hashes = manifest.Files.Values
							.Select(ContentAddressableStorage.NormalizeBlake3Hash)
							.Where(h => !string.IsNullOrEmpty(h))
							.ToHashSet(StringComparer.OrdinalIgnoreCase);

						UpdateCandidateIfBetter(results, manifest.MapName, manifest.Version ?? "1.0.0", hashes);
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}
}
