namespace Realm.Shared.Distribution;

public class ClusterStateDigestDto
{
	public string StateDigest { get; set; } = string.Empty;
	public Dictionary<string, string> CollectionHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, int> CollectionCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	public DateTime ServerTimestampUtc { get; set; } = DateTime.UtcNow;
	public string? OriginServerUrl { get; set; }
}