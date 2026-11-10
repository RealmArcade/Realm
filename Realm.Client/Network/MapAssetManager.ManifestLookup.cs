using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Distribution;
using Environment = System.Environment;

namespace Realm.Client.Network;

public static partial class MapAssetManager
{
	private static List<string> GetMapNameVariations(string rawName)
	{
		var variations = new List<string>();
		if (string.IsNullOrWhiteSpace(rawName)) return variations;

		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddMapVariation(rawName, seen, variations);

		string stripped = rawName.Trim();
		stripped = StripPrefixes(stripped);
		stripped = StripSuffixes(stripped);

		int dashIdx = stripped.LastIndexOf('-');
		if (dashIdx > 0)
		{
			string candidateTitle = stripped.Substring(0, dashIdx).Trim();
			if (!string.IsNullOrEmpty(candidateTitle))
				AddMapVariation(candidateTitle, seen, variations);
		}

		AddMapVariation(stripped, seen, variations);

		return variations;
	}

	private static void AddMapVariation(string name, HashSet<string> seen, List<string> variations)
	{
		name = name.Trim();
		if (string.IsNullOrEmpty(name)) return;

		if (seen.Add(name))
			variations.Add(name);

		string withUnderscores = name.Replace(' ', '_');
		if (seen.Add(withUnderscores))
			variations.Add(withUnderscores);

		string withSpaces = name.Replace('_', ' ');
		if (seen.Add(withSpaces))
			variations.Add(withSpaces);
	}

	private static string StripPrefixes(string name)
	{
		if (name.StartsWith("[Beta-Testing]", StringComparison.OrdinalIgnoreCase))
			name = name.Substring("[Beta-Testing]".Length).Trim();

		if (name.StartsWith("Realm_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Realm ", StringComparison.OrdinalIgnoreCase))
			name = name.Substring(6).Trim();

		return name;
	}

	private static string StripSuffixes(string name)
	{
		if (name.EndsWith("_Demo", StringComparison.OrdinalIgnoreCase))
			name = name.Substring(0, name.Length - 5).Trim();

		if (name.EndsWith(" Demo", StringComparison.OrdinalIgnoreCase))
			name = name.Substring(0, name.Length - 5).Trim();

		return name;
	}

	public static string? FindManifestPath(string mapName, string? version = null, string? manifestHash = null)
	{
		if (string.IsNullOrWhiteSpace(mapName))
			return null;

		ParseMapNameAndVersion(mapName, version, out string targetMapName, out string? targetVersion);

		var variations = GetMapNameVariations(targetMapName);

		if (!string.IsNullOrEmpty(targetVersion))
		{
			if (!string.IsNullOrEmpty(manifestHash))
			{
				string? hashPath = FindManifestWithVersionAndHash(variations, targetVersion, manifestHash);
				if (hashPath != null) return hashPath;
			}

			string? versionPath = FindManifestWithVersion(variations, targetVersion);
			if (versionPath != null) return versionPath;

			return SearchArchivesForManifestWithVersion(variations, targetVersion);
		}

		string? noVersionPath = FindManifestWithoutVersion(variations);
		if (noVersionPath != null) return noVersionPath;

		return SearchArchivesForManifestWithoutVersion(variations);
	}

	private static void ParseMapNameAndVersion(string mapName, string? version, out string targetMapName, out string? targetVersion)
	{
		targetMapName = mapName.Trim();
		targetVersion = !string.IsNullOrWhiteSpace(version) ? version.Trim() : null;

		if (!string.IsNullOrEmpty(targetVersion) || !targetMapName.Contains('_'))
			return;

		int lastUnderscore = targetMapName.LastIndexOf('_');
		string candidateName = targetMapName.Substring(0, lastUnderscore).Trim();
		string candidateVer = targetMapName.Substring(lastUnderscore + 1).Trim();
		if (!string.IsNullOrEmpty(candidateName) && !string.IsNullOrEmpty(candidateVer) && (candidateVer.Contains('.') || char.IsDigit(candidateVer[0])))
		{
			targetMapName = candidateName;
			targetVersion = candidateVer;
		}
	}

	private static string? FindManifestWithVersionAndHash(List<string> variations, string version, string manifestHash)
	{
		foreach (var variation in variations)
		{
			string directPath = GetManifestPath(variation, version, manifestHash, false);
			if (File.Exists(directPath)) return directPath;
			string p2pPath = GetManifestPath(variation, version, manifestHash, true);
			if (File.Exists(p2pPath)) return p2pPath;
		}

		return null;
	}

	private static string? FindManifestWithVersion(List<string> variations, string version)
	{
		foreach (var variation in variations)
		{
			string directPath = GetManifestPath(variation, version, null, false);
			if (File.Exists(directPath)) return directPath;
			string p2pPath = GetManifestPath(variation, version, null, true);
			if (File.Exists(p2pPath)) return p2pPath;

			string[] baseDirs = new[]
			{
				GetMapDirectory(variation, version, null, false),
				GetMapDirectory(variation, version, null, true)
			};

			foreach (var bDir in baseDirs)
			{
				if (!Directory.Exists(bDir)) continue;
				foreach (var sub in Directory.GetDirectories(bDir))
				{
					string subMf = Path.Combine(sub, "manifest.json");
					if (File.Exists(subMf)) return subMf;
				}
			}
		}

		return null;
	}

	private static string? SearchArchivesForManifestWithVersion(List<string> variations, string version)
	{
		string[] roots = new[] { GlobalArchiveDirectory, P2PArchiveDirectory };
		foreach (var root in roots)
		{
			if (!Directory.Exists(root)) continue;
			foreach (var dir in Directory.GetDirectories(root))
			{
				string dirName = Path.GetFileName(dir);
				if (dirName.StartsWith(".")) continue;

				string candidate = Path.Combine(dir, version, "manifest.json");
				if (CheckManifestMatchesVariation(candidate, version, variations, dirName))
					return candidate;

				string versionDir = Path.Combine(dir, version);
				if (!Directory.Exists(versionDir)) continue;

				foreach (var hashSub in Directory.GetDirectories(versionDir))
				{
					string hashCandidate = Path.Combine(hashSub, "manifest.json");
					if (CheckManifestMatchesVariation(hashCandidate, version, variations, dirName))
						return hashCandidate;
				}
			}
		}

		return null;
	}

	private static bool CheckManifestMatchesVariation(string path, string version, List<string> variations, string dirName)
	{
		if (!File.Exists(path)) return false;
		try
		{
			var mf = MapManifest.LoadFromFile(path);
			if (mf == null || !string.Equals(mf.Version, version, StringComparison.OrdinalIgnoreCase))
				return false;

			foreach (var variation in variations)
			{
				if (string.Equals(mf.MapName, variation, StringComparison.OrdinalIgnoreCase) ||
				    string.Equals(dirName, variation, StringComparison.OrdinalIgnoreCase))
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

	private static string? FindManifestWithoutVersion(List<string> variations)
	{
		foreach (var variation in variations)
		{
			string[] checkRoots = new[] { GlobalArchiveDirectory, P2PArchiveDirectory };
			foreach (var cRoot in checkRoots)
			{
				string mapDir = Path.Combine(cRoot, variation);
				if (!Directory.Exists(mapDir)) continue;

				string rootManifest = Path.Combine(mapDir, "manifest.json");
				if (File.Exists(rootManifest)) return rootManifest;

				var versionDirs = Directory.GetDirectories(mapDir);
				Array.Sort(versionDirs, StringComparer.OrdinalIgnoreCase);
				for (int i = versionDirs.Length - 1; i >= 0; i--)
				{
					string candidate = Path.Combine(versionDirs[i], "manifest.json");
					if (File.Exists(candidate)) return candidate;

					var hashDirs = Directory.GetDirectories(versionDirs[i]);
					Array.Sort(hashDirs, StringComparer.OrdinalIgnoreCase);
					for (int j = hashDirs.Length - 1; j >= 0; j--)
					{
						string hashCandidate = Path.Combine(hashDirs[j], "manifest.json");
						if (File.Exists(hashCandidate)) return hashCandidate;
					}
				}
			}
		}

		return null;
	}

	private static string? SearchArchivesForManifestWithoutVersion(List<string> variations)
	{
		string[] searchRoots = new[] { GlobalArchiveDirectory, P2PArchiveDirectory };
		foreach (var root in searchRoots)
		{
			if (!Directory.Exists(root)) continue;

			string? match = SearchArchiveRootForManifest(root, variations);
			if (match != null) return match;
		}

		return null;
	}

	private static string? SearchArchiveRootForManifest(string root, List<string> variations)
	{
		foreach (var dir in Directory.GetDirectories(root))
		{
			string dirName = Path.GetFileName(dir);
			if (dirName.StartsWith(".")) continue;

			var manifestCandidates = GetManifestCandidatesFromDirectory(dir);
			foreach (var mfPath in manifestCandidates)
			{
				if (CheckManifestMatchesVariations(mfPath, dirName, variations)) return mfPath;
			}
		}

		return null;
	}

	private static bool CheckManifestMatchesVariations(string mfPath, string dirName, List<string> variations)
	{
		try
		{
			var mf = MapManifest.LoadFromFile(mfPath);
			if (mf == null) return false;

			foreach (var variation in variations)
			{
				if (string.Equals(mf.MapName, variation, StringComparison.OrdinalIgnoreCase) ||
				    string.Equals(dirName, variation, StringComparison.OrdinalIgnoreCase))
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

	private static List<string> GetManifestCandidatesFromDirectory(string dir)
	{
		var candidates = new List<string>();
		string rootMf = Path.Combine(dir, "manifest.json");
		if (File.Exists(rootMf)) candidates.Add(rootMf);

		foreach (var sd in Directory.GetDirectories(dir))
		{
			string subMf = Path.Combine(sd, "manifest.json");
			if (File.Exists(subMf)) candidates.Add(subMf);

			foreach (var hsd in Directory.GetDirectories(sd))
			{
				string hsubMf = Path.Combine(hsd, "manifest.json");
				if (File.Exists(hsubMf)) candidates.Add(hsubMf);
			}
		}

		return candidates;
	}

}
