using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Utils;

namespace Realm.Client.Services.MapHistory;

public class MapHistoryService
{
	private const string AuthorName = "Realm Map Editor";
	private const string AuthorEmail = "map-editor@realm.local";

	private static readonly string[] ExcludedDirectoryNames =
	{
		".git", ".vs", ".idea", ".vscode", ".godot", ".dotnet", ".wasi", ".sidecarcache", ".cache",
		"bin", "obj", "lib", "map_backups", "map_upgrades"
	};

	private static readonly string[] ExcludedFileSuffixes =
	{
		".rmap", ".tmp", ".log", ".bak", ".elf", ".jit.elf", ".wasm", ".dll", ".pdb", ".pem", ".rkey", ".user", ".suo"
	};

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

	public static string GetStoreRootPath()
	{
		return MapHistoryStore.GetRootDirectory();
	}

	public void PrimeForMap(string mapDirectory, MapHistoryRetention retention, Action<MapHistoryStatus> onCompleted = null)
	{
		Enqueue(() =>
		{
			MapHistoryStatus status;
			try
			{
				status = PrimeInStore(mapDirectory, retention);
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
		Enqueue(() =>
		{
			MapHistoryVersionResult result;
			try
			{
				using MapHistoryStoreSession session = MapHistoryStore.Acquire(mapDirectory);
				result = CreateVersionInStore(session, mapDirectory, kind, label, sourceFolder);
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
		var completion = new TaskCompletionSource<MapHistoryVersionResult>();
		CreateVersion(mapDirectory, kind, label, result => completion.TrySetResult(result), sourceFolder: sourceFolder);
		return completion.Task;
	}

	public void ReadVersions(string mapDirectory, Action<IReadOnlyList<MapVersionEntry>> onCompleted)
	{
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
		Enqueue(() =>
		{
			MapHistoryStorageInfo info;
			try
			{
				info = BuildStorageInfo(mapDirectory, wasCompacted: false);
			}
			catch (Exception exception)
			{
				info = new MapHistoryStorageInfo { ErrorMessage = exception.Message };
			}

			onCompleted?.Invoke(info);
		});
	}

	public void CompactHistory(string mapDirectory, Action<MapHistoryStorageInfo> onCompleted)
	{
		Enqueue(() =>
		{
			MapHistoryStorageInfo info;
			try
			{
				MapHistoryStoreLayout layout = MapHistoryStore.Resolve(mapDirectory);
				if (!MapHistoryStore.RepositoryExists(layout))
				{
					info = new MapHistoryStorageInfo { ErrorMessage = "This map has no versions yet." };
				}
				else
				{
					using (MapHistoryStore.Acquire(mapDirectory))
					{
						new MapGitRepository(layout.RepositoryDirectory).Compact();
					}

					info = BuildStorageInfo(mapDirectory, wasCompacted: true);
				}
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

	public void CleanUpHistory(string mapDirectory, MapHistoryRetention retention, Action<MapHistoryCleanupResult> onCompleted)
	{
		Enqueue(() =>
		{
			MapHistoryCleanupResult result;
			try
			{
				MapHistoryStoreLayout layout = MapHistoryStore.Resolve(mapDirectory);
				if (!MapHistoryStore.RepositoryExists(layout))
				{
					result = new MapHistoryCleanupResult { Success = true };
				}
				else
				{
					using MapHistoryStoreSession session = MapHistoryStore.Acquire(mapDirectory);
					result = CondenseHistoryInStore(session, retention);
				}
			}
			catch (Exception exception)
			{
				result = new MapHistoryCleanupResult { Success = false, ErrorMessage = exception.Message };
			}

			onCompleted?.Invoke(result);
		});
	}

	public void CleanUpAllStoresInBackground(MapHistoryRetention retention)
	{
		if (!retention.IsEnabled) return;

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

					MapHistoryCleanupResult result = CondenseHistoryInStore(session, retention);
					if (result.CondensedAnything)
					{
						GD.Print($"[MapHistoryService] Condensed {result.CondensedVersionCount} older version(s) of {layout.StoreKey}.");
					}
				}
				catch (Exception exception) when (exception is IOException or InvalidOperationException)
				{
					GD.PrintErr($"[MapHistoryService] Could not clean up {storeDirectory}: {exception.Message}");
				}
			}
		});
	}

	private MapHistoryStatus PrimeInStore(string mapDirectory, MapHistoryRetention retention)
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

		if (retention.IsEnabled)
		{
			try
			{
				CondenseHistoryInStore(session, retention);
			}
			catch (Exception exception)
			{
				GD.PrintErr($"[MapHistoryService] Could not clean up this map's history: {exception.Message}");
			}
		}

		MapHistoryStatus status = BuildStatus(mapDirectory, session.Layout);
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
			return new MapHistoryStatus { IsRepositoryReady = false, StoreDirectory = layout.StoreDirectory };
		}

		return BuildStatus(mapDirectory, layout);
	}

	private static MapHistoryStatus BuildStatus(string mapDirectory, MapHistoryStoreLayout layout)
	{
		var repository = new MapGitRepository(layout.RepositoryDirectory);
		IReadOnlyList<MapGitCommitInfo> commits = repository.ReadVersionHistory(int.MaxValue);
		MapHistoryStateFile state = LoadState(layout);
		MapVersionEntry? latestVersion = commits.Count == 0 ? null : ToVersionEntry(commits[0], FindEntry(state, commits[0].Id), null);

		return new MapHistoryStatus
		{
			IsRepositoryReady = true,
			LastVersionCommitId = latestVersion?.CommitId,
			LastVersionAt = latestVersion?.CreatedAt,
			LastVersionKind = latestVersion?.Kind,
			VersionCount = commits.Count,
			StoreDirectory = layout.StoreDirectory
		};
	}

	private static MapHistoryStatus WithNotice(MapHistoryStatus status, string notice)
	{
		return new MapHistoryStatus
		{
			IsRepositoryReady = status.IsRepositoryReady,
			LastVersionCommitId = status.LastVersionCommitId,
			LastVersionAt = status.LastVersionAt,
			LastVersionKind = status.LastVersionKind,
			VersionCount = status.VersionCount,
			StoreDirectory = status.StoreDirectory,
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

	private static MapHistoryStorageInfo BuildStorageInfo(string mapDirectory, bool wasCompacted)
	{
		MapHistoryStoreLayout layout = MapHistoryStore.Resolve(mapDirectory);
		if (!MapHistoryStore.RepositoryExists(layout))
		{
			return new MapHistoryStorageInfo { WasCompacted = wasCompacted };
		}

		var repository = new MapGitRepository(layout.RepositoryDirectory);
		MapHistoryRepositorySize size = repository.ReadSize();

		return new MapHistoryStorageInfo
		{
			TotalBytes = size.TotalBytes,
			LooseObjectBytes = size.LooseObjectBytes,
			PackedBytes = size.PackedBytes,
			LooseObjectCount = size.LooseObjectCount,
			ObjectPackCount = size.ObjectPackCount,
			VersionCount = repository.ReadVersionHistory(int.MaxValue).Count,
			WasCompacted = wasCompacted
		};
	}

	private MapHistoryVersionResult CreateVersionInStore(MapHistoryStoreSession session, string mapDirectory, MapVersionKind kind, string? label, string? sourceFolder)
	{
		if (!File.Exists(Path.Combine(mapDirectory, "metadata.json")))
		{
			return new MapHistoryVersionResult { Success = false, ErrorMessage = "This folder has no metadata.json yet." };
		}

		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		MapHistoryStateFile state = LoadState(session.Layout);

		bool incrementsSaveVersion = kind is MapVersionKind.ManualSave or MapVersionKind.SaveAs or MapVersionKind.AutosaveCheckpoint or MapVersionKind.BeforePublish;
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
			    repository.HasBlob(existing.BlobId))
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

			string blobId = repository.WriteBlob(content, out bool blobWasCreated);
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

		string? headCommitId = repository.ReadHeadCommitId();
		var parentFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrEmpty(headCommitId) && repository.TryReadCommit(headCommitId, out MapGitCommitInfo parentCommit))
		{
			parentFiles = repository.ReadTreeFiles(parentCommit.TreeId);
		}

		int changedFileCount = CountChangedPaths(parentFiles, blobIdByPath);
		if (!string.IsNullOrEmpty(headCommitId) && changedFileCount == 0)
		{
			state.Files = fingerprints;
			state.FilesSourceFolder = mapDirectory;
			SaveState(session.Layout, state);
			return new MapHistoryVersionResult { Success = true, NothingToCommit = true };
		}

		if (incrementsSaveVersion && state.SaveVersion < long.MaxValue)
		{
			state.SaveVersion++;
		}

		string commitMessage = string.IsNullOrEmpty(label) ? GetDefaultCommitMessage(kind) : label;
		var signature = new MapGitSignature(AuthorName, AuthorEmail, DateTimeOffset.Now);
		string commitId = repository.WriteCommit(blobIdByPath, headCommitId, signature, commitMessage);
		repository.UpdateHead(commitId);

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
		state.MapName = MapHistoryStore.ResolveMapName(mapDirectory);
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
		if (!repository.TryReadCommit(commitId, out MapGitCommitInfo targetCommit))
		{
			return new MapHistoryRestoreResult { Success = false, ErrorMessage = "That version can no longer be read." };
		}

		MapHistoryVersionResult beforeRestoreVersion = CreateVersionInStore(session, mapDirectory, MapVersionKind.BeforeRestore, "Before restore", mapDirectory);
		if (!beforeRestoreVersion.Success)
		{
			return new MapHistoryRestoreResult { Success = false, ErrorMessage = beforeRestoreVersion.ErrorMessage };
		}

		Dictionary<string, string> targetFiles = repository.ReadTreeFiles(targetCommit.TreeId);
		string? currentHeadCommitId = repository.ReadHeadCommitId();
		var currentFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrEmpty(currentHeadCommitId) && repository.TryReadCommit(currentHeadCommitId, out MapGitCommitInfo currentCommit))
		{
			currentFiles = repository.ReadTreeFiles(currentCommit.TreeId);
		}

		int filesWritten = 0;
		foreach (var pair in targetFiles)
		{
			string absolutePath = Path.Combine(mapDirectory, pair.Key.Replace('/', Path.DirectorySeparatorChar));
			if (!repository.TryReadBlob(pair.Value, out byte[] content))
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
			TerrainRestored = filesWritten > 0 && targetFiles.ContainsKey("terrain.json"),
			MetadataRestored = targetFiles.ContainsKey("metadata.json") && filesWritten > 0
		};
	}

	private MapHistoryCleanupResult CondenseHistoryInStore(MapHistoryStoreSession session, MapHistoryRetention retention)
	{
		if (!retention.IsEnabled)
		{
			return new MapHistoryCleanupResult { Success = true };
		}

		var repository = new MapGitRepository(session.Layout.RepositoryDirectory);
		IReadOnlyList<MapGitCommitInfo> commits = repository.ReadVersionHistory(int.MaxValue);
		if (commits.Count <= 1)
		{
			return new MapHistoryCleanupResult { Success = true, RemainingVersionCount = commits.Count };
		}

		MapHistoryStateFile state = LoadState(session.Layout);
		var slots = new List<MapVersionSlot>(commits.Count);
		for (int rank = 0; rank < commits.Count; rank++)
		{
			MapGitCommitInfo commit = commits[rank];
			MapHistoryStateEntry? entry = FindEntry(state, commit.Id);
			string? label = entry?.Label ?? repository.ReadVersionLabel(commit.Id);
			long saveVersion = entry != null && entry.SaveVersion > 0 ? entry.SaveVersion : commits.Count - rank;
			slots.Add(new MapVersionSlot(commit, entry, label, saveVersion));
		}

		var keptSlots = new List<MapVersionSlot>();
		DateTimeOffset now = DateTimeOffset.Now;
		for (int rank = 0; rank < slots.Count; rank++)
		{
			TimeSpan age = now - slots[rank].Commit.CommittedAt;
			if (retention.ShouldKeep(rank, age))
			{
				keptSlots.Add(slots[rank]);
			}
		}

		int prunedCount = slots.Count - keptSlots.Count;
		if (prunedCount == 0)
		{
			return new MapHistoryCleanupResult { Success = true, RemainingVersionCount = slots.Count };
		}

		MapVersionSlot condensedSlot = slots[prunedCount - 1];
		var signature = new MapGitSignature(AuthorName, AuthorEmail, condensedSlot.Commit.CommittedAt);
		string condensedMessage = BuildCondensedMessage(prunedCount, condensedSlot.Label);
		string condensedCommitId = repository.WriteCommitForTree(condensedSlot.Commit.TreeId, null, signature, condensedMessage);

		var rewrittenEntries = new List<MapHistoryStateEntry>(keptSlots.Count + 1)
		{
			new()
			{
				CommitId = condensedCommitId,
				CreatedAt = condensedSlot.Commit.CommittedAt,
				Kind = MapVersionKind.Condensed,
				Label = condensedSlot.Label,
				SaveVersion = condensedSlot.SaveVersion,
				ChangedFileCount = condensedSlot.Entry?.ChangedFileCount ?? 0,
				ChangedBytes = condensedSlot.Entry?.ChangedBytes ?? 0,
				SourceFolder = condensedSlot.Entry?.SourceFolder
			}
		};

		var writtenLabels = new List<(string CommitId, string Label)>();
		if (!string.IsNullOrWhiteSpace(condensedSlot.Label))
		{
			writtenLabels.Add((condensedCommitId, condensedSlot.Label));
		}

		string? previousCommitId = condensedCommitId;
		for (int index = keptSlots.Count - 1; index >= 0; index--)
		{
			MapVersionSlot slot = keptSlots[index];
			string message = !string.IsNullOrWhiteSpace(slot.Label) ? slot.Label : GetDefaultCommitMessage(slot.Entry?.Kind ?? MapVersionKind.ExternalChange);
			var slotSignature = new MapGitSignature(AuthorName, AuthorEmail, slot.Commit.CommittedAt);
			string newCommitId = repository.WriteCommitForTree(slot.Commit.TreeId, previousCommitId, slotSignature, message);
			previousCommitId = newCommitId;

			rewrittenEntries.Add(new MapHistoryStateEntry
			{
				CommitId = newCommitId,
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
				writtenLabels.Add((newCommitId, slot.Label));
			}
		}

		repository.UpdateHead(previousCommitId ?? condensedCommitId);
		repository.ClearVersionLabels();
		var labelSignature = new MapGitSignature(AuthorName, AuthorEmail, DateTimeOffset.Now);
		foreach (var (labelCommitId, label) in writtenLabels)
		{
			repository.WriteVersionLabel(labelCommitId, labelSignature, label);
		}

		repository.Compact();

		state.Entries = rewrittenEntries;
		state.LastCommitId = previousCommitId ?? condensedCommitId;
		state.Files.Clear();
		state.FilesSourceFolder = null;
		SaveState(session.Layout, state);

		return new MapHistoryCleanupResult
		{
			Success = true,
			CondensedVersionCount = prunedCount,
			RemainingVersionCount = rewrittenEntries.Count
		};
	}

	private static string BuildCondensedMessage(int condensedCount, string? newestLabel)
	{
		return string.IsNullOrWhiteSpace(newestLabel)
			? $"{condensedCount} earlier versions condensed"
			: $"{newestLabel} and {condensedCount - 1} earlier versions condensed";
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

		bool isDirectory = normalized.EndsWith('/');
		string trimmed = normalized.TrimEnd('/');

		foreach (string directoryName in ExcludedDirectoryNames)
		{
			if (trimmed.Equals(directoryName, StringComparison.OrdinalIgnoreCase)) return true;
			if (trimmed.StartsWith(directoryName + "/", StringComparison.OrdinalIgnoreCase)) return true;
			if (trimmed.Contains("/" + directoryName + "/", StringComparison.OrdinalIgnoreCase)) return true;
		}

		if (isDirectory) return false;

		foreach (string suffix in ExcludedFileSuffixes)
		{
			if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
		}

		string fileName = Path.GetFileName(trimmed);
		if (fileName.StartsWith("~", StringComparison.Ordinal)) return true;
		return fileName.Equals("WasmEntryPoint.cs", StringComparison.OrdinalIgnoreCase);
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
			MapVersionKind.SaveAs => "Save point (save as)",
			MapVersionKind.AutosaveCheckpoint => "Autosave checkpoint",
			MapVersionKind.BeforePublish => "Before publish",
			MapVersionKind.BeforeMapUpgrade => "Before map upgrade",
			MapVersionKind.BeforeRestore => "Before restore",
			MapVersionKind.RestoredVersion => "Restored version",
			MapVersionKind.Condensed => "Earlier versions",
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
		public string MapName { get; set; } = string.Empty;
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
