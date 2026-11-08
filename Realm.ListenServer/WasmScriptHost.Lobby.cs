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

/// <summary>the map lobby entry point and its start warning.</summary>
public partial class WasmScriptHost : IWasmRuntime, IWasmEvaluationEngine, IDisposable
	{
	/// <summary>
	///     Runs the map's lobby entry point at tick zero, before any match exists, and reports whether the script took the
	///     lobby over. It is called once, as soon as the lobby's world is prepared.
	/// </summary>
	/// <returns>Whether the script handles its lobby, so the authority's own lobby rules must not run.</returns>
	public bool RunLobbyInit()
	{
		LobbyInitHandled = false;
		if (IsFaulted || _lobbyInit == null) return false;

		bool handled = ExecuteGuardedFunc(() => _lobbyInit.Invoke() != 0, false, "lobby-init()", timeoutMs: 30000);
		LobbyInitHandled = handled;
		LogToConsole(handled
			? "[WASM RUNTIME] The map script took over its lobby's roster."
			: "[WASM RUNTIME] The map script left its lobby's roster to the authority's own rules.");
		return handled;
	}

	/// <summary>
	///     Runs the map's lobby tick while the simulation is held at tick zero. It does nothing once the script has
	///     declined the lobby or the match has taken the host over.
	/// </summary>
	/// <param name="deltaSeconds">The time since the previous lobby tick.</param>
	public void RunLobbyUpdate(float deltaSeconds)
	{
		if (!LobbyInitHandled || IsFaulted || _lobbyUpdate == null) return;

		ExecuteGuardedAction(() => _lobbyUpdate.Invoke(deltaSeconds), "lobby-update()");
	}

	/// <summary>
	///     Asks the map whether the lobby's host must read a warning before the match starts. The guest answers with one
	///     value that carries the length of its message in its upper half and the address of the message in its lower half,
	///     which is read straight out of the guest's memory; zero means there is nothing to warn about.
	/// </summary>
	/// <param name="message">The warning the map wants the host to read, or empty when there is none.</param>
	/// <returns>Whether the map asks for a warning.</returns>
	public bool TryGetLobbyStartWarning(out string message)
	{
		message = string.Empty;
		if (!LobbyInitHandled || IsFaulted || _lobbyStartWarning == null) return false;

		long packed = ExecuteGuardedFunc(() => _lobbyStartWarning.Invoke(), 0L, "lobby-start-warning()");
		int length = (int)(packed >> 32);
		int address = unchecked((int)(packed & 0xFFFFFFFFL));
		if (length <= 0 || address <= 0) return false;

		var memory = _instance.GetMemory("memory");
		if (memory == null) return false;
		if ((long)address + length > memory.GetLength()) return false;

		message = memory.ReadString(address, length, Encoding.UTF8);
		return message.Length > 0;
	}

	public readonly struct ApiProfileScope : IDisposable
	{
		private readonly WasmScriptHost? _runtime;
		private readonly int _methodIndex;
		private readonly long _startTimestamp;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ApiProfileScope(WasmScriptHost runtime, int methodIndex)
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
}
