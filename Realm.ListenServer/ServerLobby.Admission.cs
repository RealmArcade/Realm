using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Players;
using Realm.Shared.Services;

namespace Realm.ListenServer;

/// <summary>admitting a peer and the session checks that guard it.</summary>
public partial class ServerLobby
	{
	public string AdmitPeer(int peerId, string name, long accountId, string sessionToken, bool proveAdmin, out bool isAdmin)
	{
		isAdmin = false;
		if (_matchStarted) return "server_rejected_lobby_is_closed";

		if (!proveAdmin && !string.IsNullOrEmpty(sessionToken) && _refusedSessionTokens.Contains(sessionToken))
		{
			_log.Write($"peer {peerId} asked to sit in a lobby that removed its session; the connection is refused");
			return LobbyLimits.KickedRejection;
		}

		if (proveAdmin && _adminPeerId == 0)
		{
			_adminPeerId = peerId;
			isAdmin = true;
			_log.Write($"peer {peerId} administers the lobby: it proved the ticket this authority was started with");
		}

		LobbySeat seat = default;
		bool restored = false;

		if (!string.IsNullOrEmpty(sessionToken))
		{
			string sanitizedSessionToken = LobbyLimits.Sanitize(sessionToken, MaximumSessionTokenLength);

			// The seat secret is minted on the seat's own client and every other peer's copy of it is blanked before the
			// lobby state goes out, so presenting it is what proves the seat belongs to this peer. A seat a live connection
			// still holds is taken back as well, because a reconnect always arrives as a new peer id and can outrun the
			// authority noticing the old socket is gone; seating that peer afresh would cost it its slot, team and colour,
			// or refuse it outright when the lobby has no playing slot left.
			restored = TryFindSeatBySessionToken(sanitizedSessionToken, requireDisconnected: true, out seat) ||
			           TryFindSeatBySessionToken(sanitizedSessionToken, requireDisconnected: false, out seat);

			if (restored)
			{
				int previousPeerId = seat.PeerId;
				bool previousPeerStood = !seat.IsDisconnected;
				_slotByPeer.Remove(previousPeerId);
				if (previousPeerStood && previousPeerId != peerId) _disconnect(previousPeerId);
			}
		}

		if (!restored)
		{
			int slot = FindFreePlayingSlot(isAdmin ? 1 : 0);
			bool playingSeat = slot > 0;
			if (!playingSeat) slot = FindFreeSpectatorSlot();
			if (slot <= 0) return "server_rejected_no_free_slot";

			seat = new LobbySeat
			{
				Slot = slot,
				AccountId = accountId,
				Name = DefaultName(name, slot, isAdmin),
				Faction = Roster?.GetFactionName(0) ?? string.Empty,
				TeamName = playingSeat ? ResolveJoinTeamName() : LobbyLimits.SpectatorTeamName,
				ColorRgba = ResolveJoinColorRgba(slot),
				IsHost = isAdmin,
				LatencyMilliseconds = -1,
				JitterMilliseconds = -1,
				PacketLossPercent = -1f,
				SessionToken = LobbyLimits.Sanitize(sessionToken, MaximumSessionTokenLength)
			};
			seat.Team = ResolveTeamNumber(seat.TeamName, slot);
		}

		if (restored && proveAdmin && seat.IsHost) _adminPeerId = peerId;

		seat.PeerId = peerId;
		seat.AccountId = accountId;
		seat.IsHost = isAdmin || peerId == _adminPeerId;
		seat.IsDisconnected = false;
		if (!string.IsNullOrWhiteSpace(name)) seat.Name = LobbyLimits.SanitizeDisplayName(name);
		if (string.IsNullOrEmpty(seat.Name)) seat.Name = DefaultName(name, seat.Slot, seat.IsHost);
		seat.Name = MakeNameDistinct(seat.Name, seat.Slot);

		_seats[seat.Slot] = seat;
		_slotByPeer[peerId] = seat.Slot;

		if (restored) _log.Write($"peer {peerId} took back lobby seat {seat.Slot} as '{seat.Name}'");
		else _log.Write($"peer {peerId} admitted to the lobby as seat {seat.Slot} ('{seat.Name}'), {(seat.IsHost ? "administrator" : "member")}");

		AddSystemChat($"{seat.Name} joined the lobby.");
		BroadcastState();
		return string.Empty;
	}

	/// <summary>
	///     Whether the session a dropped seat was held by is already seated on a live connection, which is a reconnect that
	///     arrived before the authority noticed the old socket was gone. The dropped seat is then gone rather than held,
	///     so one player is never counted twice.
	/// </summary>
	/// <param name="dropped">The seat whose connection just dropped.</param>
	/// <returns>Whether another connected seat holds the same session.</returns>
	private bool IsSessionSeatedOnAnotherConnection(in LobbySeat dropped)
	{
		if (dropped.SessionToken.Length == 0) return false;

		foreach (var pair in _seats)
		{
			if (pair.Value.Slot == dropped.Slot) continue;
			if (pair.Value.IsDisconnected) continue;
			if (!string.Equals(pair.Value.SessionToken, dropped.SessionToken, StringComparison.Ordinal)) continue;

			return true;
		}

		return false;
	}

	private bool TryFindSeatBySessionToken(string sessionToken, bool requireDisconnected, out LobbySeat seat)
	{
		foreach (var pair in _seats)
		{
			if (pair.Value.IsComputer) continue;
			if (requireDisconnected && !pair.Value.IsDisconnected) continue;
			if (!string.Equals(pair.Value.SessionToken, sessionToken, StringComparison.Ordinal)) continue;

			seat = pair.Value;
			return true;
		}

		seat = default;
		return false;
	}
}
