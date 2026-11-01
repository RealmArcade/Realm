using System;

namespace Realm.Client.Services.MapHistory;

public enum MapVersionKind
{
	Initial,
	ManualSave,
	AutosaveCheckpoint,
	BeforePublish,
	BeforeMapUpgrade,
	BeforeRestore,
	RestoredVersion,
	ExternalChange,
	AfterMapUpgrade,
	BeforeAssetOptimization,
	AfterAssetOptimization
}

public class MapVersionEntry
{
	public string CommitId { get; init; } = string.Empty;
	public DateTimeOffset CreatedAt { get; init; }
	public MapVersionKind Kind { get; init; }
	public string? Label { get; init; }
	public long SaveVersion { get; init; }
	public int ChangedFileCount { get; init; }
	public string? SourceFolder { get; init; }
}

public class MapHistoryStatus
{
	public bool IsRepositoryReady { get; init; }
	public DateTimeOffset? LastVersionAt { get; init; }
	public int VersionCount { get; init; }
	public bool HasUnversionedChanges { get; init; }
	public string? Notice { get; init; }
	public string? ErrorMessage { get; init; }

	public static MapHistoryStatus Unavailable(string errorMessage)
	{
		return new MapHistoryStatus { IsRepositoryReady = false, ErrorMessage = errorMessage };
	}
}

public class MapHistoryRestoreResult
{
	public bool Success { get; init; }
	public bool VersionRecorded { get; init; }
	public string? ErrorMessage { get; init; }
	public MapVersionEntry? NewVersion { get; init; }
	public int FilesWritten { get; init; }
	public int FilesRemoved { get; init; }
	public bool TerrainRestored { get; init; }
	public bool ScriptSourcesRestored { get; init; }
	public bool MetadataRestored { get; init; }
}

public class MapHistoryVersionResult
{
	public bool Success { get; init; }
	public string? ErrorMessage { get; init; }
	public MapVersionEntry? Entry { get; init; }
}

public class MapHistoryStoreInfo
{
	public string StoreKey { get; init; } = string.Empty;
	public string DirectoryPath { get; init; } = string.Empty;
	public string? MapName { get; init; }
	public long TotalBytes { get; init; }
	public int VersionCount { get; init; }
	public DateTimeOffset? LastVersionAt { get; init; }
	public bool IsRecovery { get; init; }
	public bool IsUnreadable { get; init; }
}

public class MapHistoryStoreDeleteResult
{
	public bool Success { get; init; }
	public string? ErrorMessage { get; init; }
}

public class MapHistoryStorageInfo
{
	public long TotalBytes { get; init; }
	public long PackedBytes { get; init; }
	public int VersionCount { get; init; }
	public string? ErrorMessage { get; init; }
}

public class MapHistoryCleanupResult
{
	public string? ErrorMessage { get; init; }
	public int DroppedVersionCount { get; init; }

	public bool DroppedAnything => DroppedVersionCount > 0;
}
