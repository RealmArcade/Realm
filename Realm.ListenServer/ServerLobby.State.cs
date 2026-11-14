using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Players;
using Realm.Shared.Services;

namespace Realm.ListenServer;

/// <summary>describing the lobby state and publishing link statistics.</summary>
public partial class ServerLobby
	{
	public void SendStateTo(int peerId)
	{
		if (!TryGetSeat(peerId, out _)) return;

		_send(peerId, NetworkFrameKind.LobbyState, SerializeStateFor(DescribeState(), peerId), FrameDelivery.Reliable);
		foreach (LobbyChatMessage line in _chatHistory)
		{
			_send(peerId, NetworkFrameKind.LobbyChat, MemoryPackSerializer.Serialize(line), FrameDelivery.Reliable);
		}
	}

	public void BroadcastState()
	{
		LobbyStateMessage state = DescribeState();
		foreach (var pair in _slotByPeer)
		{
			_send(pair.Key, NetworkFrameKind.LobbyState, SerializeStateFor(state, pair.Key), FrameDelivery.Reliable);
		}
	}

	private byte[] SerializeStateFor(LobbyStateMessage state, int peerId)
	{
		LobbySeat[] seats = state.Seats;
		for (int index = 0; index < seats.Length; index++) seats[index].SessionToken = string.Empty;

		for (int index = 0; index < seats.Length; index++)
		{
			if (seats[index].PeerId != peerId) continue;
			if (_seats.TryGetValue(seats[index].Slot, out LobbySeat seat)) seats[index].SessionToken = seat.SessionToken;
			break;
		}

		return MemoryPackSerializer.Serialize(state);
	}

	private LobbyStateMessage DescribeState()
	{
		var seats = new LobbySeat[_seats.Count];
		int index = 0;
		foreach (LobbySeat seat in _seats.Values)
		{
			LobbySeat published = seat;
			published.SessionToken = string.Empty;
			seats[index++] = published;
		}

		return new LobbyStateMessage
		{
			Seats = seats,
			MapName = _mapName,
			MapVersion = _mapVersion,
			MaxPlayers = _maximumPlayers,
			Factions = Roster?.GetFactionNames() ?? [],
			TeamCount = Roster?.TeamCount ?? LobbyLimits.DefaultTeamCount,
			SpectatorDelay = SpectatorDelay,
			TeamsTogether = Roster?.TeamsTogether ?? false,
			ShroudMode = (int)EffectiveShroudMode,
			HostStability = HostStability,
			CountdownActive = _countdownActive,
			CountdownRemainingSeconds = _countdownActive ? Math.Max(0, _lastCountdownSeconds) : 0,
			Options = _matchOptions.ToValues(),
			Revision = ++_revision
		};
	}

	public bool SpectatorDelay { get; private set; }

	/// <summary>
	///     Republishes the measured link of every seat on an interval, because nothing else in a quiet lobby changes: the
	///     round trip a peer is waiting on is not an event the lobby is told about.
	/// </summary>
	private void RefreshLinkStatistics(double deltaSeconds)
	{
		_linkStatisticsTimer += Math.Max(0.0, deltaSeconds);
		if (_linkStatisticsTimer < LinkStatisticsIntervalSeconds) return;

		_linkStatisticsTimer = 0.0;
		if (!ApplyLinkStatistics()) return;

		BroadcastState();
	}

	private bool ApplyLinkStatistics()
	{
		bool changed = false;
		int measuredPeers = 0;
		long totalRoundTrip = 0;
		long totalJitter = 0;
		double totalLoss = 0.0;

		foreach (var pair in _seats)
		{
			LobbySeat seat = pair.Value;
			if (seat.IsComputer || seat.IsDisconnected) continue;
			if (seat.PeerId <= 0 || seat.PeerId == _adminPeerId) continue;

			PeerLinkStatistics? statistics = _readPeerLink(seat.PeerId);
			if (statistics == null) continue;

			measuredPeers++;
			totalRoundTrip += statistics.Value.RoundTripMilliseconds;
			totalJitter += statistics.Value.JitterMilliseconds;
			totalLoss += statistics.Value.PacketLossPercent;

			changed |= PublishSeatLink(seat, statistics.Value.RoundTripMilliseconds, statistics.Value.JitterMilliseconds, statistics.Value.PacketLossPercent);
		}

		// The administrator runs its own authority, so no transit belongs to its own row: it states the average of the links
		// it serves, and nothing at all while it serves nobody.
		foreach (var pair in _seats)
		{
			LobbySeat seat = pair.Value;
			if (seat.PeerId != _adminPeerId) continue;

			if (measuredPeers == 0)
			{
				changed |= PublishSeatLink(seat, 0, 0, 0f);
				break;
			}

			changed |= PublishSeatLink(seat,
				(int)Math.Round((double)totalRoundTrip / measuredPeers),
				(int)Math.Round((double)totalJitter / measuredPeers),
				(float)(totalLoss / measuredPeers));
			break;
		}

		return changed;
	}

	private bool PublishSeatLink(LobbySeat seat, int roundTripMilliseconds, int jitterMilliseconds, float packetLossPercent)
	{
		bool unchanged = seat.LatencyMilliseconds == roundTripMilliseconds
			&& seat.JitterMilliseconds == jitterMilliseconds
			&& MathF.Abs(seat.PacketLossPercent - packetLossPercent) < 0.5f;
		if (unchanged) return false;

		seat.LatencyMilliseconds = roundTripMilliseconds;
		seat.JitterMilliseconds = jitterMilliseconds;
		seat.PacketLossPercent = packetLossPercent;
		_seats[seat.Slot] = seat;
		return true;
	}
}
