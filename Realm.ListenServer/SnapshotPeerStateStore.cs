using System;
using System.Collections.Concurrent;
using System.Threading;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public class SnapshotPeerStateStore
{
	private readonly ConcurrentDictionary<int, SnapshotPeerState> _statesByPeerId = new();

	public int Count => _statesByPeerId.Count;

	public SnapshotPeerState Get(int peerId) => _statesByPeerId.GetOrAdd(peerId, static _ => new SnapshotPeerState());

	public void Ensure(int peerId) => _statesByPeerId.TryAdd(peerId, new SnapshotPeerState());

	public void Forget(int peerId) => _statesByPeerId.TryRemove(peerId, out _);
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
