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

/// <summary>peers arriving, leaving and reclaiming a session slot.</summary>
public partial class ListenServerHost
	{
	public void OnPeerConnected(int peerId)
	{
		_connectedPeers.Add(peerId);
		_log.Write($"peer {peerId} connected");

		// The challenge is the first thing a peer is told, because the hello is the first thing it may send. The ticket
		// has to prove itself without travelling; an open match still hands a challenge out, so a peer never has to know
		// whether the authority it reached was started with a ticket.
		string challenge = RealmProtocol.CreateChallenge();
		_pendingChallenges[peerId] = challenge;
		_transport.Send(peerId, NetworkFrameKind.HandshakeChallenge, MemoryPackSerializer.Serialize(new HandshakeChallenge { Nonce = challenge }));
	}

	public void OnPeerDisconnected(int peerId, string reason)
	{
		_connectedPeers.Remove(peerId);

		if (_lobby != null && _simulation == null)
		{
			_log.Write($"peer {peerId} left the lobby: {reason}");
			_lobby.OnPeerDisconnected(peerId);
			ForgetPeerBookkeeping(peerId);
			return;
		}

		bool wasSeated = _seatedPeers.Remove(peerId);
		_log.Write(wasSeated ? $"peer {peerId} left: {reason}" : $"peer {peerId} dropped before it was seated: {reason}");

		_simulation?.ForgetPeer(peerId);
		ForgetPeerBookkeeping(peerId);

		// A pause belongs to whoever asked for it. If that player is gone, nobody left can resume it, so the match resumes
		// itself rather than staying stopped for the rest of the session.
		if (!wasSeated || !_paused || _seatedPeers.Count == 0) return;
		if (_pausedByPlayerSlot < 0) return;

		foreach (int seatedPlayerSlot in _seatedPeers.Values)
		{
			if (seatedPlayerSlot == _pausedByPlayerSlot) return;
		}

		ResumeMatch("the player who paused the match left, so the match resumes");
	}

	private bool TryReclaimSessionSlot(ServerSimulation simulation, int peerId, string sessionToken, out int playerSlot, out int team)
	{
		playerSlot = 0;
		team = 0;

		if (string.IsNullOrEmpty(sessionToken)) return false;
		if (!_sessionTokenSlots.TryGetValue(sessionToken, out playerSlot)) return false;

		if (!simulation.SeatPeerAtSlot(peerId, playerSlot))
		{
			playerSlot = 0;
			return false;
		}

		team = simulation.ReadTeam(playerSlot);
		_log.Write($"peer {peerId} took back player slot {playerSlot} with the session it was seated with");
		return true;
	}

	private void BeginSeatedPeerBookkeeping(int peerId, int playerSlot, long tick)
	{
		_seatedPeers[peerId] = playerSlot;
		_seatedAtTickPerPeer[peerId] = tick;
		_unacknowledgedPublicationsPerPeer[peerId] = -1;
		_acknowledgedSequencePerPeer.Remove(peerId);
		_roundTripMillisecondsPerPeer.Remove(peerId);
		_firstSnapshotSequencePerPeer.Remove(peerId);
		_lastLoggedAckSequencePerPeer.Remove(peerId);
		_lastBaselineRequestTickPerPeer.Remove(peerId);
		_lastAcknowledgedProgressTickPerPeer[peerId] = tick;
		_lastRepairSentTickPerPeer.Remove(peerId);
		_lastRepairFirstSequencePerPeer.Remove(peerId);
		_obstacleSignaturePerPeer.Remove(peerId);
	}

	private void ForgetPeerBookkeeping(int peerId)
	{
		_pendingChallenges.Remove(peerId);
		_lastLoggedAckSequencePerPeer.Remove(peerId);
		_firstSnapshotSequencePerPeer.Remove(peerId);
		_lastBaselineRequestTickPerPeer.Remove(peerId);
		_lastAcknowledgedProgressTickPerPeer.Remove(peerId);
		_unacknowledgedPublicationsPerPeer.Remove(peerId);
		_acknowledgedSequencePerPeer.Remove(peerId);
		_roundTripMillisecondsPerPeer.Remove(peerId);
		_seatedAtTickPerPeer.Remove(peerId);
		_lastRepairSentTickPerPeer.Remove(peerId);
		_lastRepairFirstSequencePerPeer.Remove(peerId);
		_obstacleSignaturePerPeer.Remove(peerId);
		_commandsThisTick.Remove(peerId);
		_peerLanguages.Remove(peerId);

		// Nothing this peer was sent may answer a repair once it is gone, and a peer id is not a session: one that leaves
		// and comes back under the same id must not be answered from the descriptions it was sent before it went.
		_frameRings.Remove(peerId);
		_peersThatLoseDescriptions.Remove(peerId);
		_descriptionsLostByPeer.Remove(peerId);
		_lossWindowStartedTickPerPeer.Remove(peerId);
	}
}
