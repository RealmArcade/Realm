using Arch.Core;
using Godot;
using Realm.Ecs.Components.Core;
using Realm.Client.Services;
using Realm.Client.VFX;
using System;

namespace Realm.Client;

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

	private WeaponMetadata _weapon;
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
	public WeaponMetadata Weapon => _weapon;
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
			else if (System.IO.File.Exists("Realm.ClientAssets/shaders/projectile_fx.gdshader"))
			{
				shaderCode = System.IO.File.ReadAllText("Realm.ClientAssets/shaders/projectile_fx.gdshader");
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

	public void Initialize(WeaponMetadata weapon, Vector3 start, Vector3 target, Entity targetEntity = default, Action<VisualProjectile3D> recycleCallback = null)
	{
		if (recycleCallback != null) OnRecycleRequested = recycleCallback;
		Initialize(start, target, weapon, targetEntity);
	}

	public void Initialize(string weaponId, Vector3 start, Vector3 target, Entity targetEntity = default, Action<VisualProjectile3D> recycleCallback = null)
	{
		if (recycleCallback != null) OnRecycleRequested = recycleCallback;
		if (Realm.Client.Core.GameHost.WeaponRegistry.TryGetValue(weaponId ?? "arrow", out var meta))
		{
			Initialize(start, target, meta, targetEntity);
		}
		else
		{
			var fallback = new WeaponMetadata
			{
				TemplateID = weaponId ?? "arrow",
				ProjectileSpeed = 25f,
				OrientToTrajectory = true
			};
			Initialize(start, target, fallback, targetEntity);
		}
	}

	public void Initialize(Vector3 start, Vector3 target, WeaponMetadata weapon, Entity targetEntity = default)
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

		Vector3 tumble = weapon.TumbleAngularVelocity.ToGodotVector3();
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
		_visualTransformContainer.Position = _weapon.MeshTranslationOffset.ToGodotVector3();

		Vector3 baseEuler = GetForwardAxisEulerDegrees(_weapon.ForwardAxisPreset);
		Vector3 totalEuler = baseEuler + _weapon.MeshRotationOffset.ToGodotVector3();
		_visualTransformContainer.Rotation = new Vector3(
			Mathf.DegToRad(totalEuler.X),
			Mathf.DegToRad(totalEuler.Y),
			Mathf.DegToRad(totalEuler.Z)
		);

		Vector3 meshScale = _weapon.MeshScaleOffset.ToGodotVector3();
		Vector3 baseScale = (meshScale == Vector3.Zero) ? Vector3.One : meshScale;
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

	private void ClearCustomModelInstance()
	{
		if (_customModelInstance != null && GodotObject.IsInstanceValid(_customModelInstance))
		{
			_customModelInstance.QueueFree();
			_customModelInstance = null;
		}
	}

	private void EnableFallbackMesh()
	{
		_fallbackMeshInstance.Visible = true;
		_uberShaderMaterial.SetShaderParameter("use_albedo_texture", false);
		_fallbackMeshInstance.MaterialOverride = _uberShaderMaterial;
	}

	private Node3D LoadModelPath(string modelPath)
	{
		if (modelPath.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ||
		    (Realm.Client.Core.GameHost.VfxRegistry != null && Realm.Client.Core.GameHost.VfxRegistry.ContainsKey(modelPath)))
		{
			return Unit3D.ResolveAndInstantiateAttachment(modelPath, out _, out _, out _);
		}

		var loaded = ModelCache.GetModel(modelPath);
		return loaded is Node3D n ? n : null;
	}

	private void UpdateModel()
	{
		string modelPath = _weapon.ProjectileModelPath;
		if (string.IsNullOrEmpty(modelPath))
		{
			ClearCustomModelInstance();
			_currentLoadedModelPath = null;
			EnableFallbackMesh();
			return;
		}

		bool needsReload = modelPath != _currentLoadedModelPath || _customModelInstance == null || !GodotObject.IsInstanceValid(_customModelInstance);
		if (!needsReload)
		{
			_fallbackMeshInstance.Visible = false;
			if (_customModelInstance is not ProceduralVfxInstance3D)
			{
				ApplyUberMaterialRecursively(_customModelInstance);
			}
			return;
		}

		ClearCustomModelInstance();
		_currentLoadedModelPath = modelPath;
		
		Node3D node3D = LoadModelPath(modelPath);
		if (node3D == null)
		{
			EnableFallbackMesh();
			return;
		}

		_customModelInstance = node3D;
		_visualTransformContainer.AddChild(_customModelInstance);
		_fallbackMeshInstance.Visible = false;

		if (node3D is not ProceduralVfxInstance3D)
		{
			ApplyUberMaterialRecursively(_customModelInstance);
		}
	}

	private int GetMaskSource(string maskSource)
	{
		if (string.IsNullOrEmpty(maskSource)) return 0;

		return maskSource.Trim().ToLowerInvariant() switch
		{
			"vertex_color" or "vertex_color_spikes" or "vertex color / spikes" or "spikes" => 1,
			"fresnel" or "fresnel_only" or "fresnel only" => 2,
			"texture_alpha" or "texture alpha" => 3,
			_ => 0,
		};
	}

	private void ConfigureNoiseTexture(ShaderMaterial mat)
	{
		if (string.IsNullOrEmpty(_weapon.NoiseTexture))
		{
			mat.SetShaderParameter("use_procedural_noise", true);
			return;
		}

		var tex = LoadTextureSafe(_weapon.NoiseTexture);
		mat.SetShaderParameter("noise_texture", tex);
		mat.SetShaderParameter("use_procedural_noise", tex == null);
	}

	private void ConfigureShaderMaterial(ShaderMaterial mat, Texture2D albedoTex)
	{
		EnsureSharedShader();
		mat.Shader ??= _sharedUberShader;

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
		mat.SetShaderParameter("uv_scroll_speed_1", _weapon.UvScrollSpeed1.ToGodotVector2());
		mat.SetShaderParameter("uv_scroll_speed_2", _weapon.UvScrollSpeed2.ToGodotVector2());
		mat.SetShaderParameter("threshold_cutoff", _weapon.ThresholdCutoff);
		mat.SetShaderParameter("threshold_smoothness", _weapon.ThresholdSmoothness > 0.001f ? _weapon.ThresholdSmoothness : 0.1f);

		mat.SetShaderParameter("emission_mask_source", GetMaskSource(_weapon.EmissionMaskSource));
		ConfigureNoiseTexture(mat);

		mat.SetShaderParameter("albedo_texture", albedoTex);
		mat.SetShaderParameter("use_albedo_texture", albedoTex != null);
	}

	private Texture2D FindAlbedoTexture(MeshInstance3D meshInst)
	{
		int surfCount = meshInst.Mesh?.GetSurfaceCount() ?? 0;
		for (int s = 0; s < surfCount; s++)
		{
			var surfMat = meshInst.GetSurfaceOverrideMaterial(s) ?? meshInst.Mesh?.SurfaceGetMaterial(s);
			if (surfMat is BaseMaterial3D baseMat && baseMat.AlbedoTexture != null)
			{
				return baseMat.AlbedoTexture;
			}
		}
		return null;
	}

	private void ApplyUberMaterialToMesh(MeshInstance3D meshInst)
	{
		if (string.Equals(_weapon.ShaderEffectType, "none", StringComparison.OrdinalIgnoreCase))
		{
			meshInst.MaterialOverride = null;
			return;
		}

		Texture2D albedoTex = FindAlbedoTexture(meshInst);
		var matInstance = meshInst.MaterialOverride as ShaderMaterial ?? new ShaderMaterial();
		ConfigureShaderMaterial(matInstance, albedoTex);
		meshInst.MaterialOverride = matInstance;
	}

	private void ApplyUberMaterialRecursively(Node node)
	{
		if (node is MeshInstance3D meshInst)
		{
			ApplyUberMaterialToMesh(meshInst);
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

	private void HandleImpactState(float dt)
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
	}

	private bool CheckFlightFailsafes()
	{
		if (_weapon.MaxLifetime > 0.0f && _elapsedTime >= _weapon.MaxLifetime) return true;
		if (_weapon.FailsafeRange > 0.0f && GlobalPosition.DistanceTo(_startPosition) >= _weapon.FailsafeRange) return true;
		if (_elapsedTime >= _totalFlightDuration * 3.0f + 5.0f) return true;
		return false;
	}

	private Vector3 GetCurrentTargetPosition()
	{
		Vector3 currentTarget = _initialTargetPosition;
		if (_targetEntity != default && Realm.Client.Core.GameHost.Instance?.EcsWorld?.IsAlive(_targetEntity) == true)
		{
			if (Realm.Client.Core.GameHost.Instance.EcsWorld.Has<Position>(_targetEntity))
			{
				var posComp = Realm.Client.Core.GameHost.Instance.EcsWorld.Get<Position>(_targetEntity);
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
			_currentFlightPosition = _startPosition.Lerp(currentTarget, ApplyEaseCurve(bOutT, _weapon.EaseCurve));
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
			_currentFlightPosition = currentTarget.Lerp(_startPosition, ApplyEaseCurve(bRetT, _weapon.EaseCurve));
			return false;
		}
		
		return true;
	}

	private bool ProcessHomingOrParabolicTrajectory(Vector3 currentTarget, float currentSpeed, float dt, float rawT)
	{
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
			return distToTarget <= Mathf.Max(0.5f, currentSpeed * dt * 1.5f) || rawT >= 1.0f;
		}
		
		Vector3 effectiveTarget = _initialTargetPosition.Lerp(currentTarget, Mathf.Clamp(_weapon.HomingWeight * easedT, 0.0f, 1.0f));
		_currentFlightPosition = _startPosition.Lerp(effectiveTarget, easedT);
		return rawT >= 1.0f;
	}

	private bool UpdateFlightTrajectory(string trajType, Vector3 currentTarget, float currentSpeed, float dt, float rawT)
	{
		bool isLinearVector = string.Equals(trajType, "LinearVector", StringComparison.OrdinalIgnoreCase) || 
		                      string.Equals(trajType, "Linear", StringComparison.OrdinalIgnoreCase) || 
		                      string.Equals(trajType, "Piercing", StringComparison.OrdinalIgnoreCase);
		bool isBoomerang = string.Equals(trajType, "Boomerang", StringComparison.OrdinalIgnoreCase);

		if (isLinearVector) return ProcessLinearTrajectory(currentSpeed, dt, rawT);
		if (isBoomerang) return ProcessBoomerangTrajectory(currentTarget);
		return ProcessHomingOrParabolicTrajectory(currentTarget, currentSpeed, dt, rawT);
	}

	private float CalculateArcY(float rawT)
	{
		if (_weapon.MaxBounces > 0)
		{
			int segments = 1 + _weapon.MaxBounces;
			float segmentProgress = rawT * segments;
			int currentBounce = Mathf.Min((int)Mathf.Floor(segmentProgress), segments - 1);
			float localU = segmentProgress - currentBounce;
			float bounceHeight = _weapon.ArcHeight / (1.0f + currentBounce * 0.6f);
			return 4.0f * bounceHeight * localU * (1.0f - localU);
		}
		
		if (_weapon.ArcHeight > 0.0f)
		{
			return 4.0f * _weapon.ArcHeight * rawT * (1.0f - rawT);
		}
		
		return 0.0f;
	}

	private Vector3 CalculateSpiralOffset(Vector3 right, Vector3 up)
	{
		if (_weapon.SpiralRadius <= 0.0f || _weapon.SpiralFrequency <= 0.0f) return Vector3.Zero;
		float theta = Mathf.Tau * _weapon.SpiralFrequency * _elapsedTime;
		return (right * Mathf.Cos(theta) + up * Mathf.Sin(theta)) * _weapon.SpiralRadius;
	}

	private Vector3 CalculateZigzagOffset(Vector3 right)
	{
		if (_weapon.ZigzagAmplitude <= 0.0f || _weapon.ZigzagFrequency <= 0.0f) return Vector3.Zero;
		float phi = Mathf.Tau * _weapon.ZigzagFrequency * _elapsedTime;
		return right * (Mathf.Sin(phi) * _weapon.ZigzagAmplitude);
	}

	private Vector3 CalculateOrbitOffset(string trajType, Vector3 right, Vector3 up)
	{
		bool isOrbit = string.Equals(trajType, "SwarmOrbit", StringComparison.OrdinalIgnoreCase) || 
		               string.Equals(trajType, "Swarm", StringComparison.OrdinalIgnoreCase);
		if (!isOrbit) return Vector3.Zero;
		
		float radius = _weapon.OrbitRadius > 0.0f ? _weapon.OrbitRadius : 1.0f;
		float speed = _weapon.OrbitSpeed > 0.0f ? _weapon.OrbitSpeed : 10.0f;
		float angle = _elapsedTime * speed;
		return (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
	}

	private void UpdateVisualTransforms(Vector3 nextPos, float dt, float rawT)
	{
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

		Vector3 meshScale = _weapon.MeshScaleOffset.ToGodotVector3();
		Vector3 baseScale = (meshScale == Vector3.Zero) ? Vector3.One : meshScale;
		baseScale = SafeScale(baseScale);
		float lifetimeScale = Mathf.Max(0.001f, CalculateScaleOverLifetime(rawT, _weapon.ScaleCurve));
		_visualTransformContainer.Scale = SafeScale(baseScale * lifetimeScale);

		Vector3 trailPos = GlobalPosition + GlobalTransform.Basis * _weapon.TrailOffset.ToGodotVector3();
		UpdateTrail(dt, trailPos);
	}

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
			HandleImpact(GlobalPosition);
			return;
		}

		Vector3 currentTarget = GetCurrentTargetPosition();
		float currentSpeed = CalculateSpeed(_speed, _elapsedTime, _totalFlightDuration, _weapon.SpeedCurve, _weapon.Acceleration);
		_elapsedTime += dt;
		float rawT = Mathf.Clamp(_elapsedTime / _totalFlightDuration, 0.0f, 1.0f);

		string trajType = _weapon.TrajectoryType ?? "Parabolic";
		
		bool impactOccurred = UpdateFlightTrajectory(trajType, currentTarget, currentSpeed, dt, rawT);
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
		Vector3 orbitOffset = CalculateOrbitOffset(trajType, right, up);

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

	private Vector3 CalculateRibbonForward(int index)
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

	private Vector3 CalculateRibbonSide(Vector3 forward, Vector3 p, Vector3 camPos)
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

	private void AddRibbonVertex(Vector3 p, float t, Vector3 side, float baseWidth, Color baseCol)
	{
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
			
			Vector3 forward = CalculateRibbonForward(i);
			Vector3 side = CalculateRibbonSide(forward, p, camPos);
			
			AddRibbonVertex(p, t, side, baseWidth, baseCol);
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

		if (!string.IsNullOrEmpty(_weapon.ImpactVisualEffect) && Realm.Client.Core.GameHost.Instance != null)
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
		float speed = Mathf.Max(0.1f, baseSpeed + (acceleration * elapsedTime));

		if (string.IsNullOrEmpty(speedCurve) || speedCurve == "constant")
			return speed;

		float progress = Mathf.Clamp(elapsedTime / Mathf.Max(0.001f, totalDuration), 0.0f, 1.0f);

		return speedCurve.ToLowerInvariant() switch
		{
			"accelerate" or "ease_in" => speed * (0.3f + 1.4f * progress * progress),
			"decelerate" or "ease_out" => speed * (1.7f - 1.4f * progress * progress),
			"ease_in_out" => speed * (0.4f + 1.2f * (progress * progress * (3.0f - 2.0f * progress))),
			"rocket_boost" => speed * (0.15f + 2.35f * Mathf.Pow(progress, 3.0f)),
			"burst" => speed * (1.0f + 1.5f * Mathf.Exp(-4.0f * progress)),
			_ => speed,
		};
	}

	private static float CalculateSquashStretchScale(float t)
	{
		if (t < 0.2f) return Mathf.Max(0.001f, (t / 0.2f) * 1.2f);
		if (t < 0.4f) return Mathf.Max(0.001f, 1.2f - ((t - 0.2f) / 0.2f) * 0.2f);
		if (t > 0.85f) return Mathf.Max(0.001f, (1.0f - t) / 0.15f);
		return 1.0f;
	}

	private static float CalculateScaleOverLifetime(float t, string scaleCurve)
	{
		if (string.IsNullOrEmpty(scaleCurve) || scaleCurve == "constant" || scaleCurve == "none")
			return 1.0f;

		return scaleCurve.ToLowerInvariant() switch
		{
			"grow" => Mathf.Clamp(t * 1.5f, 0.001f, 1.0f),
			"shrink" => Mathf.Clamp(1.0f - t, 0.001f, 1.0f),
			"grow_shrink" => Mathf.Max(0.001f, Mathf.Sin(Mathf.Clamp(t, 0.0f, 1.0f) * Mathf.Pi)),
			"squash_stretch" => CalculateSquashStretchScale(t),
			"impact_shrink" => t > 0.8f ? Mathf.Clamp((1.0f - t) / 0.2f, 0.001f, 1.0f) : 1.0f,
			_ => 1.0f,
		};
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