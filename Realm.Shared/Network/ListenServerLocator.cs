using System;
using System.IO;

namespace Realm.Shared.Network;

/// <summary>
///     Where the authority's own executable is. A client never simulates a match, so every match it plays is run by a
///     process beside it: the client's build publishes that process into a folder of its own, and an exported build
///     ships the same folder next to the game. The search is deliberately narrow, because a client that guessed wrong
///     would start something that is not this build's authority.
/// </summary>
public static class ListenServerLocator
{
	/// <summary>The folder a client's build keeps the authority in, relative to the client's own folder.</summary>
	public const string FolderName = "Realm.ListenServer";

	/// <summary>The file name the authority is published as, without its platform extension.</summary>
	public const string ExecutableName = "Realm.ListenServer";

	/// <summary>
	///     The authority executable that belongs to the build the calling client is part of, or nothing when this build
	///     did not ship one. The client's own folder is searched first, then the folder the build publishes it into, so a
	///     development run and an exported build are both answered.
	/// </summary>
	public static bool TryFind(out string executablePath)
	{
		executablePath = string.Empty;

		string extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
		string clientDirectory = AppContext.BaseDirectory;

		string[] candidates =
		[
			Path.Combine(clientDirectory, FolderName, ExecutableName + extension),
			Path.Combine(clientDirectory, FolderName, ExecutableName),
			Path.Combine(clientDirectory, ExecutableName + extension)
		];

		for (int index = 0; index < candidates.Length; index++)
		{
			if (!File.Exists(candidates[index])) continue;

			executablePath = candidates[index];
			return true;
		}

		return false;
	}

	/// <summary>
	///     The command a client starts the authority for a match with, or nothing when this build shipped none. It names
	///     the executable so a failure can be reported as a missing file rather than as a process that would not start.
	/// </summary>
	public static bool TryDescribeLaunch(string mapFolderPath, string logPath, string token, string? dataFolderPath, string? language, out string executablePath, out ServerLaunchRequest request)
	{
		request = null!;
		if (!TryFind(out executablePath)) return false;

		request = new ServerLaunchRequest(executablePath, mapFolderPath, logPath, token)
		{
			DataFolderPath = dataFolderPath,
			Language = language
		};

		return true;
	}
}
