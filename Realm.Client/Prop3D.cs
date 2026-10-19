using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Services;

namespace Realm.Client;

public partial class Prop3D : Node3D
{
	public Entity Entity { get; set; }

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
					ModelShaderManager.SetProceduralAnimationVelocity(this, value);
				}
			}
		}
	}

	private float _impulseStrength = 0f;
	private float _impulseDuration = 0.5f;
	private float _impulseTime = 0f;
	private float _impulseFrequency = 12.0f;

	public virtual void TriggerImpulse(float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f)
	{
		if (!HasProceduralAnimation) return;
		_impulseStrength = strength;
		_impulseDuration = duration <= 0.001f ? 0.001f : duration;
		_impulseTime = _impulseDuration;
		_impulseFrequency = frequency;
		SetProcess(true);
	}

	private string _objectId = string.Empty;

	[Export]
	public virtual string TemplateID
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World.IsAlive(Entity)
			                              && GameContext.World.Has<PropIdentity>(Entity))
				return GameContext.World.Get<PropIdentity>(Entity).PropId;
			return _objectId;
		}
		set
		{
			_objectId = value;
			_cachedResolvedModelPath = null;
			if (GameHost.Instance != null && GameContext.World.IsAlive(Entity))
			{
				var world = GameContext.World;
				world.SetOrAdd(Entity, new PropIdentity(value));
			}
		}
	}

	public virtual string PropId
	{
		get => TemplateID;
		set => TemplateID = value;
	}

	private string _cachedResolvedModelPath;

	public string ModelAssetPath
	{
		get
		{
			if (_cachedResolvedModelPath == null)
			{
				_cachedResolvedModelPath = ResolvePropModelPath(PropId);
			}

			return _cachedResolvedModelPath;
		}
	}

	public float ResourceAmount
	{
		get
		{
			if (GameHost.Instance == null || !GameContext.World.IsAlive(Entity)) return 0f;
			if (GameContext.World.Has<ResourceNode>(Entity))
				return GameContext.World.Get<ResourceNode>(Entity).Amount;
			return 0f;
		}
		set
		{
			if (GameHost.Instance == null || !GameContext.World.IsAlive(Entity)) return;
			var world = GameContext.World;
			if (world.Has<ResourceNode>(Entity))
			{
				var existing = world.Get<ResourceNode>(Entity);
				if (value < existing.Amount)
				{
					TriggerImpulse(0.35f, 0.45f);
				}

				world.Set(Entity, new ResourceNode(existing.ResourceTypeId, value));
			}
			else
			{
				world.Add(Entity, new ResourceNode(Guid.Empty, value));
			}
		}
	}

	private bool _isResource;

	public virtual bool IsResource
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World != null && Entity != default && GameContext.World.IsAlive(Entity))
			{
				if (GameContext.World.Has<ResourceNode>(Entity))
					return true;
			}

			if (GameHost.ResourceRegistry != null && !string.IsNullOrEmpty(PropId))
			{
				if (GameHost.ResourceRegistry.ContainsKey(PropId)) return true;
				string cleanId = Path.GetFileNameWithoutExtension(PropId);
				if (!string.IsNullOrEmpty(cleanId) && GameHost.ResourceRegistry.ContainsKey(cleanId)) return true;
			}

			return _isResource;
		}
		set { _isResource = value; }
	}

	private MeshInstance3D _selectionRing;
	private bool _isSelected = false;

	private MeshInstance3D _hoverRing;
	private bool _isHovered = false;

	private static readonly Dictionary<string, TorusMesh> _sharedTorusMeshes = new(StringComparer.OrdinalIgnoreCase);
	private static StandardMaterial3D _sharedSelectionMaterial;
	private static StandardMaterial3D _sharedHoverMaterial;

	public virtual float GetBaseObstacleRadius()
	{
		if (GameHost.Instance != null)
		{
			return GameHost.Instance.GetOrCalculateObstacleRadius(PropId, this);
		}

		return 1.4f;
	}

	private static TorusMesh GetOrCreateTorusMesh(float radius)
	{
		float r = Mathf.Max(0.4f, (float)Math.Round(radius, 2));
		string key = r.ToString("0.00", CultureInfo.InvariantCulture);
		if (!_sharedTorusMeshes.TryGetValue(key, out var mesh) || !GodotObject.IsInstanceValid(mesh))
		{
			mesh = new TorusMesh
			{
				InnerRadius = Mathf.Max(0.1f, r - 0.25f),
				OuterRadius = r
			};
			_sharedTorusMeshes[key] = mesh;
		}

		return mesh;
	}

	private static StandardMaterial3D GetOrCreateSelectionMaterial(Color color)
	{
		if (_sharedSelectionMaterial == null || !GodotObject.IsInstanceValid(_sharedSelectionMaterial))
		{
			_sharedSelectionMaterial = new StandardMaterial3D
			{
				AlbedoColor = color,
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

	private void UpdateSelectionRingTransform()
	{
		if (_selectionRing == null && _hoverRing == null) return;

		float sx = Mathf.Abs(Scale.X) > 0.001f ? Scale.X : 1.0f;
		float sy = Mathf.Abs(Scale.Y) > 0.001f ? Scale.Y : 1.0f;
		float sz = Mathf.Abs(Scale.Z) > 0.001f ? Scale.Z : 1.0f;

		Vector3 ringScale = new Vector3(1.0f / sx, 1.0f / sy, 1.0f / sz);
		Vector3 ringPos = new Vector3(0, 0.05f / sy, 0);

		float ratio = GameHost.Instance != null ? GameHost.Instance.GetModelCollisionCircleRatio(GameHost.Instance.GetModelAssetKey(this)) : 1.0f;
		if (ratio <= 0.001f) ratio = 1.0f;

		float baseRadius = GetBaseObstacleRadius();
		float worldRadius = baseRadius * ratio * Mathf.Abs(sx);
		var torusMesh = GetOrCreateTorusMesh(worldRadius);

		if (_selectionRing != null)
		{
			_selectionRing.Mesh = torusMesh;
			_selectionRing.Scale = ringScale;
			_selectionRing.Position = ringPos;
		}

		if (_hoverRing != null)
		{
			_hoverRing.Mesh = torusMesh;
			_hoverRing.Scale = ringScale;
			_hoverRing.Position = ringPos;
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

	private void CreateHoverRing()
	{
		if (_hoverRing != null) return;
		_hoverRing = new MeshInstance3D
		{
			Name = "_hover_ring",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
			MaterialOverride = GetOrCreateHoverMaterial(),
			Visible = _isHovered && !IsSelected
		};
		AddChild(_hoverRing);
		UpdateSelectionRingTransform();
	}

	public virtual void UpdateCollisionCircleScale(float ratio)
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

	public virtual bool IsSelected
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
		}
	}

	public virtual void SetTemporarySelectionHighlight(bool highlight)
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

	protected virtual Color GetSelectionRingColor()
	{
		return new Color(0.22f, 0.54f, 0.26f);
	}

	protected virtual void CreateSelectionRing()
	{
		if (_selectionRing != null) return;
		_selectionRing = new MeshInstance3D
		{
			Name = "_selection_ring",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
			MaterialOverride = GetOrCreateSelectionMaterial(GetSelectionRingColor()),
			Visible = _isSelected
		};
		AddChild(_selectionRing);
		UpdateSelectionRingTransform();
	}

	public bool IsPreview { get; set; } = false;

	public override void _Ready()
	{
		Name = $"Prop_{PropId}_{Guid.NewGuid().ToString().Substring(0, 4)}";

		SetNotifyTransform(true);

		CreatePropVisual();
		SetProcess(false);
	}

	public override void _Process(double delta)
	{
		if (_impulseTime > 0f)
		{
			_impulseTime -= (float)delta;
			float currentStrength = _impulseStrength * MathF.Max(0f, _impulseTime / _impulseDuration);
			ModelShaderManager.SetProceduralAnimationImpulse(this, currentStrength, _impulseFrequency, _impulseTime);
			if (_impulseTime <= 0f)
			{
				_impulseTime = 0f;
				ModelShaderManager.SetProceduralAnimationImpulse(this, 0f, 0f, 0f);
				if (!IsSelected)
				{
					SetProcess(false);
				}
			}
		}
		else if (!IsSelected)
		{
			SetProcess(false);
		}
	}

	public virtual void UpdateVisualYOffset(float yOffset)
	{
		var visual = GetNodeOrNull<Node3D>("VisualModel");
		if (visual != null)
		{
			visual.Position = new Vector3(visual.Position.X, yOffset, visual.Position.Z);
		}
	}

	public virtual void UpdateVisualScale(float globalScale)
	{
		var visual = GetNodeOrNull<Node3D>("VisualModel");
		if (visual != null && GodotObject.IsInstanceValid(visual))
		{
			float safeScale = globalScale <= 0.001f ? 1.0f : globalScale;
			visual.Scale = new Vector3(safeScale, safeScale, safeScale);
			UpdateLodVisibility();
		}
	}

	public void RefreshPropVisual()
	{
		var visual = GetNodeOrNull<Node3D>("VisualModel");
		if (visual != null && GodotObject.IsInstanceValid(visual))
		{
			RemoveChild(visual);
			visual.QueueFree();
		}

		CreatePropVisual();
	}

	public virtual bool IsSlopeAligned
	{
		get
		{
			if (GameHost.PropRegistry.TryGetValue(PropId, out var meta))
			{
				return string.Equals(meta.VisualMode, "SlopeAlignedQuad", StringComparison.OrdinalIgnoreCase);
			}

			if (GameHost.ResourceRegistry.TryGetValue(PropId, out var rMeta))
			{
				return string.Equals(rMeta.VisualMode, "SlopeAlignedQuad", StringComparison.OrdinalIgnoreCase);
			}

			return false;
		}
	}

	public virtual void UpdateSlopeAlignment()
	{
		var visual = GetNodeOrNull<Node3D>("VisualModel");
		ApplySlopeAlignment(visual);
	}

	protected void ApplySlopeAlignment(Node3D? visualNode)
	{
		if (IsSlopeAligned && GameHost.Instance?.GroundTerrain != null && visualNode != null && GodotObject.IsInstanceValid(visualNode))
		{
			GameHost.Instance.GroundTerrain.GetHeightAndNormal(GlobalPosition.X, GlobalPosition.Z, out _, out Vector3 normal);
			if (normal.LengthSquared() > 0.01f)
			{
				normal = normal.Normalized();
				Vector3 up = normal;
				Vector3 forward = MathF.Abs(up.Y) < 0.99f ? Vector3.Up.Cross(up).Cross(up).Normalized() : -Vector3.Forward;
				Vector3 right = up.Cross(forward).Normalized();
				visualNode.Basis = new Basis(right, up, -forward);
			}
		}
	}

	private void CreatePropVisual()
	{
		string modelPath = ResolvePropModelPath(PropId);

		if (IsPreview || GameContext.SessionState.IsMapEditorMode == true)
		{
			EnsureVisualModelExists(modelPath);
		}

		if (!IsPreview)
		{
			PropMultiMeshManager.Instance?.MarkDirty(PropId);
		}
	}

	private void EnsureVisualModelExists(string modelPath)
	{
		var visual = GetNodeOrNull<Node3D>("VisualModel");
		if (visual != null)
			return;

		visual = new Node3D();
		visual.Name = "VisualModel";
		string assetKey = GameHost.Instance != null ? GameHost.Instance.GetModelAssetKey(PropId) : "";
		float yOffset = GameHost.Instance != null ? GameHost.Instance.GetModelYOffset(assetKey) : 0f;
		float globalScale = GameHost.Instance != null ? GameHost.Instance.GetModelScale(this) : 1.0f;
		float safeScale = globalScale <= 0.001f ? 1.0f : globalScale;
		visual.Position = new Vector3(0, yOffset, 0);
		visual.Scale = new Vector3(safeScale, safeScale, safeScale);
		AddChild(visual);

		TryLoadAndAttachModel(visual, modelPath);

		UpdateLodVisibility();
		UpdateSlopeAlignment();
	}

	private void TryLoadAndAttachModel(Node3D visual, string modelPath)
	{
		if (string.IsNullOrEmpty(modelPath))
			return;

		try
		{
			Node node = ModelCache.GetModel(modelPath);
			if (node == null)
				return;

			visual.AddChild(node);
			AnimationRetargetingService.TryApplyRiggedIdlePose(node, PropId);
			if (!IsPreview)
			{
				GameHost.Instance?.ApplyAllGlobalOverridesToObject(this);
			}

			ModelShaderManager.SetHideInShroud(node, true);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to load prop visual for '{PropId}' ({modelPath}): {ex.Message}");
		}
	}

	private Vector3 _lastNotifiedScale = new Vector3(-1f, -1f, -1f);
	private float _lastEffectiveLodScale = -1f;

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

	public virtual void UpdateLodVisibility()
	{
		var visual = GetNodeOrNull<Node3D>("VisualModel");
		if (visual != null && GodotObject.IsInstanceValid(visual))
		{
			float effectiveScale = Math.Max(0.01f, Scale.Y * visual.Scale.Y);
			if (MathF.Abs(effectiveScale - _lastEffectiveLodScale) > 0.0005f)
			{
				_lastEffectiveLodScale = effectiveScale;
				GltfDocumentExtensionMsftLod.UpdateLodVisibilityRanges(visual, effectiveScale);
			}
		}
	}

	private static readonly Dictionary<string, string> _resolvedModelPathCache = new(StringComparer.OrdinalIgnoreCase);

	protected string ResolvePropModelPath(string propId)
	{
		if (string.IsNullOrEmpty(propId))
			return string.Empty;

		if (_resolvedModelPathCache.TryGetValue(propId, out string cachedPath))
			return cachedPath;

		string resolved = ResolvePropModelPathInternal(propId);
		_resolvedModelPathCache[propId] = resolved;
		return resolved;
	}

	public static void InvalidateModelPathCache(string propId)
	{
		if (string.IsNullOrEmpty(propId)) return;
		_resolvedModelPathCache.Remove(propId);
	}

	public static void ClearModelPathCache()
	{
		_resolvedModelPathCache.Clear();
		_sharedTorusMeshes.Clear();
	}

	private string ResolvePropModelPathInternal(string propId)
	{
		if (string.IsNullOrEmpty(propId))
			return string.Empty;

		string cleanId = Path.GetFileNameWithoutExtension(propId);
		string targetModel = TryGetModelPathFromRegistries(propId, cleanId) ?? propId;

		if (string.IsNullOrEmpty(targetModel))
			return string.Empty;

		string resolvedPath = ModelCache.ResolveModelPath(targetModel);
		if (!string.IsNullOrEmpty(resolvedPath) && (resolvedPath.StartsWith("res://") || File.Exists(resolvedPath)))
		{
			return resolvedPath;
		}

		return targetModel;
	}

	private string TryGetModelPathFromRegistries(string propId, string cleanId)
	{
		return TryGetModelPath(GameHost.PropRegistry, propId, cleanId)
		       ?? TryGetModelPath(GameHost.ResourceRegistry, propId, cleanId)
		       ?? TryGetModelPath(GameHost.UnitRegistry, propId, cleanId)
		       ?? TryGetModelPath(GameHost.BuildingRegistry, propId, cleanId);
	}

	private string TryGetModelPath(Dictionary<StringName, PropMetadata> registry, string propId, string cleanId)
	{
		if (registry == null) return null;
		if (registry.TryGetValue(propId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath)) return meta.ModelPath;
		if (!string.IsNullOrEmpty(cleanId) && registry.TryGetValue(cleanId, out meta) && !string.IsNullOrEmpty(meta.ModelPath)) return meta.ModelPath;
		return null;
	}

	private string TryGetModelPath(Dictionary<StringName, ResourceMetadata> registry, string propId, string cleanId)
	{
		if (registry == null) return null;
		if (registry.TryGetValue(propId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath)) return meta.ModelPath;
		if (!string.IsNullOrEmpty(cleanId) && registry.TryGetValue(cleanId, out meta) && !string.IsNullOrEmpty(meta.ModelPath)) return meta.ModelPath;
		return null;
	}

	private string TryGetModelPath(Dictionary<StringName, UnitMetadata> registry, string propId, string cleanId)
	{
		if (registry == null) return null;
		if (registry.TryGetValue(propId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath)) return meta.ModelPath;
		if (!string.IsNullOrEmpty(cleanId) && registry.TryGetValue(cleanId, out meta) && !string.IsNullOrEmpty(meta.ModelPath)) return meta.ModelPath;
		return null;
	}
}