using System;
using System.Collections.Generic;
using System.IO;

namespace Realm.Shared.Network;

public class ServerConsoleCapture : IDisposable
{
	private readonly StreamWriter _writer;
	private readonly object _writeLock = new();
	private bool _disposed;

	private ServerConsoleCapture(StreamWriter writer) => _writer = writer;

	public static string ResolvePath(string logPath) => logPath + ".console";

	public static string ReadLastLines(string logPath, int lineCount)
	{
		if (string.IsNullOrEmpty(logPath) || lineCount <= 0) return string.Empty;

		string path = ResolvePath(logPath);
		try
		{
			if (!File.Exists(path)) return string.Empty;

			string[] lines = File.ReadAllLines(path);
			var tail = new List<string>(lineCount);
			for (int index = lines.Length - 1; index >= 0 && tail.Count < lineCount; index--)
			{
				if (string.IsNullOrWhiteSpace(lines[index])) continue;
				tail.Insert(0, lines[index]);
			}

			return string.Join(Environment.NewLine, tail);
		}
		catch (IOException)
		{
			return string.Empty;
		}
		catch (UnauthorizedAccessException)
		{
			return string.Empty;
		}
	}

	public static bool TryOpen(string logPath, out ServerConsoleCapture? capture, out string failure)
	{
		capture = null;
		failure = string.Empty;

		string path = ResolvePath(logPath);
		try
		{
			string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
			if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

			var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
			capture = new ServerConsoleCapture(writer);
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			failure = $"the console capture '{path}' could not be opened: {exception.Message}";
			return false;
		}
	}

	public void WriteLine(string line)
	{
		if (_disposed) return;

		lock (_writeLock)
		{
			try
			{
				_writer.WriteLine(line);
			}
			catch (IOException)
			{
			}
			catch (ObjectDisposedException)
			{
			}
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_writer.Dispose();
	}
}
