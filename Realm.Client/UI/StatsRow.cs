namespace Realm.Client.UI;

public struct StatsRow
{
	public string Rank;
	public bool IsFirstPlace;
	public string Player;
	public string Faction;
	public string Col3;
	public string Col4;
	public string Col5;

	/// <summary>
	///     The SteamID64 the seat's player stated, or 0 for a computer seat or a player who is not running inside Steam. It is
	///     what the row's friend-request button names to Steam.
	/// </summary>
	public ulong SteamId;

	/// <summary>Whether the seat is played by a computer, which has nobody behind it to add.</summary>
	public bool IsComputer;
}
