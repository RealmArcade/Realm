using System;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.VFX;

[GlobalClass]
public partial class ProceduralVfxInstance3D : Node3D
{
	private Node3D _contentRoot;
	private MeshInstance3D _meshInstance;
	private ShaderMaterial _material;
	private SpellParticleInstance3D _particleInstance;

	private VfxAttachmentConfig _config = new();
	public VfxAttachmentConfig Config => _config;

	public Entity Entity { get; set; }
	public string VfxId { get; set; } = "vfx_primitive";
	public int PlayerIndex { get; set; } = 0;

	private const float GroundAuraSurfaceOffset = 0.03f;

	private bool _isGroundAura;
	private bool _tintWithPlayerColor;
	private Color _playerColor = Colors.White;

	public bool IsGroundAura => _isGroundAura;

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
		SetupEditorBaseRing();
		GameContext.RaycastService?.RegisterVfx(this);
	}

	public override void _ExitTree()
	{
		GameContext.RaycastService?.UnregisterVfx(this);
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
		if (GameContext.Host == null) return false;
		if (!GameContext.SessionState.IsMapEditorMode) return false;
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

		ApplyContentRootTransform();
	}

	private void ApplyContentRootTransform()
	{
		Vector3 position = _config.PositionOffset.ToGodotVector3();
		if (_isGroundAura && position.Y < GroundAuraSurfaceOffset)
		{
			position.Y = GroundAuraSurfaceOffset;
		}

		Vector3 scale = _config.ScaleOffset.ToGodotVector3();
		if (Mathf.IsZeroApprox(scale.X)) scale.X = 1f;
		if (Mathf.IsZeroApprox(scale.Y)) scale.Y = 1f;
		if (Mathf.IsZeroApprox(scale.Z)) scale.Z = 1f;

		_contentRoot.Position = position;
		_contentRoot.RotationDegrees = _config.RotationOffset.ToGodotVector3();
		_contentRoot.Scale = scale;
	}

	public void ConfigureAsGroundAura(Color playerColor)
	{
		_isGroundAura = true;
		_tintWithPlayerColor = _config.TintWithPlayerColor;
		_playerColor = playerColor;

		EnsureMeshAndMaterial();
		ApplyGroundAuraAppearance();
	}

	public void SetPlayerColor(Color playerColor)
	{
		if (!_isGroundAura || !_tintWithPlayerColor) return;

		_playerColor = playerColor;
		ApplyGroundAuraAppearance();
	}

	private void ApplyGroundAuraAppearance()
	{
		if (!_isGroundAura) return;

		if (_contentRoot != null && GodotObject.IsInstanceValid(_contentRoot))
		{
			ApplyContentRootTransform();
		}

		if (_material != null && GodotObject.IsInstanceValid(_material))
		{
			_material.SetShaderParameter("enable_depth_fade", false);
			_material.SetShaderParameter("enable_fresnel", false);
			_material.SetShaderParameter("use_player_color", _tintWithPlayerColor);
			_material.SetShaderParameter("player_color", _playerColor);
		}

		if (_particleInstance != null && GodotObject.IsInstanceValid(_particleInstance))
		{
			_particleInstance.SetPlayerColor(_tintWithPlayerColor ? _playerColor : Colors.White);
		}
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
			ApplyGroundAuraAppearance();
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
			return;
		}

		if (_meshInstance != null)
		{
			_meshInstance.Mesh = ProceduralVfxMeshGenerator.GetMesh(_config.PrimitiveType);
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
		ApplyGroundAuraAppearance();
	}

	public void UpdateConfig(VfxAttachmentConfig config)
	{
		if (config == null) return;
		bool meshChanged = _config.PrimitiveType != config.PrimitiveType;
		bool blendChanged = _config.BlendMode != config.BlendMode;

		_config = config.Clone();
		VfxId = _config.VfxId;
		_tintWithPlayerColor = _config.TintWithPlayerColor;

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
		}

		UpdateSelectionRingRadius();
		ApplyGroundAuraAppearance();
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
		return Realm.Client.Utils.SharedMaterials.GetOrCreateSelectionMaterial(new Color(0.22f, 0.54f, 0.26f));
	}

	private static StandardMaterial3D GetOrCreateHoverMaterial()
	{
		return Realm.Client.Utils.SharedMaterials.GetOrCreateHoverMaterial();
	}

	public static void ClearCache()
	{
		_sharedSelectionMaterial = null;
		_sharedHoverMaterial = null;
		_sharedEditorBaseMaterial = null;
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