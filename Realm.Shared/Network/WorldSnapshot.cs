using System.Collections.Generic;
using MemoryPack;
using Realm.Ecs.Replication;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct WorldSnapshot
{
	public int Sequence;
	public bool IsBaseline;
	public int BaseSequence;
	[UnitSectionFormatter]
	public UnitSection Units;
	public List<ColdPayloadDelta> ColdPayloads;
	public List<ComponentDelta> ComponentDeltas;
	public List<AbilityStateDelta> AbilityStates;
	public List<StaticTextDelta> StaticTexts;
	public List<CatalogEntry> Catalog;

	public List<LootSnapshot>? Loot;

	public List<int>? RetiredEntityIds;

	public List<UnitComponentPresence> UnitComponentPresence;
	public uint StateFingerprint;

	public void ResetForDescription(int sequence, bool isBaseline, int baseSequence)
	{
		Sequence = sequence;
		IsBaseline = isBaseline;
		BaseSequence = baseSequence;

		Units ??= new UnitSection();
		Units.Clear();
		ColdPayloads ??= new List<ColdPayloadDelta>();
		ColdPayloads.Clear();
		ComponentDeltas ??= new List<ComponentDelta>();
		ComponentDeltas.Clear();
		AbilityStates ??= new List<AbilityStateDelta>();
		AbilityStates.Clear();
		StaticTexts ??= new List<StaticTextDelta>();
		StaticTexts.Clear();
		Catalog ??= new List<CatalogEntry>();
		Catalog.Clear();
		RetiredEntityIds ??= new List<int>();
		RetiredEntityIds.Clear();

		Loot = null;
		UnitComponentPresence = null;
		StateFingerprint = 0u;
	}
}
