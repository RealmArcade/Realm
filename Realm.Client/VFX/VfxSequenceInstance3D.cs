using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Metadata;

namespace Realm.Client.VFX;

[GlobalClass]
public partial class VfxSequenceInstance3D : Node3D
{
	private const float MaximumLifetimeSeconds = 15.0f;

	private readonly List<VfxStageMetadata> _timedStages = new();
	private readonly List<VfxStageMetadata> _impactStages = new();
	private readonly List<VfxStageMetadata> _targetDeathStages = new();

	private Node3D? _anchor;
	private float _elapsedSeconds;
	private float _scaleMultiplier = 1.0f;
	private bool _impactTriggered;
	private bool _targetDeathTriggered;

	public int TargetEntityId { get; private set; } = -1;

	public bool IsFinished => ComputeIsFinished();

	public void Initialize(VfxAttachmentConfig sequenceConfig, Vector3 worldPosition, float scaleMultiplier, int targetEntityId, Node3D? anchor)
	{
		_timedStages.Clear();
		_impactStages.Clear();
		_targetDeathStages.Clear();

		_scaleMultiplier = scaleMultiplier <= 0f ? 1.0f : scaleMultiplier;
		TargetEntityId = targetEntityId;
		_anchor = anchor;
		_elapsedSeconds = 0f;
		_impactTriggered = false;
		_targetDeathTriggered = false;

		foreach (var stage in sequenceConfig.Stages ?? new List<VfxStageMetadata>())
		{
			if (stage == null || string.IsNullOrWhiteSpace(stage.EffectId) && stage.ParticleConfig == null) continue;

			switch (stage.Trigger)
			{
				case VfxStageTrigger.OnImpact:
					_impactStages.Add(stage);
					break;
				case VfxStageTrigger.OnTargetDeath:
					_targetDeathStages.Add(stage);
					break;
				default:
					_timedStages.Add(stage);
					break;
			}
		}

		_timedStages.Sort((first, second) => ResolveDelay(first).CompareTo(ResolveDelay(second)));

		Name = "VfxSequence";
		Position = worldPosition;
	}

	public void Advance(float deltaSeconds)
	{
		if (deltaSeconds <= 0f) return;

		_elapsedSeconds += deltaSeconds;

		if (_anchor != null && GodotObject.IsInstanceValid(_anchor))
		{
			Position = _anchor.GlobalPosition;
		}

		while (_timedStages.Count > 0 && ResolveDelay(_timedStages[0]) <= _elapsedSeconds)
		{
			SpawnStage(_timedStages[0], GlobalPosition);
			_timedStages.RemoveAt(0);
		}
	}

	public void DetachAnchor()
	{
		_anchor = null;
	}

	public void NotifyImpact(Vector3 worldImpactPosition)
	{
		if (_impactTriggered) return;
		_impactTriggered = true;
		_anchor = null;
		ClearSpawnedStages();
		Position = worldImpactPosition;

		foreach (var stage in _impactStages)
		{
			SpawnStage(stage, worldImpactPosition);
		}
	}

	public void NotifyTargetDeath()
	{
		if (_targetDeathTriggered) return;
		_targetDeathTriggered = true;

		foreach (var stage in _targetDeathStages)
		{
			SpawnStage(stage, GlobalPosition);
		}
	}

	private void ClearSpawnedStages()
	{
		foreach (Node child in GetChildren())
		{
			RemoveChild(child);
			child.QueueFree();
		}
	}

	private static float ResolveDelay(VfxStageMetadata stage)
	{
		if (stage.Trigger == VfxStageTrigger.Immediately) return 0f;
		return Math.Max(0f, stage.DelaySeconds);
	}

	private void SpawnStage(VfxStageMetadata stage, Vector3 worldPosition)
	{
		float stageScale = Math.Max(0.01f, stage.Scale) * _scaleMultiplier;
		GameContext.FXService.SpawnVfxStage(this, stage, worldPosition, stageScale);
	}

	private bool ComputeIsFinished()
	{
		if (_elapsedSeconds >= MaximumLifetimeSeconds) return true;
		if (_timedStages.Count > 0) return false;
		if (_impactStages.Count > 0 && !_impactTriggered) return false;
		if (_targetDeathStages.Count > 0 && !_targetDeathTriggered) return false;
		return true;
	}
}
