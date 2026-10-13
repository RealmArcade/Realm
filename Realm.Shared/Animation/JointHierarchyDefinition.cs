using System.Numerics;

namespace Realm.Shared.Animation;

public struct JointHierarchyDefinition
{
	public HumanoidBone Bone;
	public HumanoidBone Parent;
	public Vector3 RestOffset;
}