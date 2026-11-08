using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using Realm.Ecs;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.Diagnostics;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

/// <summary>sending presentation events and batches.</summary>
public partial class ListenServerHost
	{
	private int CopySeatedPeerIds()
	{
		int count = 0;
		foreach (int peerId in _seatedPeers.Keys)
		{
			if (count == _seatedPeerIds.Length) break;
			_seatedPeerIds[count++] = peerId;
		}

		return count;
	}

	private void SendPresentation(int peerCount, ServerPresentationEvent[] events) => SendPresentation(_seatedPeerIds.AsSpan(0, peerCount), events, _publishSequence);

	private void SendPresentationToPeer(int peerId, ServerPresentationEvent[] events)
	{
		Span<int> singlePeer = stackalloc int[1];
		singlePeer[0] = peerId;

		// What a map declares about a match is stated once to the peer that has just been seated, so all of it travels on
		// the stream that cannot lose it: there is no later request to state it again.
		_statePresentationBatch.Clear();
		for (int index = 0; index < events.Length; index++) _statePresentationBatch.Add(events[index].ToMessage(PresentationMessage.StateDescriptionSequence));

		SendPresentationBatch(peerId, _statePresentationBatch, FrameDelivery.Reliable);
	}

	/// <summary>
	///     Tells each of these peers what the map raised this tick, in one frame per peer and per stream. A request that
	///     states something the screen cannot be left without travels on the stream that cannot lose it, and a moment it is
	///     only allowed to miss travels on the stream that would rather be current: putting an effect behind a baseline
	///     held every effect queued after it, and giving each one its own reliable frame cost a header apiece for a shower
	///     of sparks.
	/// </summary>
	private void SendPresentation(ReadOnlySpan<int> peerIds, ServerPresentationEvent[] events, int sequence)
	{
		for (int peerIndex = 0; peerIndex < peerIds.Length; peerIndex++)
		{
			int peerId = peerIds[peerIndex];
			bool seated = _seatedPeers.TryGetValue(peerId, out int playerSlot);

			_statePresentationBatch.Clear();
			_momentPresentationBatch.Clear();

			for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
			{
				ServerPresentationEvent request = events[eventIndex];
				if (request.PlayerIndex != ServerPresentationEvent.Everyone && (!seated || request.PlayerIndex != playerSlot)) continue;

				if (PresentationBatch.IsState(request.Kind)) _statePresentationBatch.Add(request.ToMessage(sequence));
				else _momentPresentationBatch.Add(request.ToMessage(sequence));
			}

			SendPresentationBatch(peerId, _statePresentationBatch, FrameDelivery.Reliable);
			SendPresentationBatch(peerId, _momentPresentationBatch, FrameDelivery.UnreliableOrdered);
		}
	}

	private void SendPresentationBatch(int peerId, List<PresentationMessage> messages, FrameDelivery delivery)
	{
		if (messages.Count == 0) return;

		var batch = new PresentationBatch
		{
			Messages = new PresentationMessage[messages.Count]
		};

		for (int index = 0; index < messages.Count; index++) batch.Messages[index] = messages[index];

		_transport.Send(peerId, NetworkFrameKind.Presentation, MemoryPackSerializer.Serialize(batch), delivery);
		_deliveredPresentationCount += messages.Count;
	}
}
