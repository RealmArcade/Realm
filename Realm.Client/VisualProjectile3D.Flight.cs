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

	private bool UpdateFlightTrajectory(ProjectileTrajectoryType trajType, Vector3 currentTarget, float currentSpeed, float dt, float rawT)
	{
		System.Numerics.Vector3 position = ToEcsVector(_currentFlightPosition);
		System.Numerics.Vector3 direction = ToEcsVector(_currentFlightDirection);

		bool finished = trajType switch
		{
			ProjectileTrajectoryType.Linear => ProjectileTrajectorySolver.StepLinear(direction, currentSpeed, dt, rawT, ref position),
			ProjectileTrajectoryType.Boomerang => ProjectileTrajectorySolver.StepBoomerang(
				_elapsedTime, _totalFlightDuration, _weapon.BoomerangReturnDelay, _easeCurve,
				ToEcsVector(_startPosition), ToEcsVector(currentTarget), ref position),
			_ => ProjectileTrajectorySolver.StepHomingOrParabolic(
				ToEcsVector(_startPosition), ToEcsVector(_initialTargetPosition), ToEcsVector(currentTarget),
				currentSpeed, dt, rawT, _weapon.TurnRateLimit, _weapon.HomingWeight, _easeCurve,
				ref position, ref direction),
		};

		_currentFlightPosition = ToGodotVector(position);
		_currentFlightDirection = ToGodotVector(direction);
		return finished;
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
