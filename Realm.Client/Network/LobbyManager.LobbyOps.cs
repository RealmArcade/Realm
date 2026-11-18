using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.Shared.Network;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;

namespace Realm.Client.Network;
public partial class LobbyManager
{
	public bool IsLocalPlayer(PlayerInfo player) => player.PeerId == LocalPeerId && player.PeerId > 0;

	public void UpdateReadyState(int peerId, bool isReady)
	{
		PlayerInfo? player = PlayerList.Find(p => p.PeerId == peerId);
		if (player != null) player.IsReady = isReady;

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.SetReady,
			TargetPeerId = peerId,
			Flag = isReady
		});
	}

	private void SendSeatUpdate(PlayerInfo player)
	{
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.UpdateSeat,
			TargetPeerId = player.PeerId,
			Name = player.Name,
			Faction = player.Faction,
			TeamName = player.Team,
			ColorRgba = PackColor(player.Color)
		});
	}

	public void UpdatePlayerSlot(int peerId, string faction, string team, Color color, string name)
	{
		PlayerInfo? player = PlayerList.Find(p => p.PeerId == peerId);
		if (player != null)
		{
			player.Faction = faction;
			player.Team = team;
			player.Color = color;
			player.Name = name;
		}

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.UpdateSeat,
			TargetPeerId = peerId,
			Name = name,
			Faction = faction,
			TeamName = team,
			ColorRgba = PackColor(color)
		});
	}

	public void ReportLocalMapReadyState(bool isMapReady)
	{
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.ReportMapReady,
			Flag = isMapReady
		});
	}

	public void ReportLocalMapProgress(int percent)
	{
		if (IsHost) return;

		int clamped = Math.Clamp(percent, 0, 100);
		if (clamped == _reportedMapProgressPercent) return;

		_reportedMapProgressPercent = clamped;
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.ReportMapProgress,
			Progress = clamped
		});
	}

	public void UpdateSpectatorDelay(bool enabled)
	{
		if (!IsHost) return;

		SpectatorDelay = enabled;
		SpectatorDelayChanged?.Invoke(enabled);
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.SetSpectatorDelay,
			Flag = enabled
		});
	}

	/// <summary>
	///     Asks the lobby to group each team's players into consecutive player slots when the match starts. Only the host
	///     may change it, and the authority's answer is what every seat reads back.
	/// </summary>
	/// <param name="enabled">Whether the match should group each team's players.</param>
	public void UpdateTeamsTogether(bool enabled)
	{
		if (!IsHost) return;

		TeamsTogether = enabled;
		TeamsTogetherChanged?.Invoke(enabled);
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.SetTeamsTogether,
			Flag = enabled
		});
	}

	/// <summary>
	///     Sets one of the match settings the lobby's map declared. Only the host may change one, and the authority's answer
	///     is what every seat reads back.
	/// </summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="value">The value to play.</param>
	public void SetLobbySetting(int settingId, int value)
	{
		if (!IsHost) return;

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.SetLobbySetting,
			SettingId = settingId,
			SettingValue = value
		});
	}

	/// <summary>
	///     Chooses the shroud mode the next match plays. Only the host may change it, and the authority's answer is what
	///     every seat reads back.
	/// </summary>
	/// <param name="shroudMode">The mode the match should play.</param>
	public void UpdateShroudMode(MatchShroudMode shroudMode)
	{
		if (!IsHost) return;

		ShroudMode = shroudMode;
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.SetShroudMode,
			Progress = (int)shroudMode
		});
	}

	/// <summary>
	///     Opens or closes one player slot, which is how the host decides which seats the board offers. Only the host may do
	///     it, and the authority's answer is what every seat reads back.
	/// </summary>
	/// <param name="slot">The player slot.</param>
	/// <param name="open">Whether the slot should accept a player.</param>
	public void SetSlotOpen(int slot, bool open)
	{
		if (!IsHost) return;

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.SetSlotOpen,
			Slot = slot,
			Flag = open
		});
	}

	public void StartGame(string mapName, bool ignoreReadiness = false, bool acknowledgeStartWarning = false)
	{
		if (!IsHost) return;

		// The lobby closes when the match starts. There is no join-in-progress here, so a member arriving after the start
		// would stand in a room whose match they cannot enter; Steam stops admitting them at the same moment the countdown
		// begins.
		if (SteamLobbyId != 0) SteamLobbyDirectory.SetJoinable(SteamLobbyId, false);

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.StartCountdown,
			Text = mapName,
			Flag = ignoreReadiness,
			AcknowledgeStartWarning = acknowledgeStartWarning
		});
	}

	public void RequestCancelCountdown()
	{
		_lobbySession?.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.CancelCountdown });
	}

	/// <summary>
	///     Seats a computer player in one player slot. Only the host may do it, the slot is opened first when the host had
	///     closed it, and the authority's answer - which refuses a slot its map keeps or somebody still holds - is what
	///     every seat reads back.
	/// </summary>
	/// <param name="slot">The player slot the computer should play in.</param>
	/// <returns>Whether the lobby was asked to seat a computer in that slot.</returns>
	public bool AddAIBot(int slot)
	{
		if (!IsHost) return false;

		if (!IsSlotOpen(slot)) SetSlotOpen(slot, true);

		_lobbySession?.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.AddComputer, Slot = slot });
		return true;
	}

	public void SetComputerDifficulty(int peerId, LobbyBotDifficulty difficulty)
	{
		if (!IsHost) return;

		PlayerInfo? player = PlayerList.Find(p => p.PeerId == peerId);
		if (player == null || !player.IsComputer) return;

		player.Difficulty = LobbyBotDifficultyRules.Clamp((int)difficulty);
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.SetComputerDifficulty,
			TargetPeerId = peerId,
			Progress = (int)player.Difficulty
		});
	}

	public void BootPlayer(int peerId)
	{
		if (!IsHost || peerId == LocalPeerId) return;

		bool sent = _lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.Kick,
			TargetPeerId = peerId
		}) == true;

		if (!sent) GD.PushWarning($"[LobbyManager] The lobby session did not carry a kick for peer {peerId}.");
	}

	public void RequestChatHistory()
	{
		_lobbySession?.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.RequestChatHistory });
	}

	public void ReceiveChatFromAuthority(string senderName, string message, bool alliesOnly, long timestampUtcTicks)
	{
		ChatReceived?.Invoke(LobbyLimits.SanitizeDisplayName(senderName), message, alliesOnly, timestampUtcTicks);
	}

	public void SendChatMessage(string senderName, string message, bool alliesOnly = false, long timestampUtcTicks = 0)
	{
		long sentAtUtcTicks = timestampUtcTicks > 0 ? timestampUtcTicks : DateTime.UtcNow.Ticks;

		ClientServerSession? matchSession = GameContext.ServerSession;
		if (matchSession != null && matchSession.IsRunning && IsGameStarted && senderName != "System")
		{
			if (matchSession.SendChat(message, alliesOnly)) return;
		}

		if (senderName == "System") return;

		// The lobby's own chat carries the line whenever the lobby exists, because it is what keeps the history and the
		// team-only channel. Steam's lobby chat is the path that works before that connection does, which is the window
		// between entering the Steam lobby and being admitted to a seat.
		if (_lobbySession == null)
		{
			if (SteamLobbyId != 0) SteamLobbyDirectory.SendChat(message);
			return;
		}

		_lobbySession.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.Chat,
			Text = message
		});
	}

	/// <summary>Whether a seat in this lobby was taken by a player Steam identified.</summary>
	/// <param name="peerId">The connection to ask about.</param>
	/// <returns>True when the peer holds an identified seat.</returns>
	public bool HasIdentifiedSeat(int peerId)
	{
		PlayerInfo? player = PlayerList.Find(p => p.PeerId == peerId);
		return player != null && player.SteamId != 0;
	}

	/// <summary>Finds the slot a Steam identity already holds, which is how a harness watches a reconnect.</summary>
	/// <param name="steamId">The Steam identity to look for.</param>
	/// <param name="slot">The slot that identity holds.</param>
	/// <returns>Whether the identity was already seated.</returns>
	public bool TryGetReconnectOwnerForHarness(ulong steamId, out int slot)
	{
		slot = -1;
		if (steamId == 0) return false;

		PlayerInfo? owner = PlayerList.Find(p => p.SteamId == steamId);
		if (owner == null) return false;

		slot = owner.Slot;
		return true;
	}

}
