using System.Runtime.InteropServices;
using System.Text;

namespace Realm.MapAPI;

/// <summary>
///     The roster orders a map script places with the authority, declared by hand because the roster itself is published
///     in shared memory the script reads directly: only the orders a script makes cross the boundary.
/// </summary>
internal static class LobbyImports
{
	private const string Module = "custom:game/game-api";

	[DllImport(Module, EntryPoint = "add-lobby-faction")]
	[WasmImportLinkage]
	private static extern int ImportAddLobbyFaction(nint factionNamePointer, int factionNameLength);

	[DllImport(Module, EntryPoint = "set-lobby-maximum-player-count")]
	[WasmImportLinkage]
	private static extern void ImportSetLobbyMaximumPlayerCount(int maximumPlayerCount);

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

	public static unsafe bool AddFaction(string factionName)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(factionName);
		fixed (byte* pointer = bytes) return ImportAddLobbyFaction((nint)pointer, bytes.Length) != 0;
	}

	public static void SetMaximumPlayerCount(int maximumPlayerCount)
	{
		ImportSetLobbyMaximumPlayerCount(maximumPlayerCount);
	}

	public static unsafe bool SetPlayerFaction(int playerIndex, string factionName)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(factionName);
		fixed (byte* pointer = bytes) return ImportSetLobbyPlayerFaction(playerIndex, (nint)pointer, bytes.Length) != 0;
	}

	public static bool SetPlayerTeam(int playerIndex, int teamIndex)
	{
		return ImportSetLobbyPlayerTeam(playerIndex, teamIndex) != 0;
	}

	public static bool SetPlayerColor(int playerIndex, uint colorRgba)
	{
		return ImportSetLobbyPlayerColor(playerIndex, (int)colorRgba) != 0;
	}

	public static void SetFactionLocked(int playerIndex, bool locked)
	{
		ImportSetLobbyFactionLocked(playerIndex, locked ? 1 : 0);
	}

	public static void SetTeamLocked(int playerIndex, bool locked)
	{
		ImportSetLobbyTeamLocked(playerIndex, locked ? 1 : 0);
	}

	public static void SetColorLocked(int playerIndex, bool locked)
	{
		ImportSetLobbyColorLocked(playerIndex, locked ? 1 : 0);
	}
}
