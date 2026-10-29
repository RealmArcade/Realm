using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Realm.Shared.Network;

public static class ServerArgumentFlags
{
	public const string MapFlag = "--map";
	public const string PortFlag = "--port";
	public const string TokenFlag = "--token";
	public const string LogFlag = "--log";

	public const string DataFlag = "--data";

	public const string SeedFlag = "--seed";

	public const string CheatsFlag = "--cheats";

	public const string WasmDebugFlag = "--wasm-debug";

	public const string WorkspaceFlag = "--workspace";

	public const string RosterFlag = "--roster";

	public const string RecordAuthorityFlag = "--record-authority";

	public const string ParentProcessIdFlag = "--parent-pid";

	public const string LobbyFlag = "--lobby";

	public const string AdminTokenFlag = "--admin-token";

	public const string MapNameFlag = "--map-name";

	public const string MaxPlayersFlag = "--max-players";
}

public record ServerLaunchTarget(string ExecutablePath, string? ProjectDirectory)
{
	public bool IsProject => !string.IsNullOrEmpty(ProjectDirectory);
}

public record ServerLaunchRequest(ServerLaunchTarget Target, string MapFolderPath, string LogPath, string Token, int Seed)
{
	public string? DataFolderPath { get; init; }

	public string? JoinTicket => string.IsNullOrEmpty(Token) ? null : Token;

	public bool Cheats { get; init; }

	public int Port { get; init; }

	public bool WasmDebug { get; init; }

	public string? MapWorkspacePath { get; init; }

	public string? RosterPath { get; init; }

	public string? AuthorityRecordFolderPath { get; init; }

	public bool Lobby { get; init; }

	public string AdminToken { get; init; } = string.Empty;

	public string MapName { get; init; } = string.Empty;

	public int MaxPlayers { get; init; }

	public int ReadyTimeoutMilliseconds { get; init; } = 60000;

	public int ShutdownTimeoutMilliseconds { get; init; } = 5000;
}

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

public readonly record struct ServerLaunchAttempt(ServerProcessLauncher? Launcher, string Failure)
{
	public bool Started => Launcher != null;
}

public class ServerProcessLauncher : IDisposable
{
	private const int ReadinessGraceAfterExitMilliseconds = 250;

	private readonly ServerLaunchRequest _request;
	private readonly Process _process;
	private readonly ServerConsoleCapture _console;
	private readonly ServerReadyInfo _readyInfo;
	private readonly Thread _standardOutputThread;
	private readonly Thread _standardErrorThread;

	private bool _shutdownRequested;
	private bool _disposed;

	private ServerProcessLauncher(ServerLaunchRequest request, Process process, ServerConsoleCapture console, ServerReadyInfo readyInfo, Thread standardOutputThread, Thread standardErrorThread)
	{
		_request = request;
		_process = process;
		_console = console;
		_readyInfo = readyInfo;
		_standardOutputThread = standardOutputThread;
		_standardErrorThread = standardErrorThread;
	}

	public int Port => _readyInfo.Port;

	public int Protocol => _readyInfo.Protocol;

	public string Build => _readyInfo.Build;

	public string MapHash => _readyInfo.MapHash;

	public string WireTableHash => _readyInfo.WireTableHash;

	public string LogPath => _request.LogPath;

	public string ConsoleLogPath => ServerConsoleCapture.ResolvePath(_request.LogPath);

	public bool IsRunning => !_process.HasExited;

	public int ProcessId => _process.Id;

	public int ExitCode => _process.HasExited ? _process.ExitCode : int.MinValue;

	public static bool TryLaunch(ServerLaunchRequest request, out ServerProcessLauncher? launcher, out string failure)
	{
		ServerLaunchAttempt attempt = TryLaunchAsync(request).GetAwaiter().GetResult();
		launcher = attempt.Launcher;
		failure = attempt.Failure;
		return attempt.Started;
	}

	public static async Task<ServerLaunchAttempt> TryLaunchAsync(ServerLaunchRequest request, CancellationToken cancellationToken = default)
	{
		if (!DescribeTargetProblem(request.Target, out string failure)) return new ServerLaunchAttempt(null, failure);
		if (!Directory.Exists(request.MapFolderPath)) return new ServerLaunchAttempt(null, $"the map folder '{request.MapFolderPath}' does not exist");

		if (!ServerConsoleCapture.TryOpen(request.LogPath, out ServerConsoleCapture? console, out failure) || console == null) return new ServerLaunchAttempt(null, failure);

		Process? process = StartProcess(request, out failure);
		if (process == null)
		{
			console.Dispose();
			return new ServerLaunchAttempt(null, failure);
		}

		var reported = new TaskCompletionSource<ServerReadyLine>(TaskCreationOptions.RunContinuationsAsynchronously);
		Thread standardOutputThread = StartOutputReader(process, console, reported);
		Thread standardErrorThread = StartReader(process.StandardError, console, "RealmListenServerError");

		ServerReadyLine ready = await WaitForReadyAsync(process, reported.Task, request.ReadyTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
		if (ready.Info == null)
		{
			failure = DescribeUnreadyLaunch(process, request, ready.Line ?? string.Empty);
			EndProcess(process, request.ShutdownTimeoutMilliseconds);
			console.Dispose();
			return new ServerLaunchAttempt(null, failure);
		}

		var launcher = new ServerProcessLauncher(request, process, console, ready.Info, standardOutputThread, standardErrorThread);
		return new ServerLaunchAttempt(launcher, string.Empty);
	}

	private static bool DescribeTargetProblem(ServerLaunchTarget target, out string failure)
	{
		failure = string.Empty;

		if (!File.Exists(target.ExecutablePath))
		{
			failure = $"the authority was not found at '{target.ExecutablePath}'";
			return false;
		}

		if (!target.IsProject) return true;

		string projectFile = Path.Combine(target.ProjectDirectory!, "project.godot");
		if (File.Exists(projectFile)) return true;

		failure = $"'{target.ProjectDirectory}' is not the authority's project: it holds no project.godot";
		return false;
	}

	private static Process? StartProcess(ServerLaunchRequest request, out string failure)
	{
		failure = string.Empty;

		var startInfo = new ProcessStartInfo(request.Target.ExecutablePath)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		startInfo.ArgumentList.Add("--headless");
		if (request.Target.IsProject)
		{
			startInfo.ArgumentList.Add("--path");
			startInfo.ArgumentList.Add(Path.GetFullPath(request.Target.ProjectDirectory!));
		}

		startInfo.ArgumentList.Add("--");
		startInfo.ArgumentList.Add(ServerArgumentFlags.MapFlag);
		startInfo.ArgumentList.Add(Path.GetFullPath(request.MapFolderPath));
		startInfo.ArgumentList.Add(ServerArgumentFlags.PortFlag);
		startInfo.ArgumentList.Add(request.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
		startInfo.ArgumentList.Add(ServerArgumentFlags.SeedFlag);
		startInfo.ArgumentList.Add(request.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
		if (request.JoinTicket != null)
		{
			startInfo.ArgumentList.Add(ServerArgumentFlags.TokenFlag);
			startInfo.ArgumentList.Add(request.JoinTicket);
		}
		startInfo.ArgumentList.Add(ServerArgumentFlags.ParentProcessIdFlag);
		startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
		startInfo.ArgumentList.Add(ServerArgumentFlags.LogFlag);
		startInfo.ArgumentList.Add(Path.GetFullPath(request.LogPath));
		if (!string.IsNullOrWhiteSpace(request.DataFolderPath))
		{
			startInfo.ArgumentList.Add(ServerArgumentFlags.DataFlag);
			startInfo.ArgumentList.Add(Path.GetFullPath(request.DataFolderPath));
		}
		if (request.Lobby)
		{
			startInfo.ArgumentList.Add(ServerArgumentFlags.LobbyFlag);
			startInfo.ArgumentList.Add(ServerArgumentFlags.AdminTokenFlag);
			startInfo.ArgumentList.Add(request.AdminToken);
			if (!string.IsNullOrWhiteSpace(request.MapName))
			{
				startInfo.ArgumentList.Add(ServerArgumentFlags.MapNameFlag);
				startInfo.ArgumentList.Add(request.MapName);
			}

			if (request.MaxPlayers > 0)
			{
				startInfo.ArgumentList.Add(ServerArgumentFlags.MaxPlayersFlag);
				startInfo.ArgumentList.Add(request.MaxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture));
			}
		}

		if (request.Cheats) startInfo.ArgumentList.Add(ServerArgumentFlags.CheatsFlag);
		if (request.WasmDebug) startInfo.ArgumentList.Add(ServerArgumentFlags.WasmDebugFlag);
		if (request.WasmDebug && !string.IsNullOrWhiteSpace(request.MapWorkspacePath))
		{
			startInfo.ArgumentList.Add(ServerArgumentFlags.WorkspaceFlag);
			startInfo.ArgumentList.Add(Path.GetFullPath(request.MapWorkspacePath));
		}
		if (!string.IsNullOrWhiteSpace(request.RosterPath))
		{
			startInfo.ArgumentList.Add(ServerArgumentFlags.RosterFlag);
			startInfo.ArgumentList.Add(Path.GetFullPath(request.RosterPath));
		}
		if (!string.IsNullOrWhiteSpace(request.AuthorityRecordFolderPath))
		{
			startInfo.ArgumentList.Add(ServerArgumentFlags.RecordAuthorityFlag);
			startInfo.ArgumentList.Add(Path.GetFullPath(request.AuthorityRecordFolderPath));
		}

		try
		{
			return Process.Start(startInfo);
		}
		catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
		{
			failure = $"the authority could not be started: {exception.Message}";
			return null;
		}
	}

	private readonly record struct ServerReadyLine(ServerReadyInfo? Info, string? Line);

	private static Thread StartOutputReader(Process process, ServerConsoleCapture console, TaskCompletionSource<ServerReadyLine> reported)
	{
		var reader = new Thread(() =>
		{
			try
			{
				string? line;
				while ((line = process.StandardOutput.ReadLine()) != null)
				{
					console.WriteLine(line);
					if (reported.Task.IsCompleted) continue;
					if (!ServerReadyInfo.TryParseReadyLine(line, out ServerReadyInfo? parsed)) continue;

					reported.TrySetResult(new ServerReadyLine(parsed, line));
				}
			}
			catch (IOException)
			{
			}
			catch (ObjectDisposedException)
			{
			}
			finally
			{
				reported.TrySetResult(default);
			}
		})
		{
			IsBackground = true,
			Name = "RealmListenServerOutput"
		};

		reader.Start();
		return reader;
	}

	private static async Task<ServerReadyLine> WaitForReadyAsync(Process process, Task<ServerReadyLine> reported, int timeoutMilliseconds, CancellationToken cancellationToken)
	{
		Task exited;
		try
		{
			exited = process.WaitForExitAsync(cancellationToken);
		}
		catch (InvalidOperationException)
		{
			exited = Task.CompletedTask;
		}

		Task finished = await Task.WhenAny(reported, exited, Task.Delay(timeoutMilliseconds, cancellationToken)).ConfigureAwait(false);
		if (finished == reported) return await reported.ConfigureAwait(false);

		Task grace = await Task.WhenAny(reported, Task.Delay(ReadinessGraceAfterExitMilliseconds, cancellationToken)).ConfigureAwait(false);
		return grace == reported ? await reported.ConfigureAwait(false) : default;
	}

	private static string DescribeUnreadyLaunch(Process process, ServerLaunchRequest request, string readyLine)
	{
		if (readyLine.Length > 0)
		{
			return $"the authority reported itself with a line this build does not understand: {readyLine}";
		}

		if (process.HasExited)
		{
			return $"the authority exited with code {process.ExitCode} before it reported that it was listening; see '{request.LogPath}' and '{ServerConsoleCapture.ResolvePath(request.LogPath)}'";
		}

		return $"the authority did not report that it was listening within {request.ReadyTimeoutMilliseconds}ms; see '{request.LogPath}' and '{ServerConsoleCapture.ResolvePath(request.LogPath)}'";
	}

	private static Thread StartReader(StreamReader reader, ServerConsoleCapture console, string threadName)
	{
		var thread = new Thread(() =>
		{
			try
			{
				string? line;
				while ((line = reader.ReadLine()) != null)
				{
					console.WriteLine(line);
				}
			}
			catch (IOException)
			{
			}
			catch (ObjectDisposedException)
			{
			}
		})
		{
			IsBackground = true,
			Name = threadName
		};

		thread.Start();
		return thread;
	}

	public void RequestShutdown()
	{
		if (_shutdownRequested) return;
		_shutdownRequested = true;

		EndProcess(_process, _request.ShutdownTimeoutMilliseconds);
	}

	private static void EndProcess(Process process, int shutdownTimeoutMilliseconds)
	{
		if (process.HasExited) return;

		try
		{
			process.Kill(entireProcessTree: true);
			process.WaitForExit(shutdownTimeoutMilliseconds);
		}
		catch (InvalidOperationException)
		{
		}
		catch (System.ComponentModel.Win32Exception)
		{
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		RequestShutdown();

		_standardOutputThread.Join(500);
		_standardErrorThread.Join(500);

		_process.Dispose();
		_console.Dispose();
	}
}
