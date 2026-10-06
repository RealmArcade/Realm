namespace GameClientWorld;

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Realm.MapAPI;

public class WasmEntryPoint
{
    private static IMapScript? _mapScript;
    private static IGameAPI? _gameApi;

    private static unsafe string ReadString(nint ptr, int len)
    {
        if (ptr == IntPtr.Zero || len <= 0) return string.Empty;
        return System.Text.Encoding.UTF8.GetString((byte*)ptr, len);
    }

    [UnmanagedCallersOnly(EntryPoint = "initialize")]
    public static void Initialize()
    {
        try
        {
            _gameApi = new GameAPI_WasmModule();
            _gameApi.BroadcastMessage("Guest Initialize started");

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if ((typeof(IWasmModule).IsAssignableFrom(type) || typeof(IMapScript).IsAssignableFrom(type)) && !type.IsInterface && !type.IsAbstract)
                        {
                            _mapScript = (IMapScript)Activator.CreateInstance(type)!;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _gameApi.BroadcastMessage($"Error loading types from {assembly.GetName().Name}: {ex.Message}");
                }
            }

            if (_mapScript != null)
            {
                _mapScript.Initialize(_gameApi);
                _gameApi.BroadcastMessage("Guest WasmModule initialized successfully");
            }
            else
            {
                _gameApi.BroadcastMessage("Error: No WasmModule class implementing IWasmModule was found in any assembly!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in initialize: {ex}");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "update")]
    public static void Update(float delta)
    {
        try
        {
            if (_mapScript != null && _gameApi != null)
                _mapScript.Update(_gameApi, delta);
        }
        catch (Exception ex)
        {
            try
            {
                _gameApi?.BroadcastMessage($"[GUEST UPDATE ERROR] {ex.GetType().Name}: {ex.Message}");
            }
            catch { }
            Console.WriteLine($"Error in update: {ex}");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "on-unit-created")]
    public static void OnUnitCreated(int unitId)
    {
        if (_gameApi is GameAPI_WasmModule api) api.TriggerOnUnitCreated(unitId);
    }

    [UnmanagedCallersOnly(EntryPoint = "on-unit-died")]
    public static void OnUnitDied(int unitId, int killerId)
    {
        if (_gameApi is GameAPI_WasmModule api) api.TriggerOnUnitDied(unitId, killerId);
    }

    [UnmanagedCallersOnly(EntryPoint = "on-unit-damaged")]
    public static void OnUnitDamaged(int victimId, int attackerId, float damage)
    {
        if (_gameApi is GameAPI_WasmModule api) api.TriggerOnUnitDamaged(victimId, attackerId, damage);
    }

    [UnmanagedCallersOnly(EntryPoint = "on-spell-cast")]
    public static void OnSpellCast(int casterId, nint spellIdPtr, int spellIdLen, float tx, float ty, float tz)
    {
        string spellId = ReadString(spellIdPtr, spellIdLen);
        if (_gameApi is GameAPI_WasmModule api) api.TriggerOnSpellCast(casterId, spellId, new Vector3(tx, ty, tz));
    }

    [UnmanagedCallersOnly(EntryPoint = "on-player-chat-message")]
    public static void OnPlayerChatMessage(nint msgPtr, int msgLen, int senderId)
    {
        string msg = ReadString(msgPtr, msgLen);
        if (_gameApi is GameAPI_WasmModule api) api.TriggerOnPlayerChatMessage(msg, senderId);
    }

    [UnmanagedCallersOnly(EntryPoint = "on-player-left")]
    public static void OnPlayerLeft(int playerIndex)
    {
        if (_gameApi is GameAPI_WasmModule api) api.TriggerOnPlayerLeft(playerIndex);
    }

    [UnmanagedCallersOnly(EntryPoint = "on-timer-expired")]
    public static void OnTimerExpired(int timerHandle)
    {
        if (_gameApi is GameAPI_WasmModule api) api.TriggerOnTimerExpired(timerHandle);
    }

    [UnmanagedCallersOnly(EntryPoint = "cabi_realloc")]
    public static unsafe nint CabiRealloc(nint ptr, int oldSize, int alignment, int newSize)
    {
        if (ptr == IntPtr.Zero)
            return (nint)NativeMemory.Alloc((nuint)newSize);
        return (nint)NativeMemory.Realloc((void*)ptr, (nuint)newSize);
    }
}
