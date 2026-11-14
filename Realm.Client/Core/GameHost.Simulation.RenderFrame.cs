using System;
using System.Collections.Generic;
using Realm.Ecs;
using Godot;
using Realm.Client.ReplaySystem;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using UnitId = Realm.Ecs.UnitId;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Core;

public partial class GameHost
{
	private struct UnitRenderWorkItem
	{
		public Entity Entity;
		public Unit3D Unit;
		public Vector3 PosValue;
		public bool HasCachedTransform;
		public Godot.Vector3 CachedPosition;
		public Godot.Vector3 CachedScale;
		public float CachedBaseScale;
		public float CachedRotationY;
		public Basis CachedBasis;
		public Godot.Vector3 CachedUnitVelocity;
		public Aabb CachedLocalAabb;
		public bool HasEvaluatedFrustum;
		public bool IsCulled;
		public bool IsFlashingScale;

		public bool ShouldSetCulledState;
		public bool NewIsCulled;
		public bool NewHasEvaluatedFrustum;

		public bool ShouldUpdateRenderPos;
		public Vector3 NewRenderPos;

		public bool ShouldZeroEcsVelocity;
		public bool ShouldUpdateUnitVelocity;
		public Godot.Vector3 NewUnitVelocity;

		public bool ShouldUpdateEcsRotationY;
		public float NewEcsRotationY;

		public bool ShouldSetTransform;
		public Transform3D FinalTransform;
		public Godot.Vector3 NextPos;
		public float TargetRotY;
		public Basis TargetBasis;
		public Godot.Vector3 FinalScaleVec;
		public float ClampedScale;
	}

	private UnitRenderWorkItem[] _renderWorkItems = new UnitRenderWorkItem[256];
	private int _renderWorkItemCount = 0;
	private float _animationSelectionAccumulator = 0f;
	private float _renderFDelta;
	private float _renderFraction;
	private bool _renderHasFrustum;
	private Plane[] _renderFrustumPlanes = Array.Empty<Plane>();
	private bool _renderCameraFrustumChanged;
	private float _renderCurrentGameTime;
	private TerrainCell[,]? _renderGroundCells;
	private float _renderGroundQuadSize = RuntimeTerrain.DefaultQuadSize;
	private Action<int>? _processRenderWorkItemDelegate;
	private EntityCallback<Position>? _gatherRenderWorkItemDelegate;

	private readonly Plane[] _cachedCameraFrustumPlanes = new Plane[6];
	private readonly Plane[] _previousCameraFrustumPlanes = new Plane[6];
	private bool _hadFrustumLastFrame = false;

	private void UpdateRenderFrameTransforms(float fDelta)
	{
		var camera = MainCamera ?? GetViewport()?.GetCamera3D();
		bool hasFrustum = false;
		if (camera != null && GodotObject.IsInstanceValid(camera))
		{
			var frustum = camera.GetFrustum();
			if (frustum != null && frustum.Count >= 6)
			{
				for (int i = 0; i < 6; i++)
				{
					_cachedCameraFrustumPlanes[i] = frustum[i];
				}
				hasFrustum = true;
			}
		}

		bool cameraFrustumChanged = false;
		if (hasFrustum != _hadFrustumLastFrame)
		{
			cameraFrustumChanged = true;
			_hadFrustumLastFrame = hasFrustum;
		}

		if (hasFrustum)
		{
			for (int i = 0; i < 6; i++)
			{
				if (!cameraFrustumChanged && (_cachedCameraFrustumPlanes[i].Normal != _previousCameraFrustumPlanes[i].Normal || Mathf.Abs(_cachedCameraFrustumPlanes[i].D - _previousCameraFrustumPlanes[i].D) > 0.001f))
				{
					cameraFrustumChanged = true;
				}
				_previousCameraFrustumPlanes[i] = _cachedCameraFrustumPlanes[i];
			}
		}

		_animationSelectionAccumulator += fDelta;
		bool shouldDriveUnitAnimations = !GameContext.SessionState.IsMapEditorMode && _animationSelectionAccumulator >= GameplayConstants.UnitAnimationStepIntervalSeconds;
		if (shouldDriveUnitAnimations)
		{
			_animationSelectionAccumulator = 0f;
		}

		_renderFDelta = fDelta;
		_renderFraction = GameContext.ReplayPlayback.IsPlayingReplay || GameContext.SessionState.IsMapEditorMode
			? (float)Engine.GetPhysicsInterpolationFraction()
			: 1f;
		_renderHasFrustum = hasFrustum;
		_renderFrustumPlanes = hasFrustum ? _cachedCameraFrustumPlanes : Array.Empty<Plane>();
		_renderCameraFrustumChanged = cameraFrustumChanged;
		_renderCurrentGameTime = GameElapsedTime;
		RuntimeTerrain? renderTerrain = (GroundTerrain != null && GodotObject.IsInstanceValid(GroundTerrain)) ? GroundTerrain : null;
		_renderGroundCells = renderTerrain?.Cells;
		_renderGroundQuadSize = renderTerrain?.QuadSize ?? RuntimeTerrain.DefaultQuadSize;

		_renderWorkItemCount = 0;
		var query = QueryCache.AllPositionAndDefinitionIdQuery;
		_gatherRenderWorkItemDelegate ??= GatherRenderWorkItem;
		World.Query(in query, _gatherRenderWorkItemDelegate);

		if (_renderWorkItemCount == 0)
			return;

		_processRenderWorkItemDelegate ??= ProcessRenderWorkItemParallel;
		ParallelRunner.Execute(_renderWorkItemCount, _processRenderWorkItemDelegate, World);

		var world = World;
		float currentGameTime = _renderCurrentGameTime;
		for (int i = 0; i < _renderWorkItemCount; i++)
		{
			ref var item = ref _renderWorkItems[i];
			var unit3D = item.Unit;

			if (item.ShouldUpdateRenderPos && world.Has<RenderPosition>(item.Entity))
			{
				world.Set(item.Entity, new RenderPosition(item.NewRenderPos));
			}

			if (item.ShouldZeroEcsVelocity && world.Has<Velocity>(item.Entity))
			{
				world.Set(item.Entity, new Velocity(Vector3.Zero));
			}

			if (item.ShouldUpdateEcsRotationY && world.Has<RotationY>(item.Entity))
			{
				world.Set(item.Entity, new RotationY(item.NewEcsRotationY));
			}

			if (item.NewHasEvaluatedFrustum)
			{
				unit3D.HasEvaluatedFrustum = true;
			}

			if (item.ShouldSetCulledState)
			{
				unit3D.SetCulledState(item.NewIsCulled, currentGameTime);
			}

			if (item.ShouldUpdateUnitVelocity)
			{
				unit3D.Velocity = item.NewUnitVelocity;
			}

			unit3D.SetRuinSilhouette(world.Has<Dormant>(item.Entity) || world.Has<UnderConstruction>(item.Entity));
			unit3D.SetConstructionBlocked(IsConstructionHeldByFoodCap(item.Entity));

			if (item.ShouldSetTransform)
			{
				unit3D.SetTransform3D(in item.FinalTransform, in item.NextPos, item.TargetRotY, in item.TargetBasis, in item.FinalScaleVec, item.ClampedScale);
			}

			if (shouldDriveUnitAnimations && !unit3D.IsCulled && unit3D.Visible)
			{
				unit3D.PlayAnimation(DetermineUnitAnimation(item.Entity));
			}

			unit3D.StepManualAnimation(fDelta);
			item.Unit = null!;
		}
	}

	private void GatherRenderWorkItem(Entity entity, ref Position pos)
	{
		if (!TryGetUnit3D(entity, out var unit3D) || !GodotObject.IsInstanceValid(unit3D))
			return;

		var posValue = pos.Value;
		if (CheckNonFinitePositionWarning(entity, posValue, unit3D))
			return;

		if (_renderWorkItemCount >= _renderWorkItems.Length)
		{
			Array.Resize(ref _renderWorkItems, _renderWorkItems.Length * 2);
		}

		bool hasCached = unit3D.HasCachedTransform;
		_renderWorkItems[_renderWorkItemCount++] = new UnitRenderWorkItem
		{
			Entity = entity,
			Unit = unit3D,
			PosValue = posValue,
			HasCachedTransform = hasCached,
			CachedPosition = hasCached ? unit3D.CachedPosition : unit3D.GlobalPosition,
			CachedScale = hasCached ? unit3D.CachedScale : unit3D.Scale,
			CachedBaseScale = unit3D.CachedBaseScale,
			CachedRotationY = hasCached ? unit3D.CachedRotationY : unit3D.Rotation.Y,
			CachedBasis = hasCached ? unit3D.CachedBasis.Orthonormalized() : unit3D.Basis.Orthonormalized(),
			CachedUnitVelocity = unit3D.Velocity,
			CachedLocalAabb = unit3D.CachedLocalAabb,
			HasEvaluatedFrustum = unit3D.HasEvaluatedFrustum,
			IsCulled = unit3D.IsCulled,
			IsFlashingScale = unit3D.IsFlashingScale
		};
	}

	private void ProcessRenderWorkItemParallel(int index)
	{
		ref var item = ref _renderWorkItems[index];
		Entity entity = item.Entity;
		var world = World;
		float fDelta = _renderFDelta;
		float fraction = _renderFraction;

		Vector3 posValue = item.PosValue;
		Vector3 prevPos = world.Has<PreviousPosition>(entity)
			? world.Get<PreviousPosition>(entity).Value
			: posValue;

		Vector3 interpPosVec = Vector3.Lerp(prevPos, posValue, fraction);

		Godot.Vector3 nextPos = new Godot.Vector3(interpPosVec.X, interpPosVec.Y, interpPosVec.Z);
		if (world.Has<RenderPosition>(entity))
		{
			item.ShouldUpdateRenderPos = true;
			item.NewRenderPos = interpPosVec;
		}

		bool posChanged = !item.HasCachedTransform || (nextPos - item.CachedPosition).LengthSquared() > 0.0001f;

		float scaleVal = world.Has<CollisionScale>(entity) ? world.Get<CollisionScale>(entity).Value : 1.0f;
		float clampedScale = Mathf.Max(0.01f, scaleVal);
		bool scaleChanged = Mathf.Abs(item.CachedBaseScale - clampedScale) > 0.001f;

		bool isCulled = item.IsCulled;
		if (!_renderHasFrustum)
		{
			item.ShouldSetCulledState = isCulled;
			item.NewIsCulled = false;
			isCulled = false;
		}
		else if (!item.HasEvaluatedFrustum || _renderCameraFrustumChanged || posChanged || scaleChanged)
		{
			item.NewHasEvaluatedFrustum = true;
			Godot.Vector3 cullPos = item.HasCachedTransform ? item.CachedPosition : nextPos;
			Godot.Vector3 cullScale = item.HasCachedTransform ? item.CachedScale : new Godot.Vector3(clampedScale, clampedScale, clampedScale);
			Aabb localAabb = item.CachedLocalAabb;
			Aabb worldAabb = new Aabb(cullPos + localAabb.Position * cullScale, localAabb.Size * cullScale);
			bool isVisible = RuntimeTerrain.IntersectsFrustum(_renderFrustumPlanes, worldAabb);
			bool shouldCull = !isVisible;
			if (shouldCull != isCulled)
			{
				item.ShouldSetCulledState = true;
				item.NewIsCulled = shouldCull;
			}
			isCulled = shouldCull;
		}

		bool hasLookTarget = TryGetLookTarget(entity, out Godot.Vector3 lookTargetPos);
		bool hasMovement = world.Has<MoveTo>(entity) || world.Has<Follow>(entity) || world.Has<LatestDescription>(entity);
		bool hasVelocityComp = world.Has<Velocity>(entity);
		Vector3 ecsVel = hasVelocityComp ? world.Get<Velocity>(entity).Value : Vector3.Zero;
		bool hasVelocity = hasVelocityComp && ecsVel.LengthSquared() > 0.0001f;

		if (!posChanged && !hasMovement && !hasVelocity && !hasLookTarget && !item.Unit.HasHeldAnimation)
		{
			return;
		}

		if (isCulled)
		{
			if (posChanged || scaleChanged)
			{
				Godot.Vector3 scaleVec = new Godot.Vector3(clampedScale, clampedScale, clampedScale);
				Basis unscaledBasis = item.CachedBasis.Orthonormalized();
				Basis scaledBasis = unscaledBasis.Scaled(scaleVec);
				item.ShouldSetTransform = true;
				item.FinalTransform = new Transform3D(scaledBasis, nextPos);
				item.NextPos = nextPos;
				item.TargetRotY = item.CachedRotationY;
				item.TargetBasis = unscaledBasis;
				item.FinalScaleVec = scaleVec;
				item.ClampedScale = clampedScale;
			}

			return;
		}

		Godot.Vector3 velVec = new Godot.Vector3(ecsVel.X, ecsVel.Y, ecsVel.Z);
		if (!hasMovement)
		{
			if (hasVelocity)
			{
				item.ShouldZeroEcsVelocity = true;
			}
			velVec = Godot.Vector3.Zero;
		}

		Godot.Vector3 effectiveUnitVelocity = item.CachedUnitVelocity;
		if ((effectiveUnitVelocity - velVec).LengthSquared() > 0.0001f)
		{
			item.ShouldUpdateUnitVelocity = true;
			item.NewUnitVelocity = velVec;
			effectiveUnitVelocity = velVec;
		}

		Basis targetBasis = ComputeVisualRotationBasisParallel(ref item, entity, effectiveUnitVelocity, nextPos, lookTargetPos, hasLookTarget, fDelta, fraction, out float targetRotY);

		bool basisChanged = Mathf.Abs(Mathf.AngleDifference(item.CachedRotationY, targetRotY)) > 0.0005f || !targetBasis.IsEqualApprox(item.CachedBasis);
		if (posChanged || scaleChanged || basisChanged)
		{
			Godot.Vector3 finalScaleVec = new Godot.Vector3(clampedScale, clampedScale, clampedScale);
			Basis finalScaledBasis = targetBasis.Scaled(finalScaleVec);
			item.ShouldSetTransform = true;
			item.FinalTransform = new Transform3D(finalScaledBasis, nextPos);
			item.NextPos = nextPos;
			item.TargetRotY = targetRotY;
			item.TargetBasis = targetBasis;
			item.FinalScaleVec = finalScaleVec;
			item.ClampedScale = clampedScale;
		}
	}

}
