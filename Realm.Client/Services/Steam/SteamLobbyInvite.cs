namespace Realm.Client.Services;

/// <summary>
///     An invitation this player accepted in Steam, carried down to the lobby id the invitation stood for. The inviter's
///     name is only what the confirmation shows, since the join itself is an ordinary registry join by lobby id.
/// </summary>
public readonly struct SteamLobbyInvite
{
	public SteamLobbyInvite(string gameLobbyId, string inviterName)
	{
		GameLobbyId = gameLobbyId;
		InviterName = inviterName;
	}

	public string GameLobbyId { get; }

	public string InviterName { get; }
}
