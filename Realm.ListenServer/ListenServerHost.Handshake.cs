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

/// <summary>the hello exchange and its rejections.</summary>
public partial class ListenServerHost
	{
	private void HandleChatLine(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;
		if (!_seatedPeers.TryGetValue(peerId, out int playerSlot)) return;

		ChatLine line;
		try
		{
			line = MemoryPackSerializer.Deserialize<ChatLine>(payload);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a chat line that could not be read: {exception.Message}");
			return;
		}

		if (!ChatLineLimits.IsAcceptable(line.Message)) return;

		simulation.ApplyChatLine(peerId, line.Message);

		var relayed = new ChatLine
		{
			Sender = simulation.ReadPlayerName(playerSlot),
			Message = line.Message.Trim(),
			AlliesOnly = line.AlliesOnly,
			TimestampUtcTicks = line.TimestampUtcTicks > 0 ? line.TimestampUtcTicks : DateTime.UtcNow.Ticks
		};

		byte[] bytes = MemoryPackSerializer.Serialize(relayed);
		int senderTeam = simulation.ReadTeam(playerSlot);
		foreach (var seated in _seatedPeers)
		{
			if (relayed.AlliesOnly && simulation.ReadTeam(seated.Value) != senderTeam) continue;

			_transport.Send(seated.Key, NetworkFrameKind.ChatLine, bytes);
		}
	}

	private void HandleHello(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		// A cheats match belongs to the player it was started for, so nobody else may join it. What "nobody else" means is
		// set by the socket rather than by the flag: a match bound to the loopback address cannot be reached from another
		// machine at all, so the only peers that can arrive on it are processes on this one, which are already the player's
		// own. A match listening on a real interface is not loopback-bound and keeps the single-player rule.
		if (_arguments.Cheats && _seatedPeers.Count > 0 && !_arguments.LoopbackOnly)
		{
			_log.Write($"peer {peerId} refused: this match was started with cheats, so it belongs to the single player it was started for");
			SendHello(peerId, CheatsMatchRejection);
			_transport.Disconnect(peerId);
			return;
		}

		ClientHello hello;
		try
		{
			hello = MemoryPackSerializer.Deserialize<ClientHello>(payload);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a hello that could not be read: {exception.Message}");
			_transport.Disconnect(peerId);
			return;
		}

		string rejection = DescribeHandshakeRejection(peerId, in hello);
		if (rejection.Length > 0)
		{
			_log.Write($"peer {peerId} refused: {rejection}");
			SendHello(peerId, rejection);
			_transport.Disconnect(peerId);
			return;
		}

		int playerSlot;
		int team;
		if (_lobbySessionSlots.Count > 0)
		{
			if (string.IsNullOrEmpty(hello.SessionToken) || !_lobbySessionSlots.TryGetValue(hello.SessionToken, out playerSlot))
			{
				_log.Write($"peer {peerId} refused: this match's roster was arranged by a lobby, and this connection is not one of its seats");
				SendHello(peerId, LobbyRosterClosedRejection);
				_transport.Disconnect(peerId);
				return;
			}

			if (!simulation.SeatPeerAtSlot(peerId, playerSlot))
			{
				_log.Write($"peer {peerId} refused: lobby seat {playerSlot} already belongs to another connection");
				SendHello(peerId, LobbySeatTakenRejection);
				_transport.Disconnect(peerId);
				return;
			}

			team = simulation.ReadTeam(playerSlot);
		}
		else if (!TryReclaimSessionSlot(simulation, peerId, hello.SessionToken, out playerSlot, out team)
		         && !simulation.TryAssignPlayerSlot(peerId, out playerSlot, out team))
		{
			const string noSlot = "server_rejected_no_free_slot";
			_log.Write($"peer {peerId} refused: every player slot this map declares is taken");
			SendHello(peerId, noSlot);
			_transport.Disconnect(peerId);
			return;
		}

		if (!string.IsNullOrEmpty(hello.SessionToken)) _sessionTokenSlots[hello.SessionToken] = playerSlot;
		BeginSeatedPeerBookkeeping(peerId, playerSlot, simulation.TickCount);
		simulation.SetPlayerLanguage(playerSlot, hello.Language);
		SendHello(peerId, string.Empty);

		var assignment = new PeerRosterAssignment
		{
			PeerId = peerId,
			PlayerSlot = playerSlot,
			Team = team
		};

		_transport.Send(peerId, NetworkFrameKind.Roster, MemoryPackSerializer.Serialize(assignment));
		_log.Write($"peer {peerId} seated as player {playerSlot} (team {team})");
		TryClaimBaselineRequest(peerId, simulation.TickCount);
		simulation.RequestBaseline(peerId);

		SendNavMeshObstacles(peerId);
		SendPauseState(peerId);
		SendPauseAllowance(peerId, string.Empty);
	}

	private void SendHello(int peerId, string rejection, bool isAdmin = false, bool inLobby = false)
	{
		var response = new ServerHello
		{
			Build = RealmVersion.GameBuildNumber,
			Accepted = rejection.Length == 0,
			Rejection = rejection,
			MapHash = _mapHash,
			PeerId = peerId,
			IsAdmin = isAdmin,
			InLobby = inLobby
		};

		_transport.Send(peerId, NetworkFrameKind.ServerHello, MemoryPackSerializer.Serialize(response));
	}

	private const string CheatsMatchRejection = "server_rejected_cheats_match_is_local_only";

	private const string LobbyRosterClosedRejection = "server_rejected_lobby_roster_is_closed";

	private const string LobbySeatTakenRejection = "server_rejected_lobby_seat_is_taken";

	private string DescribeHandshakeRejection(int peerId, in ClientHello hello)
	{
		if (!string.Equals(hello.Build, RealmVersion.GameBuildNumber, StringComparison.Ordinal)) return "server_rejected_build";
		if (!string.Equals(hello.WireTableHash, ReplicatedComponents.ComputeWireTableHash(), StringComparison.Ordinal)) return "server_rejected_wire_table";

		if (!string.IsNullOrEmpty(hello.MapHash) && !string.Equals(hello.MapHash, _mapHash, StringComparison.Ordinal)) return "server_rejected_map";

		if (!TokenMatches(peerId, hello.TokenProof)) return "server_rejected_token";
		return string.Empty;
	}

	private bool TokenMatches(int peerId, string? presentedProof)
	{
		if (_arguments.Token.Length == 0)
		{
			_pendingChallenges.Remove(peerId);
			return true;
		}

		if (!_pendingChallenges.Remove(peerId, out string? challenge)) return false;
		if (string.IsNullOrEmpty(presentedProof)) return false;

		byte[] presented = Encoding.UTF8.GetBytes(presentedProof);
		byte[] expected = Encoding.UTF8.GetBytes(RealmProtocol.ProveToken(_arguments.Token, challenge));
		return CryptographicOperations.FixedTimeEquals(presented, expected);
	}
}
