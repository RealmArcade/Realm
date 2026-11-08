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
	public static long GetMapTotalSizeBytes(string mapName, string? version = null)
	{
		if (string.IsNullOrWhiteSpace(mapName))
			return 0;

		try
		{
			string? manifestPath = FindManifestPath(mapName, version);
			var manifest = FindHostManifest(mapName, version);
			if (manifest != null)
		{
				string? mapDirectory = !string.IsNullOrEmpty(manifestPath) ? Path.GetDirectoryName(manifestPath) : null;
				long manifestSize = GetSizeFromManifest(manifest, mapDirectory);
				if (manifestSize > 0) return manifestSize;
			}

			long fallbackSize = GetSizeFromDirectoryFallbacks(mapName, version);
			if (fallbackSize > 0) return fallbackSize;
		}
		catch
		{
		}

		return 0;
	}

	private static long GetSizeFromManifest(MapManifest manifest, string? mapDirectory)
	{
		if (manifest.FileSizes != null && manifest.FileSizes.Count > 0)
		{
			long totalFromManifest = 0;
			foreach (var size in manifest.FileSizes.Values)
				totalFromManifest += size;

			if (totalFromManifest > 0)
				return totalFromManifest;
		}

		if (manifest.Files == null || manifest.Files.Count == 0)
			return 0;

		long total = 0;
		foreach (var pair in manifest.Files)
		{
			long fileSize = GetManifestFileSize(mapDirectory, pair.Key, pair.Value);
			if (fileSize > 0) total += fileSize;
		}

		return total;
	}

	/// <summary>
	///     The size of one file a manifest lists. A map the host holds on disk is served from that folder, so its files are
	///     measured where they are; only a file the folder no longer has is measured out of the content archive.
	/// </summary>
	private static long GetManifestFileSize(string? mapDirectory, string relativePath, string hash)
	{
		if (!string.IsNullOrEmpty(mapDirectory))
		{
			string path = relativePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? relativePath.Substring(6) : relativePath;
			path = path.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);

			string onDisk = Path.Combine(mapDirectory, path);
			if (File.Exists(onDisk)) return new FileInfo(onDisk).Length;
		}

		string? assetPath = Storage.FindAssetFilePath(hash);
		return assetPath != null && File.Exists(assetPath) ? new FileInfo(assetPath).Length : 0;
	}

	private static long GetSizeFromDirectoryFallbacks(string mapName, string? version)
	{
		string? manifestPath = FindManifestPath(mapName, version);
		if (manifestPath != null && File.Exists(manifestPath))
		{
			string? dir = Path.GetDirectoryName(manifestPath);
			if (dir != null && Directory.Exists(dir))
			{
				long dirSize = CalculateDirectorySize(dir);
				if (dirSize > 0) return dirSize;
			}
		}

		string userMapDir = ProjectSettings.GlobalizePath($"user://maps/{mapName}");
		if (Directory.Exists(userMapDir))
		{
			long dirSize = CalculateDirectorySize(userMapDir);
			if (dirSize > 0) return dirSize;
		}

		string resMapDir = ProjectSettings.GlobalizePath($"res://Maps/{mapName}");
		if (Directory.Exists(resMapDir))
		{
			long dirSize = CalculateDirectorySize(resMapDir);
			if (dirSize > 0) return dirSize;
		}

		return 0;
	}

	private static long CalculateDirectorySize(string directoryPath)
	{
		if (!Directory.Exists(directoryPath))
		{
			return 0;
		}

		long total = 0;
		try
		{
			var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
			foreach (var file in files)
			{
				total += new FileInfo(file).Length;
			}
		}
		catch
		{
		}

		return total;
	}

	public static string FormatSizeInMB(long bytes)
	{
		if (bytes <= 0)
		{
			return "0.0 MB";
		}

		double mb = bytes / (1024.0 * 1024.0);
		return $"{mb:F1} MB";
	}
}
