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
	/// <summary>The payload last sent for one component of one entity, in the bytes that peer would receive.</summary>
	public Dictionary<(byte Scope, int Key, int TypeId), byte[]> SentComponents { get; } = new();

	/// <summary>The unit record last sent for one entity, so a unit that has not changed is not described again.</summary>
	public Dictionary<int, UnitSnapshot> SentUnits { get; } = new();

	/// <summary>The entities this peer was described last, so the ones that left its sight are found without a full walk.</summary>
	public HashSet<int> DescribedEntityIds { get; } = new();

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
	}
}
