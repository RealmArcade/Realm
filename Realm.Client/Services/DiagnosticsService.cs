using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Godot;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     The local half of what the admin server used to report: the game writes its own logs, and this is the way a player
///     hands them to the developer. It builds one archive of the current session's logs and a short environment summary,
///     writes it beside the logs and reveals it. Nothing is uploaded, there is no telemetry, no session counter and no
///     player identifier in an exported file unless the player asks for the SteamID to be included.
/// </summary>
public static class DiagnosticsService
{
	private const int RetainedSessions = 10;

	private const long MaximumLogFolderBytes = 32L * 1024 * 1024;

	private const string CrashFilePrefix = "crash_";

	private const string CrashFileSuffix = ".txt";

	private const string AcknowledgedCrashFileName = "crash.acknowledged";

	/// <summary>The folder every session log, crash file and diagnostics archive is written to.</summary>
	public static string LogDirectory => Path.Combine(OS.GetUserDataDir(), "logs");

	/// <summary>
	///     The one-line result of the last match this screen finished, which an exported summary carries because a player who
	///     reports "the match ended badly" is usually reporting this line. It is empty until a match ends.
	/// </summary>
	public static string LastMatchSummary { get; set; } = string.Empty;

	/// <summary>
	///     The crash file a previous session left behind and the player has not been offered yet, or <c>null</c> when the last
	///     session ended normally. The offer is shown once: acknowledging it records that it was seen, so a crash from a week
	///     ago cannot greet a player on every launch.
	/// </summary>
	public static string? UnacknowledgedCrashReport
	{
		get
		{
			try
			{
				if (!Directory.Exists(LogDirectory)) return null;

				string newest = Directory.GetFiles(LogDirectory, $"{CrashFilePrefix}*{CrashFileSuffix}")
					.OrderByDescending(File.GetLastWriteTimeUtc)
					.FirstOrDefault() ?? string.Empty;

				if (newest.Length == 0) return null;

				string acknowledgedPath = Path.Combine(LogDirectory, AcknowledgedCrashFileName);
				if (File.Exists(acknowledgedPath)
				    && string.Equals(File.ReadAllText(acknowledgedPath).Trim(), Path.GetFileName(newest), StringComparison.Ordinal))
				{
					return null;
				}

				return newest;
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[Diagnostics] the last session's crash record could not be read: {exception.Message}");
				return null;
			}
		}
	}

	/// <summary>Records that the player has seen the last session's crash, so the offer is not repeated.</summary>
	/// <param name="crashPath">The crash file that was offered.</param>
	public static void AcknowledgeCrashReport(string crashPath)
	{
		try
		{
			Directory.CreateDirectory(LogDirectory);
			File.WriteAllText(Path.Combine(LogDirectory, AcknowledgedCrashFileName), Path.GetFileName(crashPath));
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Diagnostics] the crash record could not be acknowledged: {exception.Message}");
		}
	}

	/// <summary>Opens the folder the logs are written to in the platform's file manager.</summary>
	public static void OpenLogFolder()
	{
		try
		{
			Directory.CreateDirectory(LogDirectory);
			OS.ShellOpen(LogDirectory);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Diagnostics] the log folder could not be opened: {exception.Message}");
		}
	}

	/// <summary>
	///     Writes one archive holding the session's logs and a short environment summary, then reveals it. The summary
	///     includes the SteamID only when the player asked for it.
	/// </summary>
	/// <param name="includeSteamId">Whether the player chose to include their SteamID64 in the summary.</param>
	/// <returns>The path of the archive, or an empty string when it could not be written.</returns>
	public static string ExportDiagnostics(bool includeSteamId)
	{
		try
		{
			Directory.CreateDirectory(LogDirectory);

			string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string archivePath = Path.Combine(LogDirectory, $"diagnostics_{stamp}.zip");
			string summaryPath = Path.Combine(Path.GetTempPath(), $"realm_diagnostics_{stamp}.txt");

			File.WriteAllText(summaryPath, BuildEnvironmentSummary(includeSteamId), Encoding.UTF8);

			using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
			{
				archive.CreateEntryFromFile(summaryPath, "environment.txt");

				foreach (string logPath in EnumerateSessionLogs())
				{
					try
					{
						archive.CreateEntryFromFile(logPath, $"logs/{Path.GetFileName(logPath)}");
					}
					catch (IOException exception)
					{
						GameLog.LogError($"[Diagnostics] '{Path.GetFileName(logPath)}' could not be added while it was still open: {exception.Message}");
					}
				}
			}

			try
			{
				File.Delete(summaryPath);
			}
			catch (IOException)
			{
			}

			OS.ShellShowInFileManager(archivePath);
			GameLog.LogToConsole($"[Diagnostics] exported {archivePath}");
			return archivePath;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Diagnostics] the export failed: {exception.Message}");
			return string.Empty;
		}
	}

	/// <summary>
	///     Writes a crash file for a failure that took the process down or escaped every handler. It is deliberately small:
	///     the exception, its managed stack, the last lines of the newest log and the environment summary. Nothing here runs on
	///     a simulation tick.
	/// </summary>
	/// <param name="source">Which process and subsystem failed, such as "client" or "authority".</param>
	/// <param name="exception">The failure that escaped.</param>
	/// <returns>The path of the crash file, or an empty string when it could not be written.</returns>
	public static string RecordCrash(string source, Exception exception)
	{
		try
		{
			Directory.CreateDirectory(LogDirectory);

			string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string crashPath = Path.Combine(LogDirectory, $"{CrashFilePrefix}{stamp}{CrashFileSuffix}");

			var text = new StringBuilder();
			text.AppendLine($"Realm {RealmVersion.GameBinaryVersion} ({RealmVersion.GameBuildNumber})");
			text.AppendLine($"source: {source}");
			text.AppendLine($"when: {DateTime.Now:O}");
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

			text.AppendLine();
			text.AppendLine("last log lines:");
			text.AppendLine(ReadTailOfNewestLog(80));
			text.AppendLine();
			text.AppendLine("environment:");
			text.AppendLine(BuildEnvironmentSummary(includeSteamId: false));

			File.WriteAllText(crashPath, text.ToString(), Encoding.UTF8);
			GameLog.LogError($"[Diagnostics] a crash record was written to {crashPath}");
			return crashPath;
		}
		catch (Exception writingException)
		{
			GameLog.LogError($"[Diagnostics] the crash record could not be written: {writingException.Message}");
			return string.Empty;
		}
	}

	/// <summary>
	///     Installs the process-wide handlers that turn an unexpected failure into a crash file instead of a window that
	///     disappears. A crash record is written once per failure and the process keeps its ordinary shutdown.
	/// </summary>
	/// <param name="source">Which process is installing the handlers, such as "client" or "authority".</param>
	public static void InstallCrashHandlers(string source)
	{
		AppDomain.CurrentDomain.UnhandledException += (_, arguments) =>
		{
			if (arguments.ExceptionObject is Exception exception) RecordCrash(source, exception);
		};

		System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, arguments) =>
		{
			RecordCrash(source, arguments.Exception);
			arguments.SetObserved();
		};
	}

	/// <summary>
	///     Keeps the log folder bounded: the newest sessions are kept and the oldest are deleted once either the session count
	///     or the folder's total size passes its cap. It runs at startup, before the new session writes anything, so a
	///     player's disk cannot fill up from the game alone.
	/// </summary>
	public static void TrimLogFolder()
	{
		try
		{
			if (!Directory.Exists(LogDirectory)) return;

			List<FileInfo> files = new DirectoryInfo(LogDirectory)
				.GetFiles("*", SearchOption.TopDirectoryOnly)
				.OrderByDescending(file => file.LastWriteTimeUtc)
				.ToList();

			long runningBytes = 0;
			int keptSessions = 0;
			var toDelete = new List<FileInfo>();

			foreach (FileInfo file in files)
			{
				// A crash file and the archive built from it are one session's record, so the session budget is counted by
				// the crash files and the size budget covers everything the folder holds.
				if (file.Name.StartsWith(CrashFilePrefix, StringComparison.Ordinal))
				{
					keptSessions++;
					if (keptSessions > RetainedSessions)
					{
						toDelete.Add(file);
						continue;
					}
				}

				runningBytes += file.Length;
				if (runningBytes > MaximumLogFolderBytes && keptSessions >= RetainedSessions) toDelete.Add(file);
			}

			foreach (FileInfo file in toDelete)
			{
				try
				{
					file.Delete();
				}
				catch (IOException)
				{
				}
			}
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Diagnostics] the log folder could not be trimmed: {exception.Message}");
		}
	}

	private static IEnumerable<string> EnumerateSessionLogs()
	{
		var logs = new List<string>();

		try
		{
			foreach (string path in System.IO.Directory.GetFiles(LogDirectory))
			{
				string extension = System.IO.Path.GetExtension(path);
				if (extension is ".log" or ".txt") logs.Add(path);
			}
		}
		catch (IOException exception)
		{
			GameLog.LogError($"[Diagnostics] the log folder could not be listed: {exception.Message}");
		}

		string authorityLogPath = GameContext.Lobby?.LastAuthorityLogPath ?? string.Empty;
		if (authorityLogPath.Length > 0 && File.Exists(authorityLogPath)) logs.Add(authorityLogPath);

		string consoleCapturePath = ServerConsoleCapture.ResolvePath(authorityLogPath);
		if (consoleCapturePath.Length > 0 && File.Exists(consoleCapturePath)) logs.Add(consoleCapturePath);

		return logs.Distinct(StringComparer.OrdinalIgnoreCase).Take(12);
	}

	private static string ReadTailOfNewestLog(int lines)
	{
		try
		{
			string? newest = Directory.Exists(LogDirectory)
				? Directory.GetFiles(LogDirectory, "*.log")
					.OrderByDescending(File.GetLastWriteTimeUtc)
					.FirstOrDefault()
				: null;

			if (newest == null) return "(no log to read)";

			using var stream = new FileStream(newest, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var reader = new StreamReader(stream, Encoding.UTF8);

			var tail = new Queue<string>(lines);
			string? line;
			while ((line = reader.ReadLine()) != null)
			{
				if (tail.Count == lines) tail.Dequeue();
				tail.Enqueue(line);
			}

			return string.Join(System.Environment.NewLine, tail);
		}
		catch (Exception exception)
		{
			return $"(the newest log could not be read: {exception.Message})";
		}
	}

	private static string BuildEnvironmentSummary(bool includeSteamId)
	{
		var summary = new StringBuilder();

		summary.AppendLine($"game build: {RealmVersion.GameBinaryVersion} ({RealmVersion.GameBuildNumber})");
		summary.AppendLine($"platform: {OS.GetName()} {OS.GetVersion()} / locale {OS.GetLocale()}");
		summary.AppendLine($"language: {TranslationServer.GetLocale()}");

		try
		{
			summary.AppendLine($"gpu: {RenderingServer.GetVideoAdapterVendor()} {RenderingServer.GetVideoAdapterName()}");
			summary.AppendLine($"renderer: {RenderingServer.GetVideoAdapterApiVersion()}");
		}
		catch (Exception exception)
		{
			summary.AppendLine($"gpu: unavailable ({exception.Message})");
		}

		summary.AppendLine($"steam initialized: {GameContext.Steam.IsAvailable}");
		summary.AppendLine($"steam application: {(GameContext.Steam.IsAvailable ? GameContext.Steam.AppId.ToString() : "none")}");
		summary.AppendLine($"steam identity included: {includeSteamId}");
		if (includeSteamId && GameContext.Steam.IsAvailable) summary.AppendLine($"steam id: {GameContext.Steam.SteamId}");

		summary.AppendLine($"client pid: {System.Environment.ProcessId}");

		LocalAuthority localAuthority = GameContext.LocalAuthority;
		summary.AppendLine($"authority pid: {(localAuthority.ProcessId > 0 ? localAuthority.ProcessId.ToString() : "not running")}");
		if (!string.IsNullOrEmpty(localAuthority.LogPath)) summary.AppendLine($"authority log: {localAuthority.LogPath}");

		summary.AppendLine($"map: {(string.IsNullOrEmpty(GameContext.ActiveMapName) ? "none loaded" : GameContext.ActiveMapName)}");

		LobbyManager? lobby = GameContext.Lobby;
		if (lobby != null)
		{
			summary.AppendLine($"lobby: {(string.IsNullOrEmpty(lobby.ActiveLobbyId) ? "not in one" : lobby.ActiveLobbyId)}");
		}

		if (!string.IsNullOrEmpty(LastMatchSummary))
		{
			summary.AppendLine($"last match result: {LastMatchSummary}");
		}

		return summary.ToString();
	}
}
