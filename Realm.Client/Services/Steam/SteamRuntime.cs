using System;
using Godot;
using Realm.Client.Network;
using Realm.Client.UI;

namespace Realm.Client.Services;

[GlobalClass]
public partial class SteamRuntime : Node
{
	private string? _pendingInvitedLobbyId;
	private string _pendingInviterName = string.Empty;

	public override void _Ready()
	{
		GameContext.Steam.Start();
		GameContext.Steam.LobbyInviteAccepted += OnLobbyInviteAccepted;

		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		GameContext.Steam.Pump();
		JoinPendingInvite();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest || what == NotificationExitTree)
		{
			GameContext.Steam.LobbyInviteAccepted -= OnLobbyInviteAccepted;
			GameContext.Steam.Stop();
		}
	}

	private void OnLobbyInviteAccepted(SteamLobbyInvite invite)
	{
		_pendingInvitedLobbyId = invite.GameLobbyId;
		_pendingInviterName = invite.InviterName;
	}

	private void JoinPendingInvite()
	{
		if (_pendingInvitedLobbyId == null) return;

		LobbyManager? lobby = GameContext.Lobby;
		UIManager? userInterface = GameContext.UserInterface;
		if (lobby == null || userInterface == null || !userInterface.HasVisibleScreen) return;

		string invitedLobbyId = _pendingInvitedLobbyId;
		string inviterName = _pendingInviterName;
		_pendingInvitedLobbyId = null;
		_pendingInviterName = string.Empty;

		if (lobby.HasLobbyState && string.Equals(lobby.ActiveLobbyId, invitedLobbyId, StringComparison.Ordinal)) return;

		if (lobby.HasLobbyState)
		{
			userInterface.ShowConfirmationDialog(
				string.Format(TranslationServer.Translate("{0} invited you to their lobby. Leave your current lobby and join theirs?"), DescribeInviter(inviterName)),
				() => JoinInvitedLobbyAsync(invitedLobbyId, inviterName),
				confirmText: TranslationServer.Translate("JOIN"),
				titleOverride: TranslationServer.Translate("LOBBY INVITE"));
			return;
		}

		JoinInvitedLobbyAsync(invitedLobbyId, inviterName);
	}

	private async void JoinInvitedLobbyAsync(string invitedLobbyId, string inviterName)
	{
		LobbyManager? lobby = GameContext.Lobby;
		if (lobby == null) return;

		if (!string.IsNullOrEmpty(lobby.ActiveLobbyId))
		{
			lobby.Disconnect();
		}

		bool joined = await lobby.JoinLobbyAsync(invitedLobbyId);
		if (!GodotObject.IsInstanceValid(this)) return;

		if (joined)
		{
			GameContext.UserInterface?.TransitionTo(GameScreen.LobbyRoom);
			return;
		}

		string reason = string.IsNullOrEmpty(lobby.LobbyJoinError)
			? TranslationServer.Translate("The invited lobby could not be reached. It may have been closed.")
			: lobby.LobbyJoinError;
		lobby.LobbyJoinError = null;

		GameContext.UserInterface?.ShowPopupMessage(
			TranslationServer.Translate("LOBBY INVITE FAILED"),
			string.Format(TranslationServer.Translate("{0} invited you, but this screen could not join: {1}"), DescribeInviter(inviterName), reason));
	}

	private static string DescribeInviter(string inviterName)
	{
		return string.IsNullOrEmpty(inviterName) ? TranslationServer.Translate("A Steam friend") : inviterName;
	}
}
