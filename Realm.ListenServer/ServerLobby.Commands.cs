using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Ecs;
using Realm.Shared.Players;
using Realm.Shared.Services;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

/// <summary>lobby commands from a seated peer.</summary>
public partial class ServerLobby
	{
	public void HandleCommand(int peerId, LobbyCommand command)
	{
		if (_matchStarted) return;
		if (!TryGetSeat(peerId, out LobbySeat sender)) return;

		bool isAdmin = peerId == _adminPeerId;

		switch (command.Kind)
		{
			case LobbyCommandKind.SetReady:
				SetReady(sender, command.Flag);
				break;
			case LobbyCommandKind.UpdateSeat:
				UpdateSeat(peerId, isAdmin, sender, command);
				break;
			case LobbyCommandKind.AddComputer:
				if (!RequireAdmin(peerId, isAdmin, out int adminSlot, "add a computer player")) return;
				AddComputer(adminSlot);
				break;
			case LobbyCommandKind.Kick:
				if (!RequireAdmin(peerId, isAdmin, out _, "remove a player")) return;
				Kick(command.TargetPeerId);
				break;
			case LobbyCommandKind.StartCountdown:
				if (!RequireAdmin(peerId, isAdmin, out int starterSlot, "start the match")) return;
				if (!command.AcknowledgeStartWarning && TryDescribeStartWarning(out string warning))
				{
					SendStartWarning(peerId, warning);
					return;
				}

				StartCountdown(starterSlot, command.Flag);
				break;
			case LobbyCommandKind.CancelCountdown:
				CancelCountdown(sender);
				break;
			case LobbyCommandKind.SetSpectatorDelay:
				if (!RequireAdmin(peerId, isAdmin, out _, "change the spectator delay")) return;
				SetSpectatorDelay(command.Flag);
				break;
			case LobbyCommandKind.SetComputerDifficulty:
				if (!RequireAdmin(peerId, isAdmin, out _, "change a computer player's difficulty")) return;
				SetComputerDifficulty(command.TargetPeerId, command.Progress);
				break;
			case LobbyCommandKind.SetMatchOption:
				if (!RequireAdmin(peerId, isAdmin, out _, "change the match settings")) return;
				if (!Enum.IsDefined(typeof(MatchOption), command.Option)) return;
				TrySetMatchOption((MatchOption)command.Option, command.OptionValue);
				break;
			case LobbyCommandKind.ReportMapReady:
				ReportMapReady(sender, command.Flag);
				break;
			case LobbyCommandKind.ReportMapProgress:
				ReportMapProgress(sender, command.Progress);
				break;
			case LobbyCommandKind.ReportLoadReady:
				ReportLoadReady(sender, command.Flag);
				break;
			case LobbyCommandKind.Chat:
				RelayChat(sender, command.Text);
				break;
			case LobbyCommandKind.Leave:
				RemoveSeat(peerId, $"{sender.Name} left the lobby.");
				break;
			case LobbyCommandKind.ReportHostStability:
				if (!isAdmin) return;
				ReportHostStability(command.Text);
				break;
			case LobbyCommandKind.PunchAddress:
				if (!isAdmin) return;
				if (string.IsNullOrWhiteSpace(command.Address) || command.Port <= 0 || command.Port > 65535) return;
				_punchAddress(LobbyLimits.Sanitize(command.Address, 64), command.Port);
				break;
			case LobbyCommandKind.RequestChatHistory:
				foreach (LobbyChatMessage line in _chatHistory)
				{
					_send(peerId, NetworkFrameKind.LobbyChat, MemoryPackSerializer.Serialize(line), FrameDelivery.Reliable);
				}
				break;
			default:
				_log.Write($"peer {peerId} sent a lobby command {(byte)command.Kind} this build does not accept");
				break;
		}
	}
}
