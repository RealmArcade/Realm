using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Players;
using Realm.Shared.Services;

namespace Realm.ListenServer;

/// <summary>computer seats, difficulty, kicks and seat removal.</summary>
public partial class ServerLobby
	{
	private void AddComputer(int adminSlot)
	{
		if (PlayingSeatCount >= _maximumPlayers)
		{
			AddSystemChat($"Cannot add an AI player: all {_maximumPlayers} player seats are taken.");
			return;
		}

		int slot = FindFreePlayingSlot(0);
		if (slot <= 0)
		{
			AddSystemChat($"Cannot add an AI player: all {_maximumPlayers} player seats are taken.");
			return;
		}

		var computer = new LobbySeat
		{
			PeerId = -100 - _nextComputerNumber,
			Slot = slot,
			Name = $"AI Bot {_nextComputerNumber}",
			Faction = Roster?.GetFactionName(0) ?? string.Empty,
			TeamName = ResolveJoinTeamName(),
			ColorRgba = ResolveJoinColorRgba(slot),
			IsComputer = true,
			Difficulty = (int)LobbyBotDifficultyRules.Default,
			IsMapReady = true,
			IsLoadReady = true,
			MapDownloadPercent = 100,
			LatencyMilliseconds = 0,
			JitterMilliseconds = 0,
			PacketLossPercent = 0f
		};
		computer.Team = ResolveTeamNumber(computer.TeamName, slot);

		_nextComputerNumber++;
		_seats[slot] = computer;
		_log.Write($"lobby seat {slot} is a computer player ('{computer.Name}')");

		AddSystemChat($"{computer.Name} added to the lobby.");
		BroadcastState();
	}

	private void SetComputerDifficulty(int targetPeerId, int difficulty)
	{
		if (!TryGetComputerSeat(targetPeerId, out LobbySeat computer)) return;

		LobbyBotDifficulty resolved = LobbyBotDifficultyRules.Clamp(difficulty);
		if (LobbyBotDifficultyRules.Clamp(computer.Difficulty) == resolved) return;

		computer.Difficulty = (int)resolved;
		_seats[computer.Slot] = computer;
		_log.Write($"lobby seat {computer.Slot} ('{computer.Name}') now plays on the '{resolved}' difficulty");
		BroadcastState();
	}

	private void Kick(int targetPeerId)
	{
		if (targetPeerId <= 0)
		{
			if (!TryGetComputerSeat(targetPeerId, out LobbySeat computer)) return;
			RemoveSeat(computer.PeerId, $"{computer.Name} removed from the lobby.");
			return;
		}

		if (targetPeerId == _adminPeerId) return;
		if (!TryFindSeat(targetPeerId, out LobbySeat target))
		{
			_log.Write($"the administrator asked to remove peer {targetPeerId}, which holds no seat in this lobby");
			return;
		}

		_log.Write($"peer {targetPeerId} ('{target.Name}') was removed from the lobby by its administrator");
		if (!string.IsNullOrEmpty(target.SessionToken)) _refusedSessionTokens.Add(target.SessionToken);
		bool stillConnected = _slotByPeer.ContainsKey(target.PeerId);
		RemoveSeat(target.PeerId, $"{target.Name} was removed from the lobby.");

		if (!stillConnected) return;

		var kicked = new LobbyKickedMessage { Reason = "Kicked by Host" };
		_send(target.PeerId, NetworkFrameKind.LobbyKicked, MemoryPackSerializer.Serialize(kicked), FrameDelivery.Reliable);
		_disconnect(target.PeerId);
	}

	private void RemoveSeat(int peerId, string message)
	{
		if (!TryFindSeat(peerId, out LobbySeat seat)) return;

		_seats.Remove(seat.Slot);
		_slotByPeer.Remove(peerId);
		_log.Write($"lobby seat {seat.Slot} ('{seat.Name}') is gone");

		if (peerId == _adminPeerId)
		{
			AddSystemChat(message);
			_lobbyAbandoned();
			return;
		}

		AddSystemChat(message);
		BroadcastState();
	}

	/// <summary>
	///     Asks the lobby's map whether the host must read a warning before the match starts, which is how a map stops a
	///     start its seats make unfair. A map that declines or answers nothing lets the start through.
	/// </summary>
	/// <param name="warning">The text the host has to accept, or empty when the map has nothing to warn about.</param>
	/// <returns>Whether the host must accept a warning first.</returns>
}
