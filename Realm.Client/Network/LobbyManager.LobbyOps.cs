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
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.Shared.Network;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;
using HttpClient = System.Net.Http.HttpClient;

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
	///     Sets one parameter the host configures for the match. Zero returns an option to the map's own default, which is
	///     how the standard lobby states all of them.
	/// </summary>
	/// <param name="matchOption">The option being configured.</param>
	/// <param name="value">The value to play, or zero for the map's default.</param>
	public void SetMatchOption(MatchOption matchOption, int value)
	{
		if (!IsHost) return;

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.SetMatchOption,
			Option = (int)matchOption,
			OptionValue = value
		});
	}

	public void StartGame(string mapName, bool ignoreReadiness = false, bool acknowledgeStartWarning = false)
	{
		if (!IsHost) return;

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

	public bool AddAIBot()
	{
		if (!IsHost) return false;
		if (PlayingPlayerCount >= MaxPlayers)
		{
			LastLobbyWarning = string.Format(Tr("Cannot add an AI player: all {0} playable slots are taken."), MaxPlayers);
			return false;
		}

		_lobbySession?.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.AddComputer });
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

		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.Chat,
			Text = message
		});
	}

	public bool HasRegisteredSessionToken(int peerId)
	{
		PlayerInfo? player = PlayerList.Find(p => p.PeerId == peerId);
		return player != null && !string.IsNullOrEmpty(player.SessionToken);
	}

	public bool TryGetReconnectOwnerForHarness(string sessionToken, out int slot)
	{
		slot = -1;
		if (string.IsNullOrEmpty(sessionToken)) return false;

		PlayerInfo? owner = PlayerList.Find(p => !string.IsNullOrEmpty(p.SessionToken) && string.Equals(p.SessionToken, sessionToken, StringComparison.Ordinal));
		if (owner == null) return false;

		slot = owner.Slot;
		return true;
	}

}
