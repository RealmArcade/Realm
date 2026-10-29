using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     The obstacle volumes the authority baked its navigation mesh with, sent to a peer so the mesh it bakes for
///     itself is the same one. A map's placed props are in both worlds because both load the same files, but a prop or
///     blocker the map's script spawned exists only in the authority's world, and a client that never learned it would
///     draw a route straight through a corridor the authority pathing treats as blocked.
/// </summary>
[MemoryPackable]
public partial struct NavMeshObstacleVolumes
{
	/// <summary>The centre of each obstacle, in world space.</summary>
	public NetworkVector3[] Positions;

	/// <summary>The radius of each obstacle, paired by index with <see cref="Positions" />.</summary>
	public float[] Radii;
}
