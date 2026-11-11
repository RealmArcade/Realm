using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Realm.Ecs;
using Realm.Ecs.Replication;
using DotRecast.Detour;
using Godot;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = Godot.Environment;
using FileAccess = System.IO.FileAccess;
using Realm.Shared.Abilities;
using Realm.Shared.Diagnostics;

namespace Realm.Client.Core;
public partial class GameHost
{
	public bool TryGetInterpolatedTransform(Entity entity, float fraction, out System.Numerics.Vector3 position, out float rotationY)
	{
		if (GameContext.SimulationService != null)
		{
			return GameContext.SimulationService.TryGetInterpolatedTransform(entity, fraction, out position, out rotationY);
		}

		position = System.Numerics.Vector3.Zero;
		rotationY = 0f;
		return false;
	}

	public (System.Numerics.Vector3 Position, float RotationY) GetInterpolatedTransform(Entity entity, float fraction)
	{
		return GameContext.SimulationService?.GetInterpolatedTransform(entity, fraction) ?? (System.Numerics.Vector3.Zero, 0f);
	}

	private double _lastProbedRenderSeconds;
	private double _lastProbedPhysicsSeconds;

	private TimeSpan _lastProbedGcPause;
	private int _lastProbedGcPauseTicks;
	private long _lastProbedAllocatedBytes = -1;

	public override void _Process(double delta)
	{
		long renderStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
		if (renderStarted != 0L)
		{
			double nowSeconds = LagProbe.NowSeconds();
			if (_lastProbedRenderSeconds > 0d)
			{
				double intervalMilliseconds = (nowSeconds - _lastProbedRenderSeconds) * 1000d;
				LagProbe.Add(LagStage.ClientRenderIntervalMilliseconds, intervalMilliseconds);
				LagProbe.ObserveGauge(LagGauge.ClientFramesPerSecond, intervalMilliseconds > 0d ? 1000d / intervalMilliseconds : 0d, nowSeconds);
			}

			// A frame that stalls because of a collection is a hitch a screen feels without any of the stages above being
			// slow, so what the collector has cost in total is sampled alongside them rather than inferred from them.
			LagProbe.ObserveGauge(LagGauge.ClientManagedHeapMegabytes, GC.GetTotalMemory(false) / 1048576d, nowSeconds);
			LagProbe.ObserveGauge(LagGauge.ClientGcPauseMilliseconds, GC.GetTotalPauseDuration().TotalMilliseconds, nowSeconds);
			LagProbe.ObserveGauge(LagGauge.ClientGen0Collections, GC.CollectionCount(0), nowSeconds);
			LagProbe.ObserveGauge(LagGauge.ClientGen2Collections, GC.CollectionCount(2), nowSeconds);

			// What this frame paid to the collector and what it allocated, both as the frame's own share rather than as a
			// running total: a total says a match collected for two hundred milliseconds, and only the share says whether the
			// frame that arrived late was the one that paid for it.
			TimeSpan gcPaused = GC.GetTotalPauseDuration();
			if (_lastProbedGcPauseTicks > 0) LagProbe.Add(LagStage.ClientGcPausePerFrameMilliseconds, (gcPaused - _lastProbedGcPause).TotalMilliseconds);
			_lastProbedGcPause = gcPaused;
			_lastProbedGcPauseTicks++;

			long allocatedBytes = GC.GetAllocatedBytesForCurrentThread();
			if (_lastProbedAllocatedBytes >= 0) LagProbe.Add(LagStage.ClientAllocatedKilobytesPerFrame, (allocatedBytes - _lastProbedAllocatedBytes) / 1024d);
			_lastProbedAllocatedBytes = allocatedBytes;

			_lastProbedRenderSeconds = nowSeconds;
		}

		ProcessRenderFrame(delta);

		if (renderStarted == 0L) return;

		double renderMilliseconds = LagProbe.ElapsedMilliseconds(renderStarted);
		if (renderMilliseconds > 33.4d) LagProbe.Count(LagEvent.ClientRenderFramesOverBudget);
	}

	private void ProcessRenderFrame(double delta)
	{
		if (!_firstFrameTraced && (_matchWorldVisible || GameContext.SessionState.IsMapEditorMode || GameContext.Lobby?.IsGameStarted == true))
		{
			_firstFrameTraced = true;
			MatchLoadTrace.Mark("first_frame");
			MatchLoadTrace.WriteSummary("first_frame");
		}

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.GodotProcess))
		{
			float fDelta = (float)delta;

			if (GameContext.World != null)
			{
				using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.UpdateVisualNodesFromEcs))
				{
					AdvancePresentationForRenderFrame(fDelta);
					UpdateRenderFrameTransforms(fDelta);
					UpdateSelectedAndImpulseUnitVisuals(fDelta);
				}
			}

			if (GameContext.ShroudService != null)
			{
				using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.ShroudServiceTick))
				{
					bool isReplay = GameContext.ReplayPlayback.IsPlayingReplay;
					bool isSpectator = GameContext.PerspectiveFilter.IsConfigured && GameContext.PerspectiveFilter.IsSpectating;
					int spectatorPerspective = GameContext.InGameHud?.LiveSpectatorPerspective ?? -1;
					GameContext.ShroudService.Tick(fDelta, AllUnits, AllProps, AllDecals, AllVfx, VisualProjectilePool.ActiveProjectiles, spectatorPerspective, isReplay, isSpectator);
				}
			}

			if (_unitOcclusionOutlineService != null && GodotObject.IsInstanceValid(_unitOcclusionOutlineService))
			{
				using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.UnitOcclusionOutlineTick))
				{
					_unitOcclusionOutlineService.Tick(fDelta, MainCamera, AllUnits);
				}
			}

			if (_cachedWorldEnvironment == null || !GodotObject.IsInstanceValid(_cachedWorldEnvironment))
			{
				_cachedWorldEnvironment = MainNode?.GetNodeOrNull<WorldEnvironment>("WorldEnvironment") ?? GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
			}

			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.EnvironmentUpdateFog))
			{
				GameContext.EnvironmentService?.UpdateEnvironmentalFog(MainCamera, _cachedWorldEnvironment);
			}
			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.EnvironmentUpdateWeatherParticles))
			{
				GameContext.EnvironmentService?.UpdateWeatherParticlePosition(MainCamera);
			}
			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.AudioPositionalUpdate))
			{
				if (MainCamera != null && GodotObject.IsInstanceValid(MainCamera))
				{
					GameContext.AudioService?.UpdateListenerFromCamera(MainCamera.GlobalTransform);
				}
			}
			VisualProjectilePool.AdvanceActiveProjectiles(fDelta);

			GameContext.FXService?.UpdateFloatingLabels(fDelta);
			if (MainCamera != null) GameContext.FXService?.RenderBatchedRibbonTrails(MainCamera);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		long physicsStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
		if (physicsStarted != 0L)
		{
			double nowSeconds = LagProbe.NowSeconds();
			if (_lastProbedPhysicsSeconds > 0d) LagProbe.Add(LagStage.ClientPhysicsIntervalMilliseconds, (nowSeconds - _lastProbedPhysicsSeconds) * 1000d);
			_lastProbedPhysicsSeconds = nowSeconds;
		}

		ProcessPhysicsFrame(delta, physicsStarted);
	}

	private void ProcessPhysicsFrame(double delta, long physicsStarted)
	{
		try
		{
			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.GodotPhysicsProcess))
			{
				ProcessPhysicsFrameCore(delta, physicsStarted);
			}
		}
		finally
		{
			if (physicsStarted != 0L)
			{
				double physicsMilliseconds = LagProbe.ElapsedMilliseconds(physicsStarted);
				LagProbe.Add(LagStage.ClientPhysicsTickMilliseconds, physicsMilliseconds);
				if (physicsMilliseconds > 33.4d) LagProbe.Count(LagEvent.ClientPhysicsTicksOverBudget);
			}
		}
	}

	private void ProcessPhysicsFrameCore(double delta, long physicsStarted)
	{
		{
			if (!ShouldProcessPhysicsTick()) return;

			// Building a replicated unit is this screen's work, and it is given a share of each tick rather than all of it
			// in the tick the description arrived on. Units left over from the last tick are built first, so a burst
			// finishes even when no new description follows it.
			_replicatedUnitSpawnBudgetRemainingMilliseconds = ReplicatedUnitSpawnBudgetMilliseconds;
			if (GameContext.NetworkService is { HasPendingUnitSpawns: true }) FlushReplicatedSnapshotEvents();

			if (!_simulationStartTraced && _joinedSession != null)
			{
				_simulationStartTraced = true;
				MatchLoadTrace.Mark("simulation_started");
				MatchLoadTrace.WriteSummary("simulation_started");
			}

			float fDelta = (float)delta;
			if (_wasClientInMultiplayer) UpdateConnectionStatus();

			if (GameContext.ReplayPlayback.IsPlayingReplay)
			{
				ProcessReplayTick(fDelta);
				return;
			}

			GameContext.ServerSession?.Pump();

			if (GameContext.NetworkService.IsPaused) return;

			DispatchPhysicsTick(fDelta);
		}
	}

	private bool ShouldProcessPhysicsTick()
	{
		// A world prepared for a lobby is a picture, not a world yet: the match begins at tick zero.
		if (IsBuildingMatchWorld) return false;
		return GameContext.Lobby?.IsGameStarted == true || GameContext.SessionState.IsMapEditorMode;
	}

	private void ProcessReplayTick(float fDelta)
	{
		CapturePreviousStateSnapshots();
		GameContext.ReplayPlayback.Update(fDelta);
	}

	private void DispatchPhysicsTick(float fDelta)
	{
		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.DispatchPhysicsTick))
		{
			if (GameContext.SessionState.IsMapEditorMode) ProcessMapEditorTick(fDelta);
			else UpdateClientTick(fDelta);
		}
	}

	public bool IsMultiplayerActive() => _multiplayerActive;

	public int ObservingPlayerIndex
	{
		get
		{
			var filter = GameContext.PerspectiveFilter;
			if (!filter.IsConfigured || filter.IsSpectating) return -1;
			return filter.ControlledPlayerIndex;
		}
	}

	public bool IsLocalViewpoint(int playerIndex) => playerIndex > 0 && playerIndex == ObservingPlayerIndex;

	public void RebakeNavMesh()
	{
		if (GroundTerrain == null) return;

		// Every bake uses the volumes the match baked with, so one that runs before the authority's set arrived and one
		// that runs after it describe the same corridors.
		ApplyStoredObstacleVolumes();

		// A joined match paths through the mesh the authority built, and the volumes it was built from are on their way.
		// Baking before they arrive answers corridors the authority treats as blocked, so the bake waits - bounded by
		// the deadline, because an authority with no mesh to report never sends one.
		if (_awaitingAuthoritativeObstacleVolumes && Time.GetTicksMsec() < _authoritativeObstacleDeadlineMsec)
		{
			RequestNavMeshRefresh();
			return;
		}

		GroundTerrain.BakeNavMesh();
	}

	private const ulong NavMeshRefreshDelayMsec = 500;
	private bool _navMeshRefreshPending;
	private ulong _navMeshRefreshDeadlineMsec;

	private const ulong AuthoritativeObstacleWaitMsec = 10000;

	private bool _awaitingAuthoritativeObstacleVolumes;
	private ulong _authoritativeObstacleDeadlineMsec;

	/// <summary>
	///     States that this screen's mesh is not its own: a joined match bakes from the volumes its authority reports, so
	///     a bake is held until they arrive rather than built from the props this screen happens to hold. The wait covers
	///     a slow map load, because a screen can be seated before the authority's first bake has run.
	/// </summary>
	public void AwaitAuthoritativeObstacleVolumes()
	{
		_awaitingAuthoritativeObstacleVolumes = true;
		_authoritativeObstacleDeadlineMsec = Time.GetTicksMsec() + AuthoritativeObstacleWaitMsec;
	}

	public void StopAwaitingAuthoritativeObstacleVolumes() => _awaitingAuthoritativeObstacleVolumes = false;

	/// <summary>
	/// Asks for a navigation mesh bake once the world has stopped changing. A bake is expensive and a map load spawns
	/// hundreds of props through independent requests, so the refresh waits for the last one instead of baking per prop.
	/// The wait is measured on the clock rather than in frame deltas: a machine running several peers advances a frame by
	/// more than the whole wait, which made the bake fire after the first prop and miss the rest of the map.
	/// </summary>
	public void RequestNavMeshRefresh()
	{
		if (GameContext.SessionState.IsMapEditorMode) return;
		_navMeshRefreshPending = true;
		_navMeshRefreshDeadlineMsec = Time.GetTicksMsec() + NavMeshRefreshDelayMsec;
	}

	public void UpdateNavMeshRefresh()
	{
		if (!_navMeshRefreshPending) return;
		if (Time.GetTicksMsec() < _navMeshRefreshDeadlineMsec) return;

		_navMeshRefreshPending = false;
		// Any new obstacle requests the next refresh itself, so this one only has to cover what already exists.
		RebakeNavMesh();
	}

	public bool IsNavMeshRefreshPending => _navMeshRefreshPending;

	/// <summary>
	///     Opens the ground a destroyed obstacle used to block. Only the editor does this: in a match the navigation mesh
	///     is baked from the obstacle set the authority reports, so a screen that carved its own holes would path through
	///     ground the authority still treats as blocked.
	/// </summary>
	public void UncarveObstacle(System.Numerics.Vector3 pos, float radius)
	{
		if (!GameContext.SessionState.IsMapEditorMode) return;
		if (GameContext.World != null && World.IsAlive(GameContext.Simulation.WorldEntity) && World.Has<TerrainState>(GameContext.Simulation.WorldEntity))
		{
			ref var state = ref World.Get<TerrainState>(GameContext.Simulation.WorldEntity);
			GameContext.Simulation.TerrainNavMeshService?.UncarveObstacle(ref state, pos, radius);
		}
	}

	public float ResolveTemplateBodyRadius(string templateId)
	{
		float radius = ResolveTemplateCollisionRadius(templateId);
		if (radius <= GameplayConstants.MinimumAuthoredCollisionRadius) return GameplayConstants.DefaultCollisionRadius;
		return radius;
	}

	/// <summary>
	///     Measures the horizontal footprint radius (max corner distance from the origin in the XZ plane) of a freshly
	///     instantiated model, so an import can persist an obstacle radius for assets that have no collision shapes.
	/// </summary>
	public float MeasureModelRadius(Node3D root)
	{
		if (root == null) return 0f;
		float maxRadius = 0f;
		MeasureModelRadiusRecursive(root, Transform3D.Identity, ref maxRadius);
		return maxRadius;
	}

	public float MeasureModelRadiusFromFile(string modelPath)
	{
		Node? modelNode = ModelCache.GetModel(modelPath);
		if (modelNode == null) return 0f;

		try
		{
			return modelNode is Node3D node3D ? MeasureModelRadius(node3D) : 0f;
		}
		finally
		{
			modelNode.Free();
		}
	}

	private void MeasureModelRadiusRecursive(Node3D node, Transform3D parentXform, ref float maxRadius)
	{
		var localXform = parentXform * node.Transform;
		if (node is MeshInstance3D meshNode && meshNode.Mesh != null)
		{
			var aabb = meshNode.Mesh.GetAabb();
			if (aabb.Size != Vector3.Zero)
			{
				for (int i = 0; i < 8; i++)
				{
					var corner = localXform * aabb.GetEndpoint(i);
					float r = Mathf.Sqrt(corner.X * corner.X + corner.Z * corner.Z);
					if (r > maxRadius) maxRadius = r;
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node3D child3D)
			{
				MeasureModelRadiusRecursive(child3D, localXform, ref maxRadius);
			}
		}
	}

	public bool IsPathingCapability(Entity entity, TerrainPathingFlags capability)
	{
		if (GameContext.World == null || !World.IsAlive(entity) || !World.Has<TraversableTerrain>(entity)) return false;
		int flags = World.Get<TraversableTerrain>(entity).Value;
		return ((TerrainPathingFlags)flags & capability) != 0;
	}

	private static ImageTexture? SharedShadowGradient { get; set; }

	public Texture2D GetSharedShadowGradient()
	{
		if (SharedShadowGradient != null && GodotObject.IsInstanceValid(SharedShadowGradient))
		{
			return SharedShadowGradient;
		}

		const int size = 256;
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float dx = (x + 0.5f) / size - 0.5f;
				float dy = (y + 0.5f) / size - 0.5f;
				float dist = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
				float alpha = Mathf.Clamp(1f - dist, 0f, 1f);
				alpha *= alpha;
				img.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
			}
		}

		if (!img.HasMipmaps())
		{
			img.GenerateMipmaps();
		}

		SharedShadowGradient = ImageTexture.CreateFromImage(img);
		return SharedShadowGradient;
	}

	private void UpdateConnectionStatus()
	{
		GameContext.NetworkService.UpdateConnectionStatus(_multiplayerActive, isServer: false);
	}

	private void ProcessVisualAndEnvironment(float fDelta)
	{
		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.ProcessVisualAndEnvironment))
		{
			if (GameContext.World != null && World.IsAlive(GameContext.Simulation.WorldEntity) && World.Has<WorldState>(GameContext.Simulation.WorldEntity))
			{
				var state = World.Get<WorldState>(GameContext.Simulation.WorldEntity);
				if (state.DayNightCycleEnabled && !GameContext.SessionState.IsMapEditorMode)
				{
					using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.UpdateDayNightVisuals))
					{
						float progress = state.TimeOfDayTimer / GameContext.EnvironmentService.TimeOfDayCycleDuration;
						UpdateDayNightVisuals(progress);
					}
				}
			}

			if (GameContext.EnvironmentService != null && GameContext.EnvironmentService.IsTransitioning)
			{
				using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.EnvironmentUpdateTransition))
				{
					GameContext.EnvironmentService.UpdateTransition(fDelta);
				}
			}

			GameContext.WindService?.Tick(fDelta);

			using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.UpdateMinimapPings))
			{
				UpdateMinimapPings(fDelta);
			}

			GameContext.FXService.UpdateVfxSequences(fDelta);
		}
	}

	private void RecordGameplayTickIfNeeded()
	{
		if (GameContext.ReplayPlayback.IsPlayingReplay || !GameSettings.RecordReplays) return;
		if (GameContext.ReplayService == null || !GameContext.ReplayService.IsRecording) return;

		using (GameContext.PerformanceTrackingService.SampleFunction(ProfileFunctionId.RecordGameplayTick))
		{
			RecordGameplayTick();
		}
	}

	private void PollVFXQueue()
	{
		if (GameContext.World == null || !World.IsAlive(GameContext.Simulation.WorldEntity) || !World.Has<VFXQueue>(GameContext.Simulation.WorldEntity)) return;

		ref var queue = ref World.Get<VFXQueue>(GameContext.Simulation.WorldEntity);
		if (queue.Count == 0) return;

		for (int i = 0; i < queue.Count; i++)
		{
			ProcessVFXRequest(queue.Get(i));
		}

		queue.Clear();
	}

	private void ProcessVFXRequest(VFXRequest req)
	{
		var godotPos = new Vector3(req.Position.X, req.Position.Y, req.Position.Z);
		var godotTarget = new Vector3(req.TargetPosition.X, req.TargetPosition.Y, req.TargetPosition.Z);

		if (TryProcessStagedVfx(req, godotPos)) return;
		if (TryProcessAbilityVfx(req, godotPos)) return;
		if (TryProcessProjectileVfx(req, godotPos, godotTarget)) return;

		ProcessMiscVfx(req, godotPos, godotTarget);
	}

	private bool TryProcessStagedVfx(VFXRequest req, Vector3 godotPos)
	{
		var registry = GameContext.RegistryService?.VfxRegistry;
		if (registry == null || !registry.TryGetValue(req.EffectTypeId, out var sequenceConfig)) return false;
		if (sequenceConfig?.Stages == null || sequenceConfig.Stages.Count == 0) return false;

		GameContext.FXService.SpawnVfxSequence(sequenceConfig, this, godotPos, req.Scale <= 0f ? 1.0f : req.Scale, req.EntityId);
		return true;
	}

	private bool TryProcessAbilityVfx(VFXRequest req, Vector3 godotPos)
	{
		var def = GetAbilityDefinition(req.EffectTypeId);
		if (def == null) return false;

		GameContext.FXService.SpawnAbilityEffect(this, def, godotPos);
		return true;
	}

	private bool TryProcessProjectileVfx(VFXRequest req, Vector3 godotPos, Vector3 godotTarget)
	{
		if (req.EffectTypeId != "arrow" && !WeaponRegistry.ContainsKey(req.EffectTypeId) && !req.EffectTypeId.StartsWith("proj:")) return false;

		string weaponId = req.EffectTypeId.StartsWith("proj:") ? req.EffectTypeId.Substring(5) : req.EffectTypeId;
		SpawnWeaponProjectile(godotPos, godotTarget, weaponId, GetEntityById(req.EntityId), req.OwnerPlayerIndex);
		return true;
	}

	private void ProcessMiscVfx(VFXRequest req, Vector3 godotPos, Vector3 godotTarget)
	{
		if (req.EffectTypeId == "heal") SpawnHealVisualEffect(godotPos, godotTarget);
		else if (req.EffectTypeId == "target_indicator") SpawnTargetIndicator(godotPos, new Color(req.Scale, 0.3f, 0.1f));
		else if (req.EffectTypeId == "damage_flash" || req.EffectTypeId == "heal_flash") TryProcessUnitFlash(req);
	}

	private void TryProcessUnitFlash(VFXRequest req)
	{
		if (req.EntityId == -1) return;
		if (!EntityIdToUnit3D.TryGetValue(req.EntityId, out var unit) || !GodotObject.IsInstanceValid(unit)) return;

		if (req.EffectTypeId == "damage_flash") GameContext.FXService.FlashDamageUnit(unit);
		else GameContext.FXService.FlashHealUnit(unit);
	}

	private Entity GetEntityById(int entityId)
	{
		if (entityId == -1) return Entity.Null;
		return EntityIdToUnit3D.TryGetValue(entityId, out var unit) && GodotObject.IsInstanceValid(unit) ? unit.Entity : Entity.Null;
	}

	private void UpdateMinimapPings(float fDelta)
	{
		for (int i = GameContext.FXService.ActivePings.Count - 1; i >= 0; i--)
		{
			var ping = GameContext.FXService.ActivePings[i];
			ping.LifeTime += fDelta;
			if (ping.LifeTime >= ping.MaxLifeTime)
			{
				GameContext.FXService.ActivePings.RemoveAt(i);
			}
			else
			{
				GameContext.FXService.ActivePings[i] = ping;
			}
		}
	}

	private void UpdateDayNightVisuals(float progress)
	{
		GameContext.EnvironmentService?.UpdateDayNightVisuals(progress);
	}

	public (int TimeOfDayIndex, float TimeOfDayTimer) SetTimeOfDay(int timeOfDayIndex)
	{
		return GameContext.EnvironmentService?.SetTimeOfDay(GameContext.Simulation.WorldEntity, timeOfDayIndex, GameContext.EnvironmentService.TimeOfDayCycleDuration) ?? (0, 0f);
	}

	public (int TimeOfDayIndex, float TimeOfDayTimer) CycleTimeOfDay()
	{
		return GameContext.EnvironmentService?.CycleTimeOfDay(GameContext.Simulation.WorldEntity, GameContext.EnvironmentService.TimeOfDayCycleDuration) ?? (0, 0f);
	}

	public string GetTimeOfDayName()
	{
		return GameContext.EnvironmentService?.GetTimeOfDayName(TimeOfDayIndex) ?? "Unknown";
	}

	private void SpawnSpritesheetEffect(string texturePath, Vector3 worldPosition, int columns, int rows, float secondsPerFrame, float sizeInWorldUnits)
	{
		GameContext.FXService.SpawnSpritesheetEffect(this, texturePath, worldPosition, columns, rows, secondsPerFrame, sizeInWorldUnits);
	}

	private void FlashDamageUnit(Unit3D unit)
	{
		GameContext.FXService.FlashDamageUnit(unit);
	}

	private void SpawnHealVisualEffect(Vector3 start, Vector3 target)
	{
		GameContext.FXService.SpawnHealVisualEffect(this, start, target);
	}

	private void FlashHealUnit(Unit3D unit)
	{
		GameContext.FXService.FlashHealUnit(unit);
	}

	private void SpawnPing3DEffect(Vector3 position)
	{
		GameContext.FXService.SpawnPing3DEffect(this, position);
	}

	public void SpawnWeaponProjectileForReplay(System.Numerics.Vector3 start, System.Numerics.Vector3 target, string? weaponId = null)
	{
		GameContext.FXService.SpawnWeaponProjectile(this, new Vector3(start.X, start.Y, start.Z), new Vector3(target.X, target.Y, target.Z), weaponId);
	}

	private void SpawnArrowProjectile(Vector3 start, Vector3 target)
	{
		SpawnWeaponProjectile(start, target, "arrow");
	}

	public void SpawnWeaponProjectile(Vector3 start, Vector3 target, string? weaponId = null, Entity targetEntity = default, int ownerPlayerIndex = 0)
	{
		GameContext.FXService.SpawnWeaponProjectile(this, start, target, weaponId, targetEntity, ownerPlayerIndex);
		if (GameContext.ReplayService != null && GameContext.ReplayService.IsRecording)
		{
			GameContext.ReplayService.RecordProjectile(weaponId ?? "arrow", new System.Numerics.Vector3(start.X, start.Y, start.Z), new System.Numerics.Vector3(target.X, target.Y, target.Z));
		}
	}

	private void SpawnTargetIndicator(Vector3 position, Color color)
	{
		GameContext.FXService.SpawnTargetIndicator(this, position, color);
	}

	public void AddMinimapPing(Vector3 position)
	{
		GameContext.FXService.AddMinimapPing(this, GameContext.FXService.ActivePings, position);
	}
}
