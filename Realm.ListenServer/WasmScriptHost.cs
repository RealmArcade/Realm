using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Realm.MapAPI;
using Realm.Ecs;
using Realm.Ecs.Services;
using Wasmtime;
using Engine = Wasmtime.Engine;
using FileAccess = System.IO.FileAccess;

namespace Realm.ListenServer;

/// <summary>
///     The hand-written half of the WASM runtime. Manual bindings belong here only when the linker generator cannot
///     produce them: registrations that need a custom linker configuration or WASI override, and marshaling helpers that
///     read and write guest memory. Standard bindings for the interface members of <see cref="Realm.MapAPI.IGameAPI" />
///     over primitives, vectors and strings are generated into <c>WasmRuntime.g.cs</c> and must not be written here.
/// </summary>
public partial class WasmScriptHost : IWasmRuntime, IWasmEvaluationEngine, IDisposable
	{
	private readonly SimulationContext? _simulation;
	private readonly EcsWorld? _world;
	private readonly int _randomSeed;
	private readonly string? _logDirectory;
	private readonly string? _mapWorkspacePath;

	public static event Action<string>? OnWasmLog;

	public static void LogToConsole(string message, bool isError = false)
	{
		OnWasmLog?.Invoke(message);
		if (isError || message.Contains("[ERROR]") || message.Contains("[WASM RUNTIME ERROR]") || message.Contains("[GUEST UPDATE ERROR]"))
		{
			Console.Error.WriteLine($"[WASM] {message}");
		}
		else
		{
			Console.Error.WriteLine($"[WASM] {message}");
		}
	}

	private readonly Engine _engine;
	private readonly Module _module;
	private readonly Linker _linker;
	private readonly Store _store;
	private readonly Instance _instance;
	private readonly string _mapName;
	private readonly string _scriptPath;

	private readonly Function? _cabiRealloc;
	private readonly Action? _initialize;
	private readonly Action<float>? _update;
	private readonly Func<int>? _lobbyInit;
	private readonly Action<float>? _lobbyUpdate;
	private readonly Func<long>? _lobbyStartWarning;
	private int _sharedMemoryOffset = 0;
	private IntPtr _sharedMemoryPointer = IntPtr.Zero;

	public int SharedMemoryOffset => _sharedMemoryOffset;

	/// <summary>The script this host runs, which is the artifact the map was published with.</summary>
	public string ScriptPath => _scriptPath;

	/// <summary>
	///     The base of the guest's shared memory region, which is where everything this process publishes to a map script
	///     is written.
	/// </summary>
	public IntPtr SharedMemoryPointer => _sharedMemoryPointer;

	/// <summary>
	///     The roster orders a lobby script places while this host runs without a match. Imports reach it by reference, so
	///     it is attached before the lobby runs and detached once the match takes over.
	/// </summary>
	public Realm.MapAPI.ILobbyAPI? LobbyApi { get; set; }

	/// <summary>
	///     Whether this map's script took over its lobby's roster, which it does by exporting <c>lobby-init</c> and
	///     answering that it handled it. A script that does not is run on the authority's own lobby rules.
	/// </summary>
	public bool LobbyInitHandled { get; private set; }

	private IGameAPI? _cachedApi;
	private readonly string? _wasiLogPath;
	private long _wasiLogBytesFlushed = 0;
	private int _updateTickCounter = 0;
	private readonly Dictionary<string, Func<float, float, int, float>?> _formulaFuncCache = new(StringComparer.OrdinalIgnoreCase);

	private long _executingStartTimestamp;
	private int _executingTimeoutMs;
	private readonly System.Threading.Timer _watchdogTimer;
	private readonly object _watchdogGate = new();
	private bool _watchdogStopped;
	private readonly bool _isDebugMode;

	public const int MAX_WARNING_CALLS_PER_TICK = 1000;

	private readonly int[] _apiCallCountsPerTick = new int[TotalApiMethodCount];
	private readonly int[] _lastTickApiCallCounts = new int[TotalApiMethodCount];
	private readonly long[] _cumulativeApiCallCounts = new long[TotalApiMethodCount];
	private readonly long[] _apiCallTicksPerTick = new long[TotalApiMethodCount];
	private readonly long[] _lastTickApiCallTicks = new long[TotalApiMethodCount];
	private readonly long[] _cumulativeApiCallTicks = new long[TotalApiMethodCount];
	private int _currentSimulationTick = 0;

	public bool IsFaulted { get; private set; }

	public WasmScriptHost(string wasmPath, string mapName, SimulationContext simulation, EcsWorld world, int randomSeed, string? logDirectory = null, string? mapWorkspacePath = null, bool isDebug = false)
	{
		_mapName = mapName;
		_scriptPath = wasmPath;
		_simulation = simulation;
		_world = world;
		_randomSeed = randomSeed;
		_mapWorkspacePath = mapWorkspacePath;
		_logDirectory = logDirectory;
		_isDebugMode = isDebug;

		try
		{
			var config = new Config()
				.WithEpochInterruption(true)
				.WithDebugInfo(_isDebugMode)
				.WithOptimizationLevel(OptimizationLevel.SpeedAndSize);
			_engine = new Engine(config);
			if (_isDebugMode)
			{
				LogToConsole($"[WASM RUNTIME] Debug mode enabled with DWARF symbols for '{wasmPath}'. Attach the map workspace debugger to process '{System.Diagnostics.Process.GetCurrentProcess().ProcessName}' (PID {System.Diagnostics.Process.GetCurrentProcess().Id}).");
			}
			_module = Module.FromFile(_engine, wasmPath);
			_linker = new Linker(_engine);
			_store = new Store(_engine);

			string wasmLogsDir = Path.Combine(string.IsNullOrEmpty(_logDirectory) ? Path.GetTempPath() : _logDirectory, "wasm_logs");
			if (!Directory.Exists(wasmLogsDir))
			{
				Directory.CreateDirectory(wasmLogsDir);
			}
			_wasiLogPath = Path.Combine(wasmLogsDir, $"wasm_{Guid.NewGuid():N}.log");

			var wasiConfig = new WasiConfiguration();
			wasiConfig.WithStandardOutput(_wasiLogPath);
			wasiConfig.WithStandardError(_wasiLogPath);

			_store.SetWasiConfiguration(wasiConfig);

			_linker.DefineWasi();
			_linker.DefineFunction("env", "gai_strerror", (int err) => 0);

			InitializeAutoBindings();
			InitializeManualBindings();
			ValidateModuleImports();

			_instance = _linker.Instantiate(_store, _module);
			InitializeAutoEvents();

			LogToConsole($"[WASM RUNTIME] Successfully instantiated WASM module '{mapName}'.");

			if (_isDebugMode)
			{
				ExportJitDebugElf(wasmPath);
			}

			_watchdogTimer = new System.Threading.Timer(RunWatchdog, null, 100, 100);

			var wasiInitialize = _instance.GetFunction("_initialize");
			if (wasiInitialize != null)
			{
				ExecuteGuardedAction(() => wasiInitialize.Invoke(), "wasi_initialize");
			}

			_cabiRealloc = _instance.GetFunction("cabi_realloc");

			var getSharedMemBaseFn = _instance.GetFunction("get_realm_shared_memory_base")
				?? _instance.GetFunction("get_shared_memory_base")
				?? _instance.GetFunction("g_realmSharedMemoryBase");
			if (getSharedMemBaseFn != null)
			{
				var result = getSharedMemBaseFn.Invoke();
				if (result is int offset)
				{
					_sharedMemoryOffset = offset;
					LogToConsole($"[WASM RUNTIME] Shared memory handshake succeeded at guest offset 0x{_sharedMemoryOffset:X}.");

					IntPtr sharedMemPtr = GetSharedMemoryPointer();
					if (sharedMemPtr != IntPtr.Zero)
					{
						_sharedMemoryPointer = sharedMemPtr;
						_world?.BindSharedMemory(sharedMemPtr, _sharedMemoryOffset);
						PublishSharedPlayerSlots();

						var setTableDirFn = _instance.GetFunction("set_realm_shared_memory_table_directory");
						if (setTableDirFn != null)
						{
							ExecuteGuardedAction(() => setTableDirFn.Invoke(_sharedMemoryOffset + Realm.Ecs.EcsSharedMemory.COMPONENT_ARRAY_BASE_OFFSETS_OFFSET), "set_realm_shared_memory_table_directory");
						}

						var setStridesFn = _instance.GetFunction("set_realm_shared_memory_strides");
						if (setStridesFn != null)
						{
							ExecuteGuardedAction(() => setStridesFn.Invoke(_sharedMemoryOffset + Realm.Ecs.EcsSharedMemory.COMPONENT_STRIDES_OFFSET), "set_realm_shared_memory_strides");
						}

						PublishSeededRandomSeed();
					}
				}
			}

			var initFn = _instance.GetFunction("initialize");
			if (initFn != null) _initialize = initFn.WrapAction();

			var updateFn = _instance.GetFunction("update");
			if (updateFn != null) _update = updateFn.WrapAction<float>();

			var lobbyInitFn = _instance.GetFunction("lobby-init");
			if (lobbyInitFn != null) _lobbyInit = lobbyInitFn.WrapFunc<int>();

			var lobbyUpdateFn = _instance.GetFunction("lobby-update");
			if (lobbyUpdateFn != null) _lobbyUpdate = lobbyUpdateFn.WrapAction<float>();

			var lobbyStartWarningFn = _instance.GetFunction("lobby-start-warning");
			if (lobbyStartWarningFn != null) _lobbyStartWarning = lobbyStartWarningFn.WrapFunc<long>();

			FlushWasiLogFile();
			_simulation.EvaluationEngine = this;
		}
		catch (Exception ex)
		{
			FlushWasiLogFile();
			LogToConsole($"[WASM RUNTIME ERROR] Failed to instantiate WASM module: {ex.Message}", true);
			throw;
		}
	}

	/// <summary>
	///     Rejects a script published against an older engine before the linker reports the stale symbol as an opaque
	///     unknown import. A map ships the bytes it was published with, so an import this build no longer defines means the
	///     workspace has to be recompiled, not that the runtime is misconfigured.
	/// </summary>
	private void ValidateModuleImports()
	{
		List<string>? missingImports = null;
		foreach (var import in _module.Imports)
		{
			if (IsRuntimeProvidedImportModule(import.ModuleName)) continue;
			if (_linker.GetFunction(_store, import.ModuleName, import.Name) != null) continue;
			missingImports ??= new List<string>();
			missingImports.Add($"{import.ModuleName}::{import.Name}");
		}

		if (missingImports == null) return;
		throw new InvalidOperationException($"Map script '{_scriptPath}' was built against an engine that provided {missingImports.Count} import(s) this build does not: {string.Join(", ", missingImports)}. Rebuild the map workspace with this build (Test Map or Publish) before playing it.");
	}

	private static bool IsRuntimeProvidedImportModule(string moduleName) =>
		moduleName is "env" or "wasi_snapshot_preview1" or "wasi_unstable";
}
