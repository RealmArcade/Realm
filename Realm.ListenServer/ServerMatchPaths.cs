using Realm.Shared.Maps;

namespace Realm.ListenServer;

public record ServerMatchPaths(string MapPath, string MapName, string DataDirectory, string LogDirectory)
{
	public string BaseLocaleDirectory => Path.Combine(AppContext.BaseDirectory, "locale");

	public string MapLocaleDirectory => Path.Combine(MapPath, "locale");

	public static ServerMatchPaths From(ServerArguments arguments)
	{
		string mapName = Path.GetFileName(Path.TrimEndingDirectorySeparator(arguments.MapPath));
		string? logDirectory = Path.GetDirectoryName(Path.GetFullPath(arguments.LogPath));
		return new ServerMatchPaths(arguments.MapPath, string.IsNullOrEmpty(mapName) ? "map" : mapName, arguments.DataPath, logDirectory ?? string.Empty);
	}

	public static ServerMatchPaths ForMap(LoadedMap map, string mapPath, string dataDirectory = "", string logDirectory = "")
	{
		string mapName = string.IsNullOrEmpty(map.Metadata.MapProperties?.MapName)
			? Path.GetFileName(Path.TrimEndingDirectorySeparator(mapPath))
			: map.Metadata.MapProperties!.MapName!;
		return new ServerMatchPaths(mapPath, string.IsNullOrEmpty(mapName) ? "map" : mapName, dataDirectory, logDirectory);
	}
}
