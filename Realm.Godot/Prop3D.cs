using Arch.Core;
using Realm.Ecs.Common;
using Godot;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using System;
using System.Collections.Generic;
using Realm.Godot.Utils;

public partial class Prop3D : StaticBody3D
{
	public Entity Entity { get; set; }

	private Vector3 _velocity = Vector3.Zero;
	public Vector3 Velocity
	{
		get => _velocity;
		set
		{
			if (_velocity != value)
			{
				_velocity = value;
				ModelShaderManager.SetProceduralAnimationVelocity(this, value);
			}
		}
	}

	private float _impulseStrength = 0f;
	private float _impulseDuration = 0.5f;
	private float _impulseTime = 0f;
	private float _impulseFrequency = 12.0f;

	public virtual void TriggerImpulse(float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f)
	{
		_impulseStrength = strength;
		_impulseDuration = duration <= 0.001f ? 0.001f : duration;
		_impulseTime = _impulseDuration;
		_impulseFrequency = frequency;
		SetProcess(true);
	}

	private string _objectId = string.Empty;

	[Export]
	public virtual string ObjectID
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity)
				&& GameHost.Instance.EcsWorld.Has<PropIdentity>(Entity))
				return GameHost.Instance.EcsWorld.Get<PropIdentity>(Entity).PropId;
			return _objectId;
		}
		set
		{
			_objectId = value;
			_cachedResolvedModelPath = null;
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				world.SetOrAdd(Entity, new PropIdentity(value));
			}
		}
	}

	public virtual string PropId
	{
		get => ObjectID;
		set => ObjectID = value;
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
			if (GameHost.Instance == null || !GameHost.Instance.EcsWorld.IsAlive(Entity)) return 0f;
			if (GameHost.Instance.EcsWorld.Has<ResourceNode>(Entity))
				return GameHost.Instance.EcsWorld.Get<ResourceNode>(Entity).Amount;
			return 0f;
		}
		set
		{
			if (GameHost.Instance == null || !GameHost.Instance.EcsWorld.IsAlive(Entity)) return;
			var world = GameHost.Instance.EcsWorld;
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
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && Entity != default && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				if (GameHost.Instance.EcsWorld.Has<ResourceNode>(Entity))
					return true;
			}
			if (GameHost.ResourceRegistry != null && !string.IsNullOrEmpty(PropId))
			{
				if (GameHost.ResourceRegistry.ContainsKey(PropId)) return true;
				string cleanId = System.IO.Path.GetFileNameWithoutExtension(PropId);
				if (!string.IsNullOrEmpty(cleanId) && GameHost.ResourceRegistry.ContainsKey(cleanId)) return true;
			}
			return _isResource;
		}
		set
		{
			_isResource = value;
		}
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
		string key = r.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
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

	private static readonly Dictionary<string, (Shape3D Shape, Vector3 Offset)> _modelShapeCache = new(StringComparer.OrdinalIgnoreCase);

	protected static Aabb GetCombinedAabb(Node root)
	{
		Aabb totalAabb = new Aabb();
		bool first = true;
		CollectAabbRecursive(root, Transform3D.Identity, ref totalAabb, ref first);
		return totalAabb;
	}

	private static void CollectAabbRecursive(Node node, Transform3D currentTransform, ref Aabb totalAabb, ref bool first)
	{
		if (node == null) return;

		if (node is MeshInstance3D mi && mi.Mesh != null)
		{
			Aabb localAabb = mi.Mesh.GetAabb();
			if (localAabb.Size != Vector3.Zero)
			{
				for (int i = 0; i < 8; i++)
				{
					Vector3 corner = currentTransform * localAabb.GetEndpoint(i);
					if (first)
					{
						totalAabb = new Aabb(corner, Vector3.Zero);
						first = false;
					}
					else
					{
						totalAabb = totalAabb.Expand(corner);
					}
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node3D child3D)
			{
				CollectAabbRecursive(child, currentTransform * child3D.Transform, ref totalAabb, ref first);
			}
		}
	}

	private (Shape3D Shape, Vector3 Offset) GetOrCreateCollisionShape()
	{
		string propIdKey = PropId ?? string.Empty;
		if (!string.IsNullOrEmpty(propIdKey) && _modelShapeCache.TryGetValue(propIdKey, out var cachedProp))
		{
			return cachedProp;
		}

		string modelPath = ResolvePropModelPath(PropId);
		if (!string.IsNullOrEmpty(modelPath) && _modelShapeCache.TryGetValue(modelPath, out var cached))
		{
			if (!string.IsNullOrEmpty(propIdKey))
			{
				_modelShapeCache[propIdKey] = cached;
			}
			return cached;
		}

		(Shape3D Shape, Vector3 Offset) shapeResult;

		if (!string.IsNullOrEmpty(modelPath))
		{
			try
			{
				Node modelNode = ModelCache.GetModel(modelPath);
				if (modelNode != null)
				{
					Aabb modelAabb = GetCombinedAabb(modelNode);
					modelNode.Free();

					if (modelAabb.Size.LengthSquared() > 0.01f)
					{
						var (analShape, analOffset) = Realm.Godot.Services.ModelOptimization.ModelOptimizerService.GenerateAnalyticalCollisionShape(modelAabb, isBuilding: true);
						if (analShape != null)
						{
							shapeResult = (analShape, analOffset);
							if (!string.IsNullOrEmpty(propIdKey)) _modelShapeCache[propIdKey] = shapeResult;
							_modelShapeCache[modelPath] = shapeResult;
							return shapeResult;
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to compute analytical collision shape for '{PropId}': {ex.Message}");
			}
		}

		float radius = GetBaseObstacleRadius();
		float height = Math.Max(1.0f, radius * 2.5f);
		var fallbackBox = new BoxShape3D
		{
			Size = new Vector3(radius * 2.0f, height, radius * 2.0f)
		};
		shapeResult = (fallbackBox, new Vector3(0, height * 0.5f, 0));
		if (!string.IsNullOrEmpty(propIdKey))
		{
			_modelShapeCache[propIdKey] = shapeResult;
		}
		if (!string.IsNullOrEmpty(modelPath))
		{
			_modelShapeCache[modelPath] = shapeResult;
		}
		return shapeResult;
	}

	public bool IsPreview { get; set; } = false;

	public override void _Ready()
	{
		Name = $"Prop_{PropId}_{Guid.NewGuid().ToString().Substring(0, 4)}";
		
		SetNotifyTransform(true);

		if (IsPreview)
		{
			CreatePropVisual();
			SetProcess(false);
			return;
		}

		var collisionShape = new CollisionShape3D();
		collisionShape.Name = "CollisionShape";
		AddChild(collisionShape);
		
		var (shape, offset) = GetOrCreateCollisionShape();
		collisionShape.Shape = shape;
		collisionShape.Position = offset;

		CreatePropVisual();
		SetProcess(false);
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
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
		if (IsSlopeAligned && GameHost.Instance?.GroundTerrain != null && visual != null && GodotObject.IsInstanceValid(visual))
		{
			GameHost.Instance.GroundTerrain.GetHeightAndNormal(GlobalPosition.X, GlobalPosition.Z, out _, out Vector3 normal);
			if (normal.LengthSquared() > 0.01f)
			{
				normal = normal.Normalized();
				Vector3 up = normal;
				Vector3 forward = MathF.Abs(up.Y) < 0.99f ? Vector3.Up.Cross(up).Cross(up).Normalized() : -Vector3.Forward;
				Vector3 right = up.Cross(forward).Normalized();
				visual.Basis = new Basis(right, up, -forward);
			}
		}
	}

	private void CreatePropVisual()
	{
		string modelPath = ResolvePropModelPath(PropId);

		if (IsPreview || GameHost.Instance?.IsMapEditorMode == true)
		{
			var visual = GetNodeOrNull<Node3D>("VisualModel");
			if (visual == null)
			{
				visual = new Node3D();
				visual.Name = "VisualModel";
				string assetKey = GameHost.Instance != null ? GameHost.Instance.GetModelAssetKey(PropId) : "";
				float yOffset = GameHost.Instance != null ? GameHost.Instance.GetModelYOffset(assetKey) : 0f;
				float globalScale = GameHost.Instance != null ? GameHost.Instance.GetModelScale(this) : 1.0f;
				float safeScale = globalScale <= 0.001f ? 1.0f : globalScale;
				visual.Position = new Vector3(0, yOffset, 0);
				visual.Scale = new Vector3(safeScale, safeScale, safeScale);
				AddChild(visual);

				try
				{
					if (!string.IsNullOrEmpty(modelPath))
					{
						Node node = ModelCache.GetModel(modelPath);
						if (node != null)
						{
							visual.AddChild(node);
							Realm.Godot.Animation.AnimationRetargetingService.TryApplyRiggedIdlePose(node, PropId);
							if (!IsPreview)
							{
								GameHost.Instance?.ApplyAllGlobalOverridesToObject(this);
							}
							ModelShaderManager.SetHideInShroud(node, true);
						}
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"Failed to load prop visual for '{PropId}' ({modelPath}): {ex.Message}");
				}

				UpdateLodVisibility();
				UpdateSlopeAlignment();
			}
		}

		if (!IsPreview)
		{
			PropMultiMeshManager.Instance?.MarkDirty(PropId);
		}
	}

	public override void _Notification(int what)
	{
		base._Notification(what);
		if (what == NotificationTransformChanged)
		{
			UpdateLodVisibility();
			UpdateSelectionRingTransform();
		}
	}

	public virtual void UpdateLodVisibility()
	{
		var visual = GetNodeOrNull<Node3D>("VisualModel");
		if (visual != null && GodotObject.IsInstanceValid(visual))
		{
			float effectiveScale = Math.Max(0.01f, Scale.Y * visual.Scale.Y);
			Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.UpdateLodVisibilityRanges(visual, effectiveScale);
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
		_modelShapeCache.Remove(propId);
	}

	public static void ClearModelPathCache()
	{
		_resolvedModelPathCache.Clear();
		_modelShapeCache.Clear();
		_sharedTorusMeshes.Clear();
	}

	private string ResolvePropModelPathInternal(string propId)
	{
		if (string.IsNullOrEmpty(propId))
			return string.Empty;

		string targetModel = propId;
		string cleanId = System.IO.Path.GetFileNameWithoutExtension(propId);

		if (GameHost.PropRegistry != null && ((GameHost.PropRegistry.TryGetValue(propId, out var propMeta) || (!string.IsNullOrEmpty(cleanId) && GameHost.PropRegistry.TryGetValue(cleanId, out propMeta))) && !string.IsNullOrEmpty(propMeta.ModelPath)))
		{
			targetModel = propMeta.ModelPath;
		}
		else if (GameHost.ResourceRegistry != null && ((GameHost.ResourceRegistry.TryGetValue(propId, out var resMeta) || (!string.IsNullOrEmpty(cleanId) && GameHost.ResourceRegistry.TryGetValue(cleanId, out resMeta))) && !string.IsNullOrEmpty(resMeta.ModelPath)))
		{
			targetModel = resMeta.ModelPath;
		}
		else if (GameHost.UnitRegistry != null && ((GameHost.UnitRegistry.TryGetValue(propId, out var unitMeta) || (!string.IsNullOrEmpty(cleanId) && GameHost.UnitRegistry.TryGetValue(cleanId, out unitMeta))) && !string.IsNullOrEmpty(unitMeta.ModelPath)))
		{
			targetModel = unitMeta.ModelPath;
		}
		else if (GameHost.BuildingRegistry != null && ((GameHost.BuildingRegistry.TryGetValue(propId, out var bldMeta) || (!string.IsNullOrEmpty(cleanId) && GameHost.BuildingRegistry.TryGetValue(cleanId, out bldMeta))) && !string.IsNullOrEmpty(bldMeta.ModelPath)))
		{
			targetModel = bldMeta.ModelPath;
		}

		if (string.IsNullOrEmpty(targetModel))
			return string.Empty;

		string resolvedPath = ModelCache.ResolveModelPath(targetModel);
		if (!string.IsNullOrEmpty(resolvedPath) && (resolvedPath.StartsWith("res://") || System.IO.File.Exists(resolvedPath)))
		{
			return resolvedPath;
		}

		return targetModel;
	}
}

public partial class Resource3D : Prop3D
{
}
