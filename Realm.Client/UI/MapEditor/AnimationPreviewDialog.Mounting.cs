// Preview instancing and socket mounting: seat the model in the preview viewport, create an anchor per
// configured socket (a pseudo anchor at a fixed offset when the skeleton has no matching bone), and hang
// each configured attachment off the anchor it names. Clearing runs the same walk in reverse.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;


namespace Realm.Client.UI.MapEditor;

public partial class AnimationPreviewDialog
{
	private void ClearPreviewModel()
	{
		ClearAttachmentVisuals();
		_socketAnchorNodes.Clear();
		if (_previewModelRoot != null && GodotObject.IsInstanceValid(_previewModelRoot))
		{
			if (_animPlayer != null && GodotObject.IsInstanceValid(_animPlayer))
			{
				_animPlayer.Stop(true);
			}

			_previewModelRoot.GetParent()?.RemoveChild(_previewModelRoot);
			_previewModelRoot.QueueFree();
			_previewModelRoot = null;
			_animPlayer = null;
		}
	}

	private void ClearAttachmentVisuals()
	{
		foreach (var kvp in _attachmentVisualNodes)
		{
			if (kvp.Value != null && GodotObject.IsInstanceValid(kvp.Value))
			{
				kvp.Value.GetParent()?.RemoveChild(kvp.Value);
				kvp.Value.QueueFree();
			}
		}

		_attachmentVisualNodes.Clear();
	}

	private void SetupPreviewModel(Node sourceModelRoot)
	{
		if (sourceModelRoot == null || PreviewSubViewport == null) return;

		var clonedNode = (Node3D)sourceModelRoot.Duplicate((int)DuplicateFlags.UseInstantiation);
		if (clonedNode == null) return;

		RemoveAllBoneAttachments(clonedNode);

		clonedNode.Position = Vector3.Zero;
		clonedNode.Rotation = Vector3.Zero;
		clonedNode.Scale = Vector3.One;

		PreviewSubViewport.AddChild(clonedNode);
		_previewModelRoot = clonedNode;

		if (_previewModelRoot.IsInsideTree())
		{
			_previewModelRoot.PropagateNotification((int)Node3D.NotificationTransformChanged);
		}

		FrameCameraOnNode(_previewModelRoot);

		SetupSocketAnchors();
		MountAllConfiguredAttachments();

		_animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_previewModelRoot);
	}

	private void SetupSocketAnchors()
	{
		_socketAnchorNodes.Clear();
		if (_previewModelRoot == null || !GodotObject.IsInstanceValid(_previewModelRoot)) return;

		Aabb modelAabb = CalculatePreviewModelAabb(_previewModelRoot);
		var skeleton = SkeletonValidator.FindSkeleton(_previewModelRoot);

		if (skeleton != null)
		{
			AddPseudoAnchor("Ground", new Vector3(0, modelAabb.Position.Y, 0));
			AddPseudoAnchor("Center", new Vector3(0, modelAabb.GetCenter().Y, 0));
			AddPseudoAnchor("Overhead", new Vector3(0, modelAabb.End.Y + 0.3f, 0));
			AddPseudoAnchor("Pivot", Vector3.Zero);

			AddBoneAnchor(skeleton, HumanoidBone.RightHand, "RightHand");
			AddBoneAnchor(skeleton, HumanoidBone.LeftHand, "LeftHand");
			AddBoneAnchor(skeleton, HumanoidBone.Chest, "Chest");
			AddBoneAnchor(skeleton, HumanoidBone.Hips, "Hips");
			AddBoneAnchor(skeleton, HumanoidBone.Head, "Head");
			AddBoneAnchor(skeleton, HumanoidBone.LeftFoot, "LeftFoot");
			AddBoneAnchor(skeleton, HumanoidBone.RightFoot, "RightFoot");
		}
		else
		{
			AddPseudoAnchor("Center", modelAabb.GetCenter());
			AddPseudoAnchor("Top", new Vector3(modelAabb.GetCenter().X, modelAabb.End.Y, modelAabb.GetCenter().Z));
			AddPseudoAnchor("Base", new Vector3(modelAabb.GetCenter().X, modelAabb.Position.Y, modelAabb.GetCenter().Z));
			AddPseudoAnchor("Pivot", Vector3.Zero);
		}
	}

	private void AddPseudoAnchor(string socketId, Vector3 pos)
	{
		string normSocket = ObjectAttachmentDialog.NormalizeSocketId(socketId);
		if (_previewModelRoot == null || !GodotObject.IsInstanceValid(_previewModelRoot)) return;

		var anchor = new Node3D { Name = $"PreviewSocketAnchor_{normSocket}" };
		anchor.Position = pos;
		_previewModelRoot.AddChild(anchor);
		_socketAnchorNodes[normSocket] = anchor;
	}

	private void AddBoneAnchor(Skeleton3D skeleton, HumanoidBone bone, string socketId)
	{
		string normSocket = ObjectAttachmentDialog.NormalizeSocketId(socketId);
		int boneIdx = skeleton.FindBoneInSkeleton(bone);
		if (boneIdx >= 0)
		{
			var ba = new BoneAttachment3D
			{
				Name = $"PreviewBoneAttachment_{normSocket}",
				BoneName = skeleton.GetBoneName(boneIdx),
				BoneIdx = boneIdx
			};
			skeleton.AddChild(ba);
			_socketAnchorNodes[normSocket] = ba;
		}
	}

	private void MountAllConfiguredAttachments()
	{
		ClearAttachmentVisuals();
		if (_previewModelRoot == null || !GodotObject.IsInstanceValid(_previewModelRoot)) return;
		if (string.IsNullOrEmpty(_currentUnitId)) return;

		var configured = ObjectAttachmentDialog.GetConfiguredAttachmentsForObject(_currentUnitId);

		foreach (var entry in configured)
		{
			if (string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId))
			{
				MountAttachmentVisual(entry);
			}
		}

		foreach (var entry in configured)
		{
			if (!string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId))
			{
				MountAttachmentVisual(entry);
			}
		}
	}

	private Node3D ResolveAttachmentTarget(Node3D targetAnchor, string? parentAttachmentId)
	{
		if (string.IsNullOrEmpty(parentAttachmentId))
			return targetAnchor;

		var parentMesh = Unit3D.FindAttachmentInNode(targetAnchor, parentAttachmentId)
		                 ?? (_previewModelRoot != null ? Unit3D.FindAttachmentInNode(_previewModelRoot, parentAttachmentId) : null);

		return parentMesh ?? targetAnchor;
	}

	private void MountAttachmentVisual(ConfiguredAttachmentEntry entry)
	{
		string normSocket = ObjectAttachmentDialog.NormalizeSocketId(entry.SocketId);
		if (IsInvalidSocketAnchor(normSocket, out var targetAnchor)) return;

		if (IsInvalidAttachmentId(entry.AttachmentId)) return;

		Node3D? loaded = Unit3D.ResolveAndInstantiateAttachment(entry.AttachmentId, out _, out _, out _);
		if (loaded == null) return;

		string key = ObjectAttachmentDialog.GetAttachmentKey(normSocket, entry.AttachmentId, entry.Index, entry.Orientation.ParentAttachmentId);

		SetupAttachmentMetadata(loaded, entry.AttachmentId, key);
		ApplyAttachmentTransform(loaded, entry.Orientation);
		ApplyAttachmentVisibility(loaded, key);

		Node3D attachTarget = ResolveAttachmentTarget(targetAnchor, entry.Orientation.ParentAttachmentId);
		attachTarget.AddChild(loaded);
		_attachmentVisualNodes[key] = loaded;
	}

	private bool IsInvalidSocketAnchor(string normSocket, [NotNullWhen(false)] out Node3D? targetAnchor)
	{
		if (!_socketAnchorNodes.TryGetValue(normSocket, out targetAnchor)) return true;
		if (targetAnchor == null) return true;
		if (!GodotObject.IsInstanceValid(targetAnchor)) return true;
		return false;
	}

	private bool IsInvalidAttachmentId(string attachmentId)
	{
		if (string.IsNullOrEmpty(attachmentId)) return true;
		if (attachmentId.Equals("null", StringComparison.OrdinalIgnoreCase)) return true;
		if (attachmentId.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	private void SetupAttachmentMetadata(Node3D loaded, string attachmentId, string key)
	{
		string cleanAttId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: Path.GetFileNameWithoutExtension(attachmentId);

		loaded.Name = $"AttVisual_{key}";
		loaded.SetMeta("AttachmentId", attachmentId);
		loaded.SetMeta("CleanAttachmentId", cleanAttId);
	}

	private void ApplyAttachmentTransform(Node3D loaded, HandAttachmentOrientation orientation)
	{
		loaded.Position = orientation.Position.ToGodotVector3() + (loaded.Transform.Basis.Y * orientation.NormalOffset);
		loaded.RotationDegrees = orientation.RotationDegrees.ToGodotVector3();

		var scaleVec = orientation.ScaleVector.ToGodotVector3();
		if (scaleVec == Vector3.Zero)
		{
			float scale = orientation.Scale <= 0f ? 1.0f : orientation.Scale;
			loaded.Scale = Vector3.One * scale;
		}
		else
		{
			loaded.Scale = scaleVec;
		}
	}

	private void ApplyAttachmentVisibility(Node3D loaded, string key)
	{
		if (!_attachmentVisibilities.TryGetValue(key, out bool vis))
		{
			loaded.Visible = true;
		}
		else
		{
			loaded.Visible = vis;
		}
	}



	private static Aabb CalculatePreviewModelAabb(Node3D root)
	{
		Aabb combinedAabb = new Aabb();
		bool hasAabb = false;

		void Collect(Node current)
		{
			if (current is MeshInstance3D meshInst && meshInst.Mesh != null && meshInst.Visible)
			{
				Transform3D relXform = root.GlobalTransform.AffineInverse() * meshInst.GlobalTransform;
				Realm.Client.Utils.MathUtils.UpdateAabbWithMeshCorners(ref combinedAabb, ref hasAabb, meshInst, relXform);
			}

			foreach (Node child in current.GetChildren())
			{
				if (child is not BoneAttachment3D && !child.Name.ToString().StartsWith("PreviewSocketAnchor_") && !child.Name.ToString().StartsWith("AttVisual_"))
				{
					Collect(child);
				}
			}
		}

		Collect(root);
		if (!hasAabb)
		{
			combinedAabb = new Aabb(new Vector3(-0.5f, 0f, -0.5f), new Vector3(1.0f, 1.8f, 1.0f));
		}

		return combinedAabb;
	}

	private static void RemoveAllBoneAttachments(Node node)
	{
		if (node == null) return;
		var boneAttachments = new List<BoneAttachment3D>();
		CollectBoneAttachmentsRecursive(node, boneAttachments);
		foreach (var ba in boneAttachments)
		{
			ba.GetParent()?.RemoveChild(ba);
			ba.QueueFree();
		}
	}

	private static void CollectBoneAttachmentsRecursive(Node node, List<BoneAttachment3D> list)
	{
		if (node is BoneAttachment3D ba)
		{
			list.Add(ba);
			return;
		}

		foreach (Node child in node.GetChildren())
		{
			CollectBoneAttachmentsRecursive(child, list);
		}
	}

}