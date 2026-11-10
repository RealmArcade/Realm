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

/// <summary>the watchdog and disposal.</summary>
public partial class WasmScriptHost : IWasmRuntime, IWasmEvaluationEngine, IDisposable
	{
	/// <summary>
	///     Interrupts a guest call that has run past the deadline the executing thread stated for it. It runs on the
	///     thread pool and is stopped through the same gate disposal holds, because a timer that is merely disposed does
	///     not wait for a callback already in flight, and the engine that callback interrupts is freed next.
	/// </summary>
	private void RunWatchdog(object? state)
	{
		lock (_watchdogGate)
		{
			if (_watchdogStopped) return;

			long start = Interlocked.Read(ref _executingStartTimestamp);
			int timeout = Volatile.Read(ref _executingTimeoutMs);
			if (start == 0 || timeout <= 0) return;

			double elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
			if (elapsedMs <= timeout) return;

			_engine.IncrementEpoch();
		}
	}

	public void Dispose()
	{
		if (_cachedApi != null)
		{
			_cachedApi.OnPlayerChatMessage -= HandleDebugChatCommand;
		}
		if (_simulation!.EvaluationEngine == this)
		{
			_simulation.EvaluationEngine = null;
		}
		_formulaFuncCache.Clear();
		FlushWasiLogFile();
		UnsubscribeAutoEvents();

		// Waits out a callback that is already interrupting a guest call, and turns away any that the pool has queued,
		// before the engine they call into is freed.
		lock (_watchdogGate)
		{
			_watchdogStopped = true;
		}
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
}
