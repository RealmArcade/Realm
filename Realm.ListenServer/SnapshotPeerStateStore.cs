using System.Collections.Concurrent;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     Everything the authority has already told each peer, held apart from the builder that reads it so several workers
///     can describe several peers at the same time. Within one tick a peer is described exactly once, so its state is
///     only ever written by one thread; the store itself is safe to read from all of them.
/// </summary>
public class SnapshotPeerStateStore
{
	private readonly ConcurrentDictionary<int, SnapshotPeerState> _statesByPeerId = new();

	public int Count => _statesByPeerId.Count;

	/// <summary>The state of one peer, created the first time the match describes it.</summary>
	public SnapshotPeerState Get(int peerId) => _statesByPeerId.GetOrAdd(peerId, static _ => new SnapshotPeerState());

	/// <summary>Forgets a peer that has gone, so the connection taking its place starts from nothing.</summary>
	public void Forget(int peerId) => _statesByPeerId.TryRemove(peerId, out _);
}

/// <summary>What one peer has already been told, so its next description carries only what changed since.</summary>
public class SnapshotPeerState
{
	/// <summary>
	///     The payload last sent for one component of the world or of a player, in the bytes that peer would receive. A
	///     unit's components are held on the unit's own record instead, because a description asks about several of them
	///     for every unit it can see.
	/// </summary>
	public Dictionary<(byte Scope, int Key, int TypeId), byte[]> SentComponents { get; } = new();

	/// <summary>What this peer was told about each unit it can see, so a unit that has not changed is not described again.</summary>
	public Dictionary<int, UnitDescriptionState> SentUnits { get; } = new();

	/// <summary>The entities this peer was described last, so the ones that left its sight are found without a full walk.</summary>
	public HashSet<int> DescribedEntityIds { get; } = new();

	/// <summary>
	///     Which components this peer was last told that a unit it can no longer see carries. A peer keeps the unit as it
	///     last saw it, so a component the authority drops while the unit is away has to be stated as gone when the unit
	///     comes back; without this the peer would hold it until the next baseline. One mask per unit is the whole cost of
	///     keeping that promise, and a baseline clears it because a baseline restates the world.
	/// </summary>
	public Dictionary<int, ulong> UnseenUnitComponents { get; } = new();

	/// <summary>The newest baseline this peer was given, which every delta it receives is written against.</summary>
	public int LastBaselineSequence { get; set; } = -1;

	/// <summary>How far this peer has been walked through the authority's text table.</summary>
	public int StringTableCursor { get; set; }

	/// <summary>The content payloads this peer already holds.</summary>
	public HashSet<int> SentContentIds { get; } = new();

	/// <summary>
	///     Forgets what this peer was told about the world's values, because a baseline states all of them again. What the
	///     peer was told about the text table and the content payloads is kept: those are described once for as long as
	///     the process lives, and a baseline is no reason to send them twice.
	/// </summary>
	public void ResetForDescription()
	{
		SentComponents.Clear();
		SentUnits.Clear();
		DescribedEntityIds.Clear();
		UnseenUnitComponents.Clear();
	}
}

/// <summary>
///     What one peer holds about one unit: the record it was last told, and the components of that unit it was told,
///     kept where the unit is rather than in a table keyed by entity and component type. A description of a unit asks
///     for the record and for each of the handful of components the unit carries, which in a table keyed by both was a
///     hashed lookup per component per unit per peer.
/// </summary>
public class UnitDescriptionState
{
	/// <param name="codecCount">How many components a unit can be described with, so each has its own place here.</param>
	public UnitDescriptionState(int codecCount)
	{
		SentPayloads = new byte[]?[codecCount];
	}

	/// <summary>The unit record this peer was last told, which a later description is compared against.</summary>
	public UnitSnapshot SentRecord;

	/// <summary>The unit codecs this peer was told the unit carries, one bit each.</summary>
	public ulong ToldCodecMask;

	/// <summary>The bytes this peer holds for each component of the unit, or nothing where it was told the unit lacks one.</summary>
	public byte[]?[] SentPayloads;

	/// <summary>
	///     Forgets the bytes this peer was told each component of the unit has, so every component it does carry is stated
	///     again. Which components the peer holds is kept: a peer replaces its replica when the identifier starts
	///     describing a different unit, and what it held for the old one has to be stated gone as well as restated.
	/// </summary>
	public void ForgetToldPayloadBytes()
	{
		Array.Clear(SentPayloads, 0, SentPayloads.Length);
	}
}
