using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Players;
using Realm.Shared.Services;

namespace Realm.ListenServer;

/// <summary>peers leaving and the lobby tick.</summary>
public partial class ServerLobby
	{
	public void OnPeerDisconnected(int peerId)
	{
		if (_matchStarted) return;
		if (!TryGetSeat(peerId, out LobbySeat seat)) return;

		if (peerId == _adminPeerId)
		{
			_log.Write("the peer administering the lobby is gone; the lobby is being destroyed");
			_lobbyAbandoned();
			return;
		}

		if (IsSessionSeatedOnAnotherConnection(seat))
		{
			_seats.Remove(seat.Slot);
			_slotByPeer.Remove(peerId);
			_log.Write($"lobby seat {seat.Slot} ('{seat.Name}') is gone: its session is already seated on another connection");
			BroadcastState();
			return;
		}

		seat.IsDisconnected = true;
		seat.IsReady = false;
		seat.IsMapReady = false;
		seat.IsLoadReady = false;
		_seats[seat.Slot] = seat;
		_slotByPeer.Remove(peerId);
		_log.Write($"lobby seat {seat.Slot} ('{seat.Name}') dropped and is held for its session identity");

		AddSystemChat($"{seat.Name} disconnected. Waiting for reconnect...");
		BroadcastState();
	}

	public void Tick(double deltaSeconds)
	{
		if (!_matchStarted) RefreshLinkStatistics(deltaSeconds);

		if (_matchStarted || !_countdownActive) return;

		_countdownRemainingSeconds -= Math.Max(0.0, deltaSeconds);
		int remaining = (int)Math.Ceiling(_countdownRemainingSeconds);
		if (remaining < 0) remaining = 0;
		if (remaining != _lastCountdownSeconds)
		{
			_lastCountdownSeconds = remaining;
			BroadcastState();
		}

		if (_countdownRemainingSeconds > 0) return;

		string? rejection = StartMatch();
		if (rejection == null) return;

		_countdownActive = false;
		AddSystemChat(rejection);
		BroadcastState();
	}
}
