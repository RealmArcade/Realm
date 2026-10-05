using Godot;
using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Realm.Shared.Metadata;
using JsonSerializer = System.Text.Json.JsonSerializer;

public class IndexedAsset
{
	public int Id { get; set; }
	public string FilePath { get; set; } = string.Empty;
	public string FileName { get; set; } = string.Empty;
	public string Extension { get; set; } = string.Empty;
	public string DirectoryPath { get; set; } = string.Empty;
	public long FileSizeBytes { get; set; }
	public DateTime LastModifiedUtc { get; set; }
	public List<string> Tags { get; set; } = new();
	public string MetadataJson { get; set; } = string.Empty;
	public bool HasRealmMetadata { get; set; }
	public string? AssetType { get; set; }
	public string? MapName { get; set; }
	public string? MapVersion { get; set; }
	public string? Blake3 { get; set; }
	public bool HasPlayerColorMask { get; set; }
	public string? ChromaKey { get; set; }
}

public class IndexedMapPackage
{
	public int Id { get; set; }
	public string MapName { get; set; } = string.Empty;
	public string MapVersion { get; set; } = string.Empty;
	public string ManifestPath { get; set; } = string.Empty;
	public DateTime DownloadedUtc { get; set; }
	public List<string> AssetHashes { get; set; } = new();
	public bool IsP2P { get; set; }
}

public class IndexedFolder
{
	public int Id { get; set; }
	public string DirectoryPath { get; set; } = string.Empty;
	public DateTime LastScannedUtc { get; set; }
}

public class AssetMetadataModel
{
	public List<string> Tags { get; set; } = new();
}

public record AssetIndexProgressUpdate(double ProgressPercentage, string Message);

public class AssetIndexService : IDisposable
{
	private static AssetIndexService? _instance;
	public static AssetIndexService Instance => _instance ??= ServiceLocator.TryGet<AssetIndexService>() ?? new AssetIndexService();

	public event Action<string, bool>? DirectoryIndexingStateChanged;
	public event Action<string>? DirectoryScanCompleted;

	private LiteDatabase _database;
	private ILiteCollection<IndexedAsset> _assetCollection;
	private ILiteCollection<IndexedFolder> _folderCollection;
	private ILiteCollection<IndexedMapPackage> _mapPackageCollection;
	private readonly object _syncLock = new();
	private readonly HashSet<string> _indexingDirectories = new(StringComparer.OrdinalIgnoreCase);

	private LiteDatabase? _p2pDatabase;
	private readonly object _p2pSyncLock = new();

	private static string GetUserDirectory()
	{
		if (MapAssetManager.IsGodotEngineRunning)
		{
			return ProjectSettings.GlobalizePath("user://");
		}
		else
		{
			string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
			return Path.Combine(appData, "Godot", "app_userdata", "Realm");
		}
	}

	private static string GetCacheFilePath()
	{
		string userDirectory = GetUserDirectory();
		if (!Directory.Exists(userDirectory))
		{
			Directory.CreateDirectory(userDirectory);
		}
		return Path.Combine(userDirectory, "asset_index.cache");
	}

	private void EnsureIndexes()
	{
		_assetCollection.EnsureIndex(x => x.FilePath, true);
		_assetCollection.EnsureIndex(x => x.DirectoryPath);
		_assetCollection.EnsureIndex(x => x.Extension);
		_assetCollection.EnsureIndex(x => x.Tags);
		_assetCollection.EnsureIndex(x => x.FileName);
		_assetCollection.EnsureIndex(x => x.MapName);
		_assetCollection.EnsureIndex(x => x.MapVersion);
		_assetCollection.EnsureIndex(x => x.Blake3);
		_assetCollection.EnsureIndex(x => x.HasRealmMetadata);
		_assetCollection.EnsureIndex(x => x.AssetType);
		_assetCollection.EnsureIndex(x => x.HasPlayerColorMask);

		_folderCollection.EnsureIndex(x => x.DirectoryPath, true);

		_mapPackageCollection.EnsureIndex(x => x.MapName);
		_mapPackageCollection.EnsureIndex(x => x.MapVersion);
	}

	public string GetIndexGameBuildNumber()
	{
		lock (_syncLock)
		{
			try
			{
				var metaCol = _database.GetCollection<BsonDocument>("_metadata");
				var doc = metaCol.FindById(1);
				if (doc != null && doc.TryGetValue("GameBuildNumber", out var val) && val.IsString)
				{
					return val.AsString;
				}
			}
			catch { }
			return string.Empty;
		}
	}

	public void SetIndexGameBuildNumber(string gameBuildNumber)
	{
		lock (_syncLock)
		{
			try
			{
				var metaCol = _database.GetCollection<BsonDocument>("_metadata");
				var doc = new BsonDocument
				{
					["_id"] = 1,
					["GameBuildNumber"] = gameBuildNumber
				};
				metaCol.Upsert(doc);
				_database.Checkpoint();
			}
			catch { }
		}
	}

	public bool IsIndexVersionMismatch()
	{
		string currentIndexVersion = GetIndexGameBuildNumber();
		return !string.Equals(currentIndexVersion, Realm.Shared.RealmVersion.GameBuildNumber, StringComparison.OrdinalIgnoreCase);
	}

	public bool HasIncorrectlyIndexedSample()
	{
		lock (_syncLock)
		{
			try
			{
				var categories = _assetCollection.Find(Query.Not("AssetType", BsonValue.Null))
					.Select(a => a.AssetType)
					.Where(t => !string.IsNullOrEmpty(t))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();

				if (categories.Count == 0)
				{
					return false;
				}

				foreach (var category in categories)
				{
					if (string.IsNullOrEmpty(category))
					{
						continue;
					}

					var matchingAssets = _assetCollection.Find(Query.EQ("AssetType", category)).ToList();
					if (matchingAssets.Count == 0)
					{
						continue;
					}

					var sampledAsset = matchingAssets[Random.Shared.Next(matchingAssets.Count)];
					if (string.IsNullOrEmpty(sampledAsset.FilePath) || !File.Exists(sampledAsset.FilePath))
					{
						return true;
					}

					var fileInfo = new FileInfo(sampledAsset.FilePath);
					if (fileInfo.Length != sampledAsset.FileSizeBytes)
					{
						return true;
					}

					string diskExtension = Path.GetExtension(sampledAsset.FilePath).ToLowerInvariant();
					if (!string.Equals(diskExtension, sampledAsset.Extension, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}

					string? diskMetadataJson = RealmMetadataHelper.ExtractMetadata(sampledAsset.FilePath);
					bool diskHasMetadata = !string.IsNullOrEmpty(diskMetadataJson);

					if (diskHasMetadata != sampledAsset.HasRealmMetadata)
					{
						return true;
					}

					string? diskAssetType = RealmMetadataHelper.ExtractAssetType(sampledAsset.FilePath);
					if (!string.IsNullOrEmpty(diskAssetType))
					{
						if (!string.Equals(diskAssetType, sampledAsset.AssetType, StringComparison.OrdinalIgnoreCase))
						{
							return true;
						}
					}
					else
					{
						if (!RealmMetadataHelper.IsValidAssetTypeForExtension(sampledAsset.FilePath, sampledAsset.AssetType, out string canonicalType, out _))
						{
							return true;
						}
						if (!string.Equals(canonicalType, sampledAsset.AssetType, StringComparison.OrdinalIgnoreCase))
						{
							return true;
						}
					}

					if (!string.IsNullOrEmpty(sampledAsset.Blake3) && diskHasMetadata)
					{
						try
						{
							var node = JsonNode.Parse(diskMetadataJson!);
							if (node is JsonObject obj && obj.ContainsKey("blake3"))
							{
								string? metaBlake3 = obj["blake3"]?.ToString();
								if (!string.IsNullOrEmpty(metaBlake3) && !string.Equals(metaBlake3, sampledAsset.Blake3, StringComparison.OrdinalIgnoreCase))
								{
									return true;
								}
							}
						}
						catch
						{
							return true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[AssetIndexService] Error verifying asset index sample integrity: {ex.Message}");
				return true;
			}

			return false;
		}
	}

	public void RebuildIndexFromCas(IProgress<AssetIndexProgressUpdate>? progress = null)
	{
		lock (_syncLock)
		{
			progress?.Report(new AssetIndexProgressUpdate(0.05, TranslationServer.Translate("Resetting asset database cache...")));

			try
			{
				_database.Dispose();
			}
			catch { }

			ClearP2PIndex();

			string cacheFilePath = GetCacheFilePath();
			try
			{
				if (File.Exists(cacheFilePath))
				{
					File.Delete(cacheFilePath);
				}
				string logFile = cacheFilePath + "-log";
				if (File.Exists(logFile))
				{
					File.Delete(logFile);
				}
				string tempFile = cacheFilePath + "-temp";
				if (File.Exists(tempFile))
				{
					File.Delete(tempFile);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[AssetIndexService] Failed to delete cache files during rebuild: {ex.Message}");
			}

			progress?.Report(new AssetIndexProgressUpdate(0.15, TranslationServer.Translate("Initializing database schema and indexes...")));

			var connectionString = new ConnectionString
			{
				Filename = cacheFilePath,
				Connection = ConnectionType.Shared
			};

			_database = new LiteDatabase(connectionString);
			_assetCollection = _database.GetCollection<IndexedAsset>("assets");
			_folderCollection = _database.GetCollection<IndexedFolder>("folders");
			_mapPackageCollection = _database.GetCollection<IndexedMapPackage>("map_packages");

			EnsureIndexes();
			SetIndexGameBuildNumber(Realm.Shared.RealmVersion.GameBuildNumber);

			InitializeDefaultDirectories();
			ScanAllCasManifests(progress);

			progress?.Report(new AssetIndexProgressUpdate(1.0, TranslationServer.Translate("Asset index repair complete!")));
		}
	}

	public AssetIndexService()
	{
		string cacheFilePath = GetCacheFilePath();
		var connectionString = new ConnectionString
		{
			Filename = cacheFilePath,
			Connection = ConnectionType.Shared
		};

		_database = new LiteDatabase(connectionString);
		_assetCollection = _database.GetCollection<IndexedAsset>("assets");
		_folderCollection = _database.GetCollection<IndexedFolder>("folders");
		_mapPackageCollection = _database.GetCollection<IndexedMapPackage>("map_packages");

		EnsureIndexes();

		if (!IsIndexVersionMismatch())
		{
			InitializeDefaultDirectories();
		}
	}

	private LiteDatabase GetP2PDatabase()
	{
		lock (_p2pSyncLock)
		{
			if (_p2pDatabase != null) return _p2pDatabase;
			string p2pDir = MapAssetManager.P2PArchiveDirectory;
			if (!Directory.Exists(p2pDir)) Directory.CreateDirectory(p2pDir);
			string cacheFilePath = Path.Combine(p2pDir, "p2p_asset_index.cache");
			var connectionString = new ConnectionString
			{
				Filename = cacheFilePath,
				Connection = ConnectionType.Shared
			};
			_p2pDatabase = new LiteDatabase(connectionString);
			var p2pAssetCol = _p2pDatabase.GetCollection<IndexedAsset>("assets");
			var p2pMapCol = _p2pDatabase.GetCollection<IndexedMapPackage>("map_packages");
			p2pAssetCol.EnsureIndex(x => x.FilePath, true);
			p2pAssetCol.EnsureIndex(x => x.MapName);
			p2pAssetCol.EnsureIndex(x => x.MapVersion);
			p2pAssetCol.EnsureIndex(x => x.HasPlayerColorMask);
			p2pMapCol.EnsureIndex(x => x.MapName);
			p2pMapCol.EnsureIndex(x => x.MapVersion);
			return _p2pDatabase;
		}
	}

	public void ClearP2PIndex()
	{
		lock (_p2pSyncLock)
		{
			if (_p2pDatabase != null)
			{
				_p2pDatabase.Dispose();
				_p2pDatabase = null;
			}
		}
	}

	public static string GlobalCasAssetsDirectory => NormalizePath(MapAssetManager.Storage.AssetsDirectory);

	private void InitializeDefaultDirectories()
	{
		lock (_syncLock)
		{
			string legacyArchive = NormalizePath(Path.Combine(MapAssetManager.GlobalArchiveDirectory, "global_assets.rmap"));
			string casAssetsDirectory = GlobalCasAssetsDirectory;

			var forbiddenFolders = _folderCollection.FindAll()
				.Where(f => IsForbiddenPath(f.DirectoryPath) ||
							string.Equals(f.DirectoryPath, legacyArchive, StringComparison.OrdinalIgnoreCase) ||
							string.Equals(f.DirectoryPath, casAssetsDirectory, StringComparison.OrdinalIgnoreCase) ||
							f.DirectoryPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
							f.DirectoryPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
				.Select(f => (BsonValue)f.Id)
				.ToArray();
			if (forbiddenFolders.Length > 0)
			{
				_folderCollection.DeleteMany(Query.In("_id", forbiddenFolders));
			}

			if (!Directory.Exists(casAssetsDirectory))
			{
				Directory.CreateDirectory(casAssetsDirectory);
			}

			var validFolders = _folderCollection.FindAll()
				.Select(f => f.DirectoryPath)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			var orphanedAssets = _assetCollection.FindAll()
				.Where(a => (!validFolders.Contains(a.DirectoryPath) && !string.Equals(a.DirectoryPath, casAssetsDirectory, StringComparison.OrdinalIgnoreCase)) ||
							IsForbiddenPath(a.DirectoryPath) ||
							IsForbiddenPath(a.FilePath) ||
							a.DirectoryPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
							a.DirectoryPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
							a.FilePath.Contains("/extracted/", StringComparison.OrdinalIgnoreCase) ||
							a.FilePath.Contains("\\extracted\\", StringComparison.OrdinalIgnoreCase))
				.Select(a => (BsonValue)a.Id)
				.ToArray();
			if (orphanedAssets.Length > 0)
			{
				_assetCollection.DeleteMany(Query.In("_id", orphanedAssets));
			}

			var orphanedPackages = _mapPackageCollection.FindAll()
				.Where(p => string.IsNullOrWhiteSpace(p.ManifestPath) || !File.Exists(p.ManifestPath))
				.Select(p => (BsonValue)p.Id)
				.ToArray();
			if (orphanedPackages.Length > 0)
			{
				_mapPackageCollection.DeleteMany(Query.In("_id", orphanedPackages));
			}

			_database.Checkpoint();
		}
	}

	public void ScanAllCasManifests(IProgress<AssetIndexProgressUpdate>? progress = null)
	{
		var referencedCasPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (Directory.Exists(MapAssetManager.GlobalArchiveDirectory))
		{
			var manifestFiles = Directory.GetFiles(MapAssetManager.GlobalArchiveDirectory, "manifest.json", SearchOption.AllDirectories);
			for (int i = 0; i < manifestFiles.Length; i++)
			{
				string file = manifestFiles[i];
				try
				{
					var manifest = MapManifest.LoadFromFile(file);
					if (manifest != null && manifest.Files != null && manifest.Files.Count > 0)
					{
						string mapName = !string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName : Path.GetFileName(Path.GetDirectoryName(file)) ?? "Map";
						double pct = 0.20 + 0.65 * ((double)i / Math.Max(1, manifestFiles.Length));
						progress?.Report(new AssetIndexProgressUpdate(pct, string.Format(TranslationServer.Translate("Indexing manifest {0}/{1}: {2}..."), i + 1, manifestFiles.Length, mapName)));

						var paths = RegisterManifestInternal(manifest, file, isP2P: false);
						foreach (var p in paths)
						{
							referencedCasPaths.Add(p);
						}
					}
				}
				catch { }
			}
		}

		lock (_syncLock)
		{
			var orphanedCasAssets = _assetCollection.Find(Query.EQ("DirectoryPath", GlobalCasAssetsDirectory))
				.Where(a => !referencedCasPaths.Contains(a.FilePath) || !File.Exists(a.FilePath))
				.Select(a => (BsonValue)a.Id)
				.ToArray();
			if (orphanedCasAssets.Length > 0)
			{
				_assetCollection.DeleteMany(Query.In("_id", orphanedCasAssets));
			}

			var orphanedPackages = _mapPackageCollection.FindAll()
				.Where(p => !p.IsP2P && (string.IsNullOrWhiteSpace(p.ManifestPath) || !File.Exists(p.ManifestPath)))
				.Select(p => (BsonValue)p.Id)
				.ToArray();
			if (orphanedPackages.Length > 0)
			{
				_mapPackageCollection.DeleteMany(Query.In("_id", orphanedPackages));
			}

			_database.Checkpoint();
		}

		if (Directory.Exists(MapAssetManager.P2PArchiveDirectory))
		{
			var p2pManifestFiles = Directory.GetFiles(MapAssetManager.P2PArchiveDirectory, "manifest.json", SearchOption.AllDirectories);
			for (int j = 0; j < p2pManifestFiles.Length; j++)
			{
				string file = p2pManifestFiles[j];
				try
				{
					var manifest = MapManifest.LoadFromFile(file);
					if (manifest != null && manifest.Files != null && manifest.Files.Count > 0)
					{
						double pct = 0.85 + 0.12 * ((double)j / Math.Max(1, p2pManifestFiles.Length));
						progress?.Report(new AssetIndexProgressUpdate(pct, string.Format(TranslationServer.Translate("Indexing P2P archive {0}/{1}..."), j + 1, p2pManifestFiles.Length)));
						RegisterManifest(manifest, file, isP2P: true);
					}
				}
				catch { }
			}
		}
	}

	public void RegisterManifest(Realm.Shared.Distribution.MapManifest manifest, string manifestPath, bool isP2P = false)
	{
		if (manifest == null) return;
		lock (isP2P ? _p2pSyncLock : _syncLock)
		{
			RegisterManifestInternal(manifest, manifestPath, isP2P);
		}
	}

	private List<string> RegisterManifestInternal(Realm.Shared.Distribution.MapManifest manifest, string manifestPath, bool isP2P)
	{
		var indexedPaths = new List<string>();
		if (manifest == null) return indexedPaths;
		string mapName = !string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName.Trim() : Path.GetFileNameWithoutExtension(manifestPath).Replace("_manifest", "");
		string mapVersion = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";
		int manifestFileCount = manifest.Files?.Count ?? 0;

		var manifestFileInfo = new FileInfo(manifestPath);
		DateTime manifestLastWrite = manifestFileInfo.Exists ? manifestFileInfo.LastWriteTimeUtc : DateTime.UtcNow;

		if (isP2P)
		{
			var p2pDb = GetP2PDatabase();
			var p2pMapCol = p2pDb.GetCollection<IndexedMapPackage>("map_packages");
			var p2pAssetCol = p2pDb.GetCollection<IndexedAsset>("assets");

			var package = p2pMapCol.FindOne(p => p.MapName == mapName && p.MapVersion == mapVersion);
			package ??= new IndexedMapPackage();
			package.MapName = mapName;
			package.MapVersion = mapVersion;
			package.ManifestPath = manifestPath;
			package.DownloadedUtc = DateTime.UtcNow;
			package.IsP2P = true;
			package.AssetHashes = manifest.Files != null ? manifest.Files.Values.ToList() : new List<string>();
			p2pMapCol.Upsert(package);

			if (manifest.Files != null)
			{
				var existingP2pMap = p2pAssetCol.Find(Query.EQ("DirectoryPath", MapAssetManager.P2PArchiveDirectory))
					.ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
				var processedP2pPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var p2pToUpdate = new List<IndexedAsset>();
				var p2pToInsert = new List<IndexedAsset>();

				foreach (var kvp in manifest.Files)
				{
					string virtualPath = kvp.Key;
					string hash = kvp.Value;
					string norm = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(hash);
					string? casPath = MapAssetManager.P2PStorage.FindAssetFilePath(norm);
					if (casPath == null || !File.Exists(casPath))
					{
						continue;
					}

					string normPath = NormalizePath(casPath);
					indexedPaths.Add(normPath);

					if (!processedP2pPaths.Add(normPath))
					{
						continue;
					}

					if (existingP2pMap.TryGetValue(normPath, out var existingP2pAsset))
					{
						bool needsUpdate = false;
						string? metaJson = MapAssetManager.P2PStorage.GetAssetMetadata(norm);
						if (string.IsNullOrEmpty(metaJson))
						{
							metaJson = Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(normPath);
						}
						var parsed = ParseMetadataHeaders(metaJson, virtualPath);
						string? assetType = parsed.AssetType;
						if (string.IsNullOrEmpty(assetType))
						{
							string? dir = Path.GetDirectoryName(virtualPath);
							if (!string.IsNullOrEmpty(dir))
							{
								string[] dirParts = dir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
								foreach (var part in dirParts.Reverse())
								{
									if (Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(virtualPath, part, out string canonical, out _))
									{
										assetType = canonical;
										break;
									}
								}
							}
						}
						if (!string.IsNullOrEmpty(assetType) && existingP2pAsset.AssetType != assetType)
						{
							existingP2pAsset.AssetType = assetType;
							existingP2pAsset.HasRealmMetadata = !string.IsNullOrEmpty(metaJson) || existingP2pAsset.HasRealmMetadata;
							needsUpdate = true;
						}
						bool existingP2pHasMask = parsed.SupportsTeamColor ?? DetermineHasPlayerColorMask(metaJson, normPath);
						string resolvedP2pChroma = parsed.ChromaKey ?? string.Empty;
						if (existingP2pAsset.HasPlayerColorMask != existingP2pHasMask || existingP2pAsset.ChromaKey != resolvedP2pChroma)
						{
							existingP2pAsset.HasPlayerColorMask = existingP2pHasMask;
							existingP2pAsset.ChromaKey = resolvedP2pChroma;
							needsUpdate = true;
						}
						if (string.IsNullOrEmpty(existingP2pAsset.MapName) || existingP2pAsset.MapName != mapName)
						{
							existingP2pAsset.MapName = mapName;
							needsUpdate = true;
						}
						if (string.IsNullOrEmpty(existingP2pAsset.MapVersion) || existingP2pAsset.MapVersion != mapVersion)
						{
							existingP2pAsset.MapVersion = mapVersion;
							needsUpdate = true;
						}
						if (needsUpdate)
						{
							p2pToUpdate.Add(existingP2pAsset);
						}

						var fiExisting = new FileInfo(normPath);
						string extExisting = existingP2pAsset.Extension;
						if (extExisting == ".rmesh")
						{
							if (!GlbThumbnailRenderer.HasDiskCache(normPath, norm))
							{
								GlbThumbnailRenderer.EnqueueRequest(normPath, fiExisting.LastWriteTimeUtc, norm, isHighPriority: false);
							}
						}
						continue;
					}

					var fi = new FileInfo(normPath);
					var asset = new IndexedAsset();
					asset.FilePath = normPath;
					asset.FileName = Path.GetFileName(virtualPath);
					asset.Extension = !string.IsNullOrEmpty(Path.GetExtension(virtualPath)) ? Path.GetExtension(virtualPath).ToLowerInvariant() : Path.GetExtension(normPath).ToLowerInvariant();
					asset.DirectoryPath = MapAssetManager.P2PArchiveDirectory;
					asset.FileSizeBytes = fi.Length;
					asset.LastModifiedUtc = fi.LastWriteTimeUtc;
					asset.MapName = mapName;
					asset.MapVersion = mapVersion;
					asset.Blake3 = norm;

					string? p2pMetaJson = MapAssetManager.P2PStorage.GetAssetMetadata(norm);
					if (string.IsNullOrEmpty(p2pMetaJson))
					{
						p2pMetaJson = Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(normPath);
					}

					var parsedMeta = ParseMetadataHeaders(p2pMetaJson, virtualPath);
					var tags = parsedMeta.Tags;
					string? p2pAssetType = parsedMeta.AssetType;
					bool hasRealmMetadata = !string.IsNullOrEmpty(p2pMetaJson);

					if (tags.Count == 0)
					{
						tags = LoadTagsForFile(normPath, MapAssetManager.P2PArchiveDirectory, tags);
					}
					string preferredNameNoExt = Path.GetFileNameWithoutExtension(asset.FileName);
					var nameTokens = preferredNameNoExt.Split(new[] { '_', '-', ' ', '.', '@' }, StringSplitOptions.RemoveEmptyEntries);
					foreach (var token in nameTokens)
					{
						if (token.Length > 1 && !char.IsDigit(token[0]) && !tags.Contains(token, StringComparer.OrdinalIgnoreCase))
						{
							tags.Add(token.ToLowerInvariant());
						}
					}
					if (manifest.Tags != null)
					{
						foreach (var tag in manifest.Tags)
						{
							if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase))
							{
								tags.Add(tag.Trim().ToLowerInvariant());
							}
						}
					}
					asset.Tags = tags;
					asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = tags });

					if (string.IsNullOrEmpty(p2pAssetType))
					{
						string? dir = Path.GetDirectoryName(virtualPath);
						if (!string.IsNullOrEmpty(dir))
						{
							string[] dirParts = dir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
							foreach (var part in dirParts.Reverse())
							{
								if (Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(virtualPath, part, out string canonical, out _))
								{
									p2pAssetType = canonical;
									break;
								}
							}
						}
					}
					if (string.IsNullOrEmpty(p2pAssetType) && tags != null)
					{
						foreach (var tag in tags)
						{
							if (Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(virtualPath, tag, out string canonical, out _))
							{
								p2pAssetType = canonical;
								break;
							}
						}
					}

					bool p2pHasMask = parsedMeta.SupportsTeamColor ?? DetermineHasPlayerColorMask(p2pMetaJson, normPath);
					asset.HasPlayerColorMask = p2pHasMask;
					asset.ChromaKey = parsedMeta.ChromaKey ?? string.Empty;

					asset.AssetType = p2pAssetType;
					asset.HasRealmMetadata = hasRealmMetadata || !string.IsNullOrEmpty(p2pAssetType);

					p2pToInsert.Add(asset);
					existingP2pMap[normPath] = asset;

					string extNew = asset.Extension;
					if (extNew == ".rmesh")
					{
						if (!GlbThumbnailRenderer.HasDiskCache(normPath, norm))
						{
							GlbThumbnailRenderer.EnqueueRequest(normPath, fi.LastWriteTimeUtc, norm, isHighPriority: false);
						}
					}
				}

				if (p2pToUpdate.Count > 0)
				{
					p2pAssetCol.Update(p2pToUpdate);
				}
				if (p2pToInsert.Count > 0)
				{
					p2pAssetCol.Insert(p2pToInsert);
				}
			}

			p2pDb.Checkpoint();
			return indexedPaths;
		}

		var pkg = _mapPackageCollection.FindOne(p => p.MapName == mapName && p.MapVersion == mapVersion);
		pkg ??= new IndexedMapPackage();
		pkg.MapName = mapName;
		pkg.MapVersion = mapVersion;
		pkg.ManifestPath = manifestPath;
		pkg.DownloadedUtc = DateTime.UtcNow;
		pkg.IsP2P = false;
		pkg.AssetHashes = manifest.Files != null ? manifest.Files.Values.ToList() : new List<string>();
		_mapPackageCollection.Upsert(pkg);

		if (manifest.Files != null)
		{
			var existingAssetMap = _assetCollection.Find(Query.EQ("DirectoryPath", GlobalCasAssetsDirectory))
				.ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
			var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var assetsToUpdate = new List<IndexedAsset>();
			var assetsToInsert = new List<IndexedAsset>();

			foreach (var kvp in manifest.Files)
			{
				string virtualPath = kvp.Key;
				string hash = kvp.Value;
				string norm = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(hash);
				string? casPath = MapAssetManager.Storage.FindAssetFilePath(norm);
				if (casPath == null || !File.Exists(casPath))
				{
					continue;
				}

				string normPath = NormalizePath(casPath);
				indexedPaths.Add(normPath);

				if (!processedPaths.Add(normPath))
				{
					continue;
				}

				if (existingAssetMap.TryGetValue(normPath, out var existingAsset))
				{
					bool needsUpdate = false;
					string? existingMetaJson = MapAssetManager.Storage.GetAssetMetadata(norm);
					if (string.IsNullOrEmpty(existingMetaJson))
					{
						existingMetaJson = Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(normPath);
					}
					var parsed = ParseMetadataHeaders(existingMetaJson, virtualPath);
					string? existingAssetType = parsed.AssetType;
					if (string.IsNullOrEmpty(existingAssetType))
					{
						string? dir = Path.GetDirectoryName(virtualPath);
						if (!string.IsNullOrEmpty(dir))
						{
							string[] dirParts = dir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
							foreach (var part in dirParts.Reverse())
							{
								if (Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(virtualPath, part, out string canonical, out _))
								{
									existingAssetType = canonical;
									break;
								}
							}
						}
					}
					if (!string.IsNullOrEmpty(existingAssetType) && existingAsset.AssetType != existingAssetType)
					{
						existingAsset.AssetType = existingAssetType;
						existingAsset.HasRealmMetadata = !string.IsNullOrEmpty(existingMetaJson) || existingAsset.HasRealmMetadata;
						needsUpdate = true;
					}
					bool existingCasHasMask = parsed.SupportsTeamColor ?? DetermineHasPlayerColorMask(existingMetaJson, normPath);
					string resolvedCasChroma = parsed.ChromaKey ?? string.Empty;
					if (existingAsset.HasPlayerColorMask != existingCasHasMask || existingAsset.ChromaKey != resolvedCasChroma)
					{
						existingAsset.HasPlayerColorMask = existingCasHasMask;
						existingAsset.ChromaKey = resolvedCasChroma;
						needsUpdate = true;
					}
					if (string.IsNullOrEmpty(existingAsset.MapName) || existingAsset.MapName != mapName)
					{
						existingAsset.MapName = mapName;
						needsUpdate = true;
					}
					if (string.IsNullOrEmpty(existingAsset.MapVersion) || existingAsset.MapVersion != mapVersion)
					{
						existingAsset.MapVersion = mapVersion;
						needsUpdate = true;
					}
					if (needsUpdate)
					{
						assetsToUpdate.Add(existingAsset);
					}

					var fiExisting = new FileInfo(normPath);
					string extExisting = existingAsset.Extension;
					if (extExisting == ".rmesh")
					{
						if (!GlbThumbnailRenderer.HasDiskCache(normPath, norm))
						{
							GlbThumbnailRenderer.EnqueueRequest(normPath, fiExisting.LastWriteTimeUtc, norm, isHighPriority: false);
						}
					}
					continue;
				}

				var fi = new FileInfo(normPath);
				var asset = new IndexedAsset();
				asset.FilePath = normPath;
				asset.FileName = Path.GetFileName(virtualPath);
				asset.Extension = !string.IsNullOrEmpty(Path.GetExtension(virtualPath)) ? Path.GetExtension(virtualPath).ToLowerInvariant() : Path.GetExtension(normPath).ToLowerInvariant();
				asset.DirectoryPath = GlobalCasAssetsDirectory;
				asset.FileSizeBytes = fi.Length;
				asset.LastModifiedUtc = fi.LastWriteTimeUtc;
				asset.MapName = mapName;
				asset.MapVersion = mapVersion;
				asset.Blake3 = norm;

				string? metaJson = MapAssetManager.Storage.GetAssetMetadata(norm);
				if (string.IsNullOrEmpty(metaJson))
				{
					metaJson = Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(normPath);
				}

				var parsedMeta = ParseMetadataHeaders(metaJson, virtualPath);
				var tags = parsedMeta.Tags;
				string? assetType = parsedMeta.AssetType;
				bool hasRealmMetadata = !string.IsNullOrEmpty(metaJson);

				if (tags.Count == 0)
				{
					tags = LoadTagsForFile(normPath, GlobalCasAssetsDirectory, tags);
				}
				string preferredNameNoExt = Path.GetFileNameWithoutExtension(asset.FileName);
				var nameTokens = preferredNameNoExt.Split(new[] { '_', '-', ' ', '.', '@' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (var token in nameTokens)
				{
					if (token.Length > 1 && !char.IsDigit(token[0]) && !tags.Contains(token, StringComparer.OrdinalIgnoreCase))
					{
						tags.Add(token.ToLowerInvariant());
					}
				}
				if (manifest.Tags != null)
				{
					foreach (var tag in manifest.Tags)
					{
						if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase))
						{
							tags.Add(tag.Trim().ToLowerInvariant());
						}
					}
				}
				asset.Tags = tags;
				asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = tags });

				if (string.IsNullOrEmpty(assetType))
				{
					string? dir = Path.GetDirectoryName(virtualPath);
					if (!string.IsNullOrEmpty(dir))
					{
						string[] dirParts = dir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
						foreach (var part in dirParts.Reverse())
						{
							if (Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(virtualPath, part, out string canonical, out _))
							{
								assetType = canonical;
								break;
							}
						}
					}
				}
				if (string.IsNullOrEmpty(assetType) && tags != null)
				{
					foreach (var tag in tags)
					{
						if (Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(virtualPath, tag, out string canonical, out _))
						{
							assetType = canonical;
							break;
						}
					}
				}

				bool casHasMask = parsedMeta.SupportsTeamColor ?? DetermineHasPlayerColorMask(metaJson, normPath);
				asset.HasPlayerColorMask = casHasMask;
				asset.ChromaKey = parsedMeta.ChromaKey ?? string.Empty;

				asset.AssetType = assetType;
				asset.HasRealmMetadata = hasRealmMetadata || !string.IsNullOrEmpty(assetType);

				assetsToInsert.Add(asset);
				existingAssetMap[normPath] = asset;

				string extNew = asset.Extension;
				if (extNew == ".rmesh")
				{
					if (!GlbThumbnailRenderer.HasDiskCache(normPath, norm))
					{
						GlbThumbnailRenderer.EnqueueRequest(normPath, fi.LastWriteTimeUtc, norm, isHighPriority: false);
					}
				}
			}

			if (assetsToUpdate.Count > 0)
			{
				_assetCollection.Update(assetsToUpdate);
			}
			if (assetsToInsert.Count > 0)
			{
				_assetCollection.Insert(assetsToInsert);
			}
		}

		_database.Checkpoint();
		return indexedPaths;
	}

	public IReadOnlyList<IndexedMapPackage> GetDownloadedMapPackages()
	{
		lock (_syncLock)
		{
			return _mapPackageCollection.FindAll()
				.Where(p => !p.IsP2P && !string.IsNullOrWhiteSpace(p.ManifestPath) && File.Exists(p.ManifestPath))
				.OrderBy(p => p.MapName, StringComparer.OrdinalIgnoreCase)
				.ThenByDescending(p => p.MapVersion, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
	}

	public string? GetLatestVersionForMap(string mapName)
	{
		if (string.IsNullOrWhiteSpace(mapName)) return null;
		lock (_syncLock)
		{
			var packages = _mapPackageCollection.Find(p => p.MapName == mapName && !p.IsP2P).ToList();
			if (packages.Count == 0) return null;
			packages.Sort((a, b) => CompareVersions(b.MapVersion, a.MapVersion));
			return packages[0].MapVersion;
		}
	}

	public static int CompareVersions(string v1, string v2)
	{
		if (System.Version.TryParse(v1, out var ver1) && System.Version.TryParse(v2, out var ver2))
		{
			return ver1.CompareTo(ver2);
		}
		return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsForbiddenPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return true;
		string norm = path.Replace('\\', '/').ToLowerInvariant();
		return norm.Contains(MapWorkspaceService.DefaultWorkspaceFolder) || norm.Contains("maptemplate");
	}

	public bool IsDirectoryIndexing(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath)) return false;
		lock (_syncLock)
		{
			return _indexingDirectories.Contains(NormalizePath(directoryPath));
		}
	}

	public IReadOnlyList<string> GetIndexedDirectories()
	{
		lock (_syncLock)
		{
			return _folderCollection.FindAll()
				.Select(f => f.DirectoryPath)
				.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
	}

	public void AddDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return;
		}

		string normalizedPath = NormalizePath(directoryPath);
		if (IsForbiddenPath(normalizedPath) || !Directory.Exists(normalizedPath))
		{
			return;
		}

		if (normalizedPath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
			normalizedPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
			normalizedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
			normalizedPath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
			normalizedPath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
			normalizedPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		lock (_syncLock)
		{
			var existingFolder = _folderCollection.FindOne(x => x.DirectoryPath == normalizedPath);
			if (existingFolder == null)
			{
				existingFolder = new IndexedFolder
				{
					DirectoryPath = normalizedPath,
					LastScannedUtc = DateTime.MinValue
				};
				_folderCollection.Insert(existingFolder);
				_database.Checkpoint();
			}

			if (_indexingDirectories.Contains(normalizedPath))
			{
				return;
			}
			_indexingDirectories.Add(normalizedPath);
		}

		DirectoryIndexingStateChanged?.Invoke(normalizedPath, true);

		Task.Run(() =>
		{
			try
			{
				ScanDirectoryInternal(normalizedPath);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[AssetIndexService] Scan error on {normalizedPath}: {ex.Message}");
			}
			finally
			{
				lock (_syncLock)
				{
					_indexingDirectories.Remove(normalizedPath);
				}
				DirectoryIndexingStateChanged?.Invoke(normalizedPath, false);
				DirectoryScanCompleted?.Invoke(normalizedPath);
			}
		});
	}

	public void RemoveDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return;
		}

		string normalizedPath = NormalizePath(directoryPath);
		if (string.Equals(normalizedPath, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		lock (_syncLock)
		{
			_indexingDirectories.Remove(normalizedPath);

			_folderCollection.DeleteMany(f => f.DirectoryPath == normalizedPath);
			var leftoverFolders = _folderCollection.FindAll()
				.Where(f => string.Equals(f.DirectoryPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
				.Select(f => (BsonValue)f.Id)
				.ToArray();
			if (leftoverFolders.Length > 0)
			{
				_folderCollection.DeleteMany(Query.In("_id", leftoverFolders));
			}

			_assetCollection.DeleteMany(Query.EQ("DirectoryPath", normalizedPath));
			_assetCollection.DeleteMany(Query.StartsWith("FilePath", normalizedPath + "/"));
			_assetCollection.DeleteMany(Query.StartsWith("FilePath", normalizedPath + "\\"));

			_database.Checkpoint();
		}

		DirectoryIndexingStateChanged?.Invoke(normalizedPath, false);
	}

	public void RescanDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return;
		}

		string normalizedPath = NormalizePath(directoryPath);
		if (IsForbiddenPath(normalizedPath) || !Directory.Exists(normalizedPath))
		{
			return;
		}

		lock (_syncLock)
		{
			if (_indexingDirectories.Contains(normalizedPath))
			{
				return;
			}
			_indexingDirectories.Add(normalizedPath);
		}

		DirectoryIndexingStateChanged?.Invoke(normalizedPath, true);

		Task.Run(() =>
		{
			try
			{
				ScanDirectoryInternal(normalizedPath);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[AssetIndexService] Rescan error on {normalizedPath}: {ex.Message}");
			}
			finally
			{
				lock (_syncLock)
				{
					_indexingDirectories.Remove(normalizedPath);
				}
				DirectoryIndexingStateChanged?.Invoke(normalizedPath, false);
				DirectoryScanCompleted?.Invoke(normalizedPath);
			}
		});
	}

	public void RescanAllDirectories()
	{
		List<string> dirsToScan;
		lock (_syncLock)
		{
			var validFolders = _folderCollection.FindAll()
				.Select(f => f.DirectoryPath)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			var orphanedAssets = _assetCollection.FindAll()
				.Where(a => (!validFolders.Contains(a.DirectoryPath) && !string.Equals(a.DirectoryPath, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase)) || IsForbiddenPath(a.DirectoryPath))
				.Select(a => (BsonValue)a.Id)
				.ToArray();
			if (orphanedAssets.Length > 0)
			{
				_assetCollection.DeleteMany(Query.In("_id", orphanedAssets));
			}

			dirsToScan = _folderCollection.FindAll()
				.Select(f => f.DirectoryPath)
				.Where(p => Directory.Exists(p) && !_indexingDirectories.Contains(p) && !string.Equals(p, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
				.ToList();

			foreach (var dir in dirsToScan)
			{
				_indexingDirectories.Add(dir);
			}

			_database.Checkpoint();
		}

		foreach (var dir in dirsToScan)
		{
			DirectoryIndexingStateChanged?.Invoke(dir, true);
		}

		Task.Run(() =>
		{
			try
			{
				ScanAllCasManifests();
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[AssetIndexService] RescanAll CAS manifest scan error: {ex.Message}");
			}

			foreach (var dir in dirsToScan)
			{
				try
				{
					ScanDirectoryInternal(dir);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[AssetIndexService] RescanAll error on {dir}: {ex.Message}");
				}
				finally
				{
					lock (_syncLock)
					{
						_indexingDirectories.Remove(dir);
					}
					DirectoryIndexingStateChanged?.Invoke(dir, false);
					DirectoryScanCompleted?.Invoke(dir);
				}
			}
		});
	}

	private void ScanDirectoryInternal(string normalizedDirectoryPath)
	{
		if (!Directory.Exists(normalizedDirectoryPath))
		{
			return;
		}

		if (string.Equals(normalizedDirectoryPath, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
		{
			ScanAllCasManifests();
			return;
		}

		var discoveredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var files = Directory.EnumerateFiles(normalizedDirectoryPath, "*.*", SearchOption.AllDirectories);

		Dictionary<string, IndexedAsset> existingAssets;
		lock (_syncLock)
		{
			existingAssets = _assetCollection.Find(Query.EQ("DirectoryPath", normalizedDirectoryPath))
				.ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
		}

		var batchToUpsert = new List<IndexedAsset>();

		foreach (string filePath in files)
		{
			string normalizedFilePath = NormalizePath(filePath);
			string extension = Path.GetExtension(normalizedFilePath).ToLowerInvariant();

			if (extension == ".cache" || extension == ".log" || extension == ".tmp" || extension == ".uid")
			{
				continue;
			}

			if (normalizedFilePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
				(File.Exists(normalizedFilePath.Substring(0, normalizedFilePath.Length - 5))))
			{
				continue;
			}

			discoveredFiles.Add(normalizedFilePath);
			try
			{
				var fileInfo = new FileInfo(normalizedFilePath);
				string? dir = Path.GetDirectoryName(normalizedFilePath);
				string? expectedDirType = null;
				if (!string.IsNullOrEmpty(dir))
				{
					string[] dirParts = dir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
					foreach (var part in dirParts.Reverse())
					{
						if (Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(normalizedFilePath, part, out string canonical, out _))
						{
							expectedDirType = canonical;
							break;
						}
					}
				}

				if (existingAssets.TryGetValue(normalizedFilePath, out var existingAsset) &&
					existingAsset.FileSizeBytes == fileInfo.Length &&
					existingAsset.LastModifiedUtc == fileInfo.LastWriteTimeUtc &&
					!string.IsNullOrEmpty(existingAsset.Blake3) &&
					!string.IsNullOrEmpty(existingAsset.AssetType) &&
					existingAsset.ChromaKey != null)
				{
					if (expectedDirType == null || string.Equals(existingAsset.AssetType, expectedDirType, StringComparison.OrdinalIgnoreCase))
					{
						if (existingAsset.Extension == ".rmesh")
						{
							if (!GlbThumbnailRenderer.HasDiskCache(normalizedFilePath, existingAsset.Blake3))
							{
								GlbThumbnailRenderer.EnqueueRequest(normalizedFilePath, fileInfo.LastWriteTimeUtc, existingAsset.Blake3, isHighPriority: false);
							}
						}
						continue;
					}
				}

				string? metaJson = Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(normalizedFilePath);
				bool hasRealmMetadata = !string.IsNullOrEmpty(metaJson);
				string? assetType = null;
				string normBlake3 = string.Empty;
				var embeddedTags = new List<string>();

				if (hasRealmMetadata)
				{
					try
					{
						var node = JsonNode.Parse(metaJson!);
						if (node is JsonObject obj)
						{
							string? typeVal = obj["asset_type"]?.ToString()
								?? obj["AssetType"]?.ToString()
								?? obj["default_asset_type"]?.ToString();
							if (!string.IsNullOrEmpty(typeVal) && Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(normalizedFilePath, typeVal, out string canonical, out _))
							{
								assetType = canonical;
							}
							normBlake3 = obj["blake3"]?.ToString() ?? string.Empty;
							if (obj["tags"] is JsonArray arr)
							{
								foreach (var item in arr)
								{
									string? t = item?.ToString()?.Trim();
									if (!string.IsNullOrEmpty(t) && !embeddedTags.Contains(t, StringComparer.OrdinalIgnoreCase))
									{
										embeddedTags.Add(t);
									}
								}
							}
						}
					}
					catch { }
				}

				var tags = LoadTagsForFile(normalizedFilePath, normalizedDirectoryPath, embeddedTags);

				if (string.IsNullOrEmpty(assetType))
				{
					assetType = expectedDirType;
				}

				if (string.IsNullOrEmpty(assetType) && tags != null)
				{
					foreach (var tag in tags)
					{
						if (Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(normalizedFilePath, tag, out string canonical, out _))
						{
							assetType = canonical;
							break;
						}
					}
				}
				if (string.IsNullOrEmpty(normBlake3))
				{
					try
					{
						normBlake3 = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(normalizedFilePath);
					}
					catch { }
				}
				bool hasMask = DetermineHasPlayerColorMask(metaJson, normalizedFilePath);
				string? chromaKey = Realm.Shared.Metadata.RealmMetadataHelper.ExtractChromaKeyFromMetadataJson(metaJson)
					?? (!string.IsNullOrEmpty(normalizedFilePath) ? Realm.Shared.Metadata.RealmMetadataHelper.ExtractChromaKey(normalizedFilePath) : null);
				string fileName = Path.GetFileName(normalizedFilePath);
				var asset = existingAsset ?? _assetCollection.FindOne(x => x.FilePath == normalizedFilePath) ?? new IndexedAsset();
				asset.FilePath = normalizedFilePath;
				asset.FileName = fileName;
				asset.Extension = extension;
				asset.DirectoryPath = normalizedDirectoryPath;
				asset.FileSizeBytes = fileInfo.Length;
				asset.LastModifiedUtc = fileInfo.LastWriteTimeUtc;
				asset.Tags = tags;
				asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = tags });
				asset.HasRealmMetadata = hasRealmMetadata || !string.IsNullOrEmpty(assetType);
				asset.AssetType = assetType;
				asset.HasPlayerColorMask = hasMask;
				asset.ChromaKey = chromaKey ?? string.Empty;
				asset.MapName = existingAsset?.MapName ?? asset.MapName;
				asset.MapVersion = existingAsset?.MapVersion ?? asset.MapVersion;
				asset.Blake3 = !string.IsNullOrEmpty(normBlake3) ? Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(normBlake3) : string.Empty;

				batchToUpsert.Add(asset);
				if (asset.Extension == ".rmesh")
				{
					if (!GlbThumbnailRenderer.HasDiskCache(normalizedFilePath, asset.Blake3))
					{
						GlbThumbnailRenderer.EnqueueRequest(normalizedFilePath, fileInfo.LastWriteTimeUtc, asset.Blake3, isHighPriority: false);
					}
				}

				if (batchToUpsert.Count >= 250)
				{
					lock (_syncLock)
					{
						_assetCollection.Upsert(batchToUpsert);
					}
					batchToUpsert.Clear();
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[AssetIndexService] Scan error on {normalizedFilePath}: {ex.Message}");
			}
		}

		if (batchToUpsert.Count > 0)
		{
			lock (_syncLock)
			{
				_assetCollection.Upsert(batchToUpsert);
			}
			batchToUpsert.Clear();
		}

		lock (_syncLock)
		{
			var idsToDelete = existingAssets.Values
				.Where(x => !discoveredFiles.Contains(x.FilePath))
				.Select(x => (BsonValue)x.Id)
				.ToArray();
			if (idsToDelete.Length > 0)
			{
				_assetCollection.DeleteMany(Query.In("_id", idsToDelete));
			}

			var folderRecord = _folderCollection.FindOne(x => x.DirectoryPath == normalizedDirectoryPath);
			if (folderRecord != null)
			{
				folderRecord.LastScannedUtc = DateTime.UtcNow;
				_folderCollection.Update(folderRecord);
			}

			_database.Checkpoint();
		}
	}

	private struct ParsedAssetMetadata
	{
		public string? AssetType;
		public bool? SupportsTeamColor;
		public string? ChromaKey;
		public List<string> Tags;
	}

	private static ParsedAssetMetadata ParseMetadataHeaders(string? metaJson, string filePath)
	{
		var result = new ParsedAssetMetadata
		{
			Tags = new List<string>()
		};

		if (string.IsNullOrWhiteSpace(metaJson))
		{
			return result;
		}

		try
		{
			var node = JsonNode.Parse(metaJson);
			if (node is JsonObject obj)
			{
				string? typeVal = obj["asset_type"]?.ToString()
					?? obj["AssetType"]?.ToString()
					?? obj["default_asset_type"]?.ToString();
				if (!string.IsNullOrEmpty(typeVal) && Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(filePath, typeVal, out string canonical, out _))
				{
					result.AssetType = canonical;
				}

				if (obj["team_color"] != null && bool.TryParse(obj["team_color"]?.ToString(), out bool tcVal))
				{
					result.SupportsTeamColor = tcVal;
				}
				else if (obj["supports_team_color"] != null && bool.TryParse(obj["supports_team_color"]?.ToString(), out bool stcVal))
				{
					result.SupportsTeamColor = stcVal;
				}
				else if (obj["has_player_color_mask"] != null && bool.TryParse(obj["has_player_color_mask"]?.ToString(), out bool hpcmVal))
				{
					result.SupportsTeamColor = hpcmVal;
				}

				result.ChromaKey = obj["chroma_key"]?.ToString()
					?? obj["chromaKey"]?.ToString()
					?? obj["target_hex"]?.ToString()
					?? obj["targetHex"]?.ToString();

				if (obj["tags"] is JsonArray arr)
				{
					foreach (var item in arr)
					{
						string? t = item?.ToString()?.Trim();
						if (!string.IsNullOrEmpty(t) && !result.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))
						{
							result.Tags.Add(t);
						}
					}
				}
			}
		}
		catch { }

		return result;
	}

	private static bool DetermineHasPlayerColorMask(string? metaJson, string? filePath)
	{
		if (!string.IsNullOrEmpty(metaJson) && Realm.Shared.Metadata.RealmMetadataHelper.ExtractSupportsTeamColorFromMetadataJson(metaJson))
		{
			return true;
		}

		if (!string.IsNullOrEmpty(filePath))
		{
			bool? supports = Realm.Shared.Metadata.RealmMetadataHelper.ExtractSupportsTeamColorWithMetadata(metaJson, filePath);
			if (supports.HasValue)
			{
				return supports.Value;
			}
		}

		return false;
	}

	private static List<string> ExtractTagsFromMetadataJson(string? metaJson)
	{
		var list = new List<string>();
		if (string.IsNullOrWhiteSpace(metaJson)) return list;
		try
		{
			var node = JsonNode.Parse(metaJson);
			if (node is JsonObject obj && obj["tags"] is JsonArray arr)
			{
				foreach (var item in arr)
				{
					string? t = item?.ToString()?.Trim();
					if (!string.IsNullOrEmpty(t) && !list.Contains(t, StringComparer.OrdinalIgnoreCase))
					{
						list.Add(t);
					}
				}
			}
		}
		catch { }
		return list;
	}

	private List<string> LoadTagsForFile(string filePath, string rootDirectory, List<string>? preloadedTags = null)
	{
		var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (preloadedTags != null && preloadedTags.Count > 0)
		{
			foreach (var tag in preloadedTags)
			{
				tagSet.Add(tag);
			}
		}
		else
		{
			var embeddedTags = RealmMetadataHelper.ExtractTags(filePath);
			foreach (var tag in embeddedTags)
			{
				tagSet.Add(tag);
			}
		}

		if (tagSet.Count == 0)
		{
			bool isCas = filePath.StartsWith(GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase);
			if (!isCas)
			{
				string sidecarJsonWithExt = filePath + ".json";
				string sidecarJsonNoExt = Path.Combine(Path.GetDirectoryName(filePath)!, Path.GetFileNameWithoutExtension(filePath) + ".json");

				string? foundMetadataPath = null;
				if (File.Exists(sidecarJsonWithExt))
				{
					foundMetadataPath = sidecarJsonWithExt;
				}
				else if (File.Exists(sidecarJsonNoExt) && !string.Equals(sidecarJsonNoExt, filePath, StringComparison.OrdinalIgnoreCase))
				{
					foundMetadataPath = sidecarJsonNoExt;
				}

				if (foundMetadataPath != null)
				{
					try
					{
						string jsonContent = File.ReadAllText(foundMetadataPath);
						var rootNode = JsonNode.Parse(jsonContent);
						if (rootNode is JsonObject jsonObject)
						{
							if (jsonObject["tags"] is JsonArray tagsArray)
							{
								foreach (var item in tagsArray)
								{
									if (item != null)
									{
										string tagStr = item.ToString().Trim();
										if (!string.IsNullOrEmpty(tagStr))
										{
											tagSet.Add(tagStr);
										}
									}
								}
							}
						}
					}
					catch { }
				}
			}
		}

		if (tagSet.Count == 0)
		{
			string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
			var nameTokens = nameWithoutExt.Split(new[] { '_', '-', ' ', '.', '@' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var token in nameTokens)
			{
				if (token.Length > 1 && !char.IsDigit(token[0]))
				{
					tagSet.Add(token.ToLowerInvariant());
				}
			}

			string relativeDir = Path.GetRelativePath(rootDirectory, Path.GetDirectoryName(filePath) ?? rootDirectory);
			if (!string.IsNullOrEmpty(relativeDir) && relativeDir != ".")
			{
				var dirTokens = relativeDir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (var dir in dirTokens)
				{
					if (!string.IsNullOrWhiteSpace(dir))
					{
						tagSet.Add(dir.ToLowerInvariant());
					}
				}
			}

			string ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
			if (!string.IsNullOrEmpty(ext))
			{
				tagSet.Add(ext);
			}
		}

		return tagSet.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public void UpdateAssetTags(string filePath, List<string> newTags)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return;
		}

		string normalizedFilePath = NormalizePath(filePath);
		lock (_syncLock)
		{
			var asset = _assetCollection.FindOne(x => x.FilePath == normalizedFilePath);
			if (asset == null)
			{
				return;
			}

			asset.Tags = newTags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = asset.Tags });
			_assetCollection.Update(asset);

			RealmMetadataHelper.SetTags(normalizedFilePath, asset.Tags);

			bool isCasFile = normalizedFilePath.StartsWith(GlobalCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase);
			if (isCasFile)
			{
				string hash = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(normalizedFilePath));
				string? updatedMeta = RealmMetadataHelper.ExtractMetadata(normalizedFilePath);
				if (!string.IsNullOrWhiteSpace(updatedMeta))
				{
					MapAssetManager.Storage.UpdateSidecarCache(hash, updatedMeta);
				}
			}
			else
			{
				try
				{
					string sidecarPath = normalizedFilePath + ".json";
					var root = new JsonObject
					{
						["tags"] = new JsonArray(asset.Tags.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray())
					};
					File.WriteAllText(sidecarPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[AssetIndexService] UpdateAssetTags error writing sidecar: {ex.Message}");
				}
			}

			_database.Checkpoint();
		}
	}

	public void UpdateAssetType(string filePath, string newAssetType)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return;
		}

		string normalizedFilePath = NormalizePath(filePath);
		lock (_syncLock)
		{
			var asset = _assetCollection.FindOne(x => x.FilePath == normalizedFilePath);
			if (asset == null)
			{
				return;
			}

			asset.AssetType = newAssetType;
			asset.HasRealmMetadata = true;
			_assetCollection.Update(asset);

			RealmMetadataHelper.SetAssetType(normalizedFilePath, newAssetType);

			bool isCasFile = normalizedFilePath.StartsWith(GlobalCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase);
			if (isCasFile)
			{
				string hash = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(normalizedFilePath));
				string? updatedMeta = RealmMetadataHelper.ExtractMetadata(normalizedFilePath);
				if (!string.IsNullOrWhiteSpace(updatedMeta))
				{
					MapAssetManager.Storage.UpdateSidecarCache(hash, updatedMeta);
				}
			}

			_database.Checkpoint();
		}
	}

	public static string GetCanonicalBlake3(IndexedAsset asset)
	{
		if (!string.IsNullOrEmpty(asset.Blake3))
		{
			return asset.Blake3;
		}

		if (asset.FilePath.StartsWith(GlobalCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase) ||
			asset.FilePath.StartsWith(GlobalCasAssetsDirectory + "\\", StringComparison.OrdinalIgnoreCase))
		{
			return Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(asset.FilePath));
		}

		return Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(asset.FileName);
	}

	public IndexedAsset? GetAssetByPath(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return null;
		}

		string normalizedFilePath = NormalizePath(filePath);
		lock (_syncLock)
		{
			return _assetCollection.FindOne(x => x.FilePath == normalizedFilePath);
		}
	}

	public List<IndexedAsset> SearchAssets(
		string? searchTerm,
		IReadOnlyCollection<string>? allowedExtensions = null,
		string? directoryFilter = null,
		bool requireRealmMetadata = false,
		string? requiredAssetType = null,
		string? mapNameFilter = null,
		string? mapVersionFilter = null,
		bool requirePlayerColorMask = false)
	{
		try
		{
			lock (_syncLock)
			{
				var query = _assetCollection.Query();

				if (!string.IsNullOrWhiteSpace(mapNameFilter))
				{
					string targetVersion = mapVersionFilter ?? "latest";
					if (string.Equals(targetVersion, "latest", StringComparison.OrdinalIgnoreCase))
					{
						string? latest = GetLatestVersionForMap(mapNameFilter);
						targetVersion = latest ?? "";
					}

					if (!string.IsNullOrEmpty(targetVersion))
					{
						query = query.Where(x => x.MapName == mapNameFilter && x.MapVersion == targetVersion);
					}
					else
					{
						query = query.Where(x => x.MapName == mapNameFilter);
					}
				}
				else if (!string.IsNullOrWhiteSpace(directoryFilter))
				{
					string normalizedDir = NormalizePath(directoryFilter);
					query = query.Where(x => x.DirectoryPath == normalizedDir);
				}

				HashSet<string>? extensionFilterSet = null;
				if (allowedExtensions != null && allowedExtensions.Count > 0)
				{
					var normalizedExtensions = allowedExtensions
						.Select(e => e.Trim().ToLowerInvariant())
						.Select(e => e.StartsWith(".") ? e : "." + e)
						.ToArray();

					extensionFilterSet = normalizedExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);

					if (normalizedExtensions.Length == 1)
					{
						string singleExt = normalizedExtensions[0];
						query = query.Where(x => x.Extension == singleExt);
					}
					else
					{
						var bsonExtensions = normalizedExtensions.Select(e => new BsonValue(e)).ToArray();
						query = query.Where(Query.In("Extension", bsonExtensions));
					}
				}

				if (requireRealmMetadata)
				{
					query = query.Where(x => x.HasRealmMetadata);
				}

				if (!string.IsNullOrWhiteSpace(requiredAssetType))
				{
					query = query.Where(x => x.AssetType == requiredAssetType);
				}

				if (requirePlayerColorMask)
				{
					query = query.Where(x => x.HasPlayerColorMask);
				}

				var candidateList = query.ToList();

				if (extensionFilterSet != null)
				{
					candidateList = candidateList.Where(x => extensionFilterSet.Contains(x.Extension)).ToList();
				}

				List<IndexedAsset> filtered;
				if (string.IsNullOrWhiteSpace(searchTerm))
				{
					filtered = candidateList.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToList();
				}
				else
				{
					string queryTerm = searchTerm.Trim().ToLowerInvariant();
					filtered = candidateList
						.Where(asset =>
							(asset.Tags != null && asset.Tags.Any(t => t.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase) >= 0)) ||
							asset.FileName.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase) >= 0 ||
							asset.FilePath.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase) >= 0 ||
							(!string.IsNullOrEmpty(asset.MapName) && asset.MapName.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase) >= 0))
						.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
						.ToList();
				}

				var seenBlake3 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var deduplicated = new List<IndexedAsset>();
				foreach (var item in filtered)
				{
					string blake3 = GetCanonicalBlake3(item);
					if (string.IsNullOrEmpty(blake3) || seenBlake3.Add(blake3))
					{
						deduplicated.Add(item);
					}
				}

				return deduplicated;
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetIndexService] SearchAssets error: {ex.Message}");
			return new List<IndexedAsset>();
		}
	}

	private static string NormalizePath(string path)
	{
		return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
	}

	public void Dispose()
	{
		lock (_p2pSyncLock)
		{
			_p2pDatabase?.Dispose();
			_p2pDatabase = null;
		}
		_database.Dispose();
	}
}
