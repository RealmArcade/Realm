using Realm.Ecs;
using Realm.MapAPI;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     The authority's half of <see cref="ILobbyAPI" />: it answers from the lobby it is holding and applies every order to
///     the seats the players are sitting in, so a change a map script makes is what every screen is told about.
/// </summary>
public class ServerLobbyAPI : ILobbyAPI
{
	private readonly ServerLobbyRoster _roster;

	public ServerLobbyAPI(ServerLobbyRoster roster)
	{
		_roster = roster;
	}

	public int MaximumPlayerCount => _roster.MaximumPlayerCount;

	public int SlotCount => EcsSharedMemory.MAX_PLAYERS;

	public int FactionCount => _roster.FactionCount;

	public ReadOnlySpan<char> GetFactionName(int factionIndex) => _roster.GetFactionName(factionIndex).AsSpan();

	public bool AddFaction(string factionName) => _roster.AddFaction(factionName);

	public int GetPlayerFactionIndex(int playerIndex)
	{
		if (!_roster.TryGetSeat(playerIndex, out LobbySeat seat)) return -1;
		return _roster.IndexOfFaction(seat.Faction);
	}

	public ReadOnlySpan<char> GetPlayerFaction(int playerIndex)
	{
		if (!_roster.TryGetSeat(playerIndex, out LobbySeat seat)) return ReadOnlySpan<char>.Empty;
		return seat.Faction.AsSpan();
	}

	public bool SetPlayerFaction(int playerIndex, string factionName) => _roster.TrySetPlayerFaction(playerIndex, factionName);

	public bool IsFactionLocked(int playerIndex) => _roster.IsFactionLocked(playerIndex);

	public void SetFactionLocked(int playerIndex, bool locked) => _roster.SetFactionLocked(playerIndex, locked);

	public int GetPlayerTeam(int playerIndex)
	{
		return _roster.TryGetSeat(playerIndex, out LobbySeat seat) ? seat.Team : 0;
	}

	public bool SetPlayerTeam(int playerIndex, int teamIndex) => _roster.TrySetPlayerTeam(playerIndex, teamIndex);

	public bool IsTeamLocked(int playerIndex) => _roster.IsTeamLocked(playerIndex);

	public void SetTeamLocked(int playerIndex, bool locked) => _roster.SetTeamLocked(playerIndex, locked);

	public uint GetPlayerColor(int playerIndex)
	{
		return _roster.TryGetSeat(playerIndex, out LobbySeat seat) ? seat.ColorRgba : 0u;
	}

	public bool SetPlayerColor(int playerIndex, uint colorRgba) => _roster.TrySetPlayerColor(playerIndex, colorRgba);

	public bool IsColorLocked(int playerIndex) => _roster.IsColorLocked(playerIndex);

	public void SetColorLocked(int playerIndex, bool locked) => _roster.SetColorLocked(playerIndex, locked);

	public bool IsOccupied(int playerIndex) => _roster.TryGetSeat(playerIndex, out _);

	public bool IsComputer(int playerIndex)
	{
		return _roster.TryGetSeat(playerIndex, out LobbySeat seat) && seat.IsComputer;
	}

	public bool IsHost(int playerIndex)
	{
		return _roster.TryGetSeat(playerIndex, out LobbySeat seat) && seat.IsHost;
	}

	public void SetMaximumPlayerCount(int maximumPlayerCount) => _roster.TrySetMaximumPlayerCount(maximumPlayerCount);
}
