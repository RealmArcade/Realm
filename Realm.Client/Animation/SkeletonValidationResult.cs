using Godot;
using System.Collections.Generic;

namespace Realm.Client.Animation;

public class SkeletonValidationResult
{
	public bool IsValid { get; set; }
	public Skeleton3D Skeleton { get; set; }
	public List<string> MissingRequiredBones { get; set; } = new();
	public List<string> HierarchyErrors { get; set; } = new();
	public Dictionary<HumanoidBone, int> BoneMapping { get; set; } = new();
	public string ErrorMessage { get; set; } = string.Empty;
}