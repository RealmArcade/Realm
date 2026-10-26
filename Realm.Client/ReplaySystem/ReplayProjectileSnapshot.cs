using MemoryPack;
using Realm.Shared.Network;

namespace Realm.Client.ReplaySystem
{
	[MemoryPackable]
	public partial class ReplayProjectileSnapshot
	{
		public string ProjectileTypeId { get; set; }
		public NetworkVector3 Start { get; set; }
		public NetworkVector3 Target { get; set; }
		public float Speed { get; set; }
	}
}