using System;

namespace Realm.Client.Services.MapHistory;

public enum MapVersionKind
{
	Initial,
	ManualSave,
	SaveAs,
	AutosaveCheckpoint,
	BeforePublish,
	BeforeMapUpgrade,
	BeforeRestore,
	RestoredVersion,
	ExternalChange
}

public class MapVersionEntry
{
	public string CommitId { get; init; } = string.Empty;
	public DateTimeOffset CreatedAt { get; init; }
	public MapVersionKind Kind { get; init; }
	public string? Label { get; init; }
	public long SaveVersion { get; init; }
	public int ChangedFileCount { get; init; }
	public long ChangedBytes { get; init; }
	public string? ReferenceName { get; init; }
}

public class MapHistoryStatus
{
	public bool IsRepositoryReady { get; init; }
	public string? LastVersionCommitId { get; init; }
	public DateTimeOffset? LastVersionAt { get; init; }
	public MapVersionKind? LastVersionKind { get; init; }
	public DateTimeOffset? LastBackupAt { get; init; }
	public bool BackupAvailable { get; init; }
	public string? ErrorMessage { get; init; }

	public static MapHistoryStatus Unavailable(string errorMessage)
	{
		return new MapHistoryStatus { IsRepositoryReady = false, ErrorMessage = errorMessage };
	}
}

public class MapHistoryRestoreResult
{
	public bool Success { get; init; }
	public string? ErrorMessage { get; init; }
	public MapVersionEntry? NewVersion { get; init; }
	public int FilesWritten { get; init; }
	public int FilesRemoved { get; init; }
	public bool TerrainRestored { get; init; }
	public bool MetadataRestored { get; init; }
}

public class MapHistoryRecoveryResult
{
	public bool Success { get; init; }
	public string? ErrorMessage { get; init; }
	public string? RecoveredMapDirectory { get; init; }
	public int RecoveredVersionCount { get; init; }
}

public class MapHistoryVersionResult
{
	public bool Success { get; init; }
	public string? ErrorMessage { get; init; }
	public MapVersionEntry? Entry { get; init; }
	public bool NothingToCommit { get; init; }
}
