using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using LibGit2Sharp;
using Realm.Client.Utils;

namespace Realm.Client.Services.MapHistory;

public class MapHistoryService
{
	private const string AuthorName = "Realm Map Editor";
	private const string AuthorEmail = "map-editor@realm.local";

	private static readonly string[] HistoryOnlyDirectoryNames = { ".vscode" };

	private static readonly string[] HistoryOnlyFileSuffixes = { ".pem", ".rkey", ".user", ".suo" };

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly object _queueLock = new();
	private Task _queueTail = Task.CompletedTask;
	private bool _versionRequestPending;
	private volatile MapHistoryStatus _cachedStatus = MapHistoryStatus.Unavailable("No map is open.");

	public MapHistoryStatus CachedStatus => _cachedStatus;

	private static bool HasMapFolder(string mapDirectory)
	{
		return !string.IsNullOrWhiteSpace(mapDirectory) && Directory.Exists(mapDirectory);
	}

	public void PrimeForMap(string mapDirectory, Action<MapHistoryStatus> onCompleted = null)
	{
		if (!HasMapFolder(mapDirectory))
		{
			onCompleted?.Invoke(MapHistoryStatus.Unavailable("No map folder is open."));
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

			_cachedStatus = status;
			onCompleted?.Invoke(status);
		});
	}

	public void CreateVersion(string mapDirectory, MapVersionKind kind, string? label, Action<MapHistoryVersionResult> onCompleted = null, bool coalesce = false, string? sourceFolder = null)
	{
		if (!HasMapFolder(mapDirectory))
		{
			onCompleted?.Invoke(new MapHistoryVersionResult { Success = false, ErrorMessage = "No map folder is open." });
			return;
		}

		Enqueue(() =>
		{
			MapHistoryVersionResult result;
			try
			{
				using MapHistoryStoreSession session = MapHistoryStore.Acquire(mapDirectory);
				result = CreateVersionInStore(session, mapDirectory, kind, label, sourceFolder);
				if (result.Success)
				{
					TrimHistoryAfterCommit(session);
				}
			}
			catch (Exception exception)
			{
				result = new MapHistoryVersionResult { Success = false, ErrorMessage = exception.Message };
			}

			UpdateCachedStatus(mapDirectory);
			onCompleted?.Invoke(result);
		}, coalesce);
	}

	public Task<MapHistoryVersionResult> CreateVersionAsync(string mapDirectory, MapVersionKind kind, string? label, string? sourceFolder = null)
	{
		var completion = new TaskCompletionSource<MapHistoryVersionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		CreateVersion(mapDirectory, kind, label, result => completion.TrySetResult(result), sourceFolder: sourceFolder);
		return completion.Task;
	}

	public void ReadVersions(string mapDirectory, Action<IReadOnlyList<MapVersionEntry>> onCompleted)
	{
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
				GD.PrintErr($"[MapHistoryService] Could not read versions: {exception.Message}");
			}

			onCompleted?.Invoke(versions);
		});
	}

	public void RenameVersion(string mapDirectory, string commitId, string? label, Action<MapHistoryVersionResult> onCompleted = null)
	{
		if (!HasMapFolder(mapDirectory))
		{
			onCompleted?.Invoke(new MapHistoryVersionResult { Success = false, ErrorMessage = "No map folder is open." });
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
				result = new MapHistoryVersionResult { Success = false, ErrorMessage = exception.Message };
			}

			onCompleted?.Invoke(result);
		});
	}

	public void ReadStorageUsage(string mapDirectory, Action<MapHistoryStorageInfo> onCompleted)
	{
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
				info = new MapHistoryStorageInfo { ErrorMessage = exception.Message };
			}

			onCompleted?.Invoke(info);
		});
	}

	public void ReadStatus(string mapDirectory, Action<MapHistoryStatus> onCompleted)
	{
		if (!HasMapFolder(mapDirectory))
		{
			onCompleted?.Invoke(MapHistoryStatus.Unavailable("No map folder is open."));
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
				status = MapHistoryStatus.Unavailable(exception.Message);
			}

			_cachedStatus = status;
			onCompleted?.Invoke(status);
		});
	}

	public void RestoreVersion(string mapDirectory, string commitId, Action<MapHistoryRestoreResult> onCompleted)
	{
		if (!HasMapFolder(mapDirectory))
		{
			onCompleted?.Invoke(new MapHistoryRestoreResult { Success = false, ErrorMessage = "No map folder is open." });
			return;
		}

		Enqueue(() =>
		{
			MapHistoryRestoreResult result;
			try
			{
				result = RestoreVersionInStore(mapDirectory, commitId);
			}
			catch (Exception exception)
			{
				result = new MapHistoryRestoreResult { Success = false, ErrorMessage = exception.Message };
			}

			UpdateCachedStatus(mapDirectory);
			onCompleted?.Invoke(result);
		});
	}

	public void MaintainAllStoresInBackground()
	{
		Enqueue(() =>
		{
			string root = MapHistoryStore.GetRootDirectory();
			if (!Directory.Exists(root)) return;

			foreach (string storeDirectory in Directory.EnumerateDirectories(root))
			{
				if (string.Equals(Path.GetFileName(storeDirectory), "_damaged", StringComparison.OrdinalIgnoreCase)) continue;

				try
				{
					var layout = new MapHistoryStoreLayout(root, Path.GetFileName(storeDirectory));
					using MapHistoryStoreSession? session = MapHistoryStore.TryAcquireExisting(layout, TimeSpan.FromMilliseconds(250));
					if (session == null) continue;

					MapHistoryCleanupResult result = CondenseHistoryInStore(session);
					if (result.DroppedAnything)
					{
						GD.Print($"[MapHistoryService] Condensed {result.DroppedVersionCount} older version(s) of {layout.StoreKey}.");
					}
				}
				catch (Exception exception) when (exception is IOException or InvalidOperationException)
				{
					GD.PrintErr($"[MapHistoryService] Could not clean up {storeDirectory}: {exception.Message}");
				}
			}
		});
	}

	private MapHistoryStatus PrimeInStore(string mapDirectory)
	{
		if (!File.Exists(Path.Combine(mapDirectory, "metadata.json")))
		{
			return MapHistoryStatus.Unavailable("This folder has no metadata.json yet.");
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
			GD.PrintErr($"[MapHistoryService] Could not clean up this map's history: {exception.Message}");
		}

		MapHistoryStatus status = BuildStatus(session.Layout, mapDirectory);
		return session.WasReset
			? WithNotice(status, "The stored version history of this map could not be read, so a new one was started. The previous files were kept in the history folder.")
			: status;
	}

	private MapHistoryStatus ReadStatusInStore(string mapDirectory)
	{
		string metadataPath = Path.Combine(mapDirectory, "metadata.json");
		if (!File.Exists(metadataPath))
		{
			return MapHistoryStatus.Unavailable("This folder has no metadata.json yet.");
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
		IReadOnlyList<MapGitCommitInfo> newestVersion = repository.ReadVersionHistory(1);
		MapHistoryStateFile state = LoadState(layout);
		MapGitCommitInfo? newestCommit = newestVersion.Count == 0 ? null : newestVersion[0];
		MapVersionEntry? latestVersion = newestCommit == null ? null : ToVersionEntry(newestCommit, FindEntry(state, newestCommit.Id), null);

		return new MapHistoryStatus
		{
			IsRepositoryReady = true,
			LastVersionCommitId = latestVersion?.CommitId,
			LastVersionAt = latestVersion?.CreatedAt,
			VersionCount = repository.CountVersions(int.MaxValue),
			HasUnversionedChanges = DetectUnversionedChanges(repository, state, mapDirectory, newestCommit)
		};
	}

	private static bool DetectUnversionedChanges(MapGitRepository repository, MapHistoryStateFile state, string mapDirectory, MapGitCommitInfo? newestCommit)
	{
		if (newestCommit == null || !Directory.Exists(mapDirectory)) return false;

		using Repository repositoryScope = repository.OpenRepositoryScope();
		Dictionary<string, string> versionedFiles = repository.ReadTreeFiles(newestCommit.TreeId, repositoryScope);
		List<string> workingFiles = CollectMapFiles(mapDirectory);
		if (workingFiles.Count != versionedFiles.Count) return true;

		bool canTrustFingerprints = string.Equals(state.FilesSourceFolder, mapDirectory, StringComparison.OrdinalIgnoreCase);
		foreach (string relativePath in workingFiles)
		{
			if (!versionedFiles.TryGetValue(relativePath, out string? versionedBlobId)) return true;

			string absolutePath = Path.Combine(mapDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
			var fileInfo = new FileInfo(absolutePath);
			if (!fileInfo.Exists) return true;

			if (canTrustFingerprints &&
			    state.Files.TryGetValue(relativePath, out MapHistoryFileFingerprint? fingerprint) &&
			    fingerprint.Size == fileInfo.Length &&
			    fingerprint.LastWriteTicks == fileInfo.LastWriteTimeUtc.Ticks)
			{
				if (!string.Equals(fingerprint.BlobId, versionedBlobId, StringComparison.OrdinalIgnoreCase)) return true;
				continue;
			}

			if (!TryReadStableContent(absolutePath, out byte[] content)) continue;
			if (!string.Equals(MapGitRepository.ComputeBlobId(content), versionedBlobId, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	private static MapHistoryStatus WithNotice(MapHistoryStatus status, string notice)
	{
		return new MapHistoryStatus
		{
			IsRepositoryReady = status.IsRepositoryReady,
			LastVersionCommitId = status.LastVersionCommitId,
			LastVersionAt = status.LastVersionAt,
			VersionCount = status.VersionCount,
			HasUnversionedChanges = status.HasUnversionedChanges,
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
			return new MapHistoryVersionResult { Success = false, ErrorMessage = "This map has no versions yet." };
		}

		using MapHistoryStoreSession session = MapHistoryStore.Acquire(mapDirectory);
		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		if (!repository.TryReadCommit(commitId, out MapGitCommitInfo commit))
		{
			return new MapHistoryVersionResult { Success = false, ErrorMessage = "That version can no longer be read." };
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
			VersionCount = repository.CountVersions(int.MaxValue)
		};
	}

	private MapHistoryVersionResult CreateVersionInStore(MapHistoryStoreSession session, string mapDirectory, MapVersionKind kind, string? label, string? sourceFolder)
	{
		if (!File.Exists(Path.Combine(mapDirectory, "metadata.json")))
		{
			return new MapHistoryVersionResult { Success = false, ErrorMessage = "This folder has no metadata.json yet." };
		}

		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		if (!repository.Exists)
		{
			return new MapHistoryVersionResult { Success = false, ErrorMessage = "This map's version history could not be opened." };
		}

		using Repository repositoryScope = repository.OpenRepositoryScope();
		MapHistoryStateFile state = LoadState(session.Layout);

		bool incrementsSaveVersion = kind is MapVersionKind.ManualSave or MapVersionKind.AutosaveCheckpoint or MapVersionKind.BeforePublish;
		bool canTrustFingerprints = string.Equals(state.FilesSourceFolder, mapDirectory, StringComparison.OrdinalIgnoreCase);

		List<string> workingFiles = CollectMapFiles(mapDirectory);
		var blobIdByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var fingerprints = new Dictionary<string, MapHistoryFileFingerprint>(StringComparer.OrdinalIgnoreCase);
		long writtenBytes = 0;

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
					ErrorMessage = $"'{relativePath}' was being written by another program while the version was created. Try again in a moment."
				};
			}

			string blobId = repository.WriteBlob(content, out bool blobWasCreated, repositoryScope);
			if (blobWasCreated)
			{
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

		string? headCommitId = repository.ReadHeadCommitId(repositoryScope);
		var parentFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrEmpty(headCommitId) && repository.TryReadCommit(headCommitId, out MapGitCommitInfo parentCommit, repositoryScope))
		{
			parentFiles = repository.ReadTreeFiles(parentCommit.TreeId, repositoryScope);
		}

		int changedFileCount = CountChangedPaths(parentFiles, blobIdByPath);
		if (!string.IsNullOrEmpty(headCommitId) && changedFileCount == 0)
		{
			state.Files = fingerprints;
			state.FilesSourceFolder = mapDirectory;
			SaveState(session.Layout, state);
			return new MapHistoryVersionResult { Success = true };
		}

		if (incrementsSaveVersion && state.SaveVersion < long.MaxValue)
		{
			state.SaveVersion++;
		}

		string commitMessage = string.IsNullOrEmpty(label) ? GetDefaultCommitMessage(kind) : label;
		var signature = new MapGitSignature(AuthorName, AuthorEmail, DateTimeOffset.Now);
		string commitId = repository.WriteCommit(blobIdByPath, headCommitId, signature, commitMessage, repositoryScope);
		repository.UpdateHead(commitId, repositoryScope);

		var versionEntry = new MapHistoryStateEntry
		{
			CommitId = commitId,
			CreatedAt = signature.When,
			Kind = kind,
			Label = label,
			SaveVersion = state.SaveVersion,
			ChangedFileCount = changedFileCount,
			ChangedBytes = writtenBytes,
			SourceFolder = sourceFolder ?? mapDirectory
		};

		state.Entries.RemoveAll(entry => string.Equals(entry.CommitId, commitId, StringComparison.OrdinalIgnoreCase));
		state.Entries.Add(versionEntry);
		state.LastCommitId = commitId;
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

	private MapHistoryRestoreResult RestoreVersionInStore(string mapDirectory, string commitId)
	{
		MapHistoryStoreLayout layout = MapHistoryStore.Resolve(mapDirectory);
		if (!MapHistoryStore.RepositoryExists(layout))
		{
			return new MapHistoryRestoreResult { Success = false, ErrorMessage = "This map has no versions yet." };
		}

		using MapHistoryStoreSession session = MapHistoryStore.Acquire(mapDirectory);
		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		using Repository repositoryScope = repository.OpenRepositoryScope();
		if (!repository.TryReadCommit(commitId, out MapGitCommitInfo targetCommit, repositoryScope))
		{
			return new MapHistoryRestoreResult { Success = false, ErrorMessage = "That version can no longer be read." };
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
		bool scriptSourcesRestored = false;
		foreach (var pair in targetFiles)
		{
			string absolutePath = Path.Combine(mapDirectory, pair.Key.Replace('/', Path.DirectorySeparatorChar));
			if (!repository.TryReadBlob(pair.Value, out byte[] content, repositoryScope))
			{
				continue;
			}

			if (File.Exists(absolutePath) && FileLengthMatches(absolutePath, content.Length) && FileContentMatches(absolutePath, content))
			{
				continue;
			}

			WriteFileAtomically(absolutePath, content);
			filesWritten++;
			if (IsScriptSourcePath(pair.Key)) scriptSourcesRestored = true;
		}

		int filesRemoved = 0;
		foreach (string currentPath in currentFiles.Keys)
		{
			if (targetFiles.ContainsKey(currentPath)) continue;

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

		MapHistoryVersionResult restoredVersion = CreateVersionInStore(
			session,
			mapDirectory,
			MapVersionKind.RestoredVersion,
			$"Restored version from {targetCommit.CommittedAt:yyyy-MM-dd HH:mm}",
			mapDirectory);

		return new MapHistoryRestoreResult
		{
			Success = restoredVersion.Success,
			ErrorMessage = restoredVersion.ErrorMessage,
			NewVersion = restoredVersion.Entry,
			FilesWritten = filesWritten,
			FilesRemoved = filesRemoved,
			ScriptSourcesRestored = scriptSourcesRestored,
			TerrainRestored = filesWritten > 0 && targetFiles.ContainsKey("terrain.json"),
			MetadataRestored = targetFiles.ContainsKey("metadata.json") && filesWritten > 0
		};
	}

	private void TrimHistoryAfterCommit(MapHistoryStoreSession session)
	{
		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		MapHistoryStateFile state = LoadState(session.Layout);
		MapHistoryCondensePlan plan = BuildCondensePlan(repository, state);
		if (plan.DroppedCount < MapHistoryPolicy.MinimumDroppedVersionsPerCondense) return;

		MapHistoryCleanupResult cleanup = MakeHistoryLessGranularInStore(session, repository, state, plan);
		if (cleanup.DroppedAnything)
		{
			GD.Print($"[MapHistoryService] Condensed {cleanup.DroppedVersionCount} older version(s) while saving.");
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
			string? label = entry?.Label ?? repository.ReadVersionLabel(commit.Id);
			long saveVersion = entry != null && entry.SaveVersion > 0 ? entry.SaveVersion : commitsNewestFirst.Count - rank;
			slotsNewestFirst.Add(new MapVersionSlot(commit, entry, label, saveVersion));
		}

		var keptNewestFirst = new List<MapVersionSlot>(slotsNewestFirst.Count);
		DateTimeOffset now = DateTimeOffset.Now;
		foreach (MapVersionSlot slot in slotsNewestFirst)
		{
			if (keptNewestFirst.Count == 0 || IsPinnedVersion(slot))
			{
				keptNewestFirst.Add(slot);
				continue;
			}

			MapVersionSlot newestKeptSlot = keptNewestFirst[^1];
			TimeSpan age = now - slot.Commit.CommittedAt;
			TimeSpan granularity = MapHistoryPolicy.GetGranularity(age);
			if (newestKeptSlot.Commit.CommittedAt - slot.Commit.CommittedAt < granularity) continue;

			keptNewestFirst.Add(slot);
		}

		keptNewestFirst.Reverse();
		return new MapHistoryCondensePlan(keptNewestFirst, slotsNewestFirst.Count - keptNewestFirst.Count);
	}

	private static bool IsPinnedVersion(MapVersionSlot slot)
	{
		if (!string.IsNullOrWhiteSpace(slot.Label)) return true;
		return slot.Entry?.Kind == MapVersionKind.BeforeRestore;
	}

	private MapHistoryCleanupResult MakeHistoryLessGranularInStore(MapHistoryStoreSession session, MapGitRepository repository, MapHistoryStateFile state, MapHistoryCondensePlan plan)
	{
		if (plan.DroppedCount == 0)
		{
			return new MapHistoryCleanupResult { RemainingVersionCount = plan.KeptOldestFirst.Count };
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
				ChangedBytes = slot.Entry?.ChangedBytes ?? 0,
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
		state.LastCommitId = parentCommitId;
		SaveState(session.Layout, state);

		return new MapHistoryCleanupResult
		{
			DroppedVersionCount = plan.DroppedCount,
			RemainingVersionCount = rewrittenEntries.Count
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

	private void UpdateCachedStatus(string mapDirectory)
	{
		try
		{
			_cachedStatus = ReadStatusInStore(mapDirectory);
		}
		catch (Exception exception)
		{
			_cachedStatus = MapHistoryStatus.Unavailable(exception.Message);
		}
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
			ChangedBytes = entry?.ChangedBytes ?? 0,
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

	private static string GetDefaultCommitMessage(MapVersionKind kind)
	{
		return kind switch
		{
			MapVersionKind.Initial => "Initial version",
			MapVersionKind.ManualSave => "Save point",
			MapVersionKind.AutosaveCheckpoint => "Autosave checkpoint",
			MapVersionKind.BeforePublish => "Before publish",
			MapVersionKind.BeforeMapUpgrade => "Before map upgrade",
			MapVersionKind.AfterMapUpgrade => "After map upgrade",
			MapVersionKind.BeforeRestore => "Before restore",
			MapVersionKind.RestoredVersion => "Restored version",
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
			GD.PrintErr($"[MapHistoryService] Could not read the history state: {exception.Message}");
		}

		return new MapHistoryStateFile { StoreKey = layout.StoreKey };
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
		state.StoreKey = layout.StoreKey;
		string temporaryPath = layout.StateFilePath + ".tmp";
		File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
		File.Move(temporaryPath, layout.StateFilePath, true);
	}

	public class MapHistoryStateFile
	{
		public string StoreKey { get; set; } = string.Empty;
		public long SaveVersion { get; set; }
		public string? LastCommitId { get; set; }
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
		public long ChangedBytes { get; set; }
		public string? SourceFolder { get; set; }
	}

	public class MapHistoryFileFingerprint
	{
		public string BlobId { get; set; } = string.Empty;
		public long Size { get; set; }
		public long LastWriteTicks { get; set; }
	}

	private readonly record struct MapVersionSlot(MapGitCommitInfo Commit, MapHistoryStateEntry? Entry, string? Label, long SaveVersion);
}
