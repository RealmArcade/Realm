using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Realm.ListenServer;

/// <summary>
///     The authority's own log. Writing a line is a queue insertion, so a tick that has something to report - a dropped
///     order, a peer that stopped answering - cannot be held for the disk, and the formatting and the write happen on a
///     writer thread. The queue is bounded and a line that arrives when it is full is counted and dropped rather than
///     waiting for room, because a stalled tick is worse than a missing log line.
/// </summary>
public class ServerLog : IDisposable
{
	private const int MaximumPendingLines = 4096;
	private const int ShutdownJoinMilliseconds = 5000;

	private readonly StreamWriter _writer;
	private readonly BlockingCollection<string> _pendingLines = new(new ConcurrentQueue<string>(), MaximumPendingLines);
	private readonly Thread _writerThread;
	private int _droppedLineCount;
	private bool _disposed;

	public ServerLog(string path)
	{
		string fullPath = Path.GetFullPath(path);
		string? directory = Path.GetDirectoryName(fullPath);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

		_writer = new StreamWriter(new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
		{
			AutoFlush = true
		};

		_writerThread = new Thread(WritePendingLines)
		{
			IsBackground = true,
			Name = "AuthorityLogWriter"
		};
		_writerThread.Start();
	}

	public int DroppedLineCount => Volatile.Read(ref _droppedLineCount);

	public void Write(string message)
	{
		if (_pendingLines.IsAddingCompleted) return;
		if (!_pendingLines.TryAdd(message)) Interlocked.Increment(ref _droppedLineCount);
	}

	private void WritePendingLines()
	{
		foreach (string message in _pendingLines.GetConsumingEnumerable())
		{
			try
			{
				_writer.WriteLine($"{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)} {message}");
			}
			catch (IOException)
			{
				// The log is the reporter, not the thing being reported: a full or removed disk stops the lines, not
				// the match, and the count of what was dropped stays readable in the queue.
				Interlocked.Increment(ref _droppedLineCount);
			}
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_pendingLines.CompleteAdding();
		if (_writerThread.IsAlive) _writerThread.Join(ShutdownJoinMilliseconds);
		_writer.Dispose();
		_pendingLines.Dispose();
	}
}
