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
	private const string AuthorName = "Realm Map Editor";
	private const string AuthorEmail = "map-editor@realm.local";

	private static readonly TimeSpan SynchronousLockTimeout = TimeSpan.FromSeconds(4);

	private static readonly string[] HistoryOnlyDirectoryNames = { ".vscode" };

	private static readonly string[] HistoryOnlyFileSuffixes = { ".pem", ".rkey", ".user", ".suo", AtomicFileWriter.TemporarySuffix };

	private static readonly JsonSerializerOptions JsonOptions = new()
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

	public void PrimeForMap(string mapDirectory, Action<MapHistoryStatus> onCompleted = null)
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

	public void CreateVersion(string mapDirectory, MapVersionKind kind, string? label, Action<MapHistoryVersionResult> onCompleted = null, bool coalesce = false, string? sourceFolder = null)
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
				TrimHistoryAfterCommit(session);
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
				stores = ReadStoresOnDisk();
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
				result = DeleteStoreOnDisk(storeDirectoryPath);
			}
			catch (Exception exception)
			{
				result = new MapHistoryStoreDeleteResult { Success = false, ErrorMessage = DescribeFailure(exception) };
			}

			onCompleted?.Invoke(result);
		});
	}

	private static IReadOnlyList<MapHistoryStoreInfo> ReadStoresOnDisk()
	{
		var stores = new List<MapHistoryStoreInfo>();
		string root = MapHistoryStore.GetRootDirectory();
		if (!Directory.Exists(root)) return stores;

		foreach (string storeDirectory in Directory.EnumerateDirectories(root))
		{
			string storeName = Path.GetFileName(storeDirectory);
			if (string.Equals(storeName, MapHistoryStore.DamagedDirectoryName, StringComparison.OrdinalIgnoreCase))
			{
				foreach (string recoveryDirectory in Directory.EnumerateDirectories(storeDirectory))
				{
					stores.Add(DescribeStore(Path.GetFileName(recoveryDirectory), recoveryDirectory, isRecovery: true));
				}

				continue;
			}

			stores.Add(DescribeStore(storeName, storeDirectory, isRecovery: false));
		}

		return stores.OrderByDescending(store => store.TotalBytes).ToList();
	}

	private static MapHistoryStoreInfo DescribeStore(string storeName, string storeDirectory, bool isRecovery)
	{
		long totalBytes = MeasureDirectoryBytes(storeDirectory);
		string? storedMapName = TryReadStoredMapName(storeDirectory);
		if (isRecovery) return DescribeUnreadableStore(storeName, storeDirectory, storedMapName, totalBytes, isRecovery: true, isUnreadable: false);

		string repositoryDirectory = Path.Combine(storeDirectory, "repository");
		if (!Directory.Exists(repositoryDirectory)) return DescribeUnreadableStore(storeName, storeDirectory, storedMapName, totalBytes, isRecovery: false, isUnreadable: true);

		try
		{
			var repository = new MapGitRepository(repositoryDirectory);
			if (!repository.Exists) return DescribeUnreadableStore(storeName, storeDirectory, storedMapName, totalBytes, isRecovery: false, isUnreadable: true);

			IReadOnlyList<MapGitCommitInfo> versions = repository.ReadVersionHistory(int.MaxValue);
			return new MapHistoryStoreInfo
			{
				StoreKey = storeName,
				DirectoryPath = storeDirectory,
				MapName = storedMapName,
				TotalBytes = totalBytes,
				VersionCount = versions.Count,
				LastVersionAt = versions.Count == 0 ? null : versions[0].CommittedAt,
				IsRecovery = isRecovery
			};
		}
		catch (Exception exception) when (exception is LibGit2SharpException or IOException or ArgumentException or FormatException)
		{
			return DescribeUnreadableStore(storeName, storeDirectory, storedMapName, totalBytes, isRecovery: false, isUnreadable: true);
		}
	}

	private static MapHistoryStoreInfo DescribeUnreadableStore(string storeName, string storeDirectory, string? mapName, long totalBytes, bool isRecovery, bool isUnreadable)
	{
		return new MapHistoryStoreInfo
		{
			StoreKey = storeName,
			DirectoryPath = storeDirectory,
			MapName = mapName,
			TotalBytes = totalBytes,
			IsRecovery = isRecovery,
			IsUnreadable = isUnreadable
		};
	}

	private static string? TryReadStoredMapName(string storeDirectory)
	{
		try
		{
			string statePath = Path.Combine(storeDirectory, "state.json");
			if (!File.Exists(statePath)) return null;

			MapHistoryStateFile? state = JsonSerializer.Deserialize<MapHistoryStateFile>(File.ReadAllText(statePath), JsonOptions);
			return string.IsNullOrWhiteSpace(state?.MapName) ? null : state.MapName;
		}
		catch (Exception exception) when (exception is IOException or JsonException)
		{
			return null;
		}
	}

	private static long MeasureDirectoryBytes(string directoryPath)
	{
		long totalBytes = 0;
		try
		{
			foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
			{
				try
				{
					totalBytes += new FileInfo(filePath).Length;
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			GameLog.LogError($"[MapHistoryService] Could not measure '{directoryPath}': {exception.Message}");
		}

		return totalBytes;
	}

	private MapHistoryStoreDeleteResult DeleteStoreOnDisk(string storeDirectoryPath)
	{
		if (string.IsNullOrWhiteSpace(storeDirectoryPath))
		{
			return new MapHistoryStoreDeleteResult { Success = false, ErrorMessage = _messages.NoHistoryFolderChosen };
		}

		string root = Path.GetFullPath(MapHistoryStore.GetRootDirectory());
		string fullPath = Path.GetFullPath(storeDirectoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			return new MapHistoryStoreDeleteResult { Success = false, ErrorMessage = _messages.NotPartOfHistoryStorage };
		}

		if (!Directory.Exists(fullPath)) return new MapHistoryStoreDeleteResult { Success = true };

		bool isStoredMap = string.Equals(Path.GetDirectoryName(fullPath), root, StringComparison.OrdinalIgnoreCase);
		if (isStoredMap)
		{
			var layout = new MapHistoryStoreLayout(root, Path.GetFileName(fullPath));
			if (MapHistoryStore.RepositoryExists(layout))
			{
				using (MapHistoryStoreSession? session = MapHistoryStore.TryAcquireExisting(layout, TimeSpan.FromMilliseconds(300)))
				{
					if (session == null)
					{
						return new MapHistoryStoreDeleteResult { Success = false, ErrorMessage = _messages.AnotherWindowUsesThisHistory };
					}
				}
			}
		}

		DeleteStoreDirectory(fullPath);

		return new MapHistoryStoreDeleteResult { Success = Directory.Exists(fullPath) == false, ErrorMessage = Directory.Exists(fullPath) ? _messages.HistoryFolderNotRemoved : null };
	}

	private static void DeleteStoreDirectory(string directoryPath)
	{
		try
		{
			foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
			{
				try
				{
					ClearReadOnlyAttribute(filePath);
				}
				catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
				{
				}
			}

			Directory.Delete(directoryPath, true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			GameLog.LogError($"[MapHistoryService] Could not remove '{directoryPath}': {exception.Message}");
		}
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

	public void RenameVersion(string mapDirectory, string commitId, string? label, Action<MapHistoryVersionResult> onCompleted = null)
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

	public void RestoreVersion(string mapDirectory, string commitId, Action<MapHistoryRestoreResult> onCompleted, Action beforeRestore = null)
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

				MapHistoryCleanupResult result = CondenseHistoryInStore(session);
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
			CondenseHistoryInStore(session);
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

		List<string> workingFiles = CollectMapFiles(mapDirectory);
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

			if (!TryReadStableContent(absolutePath, out byte[] content))
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
			if (File.Exists(absolutePath) && FileLengthMatches(absolutePath, content.Length) && FileContentMatches(absolutePath, content))
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

		if (!ApplyRestoreTransactionally(mapDirectory, filesToWrite, relativePathsToRemove, out filesWritten, out filesRemoved, out string? restoreError))
		{
			return new MapHistoryRestoreResult { Success = false, ErrorMessage = restoreError };
		}

		foreach (var (relativePath, _) in filesToWrite)
		{
			if (IsScriptSourcePath(relativePath)) scriptSourcesRestored = true;
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

	private void TrimHistoryAfterCommit(MapHistoryStoreSession session)
	{
		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		MapHistoryStateFile state = LoadState(session.Layout);
		if (state.LastCondenseAt.HasValue && DateTimeOffset.Now - state.LastCondenseAt.Value < MapHistoryPolicy.MinimumTimeBetweenCondenses) return;

		MapHistoryCondensePlan plan = BuildCondensePlan(repository, state);
		if (plan.DroppedCount < MapHistoryPolicy.MinimumDroppedVersionsPerCondense)
		{
			state.LastCondenseAt = DateTimeOffset.Now;
			SaveState(session.Layout, state);
			return;
		}

		MapHistoryCleanupResult cleanup = MakeHistoryLessGranularInStore(session, repository, state, plan);
		if (cleanup.DroppedAnything)
		{
			GameLog.LogToConsole($"[MapHistoryService] Condensed {cleanup.DroppedVersionCount} older version(s) while saving.");
		}
	}

	private MapHistoryCleanupResult CondenseHistoryInStore(MapHistoryStoreSession session)
	{
		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		MapHistoryStateFile state = LoadState(session.Layout);
		MapHistoryCondensePlan plan = BuildCondensePlan(repository, state);
		return MakeHistoryLessGranularInStore(session, repository, state, plan);
	}

	private MapHistoryCondensePlan BuildCondensePlan(MapGitRepository repository, MapHistoryStateFile state)
	{
		using Repository versionScope = repository.OpenRepositoryScope();
		IReadOnlyList<MapGitCommitInfo> commitsNewestFirst = repository.ReadVersionHistory(int.MaxValue);
		var entryByCommitId = new Dictionary<string, MapHistoryStateEntry>(StringComparer.OrdinalIgnoreCase);
		foreach (MapHistoryStateEntry stateEntry in state.Entries)
		{
			entryByCommitId[stateEntry.CommitId] = stateEntry;
		}

		var slotsNewestFirst = new List<MapVersionSlot>(commitsNewestFirst.Count);
		for (int rank = 0; rank < commitsNewestFirst.Count; rank++)
		{
			MapGitCommitInfo commit = commitsNewestFirst[rank];
			entryByCommitId.TryGetValue(commit.Id, out MapHistoryStateEntry? entry);
			string? noteLabel = repository.ReadVersionLabel(commit.Id, versionScope);
			string? label = entry?.Label ?? noteLabel;
			long saveVersion = entry != null && entry.SaveVersion > 0 ? entry.SaveVersion : commitsNewestFirst.Count - rank;
			slotsNewestFirst.Add(new MapVersionSlot(commit, entry, label, saveVersion, !string.IsNullOrWhiteSpace(noteLabel)));
		}

		var keptNewestFirst = new List<MapVersionSlot>(slotsNewestFirst.Count);
		var keptTreeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		DateTimeOffset now = DateTimeOffset.Now;
		foreach (MapVersionSlot slot in slotsNewestFirst)
		{
			bool isPinned = IsPinnedVersion(slot);
			if (keptNewestFirst.Count > 0)
			{
				if (!isPinned && keptTreeIds.Contains(slot.Commit.TreeId)) continue;

				if (!isPinned)
				{
					MapVersionSlot newestKeptSlot = keptNewestFirst[^1];
					TimeSpan age = now - slot.Commit.CommittedAt;
					TimeSpan granularity = MapHistoryPolicy.GetGranularity(age);
					if (newestKeptSlot.Commit.CommittedAt - slot.Commit.CommittedAt < granularity) continue;
				}
			}

			keptNewestFirst.Add(slot);
			keptTreeIds.Add(slot.Commit.TreeId);
		}

		keptNewestFirst.Reverse();
		return new MapHistoryCondensePlan(keptNewestFirst, slotsNewestFirst.Count - keptNewestFirst.Count);
	}

	private static bool IsPinnedVersion(MapVersionSlot slot)
	{
		if (slot.HasManualLabel) return true;
		if (slot.Entry?.Kind == MapVersionKind.RestoredVersion) return false;

		return !string.IsNullOrWhiteSpace(slot.Label) || slot.Entry?.Kind is MapVersionKind.BeforeRestore or MapVersionKind.BeforeNewMap;
	}

	private MapHistoryCleanupResult MakeHistoryLessGranularInStore(MapHistoryStoreSession session, MapGitRepository repository, MapHistoryStateFile state, MapHistoryCondensePlan plan)
	{
		if (plan.DroppedCount == 0)
		{
			return new MapHistoryCleanupResult();
		}

		var rewrittenEntries = new List<MapHistoryStateEntry>(plan.KeptOldestFirst.Count);
		var labelsToWrite = new List<(string CommitId, string Label)>();
		string? parentCommitId = null;

		foreach (MapVersionSlot slot in plan.KeptOldestFirst)
		{
			string commitId = slot.Commit.Id;
			if (!string.Equals(slot.Commit.ParentId, parentCommitId, StringComparison.OrdinalIgnoreCase))
			{
				string message = !string.IsNullOrWhiteSpace(slot.Label) ? slot.Label : slot.Commit.Message;
				var signature = new MapGitSignature(AuthorName, AuthorEmail, slot.Commit.CommittedAt);
				commitId = repository.WriteCommitForTree(slot.Commit.TreeId, parentCommitId, signature, message);
			}

			rewrittenEntries.Add(new MapHistoryStateEntry
			{
				CommitId = commitId,
				CreatedAt = slot.Commit.CommittedAt,
				Kind = slot.Entry?.Kind ?? MapVersionKind.ExternalChange,
				Label = slot.Label,
				SaveVersion = slot.SaveVersion,
				ChangedFileCount = slot.Entry?.ChangedFileCount ?? 0,
				SourceFolder = slot.Entry?.SourceFolder
			});

			if (!string.IsNullOrWhiteSpace(slot.Label))
			{
				labelsToWrite.Add((commitId, slot.Label));
			}

			parentCommitId = commitId;
		}

		repository.UpdateHead(parentCommitId ?? string.Empty);
		repository.ClearVersionLabels();
		var labelSignature = new MapGitSignature(AuthorName, AuthorEmail, DateTimeOffset.Now);
		foreach (var (labelCommitId, label) in labelsToWrite)
		{
			repository.WriteVersionLabel(labelCommitId, labelSignature, label);
		}

		repository.SweepUnreachableObjects();

		state.Entries = rewrittenEntries;
		state.LastCondenseAt = DateTimeOffset.Now;
		SaveState(session.Layout, state);

		return new MapHistoryCleanupResult
		{
			DroppedVersionCount = plan.DroppedCount
		};
	}

	private class MapHistoryCondensePlan
	{
		public MapHistoryCondensePlan(List<MapVersionSlot> keptOldestFirst, int droppedCount)
		{
			KeptOldestFirst = keptOldestFirst;
			DroppedCount = droppedCount;
		}

		public List<MapVersionSlot> KeptOldestFirst { get; }
		public int DroppedCount { get; }
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

	private static List<string> CollectMapFiles(string mapDirectory)
	{
		var relativePaths = new List<string>();
		if (!Directory.Exists(mapDirectory)) return relativePaths;

		CollectMapFilesInto(new DirectoryInfo(mapDirectory), string.Empty, relativePaths, 0);
		relativePaths.Sort(StringComparer.Ordinal);
		return relativePaths;
	}

	private static void CollectMapFilesInto(DirectoryInfo directory, string prefix, List<string> relativePaths, int depth)
	{
		if (depth > 64) return;

		foreach (FileInfo file in directory.GetFiles())
		{
			string relativePath = prefix + file.Name;
			if (IsExcludedFromHistory(relativePath)) continue;
			relativePaths.Add(relativePath);
		}

		foreach (DirectoryInfo childDirectory in directory.GetDirectories())
		{
			string relativePath = prefix + childDirectory.Name + "/";
			if (IsExcludedFromHistory(relativePath)) continue;
			CollectMapFilesInto(childDirectory, relativePath, relativePaths, depth + 1);
		}
	}

	private static bool IsExcludedFromHistory(string relativePath)
	{
		string normalized = relativePath.Replace('\\', '/');
		if (string.IsNullOrWhiteSpace(normalized)) return true;
		if (MapWorkspaceService.IsIgnoredPathDomain(normalized)) return true;

		string trimmed = normalized.TrimEnd('/');
		foreach (string directoryName in HistoryOnlyDirectoryNames)
		{
			if (trimmed.Equals(directoryName, StringComparison.OrdinalIgnoreCase)) return true;
			if (trimmed.StartsWith(directoryName + "/", StringComparison.OrdinalIgnoreCase)) return true;
			if (trimmed.Contains("/" + directoryName + "/", StringComparison.OrdinalIgnoreCase)) return true;
		}

		foreach (string suffix in HistoryOnlyFileSuffixes)
		{
			if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	private static bool TryReadStableContent(string absolutePath, out byte[] content)
	{
		content = Array.Empty<byte>();
		for (int attempt = 0; attempt < 3; attempt++)
		{
			try
			{
				var before = new FileInfo(absolutePath);
				if (!before.Exists) return false;

				long lengthBefore = before.Length;
				long writeTicksBefore = before.LastWriteTimeUtc.Ticks;
				byte[] readContent = File.ReadAllBytes(absolutePath);
				var after = new FileInfo(absolutePath);
				if (!after.Exists) return false;

				if (readContent.Length == lengthBefore &&
				    after.Length == lengthBefore &&
				    after.LastWriteTimeUtc.Ticks == writeTicksBefore)
				{
					content = readContent;
					return true;
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}

			System.Threading.Thread.Sleep(50);
		}

		return false;
	}

	private static bool IsScriptSourcePath(string relativePath)
	{
		return relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
		       relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
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

	private const string RestoreStagingSuffix = ".realm-restore.tmp";
	private const string RestoreBackupSuffix = ".realm-restore.bak";

	private bool ApplyRestoreTransactionally(
		string mapDirectory,
		List<(string RelativePath, byte[] Content)> filesToWrite,
		List<string> relativePathsToRemove,
		out int filesWritten,
		out int filesRemoved,
		out string? errorMessage)
	{
		filesWritten = 0;
		filesRemoved = 0;
		errorMessage = null;

		var stagedPaths = new List<string>(filesToWrite.Count);
		var touchedPaths = new List<string>(filesToWrite.Count + relativePathsToRemove.Count);

		try
		{
			foreach (var (relativePath, content) in filesToWrite)
			{
				string absolutePath = Path.Combine(mapDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
				StageRestoreContent(absolutePath, content);
				stagedPaths.Add(absolutePath);
			}

			foreach (string absolutePath in stagedPaths)
			{
				BackUpOriginalFile(absolutePath, touchedPaths);
				File.Move(absolutePath + RestoreStagingSuffix, absolutePath, true);
				if (!touchedPaths.Contains(absolutePath)) touchedPaths.Add(absolutePath);
				filesWritten++;
			}

			foreach (string relativePath in relativePathsToRemove)
		{
				string absolutePath = Path.Combine(mapDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
				if (!File.Exists(absolutePath)) continue;

				BackUpOriginalFile(absolutePath, touchedPaths);
				filesRemoved++;
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			RollBackRestore(touchedPaths, stagedPaths);
			filesWritten = 0;
			filesRemoved = 0;
			errorMessage = string.Format(_messages.RestoreFailed, exception.Message);
			return false;
		}

		foreach (string touchedPath in touchedPaths)
		{
			DeleteFileQuietly(touchedPath + RestoreBackupSuffix);
		}

		foreach (string stagedPath in stagedPaths)
		{
			DeleteFileQuietly(stagedPath + RestoreStagingSuffix);
		}

		return true;
	}

	private static void StageRestoreContent(string absolutePath, byte[] content)
	{
		string directory = Path.GetDirectoryName(absolutePath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		string stagingPath = absolutePath + RestoreStagingSuffix;
		DeleteFileQuietly(stagingPath);
		File.WriteAllBytes(stagingPath, content);
	}

	private static void BackUpOriginalFile(string absolutePath, List<string> touchedPaths)
	{
		if (!File.Exists(absolutePath)) return;

		string backupPath = absolutePath + RestoreBackupSuffix;
		DeleteFileQuietly(backupPath);
		ClearReadOnlyAttribute(absolutePath);
		File.Move(absolutePath, backupPath, true);
		touchedPaths.Add(absolutePath);
	}

	private static void RollBackRestore(List<string> touchedPaths, List<string> stagedPaths)
	{
		foreach (string touchedPath in touchedPaths)
		{
			string backupPath = touchedPath + RestoreBackupSuffix;
			try
			{
				File.Delete(touchedPath);
				if (File.Exists(backupPath))
				{
					File.Move(backupPath, touchedPath, true);
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				GameLog.LogError($"[MapHistoryService] Could not put '{touchedPath}' back: {exception.Message}");
			}
		}

		foreach (string stagedPath in stagedPaths)
		{
			DeleteFileQuietly(stagedPath + RestoreStagingSuffix);
		}
	}

	private static void DeleteFileQuietly(string absolutePath)
	{
		if (!File.Exists(absolutePath)) return;

		try
		{
			ClearReadOnlyAttribute(absolutePath);
			File.Delete(absolutePath);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			GameLog.LogError($"[MapHistoryService] Could not remove '{absolutePath}': {exception.Message}");
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

	private static MapHistoryStateFile LoadState(MapHistoryStoreLayout layout)
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

	private static void SaveState(MapHistoryStoreLayout layout, MapHistoryStateFile state)
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

	private readonly record struct MapVersionSlot(MapGitCommitInfo Commit, MapHistoryStateEntry? Entry, string? Label, long SaveVersion, bool HasManualLabel);
}
