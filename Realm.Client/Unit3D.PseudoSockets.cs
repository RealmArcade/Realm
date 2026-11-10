using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Animation;

namespace Realm.Client;
public partial class Unit3D
{
	public const string PlayerColorGlowAttachmentId = "glow:player_color";

	private readonly Dictionary<string, Node3D> _pseudoSockets = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _currentPseudoSocketAttachmentIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, MeshInstance3D> _pseudoSocketGlows = new(StringComparer.OrdinalIgnoreCase);
	private IReadOnlyList<PseudoSocketPoint>? _resolvedPseudoSocketPoints;
	private List<PseudoSocketAnchor>? _resolvedAuthorAnchors;

	public Aabb CalculateModelLocalAabb()
	{
		var visual = (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) ? _modelNode : (GetNodeOrNull<Node3D>("VisualModel") ?? this);
		Aabb combinedAabb = new Aabb();
		bool hasAabb = false;

		CalculateCombinedAabb(visual, visual, ref combinedAabb, ref hasAabb);

		if (!hasAabb)
		{
			combinedAabb = new Aabb(new Vector3(-0.5f, 0f, -0.5f), new Vector3(1.0f, 1.8f, 1.0f));
		}

		return combinedAabb;
	}

	private void CalculateCombinedAabb(Node current, Node rootVisual, ref Aabb combinedAabb, ref bool hasAabb)
	{
		UpdateCombinedAabbFromMesh(current, rootVisual, ref combinedAabb, ref hasAabb);

		foreach (Node child in current.GetChildren())
		{
			if (ShouldCalculateChildAabb(child))
			{
				CalculateCombinedAabb(child, rootVisual, ref combinedAabb, ref hasAabb);
			}
		}
	}

	private bool ShouldCalculateChildAabb(Node child)
	{
		if (child is BoneAttachment3D) return false;
		string childName = child.Name.ToString();
		if (childName.StartsWith("PseudoSocket_", StringComparison.OrdinalIgnoreCase)) return false;
		if (childName.StartsWith("SocketAttachment_", StringComparison.OrdinalIgnoreCase)) return false;
		if (childName.StartsWith("Att_", StringComparison.OrdinalIgnoreCase)) return false;
		if (childName.StartsWith("AttVisual_", StringComparison.OrdinalIgnoreCase)) return false;
		return true;
	}

	private void UpdateCombinedAabbFromMesh(Node current, Node rootVisual, ref Aabb combinedAabb, ref bool hasAabb)
	{
		if (current is not MeshInstance3D meshInst) return;
		if (meshInst.Mesh == null || !meshInst.Visible) return;

		Transform3D relXform = CalculateRelativeTransform(meshInst, rootVisual);

		if (Mathf.Abs(relXform.Basis.Determinant()) <= 0.0001f) return;

		Aabb mAabb = meshInst.Mesh.GetAabb();
		Vector3[] corners = Realm.Client.Utils.MathUtils.GetAabbCorners(mAabb);
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

	private Transform3D CalculateRelativeTransform(Node current, Node rootVisual)
	{
		Transform3D relXform = Transform3D.Identity;
		Node? curr = current;
		while (curr != null && curr != rootVisual)
		{
			if (curr is Node3D n3d) relXform = n3d.Transform * relXform;
			curr = curr.GetParent();
		}

		return relXform;
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
		string normSocket = NormalizeSocketName(socketName);

		if (TryClearPseudoSocketAttachment(normSocket, attachmentId, clearExisting, posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride))
		{
			return;
		}

		var visual = GetVisualModelNode();
		if (PseudoSocketCache.IsRealSocket(normSocket, GetRealSocketIds())) return;

		Node3D? socketNode = EnsurePseudoSocketNode(visual, normSocket);
		if (socketNode == null) return;

		_currentPseudoSocketAttachmentIds[normSocket] = attachmentId;

		PseudoSocketPoint socketPoint = ResolvePseudoSocketPoint(visual, normSocket);
		socketNode.Position = socketPoint.LocalPosition;
		socketNode.RotationDegrees = socketPoint.LocalRotationDegrees;

		if (clearExisting) ClearNodeChildren(socketNode);

		if (IsPlayerColorGlowAttachment(attachmentId))
		{
			AttachPlayerColorGlowToPseudoSocket(socketNode, normSocket);
			return;
		}

		Node3D? model = ResolveAndInstantiateAttachment(attachmentId, out float defScale, out Vector3 defPos, out Vector3 defRot);
		if (model == null) return;

		if (model is ProceduralVfxInstance3D vfxAttachment && IsGroundSocket(normSocket))
		{
			vfxAttachment.ConfigureAsGroundAura(PlayerColor);
		}

		ApplyPseudoSocketInstance(model, attachmentId, socketNode, visual, parentAttachmentId, normSocket, defScale, defPos, defRot, posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride);
	}

	public bool SetBillboardIconVisible(string attachmentId, bool visible)
	{
		if (string.IsNullOrEmpty(attachmentId)) return false;
		if (!GameHost.AttachmentRegistry.TryGetValue(attachmentId, out var attachmentMetadata)) return false;

		var iconConfig = attachmentMetadata.BillboardIcon;
		if (iconConfig == null) return false;

		string socketName = string.IsNullOrWhiteSpace(iconConfig.Socket) ? "Overhead" : iconConfig.Socket;
		string normSocket = NormalizeSocketName(socketName);

		if (!visible)
		{
			RemoveBillboardIcon(normSocket, attachmentId);
			return true;
		}

		if (TryFindBillboardIconNode(normSocket, attachmentId) != null) return true;

		if (PseudoSocketCache.IsRealSocket(normSocket, GetRealSocketIds()) && TryResolveHumanoidSocket(normSocket, out var bone))
		{
			SetSocketAttachment(bone, attachmentId);
			return true;
		}

		SetPseudoSocketAttachment(socketName, attachmentId);
		return true;
	}

	private void RemoveBillboardIcon(string normSocket, string attachmentId)
	{
		var icon = TryFindBillboardIconNode(normSocket, attachmentId);
		if (icon == null) return;

		icon.GetParent()?.RemoveChild(icon);
		icon.QueueFree();

		if (_currentPseudoSocketAttachmentIds.TryGetValue(normSocket, out var currentPseudoAttachmentId) && string.Equals(currentPseudoAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase))
		{
			_currentPseudoSocketAttachmentIds.Remove(normSocket);
		}

		if (!TryResolveHumanoidSocket(normSocket, out var bone)) return;

		if (_currentBoneAttachmentIds.TryGetValue(bone, out var currentBoneAttachmentId) && string.Equals(currentBoneAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase))
		{
			_currentBoneAttachmentIds.Remove(bone);
		}

		if (bone == HumanoidBone.RightHand && string.Equals(_currentRightAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase)) _currentRightAttachmentId = null;
		else if (bone == HumanoidBone.LeftHand && string.Equals(_currentLeftAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase)) _currentLeftAttachmentId = null;
	}

	private BillboardIconAttachment3D? TryFindBillboardIconNode(string normSocket, string attachmentId)
	{
		if (_pseudoSockets.TryGetValue(normSocket, out var pseudoSocketNode) && GodotObject.IsInstanceValid(pseudoSocketNode))
		{
			var pseudoIcon = FindBillboardIconChild(pseudoSocketNode, attachmentId);
			if (pseudoIcon != null) return pseudoIcon;
		}

		if (!TryResolveHumanoidSocket(normSocket, out var bone)) return null;
		if (!_boneAttachments.TryGetValue(bone, out var boneAttachmentNode)) return null;
		if (!GodotObject.IsInstanceValid(boneAttachmentNode)) return null;

		return FindBillboardIconChild(boneAttachmentNode, attachmentId);
	}

	private static BillboardIconAttachment3D? FindBillboardIconChild(Node parentNode, string attachmentId)
	{
		foreach (Node child in parentNode.GetChildren())
		{
			if (!GodotObject.IsInstanceValid(child) || child.IsQueuedForDeletion()) continue;

			if (child is BillboardIconAttachment3D icon && IsBillboardIconForAttachment(icon, attachmentId)) return icon;

			var nestedIcon = FindBillboardIconChild(child, attachmentId);
			if (nestedIcon != null) return nestedIcon;
		}

		return null;
	}

	private static bool IsBillboardIconForAttachment(BillboardIconAttachment3D icon, string attachmentId)
	{
		if (icon.HasMeta("AttachmentId") && string.Equals(icon.GetMeta("AttachmentId").AsString(), attachmentId, StringComparison.OrdinalIgnoreCase)) return true;

		string cleanAttachmentId = GetCleanAttachmentId(attachmentId);
		return icon.HasMeta("CleanAttachmentId") && string.Equals(icon.GetMeta("CleanAttachmentId").AsString(), cleanAttachmentId, StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryResolveHumanoidSocket(string normSocket, out HumanoidBone bone)
	{
		return Enum.TryParse(normSocket, true, out bone) && Enum.IsDefined(bone);
	}

	public static bool IsPlayerColorGlowAttachment(string attachmentId)
	{
		return string.Equals(attachmentId, PlayerColorGlowAttachmentId, StringComparison.OrdinalIgnoreCase);
	}

	private void AttachPlayerColorGlowToPseudoSocket(Node3D socketNode, string normSocket)
	{
		if (_pseudoSocketGlows.TryGetValue(normSocket, out var existing) && GodotObject.IsInstanceValid(existing))
		{
			existing.GetParent()?.RemoveChild(existing);
			existing.QueueFree();
			_pseudoSocketGlows.Remove(normSocket);
		}

		float radius = Mathf.Max(0.4f, GetBaseObstacleRadius());
		var glow = new MeshInstance3D
		{
			Name = $"PlayerColorGlow_{normSocket}",
			Mesh = VisualSelectionHelper.GetOrCreatePlayerColorGlowMesh(radius),
			MaterialOverride = SharedMaterials.GetOrCreatePlayerColorGlowMaterial(PlayerColor),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled
		};

		socketNode.AddChild(glow);
		_pseudoSocketGlows[normSocket] = glow;
	}

	private IReadOnlyList<string>? GetRealSocketIds()
	{
		return FindSkeleton(GetParentModelNode()) != null ? PseudoSocketCache.HumanoidSocketIds : null;
	}

	private List<PseudoSocketAnchor>? GetAuthorPseudoSockets()
	{
		if (string.IsNullOrEmpty(UnitId)) return null;

		if (GameHost.TryGetUnitOrBuildingMetadata(UnitId, out var meta) && meta.PseudoSockets != null)
		{
			return meta.PseudoSockets;
		}

		var registryService = GameContext.RegistryService;
		if (registryService.PropRegistry != null && registryService.PropRegistry.TryGetValue(UnitId, out var propMeta))
		{
			return propMeta.PseudoSockets;
		}

		if (registryService.ResourceRegistry != null && registryService.ResourceRegistry.TryGetValue(UnitId, out var resourceMeta))
		{
			return resourceMeta.PseudoSockets;
		}

		return null;
	}

	private IReadOnlyList<PseudoSocketPoint> GetPseudoSocketPoints(Node3D visual)
	{
		var authorAnchors = GetAuthorPseudoSockets();
		if (_resolvedPseudoSocketPoints != null && ReferenceEquals(_resolvedAuthorAnchors, authorAnchors))
		{
			return _resolvedPseudoSocketPoints;
		}

		_resolvedAuthorAnchors = authorAnchors;
		_resolvedPseudoSocketPoints = PseudoSocketCache.GetSockets(visual, ModelPath, authorAnchors, GetRealSocketIds());
		return _resolvedPseudoSocketPoints;
	}

	private PseudoSocketPoint ResolvePseudoSocketPoint(Node3D visual, string normSocket)
	{
		var points = GetPseudoSocketPoints(visual);
		string canonicalId = PseudoSocketCache.NormalizeSocketId(normSocket) ?? normSocket;

		for (int index = 0; index < points.Count; index++)
		{
			if (points[index].SocketId.Equals(canonicalId, StringComparison.OrdinalIgnoreCase))
			{
				return points[index];
			}
		}

		Aabb aabb = CalculateModelLocalAabb();
		bool isNonRigged = IsBuilding || FindSkeleton(GetParentModelNode()) == null;
		return new PseudoSocketPoint(canonicalId, canonicalId, CalculateLegacyPseudoSocketAnchorPos(canonicalId, aabb, isNonRigged), Vector3.Zero, PseudoSocketSource.Generated);
	}

	private static bool IsGroundSocket(string normSocket)
	{
		return normSocket is "ground" or "base" or "footprint";
	}

	private static string NormalizeSocketName(string socketName)
	{
		return (socketName ?? "ground").ToLowerInvariant().Replace("_", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "", StringComparison.OrdinalIgnoreCase);
	}

	private Node3D GetVisualModelNode()
	{
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) return _modelNode;
		return GetNodeOrNull<Node3D>("VisualModel") ?? this;
	}

	private Node3D GetParentModelNode()
	{
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) return _modelNode;
		return this;
	}

	private bool TryClearPseudoSocketAttachment(string normSocket, string attachmentId, bool clearExisting, Vector3? posOffsetOverride, Vector3? rotOffsetOverride, float? scaleOverride, Vector3? scaleVectorOverride, float? normalOffsetOverride)
	{
		if (IsAttachmentIdEmptyOrNone(attachmentId))
		{
			ClearEmptyPseudoSocketAttachment(normSocket);
			return true;
		}

		return ShouldClearExistingPseudoSocket(normSocket, attachmentId, clearExisting, posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride);
	}

	private void ClearEmptyPseudoSocketAttachment(string normSocket)
	{
		_currentPseudoSocketAttachmentIds.Remove(normSocket);
		if (_pseudoSockets.TryGetValue(normSocket, out var existing) && GodotObject.IsInstanceValid(existing))
		{
			ClearNodeChildren(existing);
		}
	}

	private bool ShouldClearExistingPseudoSocket(string normSocket, string attachmentId, bool clearExisting, Vector3? posOffsetOverride, Vector3? rotOffsetOverride, float? scaleOverride, Vector3? scaleVectorOverride, float? normalOffsetOverride)
	{
		if (!clearExisting) return false;
		if (!_currentPseudoSocketAttachmentIds.TryGetValue(normSocket, out var currentId) || currentId != attachmentId) return false;
		if (!_pseudoSockets.TryGetValue(normSocket, out var currentAttachment)) return false;
		if (!GodotObject.IsInstanceValid(currentAttachment)) return false;
		if (currentAttachment.GetChildCount() <= 0) return false;
		if (HasAnyOverrides(posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride)) return false;

		return true;
	}

	private Node3D? EnsurePseudoSocketNode(Node3D visual, string normSocket)
	{
		if (PseudoSocketCache.IsRealSocket(normSocket, GetRealSocketIds())) return null;

		string nodeName = $"PseudoSocket_{normSocket}";
		var socketNode = visual.GetNodeOrNull<Node3D>(nodeName);
		if (socketNode == null)
		{
			socketNode = new Node3D { Name = nodeName };
			visual.AddChild(socketNode);
		}

		_pseudoSockets[normSocket] = socketNode;
		return socketNode;
	}

	private static Vector3 CalculateLegacyPseudoSocketAnchorPos(string normSocket, Aabb aabb, bool isNonRigged)
	{
		if (isNonRigged)
		{
			return normSocket switch
			{
				"center" or "centerofmass" => aabb.GetCenter(),
				"top" or "overhead" or "roof" => new Vector3(aabb.GetCenter().X, aabb.End.Y, aabb.GetCenter().Z),
				"base" or "ground" or "footprint" => new Vector3(aabb.GetCenter().X, aabb.Position.Y, aabb.GetCenter().Z),
				"pivot" or "origin" => Vector3.Zero,
				_ => Vector3.Zero
			};
		}

		return normSocket switch
		{
			"ground" or "footprint" or "base" => new Vector3(0, aabb.Position.Y, 0),
			"center" or "centerofmass" => new Vector3(0, aabb.GetCenter().Y, 0),
			"overhead" or "crown" or "top" or "roof" => new Vector3(0, aabb.End.Y + 0.3f, 0),
			"pivot" or "origin" => Vector3.Zero,
			_ => Vector3.Zero
		};
	}

	private void ApplyPseudoSocketInstance(Node3D model, string attachmentId, Node3D socketNode, Node3D visual, string? parentAttachmentId, string normSocket, float defScale, Vector3 defPos, Vector3 defRot, Vector3? posOffsetOverride, Vector3? rotOffsetOverride, float? scaleOverride, Vector3? scaleVectorOverride, float? normalOffsetOverride)
	{
		float effectiveScale = scaleOverride ?? defScale;
		Vector3 effectivePos = posOffsetOverride ?? defPos;
		Vector3 effectiveRot = rotOffsetOverride ?? defRot;
		Vector3 effectiveScaleVec = scaleVectorOverride ?? (Vector3.One * (effectiveScale <= 0f ? 1.0f : effectiveScale));
		float effectiveNormalOffset = normalOffsetOverride ?? 0.0f;

		ApplyPseudoSocketUnitOverrides(normSocket, attachmentId, ref effectivePos, ref effectiveRot, ref effectiveScaleVec, ref effectiveNormalOffset, scaleOverride.HasValue, scaleVectorOverride.HasValue, posOffsetOverride.HasValue, rotOffsetOverride.HasValue, normalOffsetOverride.HasValue);

		Node3D targetParent = ResolvePseudoSocketTargetParent(socketNode, visual, parentAttachmentId);
		ApplyEffectiveTransformAndMetadata(model, attachmentId, targetParent, effectivePos, effectiveRot, effectiveScaleVec, effectiveNormalOffset);
	}

	private void ApplyPseudoSocketUnitOverrides(string normSocket, string attachmentId, ref Vector3 effectivePos, ref Vector3 effectiveRot, ref Vector3 effectiveScaleVec, ref float effectiveNormalOffset, bool hasScaleOverride, bool hasScaleVecOverride, bool hasPosOverride, bool hasRotOverride, bool hasNormalOverride)
	{
		if (string.IsNullOrEmpty(UnitId)) return;
		if (!GameHost.TryGetUnitOrBuildingMetadata(UnitId, out var uMeta)) return;
		if (!uMeta.TryGetObjectAttachment(normSocket, attachmentId, out var unitOrient)) return;

		if (!hasScaleOverride && !hasScaleVecOverride) effectiveScaleVec = unitOrient.ScaleVector.ToGodotVector3();
		if (!hasPosOverride) effectivePos = unitOrient.Position.ToGodotVector3();
		if (!hasRotOverride) effectiveRot = unitOrient.RotationDegrees.ToGodotVector3();
		if (!hasNormalOverride) effectiveNormalOffset = unitOrient.NormalOffset;
	}

	private Node3D ResolvePseudoSocketTargetParent(Node3D socketNode, Node3D visual, string? parentAttachmentId)
	{
		if (string.IsNullOrEmpty(parentAttachmentId)) return socketNode;
		var parentMesh = FindAttachmentInNode(socketNode, parentAttachmentId) ?? FindAttachmentInNode(visual, parentAttachmentId);
		return parentMesh ?? socketNode;
	}


	public static Node3D? FindAttachmentInNode(Node root, string attachmentId)
	{
		if (root == null || !GodotObject.IsInstanceValid(root)) return null;
		if (string.IsNullOrEmpty(attachmentId)) return null;

		string cleanAttId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? attachmentId : Path.GetFileNameWithoutExtension(attachmentId);

		foreach (Node child in root.GetChildren())
		{
			if (!GodotObject.IsInstanceValid(child) || child.IsQueuedForDeletion()) continue;

			if (child is Node3D node3D)
			{
				Node3D? found = CheckAttachmentNode(node3D, attachmentId, cleanAttId);
				if (found != null) return found;
			}
		}

		return null;
	}

	private static Node3D? CheckAttachmentNode(Node3D node3D, string attachmentId, string cleanAttId)
	{
		if (node3D.HasMeta("AttachmentId") && string.Equals(node3D.GetMeta("AttachmentId").AsString(), attachmentId, StringComparison.OrdinalIgnoreCase)) return node3D;
		if (node3D.HasMeta("CleanAttachmentId") && string.Equals(node3D.GetMeta("CleanAttachmentId").AsString(), cleanAttId, StringComparison.OrdinalIgnoreCase)) return node3D;
		if (string.Equals(node3D.Name.ToString(), $"Att_{cleanAttId}", StringComparison.OrdinalIgnoreCase)) return node3D;

		return FindAttachmentInNode(node3D, attachmentId);
	}


	public static Node3D? ResolveAndInstantiateAttachment(string? attachmentId, out float defaultScale, out Vector3 defaultPos, out Vector3 defaultRot)
	{
		defaultScale = 1.0f;
		defaultPos = Vector3.Zero;
		defaultRot = Vector3.Zero;

		if (string.IsNullOrEmpty(attachmentId)) return null;

		if (TryResolveBillboardIconAttachment(attachmentId, ref defaultScale, ref defaultPos, ref defaultRot, out var billboardIcon)) return billboardIcon;

		var vfxInstance = ResolveVfxAttachment(attachmentId, ref defaultScale, ref defaultPos, ref defaultRot);
		if (vfxInstance != null) return vfxInstance;

		var loadedNode = ResolveModelAttachment(attachmentId, ref defaultScale, ref defaultPos, ref defaultRot, out var attMeta);
		if (loadedNode != null)
		{
			if (attMeta != null && !string.IsNullOrEmpty(attMeta.ChildVfxId))
			{
				ApplyChildVfx(loadedNode, attMeta);
			}

			return loadedNode;
		}

		return null;
	}

}
