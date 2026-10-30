using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Shared.Network;

/// <summary>
///     The administrative channel a resident authority is driven through. One line is one command going in and one line
///     is one answer coming back, so a screen can put a match on its own authority, take it away again, or park it
///     without the process that holds it ever being started or stopped.
/// </summary>
public static class ServerControlProtocol
{
	public const string CommandLinePrefix = "REALM_CONTROL ";

	public const string ReplyLinePrefix = "REALM_CONTROL_REPLY ";

	public const string ResidentReadyLine = "REALM_RESIDENT_AUTHORITY_READY";

	public const string LogFlag = "--log";

	public const string DataFlag = "--data";

	public const string ParentProcessIdFlag = "--parent-pid";

	public const string Usage = $"{LogFlag} <log file> [{DataFlag} <folder a map's saved data belongs in>] [{ParentProcessIdFlag} <process id of the screen that started the authority>]";

	public static readonly JsonSerializerOptions Format = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new JsonStringEnumConverter() }
	};
}

/// <summary>
///     What a screen asks its resident authority to do with the one match it can hold.
/// </summary>
public enum ServerControlKind
{
	/// <summary>Puts a match on the authority, replacing whatever it held before.</summary>
	StartMatch,

	/// <summary>
	///     Parks the authority: the match it holds is given up, and with it the socket that match was listening on, so a
	///     screen that leaves a hosted lobby can reach somebody else's on the port it hosted with. The process itself stays
	///     up, which is the whole point of it: nothing is started or stopped between sessions.
	/// </summary>
	Pause,

	/// <summary>Answers with the state the authority is in, and the port of the match it holds when there is one.</summary>
	Report
}

public enum ServerAuthorityState
{
	Idle,
	Running,
	Paused
}

/// <summary>
///     One administrative command. Everything a match is started with travels here rather than on the authority's
///     command line, so the ticket that admits a lobby's administrator never becomes an argument another process on the
///     machine can read.
/// </summary>
public record ServerControlCommand
{
	public int RequestId { get; init; }

	public ServerControlKind Kind { get; init; } = ServerControlKind.Report;

	public string MapFolderPath { get; init; } = string.Empty;

	public int Port { get; init; }

	public int Seed { get; init; }

	public string Token { get; init; } = string.Empty;

	public bool Cheats { get; init; }

	public bool WasmDebug { get; init; }

	public string MapWorkspacePath { get; init; } = string.Empty;

	public string RosterPath { get; init; } = string.Empty;

	public string AuthorityRecordFolderPath { get; init; } = string.Empty;

	public bool Lobby { get; init; }

	public string AdminToken { get; init; } = string.Empty;

	public string MapName { get; init; } = string.Empty;

	public int MaxPlayers { get; init; }

	public string ToLine() => ServerControlProtocol.CommandLinePrefix + JsonSerializer.Serialize(this, ServerControlProtocol.Format);

	public static bool TryParseLine(string? line, out ServerControlCommand? command)
	{
		command = null;
		if (line == null || !line.StartsWith(ServerControlProtocol.CommandLinePrefix, StringComparison.Ordinal)) return false;

		string payload = line[ServerControlProtocol.CommandLinePrefix.Length..];
		command = JsonSerializer.Deserialize<ServerControlCommand>(payload, ServerControlProtocol.Format);
		return command != null;
	}
}

/// <summary>
///     What the authority did with a command, and what the screen needs to reach the match it now holds.
/// </summary>
public record ServerControlReply
{
	public int RequestId { get; init; }

	public bool Accepted { get; init; }

	public ServerAuthorityState State { get; init; }

	public string Failure { get; init; } = string.Empty;

	public int Port { get; init; }

	public string MapHash { get; init; } = string.Empty;

	public string WireTableHash { get; init; } = string.Empty;

	public string ToLine() => ServerControlProtocol.ReplyLinePrefix + JsonSerializer.Serialize(this, ServerControlProtocol.Format);

	public static bool TryParseLine(string? line, out ServerControlReply? reply)
	{
		reply = null;
		if (line == null || !line.StartsWith(ServerControlProtocol.ReplyLinePrefix, StringComparison.Ordinal)) return false;

		string payload = line[ServerControlProtocol.ReplyLinePrefix.Length..];
		reply = JsonSerializer.Deserialize<ServerControlReply>(payload, ServerControlProtocol.Format);
		return reply != null;
	}
}
