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
	public bool EvaluateFrustumCulling(bool hasFrustum, ReadOnlySpan<Plane> frustumPlanes, float currentGameTime, bool cameraFrustumChanged, bool transformChanged)
	{
		if (!hasFrustum)
		{
			SetCulledState(false, currentGameTime);
			return false;
		}

		if (!_hasEvaluatedFrustum || cameraFrustumChanged || transformChanged)
		{
			_hasEvaluatedFrustum = true;
			Vector3 pos = _hasCachedTransform ? _cachedPosition : GlobalPosition;
			Vector3 scale = _hasCachedTransform ? _cachedScale : Scale;
			Aabb worldAabb = new Aabb(pos + _cachedLocalAabb.Position * scale, _cachedLocalAabb.Size * scale);
			bool isVisible = RuntimeTerrain.IntersectsFrustum(frustumPlanes, worldAabb);
			SetCulledState(!isVisible, currentGameTime);
		}

		return _isCulled;
	}

	public void StepManualAnimation(float fDelta)
	{
		if (_isCulled || !Visible || _animationPlayer == null || !GodotObject.IsInstanceValid(_animationPlayer))
			return;

		AdvanceHeldAnimation(fDelta);
		AdvanceDeferredAnimation(fDelta);

		if (!_animationPlayer.IsPlaying())
			return;

		_animStepAccumulator += fDelta;
		if (_animStepAccumulator >= GameplayConstants.UnitAnimationStepIntervalSeconds)
		{
			_animationPlayer.Advance(_animStepAccumulator);
			_animStepAccumulator = 0f;
		}
	}

	public void SetCulledState(bool shouldCull, float currentGameTime)
	{
		if (_animationPlayer == null || !GodotObject.IsInstanceValid(_animationPlayer))
		{
			_isCulled = shouldCull;
			return;
		}

		if (shouldCull && !_isCulled)
		{
			_isCulled = true;
			_culledGameTime = currentGameTime;
			_animStepAccumulator = 0f;
			CancelDeferredAnimation();
			_animationPlayer.ProcessMode = ProcessModeEnum.Disabled;
			if (_cachedSkeleton != null && GodotObject.IsInstanceValid(_cachedSkeleton))
			{
				_cachedSkeleton.ProcessMode = ProcessModeEnum.Disabled;
			}
		}
		else if (!shouldCull && _isCulled)
		{
			_isCulled = false;
			_animStepAccumulator = 0f;
			float elapsed = currentGameTime - _culledGameTime;
			_animationPlayer.ProcessMode = ProcessModeEnum.Inherit;
			if (_cachedSkeleton != null && GodotObject.IsInstanceValid(_cachedSkeleton))
			{
				_cachedSkeleton.ProcessMode = ProcessModeEnum.Inherit;
			}

			if (elapsed > 0f && _animationPlayer.IsPlaying())
			{
				float animLength = (float)_animationPlayer.CurrentAnimationLength;
				if (animLength > 0.001f)
				{
					float currentPos = (float)_animationPlayer.CurrentAnimationPosition;
					bool isLooping = true;
					var animName = _animationPlayer.CurrentAnimation;
					if (!string.IsNullOrEmpty(animName) && _animationPlayer.HasAnimation(animName))
					{
						var anim = _animationPlayer.GetAnimation(animName);
						if (anim != null)
						{
							isLooping = anim.LoopMode != Godot.Animation.LoopModeEnum.None;
						}
					}

					if (isLooping)
					{
						float newPos = (currentPos + elapsed) % animLength;
						_animationPlayer.Seek(newPos, update: true);
					}
					else
					{
						float newPos = currentPos + elapsed;
						if (newPos >= animLength)
						{
							_animationPlayer.Seek(animLength, update: true);
						}
						else
						{
							_animationPlayer.Seek(newPos, update: true);
						}
					}
				}
			}
		}
	}

}
