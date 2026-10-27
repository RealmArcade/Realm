using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>The flags the authority is started with. Both processes read them from here, so the launcher and the parser cannot disagree.</summary>
public static class ServerArgumentFlags
{
	public const string MapFlag = "--map";
	public const string PortFlag = "--port";
	public const string TokenFlag = "--token";
	public const string LogFlag = "--log";
}

/// <summary>What a match needs in order to start its own authority: where the server is, which map it runs, and where it logs.</summary>
public record ServerLaunchRequest(string ExecutablePath, string MapFolderPath, string LogPath, string Token)
{
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
///     Starts the authoritative process for one match and connects this process to it. It waits for the one line the
///     authority prints when it is listening, connects to the port that line names, and sends the token the authority was
///     started with, so an authority can refuse a connection that did not come from its own launcher. The transport it
///     hands back is an ordinary peer: nothing about the match is shared in memory between the two processes.
/// </summary>
public class ServerProcessLauncher : IDisposable
{
	private readonly ServerLaunchRequest _request;
	private readonly Process _process;
	private readonly ServerConsoleCapture _console;
	private readonly LoopbackClientTransport _transport;
	private readonly ServerReadyInfo _readyInfo;
	private readonly Thread _standardOutputThread;
	private readonly Thread _standardErrorThread;

	private bool _shutdownRequested;
	private bool _disposed;

	private ServerProcessLauncher(ServerLaunchRequest request, Process process, ServerConsoleCapture console, LoopbackClientTransport transport, ServerReadyInfo readyInfo)
	{
		_request = request;
		_process = process;
		_console = console;
		_transport = transport;
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

	/// <summary>The peer's end of the connection to the authority, which the match writes orders to and reads snapshots from.</summary>
	public LoopbackClientTransport Transport => _transport;

	/// <summary>Whether the authority is still there. A match that finds this false reports the loss rather than waiting.</summary>
	public bool IsRunning => !_process.HasExited;

	/// <summary>The exit code of an authority that has ended, or the smallest integer when it is still running.</summary>
	public int ExitCode => _process.HasExited ? _process.ExitCode : int.MinValue;

	/// <summary>
	///     Starts the authority and connects to it, or reports why it could not, having ended what it started. Everything
	///     the authority printed goes to the match log, including the line this waits for.
	/// </summary>
	public static bool TryLaunch(ServerLaunchRequest request, out ServerProcessLauncher? launcher, out string failure)
	{
		launcher = null;
		failure = string.Empty;

		if (!File.Exists(request.ExecutablePath))
		{
			failure = $"the authority was not found at '{request.ExecutablePath}'";
			return false;
		}

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

		if (!LoopbackClientTransport.TryConnect("127.0.0.1", readyInfo.Port, request.ReadyTimeoutMilliseconds, out LoopbackClientTransport? transport, out string connectFailure) || transport == null)
		{
			failure = $"the authority reported port {readyInfo.Port} but the connection to it failed: {connectFailure}";
			EndProcess(process, request.ShutdownTimeoutMilliseconds);
			console.Dispose();
			return false;
		}

		if (!TryHandshake(transport, request, readyInfo, out string handshakeFailure))
		{
			failure = handshakeFailure;
			transport.Dispose();
			EndProcess(process, request.ShutdownTimeoutMilliseconds);
			console.Dispose();
			return false;
		}

		launcher = new ServerProcessLauncher(request, process, console, transport, readyInfo);
		return true;
	}

	private static Process? StartProcess(ServerLaunchRequest request, out string failure)
	{
		failure = string.Empty;

		var startInfo = new ProcessStartInfo(request.ExecutablePath)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		startInfo.ArgumentList.Add(ServerArgumentFlags.MapFlag);
		startInfo.ArgumentList.Add(Path.GetFullPath(request.MapFolderPath));
		startInfo.ArgumentList.Add(ServerArgumentFlags.PortFlag);
		startInfo.ArgumentList.Add("0");
		startInfo.ArgumentList.Add(ServerArgumentFlags.TokenFlag);
		startInfo.ArgumentList.Add(request.Token);
		startInfo.ArgumentList.Add(ServerArgumentFlags.LogFlag);
		startInfo.ArgumentList.Add(Path.GetFullPath(request.LogPath));

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

	private static bool TryHandshake(LoopbackClientTransport transport, ServerLaunchRequest request, ServerReadyInfo readyInfo, out string failure)
	{
		failure = string.Empty;

		var hello = new ClientHello
		{
			Protocol = RealmProtocol.Version,
			Build = readyInfo.Build,
			Token = request.Token,
			WireTableHash = readyInfo.WireTableHash
		};

		try
		{
			transport.Send(NetworkFrameKind.ClientHello, MemoryPackSerializer.Serialize(hello));
			if (!transport.TryReceive(request.ReadyTimeoutMilliseconds, out NetworkFrame frame))
			{
				failure = "the authority closed the connection before answering the hello";
				return false;
			}

			if (frame.Kind != NetworkFrameKind.ServerHello)
			{
				failure = $"the authority answered a hello with a {frame.Kind} frame";
				return false;
			}

			ServerHello response = MemoryPackSerializer.Deserialize<ServerHello>(frame.Payload);
			if (response.Accepted) return true;

			failure = $"the authority refused the connection: {response.Rejection}";
			return false;
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidDataException)
		{
			failure = $"the handshake with the authority failed: {exception.Message}";
			return false;
		}
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
	///     Asks the authority to leave and gives it the time it was promised before it is ended. A match that has ended
	///     needs its authority gone, whether or not the authority agrees that it should be.
	/// </summary>
	public void RequestShutdown()
	{
		if (_shutdownRequested) return;
		_shutdownRequested = true;

		try
		{
			_transport.Send(NetworkFrameKind.Shutdown, ReadOnlySpan<byte>.Empty);
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
		}

		if (_process.WaitForExit(_request.ShutdownTimeoutMilliseconds)) return;

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
		_transport.Dispose();

		_standardOutputThread.Join(500);
		_standardErrorThread.Join(500);

		_process.Dispose();
		_console.Dispose();
	}
}
