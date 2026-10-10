namespace Realm.Shared.Animation;

public class HumanoidBoneMapper
{
	private static readonly Dictionary<string, HumanoidBone> KnownAliases = new(StringComparer.OrdinalIgnoreCase)
	{
		["hips"] = HumanoidBone.Hips,
		["pelvis"] = HumanoidBone.Hips,
		["root"] = HumanoidBone.Hips,
		["spine"] = HumanoidBone.Spine,
		["spine1"] = HumanoidBone.Chest,
		["chest"] = HumanoidBone.Chest,
		["spine2"] = HumanoidBone.UpperChest,
		["upperchest"] = HumanoidBone.UpperChest,
		["neck"] = HumanoidBone.Neck,
		["head"] = HumanoidBone.Head,

		["leftshoulder"] = HumanoidBone.LeftShoulder,
		["shoulder_l"] = HumanoidBone.LeftShoulder,
		["l_shoulder"] = HumanoidBone.LeftShoulder,
		["clavicle_l"] = HumanoidBone.LeftShoulder,
		["l_clavicle"] = HumanoidBone.LeftShoulder,

		["leftarm"] = HumanoidBone.LeftUpperArm,
		["leftupperarm"] = HumanoidBone.LeftUpperArm,
		["upperarm_l"] = HumanoidBone.LeftUpperArm,
		["l_upperarm"] = HumanoidBone.LeftUpperArm,
		["arm_l"] = HumanoidBone.LeftUpperArm,

		["leftforearm"] = HumanoidBone.LeftLowerArm,
		["leftlowerarm"] = HumanoidBone.LeftLowerArm,
		["forearm_l"] = HumanoidBone.LeftLowerArm,
		["lowerarm_l"] = HumanoidBone.LeftLowerArm,
		["l_forearm"] = HumanoidBone.LeftLowerArm,

		["lefthand"] = HumanoidBone.LeftHand,
		["hand_l"] = HumanoidBone.LeftHand,
		["l_hand"] = HumanoidBone.LeftHand,

		["rightshoulder"] = HumanoidBone.RightShoulder,
		["shoulder_r"] = HumanoidBone.RightShoulder,
		["r_shoulder"] = HumanoidBone.RightShoulder,
		["clavicle_r"] = HumanoidBone.RightShoulder,
		["r_clavicle"] = HumanoidBone.RightShoulder,

		["rightarm"] = HumanoidBone.RightUpperArm,
		["rightupperarm"] = HumanoidBone.RightUpperArm,
		["upperarm_r"] = HumanoidBone.RightUpperArm,
		["r_upperarm"] = HumanoidBone.RightUpperArm,
		["arm_r"] = HumanoidBone.RightUpperArm,

		["rightforearm"] = HumanoidBone.RightLowerArm,
		["rightlowerarm"] = HumanoidBone.RightLowerArm,
		["forearm_r"] = HumanoidBone.RightLowerArm,
		["lowerarm_r"] = HumanoidBone.RightLowerArm,
		["r_forearm"] = HumanoidBone.RightLowerArm,

		["righthand"] = HumanoidBone.RightHand,
		["hand_r"] = HumanoidBone.RightHand,
		["r_hand"] = HumanoidBone.RightHand,

		["leftupleg"] = HumanoidBone.LeftUpperLeg,
		["leftupperleg"] = HumanoidBone.LeftUpperLeg,
		["thigh_l"] = HumanoidBone.LeftUpperLeg,
		["upperleg_l"] = HumanoidBone.LeftUpperLeg,
		["l_thigh"] = HumanoidBone.LeftUpperLeg,
		["l_upperleg"] = HumanoidBone.LeftUpperLeg,

		["leftleg"] = HumanoidBone.LeftLowerLeg,
		["leftlowerleg"] = HumanoidBone.LeftLowerLeg,
		["shin_l"] = HumanoidBone.LeftLowerLeg,
		["calf_l"] = HumanoidBone.LeftLowerLeg,
		["lowerleg_l"] = HumanoidBone.LeftLowerLeg,
		["l_calf"] = HumanoidBone.LeftLowerLeg,
		["l_shin"] = HumanoidBone.LeftLowerLeg,

		["leftfoot"] = HumanoidBone.LeftFoot,
		["foot_l"] = HumanoidBone.LeftFoot,
		["l_foot"] = HumanoidBone.LeftFoot,

		["lefttoebase"] = HumanoidBone.LeftToes,
		["lefttoe"] = HumanoidBone.LeftToes,
		["lefttoes"] = HumanoidBone.LeftToes,
		["toe_l"] = HumanoidBone.LeftToes,
		["toes_l"] = HumanoidBone.LeftToes,
		["l_toe"] = HumanoidBone.LeftToes,

		["rightupleg"] = HumanoidBone.RightUpperLeg,
		["rightupperleg"] = HumanoidBone.RightUpperLeg,
		["thigh_r"] = HumanoidBone.RightUpperLeg,
		["upperleg_r"] = HumanoidBone.RightUpperLeg,
		["r_thigh"] = HumanoidBone.RightUpperLeg,
		["r_upperleg"] = HumanoidBone.RightUpperLeg,

		["rightleg"] = HumanoidBone.RightLowerLeg,
		["rightlowerleg"] = HumanoidBone.RightLowerLeg,
		["shin_r"] = HumanoidBone.RightLowerLeg,
		["calf_r"] = HumanoidBone.RightLowerLeg,
		["lowerleg_r"] = HumanoidBone.RightLowerLeg,
		["r_calf"] = HumanoidBone.RightLowerLeg,
		["r_shin"] = HumanoidBone.RightLowerLeg,

		["rightfoot"] = HumanoidBone.RightFoot,
		["foot_r"] = HumanoidBone.RightFoot,
		["r_foot"] = HumanoidBone.RightFoot,

		["righttoebase"] = HumanoidBone.RightToes,
		["righttoe"] = HumanoidBone.RightToes,
		["righttoes"] = HumanoidBone.RightToes,
		["toe_r"] = HumanoidBone.RightToes,
		["toes_r"] = HumanoidBone.RightToes,
		["r_toe"] = HumanoidBone.RightToes,

		["lefthandthumb1"] = HumanoidBone.LeftThumb1,
		["lefthandthumb2"] = HumanoidBone.LeftThumb2,
		["lefthandthumb3"] = HumanoidBone.LeftThumb3,
		["lefthandindex1"] = HumanoidBone.LeftIndex1,
		["lefthandindex2"] = HumanoidBone.LeftIndex2,
		["lefthandindex3"] = HumanoidBone.LeftIndex3,
		["lefthandmiddle1"] = HumanoidBone.LeftMiddle1,
		["lefthandmiddle2"] = HumanoidBone.LeftMiddle2,
		["lefthandmiddle3"] = HumanoidBone.LeftMiddle3,
		["lefthandring1"] = HumanoidBone.LeftRing1,
		["lefthandring2"] = HumanoidBone.LeftRing2,
		["lefthandring3"] = HumanoidBone.LeftRing3,
		["lefthandpinky1"] = HumanoidBone.LeftLittle1,
		["lefthandpinky2"] = HumanoidBone.LeftLittle2,
		["lefthandpinky3"] = HumanoidBone.LeftLittle3,
		["leftpinky1"] = HumanoidBone.LeftLittle1,
		["leftpinky2"] = HumanoidBone.LeftLittle2,
		["leftpinky3"] = HumanoidBone.LeftLittle3,
		["lefthandlittle1"] = HumanoidBone.LeftLittle1,
		["lefthandlittle2"] = HumanoidBone.LeftLittle2,
		["lefthandlittle3"] = HumanoidBone.LeftLittle3,

		["righthandthumb1"] = HumanoidBone.RightThumb1,
		["righthandthumb2"] = HumanoidBone.RightThumb2,
		["righthandthumb3"] = HumanoidBone.RightThumb3,
		["righthandindex1"] = HumanoidBone.RightIndex1,
		["righthandindex2"] = HumanoidBone.RightIndex2,
		["righthandindex3"] = HumanoidBone.RightIndex3,
		["righthandmiddle1"] = HumanoidBone.RightMiddle1,
		["righthandmiddle2"] = HumanoidBone.RightMiddle2,
		["righthandmiddle3"] = HumanoidBone.RightMiddle3,
		["righthandring1"] = HumanoidBone.RightRing1,
		["righthandring2"] = HumanoidBone.RightRing2,
		["righthandring3"] = HumanoidBone.RightRing3,
		["righthandpinky1"] = HumanoidBone.RightLittle1,
		["righthandpinky2"] = HumanoidBone.RightLittle2,
		["righthandpinky3"] = HumanoidBone.RightLittle3,
		["rightpinky1"] = HumanoidBone.RightLittle1,
		["rightpinky2"] = HumanoidBone.RightLittle2,
		["rightpinky3"] = HumanoidBone.RightLittle3,
		["righthandlittle1"] = HumanoidBone.RightLittle1,
		["righthandlittle2"] = HumanoidBone.RightLittle2,
		["righthandlittle3"] = HumanoidBone.RightLittle3
	};

	public static string CleanBoneName(string rawName)
	{
		if (string.IsNullOrEmpty(rawName)) return string.Empty;

		string clean = StripNamespaces(rawName);
		clean = StripAssimpSuffix(clean);
		clean = StripRigPrefixes(clean);

		return clean;
	}

	private static string StripNamespaces(string name)
	{
		int colonIdx = name.LastIndexOf(':');
		if (colonIdx >= 0)
		{
			name = name.Substring(colonIdx + 1);
		}

		int slashIdx = name.LastIndexOf('/');
		if (slashIdx >= 0)
		{
			name = name.Substring(slashIdx + 1);
		}

		return name;
	}

	private static string StripAssimpSuffix(string name)
	{
		int assimpFbxIdx = name.IndexOf("_$AssimpFbx$_", StringComparison.OrdinalIgnoreCase);
		if (assimpFbxIdx >= 0)
		{
			return name.Substring(0, assimpFbxIdx);
		}

		return name;
	}

	private static string StripRigPrefixes(string name)
	{
		if (name.StartsWith("mixamorig_", StringComparison.OrdinalIgnoreCase))
		{
			name = name.Substring("mixamorig_".Length);
		}

		if (name.StartsWith("mixamorig", StringComparison.OrdinalIgnoreCase) && 
		    name.Length > "mixamorig".Length && 
		    name["mixamorig".Length] is ':' or '_' or '.')
		{
			name = name.Substring("mixamorig".Length + 1);
		}

		if (name.StartsWith("bip01_", StringComparison.OrdinalIgnoreCase) || 
		    name.StartsWith("bip01 ", StringComparison.OrdinalIgnoreCase))
		{
			name = name.Substring(6);
		}

		return name;
	}

	public static bool TryMapToCanonical(string rawName, out HumanoidBone canonicalBone)
	{
		string cleaned = CleanBoneName(rawName);
		if (KnownAliases.TryGetValue(cleaned, out canonicalBone))
		{
			return true;
		}

		if (Enum.TryParse<HumanoidBone>(cleaned, true, out canonicalBone))
		{
			return true;
		}

		canonicalBone = default;
		return false;
	}
}