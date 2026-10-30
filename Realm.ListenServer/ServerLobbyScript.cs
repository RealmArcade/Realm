using Realm.Shared.Maps;

namespace Realm.ListenServer;

/// <summary>
///     The map script while its lobby is filling: the module is instantiated and its lobby entry point has run, so the
///     match starts on a script that is already warm, and the simulation is held at tick zero until a roster starts it.
/// </summary>
public class ServerLobbyScript : IDisposable
{
	private readonly WasmScriptHost _host;
	private readonly ServerLobbyAPI _api;
	private readonly ServerLobbyRoster _roster;
	private readonly ServerLog _log;
	private bool _disposed;

	private ServerLobbyScript(WasmScriptHost host, ServerLobbyAPI api, ServerLobbyRoster roster, ServerLog log)
	{
		_host = host;
		_api = api;
		_roster = roster;
		_log = log;
	}

	/// <summary>
	///     Starts the lobby's script on the world the lobby has prepared. A map that brought no script, or whose script
	///     could not be started, leaves the lobby on the authority's own rules.
	/// </summary>
	/// <param name="map">The world the lobby prepared.</param>
	/// <param name="paths">Where this match's map, data and logs live.</param>
	/// <param name="log">The authority's log.</param>
	/// <param name="roster">The roster the lobby publishes.</param>
	/// <param name="wasmDebug">Whether the script is built for a debugger to attach to.</param>
	/// <param name="mapWorkspacePath">The workspace the script was published from, when it is known.</param>
	/// <returns>The lobby script, or nothing when the authority's own rules apply.</returns>
	public static ServerLobbyScript? Start(LoadedMap map, ServerMatchPaths paths, ServerLog log, ServerLobbyRoster roster, bool wasmDebug, string? mapWorkspacePath)
	{
		if (!ServerMapScript.TryFindScript(paths.MapPath, out string wasmPath))
		{
			log.Write("the lobby's map brought no compiled script; the authority's own lobby rules apply");
			roster.SeedDefaultFactions();
			return null;
		}

		try
		{
			var api = new ServerLobbyAPI(roster);
			var host = new WasmScriptHost(wasmPath, paths.MapName, map.Simulation, map.World, 0, paths.LogDirectory, mapWorkspacePath, wasmDebug);
			host.LobbyApi = api;
			roster.BindSharedMemory(host.SharedMemoryPointer);

			if (host.RunLobbyInit()) return new ServerLobbyScript(host, api, roster, log);

			roster.SeedDefaultFactions();
			DefaultLobbyRules.LobbyInitialize(api);
			log.Write("the lobby's map left the roster to the authority's own rules");
			return new ServerLobbyScript(host, api, roster, log);
		}
		catch (Exception exception)
		{
			log.Write($"the lobby's map script could not be started, so its lobby runs on the authority's own rules: {exception.Message}");
			roster.SeedDefaultFactions();
			return null;
		}
	}

	public bool HandlesLobby => _host.LobbyInitHandled;

	/// <summary>
	///     Runs one lobby tick. The roster is published before the script runs, so what it reads is the lobby as it stands
	///     this tick rather than as it stood last tick.
	/// </summary>
	/// <param name="deltaSeconds">The time since the previous lobby tick.</param>
	public void Tick(float deltaSeconds)
	{
		_roster.Publish();

		if (_host.LobbyInitHandled) _host.RunLobbyUpdate(deltaSeconds);
		else DefaultLobbyRules.LobbyUpdate(_api, deltaSeconds);
	}

	/// <summary>
	///     Hands the warm script to the match that is starting, which keeps the module and everything it prepared. The
	///     lobby's roster orders stop reaching it at that point, because a match no longer has a lobby.
	/// </summary>
	/// <returns>The instantiated script, which the caller now owns.</returns>
	public WasmScriptHost? DetachHost()
	{
		if (_disposed) return null;

		_disposed = true;
		_host.LobbyApi = null;
		_log.Write("the lobby's script is carried into the match, so the match starts on a module that is already warm");
		return _host;
	}

	public void Dispose()
	{
		if (_disposed) return;

		_disposed = true;
		_host.Dispose();
	}
}
