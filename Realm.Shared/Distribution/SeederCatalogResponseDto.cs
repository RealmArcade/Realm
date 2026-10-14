namespace Realm.Shared.Distribution;

public class SeederCatalogResponseDto
{
	public string SeederId { get; set; } = string.Empty;
	public int CapacityPercentage { get; set; } = 100;
	public List<string> AssetHashes { get; set; } = new();
}