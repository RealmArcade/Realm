using System.Runtime.InteropServices;
using System.Text;

namespace Realm.MapAPI;

/// <summary>
///     The roster orders a map script places with the authority, declared by hand because the roster itself is published
///     in shared memory the script reads directly: only the orders a script makes cross the boundary.
/// </summary>
public static class LobbyImports
{
	private const string Module = "custom:game/game-api";

	[DllImport(Module, EntryPoint = "add-lobby-faction")]
	[WasmImportLinkage]
	private static extern int ImportAddLobbyFaction(nint factionNamePointer, int factionNameLength);

	[DllImport(Module, EntryPoint = "set-lobby-maximum-player-count")]
	[WasmImportLinkage]
	private static extern void ImportSetLobbyMaximumPlayerCount(int maximumPlayerCount);

	[DllImport(Module, EntryPoint = "set-lobby-team-count")]
	[WasmImportLinkage]
	private static extern void ImportSetLobbyTeamCount(int teamCount);

	[DllImport(Module, EntryPoint = "set-lobby-player-faction")]
	[WasmImportLinkage]
	private static extern int ImportSetLobbyPlayerFaction(int playerIndex, nint factionNamePointer, int factionNameLength);

	[DllImport(Module, EntryPoint = "set-lobby-player-team")]
	[WasmImportLinkage]
	private static extern int ImportSetLobbyPlayerTeam(int playerIndex, int teamIndex);

	[DllImport(Module, EntryPoint = "set-lobby-player-color")]
	[WasmImportLinkage]
	private static extern int ImportSetLobbyPlayerColor(int playerIndex, int colorRgba);

	[DllImport(Module, EntryPoint = "set-lobby-faction-locked")]
	[WasmImportLinkage]
	private static extern void ImportSetLobbyFactionLocked(int playerIndex, int locked);

	[DllImport(Module, EntryPoint = "set-lobby-team-locked")]
	[WasmImportLinkage]
	private static extern void ImportSetLobbyTeamLocked(int playerIndex, int locked);

	[DllImport(Module, EntryPoint = "set-lobby-color-locked")]
	[WasmImportLinkage]
	private static extern void ImportSetLobbyColorLocked(int playerIndex, int locked);

	[DllImport(Module, EntryPoint = "set-lobby-teams-together-default")]
	[WasmImportLinkage]
	private static extern void ImportSetLobbyTeamsTogetherDefault(int teamsTogether);

	[DllImport(Module, EntryPoint = "set-lobby-team-spawn-coordinate")]
	[WasmImportLinkage]
	private static extern int ImportSetLobbyTeamSpawnCoordinate(int teamIndex, nint coordinateNamePointer, int coordinateNameLength);

	/// <summary>Places the order that adds a named faction to the lobby roster.</summary>
	/// <param name="factionName">The faction's name, passed to the authority as UTF-8 with its byte length.</param>
	/// <returns>Whether the authority accepted the order.</returns>
	public static unsafe bool AddFaction(string factionName)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(factionName);
		fixed (byte* pointer = bytes) return ImportAddLobbyFaction((nint)pointer, bytes.Length) != 0;
	}

	/// <summary>Places the order that states how many players the lobby seats.</summary>
	/// <param name="maximumPlayerCount">The number of player slots the lobby should seat.</param>
	public static void SetMaximumPlayerCount(int maximumPlayerCount)
	{
		ImportSetLobbyMaximumPlayerCount(maximumPlayerCount);
	}

	/// <summary>Places the order that states how many teams the lobby offers.</summary>
	/// <param name="teamCount">The number of teams the lobby should offer.</param>
	public static void SetTeamCount(int teamCount)
	{
		ImportSetLobbyTeamCount(teamCount);
	}

	/// <summary>Places the order that gives a player slot a faction from the roster.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="factionName">The faction's name, passed to the authority as UTF-8 with its byte length.</param>
	/// <returns>Whether the authority accepted the order.</returns>
	public static unsafe bool SetPlayerFaction(int playerIndex, string factionName)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(factionName);
		fixed (byte* pointer = bytes) return ImportSetLobbyPlayerFaction(playerIndex, (nint)pointer, bytes.Length) != 0;
	}

	/// <summary>Places the order that puts a player slot on a team.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="teamIndex">The team number, or 0 for no team.</param>
	/// <returns>Whether the authority accepted the order.</returns>
	public static bool SetPlayerTeam(int playerIndex, int teamIndex)
	{
		return ImportSetLobbyPlayerTeam(playerIndex, teamIndex) != 0;
	}

	/// <summary>Places the order that gives a player slot a colour.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="colorRgba">The colour, packed as 0xRRGGBBAA.</param>
	/// <returns>Whether the authority accepted the order.</returns>
	public static bool SetPlayerColor(int playerIndex, uint colorRgba)
	{
		return ImportSetLobbyPlayerColor(playerIndex, (int)colorRgba) != 0;
	}

	/// <summary>Places the order that locks or unlocks a slot's faction.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="locked">Whether the field is locked.</param>
	public static void SetFactionLocked(int playerIndex, bool locked)
	{
		ImportSetLobbyFactionLocked(playerIndex, locked ? 1 : 0);
	}

	/// <summary>Places the order that locks or unlocks a slot's team.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="locked">Whether the field is locked.</param>
	public static void SetTeamLocked(int playerIndex, bool locked)
	{
		ImportSetLobbyTeamLocked(playerIndex, locked ? 1 : 0);
	}

	/// <summary>Places the order that locks or unlocks a slot's colour.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="locked">Whether the field is locked.</param>
	public static void SetColorLocked(int playerIndex, bool locked)
	{
		ImportSetLobbyColorLocked(playerIndex, locked ? 1 : 0);
	}

	/// <summary>Places the order that states whether the lobby's Teams Together checkbox starts checked.</summary>
	/// <param name="teamsTogether">Whether the checkbox starts checked.</param>
	public static void SetTeamsTogetherDefault(bool teamsTogether)
	{
		ImportSetLobbyTeamsTogetherDefault(teamsTogether ? 1 : 0);
	}

	/// <summary>Places the order that assigns a board coordinate to a team, or clears the team's assignment when the name is empty.</summary>
	/// <param name="teamIndex">The team number.</param>
	/// <param name="coordinateName">The board coordinate the team begins inside, passed as UTF-8 with its byte length.</param>
	/// <returns>Whether the authority accepted the order.</returns>
	public static unsafe bool SetTeamSpawnCoordinate(int teamIndex, string coordinateName)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(coordinateName);
		fixed (byte* pointer = bytes) return ImportSetLobbyTeamSpawnCoordinate(teamIndex, (nint)pointer, bytes.Length) != 0;
	}
}
