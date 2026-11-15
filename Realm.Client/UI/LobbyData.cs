namespace Realm.Client.UI;

public struct LobbyData
{
	public string LobbyId;
	public string Map;
	public string Mode;
	public bool IsGreenlit;
	public string MapCreator;
	public string Players;
	public int Ping;
	public string GameVersion;
	public long MapSizeBytes;
	public string MapSizeFormatted;

	/// <summary>Whether this lobby verified an IPv4 endpoint another player can reach.</summary>
	public bool OffersIpv4;

	/// <summary>Whether this lobby verified an IPv6 endpoint another player can reach.</summary>
	public bool OffersIpv6;
}