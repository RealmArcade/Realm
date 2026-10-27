using System;
using Godot;
using Realm.ListenServer;
using Realm.ListenServerHost.Network;
using MatchHost = Realm.ListenServer.ListenServerHost;
using Realm.Shared;
using Realm.Shared.Network;

namespace Realm.ListenServerHost;

/// <summary>
///     The process the authority runs in. It owns nothing about the match beyond the files it was started against and
///     the clock it is advanced on: the world, the peers and the wire all belong to the simulation and the transport.
///     It exists because ENet and RPC are the engine's, and a match has to be reachable on the same wire a client
///     speaks before it can be authoritative over anything.
/// </summary>
[GlobalClass]
public partial class ListenServerEntry : Node
{
	private const int ExitStartFailed = 70;

	private MatchHost? _host;
	private ServerLog? _log;
	private double _startedAtSeconds;

	/// <summary>
	///     Reads the match's arguments, builds its world and starts listening. Anything that stops a match from running
	///     is reported to its log and to its own output, so a launcher that is waiting for the readiness line is told
	///     why it never came.
	/// </summary>
	public override void _Ready()
	{
		string[] arguments = OS.GetCmdlineUserArgs();
		if (!ServerArguments.TryParse(arguments, out ServerArguments? parsed, out string argumentError))
		{
			Console.Error.WriteLine(argumentError);
			Console.Error.WriteLine($"usage: Realm.ListenServerHost -- {ServerArguments.Usage}");
			Console.Error.Flush();
			GetTree().Quit(ExitStartFailed);
			return;
		}

		_log = new ServerLog(parsed!.LogPath);
		_log.Write($"starting pid={System.Environment.ProcessId} map={parsed.MapPath} port={parsed.Port} protocol={RealmProtocol.Version} build={RealmVersion.GameBuildNumber}");

		ServerNetworkChannel channel = GetNode<ServerNetworkChannel>("/root/RealmNetwork");
		var transport = new EnetServerTransport(channel, parsed.Port);
		_host = new MatchHost(parsed, _log, transport);

		try
		{
			_host.Start();
		}
		catch (Exception exception)
		{
			_log.Write($"failed to start: {exception}");
			Console.Error.WriteLine($"failed to start: {exception.Message}");
			Console.Error.Flush();
			_host.Dispose();
			_host = null;
			GetTree().Quit(ExitStartFailed);
			return;
		}

		_startedAtSeconds = Now();
	}

	/// <summary>The match is advanced from real time rather than from the frame rate, so a stalled frame costs the tick it swallowed.</summary>
	public override void _PhysicsProcess(double delta)
	{
		if (_host == null) return;

		if (_host.ShutdownRequested)
		{
			Shutdown();
			return;
		}

		_host.Tick(Now() - _startedAtSeconds);
	}

	public override void _ExitTree() => Shutdown();

	private static double Now() => Time.GetTicksUsec() / 1_000_000.0;

	/// <summary>Ends the match once: the peers are told, the world is released, and the process leaves.</summary>
	private void Shutdown()
	{
		MatchHost? host = _host;
		if (host == null) return;

		_host = null;
		host.Dispose();
		_log?.Write("stopped");
		_log?.Dispose();
		_log = null;

		GetTree().Quit();
	}
}
