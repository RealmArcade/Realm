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
	private string DetermineUnitAnimation(Entity entity)
	{
		if (World.Has<AnimationsFrozen>(entity)) return "Idle";
		if (TryGetReplayAnimation(entity, out string replayAnim)) return replayAnim;
		if (World.Has<Dead>(entity)) return "Death";
		if (IsEntityMoving(entity)) return "Walk";
		if (World.Has<AttackTarget>(entity)) return "Attack";
		if (World.Has<HealingTarget>(entity)) return "Spell_Cast";
		if (IsEntityGathering(entity)) return "Labor";
		if (World.Has<BuildTask>(entity)) return "Labor";

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
