using Godot;
using System;
using System.IO;

namespace Realm.Godot.VFX;

public partial class SpellParticleInstance3D : Node3D
{
	private GpuParticles3D _particles;
	private ParticleProcessMaterial _processMaterial;
	private StandardMaterial3D _billboardMaterial;
	private QuadMesh _quadMesh;
	private SpellParticleConfig _config = new();

	public SpellParticleConfig Config => _config;

	public SpellParticleInstance3D()
	{
	}

	public SpellParticleInstance3D(SpellParticleConfig config)
	{
		Initialize(config);
	}

	public override void _Ready()
	{
		EnsureComponents();
	}

	private void EnsureComponents()
	{
		if (_particles == null)
		{
			_particles = new GpuParticles3D();
			_particles.Name = "SpellGpuParticles";
			AddChild(_particles);
		}

		if (_processMaterial == null)
		{
			_processMaterial = new ParticleProcessMaterial();
			_particles.ProcessMaterial = _processMaterial;
		}

		if (_billboardMaterial == null)
		{
			_billboardMaterial = new StandardMaterial3D();
			_billboardMaterial.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
			_billboardMaterial.BillboardKeepScale = true;
			_billboardMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			_billboardMaterial.VertexColorUseAsAlbedo = true;
		}

		if (_quadMesh == null)
		{
			_quadMesh = new QuadMesh();
			_quadMesh.Size = Vector2.One;
			_quadMesh.Material = _billboardMaterial;
		}
	}

	public void Initialize(SpellParticleConfig config)
	{
		_config = config?.Clone() ?? new SpellParticleConfig();
		EnsureComponents();
		ApplyConfig();
	}

	public void UpdateConfig(SpellParticleConfig config)
	{
		if (config == null) return;
		_config = config.Clone();
		EnsureComponents();
		ApplyConfig();
	}

	public void Restart()
	{
		if (_particles != null)
		{
			_particles.Restart();
			_particles.Emitting = true;
		}
	}

	public void Stop()
	{
		if (_particles != null)
		{
			_particles.Emitting = false;
		}
	}

	public float SpeedScale
	{
		get => (float)(_particles?.SpeedScale ?? 1.0);
		set
		{
			if (_particles != null) _particles.SpeedScale = value;
		}
	}

	private void ApplyConfig()
	{
		if (_particles == null || _processMaterial == null) return;

		_particles.Amount = Math.Max(1, _config.Amount);
		_particles.Lifetime = Math.Max(0.05f, _config.Lifetime);
		_particles.Explosiveness = Mathf.Clamp(_config.Explosiveness, 0.0f, 1.0f);
		_particles.Randomness = Mathf.Clamp(_config.Randomness, 0.0f, 1.0f);
		_particles.LocalCoords = _config.LocalCoords;

		ApplyEmitterShape();
		ApplyMotion();
		ApplyColorsAndRamp();
		ApplyScaleCurve();
		ApplyRenderModeAndMesh();

		_particles.Restart();
		_particles.Emitting = true;
	}

	private void ApplyEmitterShape()
	{
		switch (_config.EmitterShape)
		{
			case SpellParticleShape.Point:
				_processMaterial.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Point;
				break;

			case SpellParticleShape.Sphere:
				_processMaterial.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
				_processMaterial.EmissionSphereRadius = Math.Max(0.01f, _config.SphereRadius);
				break;

			case SpellParticleShape.Box:
				_processMaterial.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
				_processMaterial.EmissionBoxExtents = new Vector3(
					Math.Max(0.01f, _config.BoxExtents.X),
					Math.Max(0.01f, _config.BoxExtents.Y),
					Math.Max(0.01f, _config.BoxExtents.Z)
				);
				break;

			case SpellParticleShape.Ring:
				_processMaterial.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring;
				_processMaterial.EmissionRingRadius = Math.Max(0.05f, _config.RingRadius);
				_processMaterial.EmissionRingInnerRadius = Math.Max(0.0f, _config.RingInnerRadius);
				_processMaterial.EmissionRingHeight = Math.Max(0.01f, _config.RingHeight);
				_processMaterial.EmissionRingAxis = Vector3.Up;
				break;
		}
	}

	private void ApplyMotion()
	{
		Vector3 dir = _config.Direction.ToGodotVector3();
		if (dir.LengthSquared() < 0.001f) dir = Vector3.Up;
		_processMaterial.Direction = dir.Normalized();
		_processMaterial.Spread = Mathf.Clamp(_config.SpreadDegrees, 0.0f, 180.0f);

		_processMaterial.InitialVelocityMin = _config.InitialVelocityMin;
		_processMaterial.InitialVelocityMax = Math.Max(_config.InitialVelocityMin, _config.InitialVelocityMax);

		_processMaterial.Gravity = _config.Gravity.ToGodotVector3();
		Vector3 linearAccel = _config.LinearAccel.ToGodotVector3();
		_processMaterial.LinearAccelMin = linearAccel.Length() * -0.5f;
		_processMaterial.LinearAccelMax = linearAccel.Length();
		_processMaterial.RadialAccelMin = _config.RadialAccel * 0.5f;
		_processMaterial.RadialAccelMax = _config.RadialAccel;
		_processMaterial.TangentialAccelMin = _config.TangentialAccel * -0.5f;
		_processMaterial.TangentialAccelMax = _config.TangentialAccel;
		_processMaterial.DampingMin = _config.Damping * 0.8f;
		_processMaterial.DampingMax = _config.Damping * 1.2f;
	}

	private void ApplyColorsAndRamp()
	{
		Color cStart = VfxShaderManager.ParseColorSafe(_config.ColorStart, Colors.Gold);
		cStart.A = Mathf.Clamp(_config.AlphaStart, 0.0f, 1.0f);

		Color cMid = VfxShaderManager.ParseColorSafe(_config.ColorMid, Colors.DarkOrange);
		cMid.A = Mathf.Clamp(_config.AlphaMid, 0.0f, 1.0f);

		Color cEnd = VfxShaderManager.ParseColorSafe(_config.ColorEnd, Colors.Maroon);
		cEnd.A = Mathf.Clamp(_config.AlphaEnd, 0.0f, 1.0f);

		var gradient = new Gradient();
		gradient.SetColor(0, cStart);
		if (gradient.GetPointCount() > 1)
		{
			gradient.SetColor(1, cEnd);
			gradient.AddPoint(0.5f, cMid);
		}
		else
		{
			gradient.AddPoint(0.5f, cMid);
			gradient.AddPoint(1.0f, cEnd);
		}

		var gradTex = new GradientTexture1D();
		gradTex.Gradient = gradient;
		_processMaterial.ColorRamp = gradTex;
		_processMaterial.Color = Colors.White;
	}

	private void ApplyScaleCurve()
	{
		_processMaterial.ScaleMin = Math.Max(0.01f, _config.InitialScaleMin);
		_processMaterial.ScaleMax = Math.Max(_config.InitialScaleMin, _config.InitialScaleMax);

		var curve = new Curve();
		curve.AddPoint(new Vector2(0.0f, 1.0f));
		curve.AddPoint(new Vector2(0.6f, 1.0f));
		curve.AddPoint(new Vector2(1.0f, Mathf.Clamp(_config.EndScaleRatio, 0.0f, 2.0f)));

		var curveTex = new CurveTexture();
		curveTex.Curve = curve;
		_processMaterial.ScaleCurve = curveTex;
	}

	private void ApplyRenderModeAndMesh()
	{
		if (_config.RenderMode == SpellParticleRenderMode.Mesh && !string.IsNullOrEmpty(_config.MeshAssetPath))
		{
			Mesh loadedMesh = LoadProjectileMesh(_config.MeshAssetPath);
			if (loadedMesh != null)
			{
				_particles.DrawPass1 = loadedMesh;
				return;
			}
		}

		_particles.DrawPass1 = _quadMesh;

		if (_billboardMaterial != null)
		{
			_billboardMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			_billboardMaterial.BlendMode = _config.BlendMode == VfxBlendMode.Additive 
				? BaseMaterial3D.BlendModeEnum.Add 
				: BaseMaterial3D.BlendModeEnum.Mix;

			Texture2D particleTex = !string.IsNullOrEmpty(_config.ParticleTexture) 
				? VfxShaderManager.LoadTextureSafe(_config.ParticleTexture) 
				: null;
			_billboardMaterial.AlbedoTexture = particleTex;

			var sheetMeta = !string.IsNullOrEmpty(_config.ParticleTexture)
				? VfxShaderManager.GetSpritesheetMetadataSafe(_config.ParticleTexture)
				: null;

			bool isAnimatedFlipbook = sheetMeta != null && (sheetMeta.Value.Columns > 1 || sheetMeta.Value.Rows > 1);
			_billboardMaterial.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
			_billboardMaterial.BillboardKeepScale = true;

			if (isAnimatedFlipbook)
			{
				int cols = Math.Max(1, sheetMeta!.Value.Columns);
				int rows = Math.Max(1, sheetMeta.Value.Rows);
				int totalFrames = cols * rows;

				_billboardMaterial.ParticlesAnimHFrames = cols;
				_billboardMaterial.ParticlesAnimVFrames = rows;
				_billboardMaterial.ParticlesAnimLoop = true;

				float lifetime = Math.Max(0.05f, _config.Lifetime);
				float fps = sheetMeta.Value.Fps > 0.001f ? sheetMeta.Value.Fps : 20.0f;
				float speed = (fps / totalFrames) * lifetime;

				_processMaterial.AnimSpeedMin = speed;
				_processMaterial.AnimSpeedMax = speed;
				_processMaterial.AnimOffsetMin = 0.0f;
				_processMaterial.AnimOffsetMax = 0.0f;
			}
			else
			{
				_billboardMaterial.ParticlesAnimHFrames = 1;
				_billboardMaterial.ParticlesAnimVFrames = 1;
				_billboardMaterial.ParticlesAnimLoop = false;

				_processMaterial.AnimSpeedMin = 0.0f;
				_processMaterial.AnimSpeedMax = 0.0f;
			}

			if (_config.EmissionEnergy > 0.01f)
			{
				_billboardMaterial.EmissionEnabled = true;
				_billboardMaterial.EmissionEnergyMultiplier = _config.EmissionEnergy;
				_billboardMaterial.Emission = VfxShaderManager.ParseColorSafe(_config.ColorStart, Colors.Gold);
			}
			else
			{
				_billboardMaterial.EmissionEnabled = false;
			}
		}
	}

	private Mesh LoadProjectileMesh(string assetPath)
	{
		try
		{
			string fullPath = assetPath;
			if (!fullPath.StartsWith("res://") && !fullPath.StartsWith("user://") && !File.Exists(fullPath))
			{
				string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
				string[] candidates = new[]
				{
					Path.Combine(wsPath, "Assets", "models", "projectiles", Path.GetFileName(assetPath)),
					Path.Combine(wsPath, "Assets", "models", "props", Path.GetFileName(assetPath)),
					Path.Combine(wsPath, assetPath),
					Path.Combine("MapTemplate", "Assets", "models", "projectiles", Path.GetFileName(assetPath))
				};

				foreach (var c in candidates)
				{
					if (File.Exists(c))
					{
						fullPath = c;
						break;
					}
				}
			}

			Node node = Realm.Godot.Utils.ModelCache.GetModel(fullPath);
			if (node != null)
			{
				Mesh foundMesh = FindFirstMesh(node);
				node.QueueFree();
				if (foundMesh != null) return foundMesh;
			}
		}
		catch { }

		var fallbackBox = new BoxMesh();
		fallbackBox.Size = Vector3.One * 0.2f;
		return fallbackBox;
	}

	private Mesh FindFirstMesh(Node node)
	{
		if (node is MeshInstance3D mi && mi.Mesh != null)
		{
			Mesh mesh = mi.Mesh;
			if (mi.MaterialOverride != null)
			{
				mesh = (Mesh)mesh.Duplicate();
				for (int i = 0; i < mesh.GetSurfaceCount(); i++)
				{
					if (mesh.SurfaceGetMaterial(i) == null)
					{
						mesh.SurfaceSetMaterial(i, mi.MaterialOverride);
					}
				}
			}
			return mesh;
		}
		foreach (Node child in node.GetChildren())
		{
			var m = FindFirstMesh(child);
			if (m != null) return m;
		}
		return null;
	}
}
