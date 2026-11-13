namespace Realm.Client.UI;

public struct CreatorInfo
{
	public string PublicKey { get; set; }
	public string Username { get; set; }
	public string DonationLink { get; set; }
	public string ContactInfo { get; set; }
	public int PublishedMapCount { get; set; }
	public int AssetCount { get; set; }
	public int TotalGamesPlayed { get; set; }
	public bool IsRegistered { get; set; }
}
