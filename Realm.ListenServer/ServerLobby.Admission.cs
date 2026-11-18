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
	/// <summary>
	///     Admits a peer to the lobby. A lobby that gates its seats with a join code admits the member to the room without a
	///     seat, which is what makes the code a seating gate rather than a door: the player sees the lobby and types the code
	///     in it. Everything else is seated here and now.
	/// </summary>
	/// <param name="peerId">The connection the peer arrived on.</param>
	/// <param name="name">The name the peer plays under.</param>
	/// <param name="steamId">The Steam identity Steam asserted for the peer, which a reconnect presents again.</param>
	/// <param name="proveAdmin">Whether the peer proved the ticket that administers this lobby.</param>
	/// <param name="isAdmin">Whether the peer administers the lobby, which only the admitted peer can be.</param>
	/// <returns>An empty string when the peer stands in the lobby, or the refusal it is answered with.</returns>
	public string AdmitPeer(int peerId, string name, ulong steamId, bool proveAdmin, out bool isAdmin)
	{
		isAdmin = false;
		if (_matchStarted) return "server_rejected_lobby_is_closed";

		// A removed player's Steam lobby membership outlives the kick, so the refusal has to be by the identity Steam
		// asserted rather than by whatever the screen states after it reconnects.
		if (!proveAdmin && steamId != 0 && _refusedSteamIds.Contains(steamId))
		{
			_log.Write($"peer {peerId} asked to sit in a lobby that removed its Steam identity; the connection is refused");
			return LobbyLimits.KickedRejection;
		}

		if (proveAdmin && _adminPeerId == 0)
		{
			_adminPeerId = peerId;
			isAdmin = true;
			_log.Write($"peer {peerId} administers the lobby: it proved the ticket this authority was started with");
		}

		if (WaitsForJoinCode(steamId, proveAdmin))
		{
			WaitForJoinCode(peerId, name, steamId);
			BroadcastState();
			return string.Empty;
		}

		return SeatPeer(peerId, name, steamId, proveAdmin, out isAdmin);
	}

	/// <summary>Gives a peer the seat it asked for, and announces it to the room.</summary>
	/// <param name="peerId">The connection the peer arrived on.</param>
	/// <param name="name">The name the peer plays under.</param>
	/// <param name="steamId">The Steam identity Steam asserted for the peer, which a reconnect presents again.</param>
	/// <param name="proveAdmin">Whether the peer proved the ticket that administers this lobby.</param>
	/// <param name="isAdmin">Whether the seat administers the lobby.</param>
	/// <returns>An empty string when the seat was taken, or the refusal it is answered with.</returns>
	private string SeatPeer(int peerId, string name, ulong steamId, bool proveAdmin, out bool isAdmin)
	{
		isAdmin = peerId == _adminPeerId;

		LobbySeat seat = default;
		bool restored = false;

		if (steamId != 0)
		{

			// The seat secret is minted on the seat's own client and every other peer's copy of it is blanked before the
			// lobby state goes out, so presenting it is what proves the seat belongs to this peer. A seat a live connection
			// still holds is taken back as well, because a reconnect always arrives as a new peer id and can outrun the
			// authority noticing the old socket is gone; seating that peer afresh would cost it its slot, team and colour,
			// or refuse it outright when the lobby has no playing slot left.
			restored = TryFindSeatBySteamId(steamId, requireDisconnected: true, out seat) ||
			           TryFindSeatBySteamId(steamId, requireDisconnected: false, out seat);

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
			if (slot <= 0) return "server_rejected_no_free_slot";

			seat = new LobbySeat
			{
				Slot = slot,
				Name = DefaultName(name, slot, isAdmin),
				Faction = Roster?.GetFactionName(0) ?? string.Empty,
				TeamName = ResolveJoinTeamName(),
				ColorRgba = ResolveJoinColorRgba(slot),
				IsHost = isAdmin,
				LatencyMilliseconds = -1,
				JitterMilliseconds = -1,
				PacketLossPercent = -1f
			};
			seat.Team = ResolveTeamNumber(seat.TeamName, slot);
		}

		if (restored && proveAdmin && seat.IsHost) _adminPeerId = peerId;

		seat.PeerId = peerId;

		// Stated by the seat's own client, like the name it plays under. It is published to the other seats so the table at
		// the end of the match can name the person behind it to Steam, and a seat that reconnects states it again.
		seat.SteamId = steamId;
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
	///     Whether the Steam identity a dropped seat was held by is already seated on a live connection, which is a
	///     reconnect that arrived before the authority noticed the old socket was gone. The dropped seat is then gone
	///     rather than held, so one player is never counted twice.
	/// </summary>
	/// <param name="dropped">The seat whose connection just dropped.</param>
	/// <returns>Whether another connected seat holds the same Steam identity.</returns>
	private bool IsSteamIdSeatedOnAnotherConnection(in LobbySeat dropped)
	{
		if (dropped.SteamId == 0) return false;

		foreach (var pair in _seats)
		{
			if (pair.Value.Slot == dropped.Slot) continue;
			if (pair.Value.IsDisconnected) continue;
			if (pair.Value.SteamId != dropped.SteamId) continue;

			return true;
		}

		return false;
	}

	/// <summary>
	///     Finds the seat a Steam identity already holds, which is how a dropped player's slot, team and colour survive
	///     the connection they were lost with.
	/// </summary>
	/// <param name="steamId">The Steam identity Steam asserted for the arriving peer.</param>
	/// <param name="requireDisconnected">Whether only a seat whose connection is already gone may match.</param>
	/// <param name="seat">The seat that identity holds.</param>
	/// <returns>Whether the identity was already seated.</returns>
	private bool TryFindSeatBySteamId(ulong steamId, bool requireDisconnected, out LobbySeat seat)
	{
		foreach (var pair in _seats)
		{
			if (pair.Value.IsComputer) continue;
			if (requireDisconnected && !pair.Value.IsDisconnected) continue;
			if (pair.Value.SteamId != steamId) continue;

			seat = pair.Value;
			return true;
		}

		seat = default;
		return false;
	}
}
