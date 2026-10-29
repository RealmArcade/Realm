using Realm.MapAPI;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	public void WriteSavedData(string fileName, string content) => _savedData.Write(_paths.MapName, fileName, content);

	public string ReadSavedData(string fileName, string sourceMapName = "")
	{
		string mapName = string.IsNullOrWhiteSpace(sourceMapName) ? _paths.MapName : sourceMapName;
		return _savedData.Read(mapName, fileName);
	}

	public string SavedDataDirectory => _savedData.DirectoryFor(_paths.MapName);

	public void GenerateMapDirectory(string mapName, string? targetDirectory = null)
	{
		_log.Write($"a map asked the authority to generate a map folder for '{mapName}', which is an editor operation the authority does not perform");
	}
}
