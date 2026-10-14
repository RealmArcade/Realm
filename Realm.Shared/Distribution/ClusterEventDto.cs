namespace Realm.Shared.Distribution;

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