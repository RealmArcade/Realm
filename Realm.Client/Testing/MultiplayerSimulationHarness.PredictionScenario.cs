using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Realm.Ecs.Components.Tags;

namespace Realm.Client.Testing;

public partial class MultiplayerSimulationHarness
{
	private const int ScenarioStartTick = 45;
	private const int ScenarioSweepStartTick = 60;
	private const int ScenarioSweepSpacingTicks = 32;
	private const int ScenarioSweepCount = 20;
	private const int ScenarioQueuedMoveTick = ScenarioSweepStartTick + ScenarioSweepCount * ScenarioSweepSpacingTicks;
	private const int ScenarioAttackMoveTick = ScenarioQueuedMoveTick + 20;
	private const int ScenarioGatherTick = ScenarioAttackMoveTick + 20;
	private const int ScenarioBuildTick = ScenarioGatherTick + 20;
	private const int ScenarioCommandFloodTick = ScenarioBuildTick + 5;
	private const int ScenarioCommandFloodCount = 400;
	private const int ScenarioRejectedGatherTick = ScenarioBuildTick + 15;
	private const int ScenarioRemoteMoveTick = ScenarioRejectedGatherTick + 15;
	private const int ScenarioOwnerChangeTick = ScenarioRemoteMoveTick + 15;
	private const int ScenarioFinalMoveTick = ScenarioOwnerChangeTick + 15;
	private const int ScenarioMinimumSnapshotSequence = 90;

	private const float RemoteGroupCameraIntervalSeconds = 0.25f;
	private const float RemoteGroupRadiusMeters = 15f;
	private const float RemoteGroupCameraHeightMeters = 10f;
	private const float RemoteGroupCameraBackdropMeters = 10f;

	private float _remoteGroupCameraTimer;
	private bool _remoteGroupCameraHasTarget;

	/// <summary>
	/// Points the client's camera at the densest group of units it does not own, so the host describes that group at
	/// detailed precision and the placement check is given the case it exists for: a rendered transform judged against a
	/// position the host promised to keep within five centimetres. Without this the harness client stares at empty ground
	/// - every unit sits beyond the detailed radius, the host sends all of them in one-metre steps, and the stricter half
	/// of the placement check never runs.
	/// </summary>
	private void AimClientCameraAtNearestRemoteGroup(double delta)
	{
		if (_role != "client") return;

		GameHost? gameHost = GameContext.Host;
		if (gameHost == null || GameContext.World == null) return;

		_remoteGroupCameraTimer -= (float)delta;
		if (_remoteGroupCameraTimer > 0f) return;
		_remoteGroupCameraTimer = RemoteGroupCameraIntervalSeconds;

		Camera3D? camera = gameHost.GetViewport()?.GetCamera3D();
		if (camera == null) return;

		int localPlayerIndex = gameHost.GetLocalPlayerIndexForHarness();
		if (localPlayerIndex < 0) return;

		Unit3D? densestAnchor = null;
		int densestNeighbourCount = 1;
		for (int anchorIndex = 0; anchorIndex < gameHost.AllUnits.Count; anchorIndex++)
		{
			Unit3D anchor = gameHost.AllUnits[anchorIndex];
			if (!GodotObject.IsInstanceValid(anchor) || anchor.IsBuilding || anchor.Player == localPlayerIndex) continue;

			int neighbourCount = CountRemoteNeighbours(gameHost, anchor, localPlayerIndex);
			if (neighbourCount <= densestNeighbourCount) continue;

			densestAnchor = anchor;
			densestNeighbourCount = neighbourCount;
		}

		if (densestAnchor == null) return;

		Vector3 cluster = Vector3.Zero;
		int members = 0;
		Vector3 anchorPosition = densestAnchor.GlobalPosition;
		for (int index = 0; index < gameHost.AllUnits.Count; index++)
		{
			Unit3D unit = gameHost.AllUnits[index];
			if (!GodotObject.IsInstanceValid(unit) || unit.IsBuilding || unit.Player == localPlayerIndex) continue;
			if (unit.GlobalPosition.DistanceTo(anchorPosition) > RemoteGroupRadiusMeters) continue;
			cluster += unit.GlobalPosition;
			members++;
		}

		if (members == 0) return;

		cluster /= members;
		Vector3 target = cluster + new Vector3(0f, RemoteGroupCameraHeightMeters, RemoteGroupCameraBackdropMeters);
		camera.GlobalPosition = target;

		if (!_remoteGroupCameraHasTarget)
		{
			_remoteGroupCameraHasTarget = true;
			GD.Print($"[MP_HARNESS] Client camera aimed at a group of {members} remote units around {cluster}; the host now describes them at detailed precision.");
		}
	}

	private static int CountRemoteNeighbours(GameHost gameHost, Unit3D anchor, int localPlayerIndex)
	{
		Vector3 anchorPosition = anchor.GlobalPosition;
		int neighbours = 0;
		for (int index = 0; index < gameHost.AllUnits.Count; index++)
		{
			Unit3D unit = gameHost.AllUnits[index];
			if (!GodotObject.IsInstanceValid(unit) || unit.IsBuilding || unit.Player == localPlayerIndex) continue;
			if (unit.GlobalPosition.DistanceTo(anchorPosition) > RemoteGroupRadiusMeters) continue;
			neighbours++;
		}

		return neighbours;
	}

	private bool IsScenarioNavigationReady()
	{
		NavigationParityDigest digest = NavigationParityDigest.Capture(GameContext.World, ECSContext.WorldEntity);
		return digest.HasNavMesh && digest.PolyCount > 0;
	}

	private int _scenarioTick;
	private int _scenarioPass;
	private bool _scenarioCompleted;
	private string _scenarioStage = string.Empty;

	private static bool TryResolveScenarioServerId(Entity entity, out int serverEntityId)
	{
		serverEntityId = -1;
		if (GameContext.NetworkService.TryGetServerEntityId(entity.Id, out serverEntityId)) return true;
		if (GameContext.Host?.HasAuthority != true) return false;
		serverEntityId = entity.Id;
		return serverEntityId > 0;
	}

	private void RunPredictionScenarioTick(double delta)
	{
		if (_role == "host")
		{
			RunAuthorityOrderScenarioTick();
			return;
		}

		if (_role != "client" && _role != "loopback") return;
		if (GameContext.Host == null || GameContext.World == null) return;
		if (_role == "client" && GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics < ScenarioMinimumSnapshotSequence) return;
		if (!IsScenarioNavigationReady()) return;

		if (_scenarioCompleted) return;
		if (_scenarioTick < ScenarioFinalMoveTick + 30) _scenarioTick++;

		int tick = _scenarioTick;
		if (tick == ScenarioStartTick) RunScenarioMove();
		if (tick >= ScenarioSweepStartTick && tick < ScenarioQueuedMoveTick && (tick - ScenarioSweepStartTick) % ScenarioSweepSpacingTicks == 0)
		{
			RunScenarioSweep((tick - ScenarioSweepStartTick) / ScenarioSweepSpacingTicks);
		}

		if (tick == ScenarioQueuedMoveTick) RunScenarioQueuedMove();
		else if (tick == ScenarioAttackMoveTick) RunScenarioAttackMove();
		else if (tick == ScenarioGatherTick) RunScenarioGather();
		else if (tick == ScenarioBuildTick) RunScenarioBuild();
		else if (tick == ScenarioCommandFloodTick) RunScenarioCommandFlood();
		else if (tick == ScenarioRejectedGatherTick) RunScenarioRejectedGather();
		else if (tick == ScenarioRemoteMoveTick) RunScenarioRemoteMove();
		else if (tick == ScenarioOwnerChangeTick) RunScenarioOwnerChangeAttempt();
		else if (tick == ScenarioFinalMoveTick) RunScenarioFinalMove();
	}

	private Unit3D ResolveScenarioUnit(bool requireWorker = false)
	{
		GameHost gameHost = GameContext.Host!;
		int localPlayerIndex = gameHost.GetLocalPlayerIndexForHarness();
		if (localPlayerIndex < 0) return null;

		for (int index = 0; index < gameHost.AllUnits.Count; index++)
		{
			var unit = gameHost.AllUnits[index];
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (unit.IsBuilding || unit.Player != localPlayerIndex) continue;
			if (!TryResolveScenarioServerId(unit.Entity, out _)) continue;
			if (requireWorker && unit.UnitId != "worker") continue;
			return unit;
		}

		return null;
	}

	private Unit3D ResolveScenarioRemoteUnit()
	{
		GameHost gameHost = GameContext.Host!;
		int localPlayerIndex = gameHost.GetLocalPlayerIndexForHarness();

		for (int index = 0; index < gameHost.AllUnits.Count; index++)
		{
			var unit = gameHost.AllUnits[index];
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (unit.IsBuilding || unit.Player == localPlayerIndex) continue;
			if (!TryResolveScenarioServerId(unit.Entity, out _)) continue;
			return unit;
		}

		return null;
	}

	private void SelectScenarioUnit(Unit3D unit)
	{
		GameHost gameHost = GameContext.Host!;
		gameHost.SelectedUnits.Clear();
		gameHost.SelectedUnits.Add(unit);
	}

	private void BeginScenarioStage(string stage, Unit3D unit, Vector3 target)
	{
		_scenarioStage = stage;
		int predictionTick = GameContext.MovementPredictionService?.LastPredictedTick ?? _scenarioTick;
		PredictionDiagnostics.RecordScenarioOrder(stage, predictionTick, unit != null ? unit.Entity.Id : 0, new System.Numerics.Vector3(target.X, target.Y, target.Z));
		if (unit != null)
		{
			GD.Print($"[MP_HARNESS] Scenario stage '{stage}': unit {unit.Entity.Id} server {GameContext.Host!.GetServerEntityIdForHarness(unit.Entity.Id)} player {unit.Player} localPlayer {GameContext.Host.LocalPlayerIndex} control={GameContext.PerspectiveFilter.CanControl(unit.Player)}.");
			PrintNavigationProbe(unit);
		}
	}

	private void PrintNavigationProbe(Unit3D unit)
	{
		if (!GameContext.World.Has<Realm.Ecs.Components.Terrain.TerrainState>(ECSContext.WorldEntity)) return;
		var state = GameContext.World.Get<Realm.Ecs.Components.Terrain.TerrainState>(ECSContext.WorldEntity);
		if (state.NavMeshQuery == null) return;

		ushort includeFlags = (ushort)(GameContext.World.Has<Realm.Ecs.Components.Movement.PathingFlags>(unit.Entity)
			? GameContext.World.Get<Realm.Ecs.Components.Movement.PathingFlags>(unit.Entity).Value
			: 8);
		var filter = new DotRecast.Detour.DtQueryDefaultFilter();
		filter.SetIncludeFlags(includeFlags);
		filter.SetExcludeFlags(0);
		var position = GameContext.World.Get<Realm.Ecs.Components.Core.Position>(unit.Entity).Value;
		state.NavMeshQuery.FindNearestPoly(
			new DotRecast.Core.Numerics.RcVec3f(position.X, position.Y, position.Z),
			new DotRecast.Core.Numerics.RcVec3f(10f, 10f, 10f),
			filter,
			out long reference,
			out DotRecast.Core.Numerics.RcVec3f nearest,
			out _);
		var anyFilter = new DotRecast.Detour.DtQueryDefaultFilter();
		anyFilter.SetIncludeFlags(0xFF);
		anyFilter.SetExcludeFlags(0);
		state.NavMeshQuery.FindNearestPoly(
			new DotRecast.Core.Numerics.RcVec3f(position.X, position.Y, position.Z),
			new DotRecast.Core.Numerics.RcVec3f(10f, 10f, 10f),
			anyFilter,
			out long anyReference,
			out DotRecast.Core.Numerics.RcVec3f anyNearest,
			out _);
		NavigationParityDigest digest = NavigationParityDigest.Capture(GameContext.World, ECSContext.WorldEntity);
		GD.Print($"[MP_HARNESS] Nav probe unit {unit.Entity.Id}: includeFlags={includeFlags} startRef={reference} nearest=({nearest.X:0.00},{nearest.Y:0.00},{nearest.Z:0.00}) anyRef={anyReference} anyNearest=({anyNearest.X:0.00},{anyNearest.Y:0.00},{anyNearest.Z:0.00}) polys={digest.PolyCount} ground={digest.GroundPolyCount} polyHash={digest.PolyHash:x8}.");
	}

	private static readonly Vector3[] ScenarioStraightOffsets =
	{
		new(16f, 0f, 0f), new(-16f, 0f, 0f), new(0f, 0f, 16f), new(0f, 0f, -16f)
	};

	private static readonly Vector3[] ScenarioDiagonalOffsets =
	{
		new(10f, 0f, 12f), new(-10f, 0f, 12f), new(12f, 0f, -10f), new(-12f, 0f, -10f)
	};

	private void RunScenarioMove()
	{
		if (!TryResolvePathableScenarioUnit(ScenarioStraightOffsets, out Unit3D unit, out Vector3 target)) return;

		SelectScenarioUnit(unit);
		BeginScenarioStage("move", unit, target);
		GameContext.Host!.IssueMoveCommand(target, false);
	}

	/// <summary>
	/// One pass of the sweep order set. Pathing parity is a claim about a route the host and the client produce for the
	/// same order, so the harness needs many distinct orders with corners rather than the four a single scenario pass
	/// issues. Each pass varies both distance and bearing so the route bends around navmesh geometry instead of running
	/// straight out from wherever the unit already is.
	/// </summary>
	private void RunScenarioSweep(int pass)
	{
		_scenarioPass = pass;
		if (!TryResolveSweepUnit(pass, out Unit3D unit, out Vector3 target)) return;

		SelectScenarioUnit(unit);
		BeginScenarioStage("move_sweep", unit, target);
		GameContext.Host!.IssueMoveCommand(target, false);
	}

	private bool TryResolveSweepUnit(int pass, out Unit3D unit, out Vector3 target)
	{
		unit = null;
		target = Vector3.Zero;
		GameHost gameHost = GameContext.Host!;
		int localPlayerIndex = gameHost.GetLocalPlayerIndexForHarness();
		float distance = 10f + pass % 4 * 4f;
		int bearingIndex = pass % 8;
		float bearing = bearingIndex * Mathf.Pi * 0.25f;

		for (int index = 0; index < gameHost.AllUnits.Count; index++)
		{
			var candidate = gameHost.AllUnits[index];
			if (!GodotObject.IsInstanceValid(candidate)) continue;
			if (candidate.IsBuilding || candidate.Player != localPlayerIndex) continue;
			if (!GameContext.World.Has<Realm.Ecs.Components.Core.UnitOwnerPlayer>(candidate.Entity)) continue;
			if (GameContext.World.Get<Realm.Ecs.Components.Core.UnitOwnerPlayer>(candidate.Entity).PlayerIndex != localPlayerIndex) continue;
			if (!TryResolveScenarioServerId(candidate.Entity, out _)) continue;

			var candidateTarget = candidate.GlobalPosition + new Vector3(Mathf.Cos(bearing) * distance, 0f, Mathf.Sin(bearing) * distance);
			if (!CanScenarioPath(candidate.Entity, candidate.GlobalPosition, candidateTarget)) continue;

			unit = candidate;
			target = candidateTarget;
			return true;
		}

		GD.PrintErr($"[MP_HARNESS] No locally controlled unit has a pathable destination for sweep pass {pass}.");
		return false;
	}

	private void RunScenarioQueuedMove()
	{
		if (!TryResolvePathableScenarioUnit(ScenarioDiagonalOffsets, out Unit3D unit, out Vector3 target)) return;

		SelectScenarioUnit(unit);
		BeginScenarioStage("move_queued", unit, target);
		GameContext.Host!.IssueMoveCommand(target, true);
	}

	private void RunScenarioAttackMove()
	{
		if (!TryResolvePathableScenarioUnit(ScenarioDiagonalOffsets, out Unit3D unit, out Vector3 target)) return;

		SelectScenarioUnit(unit);
		BeginScenarioStage("attack_move", unit, target);
		GameContext.Host!.IssueAttackMoveCommand(target);
	}

	private bool TryResolvePathableScenarioUnit(Vector3[] ignoredOffsets, out Unit3D unit, out Vector3 target)
	{
		unit = null;
		target = Vector3.Zero;
		GameHost gameHost = GameContext.Host!;
		int localPlayerIndex = gameHost.GetLocalPlayerIndexForHarness();

		for (int index = 0; index < gameHost.AllUnits.Count; index++)
		{
			var candidate = gameHost.AllUnits[index];
			if (!GodotObject.IsInstanceValid(candidate)) continue;
			if (candidate.IsBuilding || candidate.Player != localPlayerIndex) continue;
			if (!GameContext.World.Has<Realm.Ecs.Components.Core.UnitOwnerPlayer>(candidate.Entity)) continue;
			if (GameContext.World.Get<Realm.Ecs.Components.Core.UnitOwnerPlayer>(candidate.Entity).PlayerIndex != localPlayerIndex) continue;
			if (!TryResolveScenarioServerId(candidate.Entity, out _)) continue;

			for (int distanceIndex = 0; distanceIndex < 4; distanceIndex++)
			{
				float distance = 8f + distanceIndex * 4f;
				for (int directionIndex = 0; directionIndex < 8; directionIndex++)
				{
					float angle = directionIndex * Mathf.Pi * 0.25f;
					var candidateTarget = candidate.GlobalPosition + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
					if (!CanScenarioPath(candidate.Entity, candidate.GlobalPosition, candidateTarget)) continue;

					unit = candidate;
					target = candidateTarget;
					return true;
				}
			}
		}

		GD.PrintErr("[MP_HARNESS] No locally controlled unit has a pathable destination for the scenario stage.");
		return false;
	}

	private static bool CanScenarioPath(Entity entity, Vector3 start, Vector3 target)
	{
		if (GameContext.World == null || !GameContext.World.Has<Realm.Ecs.Components.Terrain.TerrainState>(ECSContext.WorldEntity)) return false;
		var state = GameContext.World.Get<Realm.Ecs.Components.Terrain.TerrainState>(ECSContext.WorldEntity);
		if (state.NavMeshQuery == null) return false;

		ushort includeFlags = (ushort)(GameContext.World.Has<Realm.Ecs.Components.Movement.PathingFlags>(entity)
			? GameContext.World.Get<Realm.Ecs.Components.Movement.PathingFlags>(entity).Value
			: 8);
		var filter = new DotRecast.Detour.DtQueryDefaultFilter();
		filter.SetIncludeFlags(includeFlags);
		filter.SetExcludeFlags(0);

		state.NavMeshQuery.FindNearestPoly(
			new DotRecast.Core.Numerics.RcVec3f(start.X, start.Y, start.Z),
			new DotRecast.Core.Numerics.RcVec3f(10f, 10f, 10f),
			filter,
			out long startReference,
			out _,
			out _);
		if (startReference == 0) return false;

		state.NavMeshQuery.FindNearestPoly(
			new DotRecast.Core.Numerics.RcVec3f(target.X, target.Y, target.Z),
			new DotRecast.Core.Numerics.RcVec3f(10f, 10f, 10f),
			filter,
			out long targetReference,
			out _,
			out _);
		return targetReference != 0;
	}

	private void RunScenarioGather()
	{
		Unit3D unit = ResolveScenarioUnit(requireWorker: true);
		if (unit == null) return;

		Resource3D prop = FindScenarioResourceProp(unit.GlobalPosition);
		if (prop == null) return;

		SelectScenarioUnit(unit);
		BeginScenarioStage("gather", unit, prop.GlobalPosition);
		GameContext.Host!.IssueGatherCommand(prop.Entity, false);
	}

	private Resource3D FindScenarioResourceProp(Vector3 origin)
	{
		GameHost gameHost = GameContext.Host!;
		Resource3D closest = null;
		float closestDistanceSquared = float.MaxValue;
		for (int index = 0; index < gameHost.AllProps.Count; index++)
		{
			if (gameHost.AllProps[index] is not Resource3D resource) continue;
			if (!GodotObject.IsInstanceValid(resource)) continue;
			if (GameContext.RegistryService.GetResourceTypeForNode(resource.PropId) == null) continue;

			float distanceSquared = resource.GlobalPosition.DistanceSquaredTo(origin);
			if (distanceSquared >= closestDistanceSquared) continue;
			closestDistanceSquared = distanceSquared;
			closest = resource;
		}

		return closest;
	}

	private void RunScenarioBuild()
	{
		Unit3D unit = ResolveScenarioUnit(requireWorker: true);
		if (unit == null) return;

		SelectScenarioUnit(unit);
		Vector3 target = unit.GlobalPosition + new Vector3(5f, 0f, 5f);
		BeginScenarioStage("build", unit, target);
		IssueClientBuildRequest(GameContext.Host!);
	}

	/// <summary>
	/// Submits far more commands than any player can issue so the authority has to refuse the excess rather than run the
	/// full command path hundreds of times inside one tick. The marker records that the flood was sent, so the host's
	/// hardening report can assert it was refused instead of assuming the exercise happened.
	/// </summary>
	private void RunScenarioCommandFlood()
	{
		if (_role != "client") return;

		Unit3D unit = ResolveLocalNonWorkerUnit() ?? ResolveScenarioUnit();
		if (unit == null) return;

		var serverIds = new List<int> { GameContext.NetworkService.GetServerEntityId(unit.Entity.Id) };
		Vector3 target = unit.GlobalPosition + new Vector3(6f, 0f, 0f);
		GameContext.Host!.SubmitCommandFloodForHarness("move", serverIds, target, ScenarioCommandFloodCount);
		File.WriteAllText(Path.Combine(_outputDirectory, "command_flood_sent.txt"), ScenarioCommandFloodCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
		GD.Print($"[MP_HARNESS] Client submitted {ScenarioCommandFloodCount} move commands for unit {unit.Entity.Id} to exercise the authority's command budget.");
	}

	private void RunScenarioRejectedGather()
	{
		Unit3D unit = ResolveLocalNonWorkerUnit();
		if (unit == null) return;

		Vector3 target = unit.GlobalPosition + new Vector3(40f, 0f, 40f);
		BeginScenarioStage("rejected_gather", unit, target);
		var serverIds = new List<int> { GameContext.NetworkService.GetServerEntityId(unit.Entity.Id) };
		GameContext.Host!.IssueRawCommandForHarness("gather", serverIds, target);
	}

	private Unit3D ResolveLocalNonWorkerUnit()
	{
		GameHost gameHost = GameContext.Host!;
		int localPlayerIndex = gameHost.GetLocalPlayerIndexForHarness();
		for (int index = 0; index < gameHost.AllUnits.Count; index++)
		{
			var unit = gameHost.AllUnits[index];
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (unit.IsBuilding || unit.UnitId == "worker") continue;
			if (!GameContext.World.Has<Realm.Ecs.Components.Core.UnitOwnerPlayer>(unit.Entity)) continue;
			if (GameContext.World.Get<Realm.Ecs.Components.Core.UnitOwnerPlayer>(unit.Entity).PlayerIndex != localPlayerIndex) continue;
			if (!TryResolveScenarioServerId(unit.Entity, out _)) continue;
			return unit;
		}

		return null;
	}

	private void RunScenarioRemoteMove()
	{
		Unit3D unit = ResolveScenarioRemoteUnit();
		if (unit == null) return;

		var serverIds = new List<int> { GameContext.NetworkService.GetServerEntityId(unit.Entity.Id) };
		Vector3 target = unit.GlobalPosition + new Vector3(12f, 0f, 0f);
		BeginScenarioStage("remote_move", unit, target);
		GameContext.Host!.IssueRawCommandForHarness("move", serverIds, target);
	}

	private void RunScenarioOwnerChangeAttempt()
	{
		Unit3D unit = ResolveScenarioRemoteUnit();
		if (unit == null) return;

		var serverIds = new List<int> { GameContext.NetworkService.GetServerEntityId(unit.Entity.Id) };
		Vector3 target = unit.GlobalPosition + new Vector3(0f, 0f, 12f);
		BeginScenarioStage("owner_change", unit, target);
		GameContext.Host!.IssueRawCommandForHarness("gather", serverIds, target);
	}

	private void RunScenarioFinalMove()
	{
		_scenarioCompleted = true;
		if (!TryResolvePathableScenarioUnit(ScenarioStraightOffsets, out Unit3D unit, out Vector3 target)) return;

		SelectScenarioUnit(unit);
		BeginScenarioStage("final_move", unit, target);
		GameContext.Host!.IssueMoveCommand(target, false);
	}
}
