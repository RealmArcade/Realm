using System.Collections.Generic;
using MemoryPack;
using Realm.Ecs.Replication;

namespace Realm.Client;

/// <summary>Replicated state of the entire world simulation for a specific tick.</summary>
[MemoryPackable]
public partial struct WorldSnapshot
{
	public int Sequence;
	public bool IsBaseline;
	public int BaseSequence;
	public List<UnitSnapshot> Units;
	public List<ColdPayloadDelta> ColdPayloads;
	public List<ComponentDelta> ComponentDeltas;
	public List<AbilityStateDelta> AbilityStates;
	public List<StaticTextDelta> StaticTexts;
	public List<CatalogEntry> Catalog;
	public uint StateFingerprint;
}
