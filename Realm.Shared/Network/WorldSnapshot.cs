using System.Collections.Generic;
using MemoryPack;
using Realm.Ecs.Replication;

namespace Realm.Shared.Network;

/// <summary>Replicated state of the entire world simulation for a specific tick.</summary>
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

	/// <summary>
	///     The items on the ground this peer can see, or nothing when they are unchanged since the description it already
	///     holds. A baseline always states them, even as an empty list, so a peer can retire an item that left its sight;
	///     a delta states them only when the set or one of its values changed, which for an item that never expires is
	///     once, when it is dropped.
	/// </summary>
	public List<LootSnapshot>? Loot;

	/// <summary>
	///     The units this peer was described with last tick and is not described with now, because they left its sight or
	///     left the world. A screen keeps what it holds for such a unit no longer: the authority is the only thing that
	///     can say the unit is there, and a replica it no longer describes is a unit the player could still select, count
	///     and order. An empty list states that nothing left, which is what a baseline says: a peer recovers from one by
	///     forgetting everything it holds rather than by being told what went.
	/// </summary>
	public List<int>? RetiredEntityIds;

	public List<UnitComponentPresence> UnitComponentPresence;
	public uint StateFingerprint;

	/// <summary>
	///     Empties this description for the tick about to be described, keeping the room its containers already have. A tick
	///     describes every peer, and a fresh set of lists for each of them was five to eight allocations per peer per tick -
	///     several thousand a second inside the phase a tick's whole describing was measured at. The description a slot
	///     holds is replaced by the next tick that describes it, so its containers are the ones that description is written
	///     into. What is stated only sometimes - the ground the peer can see, the units that left it - is stated as nothing
	///     rather than left holding what the tick before said.
	/// </summary>
	/// <param name="sequence">The authority's tick index this description is of.</param>
	/// <param name="isBaseline">Whether this description restates the whole world rather than what changed.</param>
	/// <param name="baseSequence">The newest baseline this description is written against.</param>
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
