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

/// <summary>frames that arrive while the lobby, not a match, is running.</summary>
public partial class ListenServerHost
	{
	public void OnFrameReceived(int peerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload)
	{
		// A session is either a lobby or a match, and a peer's role decides which traffic it may reach. While the lobby
		// owns the session there is no world to order or acknowledge, so match frames are refused rather than queued for
		// a match that does not exist yet.
		if (_lobby != null && _simulation == null)
		{
			HandleLobbyFrame(peerId, kind, payload);
			return;
		}

		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		// A seat is not a match peer yet, whatever frame it sends: nothing it could say to a running match is accepted
		// here, so the connection that said it is the thing that ends.
		if (!_seatedPeers.ContainsKey(peerId))
		{
			if (kind != NetworkFrameKind.ClientHello)
			{
				_log.Write($"peer {peerId} sent a {kind} frame before its hello");
				_transport.Disconnect(peerId);
				return;
			}

			HandleHello(peerId, payload);
			return;
		}

		switch (kind)
		{
			case NetworkFrameKind.Command:
				HandleCommand(peerId, payload);
				break;
			case NetworkFrameKind.SnapshotAck:
				HandleSnapshotAck(peerId, payload);
				break;
			case NetworkFrameKind.BaselineRequest:
				if (TryClaimBaselineRequest(peerId, simulation.TickCount)) simulation.RequestBaseline(peerId);
				break;
			case NetworkFrameKind.SnapshotRepairRequest:
				HandleSnapshotRepairRequest(peerId, payload);
				break;
			case NetworkFrameKind.SnapshotDescriptionRepeat:
				HandleDescriptionRepeatRequest(peerId);
				break;
			case NetworkFrameKind.LatencyProbe:
				// The echo travels the same stream the probe did. Answering it reliably would time the probe's loss and
				// the retransmission that recovered it rather than the path, and that figure is what every screen sizes
				// its presentation buffer from.
				_transport.Send(peerId, NetworkFrameKind.LatencyProbeEcho, payload.ToArray(), FrameDelivery.UnreliableOrdered);
				break;
			case NetworkFrameKind.PauseRequest:
				HandlePauseRequest(peerId, payload);
				break;
			case NetworkFrameKind.ConsoleCommand:
				HandleConsoleCommand(peerId, payload);
				break;
			case NetworkFrameKind.ChatLine:
				HandleChatLine(peerId, payload);
				break;
			case NetworkFrameKind.Shutdown:
				_log.Write($"peer {peerId} asked the match to end");
				Stop();
				break;
			default:
				_log.Write($"peer {peerId} sent a {kind} frame this build does not accept");
				_transport.Disconnect(peerId);
				break;
		}
	}

	private void HandleLobbyFrame(int peerId, NetworkFrameKind kind, ReadOnlySpan<byte> payload)
	{
		ServerLobby? lobby = _lobby;
		if (lobby == null) return;

		if (kind == NetworkFrameKind.ClientHello)
		{
			HandleLobbyHello(peerId, payload);
			return;
		}

		if (!lobby.TryGetSeat(peerId, out LobbySeat seat))
		{
			_log.Write($"peer {peerId} sent a {kind} frame before it was admitted to the lobby");
			_transport.Disconnect(peerId);
			return;
		}

		switch (kind)
		{
			case NetworkFrameKind.LobbyCommand:
			{
				LobbyCommand command;
				try
				{
					command = MemoryPackSerializer.Deserialize<LobbyCommand>(payload);
				}
				catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
				{
					_log.Write($"peer {peerId} sent a lobby command that could not be read: {exception.Message}");
					return;
				}

				lobby.HandleCommand(peerId, command);
				break;
			}

			case NetworkFrameKind.MapTransferRequest:
				HandleMapTransfer(peerId, payload);
				break;

			case NetworkFrameKind.LatencyProbe:
				// The echo travels the same stream the probe did. Answering it reliably would time the probe's loss and
				// the retransmission that recovered it rather than the path, and that figure is what every screen sizes
				// its presentation buffer from.
				_transport.Send(peerId, NetworkFrameKind.LatencyProbeEcho, payload.ToArray(), FrameDelivery.UnreliableOrdered);
				break;

			case NetworkFrameKind.Shutdown:
				if (peerId != lobby.AdminPeerId)
				{
					_log.Write($"peer {peerId} asked to end a lobby it does not administer; the request is refused");
					return;
				}

				_log.Write($"the peer administering the lobby asked it to end");
				Stop();
				break;

			default:
				_log.Write($"lobby seat {seat.Slot} ('{seat.Name}') sent a {kind} frame, which a lobby member may not send");
				_transport.Disconnect(peerId);
				break;
		}
	}

	private void HandleLobbyHello(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerLobby? lobby = _lobby;
		if (lobby == null) return;

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

		string rejection = DescribeLobbyHandshakeRejection(peerId, in hello);
		if (rejection.Length > 0)
		{
			_log.Write($"peer {peerId} refused by the lobby: {rejection}");
			SendHello(peerId, rejection, isAdmin: false, inLobby: true);
			_transport.Disconnect(peerId);
			return;
		}

		bool proveAdmin = ProvesAdmin(peerId, hello.AdminProof);
		string admitted = lobby.AdmitPeer(peerId, hello.PlayerName, hello.AccountId, hello.SessionToken, proveAdmin, out bool isAdmin);
		if (admitted.Length > 0)
		{
			_log.Write($"peer {peerId} refused by the lobby: {admitted}");
			SendHello(peerId, admitted, isAdmin: false, inLobby: true);
			_transport.Disconnect(peerId);
			return;
		}

		_peerLanguages[peerId] = hello.Language ?? string.Empty;
		SendHello(peerId, string.Empty, isAdmin, inLobby: true);
		lobby.SendStateTo(peerId);
	}

	private string DescribeLobbyHandshakeRejection(int peerId, in ClientHello hello)
	{
		if (!string.Equals(hello.Build, RealmVersion.GameBuildNumber, StringComparison.Ordinal)) return "server_rejected_build";
		if (!string.Equals(hello.WireTableHash, ReplicatedComponents.ComputeWireTableHash(), StringComparison.Ordinal)) return "server_rejected_wire_table";

		if (!string.IsNullOrEmpty(hello.MapHash) && _mapHash.Length > 0 && !string.Equals(hello.MapHash, _mapHash, StringComparison.Ordinal)) return "server_rejected_map";
		return string.Empty;
	}

	private bool ProvesAdmin(int peerId, string? presentedProof)
	{
		if (_arguments.AdminToken.Length == 0) return false;
		if (string.IsNullOrEmpty(presentedProof)) return false;
		if (!_pendingChallenges.Remove(peerId, out string? challenge)) return false;

		byte[] presented = Encoding.UTF8.GetBytes(presentedProof);
		byte[] expected = Encoding.UTF8.GetBytes(RealmProtocol.ProveToken(_arguments.AdminToken, challenge));
		return CryptographicOperations.FixedTimeEquals(presented, expected);
	}

	private void HandleMapTransfer(int peerId, ReadOnlySpan<byte> payload)
	{
		MapTransferRequest request;
		try
		{
			request = MemoryPackSerializer.Deserialize<MapTransferRequest>(payload);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a map transfer request that could not be read: {exception.Message}");
			return;
		}

		MapTransferReply reply = _mapShares?.Handle(peerId, request) ?? new MapTransferReply
		{
			Kind = MapTransferReplyKind.Refused,
			TransferId = request.TransferId,
			MapName = request.MapName,
			Refusal = "this lobby has no map to serve"
		};

		_transport.Send(peerId, NetworkFrameKind.MapTransferReply, MemoryPackSerializer.Serialize(reply));
	}

	private void HandleConsoleCommand(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		ScriptConsoleCommand command;
		try
		{
			command = MemoryPackSerializer.Deserialize<ScriptConsoleCommand>(payload);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a console command that could not be read: {exception.Message}");
			return;
		}

		simulation.ApplyConsoleCommand(peerId, command);
	}
}
