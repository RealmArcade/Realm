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
		if (_isRuinSilhouette) ModelShaderManager.SetRuinSilhouette(root, true);
		if (_isConstructionBlocked) ModelShaderManager.SetSilhouetteTint(root, ModelShaderManager.BlockedSilhouetteTint);
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

	private bool _isRuinSilhouette = false;

	private Tween? _activeTransitionTween;

	public bool HasPlayedSpawnShader { get; private set; }

	public bool HasPlayedDespawnShader { get; private set; }

	public bool TryPlaySpawnShader(string shaderKey)
	{
		if (HasPlayedSpawnShader) return false;
		HasPlayedSpawnShader = true;

		CancelActiveTransitionTween();
		_activeTransitionTween = SpawnDeathShaderManager.AnimateTransition(this, shaderKey, true, null, () => _activeTransitionTween = null);
		return true;
	}

	public bool TryPlayDespawnShader(string shaderKey, Action onComplete)
	{
		if (HasPlayedDespawnShader) return false;
		HasPlayedDespawnShader = true;

		CancelActiveTransitionTween();
		_activeTransitionTween = SpawnDeathShaderManager.AnimateTransition(this, shaderKey, false, null, () =>
		{
			_activeTransitionTween = null;
			onComplete();
		});
		return true;
	}

	private void CancelActiveTransitionTween()
	{
		if (_activeTransitionTween == null) return;
		if (GodotObject.IsInstanceValid(_activeTransitionTween)) _activeTransitionTween.Kill();
		_activeTransitionTween = null;
	}

	public void SetRuinSilhouette(bool dormant)
	{
		if (_isRuinSilhouette == dormant) return;
		_isRuinSilhouette = dormant;

		Node root = (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) ? _modelNode : this;
		ModelShaderManager.SetRuinSilhouette(root, dormant);
	}

	private bool _isConstructionBlocked = false;

	/// <summary>
	///     Reports whether the site this node draws is a building it cannot be raised into yet, which is answered by
	///     colouring its silhouette red instead of the dark fill a ruined or dormant building wears.
	/// </summary>
	/// <param name="blocked">Whether the site is held short of completion.</param>
	public void SetConstructionBlocked(bool blocked)
	{
		if (_isConstructionBlocked == blocked) return;
		_isConstructionBlocked = blocked;

		Node root = (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) ? _modelNode : this;
		ModelShaderManager.SetSilhouetteTint(root, blocked ? ModelShaderManager.BlockedSilhouetteTint : ModelShaderManager.DefaultSilhouetteTint);
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
		GameContext.Host?.RegisterActiveImpulseUnit(this);
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
			if (GameContext.Host != null && GameContext.World is { } world
			                              && world.IsAlive(Entity) && world.Has<DefinitionId>(Entity))
				return world.Get<DefinitionId>(Entity).Value;
			return _objectId;
		}
		set
		{
			_objectId = value;
			if (GameContext.Host != null && GameContext.World is { } world && world.IsAlive(Entity))
			{
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
			if (GameContext.Host != null && GameContext.World != null && Entity != default && GameContext.World.IsAlive(Entity))
				return GameContext.World.Has<Building>(Entity);
			if (GameHost.BuildingRegistry != null && !string.IsNullOrEmpty(UnitId) && GameHost.BuildingRegistry.ContainsKey(UnitId))
				return true;
			return _isBuilding;
		}
		set
		{
			_isBuilding = value;
			if (GameContext.Host != null && GameContext.World != null && Entity != default && GameContext.World.IsAlive(Entity))
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
			if (GameContext.Host != null && GameContext.World != null && Entity != default && GameContext.World.IsAlive(Entity))
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

	private Node3D? _modelNode;
	private AnimationPlayer? _animationPlayer;
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
	private Node3D _pathVisualsContainer = null!;
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
	private string? _deferredAnimationName;
	private float _deferredAnimationDelayRemaining = 0f;
	private string? _heldAnimationName;
	private float _heldAnimationSecondsRemaining = 0f;
	private Aabb _cachedLocalAabb = new Aabb(new Vector3(-1.5f, 0f, -1.5f), new Vector3(3f, 4f, 3f));

	public bool IsCulled => _isCulled;
	public bool HasEvaluatedFrustum
	{
		get => _hasEvaluatedFrustum;
		set => _hasEvaluatedFrustum = value;
	}
	public Aabb CachedLocalAabb => _cachedLocalAabb;

}