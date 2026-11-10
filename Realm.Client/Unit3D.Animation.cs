using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Animation;

namespace Realm.Client;
public partial class Unit3D
{
	private void UpdateDropShadow()
	{
		if (GameContext.Host == null || !GameContext.Host.IsPathingCapability(Entity, TerrainPathingFlags.Flying))
		{
			var oldDecal = GetNodeOrNull<Decal>("DropShadow");
			if (oldDecal != null)
			{
				oldDecal.QueueFree();
			}

			return;
		}

		var existing = GetNodeOrNull<Decal>("DropShadow");
		if (existing != null)
		{
			float updatedRadius = GetBaseObstacleRadius();
			if (updatedRadius > 0.001f)
			{
				float safeRadius = Mathf.Max(0.1f, updatedRadius * 2.5f);
				existing.Size = new Vector3(safeRadius, 3f, safeRadius);
			}

			return;
		}

		float radius = GetBaseObstacleRadius();
		if (radius <= 0.001f) radius = 1f;

		Decal shadowDecal = new Decal();
		shadowDecal.Name = "DropShadow";
		shadowDecal.TextureAlbedo = GameContext.Host.GetSharedShadowGradient();
		float decalSize = Mathf.Max(0.1f, radius * 2.5f);
		shadowDecal.Size = new Vector3(decalSize, 3f, decalSize);
		shadowDecal.Position = Vector3.Zero;
		shadowDecal.RotationDegrees = new Vector3(-90f, 0f, 0f);
		shadowDecal.CullMask = RuntimeTerrain.TerrainDecalCullMask;

		AddChild(shadowDecal);
	}

	private const double AnimationTransitionSeconds = 0.15;

	public bool AnimationsFrozen { get; private set; }

	public void SetAnimationsFrozen(bool frozen)
	{
		if (AnimationsFrozen == frozen) return;
		AnimationsFrozen = frozen;

		_lastRequestedAnimName = string.Empty;
		_currentAnimation = string.Empty;
		CancelDeferredAnimation();

		if (_animationPlayer == null || !GodotObject.IsInstanceValid(_animationPlayer)) return;

		if (frozen)
		{
			_animationPlayer.Pause();
			GameContext.Host?.SuspendProceduralAnimation(this);
		}
		else
		{
			_animationPlayer.Play();
			if (HasProceduralAnimation)
			{
				GameContext.Host?.ReapplyProceduralAnimation(this);
			}
		}
	}

	/// <summary>
	///     Hands the unit the animation its state calls for. A unit enters every animation a little later or earlier than its
	///     neighbours, by its own share of the transition window, so a group given the same order never moves in lockstep.
	///     The animation is never entered partway through: whatever is playing carries on for that moment and the new one
	///     then cross-fades in from its own first frame, which is what keeps a switch from moving to attacking a blend into a
	///     swing instead of a jump to a frame in the middle of one.
	/// </summary>
	public void PlayAnimation(string animName)
	{
		if (AnimationsFrozen) return;
		if (_animationPlayer == null || !GodotObject.IsInstanceValid(_animationPlayer)) return;
		if (string.IsNullOrEmpty(animName)) return;

		if (string.Equals(_deferredAnimationName, animName, StringComparison.Ordinal)) return;
		if (string.Equals(_lastRequestedAnimName, animName, StringComparison.Ordinal) && _animationPlayer.IsPlaying())
		{
			return;
		}

		_lastRequestedAnimName = animName;

		float entryDelay = (float)AnimationTransitionSeconds * AnimationEntryDelayShare;
		if (entryDelay <= 0.0001f)
		{
			ApplyAnimation(animName);
			return;
		}

		_deferredAnimationName = animName;
		_deferredAnimationDelayRemaining = entryDelay;
	}

	private void ApplyAnimation(string animName)
	{
		_deferredAnimationName = null;
		_deferredAnimationDelayRemaining = 0f;

		StringName? resolved = ResolveAnimationName(animName);
		if (resolved == null) return;

		AnimationPlayer? animationPlayer = _animationPlayer;
		if (animationPlayer == null) return;

		string resolvedStr = resolved.ToString();
		UpdateHandAttachmentsForAnimation(animName, resolvedStr);

		if (_currentAnimation == resolvedStr && animationPlayer.IsPlaying()) return;

		_currentAnimation = resolvedStr;

		var animResource = animationPlayer.GetAnimation(resolved);
		if (animResource != null)
		{
			animResource.LoopMode = ResolveAnimationType(animName) == RealmAnimationType.Death
				? Godot.Animation.LoopModeEnum.None
				: Godot.Animation.LoopModeEnum.Linear;
		}

		if (_isCulled)
		{
			_culledGameTime = GameContext.Host?.GameElapsedTime ?? 0f;
		}

		_animStepAccumulator = 0f;
		if (!IsPreview)
		{
			animationPlayer.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		}
		animationPlayer.ProcessMode = _isCulled ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
		animationPlayer.Play(resolved, AnimationTransitionSeconds);
		if (!_isCulled)
		{
			animationPlayer.Advance(0.0);
		}
	}

	/// <summary>
	///     Runs the moment a unit waits before it enters its next animation. Nothing is entered until that moment passes, so
	///     the unit is left in whichever animation it was already in rather than being held in an unplayed state.
	/// </summary>
	private void AdvanceDeferredAnimation(float fDelta)
	{
		if (_deferredAnimationName == null) return;

		_deferredAnimationDelayRemaining -= fDelta;
		if (_deferredAnimationDelayRemaining > 0f) return;

		ApplyAnimation(_deferredAnimationName);
	}

	private void CancelDeferredAnimation()
	{
		_deferredAnimationName = null;
		_deferredAnimationDelayRemaining = 0f;

		// The request is dropped with the delay that carried it, so the next one is asked for again rather than being
		// mistaken for an animation this unit is already in.
		_lastRequestedAnimName = null;
	}

	private RealmAnimationType ResolveAnimationType(string animationName)
	{
		ParseAnimationName(animationName, out string actionType, out _);
		return AnimationRetargetingService.TryParseAnimationType(actionType, out RealmAnimationType animationType) ? animationType : RealmAnimationType.Unknown;
	}

	/// <summary>
	///     This unit's share of the transition window: where it falls in the spread of entries a group given one order makes,
	///     fixed for its lifetime and derived from the node's identity, so no per-unit state or allocation keeps it stable.
	/// </summary>
	private float AnimationEntryDelayShare
	{
		get
		{
			ulong instanceId = GetInstanceId();
			uint hash = (uint)(instanceId ^ (instanceId >> 32));
			hash *= 2654435761u;
			hash ^= hash >> 13;
			return (hash & 0x3FFu) / 1024f;
		}
	}

	public void UpdateFrustumCulling(bool hasFrustum, ReadOnlySpan<Plane> frustumPlanes, float currentGameTime)
	{
		EvaluateFrustumCulling(hasFrustum, frustumPlanes, currentGameTime, true, true);
	}

	private void SeekToIdleFirstFrame()
	{
		if (_modelNode == null || !GodotObject.IsInstanceValid(_modelNode)) return;
		if (_animationPlayer == null || !GodotObject.IsInstanceValid(_animationPlayer))
		{
			_animationPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_modelNode);
		}

		StringName? idleAnim = ResolveAnimationName("Idle");
		if (idleAnim != null && _animationPlayer != null && _animationPlayer.HasAnimation(idleAnim))
		{
			UpdateHandAttachmentsForAnimation("Idle", idleAnim.ToString());
			_animationPlayer.ProcessMode = ProcessModeEnum.Inherit;
			_animationPlayer.Play(idleAnim);
			_animationPlayer.Seek(0.0, update: true);
			_animationPlayer.Pause();
		}
		else
		{
			AnimationRetargetingService.TryApplyRiggedIdlePose(_modelNode, UnitId);
		}
	}

	public static Skeleton3D? FindSkeleton(Node? root)
	{
		if (root == null || !GodotObject.IsInstanceValid(root)) return null;
		if (root is Skeleton3D skeleton) return skeleton;
		int childCount = root.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			var found = FindSkeleton(root.GetChild(i));
			if (found != null) return found;
		}

		return null;
	}

}
