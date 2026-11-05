namespace Realm.Client.Services;

/// <summary>
///     One entry of this player's Steam friends list, as the invite screen lists it. The Steam id is the only address an
///     invitation can be sent to, so it travels with the name instead of the screen looking the friend up by name.
/// </summary>
public readonly struct SteamFriend
{
	public SteamFriend(ulong steamId, string personaName, bool isOnline, bool isPlayingRealm)
	{
		SteamId = steamId;
		PersonaName = personaName;
		IsOnline = isOnline;
		IsPlayingRealm = isPlayingRealm;
	}

	public ulong SteamId { get; }

	public string PersonaName { get; }

	public bool IsOnline { get; }

	public bool IsPlayingRealm { get; }
}
