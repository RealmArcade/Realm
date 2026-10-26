using System.Globalization;
using System.Text;

namespace Realm.ListenServer;

public class ServerLog : IDisposable
{
	private readonly StreamWriter _writer;
	private readonly object _writeLock = new();

	public ServerLog(string path)
	{
		string fullPath = Path.GetFullPath(path);
		string? directory = Path.GetDirectoryName(fullPath);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

		_writer = new StreamWriter(new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
		{
			AutoFlush = true
		};
	}

	public void Write(string message)
	{
		string line = $"{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)} {message}";
		lock (_writeLock)
		{
			_writer.WriteLine(line);
		}
	}

	public void Dispose()
	{
		lock (_writeLock)
		{
			_writer.Dispose();
		}
	}
}
