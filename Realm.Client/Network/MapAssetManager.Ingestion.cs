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
	public static void EnsureUgcLicense()
	{
		if (!PathUtils.IsGodotInitialized)
		{
			return;
		}

		try
		{
			string mapsDir = GlobalArchiveDirectory;
			if (!Directory.Exists(mapsDir))
			{
				Directory.CreateDirectory(mapsDir);
			}

			string targetPath = Path.Combine(mapsDir, "RealmPlatform_UGC_License.txt");

			string[] candidateSourcePaths = new[]
			{
				PathUtils.FindPath("Terms/RealmPlatform_UGC_License.txt"),
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Terms", "RealmPlatform_UGC_License.txt"),
				Path.Combine(OS.GetExecutablePath().GetBaseDir(), "Terms", "RealmPlatform_UGC_License.txt"),
				Path.GetFullPath(Path.Combine(PathUtils.GetProjectRoot(), "..", "Terms", "RealmPlatform_UGC_License.txt")),
				Path.Combine(PathUtils.GetProjectRoot(), "Terms", "RealmPlatform_UGC_License.txt")
			};

			foreach (var candidate in candidateSourcePaths)
			{
				if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
				{
					File.Copy(candidate, targetPath, overwrite: true);
					break;
				}
			}
		}
		catch (Exception ex)
		{
			LogErr($"[MapAssetManager] Failed to copy UGC license: {ex.Message}");
		}
	}

	public static MapManifest IngestHostMap(string mapPath)
	{
		var manifest = new MapManifest();
		manifest.MapName = Path.GetFileNameWithoutExtension(mapPath);
		manifest.Version = "1.0.0";

		var newFiles = new Dictionary<string, byte[]>();
		string mapDir = ResolveMapDirectory(mapPath);

		if (Directory.Exists(mapDir))
		{
			var existing = TryLoadExistingManifest(mapDir);
			if (existing != null) return existing;

			ProcessMapDirectoryFiles(mapDir, manifest, newFiles);
		}
		else if (File.Exists(mapPath))
		{
			ProcessSingleMapFile(mapPath, manifest, newFiles);
		}
		else
		{
			ProcessEmptyMapFallback(manifest, newFiles);
		}

		AddOrUpdateGlobalArchive(newFiles);
		SaveHostManifest(manifest);

		return manifest;
	}

	private static string ResolveMapDirectory(string mapPath)
	{
		if (Directory.Exists(mapPath)) return mapPath;

		if (IsJsonFile(mapPath))
			return GetDirectoryOrDefault(mapPath);

		string? foundManifest = FindManifestPath(mapPath);
		if (foundManifest != null)
			return GetDirectoryOrDefault(foundManifest);

		string? globalUser = TryGetGlobalizedDirectory($"user://maps/{mapPath}");
		if (globalUser != null) return globalUser;

		string? globalRes = TryGetGlobalizedDirectory($"res://Maps/{mapPath}");
		if (globalRes != null) return globalRes;

		string combined = Path.Combine(GlobalArchiveDirectory, mapPath);
		if (Directory.Exists(combined)) return combined;

		return "Realm.MapScript";
	}

	private static bool IsJsonFile(string path)
	{
		return File.Exists(path) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
	}

	private static string GetDirectoryOrDefault(string path)
	{
		return Path.GetDirectoryName(path) ?? "Realm.MapScript";
	}

	private static string? TryGetGlobalizedDirectory(string godotPath)
	{
		try
		{
			string globalPath = ProjectSettings.GlobalizePath(godotPath);
			if (Directory.Exists(globalPath)) return globalPath;
		}
		catch
		{
		}

		return null;
	}

	private static MapManifest? TryLoadExistingManifest(string mapDir)
	{
		string manifestJsonPath = Path.Combine(mapDir, "manifest.json");
		if (!File.Exists(manifestJsonPath)) return null;

		try
		{
			var existing = MapManifest.LoadFromFile(manifestJsonPath);
			if (existing != null && existing.Files != null && existing.Files.Count > 0)
				return existing;
		}
		catch
		{
		}

		return null;
	}

	private static void ProcessMapDirectoryFiles(string mapDir, MapManifest manifest, Dictionary<string, byte[]> newFiles)
	{
		var files = Directory.GetFiles(mapDir, "*.*", SearchOption.AllDirectories);
		foreach (var file in files)
		{
			string relativePath = Path.GetRelativePath(mapDir, file).Replace("\\", "/");
			if (ShouldIgnoreFileForIngestion(relativePath)) continue;

			byte[] bytes = File.ReadAllBytes(file);
			string ext = Path.GetExtension(file).ToLowerInvariant();
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ext);
			string assetKey = string.IsNullOrEmpty(ext) ? blake3 : $"{blake3}{ext}";

			newFiles[assetKey] = bytes;
			manifest.Files[relativePath] = assetKey;
		}

		manifest.Assets = MapManifest.UnflattenFilesToAssets(manifest.Files);
	}

	private static bool ShouldIgnoreFileForIngestion(string relativePath)
	{
		return relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
		       relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
		       relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
		       relativePath.StartsWith(".vscode/", StringComparison.OrdinalIgnoreCase) ||
		       relativePath.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase) ||
		       relativePath.StartsWith(".sidecarcache/", StringComparison.OrdinalIgnoreCase) ||
		       relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
		       relativePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase);
	}

	private static void ProcessSingleMapFile(string mapPath, MapManifest manifest, Dictionary<string, byte[]> newFiles)
	{
		byte[] bytes = File.ReadAllBytes(mapPath);
		string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".json");
		string assetKey = $"{blake3}.json";
		newFiles[assetKey] = bytes;
		manifest.Files[Path.GetFileName(mapPath)] = assetKey;
	}

	private static void ProcessEmptyMapFallback(MapManifest manifest, Dictionary<string, byte[]> newFiles)
	{
		byte[] bytes = Encoding.UTF8.GetBytes("{\"Units\": []}");
		string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".json");
		string assetKey = $"{blake3}.json";
		newFiles[assetKey] = bytes;
		manifest.Files["metadata.json"] = assetKey;
	}

	private static void SaveHostManifest(MapManifest manifest)
	{
		try
		{
			string version = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";
			string manifestBlake3 = ComputeManifestBlake3(manifest);
			string manifestDir = GetMapDirectory(manifest.MapName, version, manifestBlake3, false);
			Directory.CreateDirectory(manifestDir);

			string manifestPath = Path.Combine(manifestDir, "manifest.json");
			File.WriteAllText(manifestPath, manifest.ToJson());
			ExtractManifestFiles(manifest, manifestDir, isP2P: false);

			Log($"[MapAssetManager] Saved host manifest and hardlinks to: {manifestPath}");
			GameContext.AssetIndexService.RegisterManifest(manifest, manifestPath, isP2P: false);
		}
		catch (Exception ex)
		{
			LogErr($"[MapAssetManager] Failed to write host manifest: {ex.Message}");
		}
	}

}
