using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;
using Godot;
using Realm.Ecs.Replication;
using Realm.ListenServer;
using Realm.ListenServerHost.Network;
using MatchHost = Realm.ListenServer.ListenServerHost;
using Realm.Shared;
using Realm.Shared.Network;

namespace Realm.ListenServerHost;

/// <summary>
///     The screen's own authority. It comes up with nothing to run and stays up for as long as the screen does: a match is
///     handed to it over its control channel, paused, or replaced, so no session has to pay for a process and a runtime
///     coming up and nothing is killed between them.
/// </summary>
[GlobalClass]
public partial class ListenServerEntry : Node
{
	private const int ExitStartFailed = 70;

	private const int ReplyWriterJoinMilliseconds = 500;

	private readonly ConcurrentQueue<string?> _incomingControlLines = new();
	private readonly BlockingCollection<string> _pendingReplies = new(new ConcurrentQueue<string>(), 64);

	private ServerLog? _log;
	private ServerNetworkChannel? _channel;
	private ParentProcessWatchdog? _parentWatchdog;
	private MatchHost? _match;
	private ServerAuthorityState _state = ServerAuthorityState.Idle;
	private Thread? _replyWriterThread;
	private string _logPath = string.Empty;
	private string _dataFolderPath = string.Empty;
	private int _parentProcessId;
	private double _matchStartedAtSeconds;
	private bool _inputClosed;

	public override void _Ready()
	{
		if (!TryParseArguments(OS.GetCmdlineUserArgs(), out string argumentError))
		{
			Console.Error.WriteLine(argumentError);
			Console.Error.WriteLine($"usage: Realm.ListenServerHost -- {ServerControlProtocol.Usage}");
			Console.Error.Flush();
			GetTree().Quit(ExitStartFailed);
			return;
		}

		_log = new ServerLog(_logPath);
		_log.Write($"resident authority up pid={System.Environment.ProcessId} build={RealmVersion.GameBuildNumber}");

		_channel = GetNode<ServerNetworkChannel>("/root/RealmNetwork");

		_parentWatchdog = new ParentProcessWatchdog(_parentProcessId);
		if (_parentWatchdog.IsWatching) _log.Write($"watching the screen that started this authority, pid={_parentProcessId}");

		StartReplyWriter();
		StartControlReader();

		Console.Out.WriteLine(ServerControlProtocol.ResidentReadyLine);
		Console.Out.Flush();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_log == null) return;

		DrainControlCommands();

		if (_inputClosed)
		{
			_log.Write("the screen that owns this authority closed its control channel; stopping");
			Shutdown();
			return;
		}

		if (_parentWatchdog != null && _parentWatchdog.Update(Now()))
		{
			_log.Write("the screen that started this authority is gone; stopping");
			Shutdown();
			return;
		}

		MatchHost? match = _match;
		if (match == null) return;

		if (match.ShutdownRequested)
		{
			EndMatch("the match asked to stop");
			return;
		}

		match.Tick(Now() - _matchStartedAtSeconds);
	}

	public override void _ExitTree() => Shutdown();

	private static double Now() => Time.GetTicksUsec() / 1_000_000.0;

	private void StartControlReader()
	{
		var reader = new Thread(() =>
		{
			try
			{
				string? line;
				while ((line = Console.In.ReadLine()) != null) _incomingControlLines.Enqueue(line);
			}
			catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
			{
			}
			finally
			{
				_incomingControlLines.Enqueue(null);
			}
		})
		{
			IsBackground = true,
			Name = "RealmAuthorityControlReader"
		};

		reader.Start();
	}

	private void StartReplyWriter()
	{
		_replyWriterThread = new Thread(() =>
		{
			try
			{
				foreach (string line in _pendingReplies.GetConsumingEnumerable())
				{
					Console.Out.WriteLine(line);
					Console.Out.Flush();
				}
			}
			catch (Exception exception) when (exception is IOException or ObjectDisposedException)
			{
			}
		})
		{
			IsBackground = true,
			Name = "RealmAuthorityReplyWriter"
		};

		_replyWriterThread.Start();
	}

	private void DrainControlCommands()
	{
		while (_incomingControlLines.TryDequeue(out string? line))
		{
			if (line == null)
			{
				_inputClosed = true;
				return;
			}

			if (!ServerControlCommand.TryParseLine(line, out ServerControlCommand? command) || command == null)
			{
				_log?.Write($"a control line this build does not understand arrived and was dropped: {line}");
				continue;
			}

			Apply(command);
			if (_log == null) return;
		}
	}

	private void Apply(ServerControlCommand command)
	{
		switch (command.Kind)
		{
			case ServerControlKind.StartMatch:
				StartMatch(command);
				break;

			case ServerControlKind.Pause:
				EndMatch("the screen that owns it paused this authority");
				_state = ServerAuthorityState.Paused;
				_log?.Write("this authority is paused: it holds no match and takes none until it is told to start one");
				Reply(command, accepted: true, string.Empty);
				break;

			default:
				Reply(command, accepted: true, string.Empty, _match?.Port ?? 0);
				break;
		}
	}

	private void StartMatch(ServerControlCommand command)
	{
		ServerLog? log = _log;
		ServerNetworkChannel? channel = _channel;
		if (log == null || channel == null)
		{
			Reply(command, accepted: false, "the authority is not running");
			return;
		}

		if (!Directory.Exists(command.MapFolderPath))
		{
			Reply(command, accepted: false, $"the map folder '{command.MapFolderPath}' does not exist");
			return;
		}

		// One authority holds one match. Whatever it was holding is given up before the next one asks for the port it
		// wants, because the match it holds is still sitting on its socket.
		EndMatch("a new match was put on this authority");

		ServerArguments arguments = ServerArguments.From(command, _logPath, _dataFolderPath);
		var transport = new EnetServerTransport(channel, command.Port);
		var host = new MatchHost(arguments, log, transport);

		try
		{
			host.Start();
		}
		catch (Exception exception)
		{
			log.Write($"the match could not start: {exception}");
			host.Dispose();
			Reply(command, accepted: false, $"the match could not start: {exception.Message}");
			return;
		}

		_match = host;
		_matchStartedAtSeconds = Now();
		_state = ServerAuthorityState.Running;

		log.Write($"this authority now holds a match on '{arguments.MapPath}' port={host.Port} lobby={arguments.Lobby}");
		Reply(command, accepted: true, string.Empty, host.Port, host.MapHash, ReplicatedComponents.ComputeWireTableHash());
	}

	private void EndMatch(string reason)
	{
		MatchHost? match = _match;
		if (match == null) return;

		_match = null;
		_state = ServerAuthorityState.Idle;

		match.Dispose();
		_log?.Write($"the match held here ended: {reason}");
	}

	private void Reply(ServerControlCommand command, bool accepted, string failure, int port = 0, string mapHash = "", string wireTableHash = "")
	{
		var reply = new ServerControlReply
		{
			RequestId = command.RequestId,
			Accepted = accepted,
			State = _state,
			Failure = failure,
			Port = port,
			MapHash = mapHash,
			WireTableHash = wireTableHash
		};

		if (_pendingReplies.IsAddingCompleted) return;
		_pendingReplies.TryAdd(reply.ToLine());
	}

	private bool TryParseArguments(string[] arguments, out string error)
	{
		error = string.Empty;
		_logPath = string.Empty;
		_dataFolderPath = AppContext.BaseDirectory;
		_parentProcessId = 0;

		for (int index = 0; index < arguments.Length; index++)
		{
			string flag = arguments[index];
			switch (flag)
			{
				case ServerControlProtocol.LogFlag:
					if (!TryTakeValue(arguments, ref index, flag, out string? logPath, out error)) return false;
					_logPath = logPath!;
					break;

				case ServerControlProtocol.DataFlag:
					if (!TryTakeValue(arguments, ref index, flag, out string? dataPath, out error)) return false;
					_dataFolderPath = dataPath!;
					break;

				case ServerControlProtocol.ParentProcessIdFlag:
					if (!TryTakeValue(arguments, ref index, flag, out string? parentText, out error)) return false;
					if (!int.TryParse(parentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _parentProcessId) || _parentProcessId <= 0)
					{
						error = $"{ServerControlProtocol.ParentProcessIdFlag} expects a process id above zero, not '{parentText}'.";
						return false;
					}

					break;

				default:
					error = $"'{flag}' is not a resident authority argument.";
					return false;
			}
		}

		if (string.IsNullOrWhiteSpace(_logPath))
		{
			error = $"{ServerControlProtocol.LogFlag} is required.";
			return false;
		}

		return true;
	}

	private static bool TryTakeValue(string[] arguments, ref int index, string flag, out string? value, out string error)
	{
		value = null;
		error = string.Empty;

		if (index + 1 >= arguments.Length)
		{
			error = $"{flag} is missing its value.";
			return false;
		}

		index++;
		value = arguments[index];
		return true;
	}

	private void Shutdown()
	{
		if (_log == null && _match == null) return;

		EndMatch("the authority is stopping");

		_pendingReplies.CompleteAdding();
		_replyWriterThread?.Join(ReplyWriterJoinMilliseconds);

		_log?.Write("stopped");
		_log?.Dispose();
		_log = null;

		_parentWatchdog?.Dispose();
		_parentWatchdog = null;

		GetTree().Quit();
	}
}
