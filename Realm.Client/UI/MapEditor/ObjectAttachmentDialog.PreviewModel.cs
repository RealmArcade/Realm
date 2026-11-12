using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

public partial class ObjectAttachmentDialog
{
	private void ClearUncommittedPreview()
	{
		if (_uncommittedPreviewNode != null && GodotObject.IsInstanceValid(_uncommittedPreviewNode))
		{
			if (!string.IsNullOrEmpty(_uncommittedPreviewKey))
			{
				_activeAttachmentVisuals.Remove(_uncommittedPreviewKey);
				if (_socketModelNodes.TryGetValue(_currentSocketId, out var smn) && smn == _uncommittedPreviewNode)
				{
					_socketModelNodes.Remove(_currentSocketId);
				}
			}

			if (_currentActiveModelNode == _uncommittedPreviewNode)
			{
				_currentActiveModelNode = null;
			}

			_uncommittedPreviewNode.GetParent()?.RemoveChild(_uncommittedPreviewNode);
			_uncommittedPreviewNode.QueueFree();
		}

		_uncommittedPreviewNode = null;
		_uncommittedPreviewKey = null;
	}

	private void ClearPreviewModel()
	{
		ClearUncommittedPreview();
		if (_previewModel != null && GodotObject.IsInstanceValid(_previewModel))
		{
			_previewModel.GetParent()?.RemoveChild(_previewModel);
			_previewModel.QueueFree();
			_previewModel = null;
		}

		foreach (var kvp in _activeAttachmentVisuals)
		{
			if (kvp.Value != null && GodotObject.IsInstanceValid(kvp.Value))
			{
				kvp.Value.GetParent()?.RemoveChild(kvp.Value);
				kvp.Value.QueueFree();
			}
		}
		_activeAttachmentVisuals.Clear();

		_socketAnchorNodes.Clear();
		_socketModelNodes.Clear();
		_currentActiveModelNode = null;
		_uncommittedPreviewNode = null;
		_uncommittedPreviewKey = null;
	}

	private void LoadTargetModelAndRig(string? targetId, Node3D? sourceModel)
	{
		ClearPreviewModel();
		if (string.IsNullOrEmpty(targetId)) return;

		Node3D? modelToInstantiate = GetModelToInstantiate(targetId, sourceModel);
		if (modelToInstantiate == null) return;

		RemoveAllExistingAttachments(modelToInstantiate);

		_previewModel = modelToInstantiate;
		_previewModel.Position = Vector3.Zero;
		_previewModel.Rotation = Vector3.Zero;
		_previewModel.Scale = Vector3.One;
		PreviewSceneRoot.AddChild(_previewModel);

		SetupSockets(sourceModel);
		FinalizeModelLoading();
	}

	private Node3D? GetModelToInstantiate(string? targetId, Node3D? sourceModel)
	{
		if (sourceModel != null && GodotObject.IsInstanceValid(sourceModel))
		{
			return (Node3D)sourceModel.Duplicate((int)DuplicateFlags.UseInstantiation);
		}

		if (string.IsNullOrEmpty(targetId)) return null;

		string modelPath = GetModelPath(targetId);
		var cached = ModelCache.GetModel(modelPath);
		if (cached is Node3D n3d)
		{
			return n3d;
		}

		if (cached != null)
		{
			cached.QueueFree();
		}

		return null;
	}

	private string GetModelPath(string targetId)
	{
		if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.TryGetValue(targetId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath))
			return meta.ModelPath;

		if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.TryGetValue(targetId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
			return bldMeta.ModelPath;

		if (GameHost.PropRegistry != null && (GameHost.PropRegistry.TryGetValue(targetId, out var pMeta) || GameHost.PropRegistry.TryGetValue(Path.GetFileNameWithoutExtension(targetId), out pMeta))
			&& !string.IsNullOrEmpty(pMeta.ModelPath))
			return pMeta.ModelPath;

		return targetId;
	}

	private void SetupSockets(Node3D? sourceModel)
	{
		var skeleton = SkeletonValidator.FindSkeleton(_previewModel);
		bool isRigged = skeleton != null && !_isTargetBuilding;
		_currentAvailableSockets.Clear();

		if (skeleton != null && isRigged)
		{
			CopySkeletonPoses(skeleton, sourceModel);
			AddBoneSockets(skeleton);
		}

		AddPseudoSockets(isRigged ? PseudoSocketCache.HumanoidSocketIds : null);
	}

	private void AddBoneSockets(Skeleton3D skeleton)
	{
		foreach (var socketDefinition in BoneSockets)
		{
			_currentAvailableSockets.Add(socketDefinition);
			CreateBoneAttachmentAnchor(socketDefinition, skeleton);
		}
	}

	private void AddPseudoSockets(IReadOnlyList<string>? realSocketIds)
	{
		var pseudoSocketPoints = PseudoSocketCache.GetSockets(_previewModel, GetModelPath(_targetObjectId), _workingPseudoSockets, realSocketIds);
		foreach (var pseudoSocketPoint in pseudoSocketPoints)
		{
			var socketDefinition = new SocketDefinition(pseudoSocketPoint.SocketId, pseudoSocketPoint.DisplayName, true);
			_currentAvailableSockets.Add(socketDefinition);
			CreatePseudoSocketAnchor(socketDefinition, pseudoSocketPoint.LocalPosition, pseudoSocketPoint.LocalRotationDegrees);
		}
	}

	private void CopySkeletonPoses(Skeleton3D skeleton, Node3D? sourceModel)
	{
		if (sourceModel == null || !GodotObject.IsInstanceValid(sourceModel)) return;

		var srcSkeleton = SkeletonValidator.FindSkeleton(sourceModel);
		if (srcSkeleton == null) return;

		int count = Math.Min(srcSkeleton.GetBoneCount(), skeleton.GetBoneCount());
		for (int i = 0; i < count; i++)
		{
			skeleton.SetBonePosePosition(i, srcSkeleton.GetBonePosePosition(i));
			skeleton.SetBonePoseRotation(i, srcSkeleton.GetBonePoseRotation(i));
			skeleton.SetBonePoseScale(i, srcSkeleton.GetBonePoseScale(i));
		}
	}

	private void CreatePseudoSocketAnchor(SocketDefinition sock, Vector3 localPosition, Vector3 localRotationDegrees)
	{
		if (_previewModel == null) return;

		var anchor = new Node3D { Name = $"Anchor_{sock.SocketId}" };
		anchor.Position = localPosition;
		anchor.RotationDegrees = localRotationDegrees;
		_previewModel.AddChild(anchor);
		_socketAnchorNodes[sock.SocketId] = anchor;
	}

	private void CreateBoneAttachmentAnchor(SocketDefinition sock, Skeleton3D skeleton)
	{
		int boneIdx = skeleton.FindBoneInSkeleton(sock.AssociatedBone!.Value);
		if (boneIdx >= 0)
		{
			var ba = new BoneAttachment3D
			{
				Name = $"BoneAttachment_{sock.AssociatedBone.Value}",
				BoneName = skeleton.GetBoneName(boneIdx),
				BoneIdx = boneIdx
			};
			skeleton.AddChild(ba);
			_socketAnchorNodes[sock.SocketId] = ba;
		}
	}

	private void FinalizeModelLoading()
	{
		UpdateSocketDropdown();
		AttachAllConfiguredAttachments();

		if (!string.IsNullOrEmpty(_currentAttachmentId))
		{
			PreviewCurrentAttachment();
			return;
		}

		var configured = GetConfiguredAttachments();
		if (configured.Count > 0)
		{
			SelectConfiguredAttachment(configured[0].SocketId, configured[0].AttachmentId, configured[0].Index);
		}
		else if (_availableAttachments.Count > 0)
		{
			_currentAttachmentId = _availableAttachments[0];
			PreviewCurrentAttachment();
		}
		else
		{
			_currentActiveModelNode = null;
		}
	}

	private static void RemoveAllExistingAttachments(Node node)
	{
		if (node == null) return;
		var toRemove = new List<Node>();

		void Collect(Node current)
		{
			if (current is BoneAttachment3D ||
			    current.Name.ToString().StartsWith("SocketAttachment_", StringComparison.OrdinalIgnoreCase) ||
			    current.Name.ToString().StartsWith("PseudoSocket_", StringComparison.OrdinalIgnoreCase))
			{
				toRemove.Add(current);
				return;
			}

			foreach (Node child in current.GetChildren())
			{
				Collect(child);
			}
		}

		Collect(node);
		foreach (var n in toRemove)
		{
			n.GetParent()?.RemoveChild(n);
			n.QueueFree();
		}
	}
}
