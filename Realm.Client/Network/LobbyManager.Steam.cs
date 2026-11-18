using System;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Network;

namespace Realm.Client.Network;

public partial class LobbyManager
{
	/// <summary>
	///     Starts listening to the Steam lobby this screen is in. Membership is reconciled from Steam's own callbacks rather
	///     than from a poll, and pre-match chat arrives over Steam's lobby chat, which works before the match connection
	///     exists.
	/// </summary>
	private void AttachSteamLobbyEvents()
	{
		SteamLobbyDirectory.MembershipChanged -= OnSteamMembershipChanged;
		SteamLobbyDirectory.MembershipChanged += OnSteamMembershipChanged;
		SteamLobbyDirectory.ChatMessageReceived -= OnSteamChatMessage;
		SteamLobbyDirectory.ChatMessageReceived += OnSteamChatMessage;
	}

	private void DetachSteamLobbyEvents()
	{
		SteamLobbyDirectory.MembershipChanged -= OnSteamMembershipChanged;
		SteamLobbyDirectory.ChatMessageReceived -= OnSteamChatMessage;
	}

	/// <summary>
	///     Reconciles the roster with Steam's membership. Steam says who is present; the lobby protocol says where they sit,
	///     so a member leaving is removed from the player rows and their seat is freed by the authority's own roster rules,
	///     which already answer a departed peer.
	/// </summary>
	/// <param name="change">The membership change Steam reported.</param>
	private void OnSteamMembershipChanged(SteamLobbyMembershipChange change)
	{
		if (change.LobbyId != SteamLobbyId) return;
		if (!change.Left) return;

		PlayerInfo? departed = PlayerList.Find(player => player.SteamId == change.SteamId);
		if (departed == null) return;

		// The host leaving ends the match: the authority is a process on their machine, so there is nothing to migrate to.
		if (departed.SteamId == SteamLobbyDirectory.GetOwnerSteamId(change.LobbyId) && !IsSinglePlayer)
		{
			NotifyHostLine(TranslationServer.Translate("The host left the lobby."));
			SteamLobbyId = 0;
			Disconnect();
			return;
		}

		PlayerList.Remove(departed);
		PlayerListUpdated?.Invoke();
	}

	private void OnSteamChatMessage(SteamLobbyChatEntry entry)
	{
		if (entry.LobbyId != SteamLobbyId) return;
		if (entry.SteamId == GameContext.Steam.SteamId) return;
		if (IsGameStarted) return;

		string sender = string.IsNullOrWhiteSpace(entry.PersonaName)
			? LobbyLimits.SanitizeDisplayName(entry.SteamId.ToString())
			: LobbyLimits.SanitizeDisplayName(entry.PersonaName);

		ChatReceived?.Invoke(sender, entry.Text, false, DateTime.UtcNow.Ticks);
	}
}
