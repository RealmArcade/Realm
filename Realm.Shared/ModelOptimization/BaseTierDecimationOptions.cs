namespace Realm.Shared.ModelOptimization;

public struct BaseTierDecimationOptions
{
	public float TargetError { get; set; }
	public int MaximumTriangleCount { get; set; }
}
