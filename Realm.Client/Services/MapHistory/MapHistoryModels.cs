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
	ExternalChange,
	Condensed
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
	public string? SourceFolder { get; init; }
}

public class MapHistoryStatus
{
	public bool IsRepositoryReady { get; init; }
	public string? LastVersionCommitId { get; init; }
	public DateTimeOffset? LastVersionAt { get; init; }
	public MapVersionKind? LastVersionKind { get; init; }
	public int VersionCount { get; init; }
	public string? StoreDirectory { get; init; }
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
	public string? ErrorMessage { get; init; }
	public MapVersionEntry? NewVersion { get; init; }
	public int FilesWritten { get; init; }
	public int FilesRemoved { get; init; }
	public bool TerrainRestored { get; init; }
	public bool MetadataRestored { get; init; }
}

public class MapHistoryVersionResult
{
	public bool Success { get; init; }
	public string? ErrorMessage { get; init; }
	public MapVersionEntry? Entry { get; init; }
	public bool NothingToCommit { get; init; }
}

public class MapHistoryStorageInfo
{
	public long TotalBytes { get; init; }
	public long LooseObjectBytes { get; init; }
	public long PackedBytes { get; init; }
	public int LooseObjectCount { get; init; }
	public int ObjectPackCount { get; init; }
	public int VersionCount { get; init; }
	public bool WasCompacted { get; init; }
	public string? ErrorMessage { get; init; }
}

public class MapHistoryRetention
{
	public int KeepDays { get; init; }
	public int KeepVersions { get; init; }
	public bool IsEnabled => KeepDays > 0 || KeepVersions > 0;

	public bool ShouldKeep(int rankFromNewest, TimeSpan age)
	{
		if (rankFromNewest <= 0) return true;
		if (KeepDays > 0 && age.TotalDays <= KeepDays) return true;
		if (KeepVersions > 0 && rankFromNewest < KeepVersions) return true;
		return false;
	}
}

public class MapHistoryCleanupResult
{
	public bool Success { get; init; }
	public string? ErrorMessage { get; init; }
	public int CondensedVersionCount { get; init; }
	public int RemainingVersionCount { get; init; }

	public bool CondensedAnything => CondensedVersionCount > 0;
}
