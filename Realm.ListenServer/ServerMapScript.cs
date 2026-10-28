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

	/// <summary>
	///     What a map's compiled script is called. A workspace names its project after itself, so a map kept in a workspace
	///     built from the current template compiles to the first name, and a map whose workspace still carries the project
	///     name the template shipped under compiles to the second. Both are the map's own script and nothing else.
	/// </summary>
	private static readonly string[] ScriptFileNames = ["MapScript.wasm", "CustomMap.wasm"];

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
	/// <param name="map">The map whose script is being loaded.</param>
	/// <param name="paths">Where the map's files are.</param>
	/// <param name="randomSeed">The seed the script draws from.</param>
	/// <param name="log">The log the match writes what it decided.</param>
	/// <param name="presentation">What the script asks a screen to show.</param>
	/// <param name="items">What puts items on units in this match.</param>
	/// <param name="loot">What the match drops and sweeps up.</param>
	/// <param name="wasmDebug">
	///     Whether the script is kept debug-able. The sandbox then keeps the script's debug information and its JIT ELF is
	///     exported into the workspace, which is what a debugger attaches to.
	/// </param>
	/// <param name="mapWorkspacePath">The workspace the script was compiled from, or nothing to work it out from the map.</param>
	public static ServerMapScript Load(LoadedMap map, ServerMatchPaths paths, int randomSeed, ServerLog log, ServerPresentationQueue presentation, ItemGrantService items, GroundLootService loot, bool wasmDebug = false, string? mapWorkspacePath = null)
	{
		var api = new ServerGameAPI(map, log, presentation, items, loot, paths);

		if (!TryFindScript(paths.MapPath, out string wasmPath))
		{
			log.Write("the map brought no compiled script; the match runs on what its files describe");
			return new ServerMapScript(api, null, string.Empty, log);
		}

		GameAPI.Current = api;

		var host = new WasmScriptHost(wasmPath, paths.MapName, map.Simulation, map.World, randomSeed, paths.LogDirectory, mapWorkspacePath, wasmDebug);
		host.Initialize(api);

		log.Write(wasmDebug ? $"map script '{wasmPath}' initialized with debug information kept" : $"map script '{wasmPath}' initialized");
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
	///     Finds the script a map folder carries. A published map keeps it where the map's own workspace compiled it, and a
	///     workspace that has been built but not packaged keeps it under its build output, so both are looked for rather
	///     than requiring a map to be packaged before the authority can run it.
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
		foreach (string scriptFileName in ScriptFileNames)
		{
			foreach (string candidate in Directory.EnumerateFiles(binDirectory, scriptFileName, SearchOption.AllDirectories))
			{
				publishedElsewhere ??= candidate;
				anyScript = candidate;
				if (candidate.Replace('\\', '/').Contains("/publish/", StringComparison.OrdinalIgnoreCase))
				{
					wasmPath = candidate;
					return true;
				}
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
