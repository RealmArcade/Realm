namespace Realm.Shared.Distribution;

public class CreatorSummaryDto
{
	public string PublicKey { get; set; } = string.Empty;
	public string Username { get; set; } = string.Empty;
	public string DonationLink { get; set; } = string.Empty;
	public string ContactInfo { get; set; } = string.Empty;
	public int PublishedMapCount { get; set; }
	public int AssetCount { get; set; }
}

public class CreatorAssetPackDto
{
	public string Title { get; set; } = string.Empty;
	public string AuthorUsername { get; set; } = string.Empty;
	public string AuthorPublicKey { get; set; } = string.Empty;
	public string RepresentativeHash { get; set; } = string.Empty;
	public int AssetCount { get; set; }
	public long TotalSizeBytes { get; set; }
}

public class CreatorPortfolioDto
{
	public string PublicKey { get; set; } = string.Empty;
	public string Username { get; set; } = string.Empty;
	public List<DiscoveryMapDto> Maps { get; set; } = new();
	public CreatorAssetPackDto? AssetPack { get; set; }
}
