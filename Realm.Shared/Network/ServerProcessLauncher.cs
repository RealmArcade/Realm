using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Realm.Shared.Network;

/// <summary>The flags the authority is started with. Both processes read them from here, so the launcher and the parser cannot disagree.</summary>
public static class ServerArgumentFlags
{
	public const string MapFlag = "--map";
	public const string PortFlag = "--port";
	public const string TokenFlag = "--token";
	public const string LogFlag = "--log";

	/// <summary>
	///     Where the authority keeps data a map asked to persist between matches. The client passes the folder it keeps
	///     its own user data in, so a map's saved progress is the same progress whether it is read by an authority this
	///     machine started or by the editor.
	/// </summary>
	public const string DataFlag = "--data";

	/// <summary>
	///     The seed a match's scripts draw from. It is a separate value from the ticket a private match is joined with,
	///     so a match can be replayed from its seed by anyone the match is open to.
	/// </summary>
	public const string SeedFlag = "--seed";

	/// <summary>
	///     Whether this match lets a player cheat. Only a match this machine plays by itself is started with it, so a
	///     cheat typed at one of its screens is applied by the authority rather than refused. Such a match is reachable
	///     on the loopback address only and seats the single player it was started for, so enabling cheats cannot make a
	///     match anyone else can join and cheat in.
	/// </summary>
	public const string CheatsFlag = "--cheats";

	/// <summary>
	///     Whether the authority keeps the debug information of the map script it runs. Wasmtime only emits the JIT ELF a
	///     debugger loads when it was told to keep DWARF, so this is what makes a map script breakpoint-able. Only a map
	///     being tested from the editor is started with it: a normal match compiles its script for speed and writes no
	///     debug artifact.
	/// </summary>
	public const string WasmDebugFlag = "--wasm-debug";

	/// <summary>
	///     The map workspace the script being run was compiled from. The authority exports the script's JIT ELF into it, so
	///     the debugger configuration the workspace already carries finds the very module the running script was built from.
	/// </summary>
	public const string WorkspaceFlag = "--workspace";

	/// <summary>
	///     The file naming the players the match is played by. A map's script spawns every player's starting units while
	///     it initializes, which happens before the authority is listening, so the seats have to travel with the launch
	///     rather than arrive with the peers that fill them.
	/// </summary>
	public const string RosterFlag = "--roster";

	/// <summary>
	///     The folder the authority writes its own account of the match into: the world it described on every sequence it
	///     published, so a run can hold a screen against the process that decided the match. Only a run that asks for the
	///     record gets one; a normal match writes nothing here.
	/// </summary>
	public const string RecordPredictionFlag = "--record-prediction";

	/// <summary>
	///     The process that started this authority, or nothing for one nobody is waiting on. A match is over when the
	///     screen that started it is gone: without this the authority keeps listening on the port, simulating a world no
	///     one watches, until the machine is restarted. A dedicated or exported authority is started without it.
	/// </summary>
	public const string ParentProcessIdFlag = "--parent-pid";
}

/// <summary>
///     What to run in order to have an authority. The authority is a Godot process because ENet and RPC are the
///     engine's, so there are two shapes of it: a project the engine is pointed at while developing, and an exported
///     executable shipped beside the game.
/// </summary>
public record ServerLaunchTarget(string ExecutablePath, string? ProjectDirectory)
{
	/// <summary>Whether this target is a project the engine is pointed at rather than an exported executable.</summary>
	public bool IsProject => !string.IsNullOrEmpty(ProjectDirectory);
}

/// <summary>What a match needs in order to start its own authority: what to run, which map it runs, where it logs, and where it keeps the data a map asked to persist.</summary>
public record ServerLaunchRequest(ServerLaunchTarget Target, string MapFolderPath, string LogPath, string Token, int Seed)
{
	/// <summary>The folder the authority resolves a map's saved data under, or nothing to let it use its own folder.</summary>
	public string? DataFolderPath { get; init; }

	/// <summary>
	///     The ticket a peer has to present to be seated, or nothing for a match anyone may join. A match this machine
	///     started for its own client is private to that client; a dedicated authority serves whoever reaches it.
	/// </summary>
	public string? JoinTicket => string.IsNullOrEmpty(Token) ? null : Token;

	/// <summary>
	///     Whether this match lets a player cheat, which only a match played against the computer does. It is set for a
	///     single player match and for a map being tested from the editor, and the authority it starts is then reachable
	///     from this machine alone: a cheat belongs to the person playing by themselves, and a match open to others may
	///     not be started with one.
	/// </summary>
	public bool Cheats { get; init; }

	/// <summary>
	///     The port the authority listens on, or zero to let the operating system pick one. A match this machine plays by
	///     itself is reached on the port the authority reports, but a match other machines are told to join has to be
	///     listening where they were told to look.
	/// </summary>
	public int Port { get; init; }

	/// <summary>
	///     Whether the authority keeps the debug information of the map script and exports its JIT ELF, which is what lets a
	///     debugger attach to the script. Only a map being tested from the editor asks for it, so every other match compiles
	///     its script for speed and leaves no debug artifact behind.
	/// </summary>
	public bool WasmDebug { get; init; }

	/// <summary>
	///     The map workspace the script was compiled from, which is where the authority exports the JIT ELF a debugger loads.	///     Nothing means the authority works it out from where the script sits, which is the case for a map folder opened on
	///     its own.
	/// </summary>
	public string? MapWorkspacePath { get; init; }

	/// <summary>
	///     The file naming the players this match is played by, or nothing for a match seated as its peers connect. It is
	///     what lets a map's script see every seat - the ones the match plays itself included - while it initializes.
	/// </summary>
	public string? RosterPath { get; init; }

	/// <summary>
	///     The folder the authority writes its own account of every published sequence into, or nothing to write none. It
	///     is what a prediction report compares a screen's prediction against and what an ECS sync report compares a
	///     screen's applied world against, so a run that measures either asks for it and every other match leaves no such
	///     file behind.
	/// </summary>
	public string? PredictionRecordFolderPath { get; init; }

	/// <summary>How long the authority has to load its map and report that it is listening.</summary>
	public int ReadyTimeoutMilliseconds { get; init; } = 60000;

	/// <summary>How long a launched authority is given to leave on its own before it is ended.</summary>
	public int ShutdownTimeoutMilliseconds { get; init; } = 5000;
}

/// <summary>
///     What the authority prints on its console, captured beside the log it writes itself. The two are separate files
///     because two processes writing one file interleave their lines over each other's offsets.
/// </summary>
public class ServerConsoleCapture : IDisposable
{
	private readonly StreamWriter _writer;
	private bool _disposed;

	private ServerConsoleCapture(StreamWriter writer) => _writer = writer;

	/// <summary>Where the console output of the authority logging to this path is kept.</summary>
	public static string ResolvePath(string logPath) => logPath + ".console";

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

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_writer.Dispose();
	}
}

/// <summary>
///     Starts the authoritative process for one match. It waits for the one line the authority prints when it is
///     listening and keeps everything the authority prints afterwards, so a chatty process cannot fill the pipe it is
///     writing into. The connection itself is the client's: it is an ordinary ENet peer of the process this starts.
/// </summary>
public class ServerProcessLauncher : IDisposable
{
	private readonly ServerLaunchRequest _request;
	private readonly Process _process;
	private readonly ServerConsoleCapture _console;
	private readonly ServerReadyInfo _readyInfo;
	private readonly Thread _standardOutputThread;
	private readonly Thread _standardErrorThread;

	private bool _shutdownRequested;
	private bool _disposed;

	private ServerProcessLauncher(ServerLaunchRequest request, Process process, ServerConsoleCapture console, ServerReadyInfo readyInfo)
	{
		_request = request;
		_process = process;
		_console = console;
		_readyInfo = readyInfo;

		_standardOutputThread = StartReader(process.StandardOutput, "RealmListenServerOutput");
		_standardErrorThread = StartReader(process.StandardError, "RealmListenServerError");
	}

	public int Port => _readyInfo.Port;

	public int Protocol => _readyInfo.Protocol;

	public string Build => _readyInfo.Build;

	public string MapHash => _readyInfo.MapHash;

	public string WireTableHash => _readyInfo.WireTableHash;

	/// <summary>The log the authority writes for itself.</summary>
	public string LogPath => _request.LogPath;

	/// <summary>Where what the authority printed on its console was kept.</summary>
	public string ConsoleLogPath => ServerConsoleCapture.ResolvePath(_request.LogPath);

	/// <summary>Whether the authority is still there. A match that finds this false reports the loss rather than waiting.</summary>
	public bool IsRunning => !_process.HasExited;

	/// <summary>
	///     The identifier of the process running the match. A debugger attaches to this one, not to the screen the match is
	///     played on, because this is the process the map script runs inside.
	/// </summary>
	public int ProcessId => _process.Id;

	/// <summary>The exit code of an authority that has ended, or the smallest integer when it is still running.</summary>
	public int ExitCode => _process.HasExited ? _process.ExitCode : int.MinValue;

	/// <summary>
	///     Starts the authority and waits for it to report that it is listening, or reports why it could not, having
	///     ended what it started. Everything the authority printed goes to the match's console capture.
	/// </summary>
	public static bool TryLaunch(ServerLaunchRequest request, out ServerProcessLauncher? launcher, out string failure)
	{
		launcher = null;
		failure = string.Empty;

		if (!DescribeTargetProblem(request.Target, out failure)) return false;
		if (!Directory.Exists(request.MapFolderPath))
		{
			failure = $"the map folder '{request.MapFolderPath}' does not exist";
			return false;
		}

		if (!ServerConsoleCapture.TryOpen(request.LogPath, out ServerConsoleCapture? console, out failure) || console == null) return false;

		Process? process = StartProcess(request, out failure);
		if (process == null)
		{
			console.Dispose();
			return false;
		}

		ServerReadyInfo? readyInfo = ReadReadyLine(process, console, request.ReadyTimeoutMilliseconds, out string readyLine);
		if (readyInfo == null)
		{
			failure = DescribeUnreadyLaunch(process, request, readyLine);
			EndProcess(process, request.ShutdownTimeoutMilliseconds);
			console.Dispose();
			return false;
		}

		launcher = new ServerProcessLauncher(request, process, console, readyInfo);
		return true;
	}

	/// <summary>Whether the target names something that can actually be started, and why not when it cannot.</summary>
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

	/// <summary>
	///     Builds the command the authority is started with. A project is pointed at by the engine that is already
	///     running this client, and an exported authority is started directly; both are started headless, and both take
	///     their own arguments after the separator so the engine does not read them first.
	/// </summary>
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
		if (!string.IsNullOrWhiteSpace(request.PredictionRecordFolderPath))
		{
			startInfo.ArgumentList.Add(ServerArgumentFlags.RecordPredictionFlag);
			startInfo.ArgumentList.Add(Path.GetFullPath(request.PredictionRecordFolderPath));
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

	/// <summary>
	///     Reads the authority's output until it says it is listening. Its output keeps being drained afterwards, so a
	///     chatty process cannot fill the pipe it is writing into and stall behind it.
	/// </summary>
	private static ServerReadyInfo? ReadReadyLine(Process process, ServerConsoleCapture console, int timeoutMilliseconds, out string readyLine)
	{
		ServerReadyInfo? readyInfo = null;
		string? reportedLine = null;
		using var reported = new ManualResetEventSlim(false);

		var reader = new Thread(() =>
		{
			try
			{
				string? line;
				while ((line = process.StandardOutput.ReadLine()) != null)
				{
					console.WriteLine(line);
					if (reportedLine != null) continue;
					if (!ServerReadyInfo.TryParseReadyLine(line, out ServerReadyInfo? parsed)) continue;

					readyInfo = parsed;
					reportedLine = line;
					reported.Set();
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
				reported.Set();
			}
		})
		{
			IsBackground = true,
			Name = "RealmListenServerReadiness"
		};

		reader.Start();

		long deadline = Environment.TickCount64 + timeoutMilliseconds;
		while (reportedLine == null && Environment.TickCount64 < deadline)
		{
			if (process.HasExited) break;
			reported.Wait(50);
		}

		readyLine = reportedLine ?? string.Empty;
		return readyInfo;
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

	private Thread StartReader(StreamReader reader, string threadName)
	{
		var thread = new Thread(() =>
		{
			try
			{
				string? line;
				while ((line = reader.ReadLine()) != null)
				{
					_console.WriteLine(line);
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

	/// <summary>
	///     Ends the authority and gives it the time it was promised before it is ended. A match that has ended needs its
	///     authority gone, whether or not the authority agrees that it should be.
	/// </summary>
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
