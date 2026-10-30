using System.Collections.Concurrent;

namespace Realm.ListenServer;

public class ServerSavedData : IDisposable
{
	private const string SavedDataFolderName = "saved_data";
	private const string BackupFolderName = "saved_data_backups";

	private const int MaximumPendingWrites = 256;
	private const int ShutdownJoinMilliseconds = 5000;

	private readonly string _dataDirectory;
	private readonly ServerLog _log;

	private readonly Dictionary<string, string> _contentsByPath = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, PendingWrite> _pendingByPath = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, byte> _queuedPaths = new(StringComparer.Ordinal);
	private readonly BlockingCollection<string> _pendingPaths = new(new ConcurrentQueue<string>(), MaximumPendingWrites);
	private readonly Thread _writerThread;

	private int _droppedWriteCount;
	private bool _disposed;

	public ServerSavedData(string dataDirectory, ServerLog log)
	{
		_dataDirectory = string.IsNullOrWhiteSpace(dataDirectory) ? AppContext.BaseDirectory : dataDirectory;
		_log = log;

		PreloadSavedData();

		_writerThread = new Thread(WritePendingFiles)
		{
			IsBackground = true,
			Name = "AuthoritySavedDataWriter"
		};
		_writerThread.Start();
	}

	public int DroppedWriteCount => Volatile.Read(ref _droppedWriteCount);

	public void Write(string mapName, string fileName, string content)
	{
		if (IsInvalidPathString(fileName) || IsInvalidPathString(MapNameOnly(mapName)))
		{
			_log.Write($"refused to write saved data '{fileName}': the name is not a plain file name");
			return;
		}

		string targetFile = TargetFile(mapName, fileName);
		_contentsByPath[targetFile] = content ?? string.Empty;
		Enqueue(targetFile, MapNameOnly(mapName), fileName, content ?? string.Empty);
	}

	public string Read(string mapName, string fileName)
	{
		if (IsInvalidPathString(fileName) || IsInvalidPathString(MapNameOnly(mapName))) return string.Empty;

		return _contentsByPath.TryGetValue(TargetFile(mapName, fileName), out string? content) ? content : string.Empty;
	}

	public string DirectoryFor(string mapName) => Path.Combine(_dataDirectory, SavedDataFolderName, MapNameOnly(mapName));

	private string TargetFile(string mapName, string fileName) => Path.Combine(DirectoryFor(mapName), fileName);

	private void PreloadSavedData()
	{
		string root = Path.Combine(_dataDirectory, SavedDataFolderName);
		try
		{
			if (!Directory.Exists(root)) return;

			string[] mapDirectories = Directory.GetDirectories(root);
			for (int directoryIndex = 0; directoryIndex < mapDirectories.Length; directoryIndex++)
			{
				string[] files = Directory.GetFiles(mapDirectories[directoryIndex]);
				for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
				{
					_contentsByPath[files[fileIndex]] = File.ReadAllText(files[fileIndex]);
				}
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_log.Write($"saved data could not be read ahead of the match: {exception.Message}");
		}
	}

	private void Enqueue(string targetFile, string mapName, string fileName, string content)
	{
		if (_disposed || _pendingPaths.IsAddingCompleted) return;

		_pendingByPath[targetFile] = new PendingWrite(mapName, fileName, content);

		// A path already queued is not queued twice: the write that is already waiting reads whatever the latest
		// content is, so a script that saves the same file every tick still costs one write per drain.
		if (!_queuedPaths.TryAdd(targetFile, 0)) return;

		if (_pendingPaths.TryAdd(targetFile)) return;

		_queuedPaths.TryRemove(targetFile, out _);
		Interlocked.Increment(ref _droppedWriteCount);
	}

	private void WritePendingFiles()
	{
		foreach (string targetFile in _pendingPaths.GetConsumingEnumerable())
		{
			_queuedPaths.TryRemove(targetFile, out _);
			if (!_pendingByPath.TryRemove(targetFile, out PendingWrite pending)) continue;

			try
			{
				WriteFile(pending, targetFile);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				_log.Write($"could not write saved data '{pending.FileName}': {exception.Message}");
				Interlocked.Increment(ref _droppedWriteCount);
			}
		}
	}

	private void WriteFile(PendingWrite pending, string targetFile)
	{
		string targetDirectory = DirectoryFor(pending.MapName);
		Directory.CreateDirectory(targetDirectory);

		CreateBackup(pending.MapName, pending.FileName, targetFile);
		File.WriteAllText(targetFile, pending.Content);
	}

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

	private readonly record struct PendingWrite(string MapName, string FileName, string Content);

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_pendingPaths.CompleteAdding();
		if (_writerThread.IsAlive) _writerThread.Join(ShutdownJoinMilliseconds);

		FlushRemainingWrites();

		_pendingPaths.Dispose();
	}

	private void FlushRemainingWrites()
	{
		foreach (KeyValuePair<string, PendingWrite> entry in _pendingByPath)
		{
			try
			{
				WriteFile(entry.Value, entry.Key);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				_log.Write($"could not write saved data '{entry.Value.FileName}' while stopping: {exception.Message}");
			}
		}

		_pendingByPath.Clear();
		_queuedPaths.Clear();
	}
}
