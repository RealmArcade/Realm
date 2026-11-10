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

/// <summary>initialising the engine and updating it once per tick.</summary>
public partial class WasmScriptHost : IWasmRuntime, IWasmEvaluationEngine, IDisposable
	{
	public void Initialize(IGameAPI api)
	{
		_cachedApi = api;
		SubscribeAutoEvents(api);
		api.OnPlayerChatMessage += HandleDebugChatCommand;
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
					RequestWasiLogFlush();
				}
			}, "update()");
		}
		finally
		{
			ProcessAndResetTickProfiling();
		}
	}
}
