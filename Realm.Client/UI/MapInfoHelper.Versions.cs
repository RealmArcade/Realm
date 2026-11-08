// Version discovery for one map: walk its folder tree (hash children, variation folders, root JSON) to
// collect the version labels and manifest hashes, dedupe them, and expose the sorted list the storage
// and browser screens show.

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using FileAccess = Godot.FileAccess;


namespace Realm.Client.UI;

public static partial class MapInfoHelper
{
	private static void AddVariation(string? name, HashSet<string> variations)
	{
		if (string.IsNullOrWhiteSpace(name)) return;
		string trimmed = name.Trim();
		variations.Add(trimmed);
		variations.Add(trimmed.Replace(' ', '_'));
		variations.Add(trimmed.Replace('_', ' '));
	}

	private static void TryAddVersion(string? ver, string? manifestHash, List<string> versions, HashSet<string> seen)
	{
		if (string.IsNullOrWhiteSpace(ver)) return;
		string label = FormatVersionDisplay(ver, manifestHash);
		if (seen.Add(label)) versions.Add(label);
	}

	private static void ProcessHashChildDir(string hashChildDir, string subDirName, List<string> versions, HashSet<string> seen)
	{
		string hashChildManifest = Path.Combine(hashChildDir, "manifest.json");
		if (!File.Exists(hashChildManifest)) return;

		string ver = ExtractVersionFromJson(hashChildManifest);
		string childHash = Path.GetFileName(hashChildDir);
		if (string.IsNullOrWhiteSpace(childHash) || childHash.Length < 4)
		{
			try
			{
				childHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(hashChildManifest), ".json");
			}
			catch
			{
			}
		}

		TryAddVersion(!string.IsNullOrWhiteSpace(ver) ? ver : subDirName, childHash, versions, seen);
	}

	private static void ProcessSubDir(string subDirPath, string subDirName, List<string> versions, HashSet<string> seen)
	{
		string manifestPath = Path.Combine(subDirPath, "manifest.json");
		string metadataPath = Path.Combine(subDirPath, "metadata.json");

		string? targetJson = File.Exists(manifestPath) ? manifestPath : (File.Exists(metadataPath) ? metadataPath : null);

		if (targetJson != null)
		{
			string ver = ExtractVersionFromJson(targetJson);
			string manifestHash = "";
			if (targetJson == manifestPath)
			{
				try
				{
					manifestHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(manifestPath), ".json");
				}
				catch
				{
				}
			}

			if (!string.IsNullOrWhiteSpace(ver)) TryAddVersion(ver, manifestHash, versions, seen);
			else if (char.IsDigit(subDirName[0]) || subDirName.StartsWith("v", StringComparison.OrdinalIgnoreCase)) TryAddVersion(subDirName, manifestHash, versions, seen);
		}

		foreach (var hashChildDir in Directory.GetDirectories(subDirPath))
		{
			ProcessHashChildDir(hashChildDir, subDirName, versions, seen);
		}
	}

	private static void ScanFolderForVersions(string dirPath, List<string> versions, HashSet<string> seen)
	{
		if (string.IsNullOrWhiteSpace(dirPath)) return;

		string globalPath = dirPath;
		try
		{
			globalPath = ProjectSettings.GlobalizePath(dirPath);
		}
		catch
		{
		}

		if (!Directory.Exists(globalPath)) return;

		foreach (var subDirPath in Directory.GetDirectories(globalPath))
		{
			string subDirName = Path.GetFileName(subDirPath);
			if (!subDirName.StartsWith(".")) ProcessSubDir(subDirPath, subDirName, versions, seen);
		}

		ProcessRootJson(globalPath, versions, seen);
	}

	private static void ProcessRootJson(string globalPath, List<string> versions, HashSet<string> seen)
	{
		string rootManifest = Path.Combine(globalPath, "manifest.json");
		string rootMetadata = Path.Combine(globalPath, "metadata.json");
		string? rootJson = GetTargetJsonFile(rootManifest, rootMetadata);

		if (rootJson == null) return;

		string ver = ExtractVersionFromJson(rootJson);
		if (string.IsNullOrWhiteSpace(ver)) return;

		string manifestHash = "";
		if (rootJson == rootManifest)
		{
			try
			{
				manifestHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(rootManifest), ".json");
			}
			catch
			{
			}
		}

		TryAddVersion(ver, manifestHash, versions, seen);
	}

	private static string? GetTargetJsonFile(string manifestPath, string metadataPath)
	{
		if (File.Exists(manifestPath)) return manifestPath;
		if (File.Exists(metadataPath)) return metadataPath;
		return null;
	}

	private static void ProcessVariationFolder(string folder, string folderName, string variation, List<string> versions, HashSet<string> seen)
	{
		if (folderName.Equals(variation, StringComparison.OrdinalIgnoreCase))
		{
			ScanFolderForVersions(folder, versions, seen);
			return;
		}

		if (folderName.StartsWith(variation + "_", StringComparison.OrdinalIgnoreCase))
		{
			string suffix = folderName.Substring(variation.Length + 1);
			string manifestPath = Path.Combine(folder, "manifest.json");
			if (File.Exists(manifestPath))
			{
				string ver = ExtractVersionFromJson(manifestPath);
				string manifestHash = "";
				try
				{
					manifestHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(manifestPath), ".json");
				}
				catch
				{
				}

				TryAddVersion(!string.IsNullOrWhiteSpace(ver) ? ver : suffix, manifestHash, versions, seen);
			}
			else
			{
				TryAddVersion(suffix, null, versions, seen);
			}
		}
	}

	private static void ProcessJsonFile(string file, string variation, List<string> versions, HashSet<string> seen)
	{
		string fileName = Path.GetFileName(file);
		if (fileName.StartsWith(variation + "_", StringComparison.OrdinalIgnoreCase))
		{
			string ver = ExtractVersionFromJson(file);
			if (!string.IsNullOrWhiteSpace(ver)) TryAddVersion(ver, null, versions, seen);
		}
	}

	private static void SearchRoot(string root, HashSet<string> variations, List<string> versions, HashSet<string> seen)
	{
		string globalRoot = root;
		try
		{
			globalRoot = ProjectSettings.GlobalizePath(root);
		}
		catch
		{
		}

		if (!Directory.Exists(globalRoot)) return;

		foreach (var variation in variations)
		{
			ScanFolderForVersions(Path.Combine(globalRoot, variation), versions, seen);
		}

		foreach (var folder in Directory.GetDirectories(globalRoot))
		{
			string folderName = Path.GetFileName(folder);
			foreach (var variation in variations)
			{
				ProcessVariationFolder(folder, folderName, variation, versions, seen);
			}
		}

		foreach (var file in Directory.GetFiles(globalRoot, "*.json"))
		{
			foreach (var variation in variations)
			{
				ProcessJsonFile(file, variation, versions, seen);
			}
		}
	}

	private static void SearchRoots(HashSet<string> variations, List<string> versions, HashSet<string> seen)
	{
		string[] searchRoots = new[] { "res://Maps", "user://maps", "user://p2p_cache" };
		foreach (var root in searchRoots)
		{
			SearchRoot(root, variations, versions, seen);
		}
	}

	public static List<string> GetDownloadedVersionsForMap(string mapPathName, string? mapDisplayName = null)
	{
		var versions = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var variations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddVariation(mapPathName, variations);
		AddVariation(mapDisplayName, variations);

		SearchRoots(variations, versions, seen);

		if (versions.Count == 0) versions.Add(FormatVersionDisplay("1.0.0", null));

		versions.Sort((a, b) => ParseVersion(b).CompareTo(ParseVersion(a)));
		return versions;
	}

}