using Realm.Shared.Maps;

namespace Realm.ListenServer;

/// <summary>
///     Where a match's files are. A map's script asks for the text of one of its own keys and for data it saved under
///     its own name, and both are answered from the folders the authority was started against rather than from folders
///     either process happens to be running in.
/// </summary>
public record ServerMatchPaths(string MapPath, string MapName, string DataDirectory, string LogDirectory)
{
	/// <summary>Where the base text of the game itself is kept, beside the files the authority is installed with.</summary>
	public string BaseLocaleDirectory => Path.Combine(AppContext.BaseDirectory, "locale");

	/// <summary>Where a map keeps its own text, which is what a map's translated keys come from.</summary>
	public string MapLocaleDirectory => Path.Combine(MapPath, "locale");

	/// <summary>The paths a started authority simulates a map with, taken from the arguments it was started with.</summary>
	public static ServerMatchPaths From(ServerArguments arguments)
	{
		string mapName = Path.GetFileName(Path.TrimEndingDirectorySeparator(arguments.MapPath));
		string? logDirectory = Path.GetDirectoryName(Path.GetFullPath(arguments.LogPath));
		return new ServerMatchPaths(arguments.MapPath, string.IsNullOrEmpty(mapName) ? "map" : mapName, arguments.DataPath, logDirectory ?? string.Empty);
	}

	/// <summary>The same paths for a map loaded by something other than a started authority, such as a test.</summary>
	public static ServerMatchPaths ForMap(LoadedMap map, string mapPath, string dataDirectory = "", string logDirectory = "")
	{
		string mapName = string.IsNullOrEmpty(map.Metadata.MapProperties?.MapName)
			? Path.GetFileName(Path.TrimEndingDirectorySeparator(mapPath))
			: map.Metadata.MapProperties!.MapName!;
		return new ServerMatchPaths(mapPath, string.IsNullOrEmpty(mapName) ? "map" : mapName, dataDirectory, logDirectory);
	}
}
