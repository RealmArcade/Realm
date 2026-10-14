using MemoryPack;

namespace Realm.Client.ReplaySystem;

[MemoryPackable]
public partial struct ReplayPlayerResourceSnapshot
{
	public float Gold { get; set; }
	public float Wood { get; set; }
	public float Stone { get; set; }
}