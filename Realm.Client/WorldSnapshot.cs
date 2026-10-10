using MemoryPack;
using System.Collections.Generic;

namespace Realm.Client;

/// <summary>Replicated state of the entire world simulation for a specific tick.</summary>
[MemoryPackable]
public partial struct WorldSnapshot
{
	public int Sequence;
	public bool IsBaseline;
	public int BaseSequence;
	public List<UnitSnapshot> Units;
}