using System;
using Realm.Ecs.Common;
using System.Collections.Generic;
using Arch.Core;
using Godot;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Godot.VFX;

public partial class Unit3D : Prop3D
{
	private string _objectId = "unit/worker";

	[Export]
	public override string TemplateID
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity)
				&& GameHost.Instance.EcsWorld.Has<DefinitionId>(Entity))
				return GameHost.Instance.EcsWorld.Get<DefinitionId>(Entity).Value;
			return _objectId;
		}
		set
		{
			_objectId = value;
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				world.SetOrAdd(Entity, new DefinitionId(value));
			}
		}
	}

	public string UnitId
	{
		get => TemplateID;
		set => TemplateID = value;
	}

	public override string PropId
	{
		get => TemplateID;
		set => TemplateID = value;
	}

	private bool _isBuilding;
	public bool IsBuilding
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && Entity != default && GameHost.Instance.EcsWorld.IsAlive(Entity))
				return GameHost.Instance.EcsWorld.Has<Building>(Entity);
			if (GameHost.BuildingRegistry != null && !string.IsNullOrEmpty(UnitId) && GameHost.BuildingRegistry.ContainsKey(UnitId))
				return true;
			return _isBuilding;
		}
		set
		{
			_isBuilding = value;
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && Entity != default && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				var world = GameHost.Instance.EcsWorld;
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

	public override bool IsResource
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && Entity != default && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				if (GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Resources.ResourceNode>(Entity))
					return true;
			}
			if (GameHost.ResourceRegistry != null && !string.IsNullOrEmpty(UnitId) && GameHost.ResourceRegistry.ContainsKey(UnitId))
				return true;
			return base.IsResource;
		}
		set
		{
			base.IsResource = value;
		}
	}

	private Node3D _modelNode;
	private AnimationPlayer _animationPlayer;
	private string _currentAnimation = string.Empty;
	private BoneAttachment3D? _rightHandAttachment;
	private BoneAttachment3D? _leftHandAttachment;
	private string? _currentRightAttachmentId;
	private string? _currentLeftAttachmentId;
	private readonly Dictionary<HumanoidBone, Node3D> _boneAttachments = new();
	private readonly Dictionary<HumanoidBone, string> _currentBoneAttachmentIds = new();
	private Node3D _pathVisualsContainer;
	private readonly System.Collections.Generic.List<MeshInstance3D> _pathMarkersPool = new();
	private readonly System.Collections.Generic.List<MeshInstance3D> _pathLinesPool = new();

	private int _player = 0;

	public int Player
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity)
				&& GameHost.Instance.EcsWorld.Has<UnitOwnerPlayer>(Entity))
				return GameHost.Instance.EcsWorld.Get<UnitOwnerPlayer>(Entity).PlayerIndex;
			return _player;
		}
		set
		{
			_player = value;
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				world.SetOrAdd(Entity, new UnitOwnerPlayer(value));
			}
			UpdatePlayerColorVisual();
		}
	}

	public int PlayerIndex
	{
		get => Player;
		set => Player = value;
	}

	public Color PlayerColor { get; private set; } = PlayerColorConfig.GetColor(0);

	public void SetPlayer(int playerIndex)
	{
		Player = playerIndex;
	}

	public void SetPlayerColor(Color color)
	{
		PlayerColor = color;
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			bool ignorePlayerColor = GameHost.Instance != null && (GameHost.Instance.GetModelIgnorePlayerColor(ModelPath) || GameHost.Instance.GetModelIgnorePlayerColor(UnitId));
			Realm.Godot.Utils.ModelShaderManager.SetPlayerColor(_modelNode, color);
			Realm.Godot.Utils.ModelShaderManager.SetIgnorePlayerColor(_modelNode, ignorePlayerColor);
		}
	}

	public void InterpolatePlayerColor(Color targetColor, float weight)
	{
		Color blended = PlayerColor.Lerp(targetColor, weight);
		SetPlayerColor(blended);
	}

	public void UpdatePlayerColorVisual()
	{
		bool ignorePlayerColor = GameHost.Instance != null && (GameHost.Instance.GetModelIgnorePlayerColor(ModelPath) || GameHost.Instance.GetModelIgnorePlayerColor(UnitId));
		if (ignorePlayerColor)
		{
			if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
			{
				Realm.Godot.Utils.ModelShaderManager.SetIgnorePlayerColor(_modelNode, true);
			}
			return;
		}

		Color resolvedColor = PlayerColorConfig.GetColor(Player);
		if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
		{
			var matchPlayer = LobbyManager.Instance.PlayerList.Find(x => x.Slot == Player);
			if (matchPlayer != null)
			{
				resolvedColor = matchPlayer.Color;
			}
		}
		SetPlayerColor(resolvedColor);
	}

	public bool IsEnemy
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity)
				&& GameHost.Instance.EcsWorld.Has<UnitFaction>(Entity))
				return GameHost.Instance.EcsWorld.Get<UnitFaction>(Entity).IsEnemy;
			return false;
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				world.SetOrAdd(Entity, new UnitFaction(value));
			}
		}
	}

	private Node3D _rallyVisualsContainer;
	private MeshInstance3D _rallyMarker;
	private readonly System.Collections.Generic.List<MeshInstance3D> _rallyMarkersPool = new();
	private readonly System.Collections.Generic.List<MeshInstance3D> _rallyLinesPool = new();

	public override bool IsSelected
	{
		get => base.IsSelected;
		set
		{
			base.IsSelected = value;
			if (IsBuilding && !IsEnemy)
			{
				UpdateRallyVisuals();
			}

			if (!value)
			{
				HidePathVisuals();
				if (_rallyVisualsContainer != null)
				{
					_rallyVisualsContainer.Visible = false;
				}
			}
			SetProcess(value);
		}
	}

	public override void _Ready()
	{
		if (IsPreview)
		{
			SeekToIdleFirstFrame();
			return;
		}

		SetNotifyTransform(true);

		var collisionShape = new CollisionShape3D();
		collisionShape.Name = "CollisionShape";
		if (IsBuilding)
		{
			var boxShape = new BoxShape3D();
			float size = (UnitId == "castle") ? 4f : 3f;
			boxShape.Size = new Vector3(size, size, size);
			collisionShape.Shape = boxShape;
			collisionShape.Position = new Vector3(0, size * 0.5f, 0);
		}
		else
		{
			var capsuleShape = new CapsuleShape3D();
			capsuleShape.Radius = 1.0f;
			capsuleShape.Height = 2.5f;
			collisionShape.Shape = capsuleShape;
			collisionShape.Position = new Vector3(0, 1.25f, 0);
		}
		AddChild(collisionShape);


		CreateSelectionRing();
		SetProcess(false);
	}

	public override void UpdateLodVisibility()
	{
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			float effectiveScale = Math.Max(0.01f, Scale.Y * _modelNode.Scale.Y);
			Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.UpdateLodVisibilityRanges(_modelNode, effectiveScale);
		}
	}

	public float BaseModelYOffset => 0f;
	public string ModelPath { get; private set; }
	public Node3D ModelNode => _modelNode;

	public override void UpdateVisualYOffset(float yOffset) => UpdateModelYOffset(yOffset);
	public override void UpdateVisualScale(float globalScale) => UpdateModelScale(globalScale);

	public void UpdateModelYOffset(float yOffset)
	{
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			_modelNode.Position = new Vector3(_modelNode.Position.X, yOffset, _modelNode.Position.Z);
		}
	}

	public void UpdateModelScale(float globalScale)
	{
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			float safeScale = globalScale <= 0.001f ? 1.0f : globalScale;
			_modelNode.Scale = new Vector3(safeScale, safeScale, safeScale);
			float yOffset = GameHost.Instance != null ? GameHost.Instance.GetModelYOffset(this) : 0f;
			_modelNode.Position = new Vector3(_modelNode.Position.X, yOffset, _modelNode.Position.Z);
			UpdateLodVisibility();
		}
	}

	public void LoadModel(string modelPath)
	{
		if (string.IsNullOrEmpty(modelPath)) return;
		ModelPath = modelPath;

		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			RemoveChild(_modelNode);
			_modelNode.QueueFree();
			_modelNode = null;
			_rightHandAttachment = null;
			_leftHandAttachment = null;
			_currentRightAttachmentId = null;
			_currentLeftAttachmentId = null;
			_boneAttachments.Clear();
			_currentBoneAttachmentIds.Clear();
			_pseudoSockets.Clear();
			_currentPseudoSocketAttachmentIds.Clear();
		}

		string resolved = ResolvePropModelPath(modelPath);
		if (string.IsNullOrEmpty(resolved))
		{
			resolved = modelPath;
		}

		_modelNode = Realm.Godot.Utils.ModelCache.GetModel(modelPath) as Node3D;
		if (_modelNode == null && !string.IsNullOrEmpty(resolved) && !resolved.Equals(modelPath, StringComparison.OrdinalIgnoreCase))
		{
			_modelNode = Realm.Godot.Utils.ModelCache.GetModel(resolved) as Node3D;
		}

		if (_modelNode == null)
		{
			GD.PrintErr($"Failed to load 3D model path: {modelPath}");
			return;
		}

		_modelNode.Name = "VisualModel";
		AddChild(_modelNode);

		try
		{
			_animationPlayer = Realm.Godot.Animation.AnimationRetargetingService.FindOrCreateAnimationPlayer(_modelNode);
			Realm.Godot.Animation.AnimationRetargetingService.LoadAndBindUnitAnimations(_modelNode, UnitId, modelPath);
			SeekToIdleFirstFrame();
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Error configuring animations for unit '{UnitId}' ({modelPath}): {ex.Message}");
		}

		try
		{
			ApplyAllConfiguredAttachments();
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Error applying attachments for unit '{UnitId}': {ex.Message}");
		}

		float globalScale = GameHost.Instance != null ? GameHost.Instance.GetModelScale(this) : 1.0f;
		float safeScale = globalScale <= 0.001f ? 1.0f : globalScale;
		_modelNode.Scale = new Vector3(safeScale, safeScale, safeScale);

		UpdateLodVisibility();

		float yOffset = GameHost.Instance != null ? GameHost.Instance.GetModelYOffset(this) : 0f;
		_modelNode.Position = new Vector3(0f, yOffset, 0f);

		if (!IsPreview)
		{
			try
			{
				bool ignorePlayerColor = GameHost.Instance != null && (GameHost.Instance.GetModelIgnorePlayerColor(modelPath) || GameHost.Instance.GetModelIgnorePlayerColor(UnitId));
				bool normalizeLuminance = GameHost.Instance != null && (GameHost.Instance.GetModelNormalizeLuminance(modelPath) || GameHost.Instance.GetModelNormalizeLuminance(UnitId));
				Realm.Godot.Utils.ModelShaderManager.ApplyPlayerColorShader(_modelNode, PlayerColor, ignorePlayerColor, normalizeLuminance);
				if (!ignorePlayerColor)
				{
					UpdatePlayerColorVisual();
				}
				else
				{
					Realm.Godot.Utils.ModelShaderManager.SetIgnorePlayerColor(_modelNode, true);
				}

				GameHost.Instance?.ApplyAllGlobalOverridesToObject(this);
			}
			catch (System.Exception ex)
			{
				GD.PrintErr($"Error applying shaders for unit '{UnitId}': {ex.Message}");
			}
		}

		try
		{
			var colShapeNode = GetNodeOrNull<CollisionShape3D>("CollisionShape");
			if (colShapeNode != null && _modelNode != null)
			{
				Aabb modelAabb = GetCombinedAabb(_modelNode);
				if (modelAabb.Size.LengthSquared() > 0.01f)
				{
					var (analShape, analOffset) = Realm.Godot.Services.ModelOptimization.ModelOptimizerService.GenerateAnalyticalCollisionShape(modelAabb, IsBuilding);
					if (analShape != null)
					{
						colShapeNode.Shape = analShape;
						colShapeNode.Position = analOffset;
					}
				}
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Error generating collision shape for unit '{UnitId}': {ex.Message}");
		}

		UpdateDropShadow();
		UpdateSlopeAlignment();
	}

	public override bool IsSlopeAligned
	{
		get
		{
			if (GameHost.UnitRegistry.TryGetValue(UnitId, out var meta))
			{
				return string.Equals(meta.VisualMode, "SlopeAlignedQuad", StringComparison.OrdinalIgnoreCase);
			}
			if (GameHost.BuildingRegistry.TryGetValue(UnitId, out var bMeta))
			{
				return string.Equals(bMeta.VisualMode, "SlopeAlignedQuad", StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}
	}

	public override void UpdateSlopeAlignment()
	{
		if (IsSlopeAligned && GameHost.Instance?.GroundTerrain != null && _modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			GameHost.Instance.GroundTerrain.GetHeightAndNormal(GlobalPosition.X, GlobalPosition.Z, out _, out Vector3 normal);
			if (normal.LengthSquared() > 0.01f)
			{
				normal = normal.Normalized();
				Vector3 up = normal;
				Vector3 forward = MathF.Abs(up.Y) < 0.99f ? Vector3.Up.Cross(up).Cross(up).Normalized() : -Vector3.Forward;
				Vector3 right = up.Cross(forward).Normalized();
				_modelNode.Basis = new Basis(right, up, -forward);
			}
		}
	}

	public override float GetBaseObstacleRadius()
	{
		if (GameHost.Instance != null)
		{
			return GameHost.Instance.GetOrCalculateObstacleRadius(UnitId, this, IsBuilding);
		}
		return 1.4f;
	}

	/// <summary>
	///     Adds (or refreshes) a projected drop shadow for flying units so they read clearly
	///     against the terrain below instead of appearing to float in empty space.
	/// </summary>
	private void UpdateDropShadow()
	{
		if (GameHost.Instance == null || !GameHost.Instance.IsPathingCapability(Entity, TerrainPathingFlags.Flying))
		{
			var oldDecal = GetNodeOrNull<Decal>("DropShadow");
			if (oldDecal != null)
			{
				oldDecal.QueueFree();
			}
			return;
		}

		var existing = GetNodeOrNull<Decal>("DropShadow");
		if (existing != null)
		{
			float updatedRadius = GameHost.Instance.GetOrCalculateObstacleRadius(UnitId, this, IsBuilding) * GameHost.Instance.GetModelCollisionCircleRatio(ModelPath);
			if (updatedRadius > 0.001f)
			{
				float safeRadius = Mathf.Max(0.1f, updatedRadius * 2.5f);
				existing.Size = new Vector3(safeRadius, 3f, safeRadius);
			}
			return;
		}

		float radius = GameHost.Instance.GetOrCalculateObstacleRadius(UnitId, this, IsBuilding) * GameHost.Instance.GetModelCollisionCircleRatio(ModelPath);
		if (radius <= 0.001f) radius = 1f;

		Decal shadowDecal = new Decal();
		shadowDecal.Name = "DropShadow";
		shadowDecal.TextureAlbedo = GameHost.Instance.GetSharedShadowGradient();
		float decalSize = Mathf.Max(0.1f, radius * 2.5f);
		shadowDecal.Size = new Vector3(decalSize, 3f, decalSize);
		shadowDecal.Position = Vector3.Zero;
		// Decals project along local -Z; tilt the node down so the shadow lands on the terrain.
		shadowDecal.RotationDegrees = new Vector3(-90f, 0f, 0f);
		shadowDecal.CullMask = RuntimeTerrain.TerrainDecalCullMask;

		AddChild(shadowDecal);
	}

	public void PlayAnimation(string animName)
	{
		if (_animationPlayer == null || !GodotObject.IsInstanceValid(_animationPlayer)) return;
		if (string.IsNullOrEmpty(animName)) return;

		StringName resolved = ResolveAnimationName(animName);
		if (resolved == null) return;

		UpdateHandAttachmentsForAnimation(animName, resolved.ToString());

		if (_currentAnimation == resolved.ToString() && _animationPlayer.IsPlaying()) return;

		_currentAnimation = resolved.ToString();

		var animResource = _animationPlayer.GetAnimation(resolved);
		if (animResource != null)
		{
			if (animName.Equals("Death", System.StringComparison.OrdinalIgnoreCase))
			{
				animResource.LoopMode = global::Godot.Animation.LoopModeEnum.None;
			}
			else
			{
				animResource.LoopMode = global::Godot.Animation.LoopModeEnum.Linear;
			}
		}

		_animationPlayer.ProcessMode = ProcessModeEnum.Inherit;
		_animationPlayer.Play(resolved);
	}

	private void SeekToIdleFirstFrame()
	{
		if (_modelNode == null || !GodotObject.IsInstanceValid(_modelNode)) return;
		if (_animationPlayer == null || !GodotObject.IsInstanceValid(_animationPlayer))
		{
			_animationPlayer = Realm.Godot.Animation.AnimationRetargetingService.FindOrCreateAnimationPlayer(_modelNode);
		}

		StringName idleAnim = ResolveAnimationName("Idle");
		if (idleAnim != null && _animationPlayer != null && _animationPlayer.HasAnimation(idleAnim))
		{
			UpdateHandAttachmentsForAnimation("Idle", idleAnim.ToString());
			_animationPlayer.ProcessMode = ProcessModeEnum.Inherit;
			_animationPlayer.Play(idleAnim);
			_animationPlayer.Seek(0.0, update: true);
			_animationPlayer.Pause();
		}
		else
		{
			Realm.Godot.Animation.AnimationRetargetingService.TryApplyRiggedIdlePose(_modelNode, UnitId);
		}
	}

	public static Skeleton3D? FindSkeleton(Node? root)
	{
		if (root == null || !GodotObject.IsInstanceValid(root)) return null;
		if (root is Skeleton3D skeleton) return skeleton;
		int childCount = root.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			var found = FindSkeleton(root.GetChild(i));
			if (found != null) return found;
		}
		return null;
	}

	public void SetHandAttachment(
		HumanoidBone hand,
		string? attachmentId,
		Vector3? posOffsetOverride = null,
		Vector3? rotOffsetOverride = null,
		float? scaleOverride = null)
	{
		SetSocketAttachment(hand, attachmentId, posOffsetOverride, rotOffsetOverride, scaleOverride, null, null, true);
	}

	public void SetSocketAttachment(
		HumanoidBone bone,
		string? attachmentId,
		Vector3? posOffsetOverride = null,
		Vector3? rotOffsetOverride = null,
		float? scaleOverride = null,
		Vector3? scaleVectorOverride = null,
		float? normalOffsetOverride = null,
		bool clearExisting = false,
		string? parentAttachmentId = null)
	{
		var parentNode = (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) ? _modelNode : (Node3D)this;
		var skeleton = FindSkeleton(parentNode);
		int boneIdx = skeleton != null ? Realm.Godot.Animation.HumanoidBoneExtensions.FindBoneInSkeleton(skeleton, bone) : -1;

		bool isRight = bone == HumanoidBone.RightHand;
		bool isLeft = bone == HumanoidBone.LeftHand;

		if (string.IsNullOrEmpty(attachmentId) ||
			attachmentId.Equals("null", StringComparison.OrdinalIgnoreCase) ||
			attachmentId.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			if (isRight) _currentRightAttachmentId = null;
			else if (isLeft) _currentLeftAttachmentId = null;
			_currentBoneAttachmentIds.Remove(bone);

			if (_boneAttachments.TryGetValue(bone, out var existing) && GodotObject.IsInstanceValid(existing))
			{
				foreach (Node child in existing.GetChildren())
				{
					child.QueueFree();
				}
			}
			return;
		}

		if (clearExisting &&
			_currentBoneAttachmentIds.TryGetValue(bone, out var currentId) &&
			currentId == attachmentId &&
			_boneAttachments.TryGetValue(bone, out var currentAttachment) &&
			GodotObject.IsInstanceValid(currentAttachment) &&
			currentAttachment.GetChildCount() > 0 &&
			!posOffsetOverride.HasValue &&
			!rotOffsetOverride.HasValue &&
			!scaleOverride.HasValue &&
			!scaleVectorOverride.HasValue &&
			!normalOffsetOverride.HasValue)
		{
			return;
		}

		if (isRight) _currentRightAttachmentId = attachmentId;
		else if (isLeft) _currentLeftAttachmentId = attachmentId;
		_currentBoneAttachmentIds[bone] = attachmentId;

		Node3D attachTarget;
		if (skeleton != null && boneIdx >= 0)
		{
			string boneName = skeleton.GetBoneName(boneIdx);
			string nodeName = $"BoneAttachment_{bone}";
			var boneAttachment = skeleton.GetNodeOrNull<BoneAttachment3D>(nodeName);
			if (boneAttachment == null)
			{
				boneAttachment = new BoneAttachment3D
				{
					Name = nodeName,
					BoneName = boneName,
					BoneIdx = boneIdx
				};
				skeleton.AddChild(boneAttachment);
			}
			_boneAttachments[bone] = boneAttachment;
			if (isRight) _rightHandAttachment = boneAttachment;
			else if (isLeft) _leftHandAttachment = boneAttachment;
			attachTarget = boneAttachment;
		}
		else
		{
			string nodeName = $"SocketAttachment_{bone}";
			var socketNode = parentNode.GetNodeOrNull<Node3D>(nodeName);
			if (socketNode == null)
			{
				socketNode = new Node3D { Name = nodeName };
				parentNode.AddChild(socketNode);
			}
			_boneAttachments[bone] = socketNode;
			attachTarget = socketNode;
		}

		if (clearExisting)
		{
			foreach (Node child in attachTarget.GetChildren())
			{
				attachTarget.RemoveChild(child);
				child.QueueFree();
			}
		}

		Node3D? model = ResolveAndInstantiateAttachment(attachmentId, out float defScale, out Vector3 defPos, out Vector3 defRot);
		if (model != null)
		{
			float effectiveScale = scaleOverride ?? defScale;
			Vector3 effectivePos = posOffsetOverride ?? defPos;
			Vector3 effectiveRot = rotOffsetOverride ?? defRot;
			Vector3 effectiveScaleVec = scaleVectorOverride ?? (Vector3.One * (effectiveScale <= 0f ? 1.0f : effectiveScale));
			float effectiveNormalOffset = normalOffsetOverride ?? 0.0f;

			if (!string.IsNullOrEmpty(UnitId) &&
				GameHost.TryGetUnitOrBuildingMetadata(UnitId, out var uMeta) &&
				uMeta.TryGetObjectAttachment(bone, attachmentId, out var unitOrient))
			{
				if (!scaleOverride.HasValue && !scaleVectorOverride.HasValue)
				{
					effectiveScaleVec = unitOrient.ScaleVector.ToGodotVector3();
				}
				if (!posOffsetOverride.HasValue) effectivePos = unitOrient.Position.ToGodotVector3();
				if (!rotOffsetOverride.HasValue) effectiveRot = unitOrient.RotationDegrees.ToGodotVector3();
				if (!normalOffsetOverride.HasValue) effectiveNormalOffset = unitOrient.NormalOffset;
			}

			if (effectiveNormalOffset != 0.0f)
			{
				effectivePos += Vector3.Up * effectiveNormalOffset;
			}

			model.Position = effectivePos;
			model.RotationDegrees = effectiveRot;
			model.Scale = effectiveScaleVec;

			string cleanAttId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
				? attachmentId
				: System.IO.Path.GetFileNameWithoutExtension(attachmentId);
			model.Name = $"Att_{cleanAttId}";
			model.SetMeta("AttachmentId", attachmentId);
			model.SetMeta("CleanAttachmentId", cleanAttId);

			Node3D targetParent = attachTarget;
			if (!string.IsNullOrEmpty(parentAttachmentId))
			{
				var parentMesh = FindAttachmentInNode(attachTarget, parentAttachmentId)
					?? FindAttachmentInNode(parentNode, parentAttachmentId);
				if (parentMesh != null)
				{
					targetParent = parentMesh;
				}
			}

			targetParent.AddChild(model);

			if (model is not ProceduralVfxInstance3D)
			{
				Realm.Godot.Utils.ModelShaderManager.ApplyPlayerColorShader(model, PlayerColor, true, true, false);
				Realm.Godot.Utils.ModelShaderManager.SetIgnorePlayerColor(model, true);
			}
		}
	}

	public virtual void ClearAllAttachments()
	{
		foreach (var kvp in _boneAttachments)
		{
			if (kvp.Value != null && GodotObject.IsInstanceValid(kvp.Value))
			{
				foreach (Node child in kvp.Value.GetChildren())
				{
					kvp.Value.RemoveChild(child);
					child.QueueFree();
				}
			}
		}
		_currentBoneAttachmentIds.Clear();
		_currentRightAttachmentId = null;
		_currentLeftAttachmentId = null;

		foreach (var kvp in _pseudoSockets)
		{
			if (kvp.Value != null && GodotObject.IsInstanceValid(kvp.Value))
			{
				foreach (Node child in kvp.Value.GetChildren())
				{
					kvp.Value.RemoveChild(child);
					child.QueueFree();
				}
			}
		}
		_currentPseudoSocketAttachmentIds.Clear();
	}

	public virtual void ApplyAllConfiguredAttachments()
	{
		ClearAllAttachments();

		if (string.IsNullOrEmpty(UnitId) ||
			!GameHost.TryGetUnitOrBuildingMetadata(UnitId, out var uMeta) ||
			uMeta.ObjectAttachments == null)
		{
			return;
		}

		var atts = uMeta.ObjectAttachments;
		var parentNode = (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) ? _modelNode : (Node3D)this;
		var skeleton = FindSkeleton(parentNode);
		bool isNonRigged = IsBuilding || skeleton == null;

		if (isNonRigged)
		{
			ApplyPseudoSocketAttachmentList("ground", atts.ground ?? atts.root);
			ApplyPseudoSocketAttachmentList("center", atts.center ?? atts.chest);
			ApplyPseudoSocketAttachmentList("overhead", atts.overhead ?? atts.head);
			ApplyPseudoSocketAttachmentList("pivot", atts.pivot ?? atts.right_hand);
		}
		else
		{
			ApplyBoneAttachmentList(HumanoidBone.RightHand, atts.right_hand);
			ApplyBoneAttachmentList(HumanoidBone.LeftHand, atts.left_hand);
			ApplyBoneAttachmentList(HumanoidBone.Chest, atts.chest);
			ApplyBoneAttachmentList(HumanoidBone.Hips, atts.root);
			ApplyBoneAttachmentList(HumanoidBone.Head, atts.head);
			ApplyBoneAttachmentList(HumanoidBone.LeftFoot, atts.left_foot);
			ApplyBoneAttachmentList(HumanoidBone.RightFoot, atts.right_foot);

			ApplyPseudoSocketAttachmentList("ground", atts.ground);
			ApplyPseudoSocketAttachmentList("center", atts.center);
			ApplyPseudoSocketAttachmentList("overhead", atts.overhead);
			ApplyPseudoSocketAttachmentList("pivot", atts.pivot);
		}
	}

	private void ApplyBoneAttachmentList(HumanoidBone bone, List<Dictionary<string, HandAttachmentOrientation>>? list)
	{
		if (list == null) return;
		foreach (var dict in list)
		{
			if (dict == null) continue;
			foreach (var kvp in dict)
			{
				if (string.IsNullOrEmpty(kvp.Value.ParentAttachmentId))
				{
					SetSocketAttachment(bone, kvp.Key, kvp.Value.Position.ToGodotVector3(), kvp.Value.RotationDegrees.ToGodotVector3(), kvp.Value.Scale, kvp.Value.ScaleVector.ToGodotVector3(), kvp.Value.NormalOffset, false, null);
				}
			}
		}
		foreach (var dict in list)
		{
			if (dict == null) continue;
			foreach (var kvp in dict)
			{
				if (!string.IsNullOrEmpty(kvp.Value.ParentAttachmentId))
				{
					SetSocketAttachment(bone, kvp.Key, kvp.Value.Position.ToGodotVector3(), kvp.Value.RotationDegrees.ToGodotVector3(), kvp.Value.Scale, kvp.Value.ScaleVector.ToGodotVector3(), kvp.Value.NormalOffset, false, kvp.Value.ParentAttachmentId);
				}
			}
		}
	}

	private void ApplyPseudoSocketAttachmentList(string socket, List<Dictionary<string, HandAttachmentOrientation>>? list)
	{
		if (list == null) return;
		foreach (var dict in list)
		{
			if (dict == null) continue;
			foreach (var kvp in dict)
			{
				if (string.IsNullOrEmpty(kvp.Value.ParentAttachmentId))
				{
					SetPseudoSocketAttachment(socket, kvp.Key, kvp.Value.Position.ToGodotVector3(), kvp.Value.RotationDegrees.ToGodotVector3(), kvp.Value.Scale, kvp.Value.ScaleVector.ToGodotVector3(), kvp.Value.NormalOffset, false, null);
				}
			}
		}
		foreach (var dict in list)
		{
			if (dict == null) continue;
			foreach (var kvp in dict)
			{
				if (!string.IsNullOrEmpty(kvp.Value.ParentAttachmentId))
				{
					SetPseudoSocketAttachment(socket, kvp.Key, kvp.Value.Position.ToGodotVector3(), kvp.Value.RotationDegrees.ToGodotVector3(), kvp.Value.Scale, kvp.Value.ScaleVector.ToGodotVector3(), kvp.Value.NormalOffset, false, kvp.Value.ParentAttachmentId);
				}
			}
		}
	}

	private readonly Dictionary<string, Node3D> _pseudoSockets = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _currentPseudoSocketAttachmentIds = new(StringComparer.OrdinalIgnoreCase);

	public Aabb CalculateModelLocalAabb()
	{
		var visual = (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) ? _modelNode : (GetNodeOrNull<Node3D>("VisualModel") ?? this);
		Aabb combinedAabb = new Aabb();
		bool hasAabb = false;

		void Collect(Node current)
		{
			if (current is MeshInstance3D meshInst && meshInst.Mesh != null && meshInst.Visible)
			{
				Transform3D relXform = Transform3D.Identity;
				Node? curr = meshInst;
				while (curr != null && curr != visual)
				{
					if (curr is Node3D n3d)
					{
						relXform = n3d.Transform * relXform;
					}
					curr = curr.GetParent();
				}

				if (Mathf.Abs(relXform.Basis.Determinant()) > 0.0001f)
				{
					Aabb mAabb = meshInst.Mesh.GetAabb();
					Vector3 min = mAabb.Position;
					Vector3 max = mAabb.End;
					Vector3[] corners = new[]
					{
						new Vector3(min.X, min.Y, min.Z),
						new Vector3(min.X, min.Y, max.Z),
						new Vector3(min.X, max.Y, min.Z),
						new Vector3(min.X, max.Y, max.Z),
						new Vector3(max.X, min.Y, min.Z),
						new Vector3(max.X, min.Y, max.Z),
						new Vector3(max.X, max.Y, min.Z),
						new Vector3(max.X, max.Y, max.Z)
					};
					for (int i = 0; i < 8; i++)
					{
						Vector3 pt = relXform * corners[i];
						if (!hasAabb)
						{
							combinedAabb = new Aabb(pt, Vector3.Zero);
							hasAabb = true;
						}
						else
						{
							combinedAabb = combinedAabb.Expand(pt);
						}
					}
				}
			}
			foreach (Node child in current.GetChildren())
			{
				if (child is not BoneAttachment3D &&
					!child.Name.ToString().StartsWith("PseudoSocket_", StringComparison.OrdinalIgnoreCase) &&
					!child.Name.ToString().StartsWith("SocketAttachment_", StringComparison.OrdinalIgnoreCase) &&
					!child.Name.ToString().StartsWith("Att_", StringComparison.OrdinalIgnoreCase) &&
					!child.Name.ToString().StartsWith("AttVisual_", StringComparison.OrdinalIgnoreCase))
				{
					Collect(child);
				}
			}
		}

		Collect(visual);
		if (!hasAabb)
		{
			combinedAabb = new Aabb(new Vector3(-0.5f, 0f, -0.5f), new Vector3(1.0f, 1.8f, 1.0f));
		}
		return combinedAabb;
	}

	public void SetPseudoSocketAttachment(
		string socketName,
		string attachmentId,
		Vector3? posOffsetOverride = null,
		Vector3? rotOffsetOverride = null,
		float? scaleOverride = null,
		Vector3? scaleVectorOverride = null,
		float? normalOffsetOverride = null,
		bool clearExisting = false,
		string? parentAttachmentId = null)
	{
		string normSocket = (socketName ?? "ground").ToLowerInvariant().Replace("_", "").Replace(" ", "");
		if (string.IsNullOrEmpty(attachmentId) ||
			attachmentId.Equals("null", StringComparison.OrdinalIgnoreCase) ||
			attachmentId.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			_currentPseudoSocketAttachmentIds.Remove(normSocket);
			if (_pseudoSockets.TryGetValue(normSocket, out var existing) && GodotObject.IsInstanceValid(existing))
			{
				foreach (Node child in existing.GetChildren())
				{
					child.QueueFree();
				}
			}
			return;
		}

		if (clearExisting &&
			_currentPseudoSocketAttachmentIds.TryGetValue(normSocket, out var currentId) &&
			currentId == attachmentId &&
			_pseudoSockets.TryGetValue(normSocket, out var currentAttachment) &&
			GodotObject.IsInstanceValid(currentAttachment) &&
			currentAttachment.GetChildCount() > 0 &&
			!posOffsetOverride.HasValue &&
			!rotOffsetOverride.HasValue &&
			!scaleOverride.HasValue &&
			!scaleVectorOverride.HasValue &&
			!normalOffsetOverride.HasValue)
		{
			return;
		}

		_currentPseudoSocketAttachmentIds[normSocket] = attachmentId;

		var visual = (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) ? _modelNode : (GetNodeOrNull<Node3D>("VisualModel") ?? this);
		string nodeName = $"PseudoSocket_{normSocket}";
		var socketNode = visual.GetNodeOrNull<Node3D>(nodeName);
		if (socketNode == null)
		{
			socketNode = new Node3D { Name = nodeName };
			visual.AddChild(socketNode);
		}
		_pseudoSockets[normSocket] = socketNode;

		Aabb aabb = CalculateModelLocalAabb();
		var parentNode = (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) ? _modelNode : (Node3D)this;
		var skeleton = FindSkeleton(parentNode);
		bool isNonRigged = IsBuilding || skeleton == null;

		Vector3 anchorPos;
		if (isNonRigged)
		{
			anchorPos = normSocket switch
			{
				"center" or "centerofmass" => aabb.GetCenter(),
				"top" or "overhead" or "roof" => new Vector3(aabb.GetCenter().X, aabb.End.Y, aabb.GetCenter().Z),
				"base" or "ground" or "footprint" => new Vector3(aabb.GetCenter().X, aabb.Position.Y, aabb.GetCenter().Z),
				"pivot" or "origin" => Vector3.Zero,
				_ => Vector3.Zero
			};
		}
		else
		{
			anchorPos = normSocket switch
			{
				"ground" or "footprint" or "base" => new Vector3(0, aabb.Position.Y, 0),
				"center" or "centerofmass" => new Vector3(0, aabb.GetCenter().Y, 0),
				"overhead" or "crown" or "top" or "roof" => new Vector3(0, aabb.End.Y + 0.3f, 0),
				"pivot" or "origin" => Vector3.Zero,
				_ => Vector3.Zero
			};
		}
		socketNode.Position = anchorPos;
		socketNode.Rotation = Vector3.Zero;

		if (clearExisting)
		{
			foreach (Node child in socketNode.GetChildren())
			{
				socketNode.RemoveChild(child);
				child.QueueFree();
			}
		}

		Node3D? model = ResolveAndInstantiateAttachment(attachmentId, out float defScale, out Vector3 defPos, out Vector3 defRot);
		if (model != null)
		{
			float effectiveScale = scaleOverride ?? defScale;
			Vector3 effectivePos = posOffsetOverride ?? defPos;
			Vector3 effectiveRot = rotOffsetOverride ?? defRot;
			Vector3 effectiveScaleVec = scaleVectorOverride ?? (Vector3.One * (effectiveScale <= 0f ? 1.0f : effectiveScale));
			float effectiveNormalOffset = normalOffsetOverride ?? 0.0f;

			if (!string.IsNullOrEmpty(UnitId) &&
				GameHost.TryGetUnitOrBuildingMetadata(UnitId, out var uMeta) &&
				uMeta.TryGetObjectAttachment(normSocket, attachmentId, out var unitOrient))
			{
				if (!scaleOverride.HasValue && !scaleVectorOverride.HasValue)
				{
					effectiveScaleVec = unitOrient.ScaleVector.ToGodotVector3();
				}
				if (!posOffsetOverride.HasValue) effectivePos = unitOrient.Position.ToGodotVector3();
				if (!rotOffsetOverride.HasValue) effectiveRot = unitOrient.RotationDegrees.ToGodotVector3();
				if (!normalOffsetOverride.HasValue) effectiveNormalOffset = unitOrient.NormalOffset;
			}

			if (effectiveNormalOffset != 0.0f)
			{
				effectivePos += Vector3.Up * effectiveNormalOffset;
			}

			model.Position = effectivePos;
			model.RotationDegrees = effectiveRot;
			model.Scale = effectiveScaleVec;

			string cleanAttId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
				? attachmentId
				: System.IO.Path.GetFileNameWithoutExtension(attachmentId);
			model.Name = $"Att_{cleanAttId}";
			model.SetMeta("AttachmentId", attachmentId);
			model.SetMeta("CleanAttachmentId", cleanAttId);

			Node3D targetParent = socketNode;
			if (!string.IsNullOrEmpty(parentAttachmentId))
			{
				var parentMesh = FindAttachmentInNode(socketNode, parentAttachmentId)
					?? FindAttachmentInNode(visual, parentAttachmentId);
				if (parentMesh != null)
				{
					targetParent = parentMesh;
				}
			}

			targetParent.AddChild(model);

			if (model is not ProceduralVfxInstance3D)
			{
				Realm.Godot.Utils.ModelShaderManager.ApplyPlayerColorShader(model, PlayerColor, true, true, false);
				Realm.Godot.Utils.ModelShaderManager.SetIgnorePlayerColor(model, true);
			}
		}
	}

	public static Node3D? FindAttachmentInNode(Node root, string attachmentId)
	{
		if (root == null || !GodotObject.IsInstanceValid(root)) return null;
		if (string.IsNullOrEmpty(attachmentId)) return null;

		string cleanAttId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: System.IO.Path.GetFileNameWithoutExtension(attachmentId);

		foreach (Node child in root.GetChildren())
		{
			if (!GodotObject.IsInstanceValid(child) || child.IsQueuedForDeletion()) continue;

			if (child is Node3D node3D)
			{
				if (node3D.HasMeta("AttachmentId") && string.Equals(node3D.GetMeta("AttachmentId").AsString(), attachmentId, StringComparison.OrdinalIgnoreCase))
				{
					return node3D;
				}
				if (node3D.HasMeta("CleanAttachmentId") && string.Equals(node3D.GetMeta("CleanAttachmentId").AsString(), cleanAttId, StringComparison.OrdinalIgnoreCase))
				{
					return node3D;
				}
				if (string.Equals(node3D.Name.ToString(), $"Att_{cleanAttId}", StringComparison.OrdinalIgnoreCase))
				{
					return node3D;
				}

				var found = FindAttachmentInNode(node3D, attachmentId);
				if (found != null) return found;
			}
		}
		return null;
	}

	public static Node3D? ResolveAndInstantiateAttachment(string attachmentId, out float defaultScale, out Vector3 defaultPos, out Vector3 defaultRot)
	{
		defaultScale = 1.0f;
		defaultPos = Vector3.Zero;
		defaultRot = Vector3.Zero;

		if (string.IsNullOrEmpty(attachmentId)) return null;

		if (attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase))
		{
			string vfxKey = attachmentId.Substring(4);
			VfxAttachmentConfig config;
			if (GameHost.VfxRegistry.TryGetValue(vfxKey, out var regCfg))
			{
				config = regCfg.Clone();
			}
			else if (Enum.TryParse<VfxPrimitiveType>(vfxKey, true, out var primType))
			{
				config = new VfxAttachmentConfig { VfxId = vfxKey, PrimitiveType = primType };
			}
			else
			{
				config = new VfxAttachmentConfig { VfxId = vfxKey, Name = vfxKey };
			}

			var vfxInstance = new ProceduralVfxInstance3D(config);
			defaultScale = 1.0f;
			defaultPos = config.PositionOffset.ToGodotVector3();
			defaultRot = config.RotationOffset.ToGodotVector3();
			return vfxInstance;
		}

		if (GameHost.VfxRegistry.TryGetValue(attachmentId, out var vfxCfg))
		{
			var vfxInstance = new ProceduralVfxInstance3D(vfxCfg.Clone());
			defaultScale = 1.0f;
			defaultPos = vfxCfg.PositionOffset.ToGodotVector3();
			defaultRot = vfxCfg.RotationOffset.ToGodotVector3();
			return vfxInstance;
		}

		if (Enum.TryParse<VfxPrimitiveType>(attachmentId, true, out var parsedPrim))
		{
			var cfg = new VfxAttachmentConfig { VfxId = attachmentId, PrimitiveType = parsedPrim };
			var vfxInstance = new ProceduralVfxInstance3D(cfg);
			defaultScale = 1.0f;
			defaultPos = cfg.PositionOffset.ToGodotVector3();
			defaultRot = cfg.RotationOffset.ToGodotVector3();
			return vfxInstance;
		}

		string modelPath = string.Empty;
		AttachmentMetadata? attMeta = null;

		if (GameHost.AttachmentRegistry.TryGetValue(attachmentId, out var meta))
		{
			attMeta = meta;
			modelPath = meta.ModelPath;
			defaultScale = meta.Scale <= 0f ? 1.0f : meta.Scale;
			defaultPos = meta.PositionOffset.ToGodotVector3();
			defaultRot = meta.RotationOffset.ToGodotVector3();
		}
		else if (GameHost.PropRegistry.TryGetValue(attachmentId, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath))
		{
			modelPath = propMeta.ModelPath;
			defaultScale = propMeta.Scale <= 0f ? 1.0f : propMeta.Scale;
			defaultPos = new Vector3(0f, propMeta.YOffset, 0f);
		}
		else if (GameHost.ResourceRegistry.TryGetValue(attachmentId, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath))
		{
			modelPath = resMeta.ModelPath;
			defaultScale = resMeta.Scale <= 0f ? 1.0f : resMeta.Scale;
			defaultPos = new Vector3(0f, resMeta.YOffset, 0f);
		}
		Node? loaded = !string.IsNullOrEmpty(modelPath) ? Realm.Godot.Utils.ModelCache.GetModel(modelPath) : null;
		if (loaded == null)
		{
			loaded = Realm.Godot.Utils.ModelCache.GetModel(attachmentId);
		}

		if (loaded == null)
		{
			loaded = Realm.Godot.Utils.ModelCache.GetModel(attachmentId);
		}

		if (loaded is Node3D loadedNode)
		{
			if (attMeta != null && !string.IsNullOrEmpty(attMeta.ChildVfxId))
			{
				var childVfx = ResolveAndInstantiateAttachment(attMeta.ChildVfxId, out _, out _, out _);
				if (childVfx != null)
				{
					childVfx.Position = attMeta.ChildVfxPosition.ToGodotVector3();
					childVfx.RotationDegrees = attMeta.ChildVfxRotation.ToGodotVector3();
					var childScale = attMeta.ChildVfxScale.ToGodotVector3();
					childVfx.Scale = childScale == Vector3.Zero ? Vector3.One : childScale;
					string cleanChildId = attMeta.ChildVfxId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
						? attMeta.ChildVfxId
						: System.IO.Path.GetFileNameWithoutExtension(attMeta.ChildVfxId);
					childVfx.Name = $"ChildVfx_{cleanChildId}";
					childVfx.SetMeta("AttachmentId", attMeta.ChildVfxId);
					childVfx.SetMeta("CleanAttachmentId", cleanChildId);
					loadedNode.AddChild(childVfx);
				}
			}
			return loadedNode;
		}

		return null;
	}

	public void UpdateHandAttachmentsForAnimation(string animName, string resolvedName)
	{
		if (string.IsNullOrEmpty(UnitId) || !GameHost.UnitRegistry.TryGetValue(UnitId, out var uMeta))
		{
			return;
		}

		if (uMeta.Animations == null || uMeta.Animations.Count == 0)
		{
			return;
		}

		string actionType = animName;
		int variantIndex = 0;

		int underscoreIdx = animName.LastIndexOf('_');
		if (underscoreIdx > 0 && int.TryParse(animName.Substring(underscoreIdx + 1), out int parsedIdx))
		{
			actionType = animName.Substring(0, underscoreIdx);
			variantIndex = parsedIdx;
		}

		UnitAnimationEntry? matchedEntry = null;

		foreach (var kvp in uMeta.Animations)
		{
			if (kvp.Key.Equals(actionType, StringComparison.OrdinalIgnoreCase))
			{
				if (kvp.Value != null && kvp.Value.Count > 0)
				{
					int clampedIndex = Math.Clamp(variantIndex, 0, kvp.Value.Count - 1);
					matchedEntry = kvp.Value[clampedIndex];
				}
				break;
			}
		}

		if (!matchedEntry.HasValue)
		{
			foreach (var kvp in uMeta.Animations)
			{
				if (kvp.Value != null)
				{
					for (int i = 0; i < kvp.Value.Count; i++)
					{
						var entry = kvp.Value[i];
						if (!string.IsNullOrEmpty(entry.Animation) &&
							(entry.Animation.Equals(animName, StringComparison.OrdinalIgnoreCase) ||
							 entry.Animation.Equals(resolvedName, StringComparison.OrdinalIgnoreCase)))
						{
							matchedEntry = entry;
							break;
						}
					}
					if (matchedEntry.HasValue) break;
				}
			}
		}

		if (matchedEntry.HasValue)
		{
			if (!string.IsNullOrEmpty(matchedEntry.Value.RightHandAttachment))
			{
				SetHandAttachment(HumanoidBone.RightHand, matchedEntry.Value.RightHandAttachment);
			}
			if (!string.IsNullOrEmpty(matchedEntry.Value.LeftHandAttachment))
			{
				SetHandAttachment(HumanoidBone.LeftHand, matchedEntry.Value.LeftHandAttachment);
			}
		}
	}

	private StringName? ResolveRandomAnimationVariant(string animName, string[] animations)
	{
		var variants = new List<StringName>();
		string prefix = $"{animName}_";
		foreach (var name in animations)
		{
			string nameStr = name.ToString();
			if (nameStr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				variants.Add(name);
			}
		}

		if (variants.Count > 0)
		{
			int randIdx = Random.Shared.Next(variants.Count);
			return variants[randIdx];
		}
		
		return null;
	}

	private StringName? TryRetargetAnimation(string animName, StringName direct)
	{
		string resolvedPath = Realm.Godot.Animation.AnimationRetargetingService.ResolveAnimationFilePath(animName, UnitId);
		if (!string.IsNullOrEmpty(resolvedPath))
		{
			var animData = Realm.Godot.Animation.AnimationRetargetingService.GetOrLoadRanimData(resolvedPath);
			if (animData != null && _modelNode != null)
			{
				if (Realm.Godot.Animation.AnimationRetargetingService.RetargetAndBind(animData, _modelNode, animName, out _))
				{
					return direct;
				}
			}
		}

		var fallbackAnim = animName switch
		{
			"Idle" => Realm.Godot.Animation.AnimationRetargetingService.GetIdleAnimationData(UnitId),
			"Walk" => Realm.Godot.Animation.RealmDefaultAnimations.Walk,
			"Attack" => Realm.Godot.Animation.RealmDefaultAnimations.Attack,
			"Death" => Realm.Godot.Animation.RealmDefaultAnimations.Death,
			"Labor" => Realm.Godot.Animation.RealmDefaultAnimations.Labor,
			"Spell_Cast" => Realm.Godot.Animation.RealmDefaultAnimations.Spell_Cast,
			"Dance" => Realm.Godot.Animation.RealmDefaultAnimations.Dance,
			_ => null
		};

		if (fallbackAnim != null && _modelNode != null)
		{
			if (Realm.Godot.Animation.AnimationRetargetingService.RetargetAndBind(fallbackAnim, _modelNode, animName, out _))
			{
				return direct;
			}
		}

		return null;
	}

	private StringName ResolveAnimationName(string animName)
	{
		if (_animationPlayer == null) return null;

		var animations = _animationPlayer.GetAnimationList();

		if (!animName.Contains('_'))
		{
			var randomVariant = ResolveRandomAnimationVariant(animName, animations);
			if (randomVariant != null) return randomVariant;
		}

		StringName direct = new StringName(animName);
		if (_animationPlayer.HasAnimation(direct))
		{
			return direct;
		}

		foreach (var name in animations)
		{
			if (name.ToString().Equals(animName, System.StringComparison.OrdinalIgnoreCase))
				return name;
		}

		var retargeted = TryRetargetAnimation(animName, direct);
		if (retargeted != null) return retargeted;

		if (animations.Length > 0)
		{
			return animations[0];
		}

		return null;
	}

	public void ApplyModelTint(Color color)
	{
		SetPlayerColor(color);
	}




	protected override Color GetSelectionRingColor()
	{
		return new Color(0.22f, 0.54f, 0.26f);
	}

	public static float GetMinY(Node node, Transform3D currentTransform = default)
	{
		if (currentTransform == default) currentTransform = Transform3D.Identity;
		float minY = float.MaxValue;
		bool foundMesh = false;
		GetMinYRecursive(node, currentTransform, ref minY, ref foundMesh);
		return foundMesh ? minY : 0f;
	}

	public static void GetMinYRecursive(Node node, Transform3D currentTransform, ref float minY, ref bool foundMesh)
	{
		if (node is MeshInstance3D meshInstance)
		{
			var mesh = meshInstance.Mesh;
			if (mesh != null)
			{
				var localAabb = mesh.GetAabb();

				Vector3[] corners = new Vector3[8] {
					new Vector3(localAabb.Position.X, localAabb.Position.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X, localAabb.Position.Y, localAabb.Position.Z + localAabb.Size.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y, localAabb.Position.Z + localAabb.Size.Z),
					new Vector3(localAabb.Position.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z + localAabb.Size.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z + localAabb.Size.Z)
				};

				foreach (var corner in corners)
				{
					Vector3 modelSpaceCorner = currentTransform * corner;
					if (!foundMesh || modelSpaceCorner.Y < minY)
					{
						minY = modelSpaceCorner.Y;
						foundMesh = true;
					}
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node3D child3D)
			{
				GetMinYRecursive(child, currentTransform * child3D.Transform, ref minY, ref foundMesh);
			}
			else
			{
				GetMinYRecursive(child, currentTransform, ref minY, ref foundMesh);
			}
		}
	}

	private void GetRemainingRallyPoints(System.Collections.Generic.List<Vector3> points)
	{
		points.Clear();
		if (GameHost.Instance == null || !GameHost.Instance.EcsWorld.IsAlive(Entity)) return;

		var world = GameHost.Instance.EcsWorld;
		points.Add(GlobalPosition);

		if (world.Has<Realm.Ecs.Components.Core.RallyPoint>(Entity))
		{
			var rp = world.Get<Realm.Ecs.Components.Core.RallyPoint>(Entity);
			for (int i = 0; i < rp.Count; i++)
			{
				points.Add(new Vector3(rp.Waypoints[i].X, rp.Waypoints[i].Y, rp.Waypoints[i].Z));
			}
		}
		else
		{
			points.Add(GlobalPosition + new Vector3(0, 0, 8));
		}
	}

	private void EnsureRallyVisualsContainer()
	{
		if (_rallyVisualsContainer == null)
		{
			_rallyVisualsContainer = new Node3D();
			_rallyVisualsContainer.TopLevel = true;
			AddChild(_rallyVisualsContainer);
		}
	}

	private MeshInstance3D GetOrCreateRallyMarker(int index)
	{
		while (_rallyMarkersPool.Count <= index)
		{
			var marker = new MeshInstance3D();
			var cylinderMesh = new CylinderMesh();
			cylinderMesh.TopRadius = 0.25f;
			cylinderMesh.BottomRadius = 0.25f;
			cylinderMesh.Height = 0.05f;
			marker.Mesh = cylinderMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f);
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.95f, 0.82f, 0.55f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			marker.MaterialOverride = mat;

			_rallyVisualsContainer.AddChild(marker);
			_rallyMarkersPool.Add(marker);
		}
		return _rallyMarkersPool[index];
	}

	private MeshInstance3D GetOrCreateRallyLine(int index)
	{
		while (_rallyLinesPool.Count <= index)
		{
			var line = new MeshInstance3D();
			var boxMesh = new BoxMesh();
			boxMesh.Size = new Vector3(0.1f, 0.05f, 1.0f);
			line.Mesh = boxMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f, 0.5f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.95f, 0.82f, 0.55f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			line.MaterialOverride = mat;

			_rallyVisualsContainer.AddChild(line);
			_rallyLinesPool.Add(line);
		}
		return _rallyLinesPool[index];
	}

	private void UpdateRallyMarkers(System.Collections.Generic.List<Vector3> points, int markerCountNeeded)
	{
		for (int i = 0; i < markerCountNeeded; i++)
		{
			var marker = GetOrCreateRallyMarker(i);
			marker.Visible = true;
			Vector3 pos = points[i + 1];
			if (GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.GetHeightAndNormal(pos.X, pos.Z, out float h, out _);
				pos.Y = h + 0.1f;
			}
			marker.GlobalPosition = pos;
		}
		for (int i = Mathf.Max(0, markerCountNeeded); i < _rallyMarkersPool.Count; i++)
		{
			_rallyMarkersPool[i].Visible = false;
		}
	}

	private void UpdateRallyLines(System.Collections.Generic.List<Vector3> points, int lineCountNeeded)
	{
		for (int i = 0; i < lineCountNeeded; i++)
		{
			var line = GetOrCreateRallyLine(i);
			line.Visible = true;

			Vector3 start = points[i];
			Vector3 end = points[i + 1];

			if (GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.GetHeightAndNormal(start.X, start.Z, out float hStart, out _);
				start.Y = hStart + 0.1f;

				GameHost.Instance.GroundTerrain.GetHeightAndNormal(end.X, end.Z, out float hEnd, out _);
				end.Y = hEnd + 0.1f;
			}

			Vector3 diff = end - start;
			float length = diff.Length();

			if (length > 0.05f)
			{
				Vector3 direction = diff.Normalized();
				Vector3 upVector = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
				var basis = Basis.LookingAt(direction, upVector).Scaled(new Vector3(1f, 1f, length));
				line.GlobalTransform = new Transform3D(basis, start + diff * 0.5f);
			}
			else
			{
				line.Visible = false;
			}
		}
		for (int i = lineCountNeeded; i < _rallyLinesPool.Count; i++)
		{
			_rallyLinesPool[i].Visible = false;
		}
	}

	public void UpdateRallyVisuals()
	{
		if (!IsBuilding || IsEnemy) return;

		if (GameHost.Instance == null || !GameHost.Instance.EcsWorld.IsAlive(Entity)) return;

		EnsureRallyVisualsContainer();
		if (!GameHost.Instance.CanProduceUnits(this))
		{
			_rallyVisualsContainer.Visible = false;
			return;
		}
		_rallyVisualsContainer.Visible = IsSelected;

		if (!IsSelected) return;

		var points = new System.Collections.Generic.List<Vector3>();
		GetRemainingRallyPoints(points);

		if (points.Count <= 1)
		{
			if (_rallyMarker != null) _rallyMarker.Visible = false;
			return;
		}

		if (_rallyMarker == null)
		{
			_rallyMarker = new MeshInstance3D();
			var cylinderMesh = new CylinderMesh();
			cylinderMesh.TopRadius = 0.1f;
			cylinderMesh.BottomRadius = 0.1f;
			cylinderMesh.Height = 3.0f;
			_rallyMarker.Mesh = cylinderMesh;
			
			var markerMat = new StandardMaterial3D();
			markerMat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f);
			markerMat.EmissionEnabled = true;
			markerMat.Emission = new Color(0.95f, 0.82f, 0.55f);
			_rallyMarker.MaterialOverride = markerMat;
			
			_rallyVisualsContainer.AddChild(_rallyMarker);
		}

		_rallyMarker.Visible = true;
		Vector3 finalPos = points[points.Count - 1];
		if (GameHost.Instance.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.GetHeightAndNormal(finalPos.X, finalPos.Z, out float hFinal, out _);
			finalPos.Y = hFinal + 1.5f;
		}
		_rallyMarker.GlobalPosition = finalPos;

		UpdateRallyMarkers(points, points.Count - 2);
		UpdateRallyLines(points, points.Count - 1);
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (IsSelected && !IsEnemy)
		{
			if (IsBuilding)
			{
				UpdateRallyVisuals();
			}
			else
			{
				UpdatePathVisuals();
			}
		}
		else
		{
			HidePathVisuals();
			if (_rallyVisualsContainer != null)
			{
				_rallyVisualsContainer.Visible = false;
			}
		}
	}

	private void EnsurePathVisualsContainer()
	{
		if (_pathVisualsContainer == null)
		{
			_pathVisualsContainer = new Node3D();
			_pathVisualsContainer.TopLevel = true;
			AddChild(_pathVisualsContainer);
		}
	}

	public bool IsAttackPath { get; set; } = false;

	private void GetRemainingPathPoints(System.Collections.Generic.List<Vector3> points)
	{
		points.Clear();
		IsAttackPath = false;
		if (GameHost.Instance == null || !GameHost.Instance.EcsWorld.IsAlive(Entity)) return;

		var world = GameHost.Instance.EcsWorld;
		if (world.Has<Realm.Ecs.Components.Combat.AttackTarget>(Entity) || world.Has<Realm.Ecs.Components.Movement.AttackMove>(Entity))
		{
			IsAttackPath = true;
		}

		points.Add(GlobalPosition);

		if (world.Has<Realm.Ecs.Services.PathFollow>(Entity))
		{
			var pf = world.Get<Realm.Ecs.Services.PathFollow>(Entity);
			for (int i = pf.CurrentWaypointIndex; i < pf.WaypointCount; i++)
			{
				points.Add(new Vector3(pf.Waypoints[i].X, pf.Waypoints[i].Y, pf.Waypoints[i].Z));
			}
		}
		else if (world.Has<Realm.Ecs.Components.Movement.MoveTo>(Entity))
		{
			var mt = world.Get<Realm.Ecs.Components.Movement.MoveTo>(Entity);
			points.Add(new Vector3(mt.Target.X, mt.Target.Y, mt.Target.Z));
		}

		if (world.Has<Realm.Ecs.Components.Movement.WaypointQueue>(Entity))
		{
			var q = world.Get<Realm.Ecs.Components.Movement.WaypointQueue>(Entity);
			for (int i = 0; i < q.Count; i++)
			{
				points.Add(new Vector3(q.Waypoints[i].X, q.Waypoints[i].Y, q.Waypoints[i].Z));
			}
		}
	}

	private void UpdatePathVisuals()
	{
		EnsurePathVisualsContainer();
		_pathVisualsContainer.Visible = true;

		var points = new System.Collections.Generic.List<Vector3>();
		GetRemainingPathPoints(points);

		if (points.Count <= 1)
		{
			HidePathVisuals();
			return;
		}

		int markerCountNeeded = points.Count - 1;
		for (int i = 0; i < markerCountNeeded; i++)
		{
			var marker = GetOrCreateMarker(i);
			marker.Visible = true;
			Vector3 pos = points[i + 1];
			if (GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.GetHeightAndNormal(pos.X, pos.Z, out float h, out _);
				pos.Y = h + 0.1f;
			}
			marker.GlobalPosition = pos;
		}
		for (int i = markerCountNeeded; i < _pathMarkersPool.Count; i++)
		{
			_pathMarkersPool[i].Visible = false;
		}

		int lineCountNeeded = points.Count - 1;
		for (int i = 0; i < lineCountNeeded; i++)
		{
			var line = GetOrCreateLine(i);
			line.Visible = true;

			Vector3 start = points[i];
			Vector3 end = points[i + 1];

			if (GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.GetHeightAndNormal(start.X, start.Z, out float hStart, out _);
				start.Y = hStart + 0.1f;

				GameHost.Instance.GroundTerrain.GetHeightAndNormal(end.X, end.Z, out float hEnd, out _);
				end.Y = hEnd + 0.1f;
			}

			Vector3 diff = end - start;
			float length = diff.Length();

			if (length > 0.05f)
			{
				Vector3 direction = diff.Normalized();
				Vector3 upVector = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
				var basis = Basis.LookingAt(direction, upVector).Scaled(new Vector3(1f, 1f, length));
				line.GlobalTransform = new Transform3D(basis, start + diff * 0.5f);
			}
			else
			{
				line.Visible = false;
			}
		}
		for (int i = lineCountNeeded; i < _pathLinesPool.Count; i++)
		{
			_pathLinesPool[i].Visible = false;
		}
	}

	private void HidePathVisuals()
	{
		if (_pathVisualsContainer != null)
		{
			_pathVisualsContainer.Visible = false;
		}
	}

	private MeshInstance3D GetOrCreateMarker(int index)
	{
		while (_pathMarkersPool.Count <= index)
		{
			var marker = new MeshInstance3D();
			var cylinderMesh = new CylinderMesh();
			cylinderMesh.TopRadius = 0.25f;
			cylinderMesh.BottomRadius = 0.25f;
			cylinderMesh.Height = 0.05f;
			marker.Mesh = cylinderMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.2f, 0.6f, 1.0f);
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.2f, 0.6f, 1.0f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			marker.MaterialOverride = mat;

			_pathVisualsContainer.AddChild(marker);
			_pathMarkersPool.Add(marker);
		}
		var m = _pathMarkersPool[index];
		if (m.MaterialOverride is StandardMaterial3D markerMat)
		{
			Color c = IsAttackPath ? new Color(0.9f, 0.1f, 0.1f) : new Color(0.2f, 0.6f, 1.0f);
			markerMat.AlbedoColor = c;
			markerMat.Emission = c;
		}
		return m;
	}

	private MeshInstance3D GetOrCreateLine(int index)
	{
		while (_pathLinesPool.Count <= index)
		{
			var line = new MeshInstance3D();
			var boxMesh = new BoxMesh();
			boxMesh.Size = new Vector3(0.1f, 0.05f, 1.0f);
			line.Mesh = boxMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.2f, 0.6f, 1.0f, 0.5f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.2f, 0.6f, 1.0f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			line.MaterialOverride = mat;

			_pathVisualsContainer.AddChild(line);
			_pathLinesPool.Add(line);
		}
		var l = _pathLinesPool[index];
		if (l.MaterialOverride is StandardMaterial3D lineMat)
		{
			Color c = IsAttackPath ? new Color(0.9f, 0.1f, 0.1f, 0.5f) : new Color(0.2f, 0.6f, 1.0f, 0.5f);
			Color emissionColor = IsAttackPath ? new Color(0.9f, 0.1f, 0.1f) : new Color(0.2f, 0.6f, 1.0f);
			lineMat.AlbedoColor = c;
			lineMat.Emission = emissionColor;
		}
		return l;
	}

	public Color Modulate
	{
		get => new Color(1f, 1f, 1f, _modulateAlpha);
		set
		{
			_modulateAlpha = value.A;
			if (_modelNode != null)
				SetAlphaRecursive(_modelNode, value.A);
		}
	}

	private float _modulateAlpha = 1f;

	public void SetConstructionAlpha(float alpha)
	{
		if (_modelNode != null)
		{
			SetAlphaRecursive(_modelNode, alpha);
		}
	}

	private void SetAlphaRecursive(Node node, float alpha)
	{
		if (node is MeshInstance3D mesh)
		{
			Material mat = mesh.MaterialOverride;
			if (mat is StandardMaterial3D stdMat)
			{
				var dup = (StandardMaterial3D)stdMat.Duplicate();
				dup.Transparency = alpha < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
				var c = dup.AlbedoColor;
				c.A = alpha;
				dup.AlbedoColor = c;
				mesh.MaterialOverride = dup;
			}
		}
		foreach (var child in node.GetChildren())
		{
			SetAlphaRecursive(child, alpha);
		}
	}
}

public partial class Building3D : Unit3D
{
}