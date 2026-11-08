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
	static MapAssetManager()
	{
		EnsureUgcLicense();
	}

	private static readonly object ArchiveLock = new object();

	public static void Log(string message)
	{
		GameLog.LogToConsole(message);
	}

	public static void LogErr(string message)
	{
		GameLog.LogError(message);
	}

	private static readonly AsyncLocal<string?> AsyncLocalArchiveDirectory = new();

	public static string? ThreadLocalArchiveDirectory
	{
		get => AsyncLocalArchiveDirectory.Value;
		set => AsyncLocalArchiveDirectory.Value = value;
	}

	private static string? _configuredStoragePath;

	public static string? ConfiguredStoragePath
	{
		get
		{
			if (_configuredStoragePath != null)
			{
				return _configuredStoragePath;
			}

			try
			{
				string[] args = Environment.GetCommandLineArgs();
				for (int i = 0; i < args.Length; i++)
				{
					if (args[i].StartsWith("--storage-path=", StringComparison.OrdinalIgnoreCase))
					{
						_configuredStoragePath = args[i].Substring("--storage-path=".Length).Trim('"');
						return _configuredStoragePath;
					}

					if (string.Equals(args[i], "--storage-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
					{
						_configuredStoragePath = args[i + 1].Trim('"');
						return _configuredStoragePath;
					}
				}
			}
			catch
			{
			}

			return null;
		}
		set
		{
			_configuredStoragePath = value;
			_storage = null;
		}
	}

	private static ContentAddressableStorage? _storage;

	public static ContentAddressableStorage Storage
	{
		get { return _storage ??= new ContentAddressableStorage(GlobalArchiveDirectory); }
	}

	private static ContentAddressableStorage? _p2pStorage;

	public static ContentAddressableStorage P2PStorage
	{
		get { return _p2pStorage ??= new ContentAddressableStorage(P2PArchiveDirectory); }
	}

	public static string P2PArchiveDirectory
	{
		get
		{
			string path = PathUtils.SafeGlobalizeUserPath("p2p_cache");
			if (!Directory.Exists(path))
			{
				Directory.CreateDirectory(path);
			}

			return path;
		}
	}

	public static void ClearP2PCache()
	{
		lock (ArchiveLock)
		{
			try
			{
				GameContext.AssetIndexService.ClearP2PIndex();
				if (Directory.Exists(P2PArchiveDirectory))
				{
					Directory.Delete(P2PArchiveDirectory, true);
					Directory.CreateDirectory(P2PArchiveDirectory);
				}

				_p2pStorage = null;
				Log("[MapAssetManager] P2P cache cleared successfully.");
			}
			catch (Exception ex)
			{
				LogErr($"[MapAssetManager] Error clearing P2P cache: {ex.Message}");
			}
		}
	}

	public static string GlobalArchiveDirectory
	{
		get
		{
			if (ThreadLocalArchiveDirectory != null)
			{
				if (!Directory.Exists(ThreadLocalArchiveDirectory))
				{
					Directory.CreateDirectory(ThreadLocalArchiveDirectory);
				}

				return ThreadLocalArchiveDirectory;
			}

			string? configured = ConfiguredStoragePath;
			if (!string.IsNullOrEmpty(configured))
			{
				if (!Directory.Exists(configured))
				{
					Directory.CreateDirectory(configured);
				}

				return configured;
			}

			return PathUtils.SafeGlobalizeUserPath("maps");
		}
	}

	public static string ComputeBlake3(byte[] bytes, string? extensionOrPath = null)
	{
		return RealmMetadataHelper.ComputeBlake3(bytes, extensionOrPath);
	}

	public static string ComputeBlake3(Stream stream, string? extensionOrPath = null)
	{
		return RealmMetadataHelper.ComputeBlake3(stream, extensionOrPath);
	}

	public static string ComputeManifestBlake3(MapManifest manifest)
	{
		if (manifest == null) return string.Empty;
		return manifest.ComputeManifestBlake3();
	}

	public static List<string> GetMissingHashes(IEnumerable<string> hashes)
	{
		var missing = new List<string>();
		if (hashes == null) return missing;

		foreach (var hash in hashes)
		{
			string norm = ContentAddressableStorage.NormalizeBlake3Hash(hash);
			if (!Storage.HasAsset(norm) && !P2PStorage.HasAsset(norm))
			{
				missing.Add(hash);
			}
		}

		return missing;
	}

	public static void AddOrUpdateGlobalArchive(Dictionary<string, byte[]> newFilesByHash)
	{
		if (newFilesByHash == null || newFilesByHash.Count == 0) return;

		foreach (var kvp in newFilesByHash)
		{
			string ext = Path.GetExtension(kvp.Key);
			Storage.StoreAsset(kvp.Value, ext);
		}
	}

	public static void AddOrUpdateP2PArchive(Dictionary<string, byte[]> newFilesByHash)
	{
		if (newFilesByHash == null || newFilesByHash.Count == 0) return;

		foreach (var kvp in newFilesByHash)
		{
			string ext = Path.GetExtension(kvp.Key);
			P2PStorage.StoreAsset(kvp.Value, ext);
		}
	}

	public static string GetMapDirectory(string mapName, string version = "1.0.0", string? manifestHash = null, bool isP2P = false)
	{
		string root = isP2P ? P2PArchiveDirectory : GlobalArchiveDirectory;
		if (!string.IsNullOrWhiteSpace(manifestHash))
		{
			string normHash = ContentAddressableStorage.NormalizeBlake3Hash(manifestHash);
			return Path.Combine(root, mapName, version, normHash);
		}

		return Path.Combine(root, mapName, version);
	}

	public static string GetManifestPath(string mapName, string version = "1.0.0", string? manifestHash = null, bool isP2P = false)
	{
		return Path.Combine(GetMapDirectory(mapName, version, manifestHash, isP2P), "manifest.json");
	}

	private static readonly ConcurrentDictionary<string, bool> _ensuredDirectories = new(StringComparer.OrdinalIgnoreCase);

	public static bool ExtractSingleAsset(string virtualPath, string hash, string targetDirectory, bool isP2P = false)
	{
		if (string.IsNullOrWhiteSpace(virtualPath) || string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(targetDirectory))
			return false;

		string relativePath = GetNormalizedVirtualPath(virtualPath);
		string norm = ContentAddressableStorage.NormalizeBlake3Hash(hash);
		string destinationFilePath = Path.Combine(targetDirectory, relativePath);

		if (File.Exists(destinationFilePath))
			return true;

		EnsureDestinationDir(destinationFilePath);

		if (TryExtractFromCas(norm, destinationFilePath, isP2P))
			return true;

		LogErr($"[MapAssetManager] Could not extract {relativePath}: hash {hash} not found in CAS storage.");
		return false;
	}

	private static string GetNormalizedVirtualPath(string virtualPath)
	{
		string relativePath = virtualPath;
		if (relativePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
			relativePath = relativePath.Substring(6);
		return relativePath.TrimStart('/', '\\');
	}

	private static void EnsureDestinationDir(string destinationFilePath)
	{
		string? destinationDir = Path.GetDirectoryName(destinationFilePath);
		if (string.IsNullOrEmpty(destinationDir) || !_ensuredDirectories.TryAdd(destinationDir, true))
			return;

		if (!Directory.Exists(destinationDir))
			Directory.CreateDirectory(destinationDir);
	}

	private static bool TryExtractFromCas(string norm, string destinationFilePath, bool isP2P)
	{
		string? casFilePath = (isP2P ? P2PStorage.FindAssetFilePath(norm) : Storage.FindAssetFilePath(norm))
		                      ?? Storage.FindAssetFilePath(norm)
		                      ?? P2PStorage.FindAssetFilePath(norm);

		if (casFilePath != null && File.Exists(casFilePath))
			return HardLinkHelper.CreateHardLinkOrCopy(destinationFilePath, casFilePath);

		byte[]? casBytes = (isP2P ? P2PStorage.GetAssetBytes(norm) : Storage.GetAssetBytes(norm))
		                   ?? Storage.GetAssetBytes(norm)
		                   ?? P2PStorage.GetAssetBytes(norm);

		if (casBytes != null)
		{
			File.WriteAllBytes(destinationFilePath, casBytes);
			return true;
		}

		return false;
	}

	public static void ExtractManifestFiles(MapManifest manifest, string targetDirectory, bool isP2P = false)
	{
		if (manifest == null || manifest.Files == null || string.IsNullOrWhiteSpace(targetDirectory))
		{
			return;
		}

		if (!Directory.Exists(targetDirectory))
		{
			Directory.CreateDirectory(targetDirectory);
		}

		try
		{
			Parallel.ForEach(manifest.Files, kvp => { ExtractSingleAsset(kvp.Key, kvp.Value, targetDirectory, isP2P); });

			string manifestPath = Path.Combine(targetDirectory, "manifest.json");
			if (!File.Exists(manifestPath))
			{
				File.WriteAllText(manifestPath, manifest.ToJson());
			}
		}
		catch (Exception ex)
		{
			LogErr($"[MapAssetManager] Error extracting manifest files to {targetDirectory}: {ex.Message}");
		}
	}

}