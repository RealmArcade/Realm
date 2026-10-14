namespace Realm.Shared.Distribution;

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