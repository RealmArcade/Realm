using System;
using Godot;
using Realm.ListenServer;
using Realm.ListenServerHost.Network;
using MatchHost = Realm.ListenServer.ListenServerHost;
using Realm.Shared;
using Realm.Shared.Network;

namespace Realm.ListenServerHost;

[GlobalClass]
public partial class ListenServerEntry : Node
{
	private const int ExitStartFailed = 70;

	private MatchHost? _host;
	private ServerLog? _log;
	private ParentProcessWatchdog? _parentWatchdog;
	private double _startedAtSeconds;

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

		if (!parsed!.TryReadAdminToken(Console.In, out ServerArguments? resolved, out string tokenError))
		{
			Console.Error.WriteLine(tokenError);
			Console.Error.Flush();
			GetTree().Quit(ExitStartFailed);
			return;
		}

		parsed = resolved;

		_log = new ServerLog(parsed.LogPath);
		_log.Write($"starting pid={System.Environment.ProcessId} map={parsed.MapPath} port={parsed.Port} protocol={RealmProtocol.Version} build={RealmVersion.GameBuildNumber}");

		_parentWatchdog = new ParentProcessWatchdog(parsed.ParentProcessId);
		if (_parentWatchdog.IsWatching) _log.Write($"watching the screen that started this match, pid={parsed.ParentProcessId}");

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

	public override void _PhysicsProcess(double delta)
	{
		if (_host == null) return;

		double now = Now();
		if (_parentWatchdog != null && _parentWatchdog.Update(now))
		{
			_log?.Write("the screen that started this match is gone; stopping");
			Shutdown();
			return;
		}

		if (_host.ShutdownRequested)
		{
			Shutdown();
			return;
		}

		_host.Tick(now - _startedAtSeconds);
	}

	public override void _ExitTree() => Shutdown();

	private static double Now() => Time.GetTicksUsec() / 1_000_000.0;

	private void Shutdown()
	{
		MatchHost? host = _host;
		if (host == null) return;

		_host = null;
		host.Dispose();
		_log?.Write("stopped");
		_log?.Dispose();
		_log = null;
		_parentWatchdog?.Dispose();
		_parentWatchdog = null;

		GetTree().Quit();
	}
}
