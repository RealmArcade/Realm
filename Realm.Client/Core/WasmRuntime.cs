// ─────────────────────────────────────────────────────────────────────────────
// Realm.Client Hand-Coded WasmRuntime class (WasmRuntime.cs)
// ─────────────────────────────────────────────────────────────────────────────
//
// What belongs in this file:
// 1. Manual WASM function registrations (e.g., in InitializeManualBindings())
//    that CANNOT be auto-generated from C# interfaces because:
//    - They require custom WASM linker configurations or WASI overrides.
//    - They wrap complex low-level host behaviors not exposed through IGameAPI.
// 2. Main class constructor, WASI configurations, lifecycle, and host-side event
//    subscriptions.
// 3. Custom marshaling helpers (e.g. ReadGuestString, WriteGuestString, etc.)
//    that perform low-level guest memory writes/reads.
//
// What should NOT be in this file (Leave to Auto-Generate in WasmRuntime.g.cs):
// 1. Standard bindings for abstract interface members of IGameAPI, IUnit,
//    or IResourceNode using primitive types, Vector3, lists, or simple strings.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Godot;
using Realm.MapAPI;
using Realm.Ecs;
using Realm.Ecs.Services;
using Realm.Client.Services;
using Wasmtime;
using Engine = Wasmtime.Engine;
using FileAccess = System.IO.FileAccess;

namespace Realm.Client.Core;

public partial class WasmRuntime : IWasmRuntime, IWasmEvaluationEngine, IDisposable
{
	public static event Action<string>? OnWasmLog;

	public static void LogToConsole(string message, bool isError = false)
	{
		OnWasmLog?.Invoke(message);
		if (isError || message.Contains("[ERROR]") || message.Contains("[WASM RUNTIME ERROR]") || message.Contains("[GUEST UPDATE ERROR]"))
		{
			GD.PrintErr($"[WASM] {message}");
		}
		else
		{
			GD.Print($"[WASM] {message}");
		}
	}

	private readonly Engine _engine;
	private readonly Module _module;
	private readonly Linker _linker;
	private readonly Store _store;
	private readonly Instance _instance;
	private readonly string _mapName;

	private readonly Function? _cabiRealloc;
	private readonly Action? _initialize;
	private readonly Action<float>? _update;
	private int _sharedMemoryOffset = 0;

	public int SharedMemoryOffset => _sharedMemoryOffset;

	private IGameAPI? _cachedApi;
	private readonly string? _wasiLogPath;
	private long _wasiLogBytesFlushed = 0;
	private int _updateTickCounter = 0;
	private readonly Dictionary<string, Func<float, float, int, float>?> _formulaFuncCache = new(StringComparer.OrdinalIgnoreCase);

	private long _executingStartTimestamp;
	private int _executingTimeoutMs;
	private readonly System.Threading.Timer _watchdogTimer;
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

	private static bool IsDebugWasmFlagActive()
	{
		return Array.Exists(System.Environment.GetCommandLineArgs(), a => a.Equals("--debug-wasm", StringComparison.OrdinalIgnoreCase));
	}

	public WasmRuntime(string wasmPath, string mapName, bool isDebug = false)
	{
		_mapName = mapName;
		_isDebugMode = isDebug || IsDebugWasmFlagActive();

		try
		{
			var config = new Config()
				.WithEpochInterruption(true)
				.WithDebugInfo(_isDebugMode)
				.WithOptimizationLevel(OptimizationLevel.SpeedAndSize);
			_engine = new Engine(config);
			if (_isDebugMode)
			{
				LogToConsole($"[WASM RUNTIME] Debug mode enabled with DWARF symbols. Attach debugger to process '{System.Diagnostics.Process.GetCurrentProcess().ProcessName}' (PID {System.Diagnostics.Process.GetCurrentProcess().Id}).");
			}
			_module = Module.FromFile(_engine, wasmPath);
			_linker = new Linker(_engine);
			_store = new Store(_engine);

			string userDir = ProjectSettings.GlobalizePath("user://");
			string wasmLogsDir = Path.Combine(userDir, "wasm_logs");
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

			_instance = _linker.Instantiate(_store, _module);
			InitializeAutoEvents();

			LogToConsole($"[WASM RUNTIME] Successfully instantiated WASM module '{mapName}'.");

			if (_isDebugMode)
			{
				ExportJitDebugElf(wasmPath);
			}

			_watchdogTimer = new System.Threading.Timer(_ =>
			{
				long start = Interlocked.Read(ref _executingStartTimestamp);
				int timeout = _executingTimeoutMs;
				if (start != 0 && timeout > 0)
				{
					double elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
					if (elapsedMs > timeout)
					{
						_engine.IncrementEpoch();
					}
				}
			}, null, 100, 100);

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
						Realm.Client.Services.GameContext.World?.BindSharedMemory(sharedMemPtr, _sharedMemoryOffset);

						var setTableDirFn = _instance.GetFunction("set_realm_shared_memory_table_directory");
						if (setTableDirFn != null)
						{
							ExecuteGuardedAction(() => setTableDirFn.Invoke(_sharedMemoryOffset + Realm.MapAPI.EcsSharedMemory.COMPONENT_ARRAY_BASE_OFFSETS_OFFSET), "set_realm_shared_memory_table_directory");
						}

						var setStridesFn = _instance.GetFunction("set_realm_shared_memory_strides");
						if (setStridesFn != null)
						{
							ExecuteGuardedAction(() => setStridesFn.Invoke(_sharedMemoryOffset + Realm.MapAPI.EcsSharedMemory.COMPONENT_STRIDES_OFFSET), "set_realm_shared_memory_strides");
						}
					}
				}
			}

			var initFn = _instance.GetFunction("initialize");
			if (initFn != null) _initialize = initFn.WrapAction();

			var updateFn = _instance.GetFunction("update");
			if (updateFn != null) _update = updateFn.WrapAction<float>();

			FlushWasiLogFile();
			ECSContext.EvaluationEngine = this;
		}
		catch (Exception ex)
		{
			FlushWasiLogFile();
			LogToConsole($"[WASM RUNTIME ERROR] Failed to instantiate WASM module: {ex.Message}", true);
			throw;
		}
	}

	public void ExecuteGuardedAction(Action action, string contextName = "script execution", int timeoutMs = 2000)
	{
		if (IsFaulted) return;

		try
		{
			_store.SetEpochDeadline(1);
			if (!_isDebugMode)
			{
				_executingTimeoutMs = timeoutMs;
				Interlocked.Exchange(ref _executingStartTimestamp, Stopwatch.GetTimestamp());
			}
			action();
		}
		catch (TrapException trapEx)
		{
			IsFaulted = true;
			FlushWasiLogFile();
			LogToConsole($"[WASM RUNTIME TRAP] Script fault or timeout in {contextName}: {trapEx.Message} (Type: {trapEx.Type})", true);
		}
		catch (Exception ex)
		{
			IsFaulted = true;
			FlushWasiLogFile();
			LogToConsole($"[WASM RUNTIME ERROR] Unhandled exception in {contextName}: {ex.Message}", true);
		}
		finally
		{
			if (!_isDebugMode)
			{
				Interlocked.Exchange(ref _executingStartTimestamp, 0);
			}
		}
	}

	public T ExecuteGuardedFunc<T>(Func<T> func, T defaultValue, string contextName = "script evaluation", int timeoutMs = 2000)
	{
		if (IsFaulted) return defaultValue;

		try
		{
			_store.SetEpochDeadline(1);
			if (!_isDebugMode)
			{
				_executingTimeoutMs = timeoutMs;
				Interlocked.Exchange(ref _executingStartTimestamp, Stopwatch.GetTimestamp());
			}
			return func();
		}
		catch (TrapException trapEx)
		{
			IsFaulted = true;
			FlushWasiLogFile();
			LogToConsole($"[WASM RUNTIME TRAP] Script fault or timeout in {contextName}: {trapEx.Message} (Type: {trapEx.Type})", true);
			return defaultValue;
		}
		catch (Exception ex)
		{
			IsFaulted = true;
			FlushWasiLogFile();
			LogToConsole($"[WASM RUNTIME ERROR] Unhandled exception in {contextName}: {ex.Message}", true);
			return defaultValue;
		}
		finally
		{
			if (!_isDebugMode)
			{
				Interlocked.Exchange(ref _executingStartTimestamp, 0);
			}
		}
	}

	private void FlushWasiLogFile()
	{
		if (string.IsNullOrEmpty(_wasiLogPath) || !File.Exists(_wasiLogPath)) return;
		try
		{
			using var fs = new FileStream(_wasiLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (fs.Length <= _wasiLogBytesFlushed) return;

			fs.Seek(_wasiLogBytesFlushed, SeekOrigin.Begin);
			using var reader = new StreamReader(fs, Encoding.UTF8);
			string newText = reader.ReadToEnd();
			_wasiLogBytesFlushed = fs.Position;

			if (!string.IsNullOrWhiteSpace(newText))
			{
				foreach (var rawLine in newText.Split('\n'))
				{
					string trimmed = rawLine.TrimEnd('\r');
					if (!string.IsNullOrWhiteSpace(trimmed))
					{
						LogToConsole($"[WASM STDOUT] {trimmed}");
					}
				}
			}
		}
		catch
		{
		}
	}

	private partial void InitializeAutoBindings();
	private partial void InitializeAutoEvents();
	private partial void SubscribeAutoEvents(IGameAPI api);
	private partial void UnsubscribeAutoEvents();
	private partial void InitializeManualBindings();

	private partial void InitializeManualBindings()
	{
		// Add manual host registrations here if needed.
	}

	public readonly struct ApiProfileScope : IDisposable
	{
		private readonly WasmRuntime? _runtime;
		private readonly int _methodIndex;
		private readonly long _startTimestamp;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ApiProfileScope(WasmRuntime runtime, int methodIndex)
		{
			_runtime = runtime;
			_methodIndex = methodIndex;
			_startTimestamp = Stopwatch.GetTimestamp();
			runtime._apiCallCountsPerTick[methodIndex]++;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Dispose()
		{
			if (_runtime != null)
			{
				_runtime._apiCallTicksPerTick[_methodIndex] += Stopwatch.GetTimestamp() - _startTimestamp;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private ApiProfileScope BeginProfileApiCall(int methodIndex)
	{
		if (!_isDebugMode)
		{
			return default;
		}

		return new ApiProfileScope(this, methodIndex);
	}

	private int AllocateInGuest(int size)
	{
		if (_cabiRealloc == null) throw new InvalidOperationException("cabi_realloc is not exported by guest.");
		return (int)_cabiRealloc.Invoke(0, 0, 4, size)!;
	}

	private string ReadGuestString(Caller caller, int address, int length)
	{
		var memory = caller.GetMemory("memory");
		if (memory == null || length == 0) return "";
		return memory.ReadString(address, length, Encoding.UTF8);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void WriteInt32(Memory memory, int address, int value)
	{
		Span<byte> span = memory.GetSpan(address, 4);
		BinaryPrimitives.WriteInt32LittleEndian(span, value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int ReadInt32(Memory memory, int address)
	{
		return BinaryPrimitives.ReadInt32LittleEndian(memory.GetSpan(address, 4));
	}

	private void WriteGuestString(Caller caller, int retAreaAddress, string value)
	{
		var memory = caller.GetMemory("memory");
		if (memory == null) return;

		byte[] bytes = Encoding.UTF8.GetBytes(value);
		int ptr = AllocateInGuest(bytes.Length);
		bytes.CopyTo(memory.GetSpan(ptr, bytes.Length));

		WriteInt32(memory, retAreaAddress, ptr);
		WriteInt32(memory, retAreaAddress + 4, bytes.Length);
	}

	private void WriteGuestIntList(Caller caller, int retAreaAddress, List<int> list)
	{
		var memory = caller.GetMemory("memory");
		if (memory == null) return;

		int size = list.Count * 4;
		int ptr = AllocateInGuest(size);
		Span<byte> destSpan = memory.GetSpan(ptr, size);
		for (int i = 0; i < list.Count; i++)
		{
			BinaryPrimitives.WriteInt32LittleEndian(destSpan.Slice(i * 4, 4), list[i]);
		}

		WriteInt32(memory, retAreaAddress, ptr);
		WriteInt32(memory, retAreaAddress + 4, list.Count);
	}

	private void WriteGuestIntSpan(Caller caller, int retAreaAddress, ReadOnlySpan<int> span)
	{
		var memory = caller.GetMemory("memory");
		if (memory == null) return;

		int size = span.Length * 4;
		int ptr = size > 0 ? AllocateInGuest(size) : 0;
		if (size > 0)
		{
			Span<byte> destSpan = memory.GetSpan(ptr, size);
			System.Runtime.InteropServices.MemoryMarshal.AsBytes(span).CopyTo(destSpan);
		}

		WriteInt32(memory, retAreaAddress, ptr);
		WriteInt32(memory, retAreaAddress + 4, span.Length);
	}

	private string[] ReadGuestStringList(Caller caller, int address, int count)
	{
		var memory = caller.GetMemory("memory");
		if (memory == null || count <= 0) return Array.Empty<string>();

		var result = new string[count];
		for (int i = 0; i < count; i++)
		{
			int strPtr = ReadInt32(memory, address + i * 8);
			int strLen = ReadInt32(memory, address + i * 8 + 4);
			result[i] = memory.ReadString(strPtr, strLen, Encoding.UTF8);
		}

		return result;
	}

	public void Initialize(IGameAPI api)
	{
		_cachedApi = api;
		SubscribeAutoEvents(api);
		api.OnPlayerChatMessage += HandlePlayerChatMessageForProfiling;
		if (IsFaulted || _initialize == null) return;

		LogToConsole("[WASM RUNTIME] Executing script initialize()...");
		ExecuteGuardedAction(() =>
		{
			_initialize.Invoke();
			FlushWasiLogFile();
			LogToConsole("[WASM RUNTIME] Script initialize() completed.");
		}, "initialize()", timeoutMs: 30000);
	}

	public void Update(IGameAPI api, float delta)
	{
		if (IsFaulted || _update == null) return;

		_currentSimulationTick++;

		try
		{
			ExecuteGuardedAction(() =>
			{
				_update.Invoke(delta);
				if (++_updateTickCounter >= 30)
				{
					_updateTickCounter = 0;
					FlushWasiLogFile();
				}
			}, "update()");
		}
		finally
		{
			ProcessAndResetTickProfiling();
		}
	}

	private void ProcessAndResetTickProfiling()
	{
		if (!_isDebugMode) return;

		for (int index = 0; index < TotalApiMethodCount; index++)
		{
			int calls = _apiCallCountsPerTick[index];
			long ticks = _apiCallTicksPerTick[index];
			if (calls > 0)
			{
				_cumulativeApiCallCounts[index] += calls;
				_cumulativeApiCallTicks[index] += ticks;
				if (calls > MAX_WARNING_CALLS_PER_TICK)
				{
					string methodName = index < ApiMethodNames.Length ? ApiMethodNames[index] : $"Method #{index}";
					LogToConsole($"[WASM Perf Warning] Tick #{_currentSimulationTick} made {calls:N0} calls to '{methodName}'. Tip: Avoid mutating state or querying cold string metadata repeatedly inside tight nested loops!", true);
				}
			}
		}

		Array.Copy(_apiCallCountsPerTick, _lastTickApiCallCounts, TotalApiMethodCount);
		Array.Clear(_apiCallCountsPerTick, 0, _apiCallCountsPerTick.Length);
		Array.Copy(_apiCallTicksPerTick, _lastTickApiCallTicks, TotalApiMethodCount);
		Array.Clear(_apiCallTicksPerTick, 0, _apiCallTicksPerTick.Length);
	}

	public string GetApiCallProfilingBreakdown(int topCount = 50)
	{
		if (!_isDebugMode)
		{
			return "WASM API profiling is only active when debug mode is enabled.";
		}

		double frequency = (double)Stopwatch.Frequency;
		var methodStats = new List<(string Name, long TotalCalls, double TotalMs, double AvgMs, int LastTickCalls, double LastTickMs)>();
		for (int index = 0; index < TotalApiMethodCount; index++)
		{
			long totalCalls = _cumulativeApiCallCounts[index] + _apiCallCountsPerTick[index];
			long totalTicks = _cumulativeApiCallTicks[index] + _apiCallTicksPerTick[index];
			int lastTickCalls = _lastTickApiCallCounts[index];
			long lastTickTicks = _lastTickApiCallTicks[index];
			if (totalCalls > 0 || lastTickCalls > 0)
			{
				string methodName = index < ApiMethodNames.Length ? ApiMethodNames[index] : $"Method #{index}";
				double totalMs = (totalTicks * 1000.0) / frequency;
				double avgMs = totalCalls > 0 ? totalMs / totalCalls : 0.0;
				double lastTickMs = (lastTickTicks * 1000.0) / frequency;
				methodStats.Add((methodName, totalCalls, totalMs, avgMs, lastTickCalls, lastTickMs));
			}
		}

		methodStats.Sort((first, second) =>
		{
			int cmp = second.TotalMs.CompareTo(first.TotalMs);
			return cmp != 0 ? cmp : second.TotalCalls.CompareTo(first.TotalCalls);
		});

		var builder = new StringBuilder();
		builder.AppendLine($"=== WASM API Call Profiling Breakdown (Tick #{_currentSimulationTick}) ===");
		if (methodStats.Count == 0)
		{
			builder.AppendLine("No WASM host API calls recorded.");
		}
		else
		{
			int displayCount = Math.Min(topCount, methodStats.Count);
			for (int index = 0; index < displayCount; index++)
			{
				var stat = methodStats[index];
				builder.AppendLine($"#{index + 1}: {stat.Name} - Calls: {stat.TotalCalls:N0} | Total: {stat.TotalMs:F2}ms | Avg: {stat.AvgMs:F4}ms (Last Tick: {stat.LastTickCalls:N0} / {stat.LastTickMs:F2}ms)");
			}
		}

		return builder.ToString();
	}

	public void ResetApiProfiling()
	{
		Array.Clear(_apiCallCountsPerTick, 0, _apiCallCountsPerTick.Length);
		Array.Clear(_lastTickApiCallCounts, 0, _lastTickApiCallCounts.Length);
		Array.Clear(_cumulativeApiCallCounts, 0, _cumulativeApiCallCounts.Length);
		Array.Clear(_apiCallTicksPerTick, 0, _apiCallTicksPerTick.Length);
		Array.Clear(_lastTickApiCallTicks, 0, _lastTickApiCallTicks.Length);
		Array.Clear(_cumulativeApiCallTicks, 0, _cumulativeApiCallTicks.Length);
	}

	public void LogApiCallProfilingBreakdown(int topCount = 10)
	{
		LogToConsole(GetApiCallProfilingBreakdown(topCount));
	}

	private void HandlePlayerChatMessageForProfiling(string message, UnitId senderUnitId)
	{
		if (string.Equals(message, "/wasmperf", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(message, "/wasmprofile", StringComparison.OrdinalIgnoreCase))
		{
			LogApiCallProfilingBreakdown();
		}
	}

	public float Evaluate(in FormulaContext context, float baseValue, float modifierValue, int level)
	{
		return ExecuteCustomFormula(context.WASMFunctionName, baseValue, modifierValue, level);
	}

	public IntPtr GetSharedMemoryPointer()
	{
		var memory = _instance.GetMemory("memory");
		if (memory == null || _sharedMemoryOffset <= 0) return IntPtr.Zero;
		Span<byte> byteSpan = memory.GetSpan(_sharedMemoryOffset, 1);
		if (byteSpan.IsEmpty) return IntPtr.Zero;
		unsafe
		{
			return (IntPtr)Unsafe.AsPointer(ref byteSpan[0]);
		}
	}

	public unsafe void ImportBinarySnapshot(ReadOnlySpan<byte> snapshotPayload)
	{
		var memory = _instance.GetMemory("memory");
		if (memory == null || _sharedMemoryOffset <= 0 || snapshotPayload.IsEmpty) return;

		int totalSharedMemorySize = EcsSharedMemory.TOTAL_SIZE_BYTES;

		if (snapshotPayload.Length >= 32 && BinaryPrimitives.ReadUInt32LittleEndian(snapshotPayload.Slice(0, 4)) == 0x53434552)
		{
			int freeListCount = BinaryPrimitives.ReadInt32LittleEndian(snapshotPayload.Slice(16, 4));
			int offset = 32 + (freeListCount * sizeof(int));
			int payloadSharedBytes = Math.Min(snapshotPayload.Length - offset, totalSharedMemorySize);

			if (payloadSharedBytes > 0)
			{
				ReadOnlySpan<byte> sharedSrc = snapshotPayload.Slice(offset, payloadSharedBytes);
				Span<byte> memorySpan = memory.GetSpan(_sharedMemoryOffset, payloadSharedBytes);
				sharedSrc.CopyTo(memorySpan);

				if (payloadSharedBytes < totalSharedMemorySize)
				{
					int remaining = totalSharedMemorySize - payloadSharedBytes;
					Span<byte> remainingSpan = memory.GetSpan(_sharedMemoryOffset + payloadSharedBytes, remaining);
					remainingSpan.Clear();
				}
				return;
			}
		}

		int bytesToCopy = Math.Min(snapshotPayload.Length, totalSharedMemorySize);
		Span<byte> destSpan = memory.GetSpan(_sharedMemoryOffset, bytesToCopy);
		snapshotPayload.Slice(0, bytesToCopy).CopyTo(destSpan);

		if (bytesToCopy < totalSharedMemorySize)
		{
			Span<byte> tailSpan = memory.GetSpan(_sharedMemoryOffset + bytesToCopy, totalSharedMemorySize - bytesToCopy);
			tailSpan.Clear();
		}
	}

	public float ExecuteCustomFormula(string functionName, float baseValue, float modifierValue, int level)
	{
		if (IsFaulted || string.IsNullOrWhiteSpace(functionName))
		{
			return 0f;
		}

		if (!_formulaFuncCache.TryGetValue(functionName, out var funcDelegate))
		{
			var func = _instance.GetFunction(functionName);
			if (func == null)
			{
				_formulaFuncCache[functionName] = null;
				return 0f;
			}

			try
			{
				funcDelegate = func.WrapFunc<float, float, int, float>();
			}
			catch
			{
				funcDelegate = (b, m, l) =>
				{
					var result = func.Invoke(b, m, l);
					if (result is float f) return f;
					if (result is int i) return (float)i;
					if (result is double d) return (float)d;
					return Convert.ToSingle(result);
				};
			}

			_formulaFuncCache[functionName] = funcDelegate;
		}

		if (funcDelegate == null)
		{
			return 0f;
		}

		return ExecuteGuardedFunc(() => funcDelegate(baseValue, modifierValue, level), 0f, $"custom formula '{functionName}'");
	}

	public void Dispose()
	{
		if (_cachedApi != null)
		{
			_cachedApi.OnPlayerChatMessage -= HandlePlayerChatMessageForProfiling;
		}
		if (ECSContext.EvaluationEngine == this)
		{
			ECSContext.EvaluationEngine = null;
		}
		_formulaFuncCache.Clear();
		FlushWasiLogFile();
		UnsubscribeAutoEvents();
		_watchdogTimer?.Dispose();
		_store.Dispose();
		_module.Dispose();
		_engine.Dispose();
		if (!string.IsNullOrEmpty(_wasiLogPath) && File.Exists(_wasiLogPath))
		{
			try
			{
				File.Delete(_wasiLogPath);
			}
			catch
			{
			}
		}
	}

	public static void ExportJitDebugElf(string wasmPath)
	{
		try
		{
			IntPtr wasmtimeModuleAddress = IntPtr.Zero;
			foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
			{
				if (module.ModuleName.Equals("wasmtime.dll", StringComparison.OrdinalIgnoreCase))
				{
					wasmtimeModuleAddress = module.BaseAddress;
					break;
				}
			}

			if (wasmtimeModuleAddress == IntPtr.Zero)
			{
				return;
			}

			if (!NativeLibrary.TryGetExport(wasmtimeModuleAddress, "__jit_debug_descriptor", out IntPtr descriptorPointer))
			{
				return;
			}

			IntPtr currentEntryPointer = Marshal.ReadIntPtr(descriptorPointer, 16);
			if (currentEntryPointer == IntPtr.Zero)
			{
				return;
			}

			string? workspaceDirectory = ResolveWorkspaceDirectory(wasmPath);
			string? directoryName = Path.GetDirectoryName(wasmPath);
			string baseFileName = Path.GetFileNameWithoutExtension(wasmPath);
			int exportedCount = 0;

			while (currentEntryPointer != IntPtr.Zero)
			{
				IntPtr symbolFileAddress = Marshal.ReadIntPtr(currentEntryPointer, 16);
				ulong symbolFileSize = (ulong)Marshal.ReadInt64(currentEntryPointer, 24);

				if (symbolFileAddress != IntPtr.Zero && symbolFileSize > 0 && symbolFileSize <= 200_000_000)
				{
					byte[] elfBytes = new byte[symbolFileSize];
					Marshal.Copy(symbolFileAddress, elfBytes, 0, (int)symbolFileSize);

					if (elfBytes.Length >= 4 && elfBytes[0] == 0x7F && elfBytes[1] == 0x45 && elfBytes[2] == 0x4C && elfBytes[3] == 0x46)
					{
						string suffix = exportedCount == 0 ? string.Empty : $"_{exportedCount}";
						string publishElfPath = Path.Combine(directoryName ?? string.Empty, $"{baseFileName}{suffix}.jit.elf");
						File.WriteAllBytes(publishElfPath, elfBytes);

						if (!string.IsNullOrEmpty(workspaceDirectory) && Directory.Exists(workspaceDirectory))
						{
							string workspaceElfPath = Path.Combine(workspaceDirectory, $"MapScript{suffix}.jit.elf");
							File.WriteAllBytes(workspaceElfPath, elfBytes);
							LogToConsole($"[WASM DEBUG] Exported JIT debug symbols to '{workspaceElfPath}'.");
						}
						else
						{
							LogToConsole($"[WASM DEBUG] Exported JIT debug symbols to '{publishElfPath}'.");
						}

						exportedCount++;
					}
				}

				currentEntryPointer = Marshal.ReadIntPtr(currentEntryPointer, 0);
			}
		}
		catch (Exception exception)
		{
			LogToConsole($"[WASM DEBUG] Failed to export JIT debug symbols: {exception.Message}", true);
		}
	}

	private static string? ResolveWorkspaceDirectory(string wasmPath)
	{
		if (!string.IsNullOrEmpty(GameContext.EditorService?._tempWorkspacePath) && Directory.Exists(GameContext.EditorService._tempWorkspacePath))
		{
			return GameContext.EditorService._tempWorkspacePath;
		}

		string? currentDirectory = Path.GetDirectoryName(Path.GetFullPath(wasmPath));
		while (!string.IsNullOrEmpty(currentDirectory))
		{
			if (File.Exists(Path.Combine(currentDirectory, "MapScript.csproj")) || Directory.Exists(Path.Combine(currentDirectory, ".vscode")))
			{
				return currentDirectory;
			}
			DirectoryInfo? parent = Directory.GetParent(currentDirectory);
			currentDirectory = parent?.FullName;
		}

		return null;
	}
}