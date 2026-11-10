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
	private void ApplyModelShaders(Node3D model, string attachmentId)
	{
		if (model is ProceduralVfxInstance3D) return;
		if (model is BillboardIconAttachment3D) return;
		bool ignorePlayerColor = GameContext.Host != null && GameContext.Host.GetModelIgnorePlayerColor(attachmentId);
		ModelShaderManager.ApplyPlayerColorShader(model, PlayerColor, ignorePlayerColor, true, false);
		ModelShaderManager.SetIgnorePlayerColor(model, ignorePlayerColor);
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
		_pseudoSocketGlows.Clear();
		RebuildGeometryInstanceCache();
	}

	public virtual void ApplyAllConfiguredAttachments()
	{
		ClearAllAttachments();

		if (string.IsNullOrEmpty(UnitId) || !GameHost.TryGetUnitOrBuildingMetadata(UnitId, out var uMeta) || uMeta.ObjectAttachments == null)
		{
			return;
		}

		var atts = uMeta.ObjectAttachments;
		var parentNode = (_modelNode != null && GodotObject.IsInstanceValid(_modelNode)) ? _modelNode : (Node3D)this;
		var skeleton = FindSkeleton(parentNode);
		bool isNonRigged = IsBuilding || skeleton == null;

		if (isNonRigged)
		{
			ApplyNonRiggedAttachments(atts);
		}
		else
		{
			ApplyRiggedAttachments(atts);
		}
	}

	private void ApplyNonRiggedAttachments(UnitObjectAttachments atts)
	{
		ApplyPseudoSocketAttachmentList("ground", atts.ground ?? atts.root);
		ApplyPseudoSocketAttachmentList("center", atts.center ?? atts.chest);
		ApplyPseudoSocketAttachmentList("overhead", atts.overhead ?? atts.head);
		ApplyPseudoSocketAttachmentList("pivot", atts.pivot ?? atts.right_hand);
		ApplyCustomPseudoSocketAttachmentLists(atts);
	}

	private void ApplyCustomPseudoSocketAttachmentLists(UnitObjectAttachments atts)
	{
		var customLists = atts.CustomSocketLists;
		if (customLists == null) return;

		foreach (var kvp in customLists)
		{
			ApplyPseudoSocketAttachmentList(kvp.Key, kvp.Value);
		}
	}

	private void ApplyRiggedAttachments(UnitObjectAttachments atts)
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
		ApplyCustomPseudoSocketAttachmentLists(atts);
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

}
