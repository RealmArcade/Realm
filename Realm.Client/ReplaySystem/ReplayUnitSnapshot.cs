using MemoryPack;

namespace Realm.Client.ReplaySystem
{
	[MemoryPackable]
	public partial class ReplayUnitSnapshot
	{
		public int EntityId { get; set; }
		public string UnitId { get; set; }
		public int OwnerPlayerEntityId { get; set; }
		public NetworkVector3 Position { get; set; }
		public float RotationY { get; set; }
		public float CurrentHp { get; set; }
		public float MaxHp { get; set; }
		public bool IsDead { get; set; }
		public bool IsBuilding { get; set; }
		public NetworkVector3 Velocity { get; set; }
		public string Animation { get; set; }
	}
}
