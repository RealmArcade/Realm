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
	/// <summary>
	///     The animation a unit's own orders call for. Death is deliberately absent: a description states that a unit is
	///     dead when it arrives, while the node is retired by the tick that draws that description, so taking the death
	///     pose from here began the fall a presentation delay early and then played it again when the clock reached it.
	/// </summary>
	/// <param name="entity">The unit to name an animation for.</param>
	/// <returns>The animation the unit's orders describe, as the name this unit's model declares it by.</returns>
	private string DetermineUnitAnimation(Entity entity)
	{
		if (World.Has<AnimationsFrozen>(entity)) return "Idle";
		if (TryGetReplayAnimation(entity, out string replayAnim)) return replayAnim;
		if (IsEntityMoving(entity)) return "Walk";
		if (World.Has<AttackTarget>(entity)) return "Attack";
		if (World.Has<HealingTarget>(entity)) return "Spell_Cast";
		if (IsEntityGathering(entity)) return "Labor";
		if (World.Has<BuildTask>(entity)) return "Labor";
		if (World.Has<RepairTask>(entity)) return "Labor";

		return "Idle";
	}

	private bool TryGetReplayAnimation(Entity entity, out string animation)
	{
		animation = string.Empty;
		if (!GameContext.ReplayPlayback.IsPlayingReplay) return false;

		animation = World.Has<ReplayAnimationState>(entity)
			? World.Get<ReplayAnimationState>(entity).Animation
			: "Idle";
		return true;
	}

	private bool IsEntityMoving(Entity entity)
	{
		return World.Has<MoveTo>(entity) && World.Has<Velocity>(entity) &&
		       World.Get<Velocity>(entity).Value.LengthSquared() > 0.01f;
	}

	private bool IsEntityGathering(Entity entity)
	{
		return World.Has<Gatherer>(entity) && !World.Get<Gatherer>(entity).ReturningToBase;
	}

}
