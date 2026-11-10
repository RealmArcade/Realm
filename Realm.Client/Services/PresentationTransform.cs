using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Stats;
using Realm.Shared;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Services;

/// <summary>
///     Where to draw a unit this frame: the interpolation between its previous and current position and
///     facing, and the presentation pass that samples the snapshot timeline for a replicated unit and writes
///     the result back as the unit's position.
/// </summary>
public sealed class PresentationTransform
{
	private const float StationaryVelocitySquared = 0.01f;

	public bool TryGetInterpolatedTransform(Entity entity, float fraction, out Vector3 position, out float rotationY)
	{
		position = Vector3.Zero;
		rotationY = 0f;

		if (GameContext.World == null || !SimulationService.World.IsAlive(entity)) return false;

		if (SimulationService.World.Has<Position>(entity))
		{
			Vector3 curPos = SimulationService.World.Get<Position>(entity).Value;
			Vector3 prevPos = SimulationService.World.Has<PreviousPosition>(entity) ? SimulationService.World.Get<PreviousPosition>(entity).Value : curPos;
			position = Vector3.Lerp(prevPos, curPos, fraction);
		}

		if (SimulationService.World.Has<RotationY>(entity))
		{
			float curRot = SimulationService.World.Get<RotationY>(entity).Value;
			float prevRot = SimulationService.World.Has<PreviousRotationY>(entity) ? SimulationService.World.Get<PreviousRotationY>(entity).Value : curRot;
			rotationY = Mathf.LerpAngle(prevRot, curRot, fraction);
		}

		return true;
	}

	public (Vector3 Position, float RotationY) GetInterpolatedTransform(Entity entity, float fraction)
	{
		TryGetInterpolatedTransform(entity, fraction, out var pos, out var rotY);
		return (pos, rotY);
	}

	/// <summary>
	///     The one thing that writes <see cref="Position" /> for a replicated unit, from the description sampled where the
	///     match clock is being rendered rather than where the authority has got to. A unit is drawn between the two
	///     descriptions either side of the cursor, so it neither trails a smoothed target nor has to be caught up.
	/// </summary>
	public void PresentationQueryAction(Entity entity, ref LatestDescription target)
	{
		if (!SimulationService.World.Has<Position>(entity)) return;

		if (MultiplayerSyncDiagnostics.IsRecording) MultiplayerSyncDiagnostics.RecordPresentationStep(entity.Id, GameContext.NetworkService?.LastAppliedSnapshotSequenceForDiagnostics ?? -1);

		// Only a unit this screen ordered to move is drawn ahead of its newest description, and the step is bounded to one
		// interval and 0.75 m along the direction it was already moving. Everything else is drawn strictly between
		// descriptions this screen holds. Drawing another player's unit ahead of its description would invent motion this
		// screen was never told about, so the step masks one interval of the delay for the local player's own orders only.
		// What this frame reads is the order this screen believes it holds, which a unit it has just ordered holds from the
		// moment the order went out; the answer the match gave to that order is stamped where the description carrying it is
		// applied, so the lead a completed order leaves behind is released when the completion is drawn rather than when
		// some later frame happened to notice it.
		int localPlayerIndex = GameContext.Host?.LocalPlayerIndex ?? -1;
		bool localOrderBelievedHeld = SimulationService.World.Has<MoveTo>(entity)
		                              && SimulationService.World.Has<UnitOwnerPlayer>(entity)
		                              && SimulationService.World.Get<UnitOwnerPlayer>(entity).PlayerIndex == localPlayerIndex;
		if (!GameContext.SnapshotTimeline.TrySample(entity.Id, localOrderBelievedHeld, out Vector3 sampledPosition, out float sampledRotationY, out Vector3 sampledVelocity)) return;

		Vector3 currentPos = SimulationService.World.Get<Position>(entity).Value;

		if (MultiplayerSyncDiagnostics.IsRecording && (sampledPosition - currentPos).LengthSquared() > 4f)
		{
			MultiplayerSyncDiagnostics.RecordPresentationStepDetail(entity.Id, currentPos, sampledPosition, 1f, sampledPosition);
		}

		PositionWriteTrace.MarkPresentation(entity, 1f, currentPos, sampledPosition, sampledPosition, target.Position);
		SimulationService.World.Set(entity, new Position(sampledPosition));
		SimulationService.World.SetOrAdd(entity, new Velocity(sampledVelocity));

		// A unit standing still is drawn facing where the authority had it facing at the moment being rendered. The
		// renderer smooths towards this value, so a unit that turns in place turns when it is drawn rather than one
		// presentation delay before it. A moving unit is turned along its own movement by the renderer instead.
		if (sampledVelocity.LengthSquared() <= StationaryVelocitySquared) target.RotationY = sampledRotationY;
	}
}
