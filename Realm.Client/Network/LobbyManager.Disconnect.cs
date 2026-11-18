using System;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Network;

namespace Realm.Client.Network;

/// <summary>Giving a lobby up: the Steam lobby, the seat and everything measured about it.</summary>
public partial class LobbyManager
{
	public void Disconnect()
	{
		GameLog.LogToConsole("[LobbyManager] Disconnecting...");

		ClientServerSession? session = _lobbySession;
		bool hostedHere = IsHost;
		_lobbySession = null;

		// The Steam lobby is the directory entry and the membership list, so leaving it is what closes the lobby for
		// everybody: the host leaving ends the match, and a joiner leaving frees their seat.
		if (SteamLobbyId != 0)
		{
			DetachSteamLobbyEvents();
			SteamLobbyDirectory.Leave(SteamLobbyId);
			SteamLobbyId = 0;
		}

		ActiveLobbyId = null;

		if (session != null)
		{
			DetachLobbySession(session);
			if (ReferenceEquals(GameContext.ServerSession, session)) GameContext.DetachServerSession();

			if (!hostedHere) session.SendLobbyCommand(new LobbyCommand { Kind = LobbyCommandKind.Leave });
			if (hostedHere) session.Dispose();
			else Callable.From(session.Dispose).CallDeferred();
		}

		ResetLobbyState();
	}

	private void ResetLobbyState()
	{
		_preparation.Cancel();

		bool hadPreparedWorld = _worldPrepared || GameContext.Host?.IsBuildingMatchWorld == true;
		_worldPrepared = false;
		_worldPreparedMapName = null;
		_loadReadyReported = false;
		_matchPresentationOpen = false;
		_isConnectedToHost = false;
		IsUnlisted = false;
		IsHost = false;
		IsGameStarted = false;
		_countdownActive = false;
		_countdownRemaining = 0;
		_countdownMapName = null;
		_matchStarted = false;
		JoinCodeRequired = false;
		JoinCodeSeatHeld = false;
		_lobbyRevision = 0;
		_downloadingMapName = null;
		PlayerList.Clear();
		PlayerListUpdated?.Invoke();

		GameContext.Host?.DiscardPreparedMatchWorld();
		if (hadPreparedWorld) GameContext.Host?.EndMatchAndClearWorld();
	}

	private bool _matchStarted;

	private void TeardownHostedLobby(ClientServerSession? launchedSession = null)
	{
		ClientServerSession? session = _lobbySession ?? launchedSession;
		_lobbySession = null;
		if (session != null)
		{
			DetachLobbySession(session);
			if (ReferenceEquals(GameContext.ServerSession, session)) GameContext.DetachServerSession();
			session.Dispose();
		}

		if (SteamLobbyId != 0)
		{
			DetachSteamLobbyEvents();
			SteamLobbyDirectory.Leave(SteamLobbyId);
			SteamLobbyId = 0;
		}

		ActiveLobbyId = null;
		ResetLobbyState();
	}
}
