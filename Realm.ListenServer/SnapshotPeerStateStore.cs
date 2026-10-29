using System.Collections.Concurrent;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public class SnapshotPeerStateStore
{
	private readonly ConcurrentDictionary<int, SnapshotPeerState> _statesByPeerId = new();

	public int Count => _statesByPeerId.Count;

	public SnapshotPeerState Get(int peerId) => _statesByPeerId.GetOrAdd(peerId, static _ => new SnapshotPeerState());

	public void Forget(int peerId) => _statesByPeerId.TryRemove(peerId, out _);
}

public class SnapshotPeerState
{
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

	public ulong ToldCodecMask;

	public byte[]?[] SentPayloads;

	public void ForgetToldPayloadBytes()
	{
		Array.Clear(SentPayloads, 0, SentPayloads.Length);
	}
}
