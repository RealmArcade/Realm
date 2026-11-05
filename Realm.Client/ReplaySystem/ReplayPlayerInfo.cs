using MemoryPack;

namespace Realm.Client.ReplaySystem;

[MemoryPackable]
public partial class ReplayPlayerInfo
{
	public int PeerId { get; set; }

	/// <summary>
	///     The account the replay participant played as, or 0 for a guest or computer slot. A replay header records the
	///     identity beside the name, because the name may have changed since the match was recorded.
	/// </summary>
	public long AccountId { get; set; }

	public string Name { get; set; }
	public string Faction { get; set; }
	public float ColorR { get; set; }
	public float ColorG { get; set; }
	public float ColorB { get; set; }
}