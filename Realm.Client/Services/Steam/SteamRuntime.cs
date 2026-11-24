using System;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Client.UI;

namespace Realm.Client.Services;

[GlobalClass]
public partial class SteamRuntime : Node
{
	/// <summary>The launch argument Steam writes when it starts this game from a friend's Join Game action.</summary>
	private const string ConnectLobbyArgument = "+connect_lobby";

	private string? _pendingInvitedLobbyId;
	private string _pendingInviterName = string.Empty;

	public override void _Ready()
	{
		GameContext.Steam.Start();
		GameContext.Steam.LobbyInviteAccepted += OnLobbyInviteAccepted;
		ReadConnectLobbyArgument();

		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		GameContext.Steam.Pump();
		GameContext.Steam.RefreshPresence();
		JoinPendingInvite();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest || what == NotificationExitTree)
		{
			GameContext.Steam.LobbyInviteAccepted -= OnLobbyInviteAccepted;
			GameContext.Steam.ClearPresence();
			GameContext.Steam.Stop();
		}
	}

	/// <summary>
	///     Reads the lobby Steam was asked to start this game in, which is the launch argument a friend's Join Game action
	///     carries. It is the same path an accepted invitation takes, so a launch and an invitation end in one join.
	/// </summary>
	private void ReadConnectLobbyArgument()
	{
		try
		{
			string[] arguments = OS.GetCmdlineArgs();
			for (int index = 0; index < arguments.Length - 1; index++)
			{
				if (!string.Equals(arguments[index], ConnectLobbyArgument, StringComparison.OrdinalIgnoreCase)) continue;

				string lobbyId = arguments[index + 1].Trim();
				if (lobbyId.Length == 0 || !ulong.TryParse(lobbyId, out ulong steamLobbyId) || steamLobbyId == 0) return;

				GameLog.LogToConsole($"[Steam] started with a request to join Steam lobby {lobbyId}");
				_pendingInvitedLobbyId = lobbyId;
				_pendingInviterName = string.Empty;
				return;
			}
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the launch arguments could not be read: {exception.Message}");
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

	/// <summary>
	///     Reached both directly and as a confirmation dialog's callback, so it keeps a void signature. It had no
	///     catcher: the current lobby is disconnected before the join is awaited, so a fault in the join left the player
	///     with no lobby, no error popup and nothing in the log.
	/// </summary>
	private async void JoinInvitedLobbyAsync(string invitedLobbyId, string inviterName)
	{
		try
		{
			await JoinInvitedLobbyCoreAsync(invitedLobbyId, inviterName);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[SteamRuntime] Joining the invited lobby failed: {exception}");
		}
	}

	private async Task JoinInvitedLobbyCoreAsync(string invitedLobbyId, string inviterName)
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
