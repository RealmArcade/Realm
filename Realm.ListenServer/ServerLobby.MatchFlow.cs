using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Players;
using Realm.Shared.Services;

namespace Realm.ListenServer;

/// <summary>cancelling a countdown, load reports, stability and chat.</summary>
public partial class ServerLobby
	{
	private void CancelCountdown(LobbySeat sender)
	{
		if (!_countdownActive)
		{
			BroadcastState();
			return;
		}

		_countdownActive = false;
		_lastCountdownSeconds = 0;
		AddSystemChat($"{sender.Name} cancelled the countdown.");
		BroadcastState();
	}

	private void SetSpectatorDelay(bool enabled)
	{
		if (SpectatorDelay == enabled) return;
		SpectatorDelay = enabled;
		BroadcastState();
	}

	private void ReportMapReady(LobbySeat sender, bool ready)
	{
		if (sender.IsComputer) return;
		if (sender.IsMapReady == ready) return;

		sender.IsMapReady = ready;
		sender.MapDownloadPercent = ready ? 100 : 0;
		if (!ready) sender.IsLoadReady = false;
		_seats[sender.Slot] = sender;
		_log.Write($"lobby seat {sender.Slot} ('{sender.Name}') {(ready ? "holds" : "does not hold")} the map");
		BroadcastState();
	}

	/// <summary>
	///     Records how far a seat is through fetching this lobby's map, so every screen in the lobby can say what that seat
	///     is waiting on instead of only the seat itself knowing.
	/// </summary>
	/// <param name="sender">The seat that reported its progress.</param>
	/// <param name="percent">How much of the map it holds, from 0 to 100.</param>
	private void ReportMapProgress(LobbySeat sender, int percent)
	{
		if (sender.IsComputer) return;

		int clamped = Math.Clamp(percent, 0, 100);
		if (sender.IsMapReady) clamped = 100;
		if (sender.MapDownloadPercent == clamped) return;

		sender.MapDownloadPercent = clamped;
		_seats[sender.Slot] = sender;
		BroadcastState();
	}

	private void ReportLoadReady(LobbySeat sender, bool ready)
	{
		if (sender.IsComputer) return;
		if (sender.IsLoadReady == ready) return;
		if (ready && !sender.IsMapReady) return;

		sender.IsLoadReady = ready;
		_seats[sender.Slot] = sender;
		_log.Write($"lobby seat {sender.Slot} ('{sender.Name}') {(ready ? "finished" : "has not finished")} preparing the match");
		BroadcastState();
	}

	private void ReportHostStability(string? stability)
	{
		string value = LobbyLimits.Sanitize(stability, 32);
		if (value.Length == 0 || string.Equals(value, HostStability, StringComparison.Ordinal)) return;

		HostStability = value;
		BroadcastState();
	}

	private void RelayChat(LobbySeat sender, string? message)
	{
		string text = LobbyLimits.Sanitize(message, LobbyLimits.MaximumChatMessageLength);
		if (text.Length == 0) return;

		var line = new LobbyChatMessage
		{
			SenderPeerId = sender.PeerId,
			SenderAccountId = sender.AccountId,
			SenderName = sender.Name,
			Message = text,
			TimestampUtcTicks = DateTime.UtcNow.Ticks
		};

		_chatHistory.Add(line);
		if (_chatHistory.Count > MaximumChatHistory) _chatHistory.RemoveAt(0);

		byte[] payload = MemoryPackSerializer.Serialize(line);
		foreach (var pair in _slotByPeer)
		{
			_send(pair.Key, NetworkFrameKind.LobbyChat, payload, FrameDelivery.Reliable);
		}
	}

	private void AddSystemChat(string message)
	{
		if (string.IsNullOrWhiteSpace(message)) return;

		var line = new LobbyChatMessage
		{
			SenderPeerId = 0,
			SenderName = "System",
			Message = LobbyLimits.Sanitize(message, LobbyLimits.MaximumChatMessageLength),
			TimestampUtcTicks = DateTime.UtcNow.Ticks
		};

		_chatHistory.Add(line);
		if (_chatHistory.Count > MaximumChatHistory) _chatHistory.RemoveAt(0);

		byte[] payload = MemoryPackSerializer.Serialize(line);
		foreach (var pair in _slotByPeer)
		{
			_send(pair.Key, NetworkFrameKind.LobbyChat, payload, FrameDelivery.Reliable);
		}
	}

	private void SendSystemLineTo(int slot, string message)
	{
		if (!_seats.TryGetValue(slot, out LobbySeat seat)) return;
		if (seat.PeerId <= 0) return;

		var line = new LobbyChatMessage
		{
			SenderPeerId = 0,
			SenderName = "System",
			Message = LobbyLimits.Sanitize(message, LobbyLimits.MaximumChatMessageLength),
			TimestampUtcTicks = DateTime.UtcNow.Ticks
		};

		_send(seat.PeerId, NetworkFrameKind.LobbyChat, MemoryPackSerializer.Serialize(line), FrameDelivery.Reliable);
	}
}
