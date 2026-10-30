using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Realm.Shared.Network;

public record ServerLaunchTarget(string ExecutablePath, string? ProjectDirectory)
{
	public bool IsProject => !string.IsNullOrEmpty(ProjectDirectory);
}

/// <summary>
///     What a screen needs to know to bring its own authority up. A match is not part of it: the authority comes up with
///     nothing to run and is handed one over its control channel, so the same process serves every session the screen
///     has until the screen itself ends.
/// </summary>
public record ResidentAuthorityLaunchRequest(ServerLaunchTarget Target, string LogPath, string DataFolderPath)
{
	public int ParentProcessId { get; init; } = Environment.ProcessId;

	public int ReadyTimeoutMilliseconds { get; init; } = 60000;

	public int ShutdownTimeoutMilliseconds { get; init; } = 5000;

	public int CommandTimeoutMilliseconds { get; init; } = 120000;
}

public readonly record struct ResidentAuthorityLaunchAttempt(ResidentAuthority? Authority, string Failure)
{
	public bool Started => Authority != null;
}

/// <summary>
///     The authority this screen runs itself, kept for as long as the screen runs. It is started once and never
///     restarted between sessions: a match is put on it, paused in place, resumed or replaced through
///     <see cref="ServerControlProtocol" />, which leaves the cost of coming up - a process, a runtime, a warm load of the
///     engine - paid once rather than once per match.
/// </summary>
public class ResidentAuthority : IDisposable
{
	private const int ReadinessGraceAfterExitMilliseconds = 250;

	private const int WriterJoinMilliseconds = 500;

	private const int MaximumPendingCommands = 16;

	private readonly ResidentAuthorityLaunchRequest _request;
	private readonly Process _process;
	private readonly ServerConsoleCapture _console;
	private readonly BlockingCollection<string> _pendingCommandLines;
	private readonly ConcurrentDictionary<int, TaskCompletionSource<ServerControlReply>> _pendingReplies;
	private readonly Thread _commandWriterThread;
	private readonly Thread _standardOutputThread;
	private readonly Thread _standardErrorThread;

	private int _nextRequestId;
	private bool _disposed;

	private ResidentAuthority(ResidentAuthorityLaunchRequest request, Process process, ServerConsoleCapture console, BlockingCollection<string> pendingCommandLines, ConcurrentDictionary<int, TaskCompletionSource<ServerControlReply>> pendingReplies, Thread commandWriterThread, Thread standardOutputThread, Thread standardErrorThread)
	{
		_request = request;
		_process = process;
		_console = console;
		_pendingCommandLines = pendingCommandLines;
		_pendingReplies = pendingReplies;
		_commandWriterThread = commandWriterThread;
		_standardOutputThread = standardOutputThread;
		_standardErrorThread = standardErrorThread;
	}

	public string LogPath => _request.LogPath;

	public string ConsoleLogPath => ServerConsoleCapture.ResolvePath(_request.LogPath);

	public int ProcessId => _process.Id;

	public bool IsRunning
	{
		get
		{
			try
			{
				return !_process.HasExited;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}
	}

	public int ExitCode
	{
		get
		{
			try
			{
				return _process.HasExited ? _process.ExitCode : int.MinValue;
			}
			catch (InvalidOperationException)
			{
				return int.MinValue;
			}
		}
	}

	public static async Task<ResidentAuthorityLaunchAttempt> LaunchAsync(ResidentAuthorityLaunchRequest request, CancellationToken cancellationToken = default)
	{
		if (!DescribeTargetProblem(request.Target, out string failure)) return new ResidentAuthorityLaunchAttempt(null, failure);
		if (!ServerConsoleCapture.TryOpen(request.LogPath, out ServerConsoleCapture? console, out failure) || console == null) return new ResidentAuthorityLaunchAttempt(null, failure);

		Process? process = StartProcess(request, out failure);
		if (process == null)
		{
			console.Dispose();
			return new ResidentAuthorityLaunchAttempt(null, failure);
		}

		var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var replies = new ConcurrentDictionary<int, TaskCompletionSource<ServerControlReply>>();
		var pendingCommandLines = new BlockingCollection<string>(new ConcurrentQueue<string>(), MaximumPendingCommands);
		Thread standardOutputThread = StartOutputReader(process, console, ready, replies);
		Thread standardErrorThread = StartErrorReader(process, console);
		Thread commandWriterThread = StartCommandWriter(process, pendingCommandLines);

		var authority = new ResidentAuthority(request, process, console, pendingCommandLines, replies, commandWriterThread, standardOutputThread, standardErrorThread);

		bool started = await WaitForReadyAsync(process, ready.Task, request.ReadyTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
		if (!started)
		{
			failure = DescribeUnreadyLaunch(process, request);
			authority.Dispose();
			return new ResidentAuthorityLaunchAttempt(null, failure);
		}

		return new ResidentAuthorityLaunchAttempt(authority, string.Empty);
	}

	/// <summary>
	///     Hands one command over and waits for the answer that belongs to it. The command travels on a queue a writer
	///     thread drains, so a screen asking for a match is never held by the authority's input pipe.
	/// </summary>
	public Task<ServerControlReply> SendAsync(ServerControlCommand command)
	{
		if (_disposed || !IsRunning) return Task.FromResult(Refused(command.RequestId, DescribeStop()));

		int requestId = Interlocked.Increment(ref _nextRequestId);
		ServerControlCommand addressed = command with { RequestId = requestId };
		var pending = new TaskCompletionSource<ServerControlReply>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pendingReplies[requestId] = pending;

		if (!_pendingCommandLines.TryAdd(addressed.ToLine()))
		{
			_pendingReplies.TryRemove(requestId, out _);
			return Task.FromResult(Refused(requestId, "the local authority is not taking commands right now"));
		}

		return AwaitReplyAsync(requestId, pending);
	}

	private async Task<ServerControlReply> AwaitReplyAsync(int requestId, TaskCompletionSource<ServerControlReply> pending)
	{
		Task answered = await Task.WhenAny(pending.Task, Task.Delay(_request.CommandTimeoutMilliseconds)).ConfigureAwait(false);
		if (answered == pending.Task) return await pending.Task.ConfigureAwait(false);

		_pendingReplies.TryRemove(requestId, out _);
		return Refused(requestId, $"the local authority did not answer within {_request.CommandTimeoutMilliseconds}ms; see '{LogPath}' and '{ConsoleLogPath}'");
	}

	public string DescribeStop()
	{
		if (IsRunning) return "the local authority is running but did not answer";

		int exitCode = ExitCode;
		return exitCode == int.MinValue
			? $"the local authority is not running; see '{LogPath}' and '{ConsoleLogPath}'"
			: $"the local authority process ended with code {exitCode}; see '{LogPath}' and '{ConsoleLogPath}'";
	}

	private static ServerControlReply Refused(int requestId, string failure) => new()
	{
		RequestId = requestId,
		Accepted = false,
		State = ServerAuthorityState.Idle,
		Failure = failure
	};

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

	private static Process? StartProcess(ResidentAuthorityLaunchRequest request, out string failure)
	{
		failure = string.Empty;

		var startInfo = new ProcessStartInfo(request.Target.ExecutablePath)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true
		};

		startInfo.ArgumentList.Add("--headless");
		if (request.Target.IsProject)
		{
			startInfo.ArgumentList.Add("--path");
			startInfo.ArgumentList.Add(Path.GetFullPath(request.Target.ProjectDirectory!));
		}

		startInfo.ArgumentList.Add("--");
		startInfo.ArgumentList.Add(ServerControlProtocol.LogFlag);
		startInfo.ArgumentList.Add(Path.GetFullPath(request.LogPath));
		if (!string.IsNullOrWhiteSpace(request.DataFolderPath))
		{
			startInfo.ArgumentList.Add(ServerControlProtocol.DataFlag);
			startInfo.ArgumentList.Add(Path.GetFullPath(request.DataFolderPath));
		}

		if (request.ParentProcessId > 0)
		{
			startInfo.ArgumentList.Add(ServerControlProtocol.ParentProcessIdFlag);
			startInfo.ArgumentList.Add(request.ParentProcessId.ToString(CultureInfo.InvariantCulture));
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

	private static Thread StartCommandWriter(Process process, BlockingCollection<string> pendingCommandLines)
	{
		// The queue this thread drains is the authority's only door, and it is drained here rather than on the caller's
		// thread so a match starting never waits on the pipe that carries its own start.
		var writer = new Thread(() =>
		{
			try
			{
				foreach (string line in pendingCommandLines.GetConsumingEnumerable())
				{
					process.StandardInput.WriteLine(line);
					process.StandardInput.Flush();
				}
			}
			catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
			{
			}
		})
		{
			IsBackground = true,
			Name = "RealmAuthorityCommandWriter"
		};

		writer.Start();
		return writer;
	}

	private static Thread StartOutputReader(Process process, ServerConsoleCapture console, TaskCompletionSource<bool> ready, ConcurrentDictionary<int, TaskCompletionSource<ServerControlReply>> replies)
	{
		var reader = new Thread(() =>
		{
			try
			{
				string? line;
				while ((line = process.StandardOutput.ReadLine()) != null)
				{
					console.WriteLine(line);

					if (!ready.Task.IsCompleted && line.StartsWith(ServerControlProtocol.ResidentReadyLine, StringComparison.Ordinal))
					{
						ready.TrySetResult(true);
						continue;
					}

					if (!ServerControlReply.TryParseLine(line, out ServerControlReply? reply) || reply == null) continue;
					if (!replies.TryRemove(reply.RequestId, out TaskCompletionSource<ServerControlReply>? pending)) continue;

					pending.TrySetResult(reply);
				}
			}
			catch (Exception exception) when (exception is IOException or ObjectDisposedException)
			{
			}
			finally
			{
				ready.TrySetResult(false);

				ServerControlReply stopped = Refused(0, "the local authority ended before it answered");
				foreach (KeyValuePair<int, TaskCompletionSource<ServerControlReply>> pending in replies)
				{
					pending.Value.TrySetResult(stopped with { RequestId = pending.Key });
				}

				replies.Clear();
			}
		})
		{
			IsBackground = true,
			Name = "RealmAuthorityOutput"
		};

		reader.Start();
		return reader;
	}

	private static Thread StartErrorReader(Process process, ServerConsoleCapture console)
	{
		var reader = new Thread(() =>
		{
			try
			{
				string? line;
				while ((line = process.StandardError.ReadLine()) != null)
				{
					console.WriteLine(line);
				}
			}
			catch (Exception exception) when (exception is IOException or ObjectDisposedException)
			{
			}
		})
		{
			IsBackground = true,
			Name = "RealmAuthorityError"
		};

		reader.Start();
		return reader;
	}

	private static async Task<bool> WaitForReadyAsync(Process process, Task<bool> ready, int timeoutMilliseconds, CancellationToken cancellationToken)
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

		Task finished = await Task.WhenAny(ready, exited, Task.Delay(timeoutMilliseconds, cancellationToken)).ConfigureAwait(false);
		if (finished == ready) return await ready.ConfigureAwait(false);

		// A resident that is exiting says so on its output a moment before the process is gone; waiting that moment is
		// what turns a start failure into the reason rather than a missing answer.
		Task grace = await Task.WhenAny(ready, Task.Delay(ReadinessGraceAfterExitMilliseconds, cancellationToken)).ConfigureAwait(false);
		return grace == ready && await ready.ConfigureAwait(false);
	}

	private static string DescribeUnreadyLaunch(Process process, ResidentAuthorityLaunchRequest request)
	{
		bool exited;
		int exitCode = int.MinValue;
		try
		{
			exited = process.HasExited;
			if (exited) exitCode = process.ExitCode;
		}
		catch (InvalidOperationException)
		{
			exited = true;
		}

		return exited
			? $"the authority exited with code {exitCode} before it was ready to take a match; see '{request.LogPath}' and '{ServerConsoleCapture.ResolvePath(request.LogPath)}'"
			: $"the authority was not ready to take a match within {request.ReadyTimeoutMilliseconds}ms; see '{request.LogPath}' and '{ServerConsoleCapture.ResolvePath(request.LogPath)}'";
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_pendingCommandLines.CompleteAdding();

		TaskCompletionSource<ServerControlReply>[] waiting = new TaskCompletionSource<ServerControlReply>[_pendingReplies.Count];
		int index = 0;
		foreach (KeyValuePair<int, TaskCompletionSource<ServerControlReply>> pending in _pendingReplies)
		{
			waiting[index++] = pending.Value;
		}

		foreach (TaskCompletionSource<ServerControlReply> pending in waiting) pending.TrySetResult(Refused(0, "the screen stopped its local authority before it answered"));
		_pendingReplies.Clear();

		EndProcess(_process, _request.ShutdownTimeoutMilliseconds);

		_commandWriterThread.Join(WriterJoinMilliseconds);
		_standardOutputThread.Join(WriterJoinMilliseconds);
		_standardErrorThread.Join(WriterJoinMilliseconds);

		_process.Dispose();
		_console.Dispose();
	}

	private static void EndProcess(Process process, int shutdownTimeoutMilliseconds)
	{
		try
		{
			if (process.HasExited) return;
		}
		catch (InvalidOperationException)
		{
			return;
		}

		try
		{
			process.Kill(entireProcessTree: true);
			process.WaitForExit(shutdownTimeoutMilliseconds);
		}
		catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
		{
		}
	}
}
