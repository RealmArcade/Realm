using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Services;
using Realm.Client.VFX;
using Realm.Shared.Audio;
using Realm.Shared.Textures;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class AbilityVfxDialog : FloatingPreview3DDialogBase
{
	public AbilityVfxDialog() { }

	private ProceduralVfxInstance3D? _vfxInstance;
	private MeshInstance3D _aoeRingMesh = null!;
	private MeshInstance3D _aoeDiskMesh = null!;
	private MeshInstance3D _groundGrid = null!;
	private AudioStreamPlayer? _sfxPlayer;

	private TextureRect _iconPreviewRect = null!;
	private LineEdit _txtIconPath = null!;
	private Action<string> _setIconPathValue = null!;

	private LineEdit _txtVisualEffect = null!;
	private Action<string> _setVisualEffectValue = null!;
	private HSlider _sldAoeRadius = null!;
	private Label _lblAoeRadiusVal = null!;
	private LineEdit _txtCastSound = null!;
	private Action<string> _setCastSoundValue = null!;

	private string _abilityId = "";
	private string _slug = "";
	private Label _lblObjectTypePrefix = null!;
	private LineEdit _txtSlug = null!;
	private LineEdit _txtName = null!;
	private bool _isUpdatingUI = false;
	private string _abilityName = "";
	private string _initialVisualEffect = "";
	private string _initialCastSound = "";
	private string _initialIconPath = "";
	private float _initialAoeRadius = 0.0f;
	private string? _initialTemplateJson = null;
	private bool _templateExistedInitially = false;

	private string _currentVisualEffect = "";
	private string _currentCastSound = "";
	private string _currentIconPath = "";
	private float _currentAoeRadius = 4.0f;
	private float _playbackSpeed = 1.0f;
	private Action<JsonObject>? _onApplied;

	public AbilityVfxDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Ability VFX & Audio Studio"), new Vector2(500, 720))
	{
		_sfxPlayer = new AudioStreamPlayer { Name = "AbilityPreviewSfx", Bus = AudioService.PreviewBusName };
		AddChild(_sfxPlayer);

		DefaultDistance = 8.0f;
		CameraDistance = 8.0f;
		DefaultYaw = Mathf.DegToRad(45.0f);
		DefaultPitch = Mathf.DegToRad(30.0f);
		CameraYaw = Mathf.DegToRad(45.0f);
		CameraPitch = Mathf.DegToRad(30.0f);
		DefaultTargetPosition = new Vector3(0, 0.5f, 0);
		TargetPosition = DefaultTargetPosition;

		BuildControls();
	}

	private void Setup3DEnvironment()
	{
		if (PreviewSceneRoot == null) return;

		_groundGrid = new MeshInstance3D();
		_groundGrid.Name = "GroundGrid";
		var planeMesh = new PlaneMesh { Size = new Vector2(24f, 24f), SubdivideWidth = 24, SubdivideDepth = 24 };
		_groundGrid.Mesh = planeMesh;
		var gridMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.12f, 0.14f, 0.18f, 0.85f),
			Roughness = 0.8f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
		};
		_groundGrid.MaterialOverride = gridMat;
		_groundGrid.Position = new Vector3(0, -0.01f, 0);
		PreviewSceneRoot.AddChild(_groundGrid);

		var diskMesh = new CylinderMesh
		{
			TopRadius = 1.0f,
			BottomRadius = 1.0f,
			Height = 0.005f
		};
		var diskMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.2f, 0.7f, 1.0f, 0.15f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
		};
		_aoeDiskMesh = new MeshInstance3D { Mesh = diskMesh, MaterialOverride = diskMat, Position = new Vector3(0, 0.01f, 0) };
		PreviewSceneRoot.AddChild(_aoeDiskMesh);

		var ringMesh = new TorusMesh
		{
			InnerRadius = 0.97f,
			OuterRadius = 1.0f,
			Rings = 48,
			RingSegments = 8
		};
		var ringMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.25f, 0.85f, 1.0f, 0.85f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
		};
		_aoeRingMesh = new MeshInstance3D { Mesh = ringMesh, MaterialOverride = ringMat, Position = new Vector3(0, 0.015f, 0) };
		PreviewSceneRoot.AddChild(_aoeRingMesh);

		UpdateAoEIndicator(_currentAoeRadius);
	}

	private void UpdateAoEIndicator(float radius)
	{
		if (_aoeRingMesh == null || _aoeDiskMesh == null) return;

		if (radius <= 0.05f)
		{
			_aoeRingMesh.Visible = false;
			_aoeDiskMesh.Visible = false;
			return;
		}

		_aoeRingMesh.Visible = true;
		_aoeDiskMesh.Visible = true;

		_aoeRingMesh.Scale = new Vector3(radius, 1.0f, radius);
		_aoeDiskMesh.Scale = new Vector3(radius, 1.0f, radius);
	}

	private void UpdateIconPreview(string iconPath)
	{
		if (_iconPreviewRect == null) return;
		var tex = ResolveTexture(iconPath);
		_iconPreviewRect.Texture = tex;
	}

	private VfxAttachmentConfig ResolveVfxConfig(string visualEffect)
	{
		if (string.IsNullOrWhiteSpace(visualEffect))
		{
			return new VfxAttachmentConfig { VfxId = "vfx_none", PrimitiveType = VfxPrimitiveType.CrossQuad };
		}

		string vfxKey = visualEffect.Trim();
		if (vfxKey.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase))
		{
			vfxKey = vfxKey.Substring(4);
		}

		if (GameContext.RegistryService.VfxRegistry.TryGetValue(vfxKey, out var regCfg))
		{
			return regCfg.Clone();
		}

		if (Enum.TryParse<VfxPrimitiveType>(vfxKey, true, out var primType))
		{
			return new VfxAttachmentConfig { VfxId = vfxKey, PrimitiveType = primType };
		}

		if (vfxKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ||
		    vfxKey.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
		    vfxKey.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
		    vfxKey.Contains('/'))
		{
			var pConfig = new SpellParticleConfig
			{
				ParticleId = vfxKey,
				Name = Path.GetFileNameWithoutExtension(vfxKey),
				RenderMode = SpellParticleRenderMode.BillboardQuad,
				ParticleTexture = vfxKey,
				Amount = 24,
				Lifetime = 1.2f,
				ColorStart = "#FFE066",
				ColorMid = "#FF6600",
				ColorEnd = "#990000",
				EmissionEnergy = 3.0f,
				InitialScaleMin = 0.4f,
				InitialScaleMax = 0.8f
			};
			return new VfxAttachmentConfig
			{
				VfxId = vfxKey,
				Name = Path.GetFileNameWithoutExtension(vfxKey),
				PrimitiveType = VfxPrimitiveType.ParticleSystem,
				ParticleConfig = pConfig
			};
		}

		return new VfxAttachmentConfig { VfxId = vfxKey, Name = vfxKey };
	}

	private void ReloadVfx()
	{
		if (PreviewSceneRoot == null) return;

		if (_vfxInstance != null && GodotObject.IsInstanceValid(_vfxInstance))
		{
			_vfxInstance.QueueFree();
			_vfxInstance = null;
		}

		if (string.IsNullOrWhiteSpace(_currentVisualEffect))
		{
			return;
		}

		var config = ResolveVfxConfig(_currentVisualEffect);
		_vfxInstance = new ProceduralVfxInstance3D(config);
		_vfxInstance.IsPreview = true;
		_vfxInstance.Name = "AbilityVfxPreview";
		PreviewSceneRoot.AddChild(_vfxInstance);
		_vfxInstance.Position = new Vector3(0, 0.5f, 0);
		_vfxInstance.SetSpeedScale(_playbackSpeed);
	}


	private void TriggerCastTest()
	{
		if (_vfxInstance != null && GodotObject.IsInstanceValid(_vfxInstance))
		{
			_vfxInstance.Restart();
		}
		else
		{
			ReloadVfx();
		}

		PlaySoundFile(_currentCastSound);
	}

	private void PlayVfxAnimation()
	{
		if (_vfxInstance != null && GodotObject.IsInstanceValid(_vfxInstance))
		{
			_vfxInstance.Play();
		}
		else
		{
			ReloadVfx();
		}
	}

	private void PauseVfxAnimation()
	{
		if (_vfxInstance != null && GodotObject.IsInstanceValid(_vfxInstance))
		{
			_vfxInstance.Stop();
		}
	}

	private void StopVfxAnimation()
	{
		if (_vfxInstance != null && GodotObject.IsInstanceValid(_vfxInstance))
		{
			_vfxInstance.Stop();
		}
	}

	public void OpenForAbility(string abilityId, JsonObject? abilityData, Action<JsonObject>? onApplied = null)
	{
		string effectiveId = GetEffectiveAbilityId(abilityId, abilityData);

		var (_, parsedSlug) = TemplateIDHelper.ParseTemplateID(effectiveId);
		_slug = !string.IsNullOrWhiteSpace(parsedSlug) ? TemplateIDHelper.ToSnakeCase(parsedSlug) : TemplateIDHelper.ToSnakeCase(effectiveId);
		_abilityId = TemplateIDHelper.NormalizeTemplateID("ability", _slug);

		ParseAbilityData(abilityData);
		_onApplied = onApplied;

		TitleLabel.Text = $"{TranslationServer.Translate("Ability VFX Studio")} - {_abilityName}";

		UpdateUIState();

		UpdateIconPreview(_currentIconPath);
		UpdateAoEIndicator(_currentAoeRadius);
		ReloadVfx();

		string targetId = !string.IsNullOrEmpty(_abilityId) ? _abilityId : _slug;
		_initialTemplateJson = GameContext.EditorService.SnapshotTemplateJson(targetId);
		_templateExistedInitially = _initialTemplateJson != null;

		OpenDialog();
		ResetCameraDefault();
	}

	protected override string GetRandomizeTemplateSlug()
	{
		return !string.IsNullOrEmpty(_abilityId) ? _abilityId : _slug;
	}

	protected override void OnAssetsRandomized()
	{
		string targetId = GetRandomizeTemplateSlug();
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var reloadedMeta) && reloadedMeta?.Templates?.Abilities != null)
		{
			var reloaded = reloadedMeta.Templates.Abilities.FirstOrDefault(a => string.Equals(a.TemplateID, targetId, StringComparison.OrdinalIgnoreCase) || string.Equals(a.TemplateID, _abilityId, StringComparison.OrdinalIgnoreCase));
			if (reloaded != null)
			{
				_currentVisualEffect = reloaded.VisualEffect ?? string.Empty;
				_currentCastSound = reloaded.CastSound ?? string.Empty;
				_currentIconPath = !string.IsNullOrEmpty(reloaded.IconPath) && reloaded.IconPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(reloaded.IconPath) : (reloaded.IconPath ?? string.Empty);
			}
		}

		UpdateUIState();
		UpdateIconPreview(_currentIconPath);
		ReloadVfx();
	}

	private string GetEffectiveAbilityId(string fallbackId, JsonObject? abilityData)
	{
		if (abilityData != null && abilityData.TryGetPropertyValue("TemplateID", out var tidNode) && !string.IsNullOrWhiteSpace(tidNode?.ToString()))
		{
			return tidNode.ToString();
		}

		if (abilityData != null && abilityData.TryGetPropertyValue("AbilityId", out var aidNode) && !string.IsNullOrWhiteSpace(aidNode?.ToString()))
		{
			return aidNode.ToString();
		}

		return fallbackId;
	}

	private void ParseAbilityData(JsonObject? abilityData)
	{
		if (abilityData == null)
		{
			SetDefaultAbilityData();
			return;
		}

		_abilityName = GetStringProperty(abilityData, "Name", _abilityId);
		_currentVisualEffect = GetStringProperty(abilityData, "VisualEffect", string.Empty);
		_currentCastSound = GetStringProperty(abilityData, "CastSound", string.Empty);
		_currentIconPath = ParseIconPath(abilityData);
		_currentAoeRadius = GetFloatProperty(abilityData, "AreaOfEffectRadius", 4.0f);

		SyncInitialState();
	}

	private void SetDefaultAbilityData()
	{
		_abilityName = _abilityId;
		_currentVisualEffect = string.Empty;
		_currentCastSound = string.Empty;
		_currentIconPath = string.Empty;
		_currentAoeRadius = 4.0f;
		SyncInitialState();
	}

	private string GetStringProperty(JsonObject data, string key, string defaultValue)
	{
		var node = data[key];
		if (node == null)
			return defaultValue;

		return node.ToString() ?? defaultValue;
	}

	private float GetFloatProperty(JsonObject data, string key, float defaultValue)
	{
		var node = data[key];
		if (node == null)
			return defaultValue;

		return (float)node;
	}

	private string ParseIconPath(JsonObject data)
	{
		string rawIcon = GetStringProperty(data, "IconPath", string.Empty);

		if (string.IsNullOrEmpty(rawIcon))
			return string.Empty;

		if (!rawIcon.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			return string.Empty;

		return Path.GetFileName(rawIcon);
	}

	private void SyncInitialState()
	{
		_initialVisualEffect = _currentVisualEffect;
		_initialCastSound = _currentCastSound;
		_initialIconPath = _currentIconPath;
		_initialAoeRadius = _currentAoeRadius;
	}

	private void UpdateUIState()
	{
		_isUpdatingUI = true;
		if (_txtSlug != null) _txtSlug.Text = _slug;
		if (_txtName != null) _txtName.Text = _abilityName;
		_setVisualEffectValue?.Invoke(_currentVisualEffect);
		_setCastSoundValue?.Invoke(_currentCastSound);
		_setIconPathValue?.Invoke(_currentIconPath);
		if (_sldAoeRadius != null) _sldAoeRadius.Value = _currentAoeRadius;
		_isUpdatingUI = false;
	}


	protected override void OnApply()
	{
		_slug = TemplateIDHelper.ResolveSlug(_txtSlug?.Text, _slug);
		_abilityId = TemplateIDHelper.NormalizeTemplateID("ability", _slug);
		if (_txtName != null)
		{
			_abilityName = _txtName.Text;
		}

		if (!string.IsNullOrEmpty(_abilityId))
		{
			Hud?.SaveCustomAbilityVfxToMetadata(
				_abilityId,
				_currentVisualEffect,
				_currentCastSound,
				_currentIconPath,
				_currentAoeRadius
			);

			var updatedData = new JsonObject
			{
				["TemplateID"] = _abilityId,
				["template_id"] = _abilityId,
				["AbilityId"] = _abilityId,
				["Name"] = _abilityName,
				["VisualEffect"] = _currentVisualEffect,
				["CastSound"] = _currentCastSound,
				["IconPath"] = _currentIconPath,
				["AreaOfEffectRadius"] = _currentAoeRadius
			};

			_onApplied?.Invoke(updatedData);
			Hud?.ShowFeedback(TranslationServer.Translate("Ability VFX and audio updated successfully."));
		}

		StopVfxAnimation();
	}

	protected override void OnCancel()
	{
		string targetId = !string.IsNullOrEmpty(_abilityId) ? _abilityId : _slug;
		if (!string.IsNullOrEmpty(targetId))
		{
			if (_templateExistedInitially)
			{
				GameContext.EditorService.RevertTemplateJson(targetId, _initialTemplateJson);
			}
			else
			{
				GameContext.EditorService.RevertTemplateJson(targetId, null);
			}
		}

		_currentVisualEffect = _initialVisualEffect;
		_currentCastSound = _initialCastSound;
		_currentIconPath = _initialIconPath;
		_currentAoeRadius = _initialAoeRadius;

		StopVfxAnimation();
	}

	public override void CloseDialog()
	{
		StopVfxAnimation();
		base.CloseDialog();
	}
}