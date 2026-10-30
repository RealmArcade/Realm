using Realm.MapAPI;
using Realm.Shared.Maps;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

public class ServerMapScript : IDisposable
{
	private const string PublishRelativePath = "bin/Release/net10.0/wasi-wasm/publish/MapScript.wasm";

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
	public static ServerMapScript Load(LoadedMap map, ServerMatchPaths paths, int randomSeed, ServerLog log, ServerPresentationQueue presentation, ItemGrantService items, GroundLootService loot, bool wasmDebug = false, string? mapWorkspacePath = null, WasmScriptHost? preparedHost = null)
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

	public void Step(float deltaSeconds)
	{
		_host?.Update(_api, deltaSeconds);
	}

	public ServerGameAPI Api => _api;

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
		_api.Dispose();
	}
}
