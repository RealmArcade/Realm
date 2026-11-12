using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Network;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     One file a map and the maps it references carry, and the maps that carry it, which is what the conflict resolution
///     dialog lists so an author can state which copy the saved map keeps.
/// </summary>
public sealed class MapReferenceAssetEntry
{
	public string RelativePath { get; init; } = string.Empty;

	public IReadOnlyList<string> CarryingMapNames { get; init; } = Array.Empty<string>();

	public bool IsConflicting => CarryingMapNames.Count > 1;
}

/// <summary>
///     Finds where a referenced map currently lives, which is either the helper folder an author is editing or the copy
///     the content-addressable store holds.
/// </summary>
public sealed class MapReferenceLocation
{
	public string MapName { get; init; } = string.Empty;

	public string Version { get; init; } = string.Empty;

	public string FolderPath { get; init; } = string.Empty;

	public bool IsFromHelperFolder { get; init; }

	public bool IsFound => !string.IsNullOrEmpty(FolderPath);
}

public sealed class MapReferenceService
{
	public MapReferences Load(string mapFolder)
	{
		if (string.IsNullOrWhiteSpace(mapFolder))
		{
			return null;
		}

		return MapReferences.Load(mapFolder);
	}

	public void Save(string mapFolder, MapReferences references)
	{
		if (string.IsNullOrWhiteSpace(mapFolder))
		{
			return;
		}

		if (references == null || references.IsEmpty)
		{
			Delete(mapFolder);
			return;
		}

		references.Save(mapFolder);
	}

	public void Delete(string mapFolder)
	{
		if (string.IsNullOrWhiteSpace(mapFolder))
		{
			return;
		}

		string path = Path.Combine(mapFolder, MapReferences.FileName);
		if (!File.Exists(path))
		{
			return;
		}

		MapReferenceResolver.ClearReadOnly(path);
		File.Delete(path);
	}

	public bool HasReferences(string mapFolder)
	{
		MapReferences references = Load(mapFolder);
		return references != null && !references.IsEmpty;
	}

	/// <summary>
	///     Applies the maps this folder references to the folder itself. Every save runs this, so testing, exporting,
	///     publishing and playing all read one ordinary map.
	/// </summary>
	/// <param name="mapFolder">The map folder being saved.</param>
	/// <returns>What the save took from the referenced maps, or why it could not be applied.</returns>
	public MapReferenceResolutionResult ApplyOnSave(string mapFolder)
	{
		MapReferenceResolutionResult result = MapReferenceResolver.Apply(mapFolder, ResolveStoreFolder);
		if (!result.Success)
		{
			GD.PrintErr($"[MapReferenceService] {result.Message}");
		}
		else if (result.HasReferences)
		{
			GD.Print($"[MapReferenceService] {result.Message}");
			foreach (string replacedFile in result.ReplacedFiles)
			{
				GD.PrintErr($"[MapReferenceService] Replaced locally edited '{replacedFile}' with the referenced copy.");
			}
		}

		return result;
	}

	public List<MapReferenceLocation> DescribeLocations(MapReferences references)
	{
		var locations = new List<MapReferenceLocation>();
		if (references == null)
		{
			return locations;
		}

		foreach (MapReference reference in references.References)
		{
			string helperFolder = ResolveHelperFolder(reference);
			if (!string.IsNullOrEmpty(helperFolder))
			{
				locations.Add(new MapReferenceLocation
				{
					MapName = reference.MapName,
					Version = reference.Version,
					FolderPath = helperFolder,
					IsFromHelperFolder = true
				});
				continue;
			}

			string storeFolder = ResolveStoreFolder(reference) ?? string.Empty;
			locations.Add(new MapReferenceLocation
			{
				MapName = reference.MapName,
				Version = reference.Version,
				FolderPath = storeFolder,
				IsFromHelperFolder = false
			});
		}

		return locations;
	}

	/// <summary>
	///     Lists every file the map and the maps it references carry, with the maps carrying each one.
	/// </summary>
	/// <param name="mapFolder">The map folder being edited.</param>
	/// <param name="references">The references it keeps.</param>
	/// <returns>One entry per path, sorted, naming the maps that carry it.</returns>
	public List<MapReferenceAssetEntry> DescribeAssets(string mapFolder, MapReferences references)
	{
		var carryingMaps = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrWhiteSpace(mapFolder) && Directory.Exists(mapFolder))
		{
			foreach (string relativePath in MapReferenceResolver.EnumeratePackageFiles(mapFolder))
			{
				AddCarrier(carryingMaps, relativePath, string.Empty);
			}
		}

		if (references != null)
		{
			foreach (MapReferenceLocation location in DescribeLocations(references))
			{
				if (!location.IsFound)
				{
					continue;
				}

				foreach (string relativePath in MapReferenceResolver.EnumeratePackageFiles(location.FolderPath))
				{
					AddCarrier(carryingMaps, relativePath, location.MapName);
				}
			}
		}

		var entries = new List<MapReferenceAssetEntry>(carryingMaps.Count);
		foreach (KeyValuePair<string, List<string>> carryingMap in carryingMaps)
		{
			entries.Add(new MapReferenceAssetEntry
			{
				RelativePath = carryingMap.Key,
				CarryingMapNames = carryingMap.Value
			});
		}

		entries.Sort((left, right) => string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase));
		return entries;
	}

	private static void AddCarrier(Dictionary<string, List<string>> carryingMaps, string relativePath, string mapName)
	{
		if (!carryingMaps.TryGetValue(relativePath, out List<string> carriers))
		{
			carriers = new List<string>(1);
			carryingMaps[relativePath] = carriers;
		}

		if (!carriers.Contains(mapName, StringComparer.OrdinalIgnoreCase))
		{
			carriers.Add(mapName);
		}
	}

	public string ResolveHelperFolder(MapReference reference)
	{
		if (reference == null || string.IsNullOrWhiteSpace(reference.FolderPath))
		{
			return string.Empty;
		}

		string folder = reference.FolderPath.Replace('\\', Path.DirectorySeparatorChar);
		if (!Directory.Exists(folder))
		{
			return string.Empty;
		}

		bool holdsMap = File.Exists(Path.Combine(folder, "metadata.json")) || File.Exists(Path.Combine(folder, "manifest.json"));
		return holdsMap ? folder : string.Empty;
	}

	public string ResolveStoreFolder(MapReference reference)
	{
		if (reference == null)
		{
			return null;
		}

		string manifestPath = MapAssetManager.FindManifestPath(reference.MapName, reference.Version, reference.ManifestBlake3);
		if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
		{
			return null;
		}

		return Path.GetDirectoryName(manifestPath);
	}
}
