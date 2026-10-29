namespace Realm.ListenServer;

public class ServerSavedData
{
	private const string SavedDataFolderName = "saved_data";
	private const string BackupFolderName = "saved_data_backups";

	private readonly string _dataDirectory;
	private readonly ServerLog _log;

	public ServerSavedData(string dataDirectory, ServerLog log)
	{
		_dataDirectory = string.IsNullOrWhiteSpace(dataDirectory) ? AppContext.BaseDirectory : dataDirectory;
		_log = log;
	}

	public void Write(string mapName, string fileName, string content)
	{
		if (IsInvalidPathString(fileName) || IsInvalidPathString(MapNameOnly(mapName)))
		{
			_log.Write($"refused to write saved data '{fileName}': the name is not a plain file name");
			return;
		}

		try
		{
			string targetDirectory = Path.Combine(_dataDirectory, SavedDataFolderName, MapNameOnly(mapName));
			Directory.CreateDirectory(targetDirectory);
			string targetFile = Path.Combine(targetDirectory, fileName);

			CreateBackup(MapNameOnly(mapName), fileName, targetFile);
			File.WriteAllText(targetFile, content ?? string.Empty);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_log.Write($"could not write saved data '{fileName}': {exception.Message}");
		}
	}

	public string Read(string mapName, string fileName)
	{
		if (IsInvalidPathString(fileName) || IsInvalidPathString(MapNameOnly(mapName))) return string.Empty;

		try
		{
			string targetFile = Path.Combine(_dataDirectory, SavedDataFolderName, MapNameOnly(mapName), fileName);
			return File.Exists(targetFile) ? File.ReadAllText(targetFile) : string.Empty;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_log.Write($"could not read saved data '{fileName}': {exception.Message}");
			return string.Empty;
		}
	}

	public string DirectoryFor(string mapName) => Path.Combine(_dataDirectory, SavedDataFolderName, MapNameOnly(mapName));

	private void CreateBackup(string mapName, string fileName, string targetFile)
	{
		if (!File.Exists(targetFile)) return;

		try
		{
			string backupsDirectory = Path.Combine(_dataDirectory, BackupFolderName, mapName);
			Directory.CreateDirectory(backupsDirectory);

			string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
			string backupFile = $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}.rsav";
			File.Copy(targetFile, Path.Combine(backupsDirectory, backupFile), true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_log.Write($"could not keep a copy of saved data '{fileName}': {exception.Message}");
		}
	}

	private static bool IsInvalidPathString(string path)
	{
		return string.IsNullOrWhiteSpace(path) || path.Contains("..") || path.Contains('/') || path.Contains('\\') || path.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
	}

	private static string MapNameOnly(string mapName)
	{
		string name = Path.GetFileNameWithoutExtension(mapName);
		return string.IsNullOrWhiteSpace(name) ? "DefaultMap" : name;
	}
}
