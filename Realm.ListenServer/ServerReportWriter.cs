using System.Collections.Concurrent;
using System.IO;
using Realm.Shared.Diagnostics;

namespace Realm.ListenServer;

public class ServerReportWriter : IDisposable
{
	private const int MaximumPendingReports = 4;
	private const int ShutdownJoinMilliseconds = 5000;

	private readonly BlockingCollection<(string Folder, SnapshotReportFile[] Files)> _pendingReports = new(new ConcurrentQueue<(string Folder, SnapshotReportFile[] Files)>(), MaximumPendingReports);
	private readonly Thread _writerThread;
	private int _droppedReportCount;
	private bool _disposed;

	public ServerReportWriter()
	{
		_writerThread = new Thread(WritePendingReports)
		{
			IsBackground = true,
			Name = "AuthorityReportWriter"
		};
		_writerThread.Start();
	}

	public int DroppedReportCount => Volatile.Read(ref _droppedReportCount);

	public void Enqueue(string folder, SnapshotReportFile[] files)
	{
		if (_disposed || _pendingReports.IsAddingCompleted) return;

		if (!_pendingReports.TryAdd((folder, files))) Interlocked.Increment(ref _droppedReportCount);
	}

	private void WritePendingReports()
	{
		foreach ((string folder, SnapshotReportFile[] files) in _pendingReports.GetConsumingEnumerable())
		{
			try
			{
				Directory.CreateDirectory(folder);
				for (int index = 0; index < files.Length; index++)
				{
					WriteAtomically(Path.Combine(folder, files[index].FileName), files[index].Contents);
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				Interlocked.Increment(ref _droppedReportCount);
			}
		}
	}

	private static void WriteAtomically(string path, string contents)
	{
		string temporaryPath = path + ".tmp";
		File.WriteAllText(temporaryPath, contents);
		File.Move(temporaryPath, path, overwrite: true);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_pendingReports.CompleteAdding();
		if (_writerThread.IsAlive) _writerThread.Join(ShutdownJoinMilliseconds);
		_pendingReports.Dispose();
	}
}
