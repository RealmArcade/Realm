using Realm.MapAPI;

namespace Realm.ListenServer;

/// <summary>
///     The lobby rules a map that brings no lobby script is played on. They offer the factions every lobby has, lock
///     nothing, and leave the colours and teams the lobby assigns on admission exactly as they are, so a lobby whose map
///     states no roster behaves the way a lobby always has.
/// </summary>
public static class DefaultLobbyRules
{
	public static void LobbyInitialize(ILobbyAPI api)
	{
		for (int index = 0; index < ServerLobbyRoster.DefaultFactions.Length; index++)
		{
			api.AddFaction(ServerLobbyRoster.DefaultFactions[index]);
		}
	}

	public static void LobbyUpdate(ILobbyAPI api, float deltaSeconds)
	{
	}
}
