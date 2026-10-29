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
	///     and order. Only a delta states them, because a baseline restates the world and a peer recovers by forgetting
	///     everything it holds rather than by being told what went.
	/// </summary>
	public List<int>? RetiredEntityIds;

	public List<UnitComponentPresence> UnitComponentPresence;
	public uint StateFingerprint;
}
