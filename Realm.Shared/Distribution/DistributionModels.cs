using System;
using System.Collections.Generic;

namespace Realm.Shared.Distribution;

/// <summary>
/// Data transfer object for requesting a batched bundle of assets by their Blake3 hashes.
/// </summary>
public class AssetBundleRequestDto
{
    /// <summary>
    /// Gets or sets the list of normalized Blake3 asset hashes requested in the bundle.
    /// </summary>
    public List<string> Hashes { get; set; } = new();
}

public class SeederNodeDto
{
    public string SeederId { get; set; } = string.Empty;
    public string IP { get; set; } = string.Empty;
    public int Port { get; set; }
    public int CapacityPercentage { get; set; } = 100;
    public bool AcceptingUploads { get; set; } = true;
    public int StoredAssetCount { get; set; }
    public List<string> MapIds { get; set; } = new();
}

public class SeederRegisterRequestDto
{
    public string SeederId { get; set; } = string.Empty;
    public string ReportedIP { get; set; } = string.Empty;
    public int Port { get; set; }
    public int CapacityPercentage { get; set; } = 100;
    public bool AcceptingUploads { get; set; } = true;
    public List<string> MapIds { get; set; } = new();
}

public class SeederCatalogResponseDto
{
    public string SeederId { get; set; } = string.Empty;
    public int CapacityPercentage { get; set; } = 100;
    public List<string> AssetHashes { get; set; } = new();
}

public class AssetUploadResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool Deduplicated { get; set; }
    public bool Merged { get; set; }
    public string Blake3Hash { get; set; } = string.Empty;
}

public class MapPublishResponseDto
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> MissingAssetHashes { get; set; } = new();
}

public class HeaderSyncRequestDto
{
    public string Blake3Hash { get; set; } = string.Empty;
    public string? MetadataHeadersJson { get; set; }
    public string? AuthorPublicKey { get; set; }
    public string? AuthorSignature { get; set; }
}

public class HeaderSyncResponseDto
{
    public bool Updated { get; set; }
    public string CurrentMetadataHeadersJson { get; set; } = string.Empty;
}

public class BloomHeadersResponseDto
{
    public string SeederId { get; set; } = string.Empty;
    public int BitCount { get; set; }
    public int HashCount { get; set; }
    public int ItemCount { get; set; }
    public string FilterDataBase64 { get; set; } = string.Empty;
}

public class PublishMapRequest
{
    public string ManifestJson { get; set; } = string.Empty;
    public string MapJson { get; set; } = string.Empty;
    public List<string> ReferencedHashes { get; set; } = new();
    public string Signature { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
}

public class MapStatsSyncDto
{
    public string MapId { get; set; } = string.Empty;
    public int TotalPlaytimeMinutes { get; set; }
    public int TotalGamesPlayed { get; set; }
    public int TotalReviewsCount { get; set; }
    public int VerifiedGoodReviewsCount { get; set; }
    public double AverageRating { get; set; }
    public bool AdminOverrideGreenlit { get; set; }
}

public class PublishedMapSyncDto
{
    public string MapId { get; set; } = string.Empty;
    public string MapTitle { get; set; } = string.Empty;
    public string ManifestJson { get; set; } = string.Empty;
    public string OwnerPublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public List<string> ReferencedHashes { get; set; } = new();
}

public class CreatorSyncDto
{
    public string Username { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string DonationLink { get; set; } = string.Empty;
    public string ContactInfo { get; set; } = string.Empty;
    public string? AdminBypassToken { get; set; }
    public DateTime? RegisteredAt { get; set; }
}

public class ClusterEventDto
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string? OriginServerUrl { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public class MapPublishedEventPayload
{
    public string MapTitle { get; set; } = string.Empty;
    public string MapVersion { get; set; } = "1.0";
    public string ManifestJson { get; set; } = string.Empty;
    public List<string> ReferencedHashes { get; set; } = new();
    public string PublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public class CreatorRegisteredEventPayload
{
    public string Username { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string? DonationLink { get; set; }
    public string? ContactInfo { get; set; }
    public string? AdminBypassToken { get; set; }
    public DateTime? RegisteredAt { get; set; }
}

public class AdminGreenlightEventPayload
{
    public string MapTitle { get; set; } = string.Empty;
    public string? MapVersion { get; set; }
    public string AdminPublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public class AdminRemoveManifestRequest
{
    public string MapTitle { get; set; } = string.Empty;
    public string? MapVersion { get; set; }
    public string AdminPublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public class RemoveManifestResponseDto
{
    public bool Success { get; set; }
    public string MapTitle { get; set; } = string.Empty;
    public string? MapVersion { get; set; }
    public bool AllVersionsRemoved { get; set; }
    public int ManifestsDeleted { get; set; }
    public int DbRecordsRemoved { get; set; }
    public List<string> DeletedManifestFiles { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

public class AdminRemoveManifestEventPayload
{
    public string MapTitle { get; set; } = string.Empty;
    public string? MapVersion { get; set; }
    public string AdminPublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public class MapMetricReportEventPayload
{
    public string MapTitle { get; set; } = string.Empty;
    public string MapVersion { get; set; } = "1.0";
    public string PlayerId { get; set; } = string.Empty;
    public double PlaytimeMinutes { get; set; }
    public int Stars { get; set; }
    public bool IsCompleteGame { get; set; }
    public string? AuthProvider { get; set; }
    public string? AuthorPublicKey { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public class PublishMapInitiateRequest
{
    public string ManifestJson { get; set; } = string.Empty;
    public string MapTitle { get; set; } = string.Empty;
    public string MapVersion { get; set; } = "1.0";
    public string PublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public List<string> ReferencedHashes { get; set; } = new();
}

public class PublishMapInitiateResponse
{
    public bool Success { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> MissingHashes { get; set; } = new();
    public bool IsGreenlit { get; set; }
}

public class PublishMapFinalizeRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string MapTitle { get; set; } = string.Empty;
    public string MapVersion { get; set; } = "1.0";
    public string PublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public class AddMapMaintainerRequest
{
    public string MapTitle { get; set; } = string.Empty;
    public string MaintainerPublicKey { get; set; } = string.Empty;
    public string? MaintainerUsername { get; set; }
    public string RequesterPublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public class RemoveMapMaintainerRequest
{
    public string MapTitle { get; set; } = string.Empty;
    public string MaintainerPublicKey { get; set; } = string.Empty;
    public string RequesterPublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public class MapMaintainersResponseDto
{
    public bool Success { get; set; }
    public string MapTitle { get; set; } = string.Empty;
    public string OwnerPublicKey { get; set; } = string.Empty;
    public List<string> Maintainers { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

public class MapMaintainersUpdatedEventPayload
{
    public string MapTitle { get; set; } = string.Empty;
    public string Action { get; set; } = "add";
    public string MaintainerPublicKey { get; set; } = string.Empty;
    public string? MaintainerUsername { get; set; }
    public string RequesterPublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public class PublishMapFinalizeResponse
{
    public bool Success { get; set; }
    public string MapId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> MissingHashes { get; set; } = new();
}

public class CasPruneResponseDto
{
    public bool Success { get; set; }
    public int TotalScanned { get; set; }
    public int OrphansPruned { get; set; }
    public int CorruptPruned { get; set; }
    public long BytesFreed { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ClusterStateDigestDto
{
    public string StateDigest { get; set; } = string.Empty;
    public Dictionary<string, string> CollectionHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> CollectionCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime ServerTimestampUtc { get; set; } = DateTime.UtcNow;
    public string? OriginServerUrl { get; set; }
}

public class ClusterSnapshotDto
{
    public string StateDigest { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public List<CreatorSyncDto> Creators { get; set; } = new();
    public List<PublishedMapSyncDto> PublishedMaps { get; set; } = new();
    public Dictionary<string, string> MapOwnership { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> MapMaintainers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<MapStatsSyncDto> MapStats { get; set; } = new();
    public Dictionary<string, string> NameLocks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> AssetSignatures { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> PlayerEngagement { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class ApplySnapshotResponseDto
{
    public bool Success { get; set; }
    public string ComputedStateDigest { get; set; } = string.Empty;
    public string ExpectedStateDigest { get; set; } = string.Empty;
    public int CreatorsImported { get; set; }
    public int MapsImported { get; set; }
    public int StatsImported { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class SeederCircuitState
{
    private int _consecutiveFailures;
    private DateTime _openUntilUtc = DateTime.MinValue;

    public int ConsecutiveFailures => _consecutiveFailures;
    public DateTime OpenUntilUtc => _openUntilUtc;

    public bool IsOpen(DateTime nowUtc)
    {
        return _consecutiveFailures >= 3 && nowUtc < _openUntilUtc;
    }

    public void RecordSuccess()
    {
        System.Threading.Interlocked.Exchange(ref _consecutiveFailures, 0);
        _openUntilUtc = DateTime.MinValue;
    }

    public void RecordFailure(DateTime nowUtc)
    {
        int failures = System.Threading.Interlocked.Increment(ref _consecutiveFailures);
        if (failures >= 3)
        {
            int exponent = Math.Min(failures - 3, 5);
            int backoffSeconds = Math.Min(60, (int)Math.Pow(2, exponent) * 5);
            _openUntilUtc = nowUtc.AddSeconds(backoffSeconds);
        }
    }

    public void Reset()
    {
        System.Threading.Interlocked.Exchange(ref _consecutiveFailures, 0);
        _openUntilUtc = DateTime.MinValue;
    }
}

public class DiscoveryMapDto
{
    public string MapId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Creator { get; set; } = "Unknown";
    public string Description { get; set; } = string.Empty;
    public string Genre { get; set; } = "Custom Map";
    public string ThumbnailHash { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public List<string> Screenshots { get; set; } = new();
    public List<string> Features { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public float RatingStars { get; set; } = 5.0f;
    public int TotalReviews { get; set; }
    public int VerifiedGoodReviews { get; set; }
    public double AverageRating { get; set; } = 5.0;
    public int PlaytimeMinutes { get; set; }
    public int GamesPlayed { get; set; }
    public long TotalSizeBytes { get; set; }
    public string FileSizeFormatted { get; set; } = "0 MB";
    public string EngineVersion { get; set; } = "Godot Realm Engine v1.0";
    public string MaxPlayers { get; set; } = "8 Players";
    public bool IsGreenlit { get; set; } = true;
    public List<string> Awards { get; set; } = new();
}
