using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     The one match a resident authority was told to hold: which map, who may join it, and where its reports go. It is
///     built from a control command rather than a command line, because the authority that runs it is already running.
/// </summary>
public record ServerArguments(string MapPath, int Port, string Token, string LogPath, string DataPath, int Seed, bool Cheats = false, bool WasmDebug = false, string WorkspacePath = "", string RosterPath = "", string RecordAuthorityPath = "", bool Lobby = false, string AdminToken = "", string MapName = "", int MaxPlayers = 0)
{
	public static ServerArguments From(ServerControlCommand command, string logPath, string dataPath)
	{
		string workspacePath = string.IsNullOrWhiteSpace(command.MapWorkspacePath) ? command.MapFolderPath : command.MapWorkspacePath;

		return new ServerArguments(
			command.MapFolderPath,
			command.Port,
			command.Token,
			logPath,
			dataPath,
			command.Seed,
			command.Cheats,
			command.WasmDebug,
			command.WasmDebug ? workspacePath : string.Empty,
			command.RosterPath,
			command.AuthorityRecordFolderPath,
			command.Lobby,
			command.AdminToken,
			command.MapName,
			command.MaxPlayers);
	}
}
