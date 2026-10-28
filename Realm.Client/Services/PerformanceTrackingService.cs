using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Realm.Client.Services;

public enum ProfileFunctionId
{
	GodotPhysicsProcess = 0,
	DispatchPhysicsTick = 1,
	ProcessGameplayTick = 2,
	ProcessCoreSimulation = 3,
	SimulationTickEcs = 4,
	SimulationSpellCooldownQuery = 5,
	SimulationCooldownsQuery = 6,
	SimulationMovementStep = 7,
	SimulationCombatStep = 8,
	SimulationEconomyStep = 9,
	SimulationBuffQueries = 10,
	SimulationPatrolArrivals = 11,
	SimulationFollowMovements = 12,
	SimulationUnitQueries = 13,
	SimulationBotControllers = 14,
	SimulationDeferredCommands = 15,
	PollVfxQueue = 16,
	TickConstructionSystem = 17,
	UpdateVisualNodesFromEcs = 18,
	ProcessVisualAndEnvironment = 19,
	UpdateDayNightVisuals = 20,
	EnvironmentUpdateTransition = 21,
	UpdateMinimapPings = 22,
	ProcessMiscTicks = 23,
	TickScheduledTimers = 24,
	TickZoneTriggers = 25,

	WasmRuntimeUpdate = 27,
	UpdateBuildingPreview = 28,
	RecordGameplayTick = 29,
	GodotProcess = 30,
	ShroudServiceTick = 31,
	EnvironmentUpdateFog = 32,
	EnvironmentUpdateWeatherParticles = 33,
	Unit3DProcess = 34,
	SimulationShroudStep = 35,
	AudioPositionalUpdate = 36,
	PredictionPerformanceSample = 37,
	UnitOcclusionOutlineTick = 38
}

public struct FunctionProfileMetric
{
	public string Name;
	public string ParentName;
	public long InvocationCount;
	public long TotalElapsedTicks;
	public long MinElapsedTicks;
	public long MaxElapsedTicks;
}

public readonly ref struct ProfileScope
{
	private readonly PerformanceTrackingService? _service;
	private readonly ProfileFunctionId _functionId;
	private readonly long _startTimestamp;

	public ProfileScope(PerformanceTrackingService? service, ProfileFunctionId functionId)
	{
		_service = service;
		_functionId = functionId;
		_startTimestamp = Stopwatch.GetTimestamp();
	}

	public void Dispose()
	{
		if (_service != null)
		{
			long elapsedTicks = Stopwatch.GetTimestamp() - _startTimestamp;
			_service.RecordFunctionMetric(_functionId, elapsedTicks);
		}
	}
}

public class PerformanceTrackingService
{
	public Stopwatch TrackerTickStopwatch { get; set; } = new Stopwatch();
	public Stopwatch TrackerIntervalStopwatch { get; set; } = new Stopwatch();
	public List<float> TrackerTickDurations { get; set; } = new List<float>();
	public List<float> TrackerApiDurations { get; set; } = new List<float>();
	public float TrackerLastTickDelay { get; set; }

	public bool IsFunctionProfilingEnabled { get; set; } = false;

	private readonly FunctionProfileMetric[] _functionMetrics;

	public PerformanceTrackingService()
	{
		int count = Enum.GetValues<ProfileFunctionId>().Length;
		_functionMetrics = new FunctionProfileMetric[count];
		InitializeMetricMetadata();
	}

	private void InitializeMetricMetadata()
	{
		RegisterMetric(ProfileFunctionId.GodotPhysicsProcess, "Godot.PhysicsProcess", "");
		RegisterMetric(ProfileFunctionId.DispatchPhysicsTick, "DispatchPhysicsTick", "Godot.PhysicsProcess");
		RegisterMetric(ProfileFunctionId.ProcessGameplayTick, "ProcessGameplayTick", "DispatchPhysicsTick");
		RegisterMetric(ProfileFunctionId.ProcessCoreSimulation, "ProcessCoreSimulation", "ProcessGameplayTick");
		RegisterMetric(ProfileFunctionId.SimulationTickEcs, "SimulationService.TickEcs", "ProcessCoreSimulation");
		RegisterMetric(ProfileFunctionId.SimulationSpellCooldownQuery, "WorldQuery.SpellCooldown", "SimulationService.TickEcs");
		RegisterMetric(ProfileFunctionId.SimulationCooldownsQuery, "WorldQuery.Cooldowns", "SimulationService.TickEcs");
		RegisterMetric(ProfileFunctionId.SimulationMovementStep, "MovementService.StepMovement", "SimulationService.TickEcs");
		RegisterMetric(ProfileFunctionId.SimulationCombatStep, "CombatService.StepCombat", "SimulationService.TickEcs");
		RegisterMetric(ProfileFunctionId.SimulationEconomyStep, "EconomyService.StepEconomy", "SimulationService.TickEcs");
		RegisterMetric(ProfileFunctionId.SimulationShroudStep, "ShroudSimulationService.Tick", "SimulationService.TickEcs");
		RegisterMetric(ProfileFunctionId.SimulationBuffQueries, "WorldQueries.BuffsAndStats", "SimulationService.TickEcs");
		RegisterMetric(ProfileFunctionId.SimulationPatrolArrivals, "ProcessPatrolArrivals", "SimulationService.TickEcs");
		RegisterMetric(ProfileFunctionId.SimulationFollowMovements, "ProcessFollowMovements", "SimulationService.TickEcs");
		RegisterMetric(ProfileFunctionId.SimulationUnitQueries, "WorldQueries.UnitRegenAndProduction", "SimulationService.TickEcs");
		RegisterMetric(ProfileFunctionId.SimulationBotControllers, "TickBotControllers", "SimulationService.TickEcs");
		RegisterMetric(ProfileFunctionId.SimulationDeferredCommands, "ApplyDeferredTickCommands", "SimulationService.TickEcs");
		RegisterMetric(ProfileFunctionId.PollVfxQueue, "PollVFXQueue", "ProcessCoreSimulation");
		RegisterMetric(ProfileFunctionId.TickConstructionSystem, "TickConstructionSystem", "ProcessCoreSimulation");
		RegisterMetric(ProfileFunctionId.UpdateVisualNodesFromEcs, "UpdateVisualNodesFromEcs", "ProcessCoreSimulation");
		RegisterMetric(ProfileFunctionId.ProcessVisualAndEnvironment, "ProcessVisualAndEnvironment", "ProcessGameplayTick");
		RegisterMetric(ProfileFunctionId.UpdateDayNightVisuals, "UpdateDayNightVisuals", "ProcessVisualAndEnvironment");
		RegisterMetric(ProfileFunctionId.EnvironmentUpdateTransition, "EnvironmentService.UpdateTransition", "ProcessVisualAndEnvironment");
		RegisterMetric(ProfileFunctionId.UpdateMinimapPings, "UpdateMinimapPings", "ProcessVisualAndEnvironment");
		RegisterMetric(ProfileFunctionId.ProcessMiscTicks, "ProcessMiscTicks", "ProcessGameplayTick");
		RegisterMetric(ProfileFunctionId.TickScheduledTimers, "TickScheduledTimers", "ProcessMiscTicks");
		RegisterMetric(ProfileFunctionId.TickZoneTriggers, "TickZoneTriggers", "ProcessMiscTicks");

		RegisterMetric(ProfileFunctionId.WasmRuntimeUpdate, "WasmRuntime.Update", "ProcessMiscTicks");
		RegisterMetric(ProfileFunctionId.UpdateBuildingPreview, "UpdateBuildingPreview", "ProcessMiscTicks");
		RegisterMetric(ProfileFunctionId.RecordGameplayTick, "RecordGameplayTick", "ProcessMiscTicks");
		RegisterMetric(ProfileFunctionId.GodotProcess, "Godot.Process", "");
		RegisterMetric(ProfileFunctionId.ShroudServiceTick, "ShroudService.Tick", "Godot.Process");
		RegisterMetric(ProfileFunctionId.EnvironmentUpdateFog, "EnvironmentService.UpdateEnvironmentalFog", "Godot.Process");
		RegisterMetric(ProfileFunctionId.EnvironmentUpdateWeatherParticles, "EnvironmentService.UpdateWeatherParticlePosition", "Godot.Process");
		RegisterMetric(ProfileFunctionId.Unit3DProcess, "Unit3D._Process", "Godot.Process");
		RegisterMetric(ProfileFunctionId.AudioPositionalUpdate, "AudioService.UpdatePositionalVoices", "Godot.Process");
		RegisterMetric(ProfileFunctionId.PredictionPerformanceSample, "PredictionPerformanceMonitor.Sample", "Godot.Process");
		RegisterMetric(ProfileFunctionId.UnitOcclusionOutlineTick, "UnitOcclusionOutlineService.Tick", "Godot.Process");
	}

	private void RegisterMetric(ProfileFunctionId id, string name, string parentName)
	{
		int index = (int)id;
		if (index >= 0 && index < _functionMetrics.Length)
		{
			_functionMetrics[index].Name = name;
			_functionMetrics[index].ParentName = parentName;
		}
	}

	public ProfileScope SampleFunction(ProfileFunctionId functionId)
	{
		if (!IsFunctionProfilingEnabled)
		{
			return default;
		}

		return new ProfileScope(this, functionId);
	}

	public void RecordFunctionMetric(ProfileFunctionId functionId, long elapsedTicks)
	{
		int index = (int)functionId;
		if (index >= 0 && index < _functionMetrics.Length)
		{
			_functionMetrics[index].InvocationCount++;
			_functionMetrics[index].TotalElapsedTicks += elapsedTicks;
			if (elapsedTicks < _functionMetrics[index].MinElapsedTicks || _functionMetrics[index].MinElapsedTicks == 0)
			{
				_functionMetrics[index].MinElapsedTicks = elapsedTicks;
			}
			if (elapsedTicks > _functionMetrics[index].MaxElapsedTicks)
			{
				_functionMetrics[index].MaxElapsedTicks = elapsedTicks;
			}
		}
	}

	public void ResetFunctionProfiling()
	{
		for (int i = 0; i < _functionMetrics.Length; i++)
		{
			_functionMetrics[i].InvocationCount = 0;
			_functionMetrics[i].TotalElapsedTicks = 0;
			_functionMetrics[i].MinElapsedTicks = 0;
			_functionMetrics[i].MaxElapsedTicks = 0;
		}
	}

	public string GetFunctionProfilingBreakdown()
	{
		var builder = new StringBuilder();
		builder.AppendLine("=== Function Utilization Hotspots ===");
		double frequency = (double)Stopwatch.Frequency;

		AppendHierarchy(builder, "", 0, frequency);
		return builder.ToString();
	}

	private void AppendHierarchy(StringBuilder builder, string parentName, int indentLevel, double frequency)
	{
		double parentTotalMs = 0.0;
		if (!string.IsNullOrEmpty(parentName))
		{
			for (int i = 0; i < _functionMetrics.Length; i++)
			{
				if (string.Equals(_functionMetrics[i].Name, parentName, StringComparison.OrdinalIgnoreCase))
				{
					parentTotalMs = (_functionMetrics[i].TotalElapsedTicks * 1000.0) / frequency;
					break;
				}
			}
		}

		string indent = new string(' ', indentLevel * 2);
		for (int i = 0; i < _functionMetrics.Length; i++)
		{
			if (string.Equals(_functionMetrics[i].ParentName, parentName, StringComparison.OrdinalIgnoreCase))
			{
				long count = _functionMetrics[i].InvocationCount;
				double totalMs = (_functionMetrics[i].TotalElapsedTicks * 1000.0) / frequency;
				double avgMs = count > 0 ? totalMs / count : 0.0;
				double minMs = (_functionMetrics[i].MinElapsedTicks * 1000.0) / frequency;
				double maxMs = (_functionMetrics[i].MaxElapsedTicks * 1000.0) / frequency;

				string percentageText = parentTotalMs > 0.0 ? $" | {(totalMs / parentTotalMs * 100.0):F1}% of parent" : "";
				builder.AppendLine($"{indent}• {_functionMetrics[i].Name}: Calls={count:N0} | Total={totalMs:F2}ms | Avg={avgMs:F3}ms | Min={minMs:F3}ms | Max={maxMs:F3}ms{percentageText}");

				AppendHierarchy(builder, _functionMetrics[i].Name, indentLevel + 1, frequency);
			}
		}
	}
}