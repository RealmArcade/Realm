using Realm.MapAPI;
using Realm.Shared.Maps;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

/// <summary>
///     The map's own script, running inside the authoritative process. The script's effects land in the server's ECS
///     through <see cref="ServerGameAPI" />, so a map that places its starting units and declares its rules does it
///     once, on the authority, and every client is told what came of it.
/// </summary>
public class ServerMapScript : IDisposable
{
	private const string PublishRelativePath = "bin/Release/net10.0/wasi-wasm/publish/MapScript.wasm";
	private const string ScriptFileName = "MapScript.wasm";

	private readonly ServerGameAPI _api;
	private readonly WasmScriptHost? _host;
	private bool _disposed;

	private ServerMapScript(ServerGameAPI api, WasmScriptHost? host, string wasmPath, ServerLog log)
	{
		_api = api;
		_host = host;
		ScriptPath = wasmPath;
	}

	/// <summary>The script this match is running, or an empty string when the map has none of its own.</summary>
	public string ScriptPath { get; }

	/// <summary>Whether the map brought a script that is now running.</summary>
	public bool IsRunning => _host != null;

	/// <summary>
	///     Loads the map's script, hands it the authority's own world through the shared memory handshake and lets it
	///     set the match up. A map without a compiled script is a map that declared nothing to run, which is not an
	///     error: the match simply starts with whatever the map files described.
	/// </summary>
	public static ServerMapScript Load(LoadedMap map, string mapPath, string? logDirectory, int randomSeed, ServerLog log, ServerPresentationQueue presentation, ItemGrantService items, GroundLootService loot)
	{
		var api = new ServerGameAPI(map, log, presentation, items, loot);

		if (!TryFindScript(mapPath, out string wasmPath))
		{
			log.Write("the map brought no compiled script; the match runs on what its files describe");
			return new ServerMapScript(api, null, string.Empty, log);
		}

		GameAPI.Current = api;

		var host = new WasmScriptHost(wasmPath, map.Metadata.MapProperties?.MapName ?? "map", map.Simulation, map.World, randomSeed, logDirectory, null);
		host.Initialize(api);

		log.Write($"map script '{wasmPath}' initialized");
		return new ServerMapScript(api, host, wasmPath, log);
	}

	/// <summary>Lets the map's script see this tick, so its timers and its per-tick rules advance with the match.</summary>
	public void Step(float deltaSeconds)
	{
		_host?.Update(_api, deltaSeconds);
	}

	/// <summary>The scripting surface this match answers through, which is also what the host imports reach.</summary>
	public ServerGameAPI Api => _api;

	/// <summary>
	///     Finds the script a map folder carries. A published map keeps it where the map's own workspace compiled it,
	///     and a workspace that has been built but not packaged keeps it under its build output, so both are looked for
	///     rather than requiring a map to be packaged before the authority can run it.
	/// </summary>
	public static bool TryFindScript(string mapPath, out string wasmPath)
	{
		wasmPath = string.Empty;
		if (string.IsNullOrEmpty(mapPath) || !Directory.Exists(mapPath)) return false;

		string published = Path.Combine(mapPath, PublishRelativePath.Replace('/', Path.DirectorySeparatorChar));
		if (File.Exists(published))
		{
			wasmPath = published;
			return true;
		}

		string binDirectory = Path.Combine(mapPath, "bin");
		if (!Directory.Exists(binDirectory)) return false;

		string? publishedElsewhere = null;
		string? anyScript = null;
		foreach (string candidate in Directory.EnumerateFiles(binDirectory, ScriptFileName, SearchOption.AllDirectories))
		{
			publishedElsewhere ??= candidate;
			anyScript = candidate;
			if (candidate.Replace('\\', '/').Contains("/publish/", StringComparison.OrdinalIgnoreCase))
			{
				wasmPath = candidate;
				return true;
			}
		}

		wasmPath = publishedElsewhere ?? anyScript ?? string.Empty;
		return wasmPath.Length > 0;
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		if (GameAPI.Current == _api) GameAPI.Current = null!;
		_host?.Dispose();
	}
}
