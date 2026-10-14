using MemoryPack;

namespace Realm.Client.ReplaySystem;

[MemoryPackable]
public partial class ReplayPlayerInfo
{
	public int PeerId { get; set; }
	public string Name { get; set; }
	public string Faction { get; set; }
	public float ColorR { get; set; }
	public float ColorG { get; set; }
	public float ColorB { get; set; }
}