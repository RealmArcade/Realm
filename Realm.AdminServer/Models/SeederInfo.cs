namespace Realm.AdminServer.Models;

public class SeederInfo
{
	public required string SeederId { get; set; }
	public required string IP { get; set; }
	public required int Port { get; set; }
	public required List<string> MapIds { get; set; }
	public int CapacityPercentage { get; set; } = 100;
	public bool AcceptingUploads { get; set; } = true;
	public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}