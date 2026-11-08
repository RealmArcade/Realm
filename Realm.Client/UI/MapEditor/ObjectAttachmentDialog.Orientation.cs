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
	private void LoadAttachmentOrientationIntoSliders(string targetId, string socketId, string attachmentId, string? parentAttachmentId = null)
	{
		HandAttachmentOrientation? unitOrient = TryLoadConfiguredOrientation(targetId, socketId, attachmentId, parentAttachmentId);

		if (unitOrient != null)
		{
			ApplyConfiguredOrientation(unitOrient);
		}
		else if (!string.IsNullOrEmpty(attachmentId) && GameHost.AttachmentRegistry.TryGetValue(attachmentId, out var attMeta))
		{
			ApplyRegistryOrientation(attMeta);
		}
		else
		{
			ApplyDefaultOrientation();
		}

		UpdateSliderDisplayValues();
	}

	private HandAttachmentOrientation? TryLoadConfiguredOrientation(string targetId, string socketId, string attachmentId, string? parentAttachmentId)
	{
		if (string.IsNullOrEmpty(targetId)) return null;

		var configured = GetConfiguredAttachments();
		string normSocket = NormalizeSocketId(socketId);
		string cleanId = GetCleanAttachmentId(attachmentId);

		foreach (var entry in configured)
		{
			if (IsOrientationMatch(entry, normSocket, attachmentId, cleanId, parentAttachmentId))
			{
				return entry.Orientation;
			}
		}

		return null;
	}

	private bool IsOrientationMatch(ConfiguredAttachmentEntry entry, string normSocket, string attachmentId, string cleanId, string? parentAttachmentId)
	{
		if (!entry.SocketId.Equals(normSocket, StringComparison.OrdinalIgnoreCase)) return false;

		bool keyMatch = entry.AttachmentId.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
		                entry.AttachmentId.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
		                Path.GetFileNameWithoutExtension(entry.AttachmentId).Equals(cleanId, StringComparison.OrdinalIgnoreCase);

		if (!keyMatch) return false;

		return parentAttachmentId == null || string.Equals(entry.Orientation.ParentAttachmentId ?? string.Empty, parentAttachmentId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
	}

	private void ApplyConfiguredOrientation(HandAttachmentOrientation unitOrient)
	{
		_currentPosOffset = unitOrient.Position.ToGodotVector3();
		_currentRotOffset = unitOrient.RotationDegrees.ToGodotVector3();
		_currentScaleOffset = unitOrient.ScaleVector.ToGodotVector3();
		_currentNormalOffset = unitOrient.NormalOffset;
		_currentParentAttachmentId = unitOrient.ParentAttachmentId;
	}

	private void ApplyRegistryOrientation(AttachmentMetadata attMeta)
	{
		_currentPosOffset = attMeta.PositionOffset.ToGodotVector3();
		_currentRotOffset = attMeta.RotationOffset.ToGodotVector3();
		_currentScaleOffset = Vector3.One * (attMeta.Scale <= 0f ? 1.0f : attMeta.Scale);
		_currentNormalOffset = 0.0f;
	}

	private void ApplyDefaultOrientation()
	{
		_currentPosOffset = Vector3.Zero;
		_currentRotOffset = Vector3.Zero;
		_currentScaleOffset = Vector3.One;
		_currentNormalOffset = 0.0f;
	}

	private void UpdateSliderDisplayValues()
	{
		UpdateSlider(_sliderPosX, _lblPosX, _currentPosOffset.X, "F3");
		UpdateSlider(_sliderPosY, _lblPosY, _currentPosOffset.Y, "F3");
		UpdateSlider(_sliderPosZ, _lblPosZ, _currentPosOffset.Z, "F3");

		UpdateSlider(_sliderRotX, _lblRotX, _currentRotOffset.X, "F0");
		UpdateSlider(_sliderRotY, _lblRotY, _currentRotOffset.Y, "F0");
		UpdateSlider(_sliderRotZ, _lblRotZ, _currentRotOffset.Z, "F0");

		UpdateSlider(_sliderScaleX, _lblScaleX, _currentScaleOffset.X, "F2");
		UpdateSlider(_sliderScaleY, _lblScaleY, _currentScaleOffset.Y, "F2");
		UpdateSlider(_sliderScaleZ, _lblScaleZ, _currentScaleOffset.Z, "F2");

		UpdateSlider(_sliderNormalOffset, _lblNormalOffset, _currentNormalOffset, "F3");
	}

	private void UpdateSlider(HSlider? slider, Label? label, float value, string format)
	{
		if (slider != null) slider.Value = value;
		if (label != null) label.Text = value.ToString(format);
	}

	private void SyncWorkingAttachmentIfConfigured()
	{
		if (string.IsNullOrEmpty(_targetObjectId) || string.IsNullOrEmpty(_currentAttachmentId)) return;
		if (IsAttachmentConfigured(_currentSocketId, _currentAttachmentId, _currentParentAttachmentId))
		{
			var orientation = new HandAttachmentOrientation
			{
				PositionX = _currentPosOffset.X,
				PositionY = _currentPosOffset.Y,
				PositionZ = _currentPosOffset.Z,
				PitchX = _currentRotOffset.X,
				YawY = _currentRotOffset.Y,
				RollZ = _currentRotOffset.Z,
				Scale = _currentScaleOffset.X,
				ScaleX = _currentScaleOffset.X,
				ScaleY = _currentScaleOffset.Y,
				ScaleZ = _currentScaleOffset.Z,
				NormalOffset = _currentNormalOffset,
				ParentAttachmentId = _currentParentAttachmentId
			};
			WorkingAttachments.SetSocketOrientation(_currentSocketId, _currentAttachmentId, orientation);
		}
	}

	private void ResetTransformValues()
	{
		_currentPosOffset = Vector3.Zero;
		_currentRotOffset = Vector3.Zero;
		_currentScaleOffset = Vector3.One;
		_currentNormalOffset = 0.0f;
		UpdateSliderDisplayValues();
		UpdateActiveAttachmentTransform();
		SyncWorkingAttachmentIfConfigured();
	}

	private void AutoCalculateAttachmentOrientation()
	{
		if (!string.IsNullOrEmpty(_currentParentAttachmentId))
		{
			ResetTransformValues();
			return;
		}

		_socketAnchorNodes.TryGetValue(_currentSocketId, out var targetAnchor);
		if (targetAnchor == null || !GodotObject.IsInstanceValid(targetAnchor)) return;

		Node3D? targetNode = GetActiveTargetNode();
		if (targetNode == null) return;

		var currentDef = _currentAvailableSockets.FirstOrDefault(s => s.SocketId.Equals(_currentSocketId, StringComparison.OrdinalIgnoreCase));
		if (currentDef.IsPseudoSocket)
		{
			ApplyPseudoSocketOrientation();
			return;
		}

		CalculateAndApplyTransform(targetAnchor, targetNode, currentDef);
	}

	private Node3D? GetActiveTargetNode()
	{
		Node3D? targetNode = _currentActiveModelNode;
		if (targetNode == null || !GodotObject.IsInstanceValid(targetNode))
		{
			PreviewCurrentAttachment();
			targetNode = _currentActiveModelNode;
		}

		return (targetNode != null && GodotObject.IsInstanceValid(targetNode)) ? targetNode : null;
	}

	private void ApplyPseudoSocketOrientation()
	{
		string normalizedSocketId = PseudoSocketCache.NormalizeSocketId(_currentSocketId) ?? _currentSocketId;
		bool isGround = normalizedSocketId.Equals("Base", StringComparison.OrdinalIgnoreCase);

		_currentPosOffset = isGround ? new Vector3(0f, 0.02f, 0f) : Vector3.Zero;
		_currentRotOffset = Vector3.Zero;
		_currentNormalOffset = 0.0f;

		UpdateSliderDisplayValues();
		UpdateActiveAttachmentTransform();
		SyncWorkingAttachmentIfConfigured();
	}

	private void CalculateAndApplyTransform(Node3D targetAnchor, Node3D targetNode, SocketDefinition currentDef)
	{
		Transform3D savedTransform = targetNode.Transform;
		targetNode.Transform = Transform3D.Identity;

		Aabb localAabb = CalculateAttachmentLocalAabb(targetNode);
		targetNode.Transform = savedTransform;

		Vector3 effectiveScale = _currentScaleOffset == Vector3.Zero ? Vector3.One : _currentScaleOffset;
		int primaryAxis = GetPrimaryAxis(localAabb.Size, effectiveScale);
		Vector3 localGripPoint = CalculateGripPoint(localAabb, primaryAxis);

		Basis desiredLocalBasis = CalculateDesiredBasis(targetAnchor, currentDef, primaryAxis);
		ApplyCalculatedTransform(desiredLocalBasis, localGripPoint, effectiveScale);
	}

	private int GetPrimaryAxis(Vector3 size, Vector3 effectiveScale)
	{
		Vector3 scaledSize = new Vector3(size.X * Mathf.Abs(effectiveScale.X), size.Y * Mathf.Abs(effectiveScale.Y), size.Z * Mathf.Abs(effectiveScale.Z));
		if (scaledSize.X > scaledSize.Y && scaledSize.X > scaledSize.Z) return 0;
		if (scaledSize.Z > scaledSize.Y && scaledSize.Z > scaledSize.X) return 2;
		return 1;
	}

	private Vector3 CalculateGripPoint(Aabb localAabb, int primaryAxis)
	{
		Vector3 size = localAabb.Size;
		if (primaryAxis == 0)
		{
			float gripX = localAabb.Position.X < -0.05f && localAabb.End.X > 0.05f
				? localAabb.Position.X + size.X * 0.15f
				: localAabb.Position.X;
			return new Vector3(gripX, localAabb.GetCenter().Y, localAabb.GetCenter().Z);
		}

		if (primaryAxis == 2)
		{
			float gripZ = localAabb.Position.Z < -0.05f && localAabb.End.Z > 0.05f
				? localAabb.Position.Z + size.Z * 0.15f
				: localAabb.Position.Z;
			return new Vector3(localAabb.GetCenter().X, localAabb.GetCenter().Y, gripZ);
		}

		float gripY = localAabb.Position.Y < -0.05f && localAabb.End.Y > 0.05f
			? localAabb.Position.Y + size.Y * 0.15f
			: localAabb.Position.Y;
		return new Vector3(localAabb.GetCenter().X, gripY, localAabb.GetCenter().Z);
	}

	private Basis CalculateDesiredBasis(Node3D targetAnchor, SocketDefinition currentDef, int primaryAxis)
	{
		Vector3 handPos = targetAnchor.GlobalPosition;
		Vector3 camPos = PreviewCamera != null ? PreviewCamera.GlobalPosition : new Vector3(0f, 1.5f, 3f);
		Vector3 dirToCam = (camPos - handPos).Normalized();

		Vector3 wristNormal = CalculateWristNormal(targetAnchor, currentDef, handPos);

		Vector3 desiredTopDir = (dirToCam * 0.7f + Vector3.Up * 0.5f + wristNormal * 0.3f).Normalized();
		Vector3 desiredRight = desiredTopDir.Cross(Vector3.Up).Normalized();

		if (desiredRight.LengthSquared() < 0.001f)
		{
			Vector3 camRight = PreviewCamera != null ? PreviewCamera.GlobalTransform.Basis.X.Normalized() : Vector3.Right;
			desiredRight = desiredTopDir.Cross(camRight).Normalized();
		}

		if (currentDef.AssociatedBone == HumanoidBone.LeftHand)
		{
			desiredRight = -desiredRight;
		}

		Vector3 desiredNormal = desiredRight.Cross(desiredTopDir).Normalized();

		Basis desiredWorldBasis = primaryAxis == 0
			? new Basis(desiredTopDir, desiredNormal, desiredRight)
			: primaryAxis == 2
				? new Basis(desiredRight, desiredNormal, desiredTopDir)
				: new Basis(desiredRight, desiredTopDir, desiredNormal);

		Basis handBasis = targetAnchor.GlobalTransform.Basis.Orthonormalized();
		return Mathf.Abs(handBasis.Determinant()) < 0.0001f
			? desiredWorldBasis.Orthonormalized()
			: (handBasis.Inverse() * desiredWorldBasis).Orthonormalized();
	}

	private Vector3 CalculateWristNormal(Node3D targetAnchor, SocketDefinition currentDef, Vector3 handPos)
	{
		Vector3 wristNormal = targetAnchor.GlobalTransform.Basis.Y.Normalized();
		var skeleton = SkeletonValidator.FindSkeleton(_previewModel);
		if (skeleton != null && currentDef.AssociatedBone.HasValue)
		{
			int lowerArmIdx = skeleton.FindBoneInSkeleton(
				currentDef.AssociatedBone.Value == HumanoidBone.RightHand ? HumanoidBone.RightLowerArm : HumanoidBone.LeftLowerArm);
			if (lowerArmIdx >= 0)
			{
				Vector3 lowerArmPos = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(lowerArmIdx).Origin;
				wristNormal = (handPos - lowerArmPos).Normalized();
			}
		}

		return wristNormal;
	}

	private void ApplyCalculatedTransform(Basis desiredLocalBasis, Vector3 localGripPoint, Vector3 effectiveScale)
	{
		Vector3 rotEulerRad = desiredLocalBasis.GetEuler();
		_currentRotOffset = new Vector3(
			NormalizeAngle(Mathf.RadToDeg(rotEulerRad.X)),
			NormalizeAngle(Mathf.RadToDeg(rotEulerRad.Y)),
			NormalizeAngle(Mathf.RadToDeg(rotEulerRad.Z))
		);

		Vector3 scaledGripPoint = new Vector3(
			localGripPoint.X * effectiveScale.X,
			localGripPoint.Y * effectiveScale.Y,
			localGripPoint.Z * effectiveScale.Z
		);

		Vector3 localOffset = -(desiredLocalBasis * scaledGripPoint);
		_currentPosOffset = new Vector3(
			Mathf.Clamp(localOffset.X, -1.5f, 1.5f),
			Mathf.Clamp(localOffset.Y, -1.5f, 1.5f),
			Mathf.Clamp(localOffset.Z, -1.5f, 1.5f)
		);

		UpdateSliderDisplayValues();
		UpdateActiveAttachmentTransform();
		SyncWorkingAttachmentIfConfigured();
	}

	private static Aabb CalculateAttachmentLocalAabb(Node3D root)
	{
		Aabb combinedAabb = new Aabb();
		bool hasAabb = false;
		if (root == null)
		{
			return combinedAabb;
		}

		void Collect(Node current)
		{
			if (current is MeshInstance3D meshInst && meshInst.Mesh != null)
			{
				ExpandAabbWithMesh(meshInst, root, ref combinedAabb, ref hasAabb);
			}

			foreach (Node child in current.GetChildren())
			{
				Collect(child);
			}
		}

		Collect(root);
		if (!hasAabb)
		{
			combinedAabb = new Aabb(new Vector3(-0.1f, -0.5f, -0.1f), new Vector3(0.2f, 1.0f, 0.2f));
		}

		return combinedAabb;
	}

	private static void ExpandAabbWithMesh(MeshInstance3D meshInst, Node3D root, ref Aabb combinedAabb, ref bool hasAabb)
	{
		Transform3D relXform = Transform3D.Identity;
		Node? curr = meshInst;
		while (curr != null && curr != root)
		{
			if (curr is Node3D n3d)
			{
				relXform = n3d.Transform * relXform;
			}

			curr = curr.GetParent();
		}

		if (Mathf.Abs(relXform.Basis.Determinant()) <= 0.0001f) return;

		Realm.Client.Utils.MathUtils.UpdateAabbWithMeshCorners(ref combinedAabb, ref hasAabb, meshInst, relXform);
	}

	private static float NormalizeAngle(float angle)
	{
		while (angle > 180f) angle -= 360f;
		while (angle < -180f) angle += 360f;
		return angle;
	}
}
