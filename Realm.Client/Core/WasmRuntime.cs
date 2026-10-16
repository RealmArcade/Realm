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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Godot;
using Realm.MapAPI;
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

	private IGameAPI? _cachedApi;
	private readonly string? _wasiLogPath;
	private long _wasiLogBytesFlushed = 0;
	private int _updateTickCounter = 0;
	private readonly Dictionary<string, Func<float, float, int, float>?> _formulaFuncCache = new(StringComparer.OrdinalIgnoreCase);

	private readonly System.Threading.Timer _watchdogTimer;
	private readonly bool _isDebugMode;

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
				.WithDebugInfo(_isDebugMode);
			if (_isDebugMode)
			{
				config.WithOptimizationLevel(OptimizationLevel.None);
			}
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
				_engine.IncrementEpoch();
			}, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

			var wasiInitialize = _instance.GetFunction("_initialize");
			if (wasiInitialize != null)
			{
				ExecuteGuardedAction(() => wasiInitialize.Invoke(), "wasi_initialize");
			}

			_cabiRealloc = _instance.GetFunction("cabi_realloc");

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
				_watchdogTimer.Change(timeoutMs, System.Threading.Timeout.Infinite);
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
				_watchdogTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
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
				_watchdogTimer.Change(timeoutMs, System.Threading.Timeout.Infinite);
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
				_watchdogTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
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
		if (IsFaulted || _initialize == null) return;

		LogToConsole("[WASM RUNTIME] Executing script initialize()...");
		ExecuteGuardedAction(() =>
		{
			_initialize.Invoke();
			FlushWasiLogFile();
			LogToConsole("[WASM RUNTIME] Script initialize() completed.");
		}, "initialize()");
	}

	public void Update(IGameAPI api, float delta)
	{
		if (IsFaulted || _update == null) return;

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

	public float Evaluate(in FormulaContext context, float baseValue, float modifierValue, int level)
	{
		return ExecuteCustomFormula(context.WASMFunctionName, baseValue, modifierValue, level);
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

			IntPtr firstEntryPointer = Marshal.ReadIntPtr(descriptorPointer, 16);
			if (firstEntryPointer == IntPtr.Zero)
			{
				return;
			}

			IntPtr symbolFileAddress = Marshal.ReadIntPtr(firstEntryPointer, 16);
			ulong symbolFileSize = (ulong)Marshal.ReadInt64(firstEntryPointer, 24);

			if (symbolFileAddress == IntPtr.Zero || symbolFileSize == 0 || symbolFileSize > 200_000_000)
			{
				return;
			}

			byte[] elfBytes = new byte[symbolFileSize];
			Marshal.Copy(symbolFileAddress, elfBytes, 0, (int)symbolFileSize);

			if (elfBytes.Length < 4 || elfBytes[0] != 0x7F || elfBytes[1] != 0x45 || elfBytes[2] != 0x4C || elfBytes[3] != 0x46)
			{
				return;
			}

			string publishElfPath = Path.ChangeExtension(wasmPath, ".jit.elf");
			File.WriteAllBytes(publishElfPath, elfBytes);

			string? workspaceDirectory = ResolveWorkspaceDirectory(wasmPath);
			if (!string.IsNullOrEmpty(workspaceDirectory) && Directory.Exists(workspaceDirectory))
			{
				string workspaceElfPath = Path.Combine(workspaceDirectory, "MapScript.jit.elf");
				File.WriteAllBytes(workspaceElfPath, elfBytes);
				LogToConsole($"[WASM DEBUG] Exported JIT debug symbols to '{workspaceElfPath}'.");
			}
			else
			{
				LogToConsole($"[WASM DEBUG] Exported JIT debug symbols to '{publishElfPath}'.");
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