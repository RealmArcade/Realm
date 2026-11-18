using MemoryPack;

namespace Realm.Client.ReplaySystem;

[MemoryPackable]
public partial class ReplayPlayerInfo
{
	public int PeerId { get; set; }

	/// <summary>
	///     The name this participant was shown under in the match as it was recorded. A replay header keeps the identity
	///     beside the name, because a Steam persona may have changed since.
	/// </summary>
	public string Name { get; set; } = null!;
	public string Faction { get; set; } = null!;
	public float ColorR { get; set; }
	public float ColorG { get; set; }
	public float ColorB { get; set; }
}