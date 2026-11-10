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

public enum ProjectileTrajectoryType
{
	Parabolic = 0,
	Linear = 1,
	Boomerang = 2,
	SwarmOrbit = 3
}

public enum ProjectileSpeedCurve
{
	Constant = 0,
	Accelerate = 1,
	Decelerate = 2,
	EaseInOut = 3,
	RocketBoost = 4,
	Burst = 5
}

public enum ProjectileScaleCurve
{
	Constant = 0,
	Grow = 1,
	Shrink = 2,
	GrowShrink = 3,
	SquashStretch = 4,
	ImpactShrink = 5
}

public enum ProjectileEaseCurve
{
	Linear = 0,
	EaseIn = 1,
	EaseOut = 2,
	EaseInOut = 3
}

[GlobalClass]
public partial class VisualProjectile3D : Node3D
{
	private const int MaxTrailPoints = 128;
	private readonly TrailPoint[] _trailPoints = new TrailPoint[MaxTrailPoints];
	private int _trailPointCount;

	private Node3D _meshContainer = null!;
	private Node3D _visualTransformContainer = null!;
	private MeshInstance3D _fallbackMeshInstance = null!;
	private Node3D? _customModelInstance;
	private readonly Dictionary<string, Node3D> _loadedModelsByPath = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, Texture2D?> _textureCache = new(StringComparer.OrdinalIgnoreCase);
	private OmniLight3D _pointLight = null!;
	private bool _hasAcquiredLight;
	private ShaderMaterial _uberShaderMaterial = null!;
	private static Shader? _sharedUberShader;

	public static void ClearSharedShaderCache()
	{
		_sharedUberShader = null;
		_textureCache.Clear();
	}

	public static void ClearCache() => ClearSharedShaderCache();

	private WeaponMetadata _weapon = null!;
	private Vector3 _startPosition;
	private Vector3 _targetPosition;
	private Vector3 _initialTargetPosition;
	private Entity _targetEntity;
	private float _speed;
	private float _totalFlightDuration;
	private float _elapsedTime;
	private bool _isFlying;
	private bool _honourDestination;
	private bool _isImpacted;
	private float _fadeTimer;
	private float _fadeDuration;
	private VfxSequenceInstance3D? _vfxSequence;
	private bool _impactStagesNotified;

	private ProjectileTrajectoryType _trajectoryType;
	private ProjectileSpeedCurve _speedCurve;
	private ProjectileScaleCurve _scaleCurve;
	private ProjectileEaseCurve _easeCurve;

	private Vector3 _cachedGlobalPosition;
	private Basis _cachedBasis = Basis.Identity;

	private Vector3 _currentFlightPosition;
	private Vector3 _currentFlightDirection;
	private Vector3 _tumbleAxis;
	private float _tumbleSpeed;
	private string? _currentLoadedModelPath;

	public bool IsActive => _isFlying || _isImpacted;
	public bool IsFlying => _isFlying;
	public bool IsImpacted => _isImpacted;
	public Action<VisualProjectile3D>? OnRecycleRequested;

	public Node3D MeshContainer => _meshContainer;
	public Node3D VisualTransformContainer => _visualTransformContainer;
	public Node3D? CustomModelInstance => _customModelInstance;
	public MeshInstance3D FallbackMeshInstance => _fallbackMeshInstance;
	public OmniLight3D PointLight => _pointLight;
	public ShaderMaterial UberShaderMaterial => _uberShaderMaterial;
	public WeaponMetadata Weapon => _weapon;
	public int PlayerIndex { get; set; } = 0;
	public float ElapsedFlightTime => _elapsedTime;
	public float TotalFlightDuration => _totalFlightDuration;
	public bool HasRibbonTrail => _hasRibbonTrail;
	public int TrailPointCount => _trailPointCount;
	public TrailPoint[] TrailPoints => _trailPoints;
	public Color ParsedRibbonColor => _parsedRibbonColor;

	public override void _Ready()
	{
		_meshContainer = new Node3D();
		_meshContainer.Name = "MeshContainer";
		AddChild(_meshContainer);

		_visualTransformContainer = new Node3D();
		_visualTransformContainer.Name = "VisualTransformContainer";
		_meshContainer.AddChild(_visualTransformContainer);

		_fallbackMeshInstance = new MeshInstance3D();
		_fallbackMeshInstance.Name = "FallbackMesh";
		var sphere = new SphereMesh();
		sphere.Radius = 0.22f;
		sphere.Height = 0.44f;
		_fallbackMeshInstance.Mesh = sphere;
		_visualTransformContainer.AddChild(_fallbackMeshInstance);

		EnsureSharedShader();
		_uberShaderMaterial = new ShaderMaterial();
		_uberShaderMaterial.Shader = _sharedUberShader;
		_fallbackMeshInstance.MaterialOverride = _uberShaderMaterial;

		_pointLight = new OmniLight3D();
		_pointLight.Name = "PointLight";
		_pointLight.ShadowEnabled = false;
		_pointLight.Visible = false;
		AddChild(_pointLight);

		SetProcess(false);
	}

	private static void EnsureSharedShader()
	{
		if (_sharedUberShader != null) return;

		string shaderCode = ShaderIncludeExpander.LoadExpandedShader("res://Assets/shaders/projectile_fx.gdshader");
		_sharedUberShader = string.IsNullOrEmpty(shaderCode)
			? GD.Load<Shader>("res://Assets/shaders/projectile_fx.gdshader")
			: new Shader { Code = shaderCode };
	}

	public void Initialize(WeaponMetadata weapon, Vector3 start, Vector3 target, Entity targetEntity = default, Action<VisualProjectile3D>? recycleCallback = null, int ownerPlayerIndex = 0, bool honourDestination = false)
	{
		if (recycleCallback != null) OnRecycleRequested = recycleCallback;
		PlayerIndex = ownerPlayerIndex;
		Initialize(start, target, weapon, targetEntity, honourDestination);
	}

	public void Initialize(string weaponId, Vector3 start, Vector3 target, Entity targetEntity = default, Action<VisualProjectile3D>? recycleCallback = null, int ownerPlayerIndex = 0, bool honourDestination = false)
	{
		if (recycleCallback != null) OnRecycleRequested = recycleCallback;
		PlayerIndex = ownerPlayerIndex;
		if (GameHost.WeaponRegistry.TryGetValue(weaponId ?? "arrow", out var meta))
		{
			Initialize(start, target, meta, targetEntity, honourDestination);
		}
		else
		{
			var fallback = new WeaponMetadata
			{
				TemplateID = weaponId ?? "arrow",
				ProjectileSpeed = 25f,
				OrientToTrajectory = true
			};
			Initialize(start, target, fallback, targetEntity, honourDestination);
		}
	}

	/// <param name="honourDestination">
	///     When true the flight always runs to completion at the requested destination instead of being cut short by the
	///     weapon's MaxLifetime/FailsafeRange, which exist to clean up projectiles whose moving target vanished. Script-spawned
	///     projectiles (Spawning a projectile at a fixed point) need the visual to stay alive until it actually arrives.
	/// </param>
	public void Initialize(Vector3 start, Vector3 target, WeaponMetadata weapon, Entity targetEntity = default, bool honourDestination = false)
	{
		if (_vfxSequence != null && GodotObject.IsInstanceValid(_vfxSequence))
		{
			_vfxSequence.DetachAnchor();
		}

		_weapon = weapon;
		_honourDestination = honourDestination;
		_startPosition = start;
		_targetPosition = target;
		_initialTargetPosition = target;
		_targetEntity = targetEntity;

		_trajectoryType = ProjectileFlightMath.ParseTrajectoryType(weapon.TrajectoryType);
		_speedCurve = ProjectileFlightMath.ParseSpeedCurve(weapon.SpeedCurve);
		_scaleCurve = ProjectileFlightMath.ParseScaleCurve(weapon.ScaleCurve);
		_easeCurve = ProjectileFlightMath.ParseEaseCurve(weapon.EaseCurve);

		_speed = weapon.ProjectileSpeed > 0 ? weapon.ProjectileSpeed : 25.0f;
		float distance = _startPosition.DistanceTo(_targetPosition);
		_totalFlightDuration = Mathf.Max(0.05f, distance / _speed);
		_elapsedTime = 0.0f;
		_isFlying = true;
		_isImpacted = false;
		_fadeTimer = 0.0f;
		_fadeDuration = weapon.RibbonLifetime > 0 ? weapon.RibbonLifetime : 0.5f;

		_currentFlightPosition = _startPosition;
		Vector3 toTarget = _targetPosition - _startPosition;
		_currentFlightDirection = toTarget.LengthSquared() > 0.001f ? toTarget.Normalized() : Vector3.Forward;

		_cachedGlobalPosition = _startPosition;
		_cachedBasis = Basis.Identity;
		Visible = true;
		GlobalPosition = _startPosition;
		_meshContainer.Visible = true;
		_meshContainer.Rotation = Vector3.Zero;

		UpdateVisualTransform();

		Vector3 tumble = weapon.TumbleAngularVelocity.ToGodotVector3();
		if (tumble.LengthSquared() > 0.001f)
		{
			_tumbleAxis = tumble.Normalized();
			_tumbleSpeed = tumble.Length();
		}
		else
		{
			_tumbleAxis = Vector3.Up;
			_tumbleSpeed = 0.0f;
		}

		UpdateModel();
		UpdateShaderMaterial();
		UpdateRibbonTrail();
		UpdatePointLight();

		_trailPointCount = 0;
		_impactStagesNotified = false;
		Visible = true;
		SetProcess(false);
		StartVfxSequence();
	}

	private void StartVfxSequence()
	{
		_vfxSequence = null;
		if (_weapon == null || string.IsNullOrEmpty(_weapon.VisualEffect)) return;

		var registry = GameContext.RegistryService?.VfxRegistry;
		if (registry == null || !registry.TryGetValue(_weapon.VisualEffect, out var sequenceConfig)) return;
		if (sequenceConfig?.Stages == null || sequenceConfig.Stages.Count == 0) return;

		Node parent = GetParent() ?? this;
		_vfxSequence = GameContext.FXService.SpawnVfxSequence(sequenceConfig, parent, _startPosition, 1.0f, _targetEntity != Entity.Null ? _targetEntity.Id : -1, this);
	}

}