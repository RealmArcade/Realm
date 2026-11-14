using Realm.MapAPI;
using Realm.Shared.Maps;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

public class ServerMapScript : IDisposable
{
	private static readonly string[] ScriptFileStems = ["MapScript", "CustomMap"];

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
	/// <param name="matchOptions">The parameters the lobby's host configured, which the script reads while it initializes.</param>
	/// <param name="teamSpawnCoordinates">The board coordinate each team begins inside, as the lobby's map seated them.</param>
	public static ServerMapScript Load(LoadedMap map, ServerMatchPaths paths, int randomSeed, ServerLog log, ServerPresentationQueue presentation, ItemGrantService items, GroundLootService loot, bool wasmDebug = false, string? mapWorkspacePath = null, WasmScriptHost? preparedHost = null, string? scriptPathOverride = null, Realm.Shared.Simulation.MatchOptionSet? matchOptions = null, IReadOnlyList<string?>? teamSpawnCoordinates = null)
	{
		var api = new ServerGameAPI(map, log, presentation, items, loot, paths, teamSpawnCoordinates);

		// The host's parameters are in place before the script runs its first line, because a map reads them once while it
		// starts and never asks again.
		if (matchOptions != null) api.MatchOptions.CopyFrom(matchOptions);

		if (preparedHost != null)
		{
			GameAPI.Current = api;
			preparedHost.PublishRandomSeed(randomSeed);
			preparedHost.Initialize(api);
			log.Write($"map script '{preparedHost.ScriptPath}' was warmed by the lobby and is initialized for the match");
			return new ServerMapScript(api, preparedHost, preparedHost.ScriptPath, log);
		}

		string wasmPath;
		bool scriptCarriesDebugInformation = wasmDebug;
		if (!string.IsNullOrWhiteSpace(scriptPathOverride))
		{
			wasmPath = scriptPathOverride;
			if (!File.Exists(wasmPath))
			{
				throw new FileNotFoundException($"the match was handed the map script '{wasmPath}' and there is no such file", wasmPath);
			}
		}
		else if (!TryFindScript(paths.MapPath, wasmDebug, out wasmPath, out bool flavorMatched))
		{
			log.Write("the map brought no compiled script; the match runs on what its files describe");
			return new ServerMapScript(api, null, string.Empty, log);
		}
		else if (!flavorMatched)
		{
			scriptCarriesDebugInformation = !wasmDebug;
			log.Write(wasmDebug
				? $"the map script '{wasmPath}' carries debug information, so a debugger can step it, but the map has no optimized build of its own to play"
				: $"the map script '{wasmPath}' carries debug information because the map has no optimized build, so this match runs the build meant for a debugger");
		}

		GameAPI.Current = api;

		var host = new WasmScriptHost(wasmPath, paths.MapName, map.Simulation, map.World, randomSeed, paths.LogDirectory, mapWorkspacePath, scriptCarriesDebugInformation);
		host.Initialize(api);

		log.Write(scriptCarriesDebugInformation ? $"map script '{wasmPath}' initialized with debug information kept" : $"map script '{wasmPath}' initialized");
		return new ServerMapScript(api, host, wasmPath, log);
	}

	public void Step(float deltaSeconds)
	{
		_host?.Update(_api, deltaSeconds);
	}

	public ServerGameAPI Api => _api;

	/// <summary>
	///     Finds the compiled script the map ships for one context. A map carries one artifact per flavor in the folder the
	///     editor publishes into: the optimized script every match runs, and the script carrying debug information a playtest
	///     attaches a debugger to. A map whose build of the requested flavor is missing still runs, on the other one, and the
	///     caller is told so it can say which one was used.
	/// </summary>
	/// <param name="mapPath">The map folder.</param>
	/// <param name="wasmDebug">Whether the script carrying debug information is the one wanted.</param>
	/// <param name="wasmPath">The compiled script, or nothing when the map ships none.</param>
	/// <param name="flavorMatched">Whether the script is the flavor that was asked for.</param>
	/// <returns>Whether the map ships a compiled script.</returns>
	public static bool TryFindScript(string mapPath, bool wasmDebug, out string wasmPath, out bool flavorMatched)
	{
		if (TryFindScriptOfFlavor(mapPath, wasmDebug, out wasmPath))
		{
			flavorMatched = true;
			return true;
		}

		flavorMatched = false;
		return TryFindScriptOfFlavor(mapPath, !wasmDebug, out wasmPath);
	}

	private static bool TryFindScriptOfFlavor(string mapPath, bool isDebug, out string wasmPath)
	{
		wasmPath = string.Empty;
		if (string.IsNullOrEmpty(mapPath) || !Directory.Exists(mapPath)) return false;

		string scriptDirectory = Path.Combine(mapPath, "bin", "script");
		if (!Directory.Exists(scriptDirectory)) return false;

		for (int index = 0; index < ScriptFileStems.Length; index++)
		{
			string scriptFileName = Realm.Shared.Services.MapFileService.ScriptFileNameForFlavor(ScriptFileStems[index] + ".wasm", isDebug);
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
