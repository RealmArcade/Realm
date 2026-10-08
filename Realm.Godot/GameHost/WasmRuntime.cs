// ─────────────────────────────────────────────────────────────────────────────
// Realm.Godot Hand-Coded WasmRuntime class (WasmRuntime.cs)
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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Wasmtime;
using Realm.MapAPI;
using Godot;

namespace Realm.Godot;

public partial class WasmRuntime : IWasmRuntime, IDisposable
{
    public static event Action<string>? OnWasmLog;

    public static void LogToConsole(string message, bool isError = false)
    {
        OnWasmLog?.Invoke(message);
        if (isError || message.Contains("[ERROR]") || message.Contains("[WASM RUNTIME ERROR]") || message.Contains("[GUEST UPDATE ERROR]"))
        {
            global::Godot.GD.PrintErr($"[WASM] {message}");
        }
        else
        {
            global::Godot.GD.Print($"[WASM] {message}");
        }
    }

    private readonly Wasmtime.Engine _engine;
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

    public WasmRuntime(string wasmPath, string mapName)
    {
        _mapName = mapName;

        try
        {
            _engine = new Wasmtime.Engine();
            _module = Module.FromFile(_engine, wasmPath);
            _linker = new Linker(_engine);
            _store = new Store(_engine);

            string userDir = global::Godot.ProjectSettings.GlobalizePath("user://");
            _wasiLogPath = System.IO.Path.Combine(userDir, $"wasm_{Guid.NewGuid():N}.log");

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

            var wasiInitialize = _instance.GetFunction("_initialize");
            if (wasiInitialize != null)
                wasiInitialize.Invoke();

            _cabiRealloc = _instance.GetFunction("cabi_realloc");

            var initFn = _instance.GetFunction("initialize");
            if (initFn != null) _initialize = initFn.WrapAction();

            var updateFn = _instance.GetFunction("update");
            if (updateFn != null) _update = updateFn.WrapAction<float>();

            FlushWasiLogFile();
        }
        catch (Exception ex)
        {
            FlushWasiLogFile();
            LogToConsole($"[WASM RUNTIME ERROR] Failed to instantiate WASM module: {ex.Message}", true);
            throw;
        }
    }

    private void FlushWasiLogFile()
    {
        if (string.IsNullOrEmpty(_wasiLogPath) || !File.Exists(_wasiLogPath)) return;
        try
        {
            using var fs = new System.IO.FileStream(_wasiLogPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            if (fs.Length <= _wasiLogBytesFlushed) return;

            fs.Seek(_wasiLogBytesFlushed, System.IO.SeekOrigin.Begin);
            using var reader = new System.IO.StreamReader(fs, Encoding.UTF8);
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
        catch { }
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
        try
        {
            LogToConsole("[WASM RUNTIME] Executing script initialize()...");
            _initialize?.Invoke();
            FlushWasiLogFile();
            LogToConsole("[WASM RUNTIME] Script initialize() completed.");
        }
        catch (Exception ex)
        {
            FlushWasiLogFile();
            LogToConsole($"[WASM RUNTIME ERROR] Exception in initialize(): {ex}");
            throw;
        }
    }

    public void Update(IGameAPI api, float delta)
    {
        try
        {
            _update?.Invoke(delta);
            if (++_updateTickCounter >= 30)
            {
                _updateTickCounter = 0;
                FlushWasiLogFile();
            }
        }
        catch (Exception ex)
        {
            FlushWasiLogFile();
            LogToConsole($"[WASM RUNTIME ERROR] Exception in update(): {ex}", true);
            throw;
        }
    }

    public void Dispose()
    {
        FlushWasiLogFile();
        UnsubscribeAutoEvents();
        _store.Dispose();
        _module.Dispose();
        _engine.Dispose();
        if (!string.IsNullOrEmpty(_wasiLogPath) && File.Exists(_wasiLogPath))
        {
            try { File.Delete(_wasiLogPath); } catch { }
        }
    }
}
