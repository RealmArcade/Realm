using System;
using System.IO;
using System.Text;
using Realm.Shared;

namespace Realm.ListenServerHost;

/// <summary>
///     Turns an unexpected failure in the authority into a file beside the authority's log instead of a process that
///     disappears. The authority runs on its own machine and its log is what the screen can hand back, so the crash record
///     travels with it and an export from the client picks it up the same way it picks up any other log.
/// </summary>
public static class AuthorityCrashCapture
{
	private static string _logPath = string.Empty;

	private static int _written;

	/// <summary>
	///     Installs the process-wide handler. It is idempotent because the authority's entry node can be rebuilt, and it
	///     writes at most one record per process: a failure during shutdown must not produce a second report over the first.
	/// </summary>
	/// <param name="logPath">The log file this authority is writing, whose folder receives the crash record.</param>
	public static void Install(string logPath)
	{
		_logPath = logPath;
		AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
	}

	private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs arguments)
	{
		if (arguments.ExceptionObject is not Exception exception) return;
		if (System.Threading.Interlocked.Exchange(ref _written, 1) != 0) return;

		try
		{
			string folder = Path.GetDirectoryName(_logPath) ?? AppContext.BaseDirectory;
			Directory.CreateDirectory(folder);

			string crashPath = Path.Combine(folder, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

			var text = new StringBuilder();
			text.AppendLine($"Realm authority {RealmVersion.GameBinaryVersion} ({RealmVersion.GameBuildNumber})");
			text.AppendLine("source: authority");
			text.AppendLine($"when: {DateTime.Now:O}");
			text.AppendLine($"pid: {Environment.ProcessId}");
			text.AppendLine($"exception: {exception.GetType().FullName}");
			text.AppendLine($"message: {exception.Message}");
			text.AppendLine();
			text.AppendLine("managed stack:");
			text.AppendLine(exception.StackTrace ?? "(no managed stack)");

			Exception? inner = exception.InnerException;
			while (inner != null)
			{
				text.AppendLine();
				text.AppendLine($"inner {inner.GetType().FullName}: {inner.Message}");
				text.AppendLine(inner.StackTrace ?? "(no managed stack)");
				inner = inner.InnerException;
			}

			File.WriteAllText(crashPath, text.ToString(), Encoding.UTF8);
			Console.Error.WriteLine($"[authority] crash record written to {crashPath}");
			Console.Error.Flush();
		}
		catch (Exception writingException)
		{
			Console.Error.WriteLine($"[authority] the crash record could not be written: {writingException.Message}");
		}
	}
}
