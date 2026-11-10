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
		int boneIdx = skeleton != null ? HumanoidBoneExtensions.FindBoneInSkeleton(skeleton, bone) : -1;

		if (TryClearSocketAttachment(bone, attachmentId, clearExisting, posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride))
		{
			return;
		}

		if (bone == HumanoidBone.RightHand) _currentRightAttachmentId = attachmentId;
		else if (bone == HumanoidBone.LeftHand) _currentLeftAttachmentId = attachmentId;
		_currentBoneAttachmentIds[bone] = attachmentId;

		Node3D attachTarget = EnsureAttachTarget(parentNode, skeleton, bone, boneIdx);

		if (clearExisting)
		{
			ClearNodeChildren(attachTarget);
		}

		Node3D? model = ResolveAndInstantiateAttachment(attachmentId, out float defScale, out Vector3 defPos, out Vector3 defRot);
		if (model != null)
		{
			ApplySocketAttachmentInstance(model, attachmentId, attachTarget, parentNode, parentAttachmentId, bone, defScale, defPos, defRot, posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride);
		}
	}

	private static bool IsAttachmentIdEmptyOrNone(string? attachmentId)
	{
		if (string.IsNullOrEmpty(attachmentId)) return true;
		if (attachmentId.Equals("null", StringComparison.OrdinalIgnoreCase)) return true;
		if (attachmentId.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	private bool TryClearSocketAttachment(HumanoidBone bone, [NotNullWhen(false)] string? attachmentId, bool clearExisting, Vector3? posOffsetOverride, Vector3? rotOffsetOverride, float? scaleOverride, Vector3? scaleVectorOverride, float? normalOffsetOverride)
	{
		if (IsAttachmentIdEmptyOrNone(attachmentId))
		{
			ClearEmptyAttachmentId(bone);
			return true;
		}

		return ShouldClearExistingAttachment(bone, attachmentId, clearExisting, posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride);
	}

	private void ClearEmptyAttachmentId(HumanoidBone bone)
	{
		if (bone == HumanoidBone.RightHand) _currentRightAttachmentId = null;
		else if (bone == HumanoidBone.LeftHand) _currentLeftAttachmentId = null;
		_currentBoneAttachmentIds.Remove(bone);

		if (_boneAttachments.TryGetValue(bone, out var existing) && GodotObject.IsInstanceValid(existing))
		{
			ClearNodeChildren(existing);
		}
	}

	private static bool HasAnyOverrides(Vector3? pos, Vector3? rot, float? scale, Vector3? scaleVec, float? norm)
	{
		return pos.HasValue || rot.HasValue || scale.HasValue || scaleVec.HasValue || norm.HasValue;
	}

	private bool ShouldClearExistingAttachment(HumanoidBone bone, string? attachmentId, bool clearExisting, Vector3? posOffsetOverride, Vector3? rotOffsetOverride, float? scaleOverride, Vector3? scaleVectorOverride, float? normalOffsetOverride)
	{
		if (!clearExisting) return false;
		if (!_currentBoneAttachmentIds.TryGetValue(bone, out var currentId) || currentId != attachmentId) return false;
		if (!_boneAttachments.TryGetValue(bone, out var currentAttachment)) return false;
		if (!GodotObject.IsInstanceValid(currentAttachment)) return false;
		if (currentAttachment.GetChildCount() <= 0) return false;
		if (HasAnyOverrides(posOffsetOverride, rotOffsetOverride, scaleOverride, scaleVectorOverride, normalOffsetOverride)) return false;

		return true;
	}

	private Node3D EnsureAttachTarget(Node3D parentNode, Skeleton3D? skeleton, HumanoidBone bone, int boneIdx)
	{
		if (skeleton != null && boneIdx >= 0)
		{
			string boneName = skeleton.GetBoneName(boneIdx);
			string nodeName = $"BoneAttachment_{bone}";
			var boneAttachment = skeleton.GetNodeOrNull<BoneAttachment3D>(nodeName);
			if (boneAttachment == null)
			{
				boneAttachment = new BoneAttachment3D { Name = nodeName, BoneName = boneName, BoneIdx = boneIdx };
				skeleton.AddChild(boneAttachment);
			}

			_boneAttachments[bone] = boneAttachment;
			if (bone == HumanoidBone.RightHand) _rightHandAttachment = boneAttachment;
			else if (bone == HumanoidBone.LeftHand) _leftHandAttachment = boneAttachment;
			return boneAttachment;
		}

		string socketNodeName = $"SocketAttachment_{bone}";
		var socketNode = parentNode.GetNodeOrNull<Node3D>(socketNodeName);
		if (socketNode == null)
		{
			socketNode = new Node3D { Name = socketNodeName };
			parentNode.AddChild(socketNode);
		}

		_boneAttachments[bone] = socketNode;
		return socketNode;
	}

	private void ClearNodeChildren(Node parentNode)
	{
		foreach (Node child in parentNode.GetChildren())
		{
			parentNode.RemoveChild(child);
			child.QueueFree();
		}
	}

	private void ApplyEffectiveTransformAndMetadata(Node3D model, string attachmentId, Node3D targetParent, Vector3 effectivePos, Vector3 effectiveRot, Vector3 effectiveScaleVec, float effectiveNormalOffset)
	{
		if (effectiveNormalOffset != 0.0f) effectivePos += Vector3.Up * effectiveNormalOffset;

		if (model is BillboardIconAttachment3D billboardIcon)
		{
			effectivePos += billboardIcon.ResolvePositionOffset();
			effectiveScaleVec = Vector3.One;
		}

		model.Position = effectivePos;
		model.RotationDegrees = effectiveRot;
		model.Scale = effectiveScaleVec;

		string cleanAttId = GetCleanAttachmentId(attachmentId);
		model.Name = $"Att_{cleanAttId}";
		model.SetMeta("AttachmentId", attachmentId);
		model.SetMeta("CleanAttachmentId", cleanAttId);

		targetParent.AddChild(model);

		ApplyModelShaders(model, attachmentId);
	}

	private void ApplySocketAttachmentInstance(Node3D model, string attachmentId, Node3D attachTarget, Node3D parentNode, string? parentAttachmentId, HumanoidBone bone, float defScale, Vector3 defPos, Vector3 defRot, Vector3? posOffsetOverride, Vector3? rotOffsetOverride, float? scaleOverride, Vector3? scaleVectorOverride, float? normalOffsetOverride)
	{
		float effectiveScale = scaleOverride ?? defScale;
		Vector3 effectivePos = posOffsetOverride ?? defPos;
		Vector3 effectiveRot = rotOffsetOverride ?? defRot;
		Vector3 effectiveScaleVec = scaleVectorOverride ?? (Vector3.One * (effectiveScale <= 0f ? 1.0f : effectiveScale));
		float effectiveNormalOffset = normalOffsetOverride ?? 0.0f;

		ApplyUnitAttachmentOverrides(bone, attachmentId, ref effectivePos, ref effectiveRot, ref effectiveScaleVec, ref effectiveNormalOffset, scaleOverride.HasValue, scaleVectorOverride.HasValue, posOffsetOverride.HasValue, rotOffsetOverride.HasValue, normalOffsetOverride.HasValue);

		Node3D targetParent = ResolveSocketTargetParent(attachTarget, parentNode, parentAttachmentId);
		ApplyEffectiveTransformAndMetadata(model, attachmentId, targetParent, effectivePos, effectiveRot, effectiveScaleVec, effectiveNormalOffset);
	}

	private void ApplyUnitAttachmentOverrides(HumanoidBone bone, string attachmentId, ref Vector3 effectivePos, ref Vector3 effectiveRot, ref Vector3 effectiveScaleVec, ref float effectiveNormalOffset, bool hasScaleOverride, bool hasScaleVecOverride, bool hasPosOverride, bool hasRotOverride, bool hasNormalOverride)
	{
		if (string.IsNullOrEmpty(UnitId)) return;
		if (!GameHost.TryGetUnitOrBuildingMetadata(UnitId, out var uMeta)) return;
		if (!uMeta.TryGetObjectAttachment(bone, attachmentId, out var unitOrient)) return;

		if (!hasScaleOverride && !hasScaleVecOverride) effectiveScaleVec = unitOrient.ScaleVector.ToGodotVector3();
		if (!hasPosOverride) effectivePos = unitOrient.Position.ToGodotVector3();
		if (!hasRotOverride) effectiveRot = unitOrient.RotationDegrees.ToGodotVector3();
		if (!hasNormalOverride) effectiveNormalOffset = unitOrient.NormalOffset;
	}

	private static string GetCleanAttachmentId(string attachmentId)
	{
		if (attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)) return attachmentId;
		return Path.GetFileNameWithoutExtension(attachmentId);
	}

	private Node3D ResolveSocketTargetParent(Node3D attachTarget, Node3D parentNode, string? parentAttachmentId)
	{
		if (string.IsNullOrEmpty(parentAttachmentId)) return attachTarget;
		var parentMesh = FindAttachmentInNode(attachTarget, parentAttachmentId) ?? FindAttachmentInNode(parentNode, parentAttachmentId);
		return parentMesh ?? attachTarget;
	}

}
