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
	private Vector3 _lastNotifiedScale = new Vector3(-1f, -1f, -1f);

	public override void _Notification(int what)
	{
		if (what == NotificationTransformChanged)
		{
			Vector3 currentScale = Scale;
			if (!currentScale.IsEqualApprox(_lastNotifiedScale))
			{
				_lastNotifiedScale = currentScale;
				UpdateLodVisibility();
				UpdateSelectionRingTransform();
			}
		}
	}

	public override void _Ready()
	{
		if (IsPreview)
		{
			SeekToIdleFirstFrame();
			return;
		}

		SetProcess(false);
		SetNotifyTransform(true);
	}

	private float _lastUnitEffectiveLodScale = -1f;

	public void RefreshLodVisibilityDistances()
	{
		_lastUnitEffectiveLodScale = -1f;
		UpdateLodVisibility();
	}

	public void UpdateLodVisibility()
	{
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			float effectiveScale = Math.Max(0.01f, Scale.Y * _modelNode.Scale.Y);
			if (MathF.Abs(effectiveScale - _lastUnitEffectiveLodScale) > 0.0005f)
			{
				_lastUnitEffectiveLodScale = effectiveScale;
				GltfDocumentExtensionMsftLod.UpdateLodVisibilityRanges(_modelNode, effectiveScale);
			}
		}
	}

	public float BaseModelYOffset => 0f;
	public string ModelPath { get; private set; } = null!;
	public Node3D? ModelNode => _modelNode;
	public bool IsFlashingScale { get; set; }

	public void UpdateVisualYOffset(float yOffset) => UpdateModelYOffset(yOffset);
	public void UpdateVisualScale(float globalScale) => UpdateModelScale(globalScale);

	public void UpdateModelYOffset(float yOffset)
	{
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			_modelNode.Position = new Vector3(_modelNode.Position.X, yOffset, _modelNode.Position.Z);
		}
	}

	public void UpdateModelScale(float globalScale)
	{
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			float safeScale = globalScale <= 0.001f ? 1.0f : globalScale;
			_cachedBaseModelScale = safeScale;
			_isLaborPulsing = false;
			_modelNode.Scale = new Vector3(safeScale, safeScale, safeScale);
			float yOffset = GameContext.Host != null ? GameContext.Host.GetModelYOffset(this) : 0f;
			_modelNode.Position = new Vector3(_modelNode.Position.X, yOffset, _modelNode.Position.Z);
			var calculatedAabb = CalculateModelLocalAabb();
			if (calculatedAabb.Size.LengthSquared() > 0.001f)
			{
				_cachedLocalAabb = new Aabb(calculatedAabb.Position * _cachedBaseModelScale, calculatedAabb.Size * _cachedBaseModelScale);
			}
			UpdateLodVisibility();
		}
	}

	public void LoadModel(string modelPath)
	{
		if (string.IsNullOrEmpty(modelPath)) return;
		ModelPath = modelPath;

		CleanupExistingModel();

		string? resolved = ModelCache.ResolveModelPath(modelPath);
		if (string.IsNullOrEmpty(resolved))
		{
			resolved = modelPath;
		}

		_modelNode = ResolveAndGetModelNode(modelPath, resolved);

		if (_modelNode == null)
		{
			GameLog.LogError($"Failed to load 3D model path: {modelPath}");
			return;
		}

		_modelNode.Name = "VisualModel";
		AddChild(_modelNode);

		_cachedSkeleton = FindSkeleton(_modelNode);
		ConfigureAnimations(modelPath);
		ApplyAttachmentsSafely();
		ApplyTransformations();
		ApplyVisuals(modelPath);
		UpdateDropShadow();
		UpdateSlopeAlignment();
		ApplyPreviewHologram();

		var calculatedAabb = CalculateModelLocalAabb();
		if (calculatedAabb.Size.LengthSquared() > 0.001f)
		{
			_cachedLocalAabb = new Aabb(calculatedAabb.Position * _cachedBaseModelScale, calculatedAabb.Size * _cachedBaseModelScale);
		}

		RebuildGeometryInstanceCache();
	}

	private void CleanupExistingModel()
	{
		if (_modelNode == null || !GodotObject.IsInstanceValid(_modelNode)) return;

		RemoveChild(_modelNode);
		_modelNode.QueueFree();
		_modelNode = null;
		_cachedSkeleton = null;
		_isCulled = false;
		_hasEvaluatedFrustum = false;
		_animStepAccumulator = 0f;
		_isLaborPulsing = false;
		_rightHandAttachment = null;
		_leftHandAttachment = null;
		_currentRightAttachmentId = null;
		_currentLeftAttachmentId = null;
		_boneAttachments.Clear();
		_currentBoneAttachmentIds.Clear();
		_pseudoSockets.Clear();
		_currentPseudoSocketAttachmentIds.Clear();
		_pseudoSocketGlows.Clear();
		_resolvedPseudoSocketPoints = null;
		_resolvedAuthorAnchors = null;
		_resolvedAnimCache.Clear();
		_lastRequestedAnimName = null;
		_currentAnimation = string.Empty;
		CancelDeferredAnimation();
		_lastUnitEffectiveLodScale = -1f;
	}

	private Node3D? ResolveAndGetModelNode(string modelPath, string resolved)
	{
		var rawNode = ModelCache.GetModel(modelPath);
		if (rawNode is Node3D node3D) return node3D;
		if (rawNode != null) rawNode.QueueFree();

		if (!string.IsNullOrEmpty(resolved) && !resolved.Equals(modelPath, StringComparison.OrdinalIgnoreCase))
		{
			rawNode = ModelCache.GetModel(resolved);
			if (rawNode is Node3D resolvedNode3D) return resolvedNode3D;
			if (rawNode != null) rawNode.QueueFree();
		}

		return null;
	}

	private void ConfigureAnimations(string modelPath)
	{
		try
		{
			if (_modelNode == null) return;
			_animationPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_modelNode);
			if (_animationPlayer != null && !IsPreview)
			{
				_animationPlayer.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
			}
			AnimationRetargetingService.LoadAndBindUnitAnimations(_modelNode, UnitId, modelPath);
			SeekToIdleFirstFrame();
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Error configuring animations for unit '{UnitId}' ({modelPath}): {ex.Message}");
		}
	}

	private void ApplyAttachmentsSafely()
	{
		try
		{
			ApplyAllConfiguredAttachments();
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Error applying attachments for unit '{UnitId}': {ex.Message}");
		}
	}

	private void ApplyTransformations()
	{
		if (_modelNode == null) return;
		float globalScale = GameContext.Host != null ? GameContext.Host.GetModelScale(this) : 1.0f;
		float safeScale = globalScale <= 0.001f ? 1.0f : globalScale;
		_cachedBaseModelScale = safeScale;
		_isLaborPulsing = false;
		_modelNode.Scale = new Vector3(safeScale, safeScale, safeScale);

		UpdateLodVisibility();

		float yOffset = GameContext.Host != null ? GameContext.Host.GetModelYOffset(this) : 0f;
		_modelNode.Position = new Vector3(0f, yOffset, 0f);
	}

	private void ApplyVisuals(string modelPath)
	{
		if (IsPreview || _modelNode == null) return;
		try
		{
			bool ignorePlayerColor = GameContext.Host != null && (GameContext.Host.GetModelIgnorePlayerColor(modelPath) || GameContext.Host.GetModelIgnorePlayerColor(UnitId));
			bool normalizeLuminance = GameContext.Host != null && (GameContext.Host.GetModelNormalizeLuminance(modelPath) || GameContext.Host.GetModelNormalizeLuminance(UnitId));
			ModelShaderManager.ApplyPlayerColorShader(_modelNode, PlayerColor, ignorePlayerColor, normalizeLuminance);
			if (!ignorePlayerColor)
			{
				UpdatePlayerColorVisual();
			}
			else
			{
				ModelShaderManager.SetIgnorePlayerColor(_modelNode, true);
			}

			GameContext.Host?.ApplyAllGlobalOverridesToObject(this);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Error applying shaders for unit '{UnitId}': {ex.Message}");
		}
	}

	public bool IsSlopeAligned
	{
		get
		{
			if (GameHost.UnitRegistry.TryGetValue(UnitId, out var meta))
			{
				return string.Equals(meta.VisualMode, "SlopeAlignedQuad", StringComparison.OrdinalIgnoreCase);
			}

			if (GameHost.BuildingRegistry.TryGetValue(UnitId, out var bMeta))
			{
				return string.Equals(bMeta.VisualMode, "SlopeAlignedQuad", StringComparison.OrdinalIgnoreCase);
			}

			return false;
		}
	}

	public void UpdateSlopeAlignment()
	{
		Realm.Client.Utils.ModelExtensions.ApplySlopeAlignment(this, _modelNode, IsSlopeAligned);
	}

	public Color? PreviewHologramColor { get; set; }

	public void ApplyPreviewHologram()
	{
		if (!PreviewHologramColor.HasValue) return;
		Realm.Client.Utils.PreviewHologram.Apply(this, PreviewHologramColor.Value);
	}

	public float GetBaseObstacleRadius()
	{
		if (GameContext.Host != null)
		{
			return GameContext.Host.ResolveEntityBodyRadius(Entity, UnitId);
		}

		return 1.4f;
	}

}
