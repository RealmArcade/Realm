using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Players;
using Realm.Shared.Services;
using Realm.Shared.Simulation;

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

		CopyColorOverlays(out uint[] colorOverlays, out uint[] minimapPlayerColors, out uint[] minimapTeamColors);

		return new LobbyStateMessage
		{
			Seats = seats,
			MapName = _mapName,
			MapVersion = _mapVersion,
			SlotCount = SlotCount,
			OpenSlots = CopyOpenSlots(),
			Factions = Roster?.GetFactionNames() ?? [],
			TeamCount = Roster?.TeamCount ?? LobbyLimits.DefaultTeamCount,
			SpectatorDelay = SpectatorDelay,
			TeamsTogether = Roster?.TeamsTogether ?? false,
			ShroudMode = (int)EffectiveShroudMode,
			HostStability = HostStability,
			CountdownActive = _countdownActive,
			CountdownRemainingSeconds = _countdownActive ? Math.Max(0, _lastCountdownSeconds) : 0,
			Settings = DescribeMatchSettings(),
			ColorOverlays = colorOverlays,
			MinimapPlayerColors = minimapPlayerColors,
			MinimapTeamColors = minimapTeamColors,
			ProxyOwners = CopyProxyOwners(),
			Handover = DescribeHandoverState(),
			ProbeRelayPort = ProbeRelayPort,
			Revision = ++_revision
		};
	}

	/// <summary>Copies which player slots this lobby leaves open, indexed by slot.</summary>
	/// <returns>One entry per player slot, where true means the slot accepts a player.</returns>
	private bool[] CopyOpenSlots()
	{
		var openSlots = new bool[_openSlots.Length];
		Array.Copy(_openSlots, openSlots, _openSlots.Length);
		return openSlots;
	}

	/// <summary>Describes the match settings the lobby's map declared, which every seat reads to show them.</summary>
	/// <returns>One entry per declared setting, in the order their identifiers name them.</returns>
	private LobbyMatchSettingMessage[] DescribeMatchSettings()
	{
		if (_matchSettings.Count == 0) return Array.Empty<LobbyMatchSettingMessage>();

		var settings = new LobbyMatchSettingMessage[_matchSettings.Count];
		int index = 0;
		foreach (LobbyMatchSetting setting in _matchSettings.Values)
		{
			settings[index++] = new LobbyMatchSettingMessage
			{
				SettingId = setting.SettingId,
				Label = setting.Label,
				Tooltip = setting.Tooltip,
				Control = (int)setting.Control,
				Value = setting.Value,
				Minimum = setting.Minimum,
				Maximum = setting.Maximum,
				Options = setting.Options
			};
		}

		return settings;
	}

	/// <summary>
	///     Whether a player who joins while the match is running watches for a delay before the board is described to them.
	///     It starts at the core game's answer - on - and follows the map's own default and then the host from the moment
	///     either states one.
	/// </summary>
	public bool SpectatorDelay { get; private set; } = true;

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
