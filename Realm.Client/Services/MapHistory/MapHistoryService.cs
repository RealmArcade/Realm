using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Utils;
using Realm.Shared.Metadata;

namespace Realm.Client.Services.MapHistory;

public class MapHistoryService
{
	private const string AuthorName = "Realm Map Editor";
	private const string AuthorEmail = "map-editor@realm.local";
	private const string HistoryIndexDirectoryGodotPath = "user://map_history";
	private const string BackupOriginRootGodotPath = "user://git_snapshots";
	private const int MaximumListedVersions = 400;

	private static readonly string[] ExcludedDirectoryNames =
	{
		".git", ".vs", ".idea", ".vscode", ".godot", ".dotnet", ".wasi", ".sidecarcache", ".cache",
		"bin", "obj", "lib", "map_backups", "map_upgrades"
	};

	private static readonly string[] ExcludedFileSuffixes =
	{
		".rmap", ".tmp", ".log", ".bak", ".elf", ".jit.elf", ".wasm", ".dll", ".pdb", ".pem", ".rkey", ".user", ".suo"
	};

	private static readonly string GeneratedIgnoreFileContent = string.Join('\n', new[]
	{
		"# Files the map editor never stores in a version.",
		".git/",
		".vs/",
		".idea/",
		".vscode/",
		".godot/",
		".dotnet/",
		".wasi/",
		".sidecarcache/",
		".cache/",
		"bin/",
		"obj/",
		"lib/",
		"map_backups/",
		"map_upgrades/",
		"*.rmap",
		"*.tmp",
		"*.log",
		"*.bak",
		"*.elf",
		"*.wasm",
		"*.dll",
		"*.pdb",
		"*.pem",
		"*.rkey",
		"*~",
		"WasmEntryPoint.cs",
		string.Empty
	});

	private readonly object _queueLock = new();
	private Task _queueTail = Task.CompletedTask;
	private bool _versionRequestPending;
	private volatile MapHistoryStatus _cachedStatus = MapHistoryStatus.Unavailable("No map is open.");

	public MapHistoryStatus CachedStatus => _cachedStatus;

	public static string GetBackupOriginRootPath()
	{
		return ProjectSettings.GlobalizePath(BackupOriginRootGodotPath);
	}

	public static string GetBackupOriginPath(string mapGuid)
	{
		return Path.Combine(ProjectSettings.GlobalizePath(BackupOriginRootGodotPath), mapGuid);
	}

	public bool HasBackupOrigin(string mapGuid)
	{
		if (string.IsNullOrEmpty(mapGuid)) return false;
		string originPath = GetBackupOriginPath(mapGuid);
		return Directory.Exists(originPath) && File.Exists(Path.Combine(originPath, "objects"));
	}

	public void PrimeForMap(string mapDirectory, Action<MapHistoryStatus> onCompleted = null)
	{
		Enqueue(() =>
		{
			MapHistoryStatus status;
			try
			{
				if (File.Exists(Path.Combine(mapDirectory, "metadata.json")))
				{
					MapGitRepository repository = EnsureRepository(mapDirectory);
					if (!repository.ReadHead().HasCommit)
					{
						CreateVersionInternal(mapDirectory, MapVersionKind.Initial, "Initial version");
					}
				}

				status = ReadOrCreateStatus(mapDirectory);
			}
			catch (Exception exception)
			{
				status = MapHistoryStatus.Unavailable(exception.Message);
			}

			_cachedStatus = status;
			onCompleted?.Invoke(status);
		});
	}

	public void CreateVersion(string mapDirectory, MapVersionKind kind, string? label, Action<MapHistoryVersionResult> onCompleted = null, bool coalesce = false)
	{
		Enqueue(() =>
		{
			MapHistoryVersionResult result;
			try
			{
				result = CreateVersionInternal(mapDirectory, kind, label);
			}
			catch (Exception exception)
			{
				result = new MapHistoryVersionResult { Success = false, ErrorMessage = exception.Message };
			}

			UpdateCachedStatusFromIndex(mapDirectory);
			onCompleted?.Invoke(result);
		}, coalesce);
	}

	public Task<MapHistoryVersionResult> CreateVersionAsync(string mapDirectory, MapVersionKind kind, string? label)
	{
		var completion = new TaskCompletionSource<MapHistoryVersionResult>();
		CreateVersion(mapDirectory, kind, label, result => completion.TrySetResult(result));
		return completion.Task;
	}

	public void ReadVersions(string mapDirectory, Action<IReadOnlyList<MapVersionEntry>> onCompleted)
	{
		Enqueue(() =>
		{
			var versions = new List<MapVersionEntry>();
			try
			{
				var repository = new MapGitRepository(mapDirectory);
				if (repository.Exists)
				{
					MapHistoryIndexFile index = LoadIndex(mapDirectory);
					foreach (MapGitCommitInfo commit in repository.ReadVersionHistory(MaximumListedVersions))
					{
						versions.Add(ToVersionEntry(commit, index));
					}
				}
			}
			catch (Exception exception)
			{
				GD.PrintErr($"[MapHistoryService] Could not read versions: {exception.Message}");
			}

			onCompleted?.Invoke(versions);
		});
	}

	public void ReadStatus(string mapDirectory, Action<MapHistoryStatus> onCompleted)
	{
		Enqueue(() =>
		{
			MapHistoryStatus status;
			try
			{
				status = ReadOrCreateStatus(mapDirectory);
			}
			catch (Exception exception)
			{
				status = MapHistoryStatus.Unavailable(exception.Message);
			}

			_cachedStatus = status;
			onCompleted?.Invoke(status);
		});
	}

	public void RestoreVersion(string mapDirectory, string commitId, Action<MapHistoryRestoreResult> onCompleted)
	{
		Enqueue(() =>
		{
			MapHistoryRestoreResult result;
			try
			{
				result = RestoreVersionInternal(mapDirectory, commitId);
			}
			catch (Exception exception)
			{
				result = new MapHistoryRestoreResult { Success = false, ErrorMessage = exception.Message };
			}

			UpdateCachedStatusFromIndex(mapDirectory);
			onCompleted?.Invoke(result);
		});
	}

	public void BackUpNow(string mapDirectory, Action<MapHistoryStatus> onCompleted = null)
	{
		Enqueue(() =>
		{
			try
			{
				MapHistoryIndexFile index = LoadIndex(mapDirectory);
				if (!string.IsNullOrEmpty(index.MapGuid))
				{
					var repository = new MapGitRepository(mapDirectory);
					if (repository.Exists)
					{
						PushBackupOrigin(repository, index);
						SaveIndex(mapDirectory, index);
					}
				}
			}
			catch (Exception exception)
			{
				GD.PrintErr($"[MapHistoryService] Backup failed: {exception.Message}");
			}

			onCompleted?.Invoke(_cachedStatus);
		});
	}

	public void RecoverFromOrigin(string mapGuid, string destinationDirectory, Action<MapHistoryRecoveryResult> onCompleted)
	{
		Enqueue(() =>
		{
			MapHistoryRecoveryResult result;
			try
			{
				result = RecoverFromOriginInternal(mapGuid, destinationDirectory);
			}
			catch (Exception exception)
			{
				result = new MapHistoryRecoveryResult { Success = false, ErrorMessage = exception.Message };
			}

			onCompleted?.Invoke(result);
		});
	}

	public IReadOnlyList<(string MapGuid, string FolderName, DateTimeOffset LastBackupAt)> ListBackupOrigins()
	{
		var origins = new List<(string MapGuid, string FolderName, DateTimeOffset LastBackupAt)>();
		string root = GetBackupOriginRootPath();
		if (!Directory.Exists(root)) return origins;

		foreach (string directory in Directory.GetDirectories(root))
		{
			string mapGuid = Path.GetFileName(directory);
			if (mapGuid.Length != 32) continue;

			string originIndexPath = Path.Combine(root, mapGuid + ".json");
			DateTimeOffset lastBackupAt = DateTimeOffset.MinValue;
			string folderName = mapGuid;
			try
			{
				if (File.Exists(originIndexPath))
				{
					var originRecord = JsonSerializer.Deserialize<MapBackupOriginRecord>(File.ReadAllText(originIndexPath));
					if (originRecord != null)
					{
						folderName = string.IsNullOrEmpty(originRecord.FolderName) ? mapGuid : originRecord.FolderName;
						lastBackupAt = originRecord.LastBackupAt;
					}
				}

				if (lastBackupAt == DateTimeOffset.MinValue)
				{
					lastBackupAt = Directory.GetLastWriteTimeUtc(directory);
				}
			}
			catch (Exception)
			{
			}

			origins.Add((mapGuid, folderName, lastBackupAt));
		}

		return origins;
	}

	private void Enqueue(Action action, bool coalesce = false)
	{
		lock (_queueLock)
		{
			if (coalesce && _versionRequestPending) return;
			if (coalesce) _versionRequestPending = true;

			_queueTail = _queueTail.ContinueWith(_ =>
			{
				try
				{
					action();
				}
				catch (Exception exception)
				{
					GD.PrintErr($"[MapHistoryService] History operation failed: {exception}");
				}
				finally
				{
					if (coalesce)
					{
						lock (_queueLock)
						{
							_versionRequestPending = false;
						}
					}
				}
			}, TaskScheduler.Default);
		}
	}

	private MapHistoryStatus ReadOrCreateStatus(string mapDirectory)
	{
		string metadataPath = Path.Combine(mapDirectory, "metadata.json");
		if (!File.Exists(metadataPath))
		{
			return MapHistoryStatus.Unavailable("This folder has no metadata.json yet.");
		}

		var repository = new MapGitRepository(mapDirectory);
		if (!repository.Exists)
		{
			return new MapHistoryStatus { IsRepositoryReady = false };
		}

		MapHistoryIndexFile index = LoadIndex(mapDirectory);
		MapVersionEntry? latestVersion = ReadLatestVersion(repository, index);
		DateTimeOffset? lastBackupAt = null;
		if (!string.IsNullOrEmpty(index.MapGuid) && HasBackupOrigin(index.MapGuid))
		{
			lastBackupAt = index.LastBackupAt == default ? null : index.LastBackupAt;
		}

		return new MapHistoryStatus
		{
			IsRepositoryReady = true,
			LastVersionCommitId = latestVersion?.CommitId,
			LastVersionAt = latestVersion?.CreatedAt,
			LastVersionKind = latestVersion?.Kind,
			LastBackupAt = lastBackupAt,
			BackupAvailable = !string.IsNullOrEmpty(index.MapGuid) && HasBackupOrigin(index.MapGuid)
		};
	}

	private void UpdateCachedStatusFromIndex(string mapDirectory)
	{
		try
		{
			_cachedStatus = ReadOrCreateStatus(mapDirectory);
		}
		catch (Exception exception)
		{
			_cachedStatus = MapHistoryStatus.Unavailable(exception.Message);
		}
	}

	private static MapVersionEntry? ReadLatestVersion(MapGitRepository repository, MapHistoryIndexFile index)
	{
		string? latestCommitId = index.LastCommitId;
		if (string.IsNullOrEmpty(latestCommitId))
		{
			return null;
		}

		if (!repository.TryReadCommit(latestCommitId, out MapGitCommitInfo commit))
		{
			return null;
		}

		return ToVersionEntry(commit, index);
	}

	private static MapVersionEntry ToVersionEntry(MapGitCommitInfo commit, MapHistoryIndexFile index)
	{
		MapHistoryIndexEntry? indexEntry = index.Entries.FirstOrDefault(entry =>
			string.Equals(entry.CommitId, commit.Id, StringComparison.OrdinalIgnoreCase));

		return new MapVersionEntry
		{
			CommitId = commit.Id,
			CreatedAt = commit.CommittedAt,
			Kind = indexEntry?.Kind ?? MapVersionKind.ExternalChange,
			Label = indexEntry?.Label,
			SaveVersion = indexEntry?.SaveVersion ?? 0,
			ChangedFileCount = indexEntry?.ChangedFileCount ?? 0,
			ChangedBytes = indexEntry?.ChangedBytes ?? 0,
			ReferenceName = indexEntry?.ReferenceName
		};
	}

	private MapGitRepository EnsureRepository(string mapDirectory)
	{
		var repository = new MapGitRepository(mapDirectory);
		if (repository.Exists)
		{
			return repository;
		}

		repository.Initialize();

		string ignorePath = Path.Combine(mapDirectory, ".gitignore");
		if (!File.Exists(ignorePath))
		{
			File.WriteAllText(ignorePath, GeneratedIgnoreFileContent);
		}

		return repository;
	}

	private MapHistoryVersionResult CreateVersionInternal(string mapDirectory, MapVersionKind kind, string? label)
	{
		string metadataPath = Path.Combine(mapDirectory, "metadata.json");
		if (!File.Exists(metadataPath))
		{
			return new MapHistoryVersionResult { Success = false, ErrorMessage = "This folder has no metadata.json yet." };
		}

		bool incrementsSaveVersion = kind is MapVersionKind.ManualSave or MapVersionKind.SaveAs or MapVersionKind.AutosaveCheckpoint or MapVersionKind.BeforePublish;
		EnsureMapIdentity(mapDirectory, incrementsSaveVersion);
		MapHistoryIndexFile index = LoadIndex(mapDirectory);
		MapGitRepository repository = EnsureRepository(mapDirectory);

		MapGitHeadState head = repository.ReadHead();
		MapGitIgnoreRules ignoreRules = MapGitIgnoreRules.LoadFromDirectory(mapDirectory);
		List<string> workingFiles = repository.CollectWorkingTreeFiles(path => IsExcludedFromHistory(path, ignoreRules));

		var blobIdByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var fingerprints = new Dictionary<string, MapHistoryFileFingerprint>(StringComparer.OrdinalIgnoreCase);
		long writtenBytes = 0;

		foreach (string relativePath in workingFiles)
		{
			string absolutePath = Path.Combine(mapDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
			var fileInfo = new FileInfo(absolutePath);
			if (!fileInfo.Exists) continue;

			if (index.Files.TryGetValue(relativePath, out MapHistoryFileFingerprint existing) &&
			    existing.Size == fileInfo.Length &&
			    existing.LastWriteTicks == fileInfo.LastWriteTimeUtc.Ticks &&
			    repository.Objects.Exists(existing.BlobId))
			{
				blobIdByPath[relativePath] = existing.BlobId;
				fingerprints[relativePath] = existing;
				continue;
			}

			byte[] content;
			try
			{
				content = File.ReadAllBytes(absolutePath);
			}
			catch (IOException)
			{
				continue;
			}

			string blobId = MapGitObjectDatabase.ComputeObjectId(MapGitObjectType.Blob, content);
			if (!repository.Objects.Exists(blobId))
			{
				repository.WriteBlob(content);
				writtenBytes += content.Length;
			}

			blobIdByPath[relativePath] = blobId;
			fingerprints[relativePath] = new MapHistoryFileFingerprint
			{
				BlobId = blobId,
				Size = fileInfo.Length,
				LastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks
			};
		}

		var parentFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrEmpty(head.CommitId) && repository.TryReadCommit(head.CommitId, out MapGitCommitInfo parentCommit))
		{
			parentFiles = repository.ReadTreeFiles(parentCommit.TreeId);
		}

		int changedFileCount = CountChangedPaths(parentFiles, blobIdByPath);
		if (!string.IsNullOrEmpty(head.CommitId) && changedFileCount == 0)
		{
			UpdateManifest(index, fingerprints);
			SaveIndex(mapDirectory, index);
			return new MapHistoryVersionResult { Success = true, NothingToCommit = true };
		}

		string commitMessage = string.IsNullOrEmpty(label) ? GetDefaultCommitMessage(kind) : label;
		var signature = new MapGitSignature(AuthorName, AuthorEmail, DateTimeOffset.Now);
		string commitId = repository.WriteCommit(blobIdByPath, head.CommitId, signature, commitMessage);

		repository.PointHeadAt(commitId);
		repository.WriteReference(MapGitRepository.SafetyReferenceName, commitId);
		repository.WriteIndex(blobIdByPath);

		var versionEntry = new MapHistoryIndexEntry
		{
			CommitId = commitId,
			CreatedAt = signature.When,
			Kind = kind,
			Label = label,
			SaveVersion = index.SaveVersion,
			ChangedFileCount = changedFileCount,
			ChangedBytes = writtenBytes,
			ReferenceName = head.IsDetached ? null : head.BranchName
		};

		index.Entries.Add(versionEntry);
		index.LastCommitId = commitId;
		index.LastBackupAt = default;
		UpdateManifest(index, fingerprints);
		SaveIndex(mapDirectory, index);

		PushBackupOrigin(repository, index);
		SaveIndex(mapDirectory, index);

		return new MapHistoryVersionResult
		{
			Success = true,
			Entry = ToVersionEntry(new MapGitCommitInfo
			{
				Id = commitId,
				CommittedAt = signature.When,
				Message = commitMessage
			}, index)
		};
	}

	private static int CountChangedPaths(Dictionary<string, string> previousFiles, Dictionary<string, string> currentFiles)
	{
		int changedCount = 0;
		foreach (var pair in currentFiles)
		{
			if (!previousFiles.TryGetValue(pair.Key, out string? previousBlobId) ||
			    !string.Equals(previousBlobId, pair.Value, StringComparison.OrdinalIgnoreCase))
			{
				changedCount++;
			}
		}

		foreach (string previousPath in previousFiles.Keys)
		{
			if (!currentFiles.ContainsKey(previousPath))
			{
				changedCount++;
			}
		}

		return changedCount;
	}

	private static void UpdateManifest(MapHistoryIndexFile index, Dictionary<string, MapHistoryFileFingerprint> fingerprints)
	{
		index.Files = new Dictionary<string, MapHistoryFileFingerprint>(fingerprints, StringComparer.OrdinalIgnoreCase);
	}

	private MapHistoryRestoreResult RestoreVersionInternal(string mapDirectory, string commitId)
	{
		var repository = new MapGitRepository(mapDirectory);
		if (!repository.Exists)
		{
			return new MapHistoryRestoreResult { Success = false, ErrorMessage = "This map has no versions yet." };
		}

		if (!repository.TryReadCommit(commitId, out MapGitCommitInfo targetCommit))
		{
			return new MapHistoryRestoreResult { Success = false, ErrorMessage = "That version can no longer be read." };
		}

		MapHistoryVersionResult beforeRestoreVersion = CreateVersionInternal(mapDirectory, MapVersionKind.BeforeRestore, "Before restore");
		if (!beforeRestoreVersion.Success)
		{
			return new MapHistoryRestoreResult { Success = false, ErrorMessage = beforeRestoreVersion.ErrorMessage };
		}

		Dictionary<string, string> targetFiles = repository.ReadTreeFiles(targetCommit.TreeId);
		MapGitHeadState headAfterSnapshot = repository.ReadHead();
		var currentFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrEmpty(headAfterSnapshot.CommitId) && repository.TryReadCommit(headAfterSnapshot.CommitId, out MapGitCommitInfo currentCommit))
		{
			currentFiles = repository.ReadTreeFiles(currentCommit.TreeId);
		}

		int filesWritten = 0;
		foreach (var pair in targetFiles)
		{
			string absolutePath = Path.Combine(mapDirectory, pair.Key.Replace('/', Path.DirectorySeparatorChar));
			if (!repository.Objects.TryRead(pair.Value, out MapGitObjectType objectType, out byte[] content) || objectType != MapGitObjectType.Blob)
			{
				continue;
			}

			if (File.Exists(absolutePath) && FileLengthMatches(absolutePath, content.Length) && FileContentMatches(absolutePath, content))
			{
				continue;
			}

			WriteFileAtomically(absolutePath, content);
			filesWritten++;
		}

		int filesRemoved = 0;
		foreach (string currentPath in currentFiles.Keys)
		{
			if (targetFiles.ContainsKey(currentPath)) continue;
			if (IsProtectedFromRestoreRemoval(currentPath)) continue;

			string absolutePath = Path.Combine(mapDirectory, currentPath.Replace('/', Path.DirectorySeparatorChar));
			if (!File.Exists(absolutePath)) continue;

			try
			{
				ClearReadOnlyAttribute(absolutePath);
				File.Delete(absolutePath);
				filesRemoved++;
			}
			catch (IOException exception)
			{
				GD.PrintErr($"[MapHistoryService] Could not remove '{currentPath}' during restore: {exception.Message}");
			}
		}

		bool terrainRestored = filesWritten > 0 && targetFiles.ContainsKey("terrain.json");
		bool metadataRestored = targetFiles.ContainsKey("metadata.json") && filesWritten > 0;

		MapHistoryVersionResult restoredVersion = CreateVersionInternal(
			mapDirectory,
			MapVersionKind.RestoredVersion,
			$"Restored version from {targetCommit.CommittedAt:yyyy-MM-dd HH:mm}");

		LoadIdentityFromDisk(mapDirectory);

		return new MapHistoryRestoreResult
		{
			Success = restoredVersion.Success,
			ErrorMessage = restoredVersion.ErrorMessage,
			NewVersion = restoredVersion.Entry,
			FilesWritten = filesWritten,
			FilesRemoved = filesRemoved,
			TerrainRestored = terrainRestored,
			MetadataRestored = metadataRestored
		};
	}

	private static bool IsProtectedFromRestoreRemoval(string relativePath)
	{
		string fileName = Path.GetFileName(relativePath);
		return fileName.Equals(".gitignore", StringComparison.OrdinalIgnoreCase) ||
		       fileName.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase);
	}

	private static bool FileLengthMatches(string path, int length)
	{
		try
		{
			return new FileInfo(path).Length == length;
		}
		catch (IOException)
		{
			return false;
		}
	}

	private static bool FileContentMatches(string path, byte[] content)
	{
		try
		{
			byte[] existing = File.ReadAllBytes(path);
			return existing.AsSpan().SequenceEqual(content);
		}
		catch (IOException)
		{
			return false;
		}
	}

	private static void WriteFileAtomically(string absolutePath, byte[] content)
	{
		string directory = Path.GetDirectoryName(absolutePath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		string temporaryPath = absolutePath + ".realm-restore.tmp";
		try
		{
			File.WriteAllBytes(temporaryPath, content);
			if (File.Exists(absolutePath))
			{
				ClearReadOnlyAttribute(absolutePath);
			}

			File.Move(temporaryPath, absolutePath, true);
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				try
				{
					File.Delete(temporaryPath);
				}
				catch (IOException)
				{
				}
			}
		}
	}

	private static void ClearReadOnlyAttribute(string absolutePath)
	{
		try
		{
			FileAttributes attributes = File.GetAttributes(absolutePath);
			if ((attributes & FileAttributes.ReadOnly) != 0)
			{
				File.SetAttributes(absolutePath, attributes & ~FileAttributes.ReadOnly);
			}
		}
		catch (Exception)
		{
		}
	}

	private void PushBackupOrigin(MapGitRepository repository, MapHistoryIndexFile index)
	{
		if (string.IsNullOrEmpty(index.MapGuid)) return;

		try
		{
			repository.PushToBareRepository(GetBackupOriginPath(index.MapGuid));
			index.LastBackupAt = DateTimeOffset.Now;
			WriteBackupOriginRecord(index);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MapHistoryService] Could not update the local backup: {exception.Message}");
		}
	}

	private static void WriteBackupOriginRecord(MapHistoryIndexFile index)
	{
		try
		{
			string originRoot = GetBackupOriginRootPath();
			Directory.CreateDirectory(originRoot);

			string recordPath = Path.Combine(originRoot, index.MapGuid + ".json");
			var record = new MapBackupOriginRecord
			{
				MapGuid = index.MapGuid,
				FolderName = string.IsNullOrEmpty(index.FolderName) ? index.MapGuid : index.FolderName,
				LastBackupAt = index.LastBackupAt,
				SaveVersion = index.SaveVersion
			};

			File.WriteAllText(recordPath, JsonSerializer.Serialize(record, JsonOptions));
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MapHistoryService] Could not record the backup origin: {exception.Message}");
		}
	}

	private MapHistoryRecoveryResult RecoverFromOriginInternal(string mapGuid, string destinationDirectory)
	{
		string originPath = GetBackupOriginPath(mapGuid);
		if (!Directory.Exists(originPath))
		{
			return new MapHistoryRecoveryResult { Success = false, ErrorMessage = "No backup was found for this map." };
		}

		string latestCommitId = ReadBareRepositoryReference(originPath);
		if (string.IsNullOrEmpty(latestCommitId))
		{
			return new MapHistoryRecoveryResult { Success = false, ErrorMessage = "The backup holds no versions." };
		}

		Directory.CreateDirectory(destinationDirectory);
		var destinationRepository = new MapGitRepository(destinationDirectory);
		destinationRepository.Initialize();

		var originObjects = new MapGitObjectDatabase(originPath);
		originObjects.CopyMissingObjectsTo(destinationRepository.Objects);

		if (!destinationRepository.TryReadCommit(latestCommitId, out MapGitCommitInfo latestCommit))
		{
			return new MapHistoryRecoveryResult { Success = false, ErrorMessage = "The backup version could not be read." };
		}

		destinationRepository.WriteReference($"refs/heads/{MapGitRepository.DefaultBranchName}", latestCommitId);
		string headPath = Path.Combine(destinationRepository.GitDirectory, "HEAD");
		File.WriteAllText(headPath, $"ref: refs/heads/{MapGitRepository.DefaultBranchName}\n");
		destinationRepository.WriteReference(MapGitRepository.SafetyReferenceName, latestCommitId);

		Dictionary<string, string> files = destinationRepository.ReadTreeFiles(latestCommit.TreeId);
		foreach (var pair in files)
		{
			string absolutePath = Path.Combine(destinationDirectory, pair.Key.Replace('/', Path.DirectorySeparatorChar));
			WriteFileAtomically(absolutePath, destinationRepository.ReadBlob(pair.Value));
		}

		string ignorePath = Path.Combine(destinationDirectory, ".gitignore");
		if (!File.Exists(ignorePath))
		{
			File.WriteAllText(ignorePath, GeneratedIgnoreFileContent);
		}

		int versionCount = destinationRepository.ReadVersionHistory(MaximumListedVersions).Count;

		destinationRepository.WriteIndex(files);

		if (File.Exists(Path.Combine(destinationDirectory, "metadata.json")))
		{
			EnsureMapIdentity(destinationDirectory, incrementSaveVersion: false);
		}

		return new MapHistoryRecoveryResult
		{
			Success = true,
			RecoveredMapDirectory = destinationDirectory,
			RecoveredVersionCount = versionCount
		};
	}

	private static string? ReadBareRepositoryReference(string originPath)
	{
		string mainReferencePath = Path.Combine(originPath, "refs", "heads", MapGitRepository.DefaultBranchName);
		if (File.Exists(mainReferencePath))
		{
			string content = File.ReadAllText(mainReferencePath).Trim();
			if (content.Length == 40) return content;
		}

		string safetyReferencePath = Path.Combine(originPath, MapGitRepository.SafetyReferenceName.Replace('/', Path.DirectorySeparatorChar));
		if (File.Exists(safetyReferencePath))
		{
			string content = File.ReadAllText(safetyReferencePath).Trim();
			if (content.Length == 40) return content;
		}

		return null;
	}

	private void EnsureMapIdentity(string mapDirectory, bool incrementSaveVersion)
	{
		MapHistoryIndexFile index = LoadIndex(mapDirectory);
		bool indexChanged = false;

		string metadataPath = Path.Combine(mapDirectory, "metadata.json");
		System.Text.Json.Nodes.JsonObject metadataRoot;
		try
		{
			metadataRoot = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(metadataPath))?.AsObject()
				?? new System.Text.Json.Nodes.JsonObject();
		}
		catch (Exception exception)
		{
			throw new InvalidOperationException($"metadata.json could not be read: {exception.Message}");
		}

		bool metadataChanged = false;

		string? mapGuid = metadataRoot["MapGUID"]?.GetValue<string>();
		if (string.IsNullOrWhiteSpace(mapGuid) || mapGuid.Length != 32)
		{
			mapGuid = !string.IsNullOrEmpty(index.MapGuid) ? index.MapGuid : Guid.NewGuid().ToString("N");
			metadataRoot["MapGUID"] = mapGuid;
			metadataChanged = true;
		}

		long saveVersion = 0;
		if (metadataRoot["SaveVersion"] is System.Text.Json.Nodes.JsonValue saveVersionNode &&
		    saveVersionNode.TryGetValue(out long parsedSaveVersion))
		{
			saveVersion = parsedSaveVersion;
		}

		if (incrementSaveVersion && saveVersion < long.MaxValue)
		{
			saveVersion++;
			metadataRoot["SaveVersion"] = saveVersion;
			metadataChanged = true;
		}

		if (metadataChanged)
		{
			EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
			MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);
		}

		if (!string.Equals(index.MapGuid, mapGuid, StringComparison.OrdinalIgnoreCase))
		{
			index.MapGuid = mapGuid;
			indexChanged = true;
		}

		if (index.SaveVersion != saveVersion)
		{
			index.SaveVersion = saveVersion;
			indexChanged = true;
		}

		string folderName = Path.GetFileName(mapDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (!string.Equals(index.FolderName, folderName, StringComparison.Ordinal))
		{
			index.FolderName = folderName;
			indexChanged = true;
		}

		if (indexChanged)
		{
			SaveIndex(mapDirectory, index);
		}
	}

	private void LoadIdentityFromDisk(string mapDirectory)
	{
		MapHistoryIndexFile index = LoadIndex(mapDirectory);
		string metadataPath = Path.Combine(mapDirectory, "metadata.json");
		try
		{
			var metadataRoot = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(metadataPath))?.AsObject();
			string? mapGuid = metadataRoot?["MapGUID"]?.GetValue<string>();
			if (!string.IsNullOrEmpty(mapGuid) && !string.Equals(index.MapGuid, mapGuid, StringComparison.OrdinalIgnoreCase))
			{
				index.MapGuid = mapGuid;
				SaveIndex(mapDirectory, index);
			}
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MapHistoryService] Could not reload the map identity: {exception.Message}");
		}
	}

	private static bool IsExcludedFromHistory(string relativePath, MapGitIgnoreRules ignoreRules)
	{
		string normalized = relativePath.Replace('\\', '/');
		if (string.IsNullOrWhiteSpace(normalized)) return true;
		if (MapWorkspaceService.IsIgnoredPathDomain(normalized)) return true;

		bool isDirectory = normalized.EndsWith('/');
		string trimmed = normalized.TrimEnd('/');

		foreach (string directoryName in ExcludedDirectoryNames)
		{
			if (trimmed.Equals(directoryName, StringComparison.OrdinalIgnoreCase)) return true;
			if (trimmed.StartsWith(directoryName + "/", StringComparison.OrdinalIgnoreCase)) return true;
			if (trimmed.Contains("/" + directoryName + "/", StringComparison.OrdinalIgnoreCase)) return true;
		}

		if (!isDirectory)
		{
			foreach (string suffix in ExcludedFileSuffixes)
			{
				if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
			}

			string fileName = Path.GetFileName(trimmed);
			if (fileName.StartsWith("~", StringComparison.Ordinal)) return true;
			if (fileName.Equals("WasmEntryPoint.cs", StringComparison.OrdinalIgnoreCase)) return true;
		}

		return ignoreRules.IsIgnored(trimmed, isDirectory);
	}

	private static string GetDefaultCommitMessage(MapVersionKind kind)
	{
		return kind switch
		{
			MapVersionKind.Initial => "Initial version",
			MapVersionKind.ManualSave => "Save point",
			MapVersionKind.SaveAs => "Save point (save as)",
			MapVersionKind.AutosaveCheckpoint => "Autosave checkpoint",
			MapVersionKind.BeforePublish => "Before publish",
			MapVersionKind.BeforeMapUpgrade => "Before map upgrade",
			MapVersionKind.BeforeRestore => "Before restore",
			MapVersionKind.RestoredVersion => "Restored version",
			_ => "Map change"
		};
	}

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private static MapHistoryIndexFile LoadIndex(string mapDirectory)
	{
		string directory = ProjectSettings.GlobalizePath(HistoryIndexDirectoryGodotPath);
		string folderKey = MapHistoryIndexFile.BuildFolderKey(mapDirectory);
		string folderScopedPath = Path.Combine(directory, folderKey + ".json");

		MapHistoryIndexFile? index = TryReadIndexFile(folderScopedPath);
		if (index == null || string.IsNullOrEmpty(index.MapGuid))
		{
			string? mapGuid = TryReadMapGuid(Path.Combine(mapDirectory, "metadata.json"));
			if (!string.IsNullOrEmpty(mapGuid))
			{
				string guidPath = Path.Combine(directory, mapGuid + ".json");
				MapHistoryIndexFile? guidIndex = TryReadIndexFile(guidPath);
				if (guidIndex != null)
				{
					guidIndex.FolderKey = folderKey;
					NormalizeIndex(guidIndex);
					return guidIndex;
				}
			}
		}

		if (index != null)
		{
			NormalizeIndex(index);
			return index;
		}

		return new MapHistoryIndexFile
		{
			FolderKey = folderKey,
			MapGuid = TryReadMapGuid(Path.Combine(mapDirectory, "metadata.json")) ?? string.Empty
		};
	}

	private static void NormalizeIndex(MapHistoryIndexFile index)
	{
		index.Entries ??= new List<MapHistoryIndexEntry>();
		index.Files ??= new Dictionary<string, MapHistoryFileFingerprint>(StringComparer.OrdinalIgnoreCase);
		if (index.Files.Comparer != StringComparer.OrdinalIgnoreCase)
		{
			index.Files = new Dictionary<string, MapHistoryFileFingerprint>(index.Files, StringComparer.OrdinalIgnoreCase);
		}
	}

	private static MapHistoryIndexFile? TryReadIndexFile(string path)
	{
		try
		{
			if (!File.Exists(path)) return null;
			return JsonSerializer.Deserialize<MapHistoryIndexFile>(File.ReadAllText(path), JsonOptions);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static string? TryReadMapGuid(string metadataPath)
	{
		try
		{
			if (!File.Exists(metadataPath)) return null;
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(metadataPath));
			if (document.RootElement.TryGetProperty("MapGUID", out JsonElement mapGuidElement) &&
			    mapGuidElement.ValueKind == JsonValueKind.String)
			{
				string? mapGuid = mapGuidElement.GetString();
				return string.IsNullOrWhiteSpace(mapGuid) ? null : mapGuid.Trim();
			}
		}
		catch (Exception)
		{
		}

		return null;
	}

	private static void SaveIndex(string mapDirectory, MapHistoryIndexFile index)
	{
		string directory = ProjectSettings.GlobalizePath(HistoryIndexDirectoryGodotPath);
		Directory.CreateDirectory(directory);

		index.FolderKey = MapHistoryIndexFile.BuildFolderKey(mapDirectory);
		string path = !string.IsNullOrEmpty(index.MapGuid)
			? Path.Combine(directory, index.MapGuid + ".json")
			: Path.Combine(directory, index.FolderKey + ".json");

		string temporaryPath = path + ".tmp";
		File.WriteAllText(temporaryPath, JsonSerializer.Serialize(index, JsonOptions));
		File.Move(temporaryPath, path, true);

		string folderScopedPath = Path.Combine(directory, index.FolderKey + ".json");
		if (!string.Equals(path, folderScopedPath, StringComparison.OrdinalIgnoreCase))
		{
			File.WriteAllText(folderScopedPath, JsonSerializer.Serialize(index, JsonOptions));
		}
	}

	public class MapHistoryIndexFile
	{
		public string FolderKey { get; set; } = string.Empty;
		public string MapGuid { get; set; } = string.Empty;
		public string FolderName { get; set; } = string.Empty;
		public long SaveVersion { get; set; }
		public string? LastCommitId { get; set; }
		public DateTimeOffset LastBackupAt { get; set; }
		public List<MapHistoryIndexEntry> Entries { get; set; } = new();
		public Dictionary<string, MapHistoryFileFingerprint> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);

		public static string BuildFolderKey(string mapDirectory)
		{
			string normalized = Path.GetFullPath(mapDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
			byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
			return Convert.ToHexStringLower(hash)[..24];
		}
	}

	public class MapHistoryIndexEntry
	{
		public string CommitId { get; set; } = string.Empty;
		public DateTimeOffset CreatedAt { get; set; }
		public MapVersionKind Kind { get; set; }
		public string? Label { get; set; }
		public long SaveVersion { get; set; }
		public int ChangedFileCount { get; set; }
		public long ChangedBytes { get; set; }
		public string? ReferenceName { get; set; }
	}

	public class MapHistoryFileFingerprint
	{
		public string BlobId { get; set; } = string.Empty;
		public long Size { get; set; }
		public long LastWriteTicks { get; set; }
	}

	private class MapBackupOriginRecord
	{
		public string MapGuid { get; set; } = string.Empty;
		public string FolderName { get; set; } = string.Empty;
		public DateTimeOffset LastBackupAt { get; set; }
		public long SaveVersion { get; set; }
	}
}
