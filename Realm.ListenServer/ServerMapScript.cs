using Realm.MapAPI;
using Realm.Shared.Maps;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

public class ServerMapScript : IDisposable
{
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

	public string ScriptPath { get; }

	public bool IsRunning => _host != null;

	/// <summary>
	///     Initializes the map script for a match that is starting.
	/// </summary>
	/// <param name="map">The world the match plays on.</param>
	/// <param name="paths">Where this match's map, data and logs live.</param>
	/// <param name="randomSeed">The seed this match's script draws from.</param>
	/// <param name="log">The authority's log.</param>
	/// <param name="presentation">The queue the script presents through.</param>
	/// <param name="items">The match's item grants.</param>
	/// <param name="loot">The match's ground loot.</param>
	/// <param name="wasmDebug">Whether a script this call has to instantiate is built for a debugger.</param>
	/// <param name="mapWorkspacePath">The workspace a script this call has to instantiate was published from.</param>
	/// <param name="preparedHost">A script the lobby already instantiated and warmed on this same world, which the match takes over instead of instantiating its own.</param>
	/// <param name="scriptPathOverride">The exact script this match must run, which replaces the search for one inside the map folder and fails loudly when it is missing.</param>
	public static ServerMapScript Load(LoadedMap map, ServerMatchPaths paths, int randomSeed, ServerLog log, ServerPresentationQueue presentation, ItemGrantService items, GroundLootService loot, bool wasmDebug = false, string? mapWorkspacePath = null, WasmScriptHost? preparedHost = null, string? scriptPathOverride = null)
	{
		var api = new ServerGameAPI(map, log, presentation, items, loot, paths);

		if (preparedHost != null)
		{
			GameAPI.Current = api;
			preparedHost.PublishRandomSeed(randomSeed);
			preparedHost.Initialize(api);
			log.Write($"map script '{preparedHost.ScriptPath}' was warmed by the lobby and is initialized for the match");
			return new ServerMapScript(api, preparedHost, preparedHost.ScriptPath, log);
		}

		string wasmPath;
		if (!string.IsNullOrWhiteSpace(scriptPathOverride))
		{
			wasmPath = scriptPathOverride;
			if (!File.Exists(wasmPath))
			{
				throw new FileNotFoundException($"the match was handed the map script '{wasmPath}' and there is no such file", wasmPath);
			}
		}
		else if (!TryFindScript(paths.MapPath, out wasmPath))
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

	public void Step(float deltaSeconds)
	{
		_host?.Update(_api, deltaSeconds);
	}

	public ServerGameAPI Api => _api;

	/// <summary>
	///     Finds the compiled script the map ships. A map carries exactly one, in the single folder the editor publishes
	///     every configuration into, so the match runs the bytes that were built last rather than the newest of several
	///     candidates.
	/// </summary>
	/// <param name="mapPath">The map folder.</param>
	/// <param name="wasmPath">The compiled script, or nothing when the map ships none.</param>
	/// <returns>Whether the map ships a compiled script.</returns>
	public static bool TryFindScript(string mapPath, out string wasmPath)
	{
		wasmPath = string.Empty;
		if (string.IsNullOrEmpty(mapPath) || !Directory.Exists(mapPath)) return false;

		string scriptDirectory = Path.Combine(mapPath, "bin", "script");
		if (!Directory.Exists(scriptDirectory)) return false;

		foreach (string scriptFileName in ScriptFileNames)
		{
			string candidate = Path.Combine(scriptDirectory, scriptFileName);
			if (!File.Exists(candidate)) continue;

			wasmPath = candidate;
			return true;
		}

		return false;
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		if (GameAPI.Current == _api) GameAPI.Current = null!;
		_host?.Dispose();
		_api.Dispose();
	}
}
