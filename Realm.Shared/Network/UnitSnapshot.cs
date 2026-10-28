using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>Replicated state of a single unit or building.</summary>
[MemoryPackable]
public partial struct UnitSnapshot
{
	public int EntityId;
	public string UnitId;
	public int OwnerPlayerEntityId;
	public int OwnerPlayerIndex;
	public NetworkVector3 Position;
	public float RotationY;
	public float CurrentHp;
	public float MaxHp;
	public bool IsDead;
	public bool IsBuilding;
	public bool IsDetailed;
}
