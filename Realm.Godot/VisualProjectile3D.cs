using Arch.Core;
using Godot;
using Realm.Ecs.Components.Core;
using Realm.Godot.Utils;
using Realm.Godot.VFX;
using System;

public partial class VisualProjectile3D : Node3D
{
	private struct TrailPoint
	{
		public Vector3 Position;
		public float Age;
	}

	private const int MaxTrailPoints = 128;
	private readonly TrailPoint[] _trailPoints = new TrailPoint[MaxTrailPoints];
	private int _trailPointCount;

	private Node3D _meshContainer;
	private Node3D _visualTransformContainer;
	private MeshInstance3D _fallbackMeshInstance;
	private Node3D _customModelInstance;
	private OmniLight3D _pointLight;
	private GpuParticles3D _trailEmitter;
	private ImmediateMesh _ribbonImmediateMesh;
	private MeshInstance3D _ribbonMeshInstance;
	private StandardMaterial3D _ribbonMaterial;
	private ShaderMaterial _uberShaderMaterial;
	private static Shader _sharedUberShader;

	public static void ClearSharedShaderCache()
	{
		_sharedUberShader = null;
	}

	private GameHost.WeaponMetadata _weapon;
	private Vector3 _startPosition;
	private Vector3 _targetPosition;
	private Vector3 _initialTargetPosition;
	private Entity _targetEntity;
	private float _speed;
	private float _totalFlightDuration;
	private float _elapsedTime;
	private bool _isFlying;
	private bool _isImpacted;
	private float _fadeTimer;
	private float _fadeDuration;

	private Vector3 _currentFlightPosition;
	private Vector3 _currentFlightDirection;
	private Vector3 _tumbleAxis;
	private float _tumbleSpeed;
	private string _currentLoadedModelPath;
	private string _currentLoadedRibbonPath;

	public bool IsActive => _isFlying || _isImpacted;
	public bool IsFlying => _isFlying;
	public bool IsImpacted => _isImpacted;
	public Action<VisualProjectile3D> OnRecycleRequested;

	public Node3D MeshContainer => _meshContainer;
	public Node3D VisualTransformContainer => _visualTransformContainer;
	public Node3D CustomModelInstance => _customModelInstance;
	public MeshInstance3D FallbackMeshInstance => _fallbackMeshInstance;
	public OmniLight3D PointLight => _pointLight;
	public GpuParticles3D TrailEmitter => _trailEmitter;
	public MeshInstance3D RibbonMeshInstance => _ribbonMeshInstance;
	public ShaderMaterial UberShaderMaterial => _uberShaderMaterial;
	public StandardMaterial3D RibbonMaterial => _ribbonMaterial;
	public GameHost.WeaponMetadata Weapon => _weapon;
	public float ElapsedFlightTime => _elapsedTime;
	public float TotalFlightDuration => _totalFlightDuration;

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

		SetupTrailEmitter();
		SetupRibbonMeshInstance();
		SetProcess(false);
	}

	private static void EnsureSharedShader()
	{
		if (_sharedUberShader == null)
		{
			string shaderCode = "";
			if (FileAccess.FileExists("res://Assets/shaders/projectile_fx.gdshader"))
			{
				using var fa = FileAccess.Open("res://Assets/shaders/projectile_fx.gdshader", FileAccess.ModeFlags.Read);
				shaderCode = fa?.GetAsText() ?? "";
			}
			else if (System.IO.File.Exists("Assets/shaders/projectile_fx.gdshader"))
			{
				shaderCode = System.IO.File.ReadAllText("Assets/shaders/projectile_fx.gdshader");
			}
			else if (System.IO.File.Exists("Realm.Godot/Assets/shaders/projectile_fx.gdshader"))
			{
				shaderCode = System.IO.File.ReadAllText("Realm.Godot/Assets/shaders/projectile_fx.gdshader");
			}

			if (!string.IsNullOrEmpty(shaderCode))
			{
				_sharedUberShader = new Shader { Code = shaderCode };
			}
			else
			{
				_sharedUberShader = GD.Load<Shader>("res://Assets/shaders/projectile_fx.gdshader");
			}
		}
	}

	private void SetupTrailEmitter()
	{
		_trailEmitter = new GpuParticles3D();
		_trailEmitter.Name = "RibbonTrailEmitter";
		_trailEmitter.Amount = 1;
		_trailEmitter.Lifetime = 0.5f;
		_trailEmitter.Explosiveness = 0.0f;
		_trailEmitter.Randomness = 0.0f;
		_trailEmitter.FixedFps = 60;
		_trailEmitter.FractDelta = true;
		_trailEmitter.LocalCoords = false;
		_trailEmitter.Emitting = false;
		_trailEmitter.Visible = false;
		AddChild(_trailEmitter);
	}

	private void SetupRibbonMeshInstance()
	{
		_ribbonImmediateMesh = new ImmediateMesh();
		_ribbonMeshInstance = new MeshInstance3D();
		_ribbonMeshInstance.Name = "RibbonMeshInstance";
		_ribbonMeshInstance.Mesh = _ribbonImmediateMesh;

		_ribbonMaterial = new StandardMaterial3D();
		_ribbonMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		_ribbonMaterial.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
		_ribbonMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		_ribbonMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		_ribbonMaterial.TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear;

		_ribbonMeshInstance.MaterialOverride = _ribbonMaterial;
		AddChild(_ribbonMeshInstance);
		_ribbonMeshInstance.TopLevel = true;
		_ribbonMeshInstance.Position = Vector3.Zero;
		_ribbonMeshInstance.Rotation = Vector3.Zero;
	}

	public void Initialize(GameHost.WeaponMetadata weapon, Vector3 start, Vector3 target, Entity targetEntity = default, Action<VisualProjectile3D> recycleCallback = null)
	{
		if (recycleCallback != null) OnRecycleRequested = recycleCallback;
		Initialize(start, target, weapon, targetEntity);
	}

	public void Initialize(string weaponId, Vector3 start, Vector3 target, Entity targetEntity = default, Action<VisualProjectile3D> recycleCallback = null)
	{
		if (recycleCallback != null) OnRecycleRequested = recycleCallback;
		if (GameHost.WeaponRegistry.TryGetValue(weaponId ?? "arrow", out var meta))
		{
			Initialize(start, target, meta, targetEntity);
		}
		else
		{
			var fallback = new GameHost.WeaponMetadata
			{
				ObjectID = weaponId ?? "arrow",
				ProjectileSpeed = 25f,
				OrientToTrajectory = true
			};
			Initialize(start, target, fallback, targetEntity);
		}
	}

	public void Initialize(Vector3 start, Vector3 target, GameHost.WeaponMetadata weapon, Entity targetEntity = default)
	{
		_weapon = weapon;
		_startPosition = start;
		_targetPosition = target;
		_initialTargetPosition = target;
		_targetEntity = targetEntity;

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

		GlobalPosition = _startPosition;
		_meshContainer.Visible = true;
		_meshContainer.Rotation = Vector3.Zero;

		UpdateVisualTransform();

		Vector3 tumble = weapon.TumbleAngularVelocity;
		if (tumble.LengthSquared() > 0.001f)
		{
			_tumbleAxis = tumble.Normalized();
			_tumbleSpeed = tumble.Length();
		}
		else
		{
			_tumbleAxis = new Vector3(1f, 0.5f, 0.2f).Normalized();
			_tumbleSpeed = 4.0f;
		}

		UpdateModel();
		UpdateShaderMaterial();
		UpdateRibbonTrail();
		UpdatePointLight();

		_trailPointCount = 0;
		_ribbonImmediateMesh?.ClearSurfaces();
		if (_ribbonMeshInstance != null)
		{
			_ribbonMeshInstance.Visible = true;
			_ribbonMeshInstance.GlobalPosition = Vector3.Zero;
			_ribbonMeshInstance.GlobalRotation = Vector3.Zero;
		}

		Visible = true;
		SetProcess(true);
	}

	private void UpdateVisualTransform()
	{
		_visualTransformContainer.Position = _weapon.MeshTranslationOffset;

		Vector3 baseEuler = GetForwardAxisEulerDegrees(_weapon.ForwardAxisPreset);
		Vector3 totalEuler = baseEuler + _weapon.MeshRotationOffset;
		_visualTransformContainer.Rotation = new Vector3(
			Mathf.DegToRad(totalEuler.X),
			Mathf.DegToRad(totalEuler.Y),
			Mathf.DegToRad(totalEuler.Z)
		);

		Vector3 baseScale = (_weapon.MeshScaleOffset == Vector3.Zero) ? Vector3.One : _weapon.MeshScaleOffset;
		baseScale = SafeScale(baseScale);
		float initialScaleFactor = Mathf.Max(0.001f, CalculateScaleOverLifetime(0.0f, _weapon.ScaleCurve));
		_visualTransformContainer.Scale = SafeScale(baseScale * initialScaleFactor);
	}

	private static Vector3 SafeScale(Vector3 scale)
	{
		return new Vector3(
			Mathf.Max(0.001f, Mathf.Abs(scale.X)),
			Mathf.Max(0.001f, Mathf.Abs(scale.Y)),
			Mathf.Max(0.001f, Mathf.Abs(scale.Z))
		);
	}

	private static Vector3 GetForwardAxisEulerDegrees(string preset)
	{
		if (string.IsNullOrEmpty(preset)) return Vector3.Zero;
		switch (preset.Trim().ToUpperInvariant())
		{
			case "+Z":
				return new Vector3(0f, 180f, 0f);
			case "+X":
				return new Vector3(0f, -90f, 0f);
			case "-X":
				return new Vector3(0f, 90f, 0f);
			case "+Y":
				return new Vector3(-90f, 0f, 0f);
			case "-Y":
				return new Vector3(90f, 0f, 0f);
			case "-Z":
			default:
				return Vector3.Zero;
		}
	}

	private void UpdateModel()
	{
		string modelPath = _weapon.ProjectileModelPath;
		if (string.IsNullOrEmpty(modelPath))
		{
			if (_customModelInstance != null && GodotObject.IsInstanceValid(_customModelInstance))
			{
				_customModelInstance.QueueFree();
				_customModelInstance = null;
			}
			_currentLoadedModelPath = null;
			_fallbackMeshInstance.Visible = true;
			_uberShaderMaterial.SetShaderParameter("use_albedo_texture", false);
			_fallbackMeshInstance.MaterialOverride = _uberShaderMaterial;
			return;
		}

		if (modelPath != _currentLoadedModelPath || _customModelInstance == null || !GodotObject.IsInstanceValid(_customModelInstance))
		{
			if (_customModelInstance != null && GodotObject.IsInstanceValid(_customModelInstance))
			{
				_customModelInstance.QueueFree();
				_customModelInstance = null;
			}

			_currentLoadedModelPath = modelPath;
			Node3D node3D = null;
			if (modelPath.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ||
				(GameHost.VfxRegistry != null && GameHost.VfxRegistry.ContainsKey(modelPath)))
			{
				node3D = Unit3D.ResolveAndInstantiateAttachment(modelPath, out _, out _, out _);
			}
			else
			{
				var loaded = ModelCache.GetModel(modelPath);
				if (loaded is Node3D n) node3D = n;
			}

			if (node3D != null)
			{
				_customModelInstance = node3D;
				_visualTransformContainer.AddChild(_customModelInstance);
				_fallbackMeshInstance.Visible = false;
				if (node3D is not ProceduralVfxInstance3D)
				{
					ApplyUberMaterialRecursively(_customModelInstance);
				}
			}
			else
			{
				_fallbackMeshInstance.Visible = true;
				_uberShaderMaterial.SetShaderParameter("use_albedo_texture", false);
				_fallbackMeshInstance.MaterialOverride = _uberShaderMaterial;
			}
		}
		else
		{
			_fallbackMeshInstance.Visible = false;
			if (_customModelInstance is not ProceduralVfxInstance3D)
			{
				ApplyUberMaterialRecursively(_customModelInstance);
			}
		}
	}

	private void ConfigureShaderMaterial(ShaderMaterial mat, Texture2D albedoTex)
	{
		EnsureSharedShader();
		if (mat.Shader == null)
		{
			mat.Shader = _sharedUberShader;
		}

		Color baseCol = ParseColor(_weapon.BaseColor, new Color(0.15f, 0.12f, 0.1f));
		Color emissiveCol = ParseColor(_weapon.EmissionColor, new Color(1.0f, 0.4f, 0.05f));
		Color fresnelCol = ParseColor(_weapon.FresnelColor, new Color(1.0f, 0.6f, 0.1f));

		bool effectEnabled = !string.Equals(_weapon.ShaderEffectType, "none", StringComparison.OrdinalIgnoreCase);
		mat.SetShaderParameter("effect_enabled", effectEnabled);
		mat.SetShaderParameter("base_color", baseCol);
		mat.SetShaderParameter("emission_color", emissiveCol);
		mat.SetShaderParameter("emission_energy", _weapon.EmissionEnergy);
		mat.SetShaderParameter("fresnel_power", _weapon.FresnelPower > 0.01f ? _weapon.FresnelPower : 3.0f);
		mat.SetShaderParameter("fresnel_color", fresnelCol);
		mat.SetShaderParameter("fresnel_factor", _weapon.FresnelFactor);
		mat.SetShaderParameter("noise_scale", _weapon.NoiseScale > 0.01f ? _weapon.NoiseScale : 3.0f);
		mat.SetShaderParameter("uv_scroll_speed_1", _weapon.UvScrollSpeed1);
		mat.SetShaderParameter("uv_scroll_speed_2", _weapon.UvScrollSpeed2);
		mat.SetShaderParameter("threshold_cutoff", _weapon.ThresholdCutoff);
		mat.SetShaderParameter("threshold_smoothness", _weapon.ThresholdSmoothness > 0.001f ? _weapon.ThresholdSmoothness : 0.1f);

		int maskSource = 0;
		if (!string.IsNullOrEmpty(_weapon.EmissionMaskSource))
		{
			switch (_weapon.EmissionMaskSource.Trim().ToLowerInvariant())
			{
				case "vertex_color":
				case "vertex_color_spikes":
				case "vertex color / spikes":
				case "spikes":
					maskSource = 1;
					break;
				case "fresnel":
				case "fresnel_only":
				case "fresnel only":
					maskSource = 2;
					break;
				case "texture_alpha":
				case "texture alpha":
					maskSource = 3;
					break;
				case "noise":
				case "noise_only":
				case "noise only":
				default:
					maskSource = 0;
					break;
			}
		}
		mat.SetShaderParameter("emission_mask_source", maskSource);

		if (!string.IsNullOrEmpty(_weapon.NoiseTexture))
		{
			var tex = LoadTextureSafe(_weapon.NoiseTexture);
			if (tex != null)
			{
				mat.SetShaderParameter("noise_texture", tex);
				mat.SetShaderParameter("use_procedural_noise", false);
			}
			else
			{
				mat.SetShaderParameter("use_procedural_noise", true);
			}
		}
		else
		{
			mat.SetShaderParameter("use_procedural_noise", true);
		}

		if (albedoTex != null)
		{
			mat.SetShaderParameter("albedo_texture", albedoTex);
			mat.SetShaderParameter("use_albedo_texture", true);
		}
		else
		{
			mat.SetShaderParameter("use_albedo_texture", false);
		}
	}

	private void ApplyUberMaterialRecursively(Node node)
	{
		if (node is MeshInstance3D meshInst)
		{
			Texture2D albedoTex = null;
			Color albedoColor = Colors.White;

			int surfCount = meshInst.Mesh?.GetSurfaceCount() ?? 0;
			for (int s = 0; s < surfCount; s++)
			{
				var surfMat = meshInst.GetSurfaceOverrideMaterial(s) ?? meshInst.Mesh?.SurfaceGetMaterial(s);
				if (surfMat is BaseMaterial3D baseMat)
				{
					if (baseMat.AlbedoTexture != null)
					{
						albedoTex = baseMat.AlbedoTexture;
					}
					albedoColor = baseMat.AlbedoColor;
					if (albedoTex != null) break;
				}
			}

			if (string.Equals(_weapon.ShaderEffectType, "none", StringComparison.OrdinalIgnoreCase))
			{
				meshInst.MaterialOverride = null;
			}
			else
			{
				var matInstance = meshInst.MaterialOverride as ShaderMaterial ?? new ShaderMaterial();
				ConfigureShaderMaterial(matInstance, albedoTex);
				meshInst.MaterialOverride = matInstance;
			}
		}

		foreach (Node child in node.GetChildren())
		{
			ApplyUberMaterialRecursively(child);
		}
	}

	private void UpdateShaderMaterial()
	{
		ConfigureShaderMaterial(_uberShaderMaterial, null);

		if (_customModelInstance != null && GodotObject.IsInstanceValid(_customModelInstance))
		{
			ApplyUberMaterialRecursively(_customModelInstance);
		}
	}

	private void UpdateRibbonTrail()
	{
		Color ribbonCol = ParseColor(_weapon.RibbonColor, new Color(1.0f, 0.65f, 0.2f));

		if (_weapon.RibbonAdditive)
		{
			_ribbonMaterial.AlbedoColor = new Color(ribbonCol.R * 2.2f, ribbonCol.G * 2.2f, ribbonCol.B * 2.2f, ribbonCol.A);
			_ribbonMaterial.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
		}
		else
		{
			_ribbonMaterial.AlbedoColor = ribbonCol;
			_ribbonMaterial.BlendMode = BaseMaterial3D.BlendModeEnum.Mix;
		}

		if (!string.IsNullOrEmpty(_weapon.RibbonTexture) && _weapon.RibbonTexture != _currentLoadedRibbonPath)
		{
			_currentLoadedRibbonPath = _weapon.RibbonTexture;
			var tex = LoadTextureSafe(_weapon.RibbonTexture);
			_ribbonMaterial.AlbedoTexture = tex;
		}
		else if (string.IsNullOrEmpty(_weapon.RibbonTexture))
		{
			_currentLoadedRibbonPath = null;
			_ribbonMaterial.AlbedoTexture = null;
		}
	}

	private void UpdatePointLight()
	{
		if (_weapon.PointLightEnabled && _weapon.PointLightIntensity > 0.01f)
		{
			_pointLight.Visible = true;
			_pointLight.LightColor = ParseColor(_weapon.PointLightColor, new Color(1.0f, 0.7f, 0.3f));
			_pointLight.LightEnergy = _weapon.PointLightIntensity;
			_pointLight.OmniRange = _weapon.PointLightRange > 0 ? _weapon.PointLightRange : 6.0f;
			_pointLight.OmniAttenuation = 2.0f;
		}
		else
		{
			_pointLight.Visible = false;
		}
	}

	public float TimeScale { get; set; } = 1.0f;
	public bool IsPaused { get; set; } = false;

	public void StepSimulation(float deltaSeconds)
	{
		if (_isImpacted && deltaSeconds < 0.0f)
		{
			_isImpacted = false;
			_isFlying = true;
			_meshContainer.Visible = true;
			_pointLight.Visible = _weapon.PointLightEnabled && _weapon.PointLightIntensity > 0.01f;
			if (_ribbonMeshInstance != null) _ribbonMeshInstance.Visible = true;
			SetProcess(true);
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

	public void AdvanceSimulation(float dt)
	{
		if (_isImpacted)
		{
			_fadeTimer += dt;
			if (_fadeTimer >= _fadeDuration)
			{
				SetProcess(false);
				_isImpacted = false;
				Visible = false;
				if (_ribbonMeshInstance != null) _ribbonMeshInstance.Visible = false;
				OnRecycleRequested?.Invoke(this);
			}
			return;
		}

		if (!_isFlying) return;

		if (_weapon.MaxLifetime > 0.0f && _elapsedTime >= _weapon.MaxLifetime)
		{
			HandleImpact(GlobalPosition);
			return;
		}

		if (_weapon.FailsafeRange > 0.0f && GlobalPosition.DistanceTo(_startPosition) >= _weapon.FailsafeRange)
		{
			HandleImpact(GlobalPosition);
			return;
		}

		if (_elapsedTime >= _totalFlightDuration * 3.0f + 5.0f)
		{
			HandleImpact(GlobalPosition);
			return;
		}

		Vector3 currentTarget = _initialTargetPosition;
		if (_targetEntity != default && GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.EcsWorld.IsAlive(_targetEntity))
		{
			if (GameHost.Instance.EcsWorld.Has<Position>(_targetEntity))
			{
				var posComp = GameHost.Instance.EcsWorld.Get<Position>(_targetEntity);
				currentTarget = new Vector3(posComp.Value.X, posComp.Value.Y + 1.2f, posComp.Value.Z);
			}
		}

		float currentSpeed = CalculateSpeed(_speed, _elapsedTime, _totalFlightDuration, _weapon.SpeedCurve, _weapon.Acceleration);
		_elapsedTime += dt;
		float rawT = Mathf.Clamp(_elapsedTime / _totalFlightDuration, 0.0f, 1.0f);
		float easedT = ApplyEaseCurve(rawT, _weapon.EaseCurve);

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

			if (distToTarget <= Mathf.Max(0.5f, currentSpeed * dt * 1.5f) || rawT >= 1.0f)
			{
				HandleImpact(_currentFlightPosition);
				return;
			}
		}
		else
		{
			Vector3 effectiveTarget = _initialTargetPosition.Lerp(currentTarget, Mathf.Clamp(_weapon.HomingWeight * easedT, 0.0f, 1.0f));
			_currentFlightPosition = _startPosition.Lerp(effectiveTarget, easedT);

			if (rawT >= 1.0f)
			{
				HandleImpact(_currentFlightPosition);
				return;
			}
		}

		float arcY = 0.0f;
		if (_weapon.MaxBounces > 0)
		{
			int segments = 1 + _weapon.MaxBounces;
			float segmentProgress = rawT * segments;
			int currentBounce = Mathf.Min((int)Mathf.Floor(segmentProgress), segments - 1);
			float localU = segmentProgress - currentBounce;
			float bounceHeight = _weapon.ArcHeight / (1.0f + currentBounce * 0.6f);
			arcY = 4.0f * bounceHeight * localU * (1.0f - localU);
		}
		else if (_weapon.ArcHeight > 0.0f)
		{
			arcY = 4.0f * _weapon.ArcHeight * rawT * (1.0f - rawT);
		}

		Vector3 forward = _currentFlightDirection.LengthSquared() > 0.001f ? _currentFlightDirection.Normalized() : Vector3.Forward;
		Vector3 right = Mathf.Abs(forward.Dot(Vector3.Up)) > 0.99f ? forward.Cross(Vector3.Right).Normalized() : forward.Cross(Vector3.Up).Normalized();
		Vector3 up = right.Cross(forward).Normalized();

		Vector3 spiralOffset = Vector3.Zero;
		if (_weapon.SpiralRadius > 0.0f && _weapon.SpiralFrequency > 0.0f)
		{
			float theta = Mathf.Tau * _weapon.SpiralFrequency * _elapsedTime;
			spiralOffset = (right * Mathf.Cos(theta) + up * Mathf.Sin(theta)) * _weapon.SpiralRadius;
		}

		Vector3 zigzagOffset = Vector3.Zero;
		if (_weapon.ZigzagAmplitude > 0.0f && _weapon.ZigzagFrequency > 0.0f)
		{
			float phi = Mathf.Tau * _weapon.ZigzagFrequency * _elapsedTime;
			zigzagOffset = right * (Mathf.Sin(phi) * _weapon.ZigzagAmplitude);
		}

		Vector3 nextPos = _currentFlightPosition + new Vector3(0, arcY, 0) + spiralOffset + zigzagOffset;

		if (_weapon.OrientToTrajectory)
		{
			Vector3 velocityDelta = nextPos - GlobalPosition;
			if (velocityDelta.LengthSquared() > 0.0001f)
			{
				Vector3 dir = velocityDelta.Normalized();
				Vector3 upVector = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
				LookAtFromPosition(nextPos, nextPos + dir, upVector);
			}
			else
			{
				GlobalPosition = nextPos;
			}
		}
		else
		{
			GlobalPosition = nextPos;
		}

		_meshContainer.RotateObjectLocal(_tumbleAxis, _tumbleSpeed * dt);

		Vector3 baseScale = (_weapon.MeshScaleOffset == Vector3.Zero) ? Vector3.One : _weapon.MeshScaleOffset;
		baseScale = SafeScale(baseScale);
		float lifetimeScale = Mathf.Max(0.001f, CalculateScaleOverLifetime(rawT, _weapon.ScaleCurve));
		_visualTransformContainer.Scale = SafeScale(baseScale * lifetimeScale);

		Vector3 trailPos = GlobalPosition + GlobalTransform.Basis * _weapon.TrailOffset;
		UpdateTrail(dt, trailPos);
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
				for (int i = moveCount; i > 0; i--)
				{
					_trailPoints[i] = _trailPoints[i - 1];
				}
				_trailPoints[0] = new TrailPoint
				{
					Position = currentPos,
					Age = 0.0f
				};
				_trailPointCount = Math.Min(_trailPointCount + 1, MaxTrailPoints);
			}
		}

		RedrawRibbonMesh(lifetime);
	}

	private void RedrawRibbonMesh(float lifetime)
	{
		if (_ribbonImmediateMesh == null) return;
		_ribbonImmediateMesh.ClearSurfaces();
		if (_trailPointCount < 2) return;

		Camera3D camera = GetViewport()?.GetCamera3D();
		Vector3 camPos = camera != null && GodotObject.IsInstanceValid(camera) ? camera.GlobalPosition : (GlobalPosition + new Vector3(0, 5, 10));

		float baseWidth = _weapon.RibbonWidth > 0 ? _weapon.RibbonWidth : 0.4f;
		Color baseCol = ParseColor(_weapon.RibbonColor, new Color(1.0f, 0.65f, 0.2f));

		_ribbonImmediateMesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, _ribbonMaterial);

		for (int i = 0; i < _trailPointCount; i++)
		{
			Vector3 p = _trailPoints[i].Position;
			float t = Mathf.Clamp(_trailPoints[i].Age / lifetime, 0.0f, 1.0f);

			Vector3 forward;
			if (i == 0)
			{
				forward = (_trailPoints[0].Position - _trailPoints[1].Position).Normalized();
			}
			else if (i == _trailPointCount - 1)
			{
				forward = (_trailPoints[i - 1].Position - _trailPoints[i].Position).Normalized();
			}
			else
			{
				forward = (_trailPoints[i - 1].Position - _trailPoints[i + 1].Position).Normalized();
			}

			if (forward.LengthSquared() < 0.001f) forward = Vector3.Forward;

			Vector3 viewDir = (p - camPos).Normalized();
			Vector3 side = forward.Cross(viewDir).Normalized();
			if (side.LengthSquared() < 0.001f)
			{
				side = forward.Cross(Vector3.Up).Normalized();
				if (side.LengthSquared() < 0.001f) side = Vector3.Right;
			}

			float width = baseWidth * (_weapon.RibbonTaper ? (1.0f - t) : 1.0f);
			Vector3 halfSide = side * (width * 0.5f);

			float u = t + (_weapon.RibbonScrollSpeed * _elapsedTime);
			float alpha = 1.0f - t;
			Color vertColor = new Color(baseCol.R, baseCol.G, baseCol.B, baseCol.A * alpha);

			_ribbonImmediateMesh.SurfaceSetColor(vertColor);
			_ribbonImmediateMesh.SurfaceSetUV(new Vector2(u, 0.0f));
			_ribbonImmediateMesh.SurfaceAddVertex(p - halfSide);

			_ribbonImmediateMesh.SurfaceSetColor(vertColor);
			_ribbonImmediateMesh.SurfaceSetUV(new Vector2(u, 1.0f));
			_ribbonImmediateMesh.SurfaceAddVertex(p + halfSide);
		}

		_ribbonImmediateMesh.SurfaceEnd();
	}

	private void HandleImpact(Vector3 impactPosition)
	{
		_isFlying = false;
		_isImpacted = true;
		_fadeTimer = 0.0f;

		_meshContainer.Visible = false;
		_pointLight.Visible = false;
		if (_ribbonMeshInstance != null) _ribbonMeshInstance.Visible = false;
		_ribbonImmediateMesh?.ClearSurfaces();

		if (!string.IsNullOrEmpty(_weapon.ImpactVisualEffect) && GameHost.Instance != null)
		{
			var fxService = ServiceLocator.TryGet<FXService>();
			fxService?.SpawnSpritesheetEffect(GetParent() ?? this, _weapon.ImpactVisualEffect, impactPosition, 4, 4, 0.04f, 4.0f);
		}

		if (!string.IsNullOrEmpty(_weapon.ImpactSound))
		{
			var audioService = ServiceLocator.TryGet<AudioService>();
			audioService?.PlaySound3D(_weapon.ImpactSound, impactPosition);
		}
	}

	private static float CalculateSpeed(float baseSpeed, float elapsedTime, float totalDuration, string speedCurve, float acceleration)
	{
		float speed = baseSpeed + (acceleration * elapsedTime);
		if (speed < 0.1f) speed = 0.1f;

		if (string.IsNullOrEmpty(speedCurve) || speedCurve == "constant")
			return speed;

		float progress = Mathf.Clamp(elapsedTime / Mathf.Max(0.001f, totalDuration), 0.0f, 1.0f);

		switch (speedCurve.ToLowerInvariant())
		{
			case "accelerate":
			case "ease_in":
				return speed * (0.3f + 1.4f * progress * progress);
			case "decelerate":
			case "ease_out":
				return speed * (1.7f - 1.4f * progress * progress);
			case "ease_in_out":
				float smoothProgress = progress * progress * (3.0f - 2.0f * progress);
				return speed * (0.4f + 1.2f * smoothProgress);
			case "rocket_boost":
				return speed * (0.15f + 2.35f * Mathf.Pow(progress, 3.0f));
			case "burst":
				return speed * (1.0f + 1.5f * Mathf.Exp(-4.0f * progress));
			default:
				return speed;
		}
	}

	private static float CalculateScaleOverLifetime(float t, string scaleCurve)
	{
		if (string.IsNullOrEmpty(scaleCurve) || scaleCurve == "constant" || scaleCurve == "none")
			return 1.0f;

		switch (scaleCurve.ToLowerInvariant())
		{
			case "grow":
				return Mathf.Clamp(t * 1.5f, 0.001f, 1.0f);
			case "shrink":
				return Mathf.Clamp(1.0f - t, 0.001f, 1.0f);
			case "grow_shrink":
				return Mathf.Max(0.001f, Mathf.Sin(Mathf.Clamp(t, 0.0f, 1.0f) * Mathf.Pi));
			case "squash_stretch":
				if (t < 0.2f)
					return Mathf.Max(0.001f, (t / 0.2f) * 1.2f);
				else if (t < 0.4f)
					return Mathf.Max(0.001f, 1.2f - ((t - 0.2f) / 0.2f) * 0.2f);
				else if (t > 0.85f)
					return Mathf.Max(0.001f, (1.0f - t) / 0.15f);
				return 1.0f;
			case "impact_shrink":
				return t > 0.8f ? Mathf.Clamp((1.0f - t) / 0.2f, 0.001f, 1.0f) : 1.0f;
			default:
				return 1.0f;
		}
	}

	private static float ApplyEaseCurve(float t, string easeCurve)
	{
		if (string.IsNullOrEmpty(easeCurve)) return t;
		switch (easeCurve.ToLowerInvariant())
		{
			case "ease_in":
				return t * t;
			case "ease_out":
				return 1.0f - (1.0f - t) * (1.0f - t);
			case "ease_in_out":
				return t < 0.5f ? 2.0f * t * t : 1.0f - Mathf.Pow(-2.0f * t + 2.0f, 2.0f) / 2.0f;
			default:
				return t;
		}
	}

	private static Color ParseColor(string hex, Color fallback)
	{
		if (string.IsNullOrEmpty(hex)) return fallback;
		try
		{
			return Color.FromHtml(hex);
		}
		catch
		{
			return fallback;
		}
	}

	private static Texture2D LoadTextureSafe(string path)
	{
		if (string.IsNullOrEmpty(path)) return null;

		if (path.StartsWith("res://") || path.StartsWith("user://"))
		{
			if (ResourceLoader.Exists(path))
			{
				return GD.Load<Texture2D>(path);
			}
		}

		string resolvedPath = ModelCache.ResolveModelPath(path);
		if (!string.IsNullOrEmpty(resolvedPath) && System.IO.File.Exists(resolvedPath))
		{
			if (resolvedPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				var img = LoadImageFromRtex(resolvedPath);
				if (img != null)
				{
					img.GenerateMipmaps();
					return ImageTexture.CreateFromImage(img);
				}
			}
			else
			{
				var img = Image.LoadFromFile(resolvedPath);
				if (img != null)
				{
					img.GenerateMipmaps();
					return ImageTexture.CreateFromImage(img);
				}
			}
		}

		return null;
	}

	private static Image? LoadImageFromRtex(string rtexPath, int layer = 0)
	{
		string globalPath = ProjectSettings.GlobalizePath(rtexPath);
		if (!System.IO.File.Exists(globalPath)) return null;

		try
		{
			byte[] bytes = System.IO.File.ReadAllBytes(globalPath);
			byte[]? layerData = Realm.Shared.Textures.RtexFile.GetLayer(bytes, layer);
			if (layerData == null || layerData.Length == 0) return null;

			var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
			if (img.LoadWebpFromBuffer(layerData) != Error.Ok)
			{
				img.LoadPngFromBuffer(layerData);
			}
			return img;
		}
		catch
		{
			return null;
		}
	}
}
