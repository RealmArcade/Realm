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

/// <summary>running guest callbacks behind a trap and timeout guard.</summary>
public partial class WasmScriptHost : IWasmRuntime, IWasmEvaluationEngine, IDisposable
	{
	public void ExecuteGuardedAction(Action action, string contextName = "script execution", int timeoutMs = 2000)
	{
		if (IsFaulted) return;

		try
		{
			_store.SetEpochDeadline(1);
			if (!_isDebugMode)
			{
				Volatile.Write(ref _executingTimeoutMs, timeoutMs);
				Interlocked.Exchange(ref _executingStartTimestamp, Stopwatch.GetTimestamp());
			}
			action();
		}
		catch (TrapException trapEx)
		{
			IsFaulted = true;
			RequestWasiLogFlush();
			LogToConsole($"[WASM RUNTIME TRAP] Script fault or timeout in {contextName}: {trapEx.Message} (Type: {trapEx.Type})", true);
		}
		catch (Exception ex)
		{
			IsFaulted = true;
			RequestWasiLogFlush();
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
				Volatile.Write(ref _executingTimeoutMs, timeoutMs);
				Interlocked.Exchange(ref _executingStartTimestamp, Stopwatch.GetTimestamp());
			}
			return func();
		}
		catch (TrapException trapEx)
		{
			IsFaulted = true;
			RequestWasiLogFlush();
			LogToConsole($"[WASM RUNTIME TRAP] Script fault or timeout in {contextName}: {trapEx.Message} (Type: {trapEx.Type})", true);
			return defaultValue;
		}
		catch (Exception ex)
		{
			IsFaulted = true;
			RequestWasiLogFlush();
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

	private int _logFlushInProgress;
	private int _logFlushQueued;

	/// <summary>
	///     Asks for the guest's standard output to be read into the log from a pool thread. The read is a file operation,
	///     and the tick that trapped is not allowed to wait for one, so the trap reports and carries on.
}
