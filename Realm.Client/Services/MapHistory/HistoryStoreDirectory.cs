using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LibGit2Sharp;
using Realm.Client.Core;

namespace Realm.Client.Services.MapHistory;

/// <summary>
///     Reading the history storage folder from outside: one entry per stored map (plus the ones recovered
///     from the damaged folder), their byte sizes and version counts, and removing a store once every
///     window that has it open has let go.
/// </summary>
internal static class HistoryStoreDirectory
{
	internal static IReadOnlyList<MapHistoryStoreInfo> ReadStoresOnDisk()
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

	internal static MapHistoryStoreInfo DescribeStore(string storeName, string storeDirectory, bool isRecovery)
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

	internal static MapHistoryStoreInfo DescribeUnreadableStore(string storeName, string storeDirectory, string? mapName, long totalBytes, bool isRecovery, bool isUnreadable)
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

	internal static string? TryReadStoredMapName(string storeDirectory)
	{
		try
		{
			string statePath = Path.Combine(storeDirectory, "state.json");
			if (!File.Exists(statePath)) return null;

			MapHistoryService.MapHistoryStateFile? state = JsonSerializer.Deserialize<MapHistoryService.MapHistoryStateFile>(File.ReadAllText(statePath), MapHistoryService.JsonOptions);
			return string.IsNullOrWhiteSpace(state?.MapName) ? null : state.MapName;
		}
		catch (Exception exception) when (exception is IOException or JsonException)
		{
			return null;
		}
	}

	internal static long MeasureDirectoryBytes(string directoryPath)
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

	internal static MapHistoryStoreDeleteResult DeleteStoreOnDisk(string storeDirectoryPath, MapHistoryMessages messages)
	{
		if (string.IsNullOrWhiteSpace(storeDirectoryPath))
		{
			return new MapHistoryStoreDeleteResult { Success = false, ErrorMessage = messages.NoHistoryFolderChosen };
		}

		string root = Path.GetFullPath(MapHistoryStore.GetRootDirectory());
		string fullPath = Path.GetFullPath(storeDirectoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			return new MapHistoryStoreDeleteResult { Success = false, ErrorMessage = messages.NotPartOfHistoryStorage };
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
						return new MapHistoryStoreDeleteResult { Success = false, ErrorMessage = messages.AnotherWindowUsesThisHistory };
					}
				}
			}
		}

		DeleteStoreDirectory(fullPath);

		return new MapHistoryStoreDeleteResult { Success = Directory.Exists(fullPath) == false, ErrorMessage = Directory.Exists(fullPath) ? messages.HistoryFolderNotRemoved : null };
	}

	internal static void DeleteStoreDirectory(string directoryPath)
	{
		try
		{
			foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
			{
				try
				{
					HistoryRestoreTransaction.ClearReadOnlyAttribute(filePath);
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
}
