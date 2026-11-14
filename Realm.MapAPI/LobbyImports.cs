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

	[DllImport(Module, EntryPoint = "set-lobby-slot-open")]
	[WasmImportLinkage]
	private static extern int ImportSetLobbySlotOpen(int playerIndex, int open);

	[DllImport(Module, EntryPoint = "set-lobby-open-slot-count")]
	[WasmImportLinkage]
	private static extern int ImportSetLobbyOpenSlotCount(int count);

	[DllImport(Module, EntryPoint = "add-lobby-match-setting")]
	[WasmImportLinkage]
	private static extern int ImportAddLobbyMatchSetting(int settingId, int control, nint labelPointer, int labelLength, nint tooltipPointer, int tooltipLength, int defaultValue);

	[DllImport(Module, EntryPoint = "add-lobby-match-setting-option")]
	[WasmImportLinkage]
	private static extern int ImportAddLobbyMatchSettingOption(int settingId, nint labelPointer, int labelLength);

	[DllImport(Module, EntryPoint = "set-lobby-match-setting-range")]
	[WasmImportLinkage]
	private static extern int ImportSetLobbyMatchSettingRange(int settingId, int minimum, int maximum);

	[DllImport(Module, EntryPoint = "get-lobby-match-setting-value")]
	[WasmImportLinkage]
	private static extern int ImportGetLobbyMatchSettingValue(int settingId);

	[DllImport(Module, EntryPoint = "set-lobby-spectator-delay-default")]
	[WasmImportLinkage]
	private static extern void ImportSetLobbySpectatorDelayDefault(int enabled);

	[DllImport(Module, EntryPoint = "set-lobby-shroud-mode-default")]
	[WasmImportLinkage]
	private static extern void ImportSetLobbyShroudModeDefault(int shroudMode);

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

	/// <summary>Places the order that opens or closes one player slot.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="open">Whether the slot accepts a player.</param>
	/// <returns>Whether the authority accepted the order.</returns>
	public static bool SetSlotOpen(int playerIndex, bool open)
	{
		return ImportSetLobbySlotOpen(playerIndex, open ? 1 : 0) != 0;
	}

	/// <summary>Places the order that opens the first slots up to a count and closes the rest.</summary>
	/// <param name="count">How many slots, counted from the first, the lobby should offer.</param>
	/// <returns>Whether the authority accepted the order.</returns>
	public static bool SetOpenSlotCount(int count)
	{
		return ImportSetLobbyOpenSlotCount(count) != 0;
	}

	/// <summary>Places the order that declares one match setting of the map's own.</summary>
	/// <param name="settingId">The identifier the map declares the setting under.</param>
	/// <param name="control">The control kind, as the value of the lobby setting control enum.</param>
	/// <param name="label">The label the host reads, passed as UTF-8 with its byte length.</param>
	/// <param name="tooltip">The sentence that explains the setting, or empty, passed as UTF-8 with its byte length.</param>
	/// <param name="defaultValue">The value the setting holds until the host changes it.</param>
	/// <returns>Whether the authority accepted the declaration.</returns>
	public static unsafe bool AddMatchSetting(int settingId, int control, string label, string tooltip, int defaultValue)
	{
		byte[] labelBytes = Encoding.UTF8.GetBytes(label);
		byte[] tooltipBytes = Encoding.UTF8.GetBytes(tooltip);
		fixed (byte* labelPointer = labelBytes)
		fixed (byte* tooltipPointer = tooltipBytes)
		{
			return ImportAddLobbyMatchSetting(settingId, control, (nint)labelPointer, labelBytes.Length, (nint)tooltipPointer, tooltipBytes.Length, defaultValue) != 0;
		}
	}

	/// <summary>Places the order that adds one option to a setting declared as a choice.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="optionLabel">The text the host reads in the dropdown, passed as UTF-8 with its byte length.</param>
	/// <returns>Whether the authority accepted the option.</returns>
	public static unsafe bool AddMatchSettingOption(int settingId, string optionLabel)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(optionLabel);
		fixed (byte* pointer = bytes) return ImportAddLobbyMatchSettingOption(settingId, (nint)pointer, bytes.Length) != 0;
	}

	/// <summary>Places the order that states the values a count setting may hold.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="minimum">The lowest value the host may choose.</param>
	/// <param name="maximum">The highest value the host may choose.</param>
	/// <returns>Whether the authority accepted the range.</returns>
	public static bool SetMatchSettingRange(int settingId, int minimum, int maximum)
	{
		return ImportSetLobbyMatchSettingRange(settingId, minimum, maximum) != 0;
	}

	/// <summary>Reads the value the host chose for one of the map's own settings.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <returns>The value the setting holds, or zero for a setting the lobby holds no value for.</returns>
	public static int GetMatchSettingValue(int settingId)
	{
		return ImportGetLobbyMatchSettingValue(settingId);
	}

	/// <summary>Places the order that states whether late joiners wait before they see the board.</summary>
	/// <param name="enabled">Whether the delay is on.</param>
	public static void SetSpectatorDelayDefault(int enabled)
	{
		ImportSetLobbySpectatorDelayDefault(enabled);
	}

	/// <summary>Places the order that states which shroud the lobby starts with.</summary>
	/// <param name="shroudMode">The mode, as the value of the match option the lobby publishes.</param>
	public static void SetShroudModeDefault(int shroudMode)
	{
		ImportSetLobbyShroudModeDefault(shroudMode);
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
