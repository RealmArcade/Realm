using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Realm.Shared.Textures;
using FileAccess = Godot.FileAccess;

namespace Realm.Client;

public partial class VisualProjectile3D : Node3D
{
	public void StepSimulation(float deltaSeconds)
	{
		if (_isImpacted && deltaSeconds < 0.0f)
		{
			_isImpacted = false;
			_isFlying = true;
			_meshContainer.Visible = true;
			_pointLight.Visible = _hasAcquiredLight;
		}

		float sign = Mathf.Sign(deltaSeconds);
		float remaining = Mathf.Abs(deltaSeconds);
		float maxSubStep = 1.0f / 60.0f;

		while (remaining > 0.0001f)
		{
			float subDt = Mathf.Min(maxSubStep, remaining) * sign;
			AdvanceSimulation(subDt);
			remaining -= Mathf.Abs(subDt);
		}
	}

	public override void _Process(double delta)
	{
		if (IsPaused) return;
		float dt = (float)delta * TimeScale;
		AdvanceSimulation(dt);
	}

	private void HandleImpactState(float dt)
	{
		_fadeTimer += dt;
		if (_fadeTimer >= _fadeDuration)
		{
			_isImpacted = false;
			Visible = false;
			OnRecycleRequested?.Invoke(this);
		}
	}

	private bool CheckFlightFailsafes()
	{
		if (!_honourDestination)
		{
			if (_weapon.MaxLifetime > 0.0f && _elapsedTime >= _weapon.MaxLifetime) return true;
			if (_weapon.FailsafeRange > 0.0f && _cachedGlobalPosition.DistanceTo(_startPosition) >= _weapon.FailsafeRange) return true;
		}

		if (_elapsedTime >= _totalFlightDuration * 3.0f + 5.0f) return true;
		return false;
	}

	private Vector3 GetCurrentTargetPosition()
	{
		Vector3 currentTarget = _initialTargetPosition;
		EcsWorld? world = GameContext.Host != null ? GameContext.World : null;
		if (_targetEntity != default && world != null && world.IsAlive(_targetEntity))
		{
			if (world.Has<Position>(_targetEntity))
			{
				var posComp = world.Get<Position>(_targetEntity);
				currentTarget = new Vector3(posComp.Value.X, posComp.Value.Y + 1.2f, posComp.Value.Z);
			}
		}

		return currentTarget;
	}

	private bool ProcessLinearTrajectory(float currentSpeed, float dt, float rawT)
	{
		_currentFlightPosition += _currentFlightDirection * (currentSpeed * dt);
		return rawT >= 1.0f;
	}

	private bool ProcessBoomerangTrajectory(Vector3 currentTarget)
	{
		float delay = _weapon.BoomerangReturnDelay >= 0.0f ? _weapon.BoomerangReturnDelay : 0.2f;
		float outboundDuration = _totalFlightDuration;
		float returnDuration = _totalFlightDuration;

		if (_elapsedTime <= outboundDuration)
		{
			float bOutT = Mathf.Clamp(_elapsedTime / outboundDuration, 0.0f, 1.0f);
			_currentFlightPosition = _startPosition.Lerp(currentTarget, ProjectileFlightMath.Ease(bOutT, _easeCurve));
			return false;
		}

		if (_elapsedTime <= outboundDuration + delay)
		{
			_currentFlightPosition = currentTarget;
			return false;
		}

		if (_elapsedTime <= outboundDuration + delay + returnDuration)
		{
			float bRetT = Mathf.Clamp((_elapsedTime - outboundDuration - delay) / returnDuration, 0.0f, 1.0f);
			_currentFlightPosition = currentTarget.Lerp(_startPosition, ProjectileFlightMath.Ease(bRetT, _easeCurve));
			return false;
		}

		return true;
	}

	private bool ProcessHomingOrParabolicTrajectory(Vector3 currentTarget, float currentSpeed, float dt, float rawT)
	{
		float easedT = ProjectileFlightMath.Ease(rawT, _easeCurve);

		if (_weapon.TurnRateLimit > 0.0f && _weapon.HomingWeight > 0.0f)
		{
			Vector3 toCurrentTarget = currentTarget - _currentFlightPosition;
			float distToTarget = toCurrentTarget.Length();
			if (distToTarget > 0.001f)
			{
				Vector3 desiredDir = toCurrentTarget / distToTarget;
				float maxTurnRadians = Mathf.DegToRad(_weapon.TurnRateLimit) * dt * _weapon.HomingWeight;
				float angleBetween = _currentFlightDirection.AngleTo(desiredDir);
				if (angleBetween > 0.0001f)
				{
					float step = Mathf.Min(1.0f, maxTurnRadians / angleBetween);
					_currentFlightDirection = _currentFlightDirection.Slerp(desiredDir, step).Normalized();
				}
			}

			_currentFlightPosition += _currentFlightDirection * (currentSpeed * dt);
			return distToTarget <= Mathf.Max(0.5f, currentSpeed * dt * 1.5f) || rawT >= 1.0f;
		}

		Vector3 effectiveTarget = _initialTargetPosition.Lerp(currentTarget, Mathf.Clamp(_weapon.HomingWeight * easedT, 0.0f, 1.0f));
		_currentFlightPosition = _startPosition.Lerp(effectiveTarget, easedT);
		return rawT >= 1.0f;
	}

	private bool UpdateFlightTrajectory(ProjectileTrajectoryType trajType, Vector3 currentTarget, float currentSpeed, float dt, float rawT)
	{
		return trajType switch
		{
			ProjectileTrajectoryType.Linear => ProcessLinearTrajectory(currentSpeed, dt, rawT),
			ProjectileTrajectoryType.Boomerang => ProcessBoomerangTrajectory(currentTarget),
			_ => ProcessHomingOrParabolicTrajectory(currentTarget, currentSpeed, dt, rawT),
		};
	}

	private void UpdateVisualTransforms(Vector3 nextPos, float dt, float rawT)
	{
		if (_weapon.OrientToTrajectory)
		{
			Vector3 velocityDelta = nextPos - _cachedGlobalPosition;
			if (velocityDelta.LengthSquared() > 0.0001f)
			{
				Vector3 dir = velocityDelta.Normalized();
				Vector3 upVector = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
				_cachedBasis = Basis.LookingAt(dir, upVector);
				_cachedGlobalPosition = nextPos;
				GlobalTransform = new Transform3D(_cachedBasis, _cachedGlobalPosition);
			}
			else
			{
				_cachedGlobalPosition = nextPos;
				GlobalPosition = nextPos;
			}
		}
		else
		{
			_cachedGlobalPosition = nextPos;
			GlobalPosition = nextPos;
		}

		if (_tumbleSpeed > 0.0001f)
		{
			_meshContainer.RotateObjectLocal(_tumbleAxis, _tumbleSpeed * dt);
		}

		if (_scaleCurve != ProjectileScaleCurve.Constant)
		{
			Vector3 meshScale = _weapon.MeshScaleOffset.ToGodotVector3();
			Vector3 baseScale = (meshScale == Vector3.Zero) ? Vector3.One : meshScale;
			baseScale = SafeScale(baseScale);
			float lifetimeScale = Mathf.Max(0.001f, ProjectileFlightMath.ScaleOverLifetime(rawT, _scaleCurve));
			_visualTransformContainer.Scale = SafeScale(baseScale * lifetimeScale);
		}

		Vector3 trailPos = _cachedGlobalPosition + _cachedBasis * _weapon.TrailOffset.ToGodotVector3();
		UpdateTrail(dt, trailPos);
	}

}
