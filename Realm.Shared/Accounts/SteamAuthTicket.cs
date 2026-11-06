namespace Realm.Shared.Accounts;

/// <summary>
///     The identity a Steam auth session ticket is minted for. Steam binds a web-API ticket to the string the game asked for
///     it with, and the authority must name the same string when it exchanges the ticket, so both sides read it from here
///     instead of each carrying a copy that could drift.
/// </summary>
public static class SteamAuthTicket
{
	public const string Identity = "realm-game";
}
