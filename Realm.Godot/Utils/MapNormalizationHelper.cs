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

		var workspaceHashes = new Dictionary<string, (List<string> RelativePaths, long Size)>(StringComparer.OrdinalIgnoreCase);
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

			long size = 0;
			if (currentManifest.FileSizes != null && currentManifest.FileSizes.TryGetValue(pair.Key, out long foundSize))
			{
				size = foundSize;
			}
			else
			{
				string diskPath = Path.Combine(targetDirectory, relativePath);
				if (File.Exists(diskPath))
				{
					size = new FileInfo(diskPath).Length;
				}
			}

			if (!workspaceHashes.TryGetValue(normalizedHash, out var entry))
			{
				entry = (new List<string>(), size);
				workspaceHashes[normalizedHash] = entry;
			}
			entry.RelativePaths.Add(relativePath);
		}

		if (workspaceHashes.Count == 0)
		{
			return new List<GreenlitReferenceSuggestion>();
		}

		var candidateMaps = DiscoverAllCandidateManifests(currentMapName);
		var uncoveredHashes = new HashSet<string>(workspaceHashes.Keys, StringComparer.OrdinalIgnoreCase);
		var suggestions = new List<GreenlitReferenceSuggestion>();

		while (uncoveredHashes.Count > 0)
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

				long matchedBytes = 0;
				foreach (var hash in matched)
				{
					if (workspaceHashes.TryGetValue(hash, out var hashEntry))
					{
						matchedBytes += hashEntry.Size;
					}
				}

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

		var candidateMaps = DiscoverAllCandidateManifests(manifest.MapName ?? string.Empty);
		var candidateMapDictionary = candidateMaps.ToDictionary(c => c.MapTitle, c => c, StringComparer.OrdinalIgnoreCase);

		var referencedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var referenceTitle in manifest.GreenlitReferences)
		{
			if (candidateMapDictionary.TryGetValue(referenceTitle, out var candidate))
			{
				foreach (var hash in candidate.AssetHashes)
				{
					referencedHashes.Add(hash);
				}
			}
		}

		if (referencedHashes.Count == 0)
		{
			return excluded;
		}

		foreach (var pair in manifest.Files)
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

		return excluded;
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
					if (!string.IsNullOrEmpty(versionInfo.ManifestFilePath) && File.Exists(versionInfo.ManifestFilePath))
					{
						try
						{
							var manifest = MapManifest.LoadFromJson(File.ReadAllText(versionInfo.ManifestFilePath));
							if (manifest?.Files != null && manifest.Files.Count > 0)
							{
								var hashes = manifest.Files.Values
									.Select(ContentAddressableStorage.NormalizeBlake3Hash)
									.Where(h => !string.IsNullOrEmpty(h))
									.ToHashSet(StringComparer.OrdinalIgnoreCase);

								if (!results.ContainsKey(map.Title) || hashes.Count > results[map.Title].AssetHashes.Count)
								{
									results[map.Title] = new CandidateMapInfo
									{
										MapTitle = map.Title,
										MapVersion = versionInfo.Version,
										AssetHashes = hashes
									};
								}
							}
						}
						catch
						{
						}
					}
				}
			}
		}
		catch
		{
		}

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
					try
					{
						var manifest = MapManifest.LoadFromJson(File.ReadAllText(package.ManifestPath));
						if (manifest?.Files != null && manifest.Files.Count > 0)
						{
							var hashes = manifest.Files.Values
								.Select(ContentAddressableStorage.NormalizeBlake3Hash)
								.Where(h => !string.IsNullOrEmpty(h))
								.ToHashSet(StringComparer.OrdinalIgnoreCase);

							if (!results.ContainsKey(package.MapName) || hashes.Count > results[package.MapName].AssetHashes.Count)
							{
								results[package.MapName] = new CandidateMapInfo
								{
									MapTitle = package.MapName,
									MapVersion = package.MapVersion,
									AssetHashes = hashes
								};
							}
						}
					}
					catch
					{
					}
				}
				else if (package.AssetHashes != null && package.AssetHashes.Count > 0)
				{
					var hashes = package.AssetHashes
						.Select(ContentAddressableStorage.NormalizeBlake3Hash)
						.Where(h => !string.IsNullOrEmpty(h))
						.ToHashSet(StringComparer.OrdinalIgnoreCase);

					if (!results.ContainsKey(package.MapName) || hashes.Count > results[package.MapName].AssetHashes.Count)
					{
						results[package.MapName] = new CandidateMapInfo
						{
							MapTitle = package.MapName,
							MapVersion = package.MapVersion,
							AssetHashes = hashes
						};
					}
				}
			}
		}
		catch
		{
		}

		try
		{
			string resourceMapsDirectory = ProjectSettings.GlobalizePath("res://Maps");
			if (Directory.Exists(resourceMapsDirectory))
			{
				var manifestFiles = Directory.GetFiles(resourceMapsDirectory, "manifest.json", SearchOption.AllDirectories);
				foreach (var manifestFile in manifestFiles)
				{
					try
					{
						var manifest = MapManifest.LoadFromJson(File.ReadAllText(manifestFile));
						if (manifest != null && !string.IsNullOrEmpty(manifest.MapName) && !string.Equals(manifest.MapName, currentMapName, StringComparison.OrdinalIgnoreCase))
						{
							var hashes = manifest.Files.Values
								.Select(ContentAddressableStorage.NormalizeBlake3Hash)
								.Where(h => !string.IsNullOrEmpty(h))
								.ToHashSet(StringComparer.OrdinalIgnoreCase);

							if (!results.ContainsKey(manifest.MapName) || hashes.Count > results[manifest.MapName].AssetHashes.Count)
							{
								results[manifest.MapName] = new CandidateMapInfo
								{
									MapTitle = manifest.MapName,
									MapVersion = manifest.Version ?? "1.0.0",
									AssetHashes = hashes
								};
							}
						}
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}

		return results.Values.ToList();
	}
}
