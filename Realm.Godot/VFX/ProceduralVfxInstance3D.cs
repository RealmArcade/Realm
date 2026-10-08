using Arch.Core;
using Godot;
using System;

namespace Realm.Godot.VFX;

public partial class ProceduralVfxInstance3D : Node3D
{
	private Node3D _contentRoot;
	private MeshInstance3D _meshInstance;
	private ShaderMaterial _material;
	private SpellParticleInstance3D _particleInstance;
	private StaticBody3D _editorStaticBody;
	private CollisionShape3D _editorCollisionShape;

	private VfxAttachmentConfig _config = new();
	public VfxAttachmentConfig Config => _config;

	public Entity Entity { get; set; }
	public string VfxId { get; set; } = "vfx_primitive";

	private MeshInstance3D _selectionRing;
	private bool _isSelected = false;
	private MeshInstance3D _hoverRing;
	private bool _isHovered = false;
	private MeshInstance3D _editorBaseRing;

	private static StandardMaterial3D _sharedSelectionMaterial;
	private static StandardMaterial3D _sharedHoverMaterial;
	private static StandardMaterial3D _sharedEditorBaseMaterial;

	public bool IsPreview { get; set; } = false;

	public bool IsHovered
	{
		get => _isHovered;
		set
		{
			_isHovered = value;
			if (!ShouldShowEditorBaseRing())
			{
				if (_hoverRing != null) _hoverRing.Visible = false;
				return;
			}
			if (_hoverRing == null && _isHovered)
			{
				CreateHoverRing();
			}
			if (_hoverRing != null)
			{
				_hoverRing.Visible = _isHovered && !_isSelected;
			}
		}
	}

	public bool IsSelected
	{
		get => _isSelected;
		set
		{
			_isSelected = value;
			if (!ShouldShowEditorBaseRing())
			{
				if (_selectionRing != null) _selectionRing.Visible = false;
				return;
			}
			if (_selectionRing == null && _isSelected)
			{
				CreateSelectionRing();
			}
			if (_selectionRing != null)
			{
				_selectionRing.Visible = _isSelected;
			}
			if (_hoverRing != null)
			{
				_hoverRing.Visible = _isHovered && !_isSelected;
			}
		}
	}

	public ProceduralVfxInstance3D()
	{
	}

	public ProceduralVfxInstance3D(VfxAttachmentConfig config)
	{
		Initialize(config);
	}

	public override void _Ready()
	{
		EnsureMeshAndMaterial();
		SetupEditorCollision();
		SetupEditorBaseRing();
	}

	private bool ShouldShowEditorBaseRing()
	{
		if (!IsBaseEnvironmentValid()) return false;
		if (HasInvalidParentNode()) return false;
		return true;
	}

	private bool IsBaseEnvironmentValid()
	{
		if (IsPreview) return false;
		if (GameHost.Instance == null) return false;
		if (!GameHost.Instance.IsMapEditorMode) return false;
		if (RuntimeTerrain.IsMinimapRendering) return false;
		if (GetViewport() is SubViewport) return false;
		return true;
	}

	private bool HasInvalidParentNode()
	{
		Node parent = GetParent();
		while (parent != null)
		{
			if (IsInvalidParent(parent)) return true;
			parent = parent.GetParent();
		}
		return false;
	}

	private bool IsInvalidParent(Node parent)
	{
		if (parent is SubViewport) return true;
		if (parent is Unit3D) return true;
		if (parent is Prop3D) return true;
		if (parent is VisualProjectile3D) return true;
		
		string parentName = parent.Name.ToString();
		if (parentName.Contains("Preview", StringComparison.OrdinalIgnoreCase)) return true;
		if (parentName.Contains("Dialog", StringComparison.OrdinalIgnoreCase)) return true;
		
		return false;
	}

	private void SetupEditorBaseRing()
	{
		bool isEditor = ShouldShowEditorBaseRing();
		if (!isEditor)
		{
			if (_editorBaseRing != null) _editorBaseRing.Visible = false;
			return;
		}

		if (_editorBaseRing == null)
		{
			float radius = GetSelectionRadius();
			_editorBaseRing = new MeshInstance3D
			{
				Name = "_editor_base_ring",
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				GIMode = GeometryInstance3D.GIModeEnum.Disabled,
				Mesh = CreateTorusMesh(radius),
				Position = new Vector3(0, 0.04f, 0),
				MaterialOverride = GetOrCreateEditorBaseMaterial(),
				Visible = true
			};
			AddChild(_editorBaseRing);
		}
		else
		{
			_editorBaseRing.Visible = true;
			_editorBaseRing.Mesh = CreateTorusMesh(GetSelectionRadius());
		}
	}

	public void SetEditorBaseRingVisible(bool visible)
	{
		if (visible && ShouldShowEditorBaseRing())
		{
			SetupEditorBaseRing();
		}
		else if (_editorBaseRing != null && GodotObject.IsInstanceValid(_editorBaseRing))
		{
			_editorBaseRing.Visible = false;
		}
	}

	private void EnsureContentRoot()
	{
		if (_contentRoot == null)
		{
			_contentRoot = new Node3D();
			_contentRoot.Name = "VfxContentRoot";
			AddChild(_contentRoot);
		}
		_contentRoot.Position = _config.PositionOffset.ToGodotVector3();
		_contentRoot.RotationDegrees = _config.RotationOffset.ToGodotVector3();
		Vector3 scale = _config.ScaleOffset.ToGodotVector3();
		if (Mathf.IsZeroApprox(scale.X)) scale.X = 1f;
		if (Mathf.IsZeroApprox(scale.Y)) scale.Y = 1f;
		if (Mathf.IsZeroApprox(scale.Z)) scale.Z = 1f;
		_contentRoot.Scale = scale;
	}

	private void EnsureMeshAndMaterial()
	{
		EnsureContentRoot();

		if (_config.PrimitiveType == VfxPrimitiveType.ParticleSystem)
		{
			if (_meshInstance != null) _meshInstance.Visible = false;
			if (_particleInstance == null)
			{
				_particleInstance = new SpellParticleInstance3D();
				_particleInstance.Name = "VfxParticleInstance";
				_contentRoot.AddChild(_particleInstance);
			}
			_particleInstance.Visible = true;
			var pConfig = _config.ParticleConfig ?? SpellParticleConfig.CreatePreset(_config.VfxId);
			_particleInstance.UpdateConfig(pConfig);
			return;
		}

		if (_particleInstance != null)
		{
			_particleInstance.Visible = false;
		}

		if (_meshInstance == null)
		{
			_meshInstance = new MeshInstance3D();
			_meshInstance.Name = "VfxPrimitiveMesh";
			_meshInstance.LodBias = 100.0f;
			_contentRoot.AddChild(_meshInstance);
		}
		_meshInstance.Visible = true;

		if (_material == null)
		{
			_material = VfxShaderManager.CreateMaterial(_config);
			_meshInstance.MaterialOverride = _material;
		}

		UpdateMesh();
	}

	private void UpdateMesh()
	{
		if (_config.PrimitiveType == VfxPrimitiveType.ParticleSystem)
		{
			UpdateCollisionShapeBounds();
			return;
		}

		if (_meshInstance != null)
		{
			_meshInstance.Mesh = ProceduralVfxMeshGenerator.GetMesh(_config.PrimitiveType);
			UpdateCollisionShapeBounds();
			UpdateSelectionRingRadius();
		}
	}

	public void Initialize(VfxAttachmentConfig config)
	{
		_config = config?.Clone() ?? new VfxAttachmentConfig();
		VfxId = _config.VfxId;

		EnsureMeshAndMaterial();
		if (_config.PrimitiveType != VfxPrimitiveType.ParticleSystem && _material != null)
		{
			VfxShaderManager.ApplyConfigToMaterial(_material, _config);
		}
		UpdateMesh();
		UpdateSelectionRingRadius();
	}

	public void UpdateConfig(VfxAttachmentConfig config)
	{
		if (config == null) return;
		bool meshChanged = _config.PrimitiveType != config.PrimitiveType;
		bool blendChanged = _config.BlendMode != config.BlendMode;

		_config = config.Clone();
		VfxId = _config.VfxId;

		EnsureMeshAndMaterial();

		if (_config.PrimitiveType != VfxPrimitiveType.ParticleSystem)
		{
			if (blendChanged || meshChanged)
			{
				_material.Shader = VfxShaderManager.GetShader(_config.BlendMode, _config.PrimitiveType);
			}

			VfxShaderManager.ApplyConfigToMaterial(_material, _config);

			if (meshChanged)
			{
				UpdateMesh();
			}
		}
		else if (_particleInstance != null)
		{
			var pConfig = _config.ParticleConfig ?? SpellParticleConfig.CreatePreset(_config.VfxId);
			_particleInstance.UpdateConfig(pConfig);
			UpdateCollisionShapeBounds();
		}
		UpdateSelectionRingRadius();
	}

	public void Restart()
	{
		if (_particleInstance != null && GodotObject.IsInstanceValid(_particleInstance))
		{
			_particleInstance.Restart();
		}
		if (_meshInstance != null && GodotObject.IsInstanceValid(_meshInstance))
		{
			_meshInstance.Visible = true;
		}
	}

	public void Play()
	{
		if (_particleInstance != null && GodotObject.IsInstanceValid(_particleInstance))
		{
			_particleInstance.Restart();
		}
		if (_meshInstance != null && GodotObject.IsInstanceValid(_meshInstance))
		{
			_meshInstance.Visible = true;
		}
	}

	public void Stop()
	{
		if (_particleInstance != null && GodotObject.IsInstanceValid(_particleInstance))
		{
			_particleInstance.Stop();
		}
		if (_meshInstance != null && GodotObject.IsInstanceValid(_meshInstance))
		{
			_meshInstance.Visible = false;
		}
	}

	public void SetSpeedScale(float speed)
	{
		if (_particleInstance != null && GodotObject.IsInstanceValid(_particleInstance))
		{
			_particleInstance.SpeedScale = speed;
		}
	}

	private void SetupEditorCollision()
	{
		bool isEditor = ShouldShowEditorBaseRing();
		if (!isEditor) return;

		EnsureContentRoot();

		if (_editorStaticBody == null)
		{
			_editorStaticBody = new StaticBody3D();
			_editorStaticBody.Name = "EditorCollider";
			_editorStaticBody.CollisionLayer = 1u;
			_editorStaticBody.CollisionMask = 0;
			_contentRoot.AddChild(_editorStaticBody);

			_editorCollisionShape = new CollisionShape3D();
			_editorCollisionShape.Name = "CollisionShape";
			_editorStaticBody.AddChild(_editorCollisionShape);

			UpdateCollisionShapeBounds();
		}
	}

	private void UpdateCollisionShapeBounds()
	{
		if (_editorCollisionShape == null) return;

		if (_config.PrimitiveType == VfxPrimitiveType.ParticleSystem)
		{
			float radius = GetSelectionRadius();
			var particleBox = new BoxShape3D();
			particleBox.Size = new Vector3(radius * 2.0f, MathF.Max(0.8f, radius * 1.5f), radius * 2.0f);
			_editorCollisionShape.Shape = particleBox;
			_editorCollisionShape.Position = new Vector3(0.0f, particleBox.Size.Y * 0.5f, 0.0f);
			return;
		}

		if (_meshInstance?.Mesh == null) return;

		Aabb bounds = _meshInstance.Mesh.GetAabb();
		Vector3 size = bounds.Size;
		size.X = Mathf.Max(size.X, 0.4f);
		size.Y = Mathf.Max(size.Y, 0.4f);
		size.Z = Mathf.Max(size.Z, 0.4f);

		var box = new BoxShape3D();
		box.Size = size;
		_editorCollisionShape.Shape = box;
		_editorCollisionShape.Position = bounds.GetCenter();
	}

	public void SetEditorCollisionEnabled(bool enabled)
	{
		if (_editorStaticBody != null && GodotObject.IsInstanceValid(_editorStaticBody))
		{
			_editorStaticBody.CollisionLayer = enabled ? 1u : 0u;
		}
	}

	private float GetSelectionRadius()
	{
		if (_config.PrimitiveType == VfxPrimitiveType.ParticleSystem)
		{
			var pConfig = _config.ParticleConfig ?? SpellParticleConfig.CreatePreset(_config.VfxId);
			if (pConfig != null)
			{
				if (pConfig.EmitterShape == SpellParticleShape.Sphere)
				{
					return Mathf.Max(1.0f, pConfig.SphereRadius * 1.2f);
				}
				if (pConfig.EmitterShape == SpellParticleShape.Ring)
				{
					return Mathf.Max(1.0f, pConfig.RingRadius * 1.1f);
				}
				if (pConfig.EmitterShape == SpellParticleShape.Box)
				{
					float maxHorizontal = Mathf.Max(pConfig.BoxExtents.X, pConfig.BoxExtents.Z);
					return Mathf.Max(1.0f, maxHorizontal * 1.2f);
				}
			}
			return 1.5f;
		}

		if (_meshInstance?.Mesh != null)
		{
			Aabb bounds = _meshInstance.Mesh.GetAabb();
			float maxHorizontal = Mathf.Max(bounds.Size.X, bounds.Size.Z) * 0.5f;
			return Mathf.Max(1.0f, maxHorizontal * 1.15f);
		}

		return 1.5f;
	}

	private static StandardMaterial3D GetOrCreateEditorBaseMaterial()
	{
		if (_sharedEditorBaseMaterial == null || !GodotObject.IsInstanceValid(_sharedEditorBaseMaterial))
		{
			_sharedEditorBaseMaterial = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.72f, 0.22f, 0.95f, 0.75f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				DisableReceiveShadows = true,
				EmissionEnabled = true,
				Emission = new Color(0.72f, 0.22f, 0.95f),
				EmissionEnergyMultiplier = 0.5f,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			};
		}
		return _sharedEditorBaseMaterial;
	}

	private static StandardMaterial3D GetOrCreateSelectionMaterial()
	{
		if (_sharedSelectionMaterial == null || !GodotObject.IsInstanceValid(_sharedSelectionMaterial))
		{
			_sharedSelectionMaterial = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.22f, 0.54f, 0.26f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				DisableReceiveShadows = true,
				EmissionEnabled = false,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			};
		}
		return _sharedSelectionMaterial;
	}

	private static StandardMaterial3D GetOrCreateHoverMaterial()
	{
		if (_sharedHoverMaterial == null || !GodotObject.IsInstanceValid(_sharedHoverMaterial))
		{
			_sharedHoverMaterial = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.88f, 0.88f, 0.88f, 0.22f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				DisableReceiveShadows = true,
				EmissionEnabled = false,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			};
		}
		return _sharedHoverMaterial;
	}

	private static TorusMesh CreateTorusMesh(float radius)
	{
		float outer = Mathf.Max(0.5f, radius);
		float inner = Mathf.Max(0.2f, outer - 0.25f);
		return new TorusMesh
		{
			InnerRadius = inner,
			OuterRadius = outer
		};
	}

	private void CreateSelectionRing()
	{
		if (_selectionRing != null) return;
		float radius = GetSelectionRadius();
		_selectionRing = new MeshInstance3D
		{
			Name = "_selection_ring",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
			Mesh = CreateTorusMesh(radius),
			Position = new Vector3(0, 0.05f, 0),
			MaterialOverride = GetOrCreateSelectionMaterial(),
			Visible = _isSelected
		};
		AddChild(_selectionRing);
	}

	private void CreateHoverRing()
	{
		if (_hoverRing != null) return;
		float radius = GetSelectionRadius();
		_hoverRing = new MeshInstance3D
		{
			Name = "_hover_ring",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
			Mesh = CreateTorusMesh(radius),
			Position = new Vector3(0, 0.05f, 0),
			MaterialOverride = GetOrCreateHoverMaterial(),
			Visible = _isHovered && !_isSelected
		};
		AddChild(_hoverRing);
	}

	private void UpdateSelectionRingRadius()
	{
		if (_selectionRing == null && _hoverRing == null && _editorBaseRing == null) return;
		float radius = GetSelectionRadius();
		if (_selectionRing != null)
		{
			_selectionRing.Mesh = CreateTorusMesh(radius);
		}
		if (_hoverRing != null)
		{
			_hoverRing.Mesh = CreateTorusMesh(radius);
		}
		if (_editorBaseRing != null)
		{
			bool isEditor = ShouldShowEditorBaseRing();
			_editorBaseRing.Visible = isEditor;
			if (isEditor)
			{
				_editorBaseRing.Mesh = CreateTorusMesh(radius);
			}
		}
	}
}
