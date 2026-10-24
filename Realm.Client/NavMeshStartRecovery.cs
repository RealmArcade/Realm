using DotRecast.Core.Numerics;

namespace Realm.Client
{
	public readonly struct NavMeshStartRecovery
	{
		public readonly bool UsedFallback;
		public readonly bool Found;
		public readonly RcVec3f Requested;
		public readonly RcVec3f Snapped;
		public readonly long StartRef;
		public readonly float LateralDistance;
		public readonly float VerticalDelta;

		public NavMeshStartRecovery(bool usedFallback, bool found, RcVec3f requested, RcVec3f snapped, long startRef, float lateralDistance, float verticalDelta)
		{
			UsedFallback = usedFallback;
			Found = found;
			Requested = requested;
			Snapped = snapped;
			StartRef = startRef;
			LateralDistance = lateralDistance;
			VerticalDelta = verticalDelta;
		}
	}
}
