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

/// <summary>the api call profile, its scope and its breakdown.</summary>
public partial class WasmScriptHost : IWasmRuntime, IWasmEvaluationEngine, IDisposable
	{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private ApiProfileScope BeginProfileApiCall(int methodIndex)
	{
		if (!_isDebugMode)
		{
			return default;
		}

		return new ApiProfileScope(this, methodIndex);
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
}
