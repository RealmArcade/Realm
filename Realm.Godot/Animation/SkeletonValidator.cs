using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Realm.Godot.Animation;

public class SkeletonValidationResult
{
	public bool IsValid { get; set; }
	public Skeleton3D Skeleton { get; set; }
	public List<string> MissingRequiredBones { get; set; } = new();
	public List<string> HierarchyErrors { get; set; } = new();
	public Dictionary<HumanoidBone, int> BoneMapping { get; set; } = new();
	public string ErrorMessage { get; set; } = string.Empty;
}

public static class SkeletonValidator
{
	private static readonly HumanoidBone[] RequiredHumanoidBones = new[]
	{
		HumanoidBone.Hips,
		HumanoidBone.Spine,
		HumanoidBone.LeftUpperArm,
		HumanoidBone.LeftLowerArm,
		HumanoidBone.LeftHand,
		HumanoidBone.RightUpperArm,
		HumanoidBone.RightLowerArm,
		HumanoidBone.RightHand,
		HumanoidBone.LeftUpperLeg,
		HumanoidBone.LeftLowerLeg,
		HumanoidBone.LeftFoot,
		HumanoidBone.RightUpperLeg,
		HumanoidBone.RightLowerLeg,
		HumanoidBone.RightFoot
	};

	public static Skeleton3D FindSkeleton(Node rootNode)
	{
		if (rootNode == null) return null;
		if (rootNode is Skeleton3D skeleton) return skeleton;
		if (rootNode is Unit3D unit && unit.ModelNode != null)
		{
			var foundInModel = FindSkeleton(unit.ModelNode);
			if (foundInModel != null) return foundInModel;
		}

		int childCount = rootNode.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			var found = FindSkeleton(rootNode.GetChild(i));
			if (found != null) return found;
		}

		return null;
	}

	public static SkeletonValidationResult Validate(Node modelRoot)
	{
		var result = new SkeletonValidationResult();

		if (modelRoot == null)
		{
			result.IsValid = false;
			result.ErrorMessage = "Model root node is null.";
			return result;
		}

		Skeleton3D skeleton = FindSkeleton(modelRoot);
		if (skeleton == null)
		{
			result.IsValid = false;
			result.ErrorMessage = "Target model does not contain a Skeleton3D (unrigged mesh).";
			return result;
		}

		result.Skeleton = skeleton;
		result.BoneMapping = skeleton.BuildSkeletonBoneMap();

		if (!result.BoneMapping.ContainsKey(HumanoidBone.Head) && !result.BoneMapping.ContainsKey(HumanoidBone.Neck))
		{
			result.MissingRequiredBones.Add("Head/Neck");
		}

		foreach (var requiredBone in RequiredHumanoidBones)
		{
			if (!result.BoneMapping.ContainsKey(requiredBone))
			{
				result.MissingRequiredBones.Add(requiredBone.ToString());
			}
		}

		if (result.MissingRequiredBones.Count > 0)
		{
			result.IsValid = false;
			result.ErrorMessage = $"Missing required humanoid bones: {string.Join(", ", result.MissingRequiredBones)}.";
			return result;
		}

		ValidateBoneChain(skeleton, result, HumanoidBone.Hips, HumanoidBone.Spine);
		ValidateBoneChain(skeleton, result, HumanoidBone.Hips, HumanoidBone.LeftUpperLeg);
		ValidateBoneChain(skeleton, result, HumanoidBone.Hips, HumanoidBone.RightUpperLeg);

		ValidateBoneChain(skeleton, result, HumanoidBone.LeftUpperArm, HumanoidBone.LeftLowerArm);
		ValidateBoneChain(skeleton, result, HumanoidBone.LeftLowerArm, HumanoidBone.LeftHand);

		ValidateBoneChain(skeleton, result, HumanoidBone.RightUpperArm, HumanoidBone.RightLowerArm);
		ValidateBoneChain(skeleton, result, HumanoidBone.RightLowerArm, HumanoidBone.RightHand);

		ValidateBoneChain(skeleton, result, HumanoidBone.LeftUpperLeg, HumanoidBone.LeftLowerLeg);
		ValidateBoneChain(skeleton, result, HumanoidBone.LeftLowerLeg, HumanoidBone.LeftFoot);

		ValidateBoneChain(skeleton, result, HumanoidBone.RightUpperLeg, HumanoidBone.RightLowerLeg);
		ValidateBoneChain(skeleton, result, HumanoidBone.RightLowerLeg, HumanoidBone.RightFoot);

		if (result.HierarchyErrors.Count > 0)
		{
			result.IsValid = false;
			result.ErrorMessage = $"Bone hierarchy mismatch: {string.Join("; ", result.HierarchyErrors)}.";
			return result;
		}

		result.IsValid = true;
		return result;
	}

	private static void ValidateBoneChain(
		Skeleton3D skeleton,
		SkeletonValidationResult result,
		HumanoidBone parentBone,
		HumanoidBone childBone)
	{
		if (!result.BoneMapping.TryGetValue(parentBone, out int parentIdx) ||
			!result.BoneMapping.TryGetValue(childBone, out int childIdx))
		{
			return;
		}

		if (!IsAncestor(skeleton, parentIdx, childIdx))
		{
			result.HierarchyErrors.Add($"Bone '{childBone}' is not a descendant of '{parentBone}'");
		}
	}

	private static bool IsAncestor(Skeleton3D skeleton, int ancestorIdx, int descendantIdx)
	{
		if (ancestorIdx == descendantIdx) return true;
		int current = skeleton.GetBoneParent(descendantIdx);
		while (current >= 0)
		{
			if (current == ancestorIdx) return true;
			current = skeleton.GetBoneParent(current);
		}
		return false;
	}
}
