using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using LibGit2Sharp;
using Realm.Client.Utils;
using Realm.Shared.Services;

namespace Realm.Client.Services.MapHistory;

public class MapHistoryService
{
	public const string AuthorName = "Realm Map Editor";
	public const string AuthorEmail = "map-editor@realm.local";

	private static readonly TimeSpan SynchronousLockTimeout = TimeSpan.FromSeconds(4);

	public static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly object _queueLock = new();
	private Task _queueTail = Task.CompletedTask;
	private bool _versionRequestPending;
	private int _isMaintainingStores;
	private MapHistoryMessages _messages = MapHistoryMessages.Capture();

	private void RefreshMessages()
	{
		_messages = MapHistoryMessages.KeepCurrent(_messages);
	}

	private static bool HasMapFolder(string mapDirectory)
	{
		return !string.IsNullOrWhiteSpace(mapDirectory) && Directory.Exists(mapDirectory);
	}

	public void PrimeForMap(string mapDirectory, Action<MapHistoryStatus>? onCompleted = null)
	{
		RefreshMessages();

		if (!HasMapFolder(mapDirectory))
		{
			onCompleted?.Invoke(MapHistoryStatus.Unavailable(_messages.NoMapFolderOpen));
			return;
		}

		Enqueue(() =>
		{
			MapHistoryStatus status;
			try
			{
				status = PrimeInStore(mapDirectory);
			}
			catch (Exception exception)
			{
				status = MapHistoryStatus.Unavailable(exception.Message);
			}

			onCompleted?.Invoke(status);
		});
	}

	public void CreateVersion(string mapDirectory, MapVersionKind kind, string? label, Action<MapHistoryVersionResult>? onCompleted = null, bool coalesce = false, string? sourceFolder = null)
	{
		RefreshMessages();

		if (!HasMapFolder(mapDirectory))
		{
			onCompleted?.Invoke(new MapHistoryVersionResult { Success = false, ErrorMessage = _messages.NoMapFolderOpen });
			return;
		}

		Enqueue(() => onCompleted?.Invoke(CreateVersionNow(mapDirectory, kind, label, sourceFolder)), coalesce);
	}

	public MapHistoryVersionResult CaptureVersionSynchronously(string mapDirectory, MapVersionKind kind, string? label, string? sourceFolder = null)
	{
		RefreshMessages();

		if (!HasMapFolder(mapDirectory))
		{
			return new MapHistoryVersionResult { Success = false, ErrorMessage = _messages.NoMapFolderOpen };
		}

		return CreateVersionNow(mapDirectory, kind, label, sourceFolder, SynchronousLockTimeout);
	}

	private MapHistoryVersionResult CreateVersionNow(string mapDirectory, MapVersionKind kind, string? label, string? sourceFolder, TimeSpan? lockTimeout = null)
	{
		MapHistoryVersionResult result;
		try
		{
			using MapHistoryStoreSession session = MapHistoryStore.Acquire(mapDirectory, lockTimeout);
			result = CreateVersionInStore(session, mapDirectory, kind, label, sourceFolder);
			if (result.Success)
			{
				HistoryCondensation.TrimHistoryAfterCommit(session);
			}
		}
		catch (Exception exception)
		{
			result = new MapHistoryVersionResult { Success = false, ErrorMessage = DescribeFailure(exception) };
		}

		return result;
	}

	public Task<MapHistoryVersionResult> CreateVersionAsync(string mapDirectory, MapVersionKind kind, string? label, string? sourceFolder = null)
	{
		var completion = new TaskCompletionSource<MapHistoryVersionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		CreateVersion(mapDirectory, kind, label, result => completion.TrySetResult(result), sourceFolder: sourceFolder);
		return completion.Task;
	}

	public void ReadStores(Action<IReadOnlyList<MapHistoryStoreInfo>> onCompleted)
	{
		RefreshMessages();

		Enqueue(() =>
		{
			IReadOnlyList<MapHistoryStoreInfo> stores;
			try
			{
				stores = HistoryStoreDirectory.ReadStoresOnDisk();
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[MapHistoryService] Could not read the stored histories: {exception.Message}");
				stores = Array.Empty<MapHistoryStoreInfo>();
			}

			onCompleted?.Invoke(stores);
		});
	}

	public void DeleteStore(string storeDirectoryPath, Action<MapHistoryStoreDeleteResult> onCompleted)
	{
		RefreshMessages();

		Enqueue(() =>
		{
			MapHistoryStoreDeleteResult result;
			try
			{
				result = HistoryStoreDirectory.DeleteStoreOnDisk(storeDirectoryPath, _messages);
			}
			catch (Exception exception)
			{
				result = new MapHistoryStoreDeleteResult { Success = false, ErrorMessage = DescribeFailure(exception) };
			}

			onCompleted?.Invoke(result);
		});
	}





	public void WaitForQueue(TimeSpan timeout)
	{
		Task queuedWork;
		lock (_queueLock)
		{
			queuedWork = _queueTail;
		}

		try
		{
			queuedWork.Wait(timeout);
		}
		catch (AggregateException exception)
		{
			GameLog.LogError($"[MapHistoryService] A queued history operation did not finish cleanly: {exception.GetBaseException().Message}");
		}
	}

	public bool HasExistingVersions(string mapDirectory)
	{
		RefreshMessages();

		if (!HasMapFolder(mapDirectory)) return false;

		try
		{
			MapHistoryStoreLayout layout = MapHistoryStore.Resolve(mapDirectory);
			if (!MapHistoryStore.RepositoryExists(layout)) return false;

			using MapHistoryStoreSession? session = MapHistoryStore.TryAcquireExisting(layout, TimeSpan.FromMilliseconds(250));
			string repositoryDirectory = session?.Layout.RepositoryDirectory ?? layout.RepositoryDirectory;
			var repository = new MapGitRepository(repositoryDirectory);
			return !string.IsNullOrEmpty(repository.ReadHeadCommitId());
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapHistoryService] Could not read this map's version history: {exception.Message}");
			return false;
		}
	}

	public void ReadVersions(string mapDirectory, Action<IReadOnlyList<MapVersionEntry>> onCompleted)
	{
		RefreshMessages();

		if (!HasMapFolder(mapDirectory))
		{
			onCompleted?.Invoke(Array.Empty<MapVersionEntry>());
			return;
		}

		Enqueue(() =>
		{
			IReadOnlyList<MapVersionEntry> versions = Array.Empty<MapVersionEntry>();
			try
			{
				versions = ReadVersionsInStore(mapDirectory);
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[MapHistoryService] Could not read versions: {exception.Message}");
			}

			onCompleted?.Invoke(versions);
		});
	}

	public void RenameVersion(string mapDirectory, string commitId, string? label, Action<MapHistoryVersionResult>? onCompleted = null)
	{
		RefreshMessages();

		if (!HasMapFolder(mapDirectory))
		{
			onCompleted?.Invoke(new MapHistoryVersionResult { Success = false, ErrorMessage = _messages.NoMapFolderOpen });
			return;
		}

		Enqueue(() =>
		{
			MapHistoryVersionResult result;
			try
			{
				result = RenameVersionInStore(mapDirectory, commitId, label);
			}
			catch (Exception exception)
			{
				result = new MapHistoryVersionResult { Success = false, ErrorMessage = DescribeFailure(exception) };
			}

			onCompleted?.Invoke(result);
		});
	}

	public void ReadStorageUsage(string mapDirectory, Action<MapHistoryStorageInfo> onCompleted)
	{
		RefreshMessages();

		if (!HasMapFolder(mapDirectory))
		{
			onCompleted?.Invoke(new MapHistoryStorageInfo());
			return;
		}

		Enqueue(() =>
		{
			MapHistoryStorageInfo info;
			try
			{
				info = BuildStorageInfo(mapDirectory);
			}
			catch (Exception exception)
			{
				info = new MapHistoryStorageInfo { ErrorMessage = DescribeFailure(exception) };
			}

			onCompleted?.Invoke(info);
		});
	}

	public void ReadStatus(string mapDirectory, Action<MapHistoryStatus> onCompleted)
	{
		RefreshMessages();

		if (!HasMapFolder(mapDirectory))
		{
			onCompleted?.Invoke(MapHistoryStatus.Unavailable(_messages.NoMapFolderOpen));
			return;
		}

		Enqueue(() =>
		{
			MapHistoryStatus status;
			try
			{
				status = ReadStatusInStore(mapDirectory);
			}
			catch (Exception exception)
			{
				status = MapHistoryStatus.Unavailable(DescribeFailure(exception));
			}

			onCompleted?.Invoke(status);
		});
	}

	public void RestoreVersion(string mapDirectory, string commitId, Action<MapHistoryRestoreResult>? onCompleted, Action? beforeRestore = null)
	{
		RefreshMessages();

		if (!HasMapFolder(mapDirectory))
		{
			onCompleted?.Invoke(new MapHistoryRestoreResult { Success = false, ErrorMessage = _messages.NoMapFolderOpen });
			return;
		}

		beforeRestore?.Invoke();

		Enqueue(() =>
		{
			MapHistoryRestoreResult result;
			try
			{
				result = RestoreVersionInStore(mapDirectory, commitId);
			}
			catch (Exception exception)
			{
				result = new MapHistoryRestoreResult { Success = false, ErrorMessage = DescribeFailure(exception) };
			}

			onCompleted?.Invoke(result);
		});
	}

	public void MaintainAllStoresInBackground()
	{
		RefreshMessages();

		if (System.Threading.Interlocked.CompareExchange(ref _isMaintainingStores, 1, 0) != 0) return;

		Task queuedWork;
		lock (_queueLock)
		{
			queuedWork = _queueTail;
		}

		queuedWork.ContinueWith(_ =>
		{
			try
			{
				MaintainAllStores();
			}
			finally
			{
				System.Threading.Volatile.Write(ref _isMaintainingStores, 0);
			}
		}, TaskScheduler.Default);
	}

	private void MaintainAllStores()
	{
		string root = MapHistoryStore.GetRootDirectory();
		if (!Directory.Exists(root)) return;

		foreach (string storeDirectory in Directory.EnumerateDirectories(root))
		{
			if (string.Equals(Path.GetFileName(storeDirectory), MapHistoryStore.DamagedDirectoryName, StringComparison.OrdinalIgnoreCase)) continue;

			try
			{
				var layout = new MapHistoryStoreLayout(root, Path.GetFileName(storeDirectory));
				using MapHistoryStoreSession? session = MapHistoryStore.TryAcquireExisting(layout, TimeSpan.FromMilliseconds(250));
				if (session == null) continue;

				MapHistoryCleanupResult result = HistoryCondensation.CondenseHistoryInStore(session);
				if (result.DroppedAnything)
				{
					GameLog.LogToConsole($"[MapHistoryService] Condensed {result.DroppedVersionCount} older version(s) of {layout.StoreKey}.");
				}
			}
			catch (Exception exception) when (exception is IOException or InvalidOperationException)
			{
				GameLog.LogError($"[MapHistoryService] Could not clean up {storeDirectory}: {exception.Message}");
			}
		}
	}

	private MapHistoryStatus PrimeInStore(string mapDirectory)
	{
		if (!File.Exists(Path.Combine(mapDirectory, "metadata.json")))
		{
			return MapHistoryStatus.Unavailable(_messages.NoMetadataYet);
		}

		using MapHistoryStoreSession session = MapHistoryStore.Acquire(mapDirectory);
		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		if (string.IsNullOrEmpty(repository.ReadHeadCommitId()))
		{
			CreateVersionInStore(session, mapDirectory, MapVersionKind.Initial, "First version", mapDirectory);
		}

		try
		{
			HistoryCondensation.CondenseHistoryInStore(session);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapHistoryService] Could not clean up this map's history: {exception.Message}");
		}

		MapHistoryStatus status = BuildStatus(session.Layout, mapDirectory);
		return session.WasReset
			? WithNotice(status, _messages.HistoryUnreadableReset)
			: status;
	}

	private MapHistoryStatus ReadStatusInStore(string mapDirectory)
	{
		string metadataPath = Path.Combine(mapDirectory, "metadata.json");
		if (!File.Exists(metadataPath))
		{
			return MapHistoryStatus.Unavailable(_messages.NoMetadataYet);
		}

		MapHistoryStoreLayout layout = MapHistoryStore.Resolve(mapDirectory);
		if (!MapHistoryStore.RepositoryExists(layout))
		{
			return new MapHistoryStatus { IsRepositoryReady = false };
		}

		return BuildStatus(layout, mapDirectory);
	}

	private static MapHistoryStatus BuildStatus(MapHistoryStoreLayout layout, string mapDirectory)
	{
		var repository = new MapGitRepository(layout.RepositoryDirectory);
		IReadOnlyList<MapGitCommitInfo> versionsNewestFirst = repository.ReadVersionHistory(int.MaxValue);
		MapHistoryStateFile state = LoadState(layout);
		MapGitCommitInfo? newestCommit = versionsNewestFirst.Count == 0 ? null : versionsNewestFirst[0];
		MapVersionEntry? latestVersion = newestCommit == null ? null : ToVersionEntry(newestCommit, FindEntry(state, newestCommit.Id), null);

		return new MapHistoryStatus
		{
			IsRepositoryReady = true,
			LastVersionAt = latestVersion?.CreatedAt,
			VersionCount = versionsNewestFirst.Count
		};
	}

	private static MapHistoryStatus WithNotice(MapHistoryStatus status, string notice)
	{
		return new MapHistoryStatus
		{
			IsRepositoryReady = status.IsRepositoryReady,
			LastVersionAt = status.LastVersionAt,
			VersionCount = status.VersionCount,
			Notice = notice,
			ErrorMessage = status.ErrorMessage
		};
	}

	private IReadOnlyList<MapVersionEntry> ReadVersionsInStore(string mapDirectory)
	{
		MapHistoryStoreLayout layout = MapHistoryStore.Resolve(mapDirectory);
		if (!MapHistoryStore.RepositoryExists(layout)) return Array.Empty<MapVersionEntry>();

		using MapHistoryStoreSession session = MapHistoryStore.Acquire(mapDirectory);
		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		MapHistoryStateFile state = LoadState(session.Layout);

		var versions = new List<MapVersionEntry>();
		foreach (MapGitCommitInfo commit in repository.ReadVersionHistory(int.MaxValue))
		{
			MapHistoryStateEntry? entry = FindEntry(state, commit.Id);
			versions.Add(ToVersionEntry(commit, entry, entry == null ? repository.ReadVersionLabel(commit.Id) : null));
		}

		return versions;
	}

	private MapHistoryVersionResult RenameVersionInStore(string mapDirectory, string commitId, string? label)
	{
		MapHistoryStoreLayout layout = MapHistoryStore.Resolve(mapDirectory);
		if (!MapHistoryStore.RepositoryExists(layout))
		{
			return new MapHistoryVersionResult { Success = false, ErrorMessage = _messages.NoVersionsYet };
		}

		using MapHistoryStoreSession session = MapHistoryStore.Acquire(mapDirectory);
		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		if (!repository.TryReadCommit(commitId, out MapGitCommitInfo commit))
		{
			return new MapHistoryVersionResult { Success = false, ErrorMessage = _messages.VersionUnreadable };
		}

		string? normalizedLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
		repository.WriteVersionLabel(commitId, new MapGitSignature(AuthorName, AuthorEmail, DateTimeOffset.Now), normalizedLabel);

		MapHistoryStateFile state = LoadState(session.Layout);
		MapHistoryStateEntry? entry = FindEntry(state, commitId);
		if (entry != null)
		{
			entry.Label = normalizedLabel;
			SaveState(session.Layout, state);
		}

		return new MapHistoryVersionResult { Success = true, Entry = ToVersionEntry(commit, entry, normalizedLabel) };
	}

	private static MapHistoryStorageInfo BuildStorageInfo(string mapDirectory)
	{
		MapHistoryStoreLayout layout = MapHistoryStore.Resolve(mapDirectory);
		if (!MapHistoryStore.RepositoryExists(layout))
		{
			return new MapHistoryStorageInfo();
		}

		var repository = new MapGitRepository(layout.RepositoryDirectory);
		MapHistoryRepositorySize size = repository.ReadSize();

		return new MapHistoryStorageInfo
		{
			TotalBytes = size.TotalBytes,
			PackedBytes = size.PackedBytes,
			VersionCount = repository.ReadVersionHistory(int.MaxValue).Count
		};
	}

	private MapHistoryVersionResult CreateVersionInStore(MapHistoryStoreSession session, string mapDirectory, MapVersionKind kind, string? label, string? sourceFolder)
	{
		if (!File.Exists(Path.Combine(mapDirectory, "metadata.json")))
		{
			return new MapHistoryVersionResult { Success = false, ErrorMessage = _messages.NoMetadataYet };
		}

		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		if (!repository.Exists)
		{
			return new MapHistoryVersionResult { Success = false, ErrorMessage = _messages.HistoryCouldNotBeOpened };
		}

		using Repository repositoryScope = repository.OpenRepositoryScope();
		MapHistoryStateFile state = LoadState(session.Layout);
		if (string.IsNullOrWhiteSpace(state.MapName))
		{
			state.MapName = MapHistoryStore.ResolveMapName(mapDirectory);
		}

		bool incrementsSaveVersion = kind is MapVersionKind.ManualSave or MapVersionKind.AutosaveCheckpoint or MapVersionKind.BeforePublish;
		bool canTrustFingerprints = string.Equals(state.FilesSourceFolder, mapDirectory, StringComparison.OrdinalIgnoreCase);

		List<string> workingFiles = HistoryFileSet.CollectMapFiles(mapDirectory);
		var blobIdByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var fingerprints = new Dictionary<string, MapHistoryFileFingerprint>(StringComparer.OrdinalIgnoreCase);

		foreach (string relativePath in workingFiles)
		{
			string absolutePath = Path.Combine(mapDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
			var fileInfo = new FileInfo(absolutePath);
			if (!fileInfo.Exists) continue;

			if (canTrustFingerprints &&
			    state.Files.TryGetValue(relativePath, out MapHistoryFileFingerprint? existing) &&
			    existing.Size == fileInfo.Length &&
			    existing.LastWriteTicks == fileInfo.LastWriteTimeUtc.Ticks &&
			    repository.HasBlob(existing.BlobId, repositoryScope))
			{
				blobIdByPath[relativePath] = existing.BlobId;
				fingerprints[relativePath] = existing;
				continue;
			}

			if (!HistoryFileSet.TryReadStableContent(absolutePath, out byte[] content))
			{
				return new MapHistoryVersionResult
				{
					Success = false,
					ErrorMessage = string.Format(_messages.FileBeingWritten, relativePath)
				};
			}

			string blobId = repository.WriteBlob(content, repositoryScope);

			blobIdByPath[relativePath] = blobId;
			fingerprints[relativePath] = new MapHistoryFileFingerprint
			{
				BlobId = blobId,
				Size = fileInfo.Length,
				LastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks
			};
		}

		string? headCommitId = repository.ReadHeadCommitId(repositoryScope);
		var parentFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrEmpty(headCommitId) && repository.TryReadCommit(headCommitId, out MapGitCommitInfo parentCommit, repositoryScope))
		{
			parentFiles = repository.ReadTreeFiles(parentCommit.TreeId, repositoryScope);
		}

		int changedFileCount = CountChangedPaths(parentFiles, blobIdByPath);
		if (!string.IsNullOrEmpty(headCommitId) && changedFileCount == 0)
		{
			return RememberFilesAlreadyInHistory(session, state, fingerprints, mapDirectory);
		}

		string treeId = repository.WriteTree(blobIdByPath, repositoryScope);
		if (!string.IsNullOrEmpty(headCommitId) && !string.IsNullOrEmpty(repository.FindCommitIdWithTree(treeId, repositoryScope)))
		{
			return RememberFilesAlreadyInHistory(session, state, fingerprints, mapDirectory);
		}

		if (incrementsSaveVersion && state.SaveVersion < long.MaxValue)
		{
			state.SaveVersion++;
		}

		string commitMessage = string.IsNullOrEmpty(label) ? GetDefaultCommitMessage(kind) : label;
		var signature = new MapGitSignature(AuthorName, AuthorEmail, DateTimeOffset.Now);
		string commitId = repository.WriteCommitForTree(treeId, headCommitId, signature, commitMessage);
		repository.UpdateHead(commitId, repositoryScope);

		var versionEntry = new MapHistoryStateEntry
		{
			CommitId = commitId,
			CreatedAt = signature.When,
			Kind = kind,
			Label = label,
			SaveVersion = state.SaveVersion,
			ChangedFileCount = changedFileCount,
			SourceFolder = sourceFolder ?? mapDirectory
		};

		state.Entries.RemoveAll(entry => string.Equals(entry.CommitId, commitId, StringComparison.OrdinalIgnoreCase));
		state.Entries.Add(versionEntry);
		state.Files = fingerprints;
		state.FilesSourceFolder = mapDirectory;
		SaveState(session.Layout, state);

		return new MapHistoryVersionResult
		{
			Success = true,
			Entry = ToVersionEntry(new MapGitCommitInfo
			{
				Id = commitId,
				CommittedAt = signature.When,
				Message = commitMessage
			}, versionEntry, versionEntry.Label)
		};
	}

	private MapHistoryVersionResult RememberFilesAlreadyInHistory(MapHistoryStoreSession session, MapHistoryStateFile state, Dictionary<string, MapHistoryFileFingerprint> fingerprints, string mapDirectory)
	{
		state.Files = fingerprints;
		state.FilesSourceFolder = mapDirectory;
		SaveState(session.Layout, state);

		return new MapHistoryVersionResult { Success = true, AlreadyInHistory = true };
	}

	private MapHistoryRestoreResult RestoreVersionInStore(string mapDirectory, string commitId)
	{
		MapHistoryStoreLayout layout = MapHistoryStore.Resolve(mapDirectory);
		if (!MapHistoryStore.RepositoryExists(layout))
		{
			return new MapHistoryRestoreResult { Success = false, ErrorMessage = _messages.NoVersionsYet };
		}

		using MapHistoryStoreSession session = MapHistoryStore.Acquire(mapDirectory);
		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		using Repository repositoryScope = repository.OpenRepositoryScope();
		if (!repository.TryReadCommit(commitId, out MapGitCommitInfo targetCommit, repositoryScope))
		{
			return new MapHistoryRestoreResult { Success = false, ErrorMessage = _messages.VersionUnreadable };
		}

		MapHistoryVersionResult beforeRestoreVersion = CreateVersionInStore(session, mapDirectory, MapVersionKind.BeforeRestore, "Before restore", mapDirectory);
		if (!beforeRestoreVersion.Success)
		{
			return new MapHistoryRestoreResult { Success = false, ErrorMessage = beforeRestoreVersion.ErrorMessage };
		}

		Dictionary<string, string> targetFiles = repository.ReadTreeFiles(targetCommit.TreeId, repositoryScope);
		string? currentHeadCommitId = repository.ReadHeadCommitId(repositoryScope);
		var currentFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrEmpty(currentHeadCommitId) && repository.TryReadCommit(currentHeadCommitId, out MapGitCommitInfo currentCommit, repositoryScope))
		{
			currentFiles = repository.ReadTreeFiles(currentCommit.TreeId, repositoryScope);
		}

		int filesWritten = 0;
		int filesRemoved = 0;
		bool scriptSourcesRestored = false;
		bool terrainRestored = false;
		bool metadataRestored = false;

		var filesToWrite = new List<(string RelativePath, byte[] Content)>();
		foreach (var pair in targetFiles)
		{
			if (!repository.TryReadBlob(pair.Value, out byte[] content, repositoryScope))
			{
				return new MapHistoryRestoreResult
				{
					Success = false,
					ErrorMessage = string.Format(_messages.VersionIsMissingFile, pair.Key)
				};
			}

			string absolutePath = Path.Combine(mapDirectory, pair.Key.Replace('/', Path.DirectorySeparatorChar));
			if (File.Exists(absolutePath) && HistoryFileSet.FileLengthMatches(absolutePath, content.Length) && HistoryFileSet.FileContentMatches(absolutePath, content))
			{
				continue;
			}

			filesToWrite.Add((pair.Key, content));
		}

		var relativePathsToRemove = new List<string>();
		foreach (string currentPath in currentFiles.Keys)
		{
			if (targetFiles.ContainsKey(currentPath)) continue;

			string absolutePath = Path.Combine(mapDirectory, currentPath.Replace('/', Path.DirectorySeparatorChar));
			if (File.Exists(absolutePath)) relativePathsToRemove.Add(currentPath);
		}

		if (!HistoryRestoreTransaction.Apply(mapDirectory, filesToWrite, relativePathsToRemove, out filesWritten, out filesRemoved, _messages.RestoreFailed, out string? restoreError))
		{
			return new MapHistoryRestoreResult { Success = false, ErrorMessage = restoreError };
		}

		foreach (var (relativePath, _) in filesToWrite)
		{
			if (HistoryFileSet.IsScriptSourcePath(relativePath)) scriptSourcesRestored = true;
			if (string.Equals(relativePath, "terrain.json", StringComparison.OrdinalIgnoreCase)) terrainRestored = true;
			if (string.Equals(relativePath, "metadata.json", StringComparison.OrdinalIgnoreCase)) metadataRestored = true;
		}

		MapHistoryVersionResult restoredVersion = CreateVersionInStore(
			session,
			mapDirectory,
			MapVersionKind.RestoredVersion,
			$"Restored version from {targetCommit.CommittedAt:yyyy-MM-dd HH:mm}",
			mapDirectory);

		return new MapHistoryRestoreResult
		{
			Success = true,
			VersionRecorded = restoredVersion.Entry != null,
			AlreadyInHistory = restoredVersion.AlreadyInHistory,
			ErrorMessage = restoredVersion.Entry != null ? null : restoredVersion.ErrorMessage,
			NewVersion = restoredVersion.Entry,
			FilesWritten = filesWritten,
			FilesRemoved = filesRemoved,
			ScriptSourcesRestored = scriptSourcesRestored,
			TerrainRestored = terrainRestored,
			MetadataRestored = metadataRestored
		};
	}


	private bool _nativeLibraryFailureReported;

	private string DescribeFailure(Exception exception)
	{
		Exception rootCause = exception;
		while (rootCause is TypeInitializationException or AggregateException && rootCause.InnerException != null)
		{
			rootCause = rootCause.InnerException;
		}

		if (rootCause is MapHistoryStoreBusyException)
		{
			return _messages.HistoryStillBusy;
		}

		if (rootCause is not (DllNotFoundException or BadImageFormatException))
		{
			return exception.Message;
		}

		if (!_nativeLibraryFailureReported)
		{
			_nativeLibraryFailureReported = true;
			GameLog.LogError($"[MapHistoryService] libgit2 could not be loaded, so the version history is unavailable: {rootCause.Message}");
		}

		return _messages.HistoryUnavailableInBuild;
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
					GameLog.LogError($"[MapHistoryService] History operation failed: {exception}");
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

	private static MapHistoryStateEntry? FindEntry(MapHistoryStateFile state, string commitId)
	{
		return state.Entries.FirstOrDefault(entry => string.Equals(entry.CommitId, commitId, StringComparison.OrdinalIgnoreCase));
	}

	private static MapVersionEntry ToVersionEntry(MapGitCommitInfo commit, MapHistoryStateEntry? entry, string? noteLabel)
	{
		return new MapVersionEntry
		{
			CommitId = commit.Id,
			CreatedAt = commit.CommittedAt,
			Kind = entry?.Kind ?? MapVersionKind.ExternalChange,
			Label = entry?.Label ?? noteLabel,
			SaveVersion = entry?.SaveVersion ?? 0,
			ChangedFileCount = entry?.ChangedFileCount ?? 0,
			SourceFolder = entry?.SourceFolder
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





	private static string GetDefaultCommitMessage(MapVersionKind kind)
	{
		return kind switch
		{
			MapVersionKind.Initial => "Initial version",
			MapVersionKind.ManualSave => "Save point",
			MapVersionKind.AutosaveCheckpoint => "Autosave checkpoint",
			MapVersionKind.BeforePublish => "Before publish",
			MapVersionKind.BeforePlaytest => "Before playtest",
			MapVersionKind.BeforeMapUpgrade => "Before map upgrade",
			MapVersionKind.AfterMapUpgrade => "After map upgrade",
			MapVersionKind.BeforeRestore => "Before restore",
			MapVersionKind.BeforeNewMap => "Before a new map",
			MapVersionKind.RestoredVersion => "Restored version",
			MapVersionKind.BeforeAssetOptimization => "Before asset cleanup",
			MapVersionKind.AfterAssetOptimization => "After asset cleanup",
			_ => "Map change"
		};
	}

	public static MapHistoryStateFile LoadState(MapHistoryStoreLayout layout)
	{
		try
		{
			if (File.Exists(layout.StateFilePath))
			{
				MapHistoryStateFile? loaded = JsonSerializer.Deserialize<MapHistoryStateFile>(File.ReadAllText(layout.StateFilePath), JsonOptions);
				if (loaded != null)
				{
					Normalize(loaded);
					return loaded;
				}
			}
		}
		catch (Exception exception) when (exception is IOException or JsonException)
		{
			GameLog.LogError($"[MapHistoryService] Could not read the history state: {exception.Message}");
		}

		return new MapHistoryStateFile();
	}

	private static void Normalize(MapHistoryStateFile state)
	{
		state.Entries ??= new List<MapHistoryStateEntry>();
		state.Files ??= new Dictionary<string, MapHistoryFileFingerprint>(StringComparer.OrdinalIgnoreCase);
		if (state.Files.Comparer != StringComparer.OrdinalIgnoreCase)
		{
			state.Files = new Dictionary<string, MapHistoryFileFingerprint>(state.Files, StringComparer.OrdinalIgnoreCase);
		}
	}

	public static void SaveState(MapHistoryStoreLayout layout, MapHistoryStateFile state)
	{
		Directory.CreateDirectory(layout.StoreDirectory);
		string temporaryPath = layout.StateFilePath + ".tmp";
		File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
		File.Move(temporaryPath, layout.StateFilePath, true);
	}

	public class MapHistoryStateFile
	{
		public string? MapName { get; set; }
		public long SaveVersion { get; set; }
		public DateTimeOffset? LastCondenseAt { get; set; }
		public string? FilesSourceFolder { get; set; }
		public List<MapHistoryStateEntry> Entries { get; set; } = new();
		public Dictionary<string, MapHistoryFileFingerprint> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	}

	public class MapHistoryStateEntry
	{
		public string CommitId { get; set; } = string.Empty;
		public DateTimeOffset CreatedAt { get; set; }
		public MapVersionKind Kind { get; set; }
		public string? Label { get; set; }
		public long SaveVersion { get; set; }
		public int ChangedFileCount { get; set; }
		public string? SourceFolder { get; set; }
	}

	public class MapHistoryFileFingerprint
	{
		public string BlobId { get; set; } = string.Empty;
		public long Size { get; set; }
		public long LastWriteTicks { get; set; }
	}

}
