using System;
using System.Threading;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     What each peer has already been told. The tick thread is the writer: it creates the state of every seated peer
///     before the publish lanes are forked and removes it once the peer is gone. Every lane reads at once, so the table is
///     published by reference and replaced rather than mutated: a lane reads a dictionary that is never written, which is
///     what makes the lock-free lookup safe. A peer that seats mid-run pays for one replacement, not for a lock per lookup.
/// </summary>
public class SnapshotPeerStateStore
{
	private readonly object _creationLock = new();

	private Dictionary<int, SnapshotPeerState> _statesByPeerId = new();

	public int Count => Volatile.Read(ref _statesByPeerId).Count;

	public SnapshotPeerState Get(int peerId)
	{
		Dictionary<int, SnapshotPeerState> published = Volatile.Read(ref _statesByPeerId);
		if (published.TryGetValue(peerId, out SnapshotPeerState? state)) return state;

		return Ensure(peerId);
	}

	public SnapshotPeerState Ensure(int peerId)
	{
		Dictionary<int, SnapshotPeerState> published = Volatile.Read(ref _statesByPeerId);
		if (published.TryGetValue(peerId, out SnapshotPeerState? existing)) return existing;

		lock (_creationLock)
		{
			published = Volatile.Read(ref _statesByPeerId);
			if (published.TryGetValue(peerId, out existing)) return existing;

			var state = new SnapshotPeerState();
			var expanded = new Dictionary<int, SnapshotPeerState>(published) { [peerId] = state };
			Volatile.Write(ref _statesByPeerId, expanded);
			return state;
		}
	}

	public void Forget(int peerId)
	{
		Dictionary<int, SnapshotPeerState> published = Volatile.Read(ref _statesByPeerId);
		if (!published.ContainsKey(peerId)) return;

		lock (_creationLock)
		{
			published = Volatile.Read(ref _statesByPeerId);
			if (!published.ContainsKey(peerId)) return;

			var reduced = new Dictionary<int, SnapshotPeerState>(published);
			reduced.Remove(peerId);
			Volatile.Write(ref _statesByPeerId, reduced);
		}
	}
}

/// <summary>
///     What one peer has already been told. Its containers are mutated in place while a description is written, which is
///     safe only because exactly one worker describes a given peer at a time: <see cref="EnterDescription" /> is what holds
///     that invariant at run time rather than leaving it in the comment beside the fan-out that depends on it.
/// </summary>
public class SnapshotPeerState
{
	private int _describingThreadId;

	/// <summary>
	///     States that this thread is writing a description for this peer. A second thread entering the same peer is a
	///     defect in the fan-out, and one that would otherwise show up as two descriptions of the same peer disagreeing
	///     about what was already sent.
	/// </summary>
	public void EnterDescription()
	{
		int current = Environment.CurrentManagedThreadId;
		int owner = Interlocked.CompareExchange(ref _describingThreadId, current, 0);
		if (owner == 0 || owner == current) return;

		throw new InvalidOperationException($"this peer is already being described by thread {owner}; thread {current} cannot describe it at the same time");
	}

	public void ExitDescription() => Volatile.Write(ref _describingThreadId, 0);

	public Dictionary<(byte Scope, int Key, int TypeId), byte[]> SentComponents { get; } = new();

	public Dictionary<int, UnitDescriptionState> SentUnits { get; } = new();

	public HashSet<int> DescribedEntityIds { get; } = new();

	public Dictionary<int, ulong> UnseenUnitComponents { get; } = new();

	public int LastBaselineSequence { get; set; } = -1;

	public int StringTableCursor { get; set; }

	public HashSet<int> SentContentIds { get; } = new();

	public uint SentLootSignature { get; set; }

	public bool HasSentLoot { get; set; }

	public void ResetForDescription()
	{
		SentComponents.Clear();
		SentUnits.Clear();
		DescribedEntityIds.Clear();
		UnseenUnitComponents.Clear();
		HasSentLoot = false;
	}
}

public class UnitDescriptionState
{
	public UnitDescriptionState(int codecCount)
	{
		SentPayloads = new byte[]?[codecCount];
	}

	public UnitSnapshot SentRecord;

	/// <summary>
	///     The definition this peer was last told this unit is built from, so the description of a unit that moves can leave
	///     it out. Null until the peer has been told, and forgotten with the rest of the record when the unit leaves its
	///     sight.
	/// </summary>
	public string? ToldDefinition;

	public ulong ToldCodecMask;

	public byte[]?[] SentPayloads;

	public void ForgetToldPayloadBytes()
	{
		Array.Clear(SentPayloads, 0, SentPayloads.Length);
	}
}
