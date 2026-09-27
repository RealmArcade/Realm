using Godot;
using System;
using System.Collections.Generic;
using Realm.Shared.Animation;

namespace Realm.Godot.Animation;

public static class HumanoidBoneExtensions
{
	public static int FindBoneInSkeleton(this Skeleton3D skeleton, HumanoidBone canonicalBone)
	{
		if (skeleton == null) return -1;
		int boneCount = skeleton.GetBoneCount();
		for (int i = 0; i < boneCount; i++)
		{
			string boneName = skeleton.GetBoneName(i);
			if (HumanoidBoneMapper.TryMapToCanonical(boneName, out var mapped) && mapped == canonicalBone)
			{
				return i;
			}
		}
		return -1;
	}

	public static Dictionary<HumanoidBone, int> BuildSkeletonBoneMap(this Skeleton3D skeleton)
	{
		var result = new Dictionary<HumanoidBone, int>();
		if (skeleton == null) return result;
		int boneCount = skeleton.GetBoneCount();
		for (int i = 0; i < boneCount; i++)
		{
			string boneName = skeleton.GetBoneName(i);
			if (HumanoidBoneMapper.TryMapToCanonical(boneName, out var mapped))
			{
				if (!result.ContainsKey(mapped))
				{
					result[mapped] = i;
				}
			}
		}
		return result;
	}
}
