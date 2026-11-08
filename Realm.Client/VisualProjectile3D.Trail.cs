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
	public void AdvanceSimulation(float dt)
	{
		if (_isImpacted)
		{
			HandleImpactState(dt);
			return;
		}

		if (!_isFlying) return;

		if (CheckFlightFailsafes())
		{
			HandleImpact(_cachedGlobalPosition);
			return;
		}

		Vector3 currentTarget = GetCurrentTargetPosition();
		float currentSpeed = CalculateSpeed(_speed, _elapsedTime, _totalFlightDuration, _speedCurve, _weapon.Acceleration);
		_elapsedTime += dt;
		float rawT = Mathf.Clamp(_elapsedTime / _totalFlightDuration, 0.0f, 1.0f);

		bool impactOccurred = UpdateFlightTrajectory(_trajectoryType, currentTarget, currentSpeed, dt, rawT);
		if (impactOccurred)
		{
			HandleImpact(_currentFlightPosition);
			return;
		}

		float arcY = CalculateArcY(rawT);
		Vector3 forward = _currentFlightDirection.LengthSquared() > 0.001f ? _currentFlightDirection.Normalized() : Vector3.Forward;
		Vector3 right = Mathf.Abs(forward.Dot(Vector3.Up)) > 0.99f ? forward.Cross(Vector3.Right).Normalized() : forward.Cross(Vector3.Up).Normalized();
		Vector3 up = right.Cross(forward).Normalized();

		Vector3 spiralOffset = CalculateSpiralOffset(right, up);
		Vector3 zigzagOffset = CalculateZigzagOffset(right);
		Vector3 orbitOffset = CalculateOrbitOffset(_trajectoryType, right, up);

		Vector3 nextPos = _currentFlightPosition + new Vector3(0, arcY, 0) + spiralOffset + zigzagOffset + orbitOffset;
		UpdateVisualTransforms(nextPos, dt, rawT);
	}

	private void UpdateTrail(float dt, Vector3 currentPos)
	{
		float lifetime = _weapon.RibbonLifetime > 0 ? _weapon.RibbonLifetime : 0.5f;

		int validCount = 0;
		for (int i = 0; i < _trailPointCount; i++)
		{
			_trailPoints[i].Age += dt;
			if (_trailPoints[i].Age <= lifetime)
			{
				_trailPoints[validCount] = _trailPoints[i];
				validCount++;
			}
		}

		_trailPointCount = validCount;

		if (_isFlying)
		{
			bool addNew = true;
			if (_trailPointCount > 0)
			{
				float distSq = _trailPoints[0].Position.DistanceSquaredTo(currentPos);
				if (distSq < 0.0001f)
				{
					_trailPoints[0].Position = currentPos;
					_trailPoints[0].Age = 0.0f;
					addNew = false;
				}
			}

			if (addNew)
			{
				int moveCount = Math.Min(_trailPointCount, MaxTrailPoints - 1);
				if (moveCount > 0)
				{
					Array.Copy(_trailPoints, 0, _trailPoints, 1, moveCount);
				}

				_trailPoints[0] = new TrailPoint
				{
					Position = currentPos,
					Age = 0.0f
				};
				_trailPointCount = Math.Min(_trailPointCount + 1, MaxTrailPoints);
			}
		}
	}

	public Vector3 CalculateRibbonForwardIndex(int index)
	{
		Vector3 forward;
		if (index == 0)
		{
			forward = (_trailPoints[0].Position - _trailPoints[1].Position).Normalized();
		}
		else if (index == _trailPointCount - 1)
		{
			forward = (_trailPoints[index - 1].Position - _trailPoints[index].Position).Normalized();
		}
		else
		{
			forward = (_trailPoints[index - 1].Position - _trailPoints[index + 1].Position).Normalized();
		}

		return forward.LengthSquared() < 0.001f ? Vector3.Forward : forward;
	}

	public Vector3 CalculateRibbonSideDirection(Vector3 forward, Vector3 p, Vector3 camPos)
	{
		Vector3 viewDir = (p - camPos).Normalized();
		Vector3 side = forward.Cross(viewDir).Normalized();
		if (side.LengthSquared() < 0.001f)
		{
			side = forward.Cross(Vector3.Up).Normalized();
			if (side.LengthSquared() < 0.001f) side = Vector3.Right;
		}

		return side;
	}

	private void HandleImpact(Vector3 impactPosition)
	{
		_isFlying = false;
		_isImpacted = true;
		_fadeTimer = 0.0f;

		_meshContainer.Visible = false;
		ReleaseLight();

		if (!_impactStagesNotified)
		{
			_impactStagesNotified = true;
			if (_vfxSequence != null && GodotObject.IsInstanceValid(_vfxSequence))
			{
				_vfxSequence.NotifyImpact(impactPosition);
			}
		}

		if (!string.IsNullOrEmpty(_weapon.ImpactParticleEffect))
		{
			float particleScale = _weapon.ImpactParticleScale <= 0f ? 1.0f : _weapon.ImpactParticleScale;
			GameContext.FXService.SpawnSingleParticleImpact(GetParent() ?? this, _weapon.ImpactParticleEffect, impactPosition, particleScale);
		}

		if (!string.IsNullOrEmpty(_weapon.ImpactVisualEffect) && GameContext.Host != null)
		{
			var fxService = GameContext.FXService;
			fxService?.SpawnSpritesheetEffect(GetParent() ?? this, _weapon.ImpactVisualEffect, impactPosition, 4, 4, 0.04f, 4.0f);
		}

		if (!string.IsNullOrEmpty(_weapon.ImpactSound))
		{
			var audioService = GameContext.AudioService;
			audioService?.PlaySound3D(_weapon.ImpactSound, impactPosition);
		}
	}

}
