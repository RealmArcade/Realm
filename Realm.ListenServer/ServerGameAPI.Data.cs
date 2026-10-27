using Realm.MapAPI;

namespace Realm.ListenServer;

/// <summary>
///     The data a map keeps between matches. A map names a file of its own and the authority writes it under the map's
///     name in the folder the launcher pointed at, so a map that remembers a player's progress remembers it in the same
///     place whether the match was started from the lobby or from the editor.
/// </summary>
public partial class ServerGameAPI
{
	/// <summary>Writes one of the map's own data files, keeping the copy it replaced.</summary>
	public void WriteSavedData(string fileName, string content) => _savedData.Write(_paths.MapName, fileName, content);

	/// <summary>
	///     One of the map's own data files, or nothing when the map has never written it. A map may name another map to
	///     read from, which is how one map reads the progress another one left behind.
	/// </summary>
	public string ReadSavedData(string fileName, string sourceMapName = "")
	{
		string mapName = string.IsNullOrWhiteSpace(sourceMapName) ? _paths.MapName : sourceMapName;
		return _savedData.Read(mapName, fileName);
	}

	/// <summary>Where this match keeps the map's saved data, so a log can say where a map's progress is going.</summary>
	public string SavedDataDirectory => _savedData.DirectoryFor(_paths.MapName);

	/// <summary>
	///     Writing a map's own folder from inside a match is an editor operation: the authority runs a map, it does not
	///     author one, so there is nothing for it to generate.
	/// </summary>
	public void GenerateMapDirectory(string mapName, string? targetDirectory = null)
	{
		_log.Write($"a map asked the authority to generate a map folder for '{mapName}', which is an editor operation the authority does not perform");
	}
}
