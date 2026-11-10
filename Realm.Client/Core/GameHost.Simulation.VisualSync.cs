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
	private static readonly QueryDescription _constructionVisualQuery = new QueryDescription().WithAll<ConstructionState>().WithNone<Dead>();

	private void UpdateVisualNodesFromEcs(float fDelta)
	{
		foreach (var entity in World.Query(in _constructionVisualQuery))
		{
			if (TryGetUnit3D(entity, out var buildingNode) && GodotObject.IsInstanceValid(buildingNode))
			{
				ref var construction = ref World.Get<ConstructionState>(entity);
				float alpha = Mathf.Lerp(0.3f, 1.0f, construction.Progress / Mathf.Max(construction.TotalBuildTime, 0.001f));
				buildingNode.Modulate = new Color(1f, 1f, 1f, alpha);
			}
		}
	}

	private bool CheckNonFinitePositionWarning(Entity entity, Vector3 posValue, Unit3D unit3D)
	{
		if (float.IsFinite(posValue.X) && float.IsFinite(posValue.Y) && float.IsFinite(posValue.Z))
			return false;

		if (GameContext.SimulationService.WarnedNonFinitePositions.Add(entity))
		{
			// Past the limit, warnings for entities that no longer exist are swept so a recycled entity id gets a fresh
			// warning later rather than being remembered as already reported.
			if (GameContext.SimulationService.WarnedNonFinitePositions.Count > SimulationService.WarnedNonFinitePositionsLimit)
			{
				GameContext.SimulationService.WarnedNonFinitePositions.RemoveWhere(warned => !World.IsAlive(warned));
			}

			GD.PushWarning($"[Simulation] Unit '{unit3D.Name}' (entity {entity.Id}) has a non-finite ECS position ({posValue.X}, {posValue.Y}, {posValue.Z}); skipping visual sync.");
		}

		return true;
	}

	private Basis ComputeVisualRotationBasisParallel(ref UnitRenderWorkItem item, Entity entity, Godot.Vector3 unitVelocity, Godot.Vector3 nextPos, Godot.Vector3 lookTargetPos, bool hasLookTarget, float fDelta, float fraction, out float targetRotY)
	{
		var world = World;
		Godot.Vector3 dir = unitVelocity;
		bool hasDir = dir.LengthSquared() > 0.01f;

		bool forceLookTarget = false;
		if (hasLookTarget)
		{
			if (!hasDir) forceLookTarget = true;
			else
			{
				float distToLook = lookTargetPos.DistanceTo(nextPos);
				if (distToLook < SimulationService.LookTargetProximityDistance || world.Has<Follow>(entity)) forceLookTarget = true;
			}
		}

		if (forceLookTarget)
		{
			dir = (lookTargetPos - nextPos);
			dir.Y = 0f;
			dir = dir.Normalized();
			hasDir = dir.LengthSquared() > 0.01f;
		}

		float currRot = world.Has<RotationY>(entity) ? world.Get<RotationY>(entity).Value : item.CachedRotationY;
		float prevRot = world.Has<PreviousRotationY>(entity) ? world.Get<PreviousRotationY>(entity).Value : currRot;
		float interpRotY = Mathf.LerpAngle(prevRot, currRot, fraction);

		if (hasDir)
		{
			return ComputeDirectionalRotationBasisParallel(ref item, entity, nextPos, dir, fDelta, out targetRotY);
		}
		else if (world.Has<LatestDescription>(entity))
		{
			var interp = world.Get<LatestDescription>(entity);
			targetRotY = Mathf.LerpAngle(item.CachedRotationY, interp.RotationY, 10f * fDelta);

			if (world.Has<RotationY>(entity))
			{
				float ecsRot = world.Get<RotationY>(entity).Value;
				if (Mathf.Abs(ecsRot - targetRotY) > 0.001f)
				{
					item.ShouldUpdateEcsRotationY = true;
					item.NewEcsRotationY = targetRotY;
				}
			}

			return Basis.FromEuler(new Godot.Vector3(0f, targetRotY, 0f));
		}
		else
		{
			targetRotY = interpRotY;
			return Basis.FromEuler(new Godot.Vector3(0f, targetRotY, 0f));
		}
	}

	private bool TryGetLookTarget(Entity entity, out Godot.Vector3 lookTargetPos)
	{
		lookTargetPos = Godot.Vector3.Zero;

		if (World.Has<AttackTarget>(entity))
			return TryGetTargetPosition(World.Get<AttackTarget>(entity).Target, out lookTargetPos);

		if (World.Has<HealingTarget>(entity))
			return TryGetTargetPosition(World.Get<HealingTarget>(entity).Target, out lookTargetPos);

		if (World.Has<Follow>(entity))
			return TryGetTargetPosition(World.Get<Follow>(entity).Target, out lookTargetPos);

		if (World.Has<BuildTask>(entity))
			return TryGetTargetPosition(World.Get<BuildTask>(entity).BuildingEntity, out lookTargetPos);

		return false;
	}

	private bool TryGetTargetPosition(Entity targetEnt, out Godot.Vector3 position)
	{
		position = Godot.Vector3.Zero;
		if (World.IsAlive(targetEnt) && World.Has<Position>(targetEnt))
		{
			var tPosComp = World.Get<Position>(targetEnt);
			position = new Godot.Vector3(tPosComp.Value.X, tPosComp.Value.Y, tPosComp.Value.Z);
			return true;
		}

		return false;
	}

	private Basis ComputeDirectionalRotationBasisParallel(ref UnitRenderWorkItem item, Entity entity, Godot.Vector3 nextPos, Godot.Vector3 dir, float fDelta, out float targetRotY)
	{
		var world = World;
		dir = dir.Normalized();
		float angle = Mathf.Atan2(-dir.X, -dir.Z) + Mathf.Pi;

		bool isFlying = world.Has<Realm.Ecs.Components.Movement.PathingFlags>(entity)
		                && ((TerrainPathingFlags)world.Get<Realm.Ecs.Components.Movement.PathingFlags>(entity).Value & TerrainPathingFlags.Flying) != 0;
		float turnRate = 10f;
		if (world.Has<MovementStats>(entity))
		{
			var moveStats = world.Get<MovementStats>(entity);
			if (moveStats.TurnRate > 0f) turnRate = moveStats.TurnRate;
		}

		targetRotY = Mathf.LerpAngle(item.CachedRotationY, angle, turnRate * fDelta);

		if (world.Has<RotationY>(entity))
		{
			float ecsRot = world.Get<RotationY>(entity).Value;
			if (Mathf.Abs(ecsRot - targetRotY) > 0.001f)
			{
				item.ShouldUpdateEcsRotationY = true;
				item.NewEcsRotationY = targetRotY;
			}
		}

		Basis currentBasis = Basis.FromEuler(new Godot.Vector3(0f, targetRotY, 0f));

		Godot.Vector3 normal = Godot.Vector3.Up;
		TerrainCell[,]? groundCells = _renderGroundCells;
		if (!isFlying && groundCells != null)
		{
			RuntimeTerrain.SampleHeightAndNormal(groundCells, _renderGroundQuadSize, nextPos.X, nextPos.Z, out _, out normal);
		}

		if (Mathf.Abs(normal.Y - 1f) > 0.02f)
		{
			Godot.Vector3 forwardDir = new Godot.Vector3(-Mathf.Sin(targetRotY), 0f, -Mathf.Cos(targetRotY));
			Godot.Vector3 up = normal.Normalized();
			Godot.Vector3 right = forwardDir.Cross(up);
			if (right.LengthSquared() > 0.00001f)
			{
				right = right.Normalized();
				Godot.Vector3 forwardPerp = right.Cross(up).Normalized();
				Basis targetTerrainBasis = new Basis(right, up, forwardPerp);
				float det = targetTerrainBasis.Determinant();
				if (!float.IsNaN(det) && Mathf.Abs(det) > 0.001f)
				{
					var qTarget = targetTerrainBasis.GetRotationQuaternion();
					var qCurrent = item.CachedBasis.GetRotationQuaternion();
					var qLerp = qCurrent.Slerp(qTarget, 10f * fDelta);
					return new Basis(qLerp);
				}
			}
		}

		return currentBasis;
	}

}
