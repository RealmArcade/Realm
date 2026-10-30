using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct NavMeshObstacleVolumes
{
	public NetworkVector3[] Positions;

	public float[] Radii;
}
