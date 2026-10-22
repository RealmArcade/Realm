using System;
using System.Collections.Generic;
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

[GlobalClass]
public partial class Unit3D : Node3D
{
	public Entity Entity { get; set; }

	private Vector3 _cachedPosition = Vector3.Zero;
	private float _cachedRotationY = 0f;
	private Basis _cachedBasis = Basis.Identity;
	private Vector3 _cachedScale = Vector3.One;
	private float _cachedBaseScale = 1.0f;
	private Transform3D _cachedTransform = Transform3D.Identity;
	private bool _hasCachedTransform = false;

	public Vector3 CachedPosition => _hasCachedTransform ? _cachedPosition : GlobalPosition;
	public float CachedRotationY => _hasCachedTransform ? _cachedRotationY : Rotation.Y;
	public Basis CachedBasis => _hasCachedTransform ? _cachedBasis : Basis;
	public Vector3 CachedScale => _hasCachedTransform ? _cachedScale : Scale;
	public float CachedBaseScale => _cachedBaseScale;
	public Transform3D CachedTransform => _hasCachedTransform ? _cachedTransform : GlobalTransform;
	public bool HasCachedTransform => _hasCachedTransform;

	public void SetTransform3D(in Transform3D xform, in Vector3 pos, float rotY, in Basis basis, in Vector3 scale, float baseScale)
	{
		_cachedPosition = pos;
		_cachedRotationY = rotY;
		_cachedBasis = basis;
		_cachedScale = scale;
		_cachedBaseScale = baseScale;
		_cachedTransform = xform;
		_hasCachedTransform = true;
		Transform = xform;
	}

	public void SetInitialTransform(in Vector3 pos, float rotY, in Vector3 scale)
	{
		_cachedPosition = pos;
		_cachedRotationY = rotY;
		_cachedBasis = Basis.FromEuler(new Vector3(0f, rotY, 0f));
		_cachedScale = scale;
		_cachedBaseScale = scale.Y;
		_cachedTransform = new Transform3D(_cachedBasis.Scaled(scale), pos);
		_hasCachedTransform = true;
		Transform = _cachedTransform;
	}

	private readonly List<GeometryInstance3D> _cachedGeometryInstances = new();

	public void RebuildGeometryInstanceCache()
	{
		_cachedGeometryInstances.Clear();
		Node root = (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) ? _modelNode : this;
		CollectGeometryInstancesRecursive(root, _cachedGeometryInstances);
	}

	private static void CollectGeometryInstancesRecursive(Node node, List<GeometryInstance3D> list)
	{
		if (node is GeometryInstance3D geom && !ModelShaderManager.IsAttachmentNode(geom))
		{
			list.Add(geom);
		}

		int childCount = node.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			CollectGeometryInstancesRecursive(node.GetChild(i), list);
		}
	}

	public IReadOnlyList<GeometryInstance3D> GetCachedGeometryInstances()
	{
		if (_cachedGeometryInstances.Count == 0)
		{
			RebuildGeometryInstanceCache();
		}
		return _cachedGeometryInstances;
	}

	private float _cachedBaseModelScale = 1.0f;
	private bool _isLaborPulsing = false;

	public void SetLaborPulseScale(float pulse)
	{
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			_isLaborPulsing = true;
			float baseModelScale = _cachedBaseModelScale;
			_modelNode.Scale = new Vector3(pulse * 0.9f * baseModelScale, (2.0f - pulse) * 0.9f * baseModelScale, pulse * 0.9f * baseModelScale);
		}
	}

	public void ResetLaborPulseScale()
	{
		if (!_isLaborPulsing) return;
		_isLaborPulsing = false;
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			float baseModelScale = _cachedBaseModelScale;
			_modelNode.Scale = new Vector3(baseModelScale, baseModelScale, baseModelScale);
		}
	}

	private Vector3 _velocity = Vector3.Zero;
	public bool HasProceduralAnimation { get; set; }

	public Vector3 Velocity
	{
		get => _velocity;
		set
		{
			if (_velocity != value)
			{
				_velocity = value;
				if (HasProceduralAnimation)
				{
					ModelShaderManager.SetProceduralAnimationVelocity(GetCachedGeometryInstances(), value);
				}
			}
		}
	}

	private float _impulseStrength = 0f;
	private float _impulseDuration = 0.5f;
	private float _impulseTime = 0f;
	private float _impulseFrequency = 12.0f;

	public bool HasActiveImpulse => _impulseTime > 0f;

	public void TriggerImpulse(float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f)
	{
		if (!HasProceduralAnimation) return;
		_impulseStrength = strength;
		_impulseDuration = duration <= 0.001f ? 0.001f : duration;
		_impulseTime = _impulseDuration;
		_impulseFrequency = frequency;
		GameHost.Instance?.RegisterActiveImpulseUnit(this);
	}

	public void UpdateImpulseVisuals(float fDelta)
	{
		if (_impulseTime <= 0f) return;

		_impulseTime -= fDelta;
		if (_impulseTime <= 0f)
		{
			_impulseTime = 0f;
			_impulseStrength = 0f;
			ModelShaderManager.SetProceduralAnimationImpulse(GetCachedGeometryInstances(), 0f, _impulseFrequency, 0f);
		}
		else
		{
			float progress = 1f - (_impulseTime / _impulseDuration);
			float currentStrength = _impulseStrength * (1f - progress);
			ModelShaderManager.SetProceduralAnimationImpulse(GetCachedGeometryInstances(), currentStrength, _impulseFrequency, progress);
		}
	}

	private string _objectId = "unit/worker";

	[Export]
	public string TemplateID
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World.IsAlive(Entity)
			                              && GameContext.World.Has<DefinitionId>(Entity))
				return GameContext.World.Get<DefinitionId>(Entity).Value;
			return _objectId;
		}
		set
		{
			_objectId = value;
			if (GameHost.Instance != null && GameContext.World.IsAlive(Entity))
			{
				var world = GameContext.World;
				world.SetOrAdd(Entity, new DefinitionId(value));
			}
		}
	}

	public string UnitId
	{
		get => TemplateID;
		set => TemplateID = value;
	}

	public string PropId
	{
		get => TemplateID;
		set => TemplateID = value;
	}

	private bool _isBuilding;

	public bool IsBuilding
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World != null && Entity != default && GameContext.World.IsAlive(Entity))
				return GameContext.World.Has<Building>(Entity);
			if (GameHost.BuildingRegistry != null && !string.IsNullOrEmpty(UnitId) && GameHost.BuildingRegistry.ContainsKey(UnitId))
				return true;
			return _isBuilding;
		}
		set
		{
			_isBuilding = value;
			if (GameHost.Instance != null && GameContext.World != null && Entity != default && GameContext.World.IsAlive(Entity))
			{
				var world = GameContext.World;
				if (value)
				{
					if (!world.Has<Building>(Entity))
						world.Add(Entity, new Building());
				}
				else
				{
					if (world.Has<Building>(Entity))
						world.Remove<Building>(Entity);
				}
			}
		}
	}

	public bool IsResource
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World != null && Entity != default && GameContext.World.IsAlive(Entity))
			{
				if (GameContext.World.Has<ResourceNode>(Entity))
					return true;
			}

			if (GameHost.ResourceRegistry != null && !string.IsNullOrEmpty(UnitId) && GameHost.ResourceRegistry.ContainsKey(UnitId))
				return true;
			return _isResource;
		}
		set { _isResource = value; }
	}

	private bool _isResource;

	private Node3D _modelNode;
	private AnimationPlayer _animationPlayer;
	private Skeleton3D? _cachedSkeleton;
	private string _currentAnimation = string.Empty;
	private string? _lastRequestedAnimName;
	private readonly Dictionary<string, StringName?> _resolvedAnimCache = new(StringComparer.OrdinalIgnoreCase);
	private BoneAttachment3D? _rightHandAttachment;
	private BoneAttachment3D? _leftHandAttachment;
	private string? _currentRightAttachmentId;
	private string? _currentLeftAttachmentId;
	private readonly Dictionary<HumanoidBone, Node3D> _boneAttachments = new();
	private readonly Dictionary<HumanoidBone, string> _currentBoneAttachmentIds = new();
	private Node3D _pathVisualsContainer;
	private readonly List<MeshInstance3D> _pathMarkersPool = new();
	private readonly List<MeshInstance3D> _pathLinesPool = new();
	private readonly List<Vector3> _pathPointsBuffer = new();
	private readonly List<Vector3> _pathSampledPoints = new();
	private Vector3 _lastPathRenderPos = new Vector3(float.NaN, float.NaN, float.NaN);
	private bool _lastPathRenderIsAttackPath = false;
	private readonly List<Vector3> _lastRenderedPathPoints = new();

	private bool _isCulled = false;
	private float _culledGameTime = 0f;
	private bool _hasEvaluatedFrustum = false;
	private float _animStepAccumulator = 0f;
	private Aabb _cachedLocalAabb = new Aabb(new Vector3(-1.5f, 0f, -1.5f), new Vector3(3f, 4f, 3f));

	public bool IsCulled => _isCulled;
	public bool HasEvaluatedFrustum
	{
		get => _hasEvaluatedFrustum;
		set => _hasEvaluatedFrustum = value;
	}
	public Aabb CachedLocalAabb => _cachedLocalAabb;

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

	private int _player = 0;

	public int Player
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World.IsAlive(Entity)
			                              && GameContext.World.Has<UnitOwnerPlayer>(Entity))
				return GameContext.World.Get<UnitOwnerPlayer>(Entity).PlayerIndex;
			return _player;
		}
		set
		{
			_player = value;
			if (GameHost.Instance != null && GameContext.World.IsAlive(Entity))
			{
				var world = GameContext.World;
				world.SetOrAdd(Entity, new UnitOwnerPlayer(value));
			}

			UpdatePlayerColorVisual();
		}
	}

	public int PlayerIndex
	{
		get => Player;
		set => Player = value;
	}

	public Color PlayerColor { get; private set; } = PlayerColorConfig.GetColor(0);

	public void SetPlayer(int playerIndex)
	{
		Player = playerIndex;
	}

	public void SetPlayerColor(Color color)
	{
		PlayerColor = color;
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			bool ignorePlayerColor = GameHost.Instance != null && (GameHost.Instance.GetModelIgnorePlayerColor(ModelPath) || GameHost.Instance.GetModelIgnorePlayerColor(UnitId));
			ModelShaderManager.SetPlayerColor(_modelNode, color);
			ModelShaderManager.SetPlayerColorOnAttachments(_modelNode, color);
			ModelShaderManager.SetIgnorePlayerColor(_modelNode, ignorePlayerColor);
		}

		if (_playerColorRing != null && GodotObject.IsInstanceValid(_playerColorRing))
		{
			_playerColorRing.MaterialOverride = Realm.Client.Utils.SharedMaterials.GetOrCreatePlayerColorGlowMaterial(color);
		}

		foreach (var glowingSocket in _pseudoSocketGlows)
		{
			if (glowingSocket.Value != null && GodotObject.IsInstanceValid(glowingSocket.Value))
			{
				glowingSocket.Value.MaterialOverride = Realm.Client.Utils.SharedMaterials.GetOrCreatePlayerColorGlowMaterial(color);
			}
		}
	}

	public void InterpolatePlayerColor(Color targetColor, float weight)
	{
		Color blended = PlayerColor.Lerp(targetColor, weight);
		SetPlayerColor(blended);
	}

	public void UpdatePlayerColorVisual()
	{
		bool ignorePlayerColor = GameHost.Instance != null && (GameHost.Instance.GetModelIgnorePlayerColor(ModelPath) || GameHost.Instance.GetModelIgnorePlayerColor(UnitId));
		if (ignorePlayerColor)
		{
			if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
			{
				ModelShaderManager.SetIgnorePlayerColor(_modelNode, true);
			}

			return;
		}

		Color resolvedColor = PlayerColorConfig.GetColor(Player);
		if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
		{
			var matchPlayer = LobbyManager.Instance.PlayerList.Find(x => x.Slot == Player);
			if (matchPlayer != null)
			{
				resolvedColor = matchPlayer.Color;
			}
		}

		SetPlayerColor(resolvedColor);
	}
	private Node3D _rallyVisualsContainer;
	private MeshInstance3D _rallyMarker;
	private readonly List<MeshInstance3D> _rallyMarkersPool = new();
	private readonly List<MeshInstance3D> _rallyLinesPool = new();
	private readonly List<Vector3> _rallyPointsBuffer = new();
	private Vector3 _lastRallyRenderPos = new Vector3(float.NaN, float.NaN, float.NaN);
	private readonly List<Vector3> _lastRenderedRallyPoints = new();

	private MeshInstance3D _selectionRing;
	private bool _isSelected = false;
	private MeshInstance3D _hoverRing;
	private bool _isHovered = false;
	private MeshInstance3D _playerColorRing;

	public bool IsPreview { get; set; } = false;

	public bool IsSelected
	{
		get => _isSelected;
		set
		{
			_isSelected = value;
			if (_selectionRing == null && _isSelected)
			{
				CreateSelectionRing();
			}

			if (_selectionRing != null)
			{
				if (_isSelected)
				{
					UpdateSelectionRingTransform();
				}

				_selectionRing.Visible = _isSelected;
			}

			if (_hoverRing != null)
			{
				_hoverRing.Visible = _isHovered && !_isSelected;
			}

			if (IsBuilding && GameContext.PerspectiveFilter.CanSeeOwnedUi(Player))
			{
				UpdateRallyVisuals();
			}

			if (!value)
			{
				HidePathVisuals();
				if (_rallyVisualsContainer != null)
				{
					_rallyVisualsContainer.Visible = false;
				}
			}

		}
	}

	public bool IsHovered
	{
		get => _isHovered;
		set
		{
			_isHovered = value;
			if (_hoverRing == null && _isHovered)
			{
				CreateHoverRing();
			}

			if (_hoverRing != null)
			{
				if (_isHovered)
				{
					UpdateSelectionRingTransform();
				}

				_hoverRing.Visible = _isHovered && !IsSelected;
			}
		}
	}

	public static void ClearCache()
	{
		Realm.Client.Utils.VisualSelectionHelper.ClearCache();
	}

	private void UpdateSelectionRingTransform()
	{
		Realm.Client.Utils.VisualSelectionHelper.UpdateSelectionRing(this, _selectionRing, _hoverRing, Scale, GetBaseObstacleRadius());
		UpdatePlayerColorRing();
	}

	private void UpdatePlayerColorRing()
	{
		if (IsPreview || IsBuilding)
		{
			if (_playerColorRing != null) _playerColorRing.Visible = false;
			return;
		}

		if (UI.GameSettings.QualityIdx == UI.GraphicsQuality.Low)
		{
			if (_playerColorRing != null) _playerColorRing.Visible = false;
			return;
		}

		float baseRadius = GetBaseObstacleRadius();
		if (baseRadius <= 0.001f)
		{
			if (_playerColorRing != null) _playerColorRing.Visible = false;
			return;
		}

		EnsurePlayerColorRing();

		Realm.Client.Utils.VisualSelectionHelper.CalculateSelectionRingParams(Scale, ModelPath, baseRadius, out float sx, out float ratio, out float worldRadius);

		float sy = Mathf.Abs(Scale.Y) > 0.001f ? Mathf.Abs(Scale.Y) : 1.0f;
		_playerColorRing.Mesh = Realm.Client.Utils.VisualSelectionHelper.GetOrCreatePlayerColorGlowMesh(worldRadius);
		_playerColorRing.Scale = new Vector3(1.0f / sx, 1.0f / sy, 1.0f / sx);
		_playerColorRing.Position = new Vector3(0f, 0.04f / sy, 0f);
		_playerColorRing.MaterialOverride = Realm.Client.Utils.SharedMaterials.GetOrCreatePlayerColorGlowMaterial(PlayerColor);
		_playerColorRing.Visible = !IsSelected && !IsHovered;
	}

	private void EnsurePlayerColorRing()
	{
		if (_playerColorRing != null) return;

		_playerColorRing = new MeshInstance3D
		{
			Name = "PlayerColorGlow_MeshInstance3D",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
			Visible = false
		};
		AddChild(_playerColorRing);
	}

	private void CreateHoverRing()
	{
		if (_hoverRing != null) return;
		_hoverRing = new MeshInstance3D
		{
			Name = "_hover_ring",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
			MaterialOverride = Realm.Client.Utils.SharedMaterials.GetOrCreateHoverMaterial(),
			Visible = _isHovered && !IsSelected
		};
		AddChild(_hoverRing);
		UpdateSelectionRingTransform();
	}

	public void UpdateCollisionCircleScale(float ratio)
	{
		UpdateSelectionRingTransform();
		if (_selectionRing != null)
		{
			_selectionRing.Visible = _isSelected;
		}

		if (_hoverRing != null)
		{
			_hoverRing.Visible = _isHovered && !_isSelected;
		}
	}

	public void SetTemporarySelectionHighlight(bool highlight)
	{
		if (_selectionRing == null && highlight)
		{
			CreateSelectionRing();
		}

		if (_selectionRing != null)
		{
			if (highlight || _isSelected)
			{
				UpdateSelectionRingTransform();
			}

			_selectionRing.Visible = highlight || _isSelected;
		}
	}

	private void CreateSelectionRing()
	{
		if (_selectionRing != null) return;
		_selectionRing = new MeshInstance3D
		{
			Name = "_selection_ring",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
			MaterialOverride = Realm.Client.Utils.SharedMaterials.GetOrCreateSelectionMaterial(GetSelectionRingColor()),
			Visible = _isSelected
		};
		AddChild(_selectionRing);
		UpdateSelectionRingTransform();
	}

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
	public string ModelPath { get; private set; }
	public Node3D ModelNode => _modelNode;
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
			float yOffset = GameHost.Instance != null ? GameHost.Instance.GetModelYOffset(this) : 0f;
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

		string resolved = ModelCache.ResolveModelPath(modelPath);
		if (string.IsNullOrEmpty(resolved))
		{
			resolved = modelPath;
		}

		_modelNode = ResolveAndGetModelNode(modelPath, resolved);

		if (_modelNode == null)
		{
			GD.PrintErr($"Failed to load 3D model path: {modelPath}");
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
			GD.PrintErr($"Error configuring animations for unit '{UnitId}' ({modelPath}): {ex.Message}");
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
			GD.PrintErr($"Error applying attachments for unit '{UnitId}': {ex.Message}");
		}
	}

	private void ApplyTransformations()
	{
		if (_modelNode == null) return;
		float globalScale = GameHost.Instance != null ? GameHost.Instance.GetModelScale(this) : 1.0f;
		float safeScale = globalScale <= 0.001f ? 1.0f : globalScale;
		_cachedBaseModelScale = safeScale;
		_isLaborPulsing = false;
		_modelNode.Scale = new Vector3(safeScale, safeScale, safeScale);

		UpdateLodVisibility();

		float yOffset = GameHost.Instance != null ? GameHost.Instance.GetModelYOffset(this) : 0f;
		_modelNode.Position = new Vector3(0f, yOffset, 0f);
	}

	private void ApplyVisuals(string modelPath)
	{
		if (IsPreview || _modelNode == null) return;
		try
		{
			bool ignorePlayerColor = GameHost.Instance != null && (GameHost.Instance.GetModelIgnorePlayerColor(modelPath) || GameHost.Instance.GetModelIgnorePlayerColor(UnitId));
			bool normalizeLuminance = GameHost.Instance != null && (GameHost.Instance.GetModelNormalizeLuminance(modelPath) || GameHost.Instance.GetModelNormalizeLuminance(UnitId));
			ModelShaderManager.ApplyPlayerColorShader(_modelNode, PlayerColor, ignorePlayerColor, normalizeLuminance);
			if (!ignorePlayerColor)
			{
				UpdatePlayerColorVisual();
			}
			else
			{
				ModelShaderManager.SetIgnorePlayerColor(_modelNode, true);
			}

			GameHost.Instance?.ApplyAllGlobalOverridesToObject(this);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error applying shaders for unit '{UnitId}': {ex.Message}");
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
		if (GameHost.Instance != null)
		{
			return GameHost.Instance.GetOrCalculateObstacleRadius(UnitId, this);
		}

		return 1.4f;
	}

	/// <summary>
	///     Adds (or refreshes) a projected drop shadow for flying units so they read clearly
	///     against the terrain below instead of appearing to float in empty space.
	/// </summary>
	private void UpdateDropShadow()
	{
		if (GameHost.Instance == null || !GameHost.Instance.IsPathingCapability(Entity, TerrainPathingFlags.Flying))
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
			float updatedRadius = GameHost.Instance.GetOrCalculateObstacleRadius(UnitId, this) * GameHost.Instance.GetModelCollisionCircleRatio(ModelPath);
			if (updatedRadius > 0.001f)
			{
				float safeRadius = Mathf.Max(0.1f, updatedRadius * 2.5f);
				existing.Size = new Vector3(safeRadius, 3f, safeRadius);
			}

			return;
		}

		float radius = GameHost.Instance.GetOrCalculateObstacleRadius(UnitId, this) * GameHost.Instance.GetModelCollisionCircleRatio(ModelPath);
		if (radius <= 0.001f) radius = 1f;

		Decal shadowDecal = new Decal();
		shadowDecal.Name = "DropShadow";
		shadowDecal.TextureAlbedo = GameHost.Instance.GetSharedShadowGradient();
		float decalSize = Mathf.Max(0.1f, radius * 2.5f);
		shadowDecal.Size = new Vector3(decalSize, 3f, decalSize);
		shadowDecal.Position = Vector3.Zero;
		// Decals project along local -Z; tilt the node down so the shadow lands on the terrain.
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

		if (_animationPlayer == null || !GodotObject.IsInstanceValid(_animationPlayer)) return;

		if (frozen)
		{
			_animationPlayer.Pause();
			GameHost.Instance?.SuspendProceduralAnimation(this);
		}
		else
		{
			_animationPlayer.Play();
			if (HasProceduralAnimation)
			{
				GameHost.Instance?.ReapplyProceduralAnimation(this);
			}
		}
	}

	public void PlayAnimation(string animName)
	{
		if (AnimationsFrozen) return;
		if (_animationPlayer == null || !GodotObject.IsInstanceValid(_animationPlayer)) return;
		if (string.IsNullOrEmpty(animName)) return;

		if (string.Equals(_lastRequestedAnimName, animName, StringComparison.Ordinal) && _animationPlayer.IsPlaying())
		{
			return;
		}

		_lastRequestedAnimName = animName;

		StringName resolved = ResolveAnimationName(animName);
		if (resolved == null) return;

		string resolvedStr = resolved.ToString();
		UpdateHandAttachmentsForAnimation(animName, resolvedStr);

		if (_currentAnimation == resolvedStr && _animationPlayer.IsPlaying()) return;

		_currentAnimation = resolvedStr;

		var animResource = _animationPlayer.GetAnimation(resolved);
		if (animResource != null)
		{
			if (animName.Equals("Death", StringComparison.OrdinalIgnoreCase))
			{
				animResource.LoopMode = Godot.Animation.LoopModeEnum.None;
			}
			else
			{
				animResource.LoopMode = Godot.Animation.LoopModeEnum.Linear;
			}
		}

		if (_isCulled)
		{
			_culledGameTime = GameHost.Instance?.GameElapsedTime ?? 0f;
		}

		_animStepAccumulator = 0f;
		if (!IsPreview)
		{
			_animationPlayer.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		}
		_animationPlayer.ProcessMode = _isCulled ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
		_animationPlayer.Play(resolved, AnimationTransitionSeconds);
		if (!_isCulled)
		{
			_animationPlayer.Advance(0.0);
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

		StringName idleAnim = ResolveAnimationName("Idle");
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

	public void SetHandAttachment(
		HumanoidBone hand,
		string? attachmentId,
		Vector3? posOffsetOverride = null,
		Vector3? rotOffsetOverride = null,
		float? scaleOverride = null)
	{
		SetSocketAttachment(hand, attachmentId, posOffsetOverride, rotOffsetOverride, scaleOverride, null, null, true);
	}

	public void SetSocketAttachment(
		HumanoidBone bone,
		string? attachmentId,
		Vector3? posOffsetOverride = null,
		Vector3? rotOffsetOverride = null,
		float? scaleOverride = null,
		Vector3? scaleVectorOverride = null,
		float? normalOffsetOverride = null,
		bool clearExisting = false,
		string? parentAttachmentId = null)
	{
		var parentNode = (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) ? _modelNode : (Node3D)this;
		var skeleton = FindSkeleton(parentNode);
		int boneIdx = skeleton != null ? HumanoidBoneExtensions.FindBoneInSkeleton(skeleton, bone) : -1;

		if (TryClearSocketAttachment(bone, attachmentId, clearExisting, posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride))
		{
			return;
		}

		if (bone == HumanoidBone.RightHand) _currentRightAttachmentId = attachmentId;
		else if (bone == HumanoidBone.LeftHand) _currentLeftAttachmentId = attachmentId;
		_currentBoneAttachmentIds[bone] = attachmentId;

		Node3D attachTarget = EnsureAttachTarget(parentNode, skeleton, bone, boneIdx);

		if (clearExisting)
		{
			ClearNodeChildren(attachTarget);
		}

		Node3D? model = ResolveAndInstantiateAttachment(attachmentId, out float defScale, out Vector3 defPos, out Vector3 defRot);
		if (model != null)
		{
			ApplySocketAttachmentInstance(model, attachmentId, attachTarget, parentNode, parentAttachmentId, bone, defScale, defPos, defRot, posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride);
		}
	}

	private static bool IsAttachmentIdEmptyOrNone(string? attachmentId)
	{
		if (string.IsNullOrEmpty(attachmentId)) return true;
		if (attachmentId.Equals("null", StringComparison.OrdinalIgnoreCase)) return true;
		if (attachmentId.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	private bool TryClearSocketAttachment(HumanoidBone bone, string? attachmentId, bool clearExisting, Vector3? posOffsetOverride, Vector3? rotOffsetOverride, float? scaleOverride, Vector3? scaleVectorOverride, float? normalOffsetOverride)
	{
		if (IsAttachmentIdEmptyOrNone(attachmentId))
		{
			ClearEmptyAttachmentId(bone);
			return true;
		}

		return ShouldClearExistingAttachment(bone, attachmentId, clearExisting, posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride);
	}

	private void ClearEmptyAttachmentId(HumanoidBone bone)
	{
		if (bone == HumanoidBone.RightHand) _currentRightAttachmentId = null;
		else if (bone == HumanoidBone.LeftHand) _currentLeftAttachmentId = null;
		_currentBoneAttachmentIds.Remove(bone);

		if (_boneAttachments.TryGetValue(bone, out var existing) && GodotObject.IsInstanceValid(existing))
		{
			ClearNodeChildren(existing);
		}
	}

	private static bool HasAnyOverrides(Vector3? pos, Vector3? rot, float? scale, Vector3? scaleVec, float? norm)
	{
		return pos.HasValue || rot.HasValue || scale.HasValue || scaleVec.HasValue || norm.HasValue;
	}

	private bool ShouldClearExistingAttachment(HumanoidBone bone, string? attachmentId, bool clearExisting, Vector3? posOffsetOverride, Vector3? rotOffsetOverride, float? scaleOverride, Vector3? scaleVectorOverride, float? normalOffsetOverride)
	{
		if (!clearExisting) return false;
		if (!_currentBoneAttachmentIds.TryGetValue(bone, out var currentId) || currentId != attachmentId) return false;
		if (!_boneAttachments.TryGetValue(bone, out var currentAttachment)) return false;
		if (!GodotObject.IsInstanceValid(currentAttachment)) return false;
		if (currentAttachment.GetChildCount() <= 0) return false;
		if (HasAnyOverrides(posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride)) return false;

		return true;
	}

	private Node3D EnsureAttachTarget(Node3D parentNode, Skeleton3D? skeleton, HumanoidBone bone, int boneIdx)
	{
		if (skeleton != null && boneIdx >= 0)
		{
			string boneName = skeleton.GetBoneName(boneIdx);
			string nodeName = $"BoneAttachment_{bone}";
			var boneAttachment = skeleton.GetNodeOrNull<BoneAttachment3D>(nodeName);
			if (boneAttachment == null)
			{
				boneAttachment = new BoneAttachment3D { Name = nodeName, BoneName = boneName, BoneIdx = boneIdx };
				skeleton.AddChild(boneAttachment);
			}

			_boneAttachments[bone] = boneAttachment;
			if (bone == HumanoidBone.RightHand) _rightHandAttachment = boneAttachment;
			else if (bone == HumanoidBone.LeftHand) _leftHandAttachment = boneAttachment;
			return boneAttachment;
		}

		string socketNodeName = $"SocketAttachment_{bone}";
		var socketNode = parentNode.GetNodeOrNull<Node3D>(socketNodeName);
		if (socketNode == null)
		{
			socketNode = new Node3D { Name = socketNodeName };
			parentNode.AddChild(socketNode);
		}

		_boneAttachments[bone] = socketNode;
		return socketNode;
	}

	private void ClearNodeChildren(Node parentNode)
	{
		foreach (Node child in parentNode.GetChildren())
		{
			parentNode.RemoveChild(child);
			child.QueueFree();
		}
	}

	private void ApplyEffectiveTransformAndMetadata(Node3D model, string attachmentId, Node3D targetParent, Vector3 effectivePos, Vector3 effectiveRot, Vector3 effectiveScaleVec, float effectiveNormalOffset)
	{
		if (effectiveNormalOffset != 0.0f) effectivePos += Vector3.Up * effectiveNormalOffset;

		model.Position = effectivePos;
		model.RotationDegrees = effectiveRot;
		model.Scale = effectiveScaleVec;

		string cleanAttId = GetCleanAttachmentId(attachmentId);
		model.Name = $"Att_{cleanAttId}";
		model.SetMeta("AttachmentId", attachmentId);
		model.SetMeta("CleanAttachmentId", cleanAttId);

		targetParent.AddChild(model);

		ApplyModelShaders(model, attachmentId);
	}

	private void ApplySocketAttachmentInstance(Node3D model, string attachmentId, Node3D attachTarget, Node3D parentNode, string? parentAttachmentId, HumanoidBone bone, float defScale, Vector3 defPos, Vector3 defRot, Vector3? posOffsetOverride, Vector3? rotOffsetOverride, float? scaleOverride, Vector3? scaleVectorOverride, float? normalOffsetOverride)
	{
		float effectiveScale = scaleOverride ?? defScale;
		Vector3 effectivePos = posOffsetOverride ?? defPos;
		Vector3 effectiveRot = rotOffsetOverride ?? defRot;
		Vector3 effectiveScaleVec = scaleVectorOverride ?? (Vector3.One * (effectiveScale <= 0f ? 1.0f : effectiveScale));
		float effectiveNormalOffset = normalOffsetOverride ?? 0.0f;

		ApplyUnitAttachmentOverrides(bone, attachmentId, ref effectivePos, ref effectiveRot, ref effectiveScaleVec, ref effectiveNormalOffset, scaleOverride.HasValue, scaleVectorOverride.HasValue, posOffsetOverride.HasValue, rotOffsetOverride.HasValue, normalOffsetOverride.HasValue);

		Node3D targetParent = ResolveSocketTargetParent(attachTarget, parentNode, parentAttachmentId);
		ApplyEffectiveTransformAndMetadata(model, attachmentId, targetParent, effectivePos, effectiveRot, effectiveScaleVec, effectiveNormalOffset);
	}

	private void ApplyUnitAttachmentOverrides(HumanoidBone bone, string attachmentId, ref Vector3 effectivePos, ref Vector3 effectiveRot, ref Vector3 effectiveScaleVec, ref float effectiveNormalOffset, bool hasScaleOverride, bool hasScaleVecOverride, bool hasPosOverride, bool hasRotOverride, bool hasNormalOverride)
	{
		if (string.IsNullOrEmpty(UnitId)) return;
		if (!GameHost.TryGetUnitOrBuildingMetadata(UnitId, out var uMeta)) return;
		if (!uMeta.TryGetObjectAttachment(bone, attachmentId, out var unitOrient)) return;

		if (!hasScaleOverride && !hasScaleVecOverride) effectiveScaleVec = unitOrient.ScaleVector.ToGodotVector3();
		if (!hasPosOverride) effectivePos = unitOrient.Position.ToGodotVector3();
		if (!hasRotOverride) effectiveRot = unitOrient.RotationDegrees.ToGodotVector3();
		if (!hasNormalOverride) effectiveNormalOffset = unitOrient.NormalOffset;
	}

	private static string GetCleanAttachmentId(string attachmentId)
	{
		if (attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)) return attachmentId;
		return Path.GetFileNameWithoutExtension(attachmentId);
	}

	private Node3D ResolveSocketTargetParent(Node3D attachTarget, Node3D parentNode, string? parentAttachmentId)
	{
		if (string.IsNullOrEmpty(parentAttachmentId)) return attachTarget;
		var parentMesh = FindAttachmentInNode(attachTarget, parentAttachmentId) ?? FindAttachmentInNode(parentNode, parentAttachmentId);
		return parentMesh ?? attachTarget;
	}

	private void ApplyModelShaders(Node3D model, string attachmentId)
	{
		if (model is ProceduralVfxInstance3D) return;
		if (model is BillboardIconAttachment3D) return;
		bool ignorePlayerColor = GameHost.Instance != null && GameHost.Instance.GetModelIgnorePlayerColor(attachmentId);
		ModelShaderManager.ApplyPlayerColorShader(model, PlayerColor, ignorePlayerColor, true, false);
		ModelShaderManager.SetIgnorePlayerColor(model, ignorePlayerColor);
	}


	public virtual void ClearAllAttachments()
	{
		foreach (var kvp in _boneAttachments)
		{
			if (kvp.Value != null && GodotObject.IsInstanceValid(kvp.Value))
			{
				foreach (Node child in kvp.Value.GetChildren())
				{
					kvp.Value.RemoveChild(child);
					child.QueueFree();
				}
			}
		}

		_currentBoneAttachmentIds.Clear();
		_currentRightAttachmentId = null;
		_currentLeftAttachmentId = null;

		foreach (var kvp in _pseudoSockets)
		{
			if (kvp.Value != null && GodotObject.IsInstanceValid(kvp.Value))
			{
				foreach (Node child in kvp.Value.GetChildren())
				{
					kvp.Value.RemoveChild(child);
					child.QueueFree();
				}
			}
		}

		_currentPseudoSocketAttachmentIds.Clear();
		_pseudoSocketGlows.Clear();
		RebuildGeometryInstanceCache();
	}

	public virtual void ApplyAllConfiguredAttachments()
	{
		ClearAllAttachments();

		if (string.IsNullOrEmpty(UnitId) || !GameHost.TryGetUnitOrBuildingMetadata(UnitId, out var uMeta) || uMeta.ObjectAttachments == null)
		{
			return;
		}

		var atts = uMeta.ObjectAttachments;
		var parentNode = (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) ? _modelNode : (Node3D)this;
		var skeleton = FindSkeleton(parentNode);
		bool isNonRigged = IsBuilding || skeleton == null;

		if (isNonRigged)
		{
			ApplyNonRiggedAttachments(atts);
		}
		else
		{
			ApplyRiggedAttachments(atts);
		}
	}

	private void ApplyNonRiggedAttachments(UnitObjectAttachments atts)
	{
		ApplyPseudoSocketAttachmentList("ground", atts.ground ?? atts.root);
		ApplyPseudoSocketAttachmentList("center", atts.center ?? atts.chest);
		ApplyPseudoSocketAttachmentList("overhead", atts.overhead ?? atts.head);
		ApplyPseudoSocketAttachmentList("pivot", atts.pivot ?? atts.right_hand);
		ApplyCustomPseudoSocketAttachmentLists(atts);
	}

	private void ApplyCustomPseudoSocketAttachmentLists(UnitObjectAttachments atts)
	{
		var customLists = atts.CustomSocketLists;
		if (customLists == null) return;

		foreach (var kvp in customLists)
		{
			ApplyPseudoSocketAttachmentList(kvp.Key, kvp.Value);
		}
	}

	private void ApplyRiggedAttachments(UnitObjectAttachments atts)
	{
		ApplyBoneAttachmentList(HumanoidBone.RightHand, atts.right_hand);
		ApplyBoneAttachmentList(HumanoidBone.LeftHand, atts.left_hand);
		ApplyBoneAttachmentList(HumanoidBone.Chest, atts.chest);
		ApplyBoneAttachmentList(HumanoidBone.Hips, atts.root);
		ApplyBoneAttachmentList(HumanoidBone.Head, atts.head);
		ApplyBoneAttachmentList(HumanoidBone.LeftFoot, atts.left_foot);
		ApplyBoneAttachmentList(HumanoidBone.RightFoot, atts.right_foot);

		ApplyPseudoSocketAttachmentList("ground", atts.ground);
		ApplyPseudoSocketAttachmentList("center", atts.center);
		ApplyPseudoSocketAttachmentList("overhead", atts.overhead);
		ApplyPseudoSocketAttachmentList("pivot", atts.pivot);
		ApplyCustomPseudoSocketAttachmentLists(atts);
	}


	private void ApplyBoneAttachmentList(HumanoidBone bone, List<Dictionary<string, HandAttachmentOrientation>>? list)
	{
		if (list == null) return;
		foreach (var dict in list)
		{
			if (dict == null) continue;
			foreach (var kvp in dict)
			{
				if (string.IsNullOrEmpty(kvp.Value.ParentAttachmentId))
				{
					SetSocketAttachment(bone, kvp.Key, kvp.Value.Position.ToGodotVector3(), kvp.Value.RotationDegrees.ToGodotVector3(), kvp.Value.Scale, kvp.Value.ScaleVector.ToGodotVector3(), kvp.Value.NormalOffset, false, null);
				}
			}
		}

		foreach (var dict in list)
		{
			if (dict == null) continue;
			foreach (var kvp in dict)
			{
				if (!string.IsNullOrEmpty(kvp.Value.ParentAttachmentId))
				{
					SetSocketAttachment(bone, kvp.Key, kvp.Value.Position.ToGodotVector3(), kvp.Value.RotationDegrees.ToGodotVector3(), kvp.Value.Scale, kvp.Value.ScaleVector.ToGodotVector3(), kvp.Value.NormalOffset, false, kvp.Value.ParentAttachmentId);
				}
			}
		}
	}

	private void ApplyPseudoSocketAttachmentList(string socket, List<Dictionary<string, HandAttachmentOrientation>>? list)
	{
		if (list == null) return;
		foreach (var dict in list)
		{
			if (dict == null) continue;
			foreach (var kvp in dict)
			{
				if (string.IsNullOrEmpty(kvp.Value.ParentAttachmentId))
				{
					SetPseudoSocketAttachment(socket, kvp.Key, kvp.Value.Position.ToGodotVector3(), kvp.Value.RotationDegrees.ToGodotVector3(), kvp.Value.Scale, kvp.Value.ScaleVector.ToGodotVector3(), kvp.Value.NormalOffset, false, null);
				}
			}
		}

		foreach (var dict in list)
		{
			if (dict == null) continue;
			foreach (var kvp in dict)
			{
				if (!string.IsNullOrEmpty(kvp.Value.ParentAttachmentId))
				{
					SetPseudoSocketAttachment(socket, kvp.Key, kvp.Value.Position.ToGodotVector3(), kvp.Value.RotationDegrees.ToGodotVector3(), kvp.Value.Scale, kvp.Value.ScaleVector.ToGodotVector3(), kvp.Value.NormalOffset, false, kvp.Value.ParentAttachmentId);
				}
			}
		}
	}

	public const string PlayerColorGlowAttachmentId = "glow:player_color";

	private readonly Dictionary<string, Node3D> _pseudoSockets = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _currentPseudoSocketAttachmentIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, MeshInstance3D> _pseudoSocketGlows = new(StringComparer.OrdinalIgnoreCase);
	private IReadOnlyList<PseudoSocketPoint>? _resolvedPseudoSocketPoints;
	private List<PseudoSocketAnchor>? _resolvedAuthorAnchors;

	public Aabb CalculateModelLocalAabb()
	{
		var visual = (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) ? _modelNode : (GetNodeOrNull<Node3D>("VisualModel") ?? this);
		Aabb combinedAabb = new Aabb();
		bool hasAabb = false;

		CalculateCombinedAabb(visual, visual, ref combinedAabb, ref hasAabb);

		if (!hasAabb)
		{
			combinedAabb = new Aabb(new Vector3(-0.5f, 0f, -0.5f), new Vector3(1.0f, 1.8f, 1.0f));
		}

		return combinedAabb;
	}

	private void CalculateCombinedAabb(Node current, Node rootVisual, ref Aabb combinedAabb, ref bool hasAabb)
	{
		UpdateCombinedAabbFromMesh(current, rootVisual, ref combinedAabb, ref hasAabb);

		foreach (Node child in current.GetChildren())
		{
			if (ShouldCalculateChildAabb(child))
			{
				CalculateCombinedAabb(child, rootVisual, ref combinedAabb, ref hasAabb);
			}
		}
	}

	private bool ShouldCalculateChildAabb(Node child)
	{
		if (child is BoneAttachment3D) return false;
		string childName = child.Name.ToString();
		if (childName.StartsWith("PseudoSocket_", StringComparison.OrdinalIgnoreCase)) return false;
		if (childName.StartsWith("SocketAttachment_", StringComparison.OrdinalIgnoreCase)) return false;
		if (childName.StartsWith("Att_", StringComparison.OrdinalIgnoreCase)) return false;
		if (childName.StartsWith("AttVisual_", StringComparison.OrdinalIgnoreCase)) return false;
		return true;
	}

	private void UpdateCombinedAabbFromMesh(Node current, Node rootVisual, ref Aabb combinedAabb, ref bool hasAabb)
	{
		if (current is not MeshInstance3D meshInst) return;
		if (meshInst.Mesh == null || !meshInst.Visible) return;

		Transform3D relXform = CalculateRelativeTransform(meshInst, rootVisual);

		if (Mathf.Abs(relXform.Basis.Determinant()) <= 0.0001f) return;

		Aabb mAabb = meshInst.Mesh.GetAabb();
		Vector3[] corners = Realm.Client.Utils.MathUtils.GetAabbCorners(mAabb);
		for (int i = 0; i < 8; i++)
		{
			Vector3 pt = relXform * corners[i];
			if (!hasAabb)
			{
				combinedAabb = new Aabb(pt, Vector3.Zero);
				hasAabb = true;
			}
			else
			{
				combinedAabb = combinedAabb.Expand(pt);
			}
		}
	}

	private Transform3D CalculateRelativeTransform(Node current, Node rootVisual)
	{
		Transform3D relXform = Transform3D.Identity;
		Node? curr = current;
		while (curr != null && curr != rootVisual)
		{
			if (curr is Node3D n3d) relXform = n3d.Transform * relXform;
			curr = curr.GetParent();
		}

		return relXform;
	}



	public void SetPseudoSocketAttachment(
		string socketName,
		string attachmentId,
		Vector3? posOffsetOverride = null,
		Vector3? rotOffsetOverride = null,
		float? scaleOverride = null,
		Vector3? scaleVectorOverride = null,
		float? normalOffsetOverride = null,
		bool clearExisting = false,
		string? parentAttachmentId = null)
	{
		string normSocket = NormalizeSocketName(socketName);

		if (TryClearPseudoSocketAttachment(normSocket, attachmentId, clearExisting, posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride))
		{
			return;
		}

		var visual = GetVisualModelNode();
		if (PseudoSocketCache.IsRealSocket(normSocket, GetRealSocketIds())) return;

		Node3D? socketNode = EnsurePseudoSocketNode(visual, normSocket);
		if (socketNode == null) return;

		_currentPseudoSocketAttachmentIds[normSocket] = attachmentId;

		PseudoSocketPoint socketPoint = ResolvePseudoSocketPoint(visual, normSocket);
		socketNode.Position = socketPoint.LocalPosition;
		socketNode.RotationDegrees = socketPoint.LocalRotationDegrees;

		if (clearExisting) ClearNodeChildren(socketNode);

		if (IsPlayerColorGlowAttachment(attachmentId))
		{
			AttachPlayerColorGlowToPseudoSocket(socketNode, normSocket);
			return;
		}

		Node3D? model = ResolveAndInstantiateAttachment(attachmentId, out float defScale, out Vector3 defPos, out Vector3 defRot);
		if (model == null) return;

		ApplyPseudoSocketInstance(model, attachmentId, socketNode, visual, parentAttachmentId, normSocket, defScale, defPos, defRot, posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride);
	}

	public bool SetBillboardIconVisible(string attachmentId, bool visible)
	{
		if (string.IsNullOrEmpty(attachmentId)) return false;
		if (!GameHost.AttachmentRegistry.TryGetValue(attachmentId, out var attachmentMetadata)) return false;

		var iconConfig = attachmentMetadata.BillboardIcon;
		if (iconConfig == null) return false;

		string socket = string.IsNullOrWhiteSpace(iconConfig.Socket) ? "Overhead" : iconConfig.Socket;
		if (visible)
		{
			SetPseudoSocketAttachment(socket, attachmentId, iconConfig.PositionOffset.ToGodotVector3(), Vector3.Zero, null, null, null, true);
			return true;
		}

		SetPseudoSocketAttachment(socket, string.Empty, null, null, null, null, null, true);
		return true;
	}

	public static bool IsPlayerColorGlowAttachment(string attachmentId)
	{
		return string.Equals(attachmentId, PlayerColorGlowAttachmentId, StringComparison.OrdinalIgnoreCase);
	}

	private void AttachPlayerColorGlowToPseudoSocket(Node3D socketNode, string normSocket)
	{
		if (_pseudoSocketGlows.TryGetValue(normSocket, out var existing) && GodotObject.IsInstanceValid(existing))
		{
			existing.GetParent()?.RemoveChild(existing);
			existing.QueueFree();
			_pseudoSocketGlows.Remove(normSocket);
		}

		float radius = Mathf.Max(0.4f, GetBaseObstacleRadius());
		var glow = new MeshInstance3D
		{
			Name = $"PlayerColorGlow_{normSocket}",
			Mesh = VisualSelectionHelper.GetOrCreatePlayerColorGlowMesh(radius),
			MaterialOverride = SharedMaterials.GetOrCreatePlayerColorGlowMaterial(PlayerColor),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled
		};

		socketNode.AddChild(glow);
		_pseudoSocketGlows[normSocket] = glow;
	}

	private IReadOnlyList<string>? GetRealSocketIds()
	{
		return FindSkeleton(GetParentModelNode()) != null ? PseudoSocketCache.HumanoidSocketIds : null;
	}

	private List<PseudoSocketAnchor>? GetAuthorPseudoSockets()
	{
		if (string.IsNullOrEmpty(UnitId)) return null;

		if (GameHost.TryGetUnitOrBuildingMetadata(UnitId, out var meta) && meta.PseudoSockets != null)
		{
			return meta.PseudoSockets;
		}

		var registryService = GameContext.RegistryService;
		if (registryService.PropRegistry != null && registryService.PropRegistry.TryGetValue(UnitId, out var propMeta))
		{
			return propMeta.PseudoSockets;
		}

		if (registryService.ResourceRegistry != null && registryService.ResourceRegistry.TryGetValue(UnitId, out var resourceMeta))
		{
			return resourceMeta.PseudoSockets;
		}

		return null;
	}

	private IReadOnlyList<PseudoSocketPoint> GetPseudoSocketPoints(Node3D visual)
	{
		var authorAnchors = GetAuthorPseudoSockets();
		if (_resolvedPseudoSocketPoints != null && ReferenceEquals(_resolvedAuthorAnchors, authorAnchors))
		{
			return _resolvedPseudoSocketPoints;
		}

		_resolvedAuthorAnchors = authorAnchors;
		_resolvedPseudoSocketPoints = PseudoSocketCache.GetSockets(visual, ModelPath, authorAnchors, GetRealSocketIds());
		return _resolvedPseudoSocketPoints;
	}

	private PseudoSocketPoint ResolvePseudoSocketPoint(Node3D visual, string normSocket)
	{
		var points = GetPseudoSocketPoints(visual);
		string canonicalId = PseudoSocketCache.NormalizeSocketId(normSocket) ?? normSocket;

		for (int index = 0; index < points.Count; index++)
		{
			if (points[index].SocketId.Equals(canonicalId, StringComparison.OrdinalIgnoreCase))
			{
				return points[index];
			}
		}

		Aabb aabb = CalculateModelLocalAabb();
		bool isNonRigged = IsBuilding || FindSkeleton(GetParentModelNode()) == null;
		return new PseudoSocketPoint(canonicalId, canonicalId, CalculateLegacyPseudoSocketAnchorPos(canonicalId, aabb, isNonRigged), Vector3.Zero, PseudoSocketSource.Generated);
	}

	private static string NormalizeSocketName(string socketName)
	{
		return (socketName ?? "ground").ToLowerInvariant().Replace("_", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "", StringComparison.OrdinalIgnoreCase);
	}

	private Node3D GetVisualModelNode()
	{
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) return _modelNode;
		return GetNodeOrNull<Node3D>("VisualModel") ?? this;
	}

	private Node3D GetParentModelNode()
	{
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) return _modelNode;
		return this;
	}

	private bool TryClearPseudoSocketAttachment(string normSocket, string attachmentId, bool clearExisting, Vector3? posOffsetOverride, Vector3? rotOffsetOverride, float? scaleOverride, Vector3? scaleVectorOverride, float? normalOffsetOverride)
	{
		if (IsAttachmentIdEmptyOrNone(attachmentId))
		{
			ClearEmptyPseudoSocketAttachment(normSocket);
			return true;
		}

		return ShouldClearExistingPseudoSocket(normSocket, attachmentId, clearExisting, posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride);
	}

	private void ClearEmptyPseudoSocketAttachment(string normSocket)
	{
		_currentPseudoSocketAttachmentIds.Remove(normSocket);
		if (_pseudoSockets.TryGetValue(normSocket, out var existing) && GodotObject.IsInstanceValid(existing))
		{
			ClearNodeChildren(existing);
		}
	}

	private bool ShouldClearExistingPseudoSocket(string normSocket, string attachmentId, bool clearExisting, Vector3? posOffsetOverride, Vector3? rotOffsetOverride, float? scaleOverride, Vector3? scaleVectorOverride, float? normalOffsetOverride)
	{
		if (!clearExisting) return false;
		if (!_currentPseudoSocketAttachmentIds.TryGetValue(normSocket, out var currentId) || currentId != attachmentId) return false;
		if (!_pseudoSockets.TryGetValue(normSocket, out var currentAttachment)) return false;
		if (!GodotObject.IsInstanceValid(currentAttachment)) return false;
		if (currentAttachment.GetChildCount() <= 0) return false;
		if (HasAnyOverrides(posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride)) return false;

		return true;
	}

	private Node3D? EnsurePseudoSocketNode(Node3D visual, string normSocket)
	{
		if (PseudoSocketCache.IsRealSocket(normSocket, GetRealSocketIds())) return null;

		string nodeName = $"PseudoSocket_{normSocket}";
		var socketNode = visual.GetNodeOrNull<Node3D>(nodeName);
		if (socketNode == null)
		{
			socketNode = new Node3D { Name = nodeName };
			visual.AddChild(socketNode);
		}

		_pseudoSockets[normSocket] = socketNode;
		return socketNode;
	}

	private static Vector3 CalculateLegacyPseudoSocketAnchorPos(string normSocket, Aabb aabb, bool isNonRigged)
	{
		if (isNonRigged)
		{
			return normSocket switch
			{
				"center" or "centerofmass" => aabb.GetCenter(),
				"top" or "overhead" or "roof" => new Vector3(aabb.GetCenter().X, aabb.End.Y, aabb.GetCenter().Z),
				"base" or "ground" or "footprint" => new Vector3(aabb.GetCenter().X, aabb.Position.Y, aabb.GetCenter().Z),
				"pivot" or "origin" => Vector3.Zero,
				_ => Vector3.Zero
			};
		}

		return normSocket switch
		{
			"ground" or "footprint" or "base" => new Vector3(0, aabb.Position.Y, 0),
			"center" or "centerofmass" => new Vector3(0, aabb.GetCenter().Y, 0),
			"overhead" or "crown" or "top" or "roof" => new Vector3(0, aabb.End.Y + 0.3f, 0),
			"pivot" or "origin" => Vector3.Zero,
			_ => Vector3.Zero
		};
	}

	private void ApplyPseudoSocketInstance(Node3D model, string attachmentId, Node3D socketNode, Node3D visual, string? parentAttachmentId, string normSocket, float defScale, Vector3 defPos, Vector3 defRot, Vector3? posOffsetOverride, Vector3? rotOffsetOverride, float? scaleOverride, Vector3? scaleVectorOverride, float? normalOffsetOverride)
	{
		float effectiveScale = scaleOverride ?? defScale;
		Vector3 effectivePos = posOffsetOverride ?? defPos;
		Vector3 effectiveRot = rotOffsetOverride ?? defRot;
		Vector3 effectiveScaleVec = scaleVectorOverride ?? (Vector3.One * (effectiveScale <= 0f ? 1.0f : effectiveScale));
		float effectiveNormalOffset = normalOffsetOverride ?? 0.0f;

		ApplyPseudoSocketUnitOverrides(normSocket, attachmentId, ref effectivePos, ref effectiveRot, ref effectiveScaleVec, ref effectiveNormalOffset, scaleOverride.HasValue, scaleVectorOverride.HasValue, posOffsetOverride.HasValue, rotOffsetOverride.HasValue, normalOffsetOverride.HasValue);

		Node3D targetParent = ResolvePseudoSocketTargetParent(socketNode, visual, parentAttachmentId);
		ApplyEffectiveTransformAndMetadata(model, attachmentId, targetParent, effectivePos, effectiveRot, effectiveScaleVec, effectiveNormalOffset);
	}

	private void ApplyPseudoSocketUnitOverrides(string normSocket, string attachmentId, ref Vector3 effectivePos, ref Vector3 effectiveRot, ref Vector3 effectiveScaleVec, ref float effectiveNormalOffset, bool hasScaleOverride, bool hasScaleVecOverride, bool hasPosOverride, bool hasRotOverride, bool hasNormalOverride)
	{
		if (string.IsNullOrEmpty(UnitId)) return;
		if (!GameHost.TryGetUnitOrBuildingMetadata(UnitId, out var uMeta)) return;
		if (!uMeta.TryGetObjectAttachment(normSocket, attachmentId, out var unitOrient)) return;

		if (!hasScaleOverride && !hasScaleVecOverride) effectiveScaleVec = unitOrient.ScaleVector.ToGodotVector3();
		if (!hasPosOverride) effectivePos = unitOrient.Position.ToGodotVector3();
		if (!hasRotOverride) effectiveRot = unitOrient.RotationDegrees.ToGodotVector3();
		if (!hasNormalOverride) effectiveNormalOffset = unitOrient.NormalOffset;
	}

	private Node3D ResolvePseudoSocketTargetParent(Node3D socketNode, Node3D visual, string? parentAttachmentId)
	{
		if (string.IsNullOrEmpty(parentAttachmentId)) return socketNode;
		var parentMesh = FindAttachmentInNode(socketNode, parentAttachmentId) ?? FindAttachmentInNode(visual, parentAttachmentId);
		return parentMesh ?? socketNode;
	}


	public static Node3D? FindAttachmentInNode(Node root, string attachmentId)
	{
		if (root == null || !GodotObject.IsInstanceValid(root)) return null;
		if (string.IsNullOrEmpty(attachmentId)) return null;

		string cleanAttId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? attachmentId : Path.GetFileNameWithoutExtension(attachmentId);

		foreach (Node child in root.GetChildren())
		{
			if (!GodotObject.IsInstanceValid(child) || child.IsQueuedForDeletion()) continue;

			if (child is Node3D node3D)
			{
				Node3D? found = CheckAttachmentNode(node3D, attachmentId, cleanAttId);
				if (found != null) return found;
			}
		}

		return null;
	}

	private static Node3D? CheckAttachmentNode(Node3D node3D, string attachmentId, string cleanAttId)
	{
		if (node3D.HasMeta("AttachmentId") && string.Equals(node3D.GetMeta("AttachmentId").AsString(), attachmentId, StringComparison.OrdinalIgnoreCase)) return node3D;
		if (node3D.HasMeta("CleanAttachmentId") && string.Equals(node3D.GetMeta("CleanAttachmentId").AsString(), cleanAttId, StringComparison.OrdinalIgnoreCase)) return node3D;
		if (string.Equals(node3D.Name.ToString(), $"Att_{cleanAttId}", StringComparison.OrdinalIgnoreCase)) return node3D;

		return FindAttachmentInNode(node3D, attachmentId);
	}


	public static Node3D? ResolveAndInstantiateAttachment(string attachmentId, out float defaultScale, out Vector3 defaultPos, out Vector3 defaultRot)
	{
		defaultScale = 1.0f;
		defaultPos = Vector3.Zero;
		defaultRot = Vector3.Zero;

		if (string.IsNullOrEmpty(attachmentId)) return null;

		if (TryResolveBillboardIconAttachment(attachmentId, ref defaultScale, ref defaultPos, ref defaultRot, out var billboardIcon)) return billboardIcon;

		var vfxInstance = ResolveVfxAttachment(attachmentId, ref defaultScale, ref defaultPos, ref defaultRot);
		if (vfxInstance != null) return vfxInstance;

		var loadedNode = ResolveModelAttachment(attachmentId, ref defaultScale, ref defaultPos, ref defaultRot, out var attMeta);
		if (loadedNode != null)
		{
			if (attMeta != null && !string.IsNullOrEmpty(attMeta.ChildVfxId))
			{
				ApplyChildVfx(loadedNode, attMeta);
			}

			return loadedNode;
		}

		return null;
	}

	private static bool TryResolveBillboardIconAttachment(
		string attachmentId,
		ref float defaultScale,
		ref Vector3 defaultPos,
		ref Vector3 defaultRot,
		out Node3D? billboardIcon)
	{
		billboardIcon = null;
		if (!GameHost.AttachmentRegistry.TryGetValue(attachmentId, out var attachmentMetadata)) return false;
		if (attachmentMetadata.BillboardIcon == null || string.IsNullOrWhiteSpace(attachmentMetadata.BillboardIcon.TexturePath)) return false;

		var createdIcon = BillboardIconAttachment3D.TryCreate(attachmentMetadata.BillboardIcon);
		if (createdIcon == null) return false;

		defaultScale = attachmentMetadata.Scale <= 0f ? 1.0f : attachmentMetadata.Scale;
		defaultPos = attachmentMetadata.PositionOffset.ToGodotVector3() + createdIcon.ResolvePositionOffset();
		defaultRot = Vector3.Zero;
		createdIcon.SetMeta("BillboardSocket", createdIcon.ResolveSocketId());
		billboardIcon = createdIcon;
		return true;
	}

	private static ProceduralVfxInstance3D? ResolveVfxAttachment(string attachmentId, ref float defaultScale, ref Vector3 defaultPos, ref Vector3 defaultRot)
	{
		if (attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase))
		{
			string vfxKey = attachmentId.Substring(4);
			VfxAttachmentConfig config;
			if (GameContext.RegistryService.VfxRegistry.TryGetValue(vfxKey, out var regCfg)) config = regCfg.Clone();
			else if (Enum.TryParse<VfxPrimitiveType>(vfxKey, true, out var primType)) config = new VfxAttachmentConfig { VfxId = vfxKey, PrimitiveType = primType };
			else config = new VfxAttachmentConfig { VfxId = vfxKey, Name = vfxKey };

			defaultScale = 1.0f;
			defaultPos = config.PositionOffset.ToGodotVector3();
			defaultRot = config.RotationOffset.ToGodotVector3();
			return new ProceduralVfxInstance3D(config);
		}

		if (GameContext.RegistryService.VfxRegistry.TryGetValue(attachmentId, out var vfxCfg))
		{
			defaultScale = 1.0f;
			defaultPos = vfxCfg.PositionOffset.ToGodotVector3();
			defaultRot = vfxCfg.RotationOffset.ToGodotVector3();
			return new ProceduralVfxInstance3D(vfxCfg.Clone());
		}

		if (Enum.TryParse<VfxPrimitiveType>(attachmentId, true, out var parsedPrim))
		{
			var cfg = new VfxAttachmentConfig { VfxId = attachmentId, PrimitiveType = parsedPrim };
			defaultScale = 1.0f;
			defaultPos = cfg.PositionOffset.ToGodotVector3();
			defaultRot = cfg.RotationOffset.ToGodotVector3();
			return new ProceduralVfxInstance3D(cfg);
		}

		return null;
	}

	private static Node3D? ResolveModelAttachment(string attachmentId, ref float defaultScale, ref Vector3 defaultPos, ref Vector3 defaultRot, out AttachmentMetadata? attMeta)
	{
		attMeta = null;

		if (TryResolveFromAttachmentRegistry(attachmentId, ref defaultScale, ref defaultPos, ref defaultRot, out attMeta, out string modelPath))
		{
			return LoadModelAttachment(modelPath, attachmentId);
		}

		if (TryResolveFromPropRegistry(attachmentId, ref defaultScale, ref defaultPos, out modelPath))
		{
			return LoadModelAttachment(modelPath, attachmentId);
		}

		if (TryResolveFromResourceRegistry(attachmentId, ref defaultScale, ref defaultPos, out modelPath))
		{
			return LoadModelAttachment(modelPath, attachmentId);
		}

		return LoadModelAttachment(string.Empty, attachmentId);
	}

	private static bool TryResolveFromAttachmentRegistry(string attachmentId, ref float defaultScale, ref Vector3 defaultPos, ref Vector3 defaultRot, out AttachmentMetadata? attMeta, out string modelPath)
	{
		attMeta = null;
		modelPath = string.Empty;
		if (!GameHost.AttachmentRegistry.TryGetValue(attachmentId, out var meta)) return false;

		attMeta = meta;
		modelPath = meta.ModelPath;
		defaultScale = meta.Scale <= 0f ? 1.0f : meta.Scale;
		defaultPos = meta.PositionOffset.ToGodotVector3();
		defaultRot = meta.RotationOffset.ToGodotVector3();
		return true;
	}

	private static bool TryResolveFromPropRegistry(string attachmentId, ref float defaultScale, ref Vector3 defaultPos, out string modelPath)
	{
		modelPath = string.Empty;
		if (!GameHost.PropRegistry.TryGetValue(attachmentId, out var propMeta)) return false;
		if (string.IsNullOrEmpty(propMeta.ModelPath)) return false;

		modelPath = propMeta.ModelPath;
		defaultScale = propMeta.Scale <= 0f ? 1.0f : propMeta.Scale;
		defaultPos = new Vector3(0f, propMeta.YOffset, 0f);
		return true;
	}

	private static bool TryResolveFromResourceRegistry(string attachmentId, ref float defaultScale, ref Vector3 defaultPos, out string modelPath)
	{
		modelPath = string.Empty;
		if (!GameHost.ResourceRegistry.TryGetValue(attachmentId, out var resMeta)) return false;
		if (string.IsNullOrEmpty(resMeta.ModelPath)) return false;

		modelPath = resMeta.ModelPath;
		defaultScale = resMeta.Scale <= 0f ? 1.0f : resMeta.Scale;
		defaultPos = new Vector3(0f, resMeta.YOffset, 0f);
		return true;
	}

	private static Node3D? LoadModelAttachment(string modelPath, string attachmentId)
	{
		Node? loaded = !string.IsNullOrEmpty(modelPath) ? ModelCache.GetModel(modelPath) : null;
		if (loaded == null) loaded = ModelCache.GetModel(attachmentId);
		if (loaded is Node3D n) return n;
		if (loaded != null) loaded.QueueFree();
		return null;
	}

	private static void ApplyChildVfx(Node3D loadedNode, AttachmentMetadata attMeta)
	{
		var childVfx = ResolveAndInstantiateAttachment(attMeta.ChildVfxId, out _, out _, out _);
		if (childVfx != null)
		{
			childVfx.Position = attMeta.ChildVfxPosition.ToGodotVector3();
			childVfx.RotationDegrees = attMeta.ChildVfxRotation.ToGodotVector3();
			var childScale = attMeta.ChildVfxScale.ToGodotVector3();
			childVfx.Scale = childScale == Vector3.Zero ? Vector3.One : childScale;
			string cleanChildId = attMeta.ChildVfxId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? attMeta.ChildVfxId : Path.GetFileNameWithoutExtension(attMeta.ChildVfxId);
			childVfx.Name = $"ChildVfx_{cleanChildId}";
			childVfx.SetMeta("AttachmentId", attMeta.ChildVfxId);
			childVfx.SetMeta("CleanAttachmentId", cleanChildId);
			loadedNode.AddChild(childVfx);
		}
	}


	public void UpdateHandAttachmentsForAnimation(string animName, string resolvedName)
	{
		if (string.IsNullOrEmpty(UnitId) || !GameHost.UnitRegistry.TryGetValue(UnitId, out var uMeta) || uMeta.Animations == null || uMeta.Animations.Count == 0)
		{
			return;
		}

		ParseAnimationName(animName, out string actionType, out int variantIndex);

		UnitAnimationEntry? matchedEntry = FindExactAnimationEntry(uMeta, actionType, variantIndex);

		if (!matchedEntry.HasValue)
		{
			matchedEntry = FindFallbackAnimationEntry(uMeta, animName, resolvedName);
		}

		if (matchedEntry.HasValue)
		{
			if (!string.IsNullOrEmpty(matchedEntry.Value.RightHandAttachment)) SetHandAttachment(HumanoidBone.RightHand, matchedEntry.Value.RightHandAttachment);
			if (!string.IsNullOrEmpty(matchedEntry.Value.LeftHandAttachment)) SetHandAttachment(HumanoidBone.LeftHand, matchedEntry.Value.LeftHandAttachment);
		}
	}

	private void ParseAnimationName(string animName, out string actionType, out int variantIndex)
	{
		actionType = animName;
		variantIndex = 0;
		int underscoreIdx = animName.LastIndexOf('_');
		if (underscoreIdx > 0 && int.TryParse(animName.Substring(underscoreIdx + 1), out int parsedIdx))
		{
			actionType = animName.Substring(0, underscoreIdx);
			variantIndex = parsedIdx;
		}
	}

	private UnitAnimationEntry? FindExactAnimationEntry(UnitMetadata uMeta, string actionType, int variantIndex)
	{
		foreach (var kvp in uMeta.Animations)
		{
			if (kvp.Key.Equals(actionType, StringComparison.OrdinalIgnoreCase))
			{
				if (kvp.Value != null && kvp.Value.Count > 0)
				{
					int clampedIndex = Math.Clamp(variantIndex, 0, kvp.Value.Count - 1);
					return kvp.Value[clampedIndex];
				}

				break;
			}
		}

		return null;
	}

	private UnitAnimationEntry? FindFallbackAnimationEntry(UnitMetadata uMeta, string animName, string resolvedName)
	{
		foreach (var kvp in uMeta.Animations)
		{
			if (kvp.Value == null) continue;

			for (int i = 0; i < kvp.Value.Count; i++)
			{
				var entry = kvp.Value[i];
				if (!string.IsNullOrEmpty(entry.Animation) && (entry.Animation.Equals(animName, StringComparison.OrdinalIgnoreCase) || entry.Animation.Equals(resolvedName, StringComparison.OrdinalIgnoreCase)))
				{
					return entry;
				}
			}
		}

		return null;
	}


	private StringName? ResolveRandomAnimationVariant(string animName, string[] animations)
	{
		var variants = new List<StringName>();
		string prefix = $"{animName}_";
		foreach (var name in animations)
		{
			string nameStr = name.ToString();
			if (nameStr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				variants.Add(name);
			}
		}

		if (variants.Count > 0)
		{
			int randIdx = Random.Shared.Next(variants.Count);
			return variants[randIdx];
		}

		return null;
	}

	private StringName? TryRetargetAnimation(string animName, StringName direct)
	{
		string resolvedPath = AnimationRetargetingService.ResolveAnimationFilePath(animName, UnitId);
		if (!string.IsNullOrEmpty(resolvedPath))
		{
			if (TryBindRetargetedAnimation(resolvedPath, animName)) return direct;
		}

		var fallbackAnim = GetFallbackAnimationData(animName);
		if (fallbackAnim != null && _modelNode != null)
		{
			if (AnimationRetargetingService.RetargetAndBind(fallbackAnim, _modelNode, animName, out _))
			{
				return direct;
			}
		}

		return null;
	}

	private bool TryBindRetargetedAnimation(string resolvedPath, string animName)
	{
		var animData = AnimationRetargetingService.GetOrLoadRanimData(resolvedPath);
		if (animData != null && _modelNode != null)
		{
			if (AnimationRetargetingService.RetargetAndBind(animData, _modelNode, animName, out _))
			{
				return true;
			}
		}

		return false;
	}

	private RealmAnimationData? GetFallbackAnimationData(RealmAnimationType animType)
	{
		return AnimationRetargetingService.GetFallbackAnimationData(animType, UnitId);
	}

	private RealmAnimationData? GetFallbackAnimationData(string animName)
	{
		return AnimationRetargetingService.GetFallbackAnimationData(animName, UnitId);
	}


	private StringName ResolveAnimationName(string animName)
	{
		if (_animationPlayer == null) return null;
		if (_resolvedAnimCache.TryGetValue(animName, out var cached)) return cached;

		var animations = _animationPlayer.GetAnimationList();
		StringName? result = null;

		if (!animName.Contains('_'))
		{
			var randomVariant = ResolveRandomAnimationVariant(animName, animations);
			if (randomVariant != null) result = randomVariant;
		}

		if (result == null)
		{
			StringName direct = new StringName(animName);
			if (_animationPlayer.HasAnimation(direct))
			{
				result = direct;
			}
			else
			{
				foreach (var name in animations)
				{
					if (name.ToString().Equals(animName, StringComparison.OrdinalIgnoreCase))
					{
						result = name;
						break;
					}
				}

				if (result == null)
				{
					var retargeted = TryRetargetAnimation(animName, direct);
					if (retargeted != null) result = retargeted;
					else if (animations.Length > 0) result = animations[0];
				}
			}
		}

		_resolvedAnimCache[animName] = result;
		return result;
	}

	public void ApplyModelTint(Color color)
	{
		SetPlayerColor(color);
	}


	protected Color GetSelectionRingColor()
	{
		return new Color(0.22f, 0.54f, 0.26f);
	}

	public static float GetMinY(Node node, Transform3D currentTransform = default)
	{
		if (currentTransform == default) currentTransform = Transform3D.Identity;
		float minY = float.MaxValue;
		bool foundMesh = false;
		GetMinYRecursive(node, currentTransform, ref minY, ref foundMesh);
		return foundMesh ? minY : 0f;
	}

	public static void GetMinYRecursive(Node node, Transform3D currentTransform, ref float minY, ref bool foundMesh)
	{
		if (node is MeshInstance3D meshInstance)
		{
			var mesh = meshInstance.Mesh;
			if (mesh != null)
			{
				var localAabb = mesh.GetAabb();

				Vector3[] corners = new Vector3[8]
				{
					new Vector3(localAabb.Position.X, localAabb.Position.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X, localAabb.Position.Y, localAabb.Position.Z + localAabb.Size.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y, localAabb.Position.Z + localAabb.Size.Z),
					new Vector3(localAabb.Position.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z + localAabb.Size.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z + localAabb.Size.Z)
				};

				foreach (var corner in corners)
				{
					Vector3 modelSpaceCorner = currentTransform * corner;
					if (!foundMesh || modelSpaceCorner.Y < minY)
					{
						minY = modelSpaceCorner.Y;
						foundMesh = true;
					}
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node3D child3D)
			{
				GetMinYRecursive(child, currentTransform * child3D.Transform, ref minY, ref foundMesh);
			}
			else
			{
				GetMinYRecursive(child, currentTransform, ref minY, ref foundMesh);
			}
		}
	}

	private Vector3 GetEcsOrLocalPosition()
	{
		if (GameHost.Instance != null && GameContext.World != null && Entity != default && GameContext.World.IsAlive(Entity) && GameContext.World.Has<Position>(Entity))
		{
			var p = GameContext.World.Get<Position>(Entity).Value;
			return new Vector3(p.X, p.Y, p.Z);
		}

		return Position;
	}

	private void GetRemainingRallyPoints(List<Vector3> points)
	{
		points.Clear();
		if (GameHost.Instance == null || !GameContext.World.IsAlive(Entity)) return;

		var world = GameContext.World;
		Vector3 currentPos = GetEcsOrLocalPosition();
		points.Add(currentPos);

		if (world.Has<RallyPoint>(Entity))
		{
			var rp = world.Get<RallyPoint>(Entity);
			for (int i = 0; i < rp.Count; i++)
			{
				points.Add(new Vector3(rp.Waypoints[i].X, rp.Waypoints[i].Y, rp.Waypoints[i].Z));
			}
		}
		else
		{
			points.Add(currentPos + new Vector3(0, 0, 8));
		}
	}

	private void EnsureRallyVisualsContainer()
	{
		if (_rallyVisualsContainer == null)
		{
			_rallyVisualsContainer = new Node3D();
			_rallyVisualsContainer.Name = "RallyVisualsContainer_Node3D";
			_rallyVisualsContainer.TopLevel = true;
			AddChild(_rallyVisualsContainer);
		}
	}

	private MeshInstance3D GetOrCreateRallyMarker(int index)
	{
		while (_rallyMarkersPool.Count <= index)
		{
			var marker = new MeshInstance3D();
			marker.Name = "Marker_MeshInstance3D";
			var cylinderMesh = new CylinderMesh();
			cylinderMesh.TopRadius = 0.25f;
			cylinderMesh.BottomRadius = 0.25f;
			cylinderMesh.Height = 0.05f;
			marker.Mesh = cylinderMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f);
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.95f, 0.82f, 0.55f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			marker.MaterialOverride = mat;

			_rallyVisualsContainer.AddChild(marker);
			_rallyMarkersPool.Add(marker);
		}

		return _rallyMarkersPool[index];
	}

	private MeshInstance3D GetOrCreateRallyLine(int index)
	{
		while (_rallyLinesPool.Count <= index)
		{
			var line = new MeshInstance3D();
			line.Name = "Line_MeshInstance3D";
			var boxMesh = new BoxMesh();
			boxMesh.Size = new Vector3(0.1f, 0.05f, 1.0f);
			line.Mesh = boxMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f, 0.5f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.95f, 0.82f, 0.55f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			line.MaterialOverride = mat;

			_rallyVisualsContainer.AddChild(line);
			_rallyLinesPool.Add(line);
		}

		return _rallyLinesPool[index];
	}

	private void UpdateRallyMarkers(List<Vector3> points, int markerCountNeeded)
	{
		for (int i = 0; i < markerCountNeeded; i++)
		{
			var marker = GetOrCreateRallyMarker(i);
			marker.Visible = true;
			Vector3 pos = points[i + 1];
			if (GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.GetHeightAndNormal(pos.X, pos.Z, out float h, out _);
				pos.Y = h + 0.1f;
			}

			marker.GlobalPosition = pos;
		}

		for (int i = Mathf.Max(0, markerCountNeeded); i < _rallyMarkersPool.Count; i++)
		{
			_rallyMarkersPool[i].Visible = false;
		}
	}

	private void UpdateLineTransform(MeshInstance3D line, Vector3 start, Vector3 end, float width, Color color, float alpha)
	{
		Vector3 diff = end - start;
		float length = diff.Length();

		if (length <= 0.02f)
		{
			line.Visible = false;
			return;
		}

		Vector3 direction = diff.Normalized();
		Vector3 upVector = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
		var basis = Basis.LookingAt(direction, upVector).Scaled(new Vector3(width / 0.1f, 1f, length));
		line.GlobalTransform = new Transform3D(basis, start + diff * 0.5f);

		if (line.MaterialOverride is StandardMaterial3D lineMaterial)
		{
			lineMaterial.AlbedoColor = new Color(color.R, color.G, color.B, alpha * 0.75f);
			lineMaterial.Emission = new Color(color.R, color.G, color.B);
		}
	}

	private void UpdateRallyLines(List<Vector3> points, int lineCountNeeded)
	{
		for (int i = 0; i < lineCountNeeded; i++)
		{
			var line = GetOrCreateRallyLine(i);
			line.Visible = true;

			Vector3 start = SamplePathGround(points[i]);
			Vector3 end = SamplePathGround(points[i + 1]);

			UpdateLineTransform(line, start, end, PathBaseWidth, new Color(0.95f, 0.85f, 0.25f), 0.85f);
		}

		for (int i = lineCountNeeded; i < _rallyLinesPool.Count; i++)
		{
			_rallyLinesPool[i].Visible = false;
		}
	}

	private static bool PointsListEqual(List<Vector3> a, List<Vector3> b)
	{
		if (a.Count != b.Count) return false;
		for (int i = 0; i < a.Count; i++)
		{
			if (a[i].DistanceSquaredTo(b[i]) > 0.0001f) return false;
		}

		return true;
	}

	public void UpdateRallyVisuals()
	{
		if (!IsBuilding || !GameContext.PerspectiveFilter.CanSeeOwnedUi(Player)) return;
		if (GameHost.Instance == null || !GameContext.World.IsAlive(Entity)) return;

		EnsureRallyVisualsContainer();
		if (!GameHost.Instance.CanProduceUnits(this))
		{
			_rallyVisualsContainer.Visible = false;
			return;
		}

		_rallyVisualsContainer.Visible = IsSelected;
		if (!IsSelected) return;

		GetRemainingRallyPoints(_rallyPointsBuffer);

		if (_rallyPointsBuffer.Count <= 1)
		{
			if (_rallyMarker != null) _rallyMarker.Visible = false;
			_lastRallyRenderPos = new Vector3(float.NaN, float.NaN, float.NaN);
			_lastRenderedRallyPoints.Clear();
			return;
		}

		Vector3 currentPos = _rallyPointsBuffer[0];
		bool posUnchanged = !float.IsNaN(_lastRallyRenderPos.X) && currentPos.DistanceSquaredTo(_lastRallyRenderPos) < 0.0025f;
		bool pointsUnchanged = PointsListEqual(_rallyPointsBuffer, _lastRenderedRallyPoints);

		if (posUnchanged && pointsUnchanged) return;

		_lastRallyRenderPos = currentPos;
		_lastRenderedRallyPoints.Clear();
		_lastRenderedRallyPoints.AddRange(_rallyPointsBuffer);

		EnsureRallyMarkerExists();
		_rallyMarker.Visible = true;

		PositionRallyMarker(_rallyPointsBuffer[_rallyPointsBuffer.Count - 1]);

		UpdateRallyMarkers(_rallyPointsBuffer, _rallyPointsBuffer.Count - 2);
		UpdateRallyLines(_rallyPointsBuffer, _rallyPointsBuffer.Count - 1);
	}

	private void EnsureRallyMarkerExists()
	{
		if (_rallyMarker != null) return;

		var cylinderMesh = new CylinderMesh();
		cylinderMesh.TopRadius = 0.1f;
		cylinderMesh.BottomRadius = 0.1f;
		cylinderMesh.Height = 3.0f;
		_rallyMarker = new MeshInstance3D();
		_rallyMarker.Name = "RallyMarker_MeshInstance3D";
		_rallyMarker.Mesh = cylinderMesh;

		var markerMat = new StandardMaterial3D();
		markerMat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f);
		markerMat.EmissionEnabled = true;
		markerMat.Emission = new Color(0.95f, 0.82f, 0.55f);
		_rallyMarker.MaterialOverride = markerMat;

		_rallyVisualsContainer.AddChild(_rallyMarker);
	}

	private void PositionRallyMarker(Vector3 finalPos)
	{
		if (GameHost.Instance.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.GetHeightAndNormal(finalPos.X, finalPos.Z, out float hFinal, out _);
			finalPos.Y = hFinal + 1.5f;
		}

		_rallyMarker.GlobalPosition = finalPos;
	}


	public void UpdateSelectedVisuals(float fDelta)
	{
		if (IsSelected && GameContext.PerspectiveFilter.CanSeeOwnedUi(Player))
		{
			if (IsBuilding)
			{
				UpdateRallyVisuals();
			}
			else
			{
				UpdatePathVisuals();
			}
		}
		else
		{
			HidePathVisuals();
			if (_rallyVisualsContainer != null)
			{
				_rallyVisualsContainer.Visible = false;
			}
		}
	}

	private void EnsurePathVisualsContainer()
	{
		if (_pathVisualsContainer == null)
		{
			_pathVisualsContainer = new Node3D();
			_pathVisualsContainer.Name = "PathVisualsContainer_Node3D";
			_pathVisualsContainer.TopLevel = true;
			AddChild(_pathVisualsContainer);
		}
	}

	public bool IsAttackPath { get; set; } = false;

	private void GetRemainingPathPoints(List<Vector3> points)
	{
		points.Clear();
		IsAttackPath = false;
		if (GameHost.Instance == null || !GameContext.World.IsAlive(Entity)) return;

		var world = GameContext.World;
		if (world.Has<AttackTarget>(Entity) || world.Has<AttackMove>(Entity))
		{
			IsAttackPath = true;
		}

		points.Add(GetEcsOrLocalPosition());

		if (world.Has<PathFollow>(Entity))
		{
			var pf = world.Get<PathFollow>(Entity);
			for (int i = pf.CurrentWaypointIndex; i < pf.WaypointCount; i++)
			{
				points.Add(new Vector3(pf.Waypoints[i].X, pf.Waypoints[i].Y, pf.Waypoints[i].Z));
			}
		}
		else if (world.Has<MoveTo>(Entity))
		{
			var mt = world.Get<MoveTo>(Entity);
			points.Add(new Vector3(mt.Target.X, mt.Target.Y, mt.Target.Z));
		}

		if (world.Has<WaypointQueue>(Entity))
		{
			var q = world.Get<WaypointQueue>(Entity);
			for (int i = 0; i < q.Count; i++)
			{
				points.Add(new Vector3(q.Waypoints[i].X, q.Waypoints[i].Y, q.Waypoints[i].Z));
			}
		}
	}

	private const float PathSampleSpacing = 1.5f;
	private const float PathMarkerSpacing = 4.5f;
	private const float PathGroundOffset = 0.12f;
	private const float PathBaseWidth = 0.14f;
	private const float PathTaperLengthRatio = 0.55f;
	private const float PathFadeLengthRatio = 0.6f;

	private Vector3 SamplePathGround(Vector3 point)
	{
		if (GameHost.Instance?.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.GetHeightAndNormal(point.X, point.Z, out float height, out _);
			return new Vector3(point.X, height + PathGroundOffset, point.Z);
		}

		return new Vector3(point.X, point.Y + PathGroundOffset, point.Z);
	}

	private void BuildSampledPath()
	{
		_pathSampledPoints.Clear();

		if (_pathPointsBuffer.Count <= 1)
			return;

		_pathSampledPoints.Add(SamplePathGround(_pathPointsBuffer[1]));

		for (int segment = 1; segment + 1 < _pathPointsBuffer.Count; segment++)
		{
			Vector3 start = _pathPointsBuffer[segment];
			Vector3 end = _pathPointsBuffer[segment + 1];
			float horizontalLength = new Vector3(end.X - start.X, 0f, end.Z - start.Z).Length();
			int steps = Mathf.Max(1, Mathf.CeilToInt(horizontalLength / PathSampleSpacing));

			for (int step = 1; step <= steps; step++)
			{
				Vector3 point = start.Lerp(end, step / (float)steps);
				_pathSampledPoints.Add(SamplePathGround(point));
			}
		}
	}

	private void UpdatePathVisuals()
	{
		EnsurePathVisualsContainer();
		_pathVisualsContainer.Visible = true;

		GetRemainingPathPoints(_pathPointsBuffer);

		if (_pathPointsBuffer.Count <= 1)
		{
			HidePathVisuals();
			_lastPathRenderIsAttackPath = IsAttackPath;
			_lastRenderedPathPoints.Clear();
			_pathSampledPoints.Clear();
			return;
		}

		bool attackModeUnchanged = IsAttackPath == _lastPathRenderIsAttackPath;
		bool pointsUnchanged = PointsListEqual(_pathPointsBuffer, _lastRenderedPathPoints);

		if (attackModeUnchanged && pointsUnchanged && _pathSampledPoints.Count > 0) return;

		_lastPathRenderPos = _pathPointsBuffer[1];
		_lastPathRenderIsAttackPath = IsAttackPath;
		_lastRenderedPathPoints.Clear();
		_lastRenderedPathPoints.AddRange(_pathPointsBuffer);

		BuildSampledPath();

		Color pathColor = IsAttackPath ? new Color(0.9f, 0.22f, 0.18f) : new Color(0.22f, 0.62f, 1f);
		int sampleCount = _pathSampledPoints.Count;

		float totalLength = 0f;
		for (int i = 1; i < sampleCount; i++)
		{
			totalLength += _pathSampledPoints[i].DistanceTo(_pathSampledPoints[i - 1]);
		}

		int lineIndex = 0;
		float travelled = 0f;
		for (int i = 1; i < sampleCount; i++)
		{
			Vector3 start = _pathSampledPoints[i - 1];
			Vector3 end = _pathSampledPoints[i];
			float segmentLength = start.DistanceTo(end);
			travelled += segmentLength;

			float progress = totalLength > 0.001f ? travelled / totalLength : 0f;
			float width = PathBaseWidth * (1f - PathTaperLengthRatio * progress);
			float alpha = 1f - PathFadeLengthRatio * progress;

			var line = GetOrCreateLine(lineIndex);
			line.Visible = true;
			UpdateLineTransform(line, start, end, width, pathColor, alpha);
			lineIndex++;
		}

		for (int i = lineIndex; i < _pathLinesPool.Count; i++)
		{
			_pathLinesPool[i].Visible = false;
		}

		int markerIndex = 0;
		travelled = 0f;
		float sinceLastMarker = 0f;
		for (int i = 1; i < sampleCount; i++)
		{
			float segmentLength = _pathSampledPoints[i].DistanceTo(_pathSampledPoints[i - 1]);
			travelled += segmentLength;
			sinceLastMarker += segmentLength;

			if (sinceLastMarker < PathMarkerSpacing)
				continue;

			sinceLastMarker = 0f;
			float progress = totalLength > 0.001f ? travelled / totalLength : 0f;
			var marker = GetOrCreateMarker(markerIndex);
			marker.Visible = true;
			marker.GlobalPosition = _pathSampledPoints[i];
			SetMarkerColor(marker, pathColor, 1f - PathFadeLengthRatio * progress);
			markerIndex++;
		}

		for (int i = markerIndex; i < _pathMarkersPool.Count; i++)
		{
			_pathMarkersPool[i].Visible = false;
		}
	}

	private static void SetMarkerColor(MeshInstance3D marker, Color color, float alpha)
	{
		if (marker.MaterialOverride is not StandardMaterial3D markerMaterial) return;

		Color shaded = new Color(color.R, color.G, color.B, alpha);
		markerMaterial.AlbedoColor = shaded;
		markerMaterial.Emission = new Color(color.R, color.G, color.B);
	}

	private void HidePathVisuals()
	{
		if (_pathVisualsContainer != null)
		{
			_pathVisualsContainer.Visible = false;
		}
	}

	private MeshInstance3D GetOrCreateMarker(int index)
	{
		while (_pathMarkersPool.Count <= index)
		{
			var marker = new MeshInstance3D();
			marker.Name = "Marker_MeshInstance3D";
			var cylinderMesh = new CylinderMesh();
			cylinderMesh.TopRadius = 0.25f;
			cylinderMesh.BottomRadius = 0.25f;
			cylinderMesh.Height = 0.05f;
			marker.Mesh = cylinderMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.2f, 0.6f, 1.0f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.2f, 0.6f, 1.0f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			marker.MaterialOverride = mat;

			_pathVisualsContainer.AddChild(marker);
			_pathMarkersPool.Add(marker);
		}

		var m = _pathMarkersPool[index];
		if (m.MaterialOverride is StandardMaterial3D markerMat)
		{
			Color c = IsAttackPath ? new Color(0.9f, 0.1f, 0.1f) : new Color(0.2f, 0.6f, 1.0f);
			markerMat.AlbedoColor = c;
			markerMat.Emission = c;
		}

		return m;
	}

	private MeshInstance3D GetOrCreateLine(int index)
	{
		while (_pathLinesPool.Count <= index)
		{
			var line = new MeshInstance3D();
			line.Name = "Line_MeshInstance3D";
			var boxMesh = new BoxMesh();
			boxMesh.Size = new Vector3(0.1f, 0.05f, 1.0f);
			line.Mesh = boxMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.2f, 0.6f, 1.0f, 0.5f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.2f, 0.6f, 1.0f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			line.MaterialOverride = mat;

			_pathVisualsContainer.AddChild(line);
			_pathLinesPool.Add(line);
		}

		var l = _pathLinesPool[index];
		if (l.MaterialOverride is StandardMaterial3D lineMat)
		{
			Color c = IsAttackPath ? new Color(0.9f, 0.1f, 0.1f, 0.5f) : new Color(0.2f, 0.6f, 1.0f, 0.5f);
			Color emissionColor = IsAttackPath ? new Color(0.9f, 0.1f, 0.1f) : new Color(0.2f, 0.6f, 1.0f);
			lineMat.AlbedoColor = c;
			lineMat.Emission = emissionColor;
		}

		return l;
	}

	public Color Modulate
	{
		get => new Color(1f, 1f, 1f, _modulateAlpha);
		set
		{
			if (Mathf.Abs(_modulateAlpha - value.A) < 0.005f) return;
			_modulateAlpha = value.A;
			if (_modelNode != null)
				SetAlphaRecursive(_modelNode, value.A);
		}
	}

	private float _modulateAlpha = 1f;

	public void SetConstructionAlpha(float alpha)
	{
		if (Mathf.Abs(_modulateAlpha - alpha) < 0.005f) return;
		_modulateAlpha = alpha;
		if (_modelNode != null)
		{
			SetAlphaRecursive(_modelNode, alpha);
		}
	}

	private void SetAlphaRecursive(Node node, float alpha)
	{
		if (node is MeshInstance3D mesh)
		{
			Material mat = mesh.MaterialOverride;
			if (mat is StandardMaterial3D stdMat)
			{
				if (!stdMat.HasMeta("AlphaDuplicated"))
				{
					stdMat = (StandardMaterial3D)stdMat.Duplicate();
					stdMat.SetMeta("AlphaDuplicated", true);
					mesh.MaterialOverride = stdMat;
				}
				stdMat.Transparency = alpha < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
				var c = stdMat.AlbedoColor;
				c.A = alpha;
				stdMat.AlbedoColor = c;
			}
		}

		int childCount = node.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			SetAlphaRecursive(node.GetChild(i), alpha);
		}
	}
}