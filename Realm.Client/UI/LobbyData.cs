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

	/// <summary>
	///     How reachable this lobby's game port was shown to be when its host published it: reachable by a player
	///     without punching, reachable only after both sides punch, or not shown to be reachable at all.
	/// </summary>
	public string ReachabilityClass;

	/// <summary>Whether this lobby verified an IPv4 endpoint another player can reach.</summary>
	public bool OffersIpv4;

	/// <summary>Whether this lobby verified an IPv6 endpoint another player can reach.</summary>
	public bool OffersIpv6;
}