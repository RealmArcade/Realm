namespace Realm.Shared.Distribution;

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