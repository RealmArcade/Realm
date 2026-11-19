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
	/// <summary>
	///     Joins somebody else's match. The id is a Steam lobby: entering it is Steam's decision, the host it names is a
	///     SteamID, and the connection is Steam's peer-to-peer socket. Nothing here asks a registry, punches a router or reads
	///     an address, because Steam owns the directory and the routing.
	/// </summary>
	/// <param name="lobbyId">The Steam lobby id, as the browser listed it.</param>
	/// <returns>Whether this screen is standing in the lobby and admitted to it.</returns>
	public async Task<bool> JoinLobbyAsync(string lobbyId)
	{
		InitializeClientState(lobbyId);
		SteamLobbyId = 0;

		if (!SteamLobbyDirectory.IsAvailable)
		{
			LobbyJoinError = GameContext.Steam.UnavailableExplanation;
			ConnectionFailed?.Invoke(LobbyJoinError);
			return false;
		}

		if (!ulong.TryParse(lobbyId, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong steamLobbyId) || steamLobbyId == 0)
		{
			LobbyJoinError = TranslationServer.Translate("This lobby no longer exists.");
			ConnectionFailed?.Invoke(LobbyJoinError);
			return false;
		}

		try
		{
			string enterFailure = await SteamLobbyDirectory.JoinAsync(steamLobbyId);
			if (enterFailure.Length > 0)
			{
				LobbyJoinError = enterFailure;
				ConnectionFailed?.Invoke(enterFailure);
				return false;
			}

			SteamLobbyId = steamLobbyId;
			AttachSteamLobbyEvents();

			string hostSteamIdText = SteamLobbyDirectory.GetData(steamLobbyId, SteamLobbyDirectory.HostSteamIdKey);
			if (!ulong.TryParse(hostSteamIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong hostSteamId) || hostSteamId == 0)
			{
				SteamLobbyDirectory.Leave(steamLobbyId);
				LobbyJoinError = TranslationServer.Translate("This lobby does not name a host this screen can reach.");
				ConnectionFailed?.Invoke(LobbyJoinError);
				return false;
			}

			string mapName = SteamLobbyDirectory.GetData(steamLobbyId, SteamLobbyDirectory.MapKey);
			if (!string.IsNullOrEmpty(mapName))
			{
				ActiveMapName = mapName;
				ActiveMapVersion = SteamLobbyDirectory.GetData(steamLobbyId, SteamLobbyDirectory.MapVersionKey);
			}

			// The map is checked before the connection is asked for, because a screen that cannot play the map would only
			// take a seat in the room and be refused, or worse, be admitted to a match it cannot draw.
			ulong.TryParse(SteamLobbyDirectory.GetData(steamLobbyId, SteamLobbyDirectory.MapWorkshopIdKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong advertisedWorkshopId);
			string mapFailure = await LobbyMapCompatibilityService.EvaluateAsync(
				mapName,
				advertisedWorkshopId,
				SteamLobbyDirectory.GetData(steamLobbyId, SteamLobbyDirectory.MapHashKey),
				SteamLobbyDirectory.GetData(steamLobbyId, SteamLobbyDirectory.EngineGenerationKey));
			if (mapFailure.Length > 0)
			{
				SteamLobbyDirectory.Leave(steamLobbyId);
				LobbyJoinError = mapFailure;
				ConnectionFailed?.Invoke(mapFailure);
				return false;
			}

			var channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork");
			if (channel == null)
			{
				LobbyJoinError = "This build has no channel to an authority.";
				ConnectionFailed?.Invoke(LobbyJoinError);
				return false;
			}

			GameContext.ServerSession?.Dispose();
			GameContext.DetachServerSession();

			var session = new ClientServerSession(channel);
			session.ConfigureLobbyIdentity(PlayerDisplayName, GameContext.Steam.SteamId);
			AttachLobbySession(session);

			if (!session.TryJoinSteamLobby(hostSteamId, LocalizationManager.GetCurrentLanguageCode(), out string joinFailure))
			{
				DropLobbySession(session);
				SteamLobbyDirectory.Leave(steamLobbyId);
				LobbyJoinError = joinFailure;
				ConnectionFailed?.Invoke(joinFailure);
				return false;
			}

			_isConnectedToHost = true;

			var admission = await WaitForLobbyAdmissionAsync(session, TimeSpan.FromMilliseconds(JoinAttemptTimeoutMilliseconds));
			if (admission.Failure != null)
			{
				DropLobbySession(session);
				SteamLobbyDirectory.Leave(steamLobbyId);
				LobbyJoinError = admission.Failure;
				ConnectionFailed?.Invoke(admission.Failure);
				return false;
			}

			IsHost = session.IsAdmin;
			return true;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[LobbyManager] Join error: {exception.Message}");
			LobbyJoinError = exception.Message;
			ConnectionFailed?.Invoke(exception.Message);
			return false;
		}
	}

	private void InitializeClientState(string lobbyId)
	{
		IsHost = false;
		IsGameStarted = false;
		ActiveLobbyId = lobbyId;
		LastLobbyWarning = null;
		LastHostError = null;
		_downloadingMapName = null;
		// A copy prepared for the lobby this screen was in a moment ago belongs to that lobby's package and to nothing
		// else, so it is dropped here rather than resolved for a map of the same name in this one.
		ActiveMapSessionDirectory = string.Empty;
		PlayerList.Clear();
	}

	/// <summary>
	///     How long one join attempt waits for the lobby's authority to admit this screen. A Steam connection on a slow link
	///     legitimately takes seconds to come up, so the budget is spent on one attempt instead of being divided between
	///     several: a retry is for silence, and a refusal is an answer that a second attempt would only repeat.
	/// </summary>
	private const int JoinAttemptTimeoutMilliseconds = 15000;

	private void DropLobbySession(ClientServerSession session)
	{
		DetachLobbySession(session);
		session.Dispose();
		if (ReferenceEquals(_lobbySession, session)) _lobbySession = null;
	}

	private async Task<string?> WaitForLobbyAdmissionAsync(ClientServerSession session, string? timeoutMessage = null)
	{
		var (failure, _) = await WaitForLobbyAdmissionAsync(session, TimeSpan.FromSeconds(15), timeoutMessage);
		return failure;
	}

	/// <summary>
	///     Waits for the authority to admit this screen. The second member separates a lobby that refused this join from one
	///     that never heard it: only the second is worth asking again, because a refusal is a decision the authority already
	///     made and a retry would put the same question to it.
	/// </summary>
	/// <param name="session">The session waiting for its answer.</param>
	/// <param name="budget">How long the authority has to answer before this attempt is given up.</param>
	/// <returns>Why the join failed or null when the authority admitted it, and whether a hello arrived at all.</returns>
	private async Task<(string? Failure, bool Answered)> WaitForLobbyAdmissionAsync(ClientServerSession session, TimeSpan budget, string? timeoutMessage = null)
	{
		var decided = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		_admissionTcs = decided;
		bool helloAnswered = false;

		void OnHello(ServerHello hello)
		{
			helloAnswered = true;
			decided.TrySetResult(hello.Accepted ? null : hello.Rejection);
		}

		void OnLost(string reason) => decided.TrySetResult(reason);

		session.HelloAnswered += OnHello;
		session.ServerLost += OnLost;

		try
		{
			var completed = await Task.WhenAny(decided.Task, Task.Delay(budget));
			// The budget expiring says nothing about Steam when no Steam connection was involved, so the caller names the
			// transport it waited on and a single-player start never reports a Steam failure on a loopback socket.
			string? failure = completed == decided.Task
				? decided.Task.Result
				: timeoutMessage ?? Tr("Steam could not connect this screen to the host in time.");
			return (failure, helloAnswered);
		}
		finally
		{
			session.HelloAnswered -= OnHello;
			session.ServerLost -= OnLost;
			_admissionTcs = null;
		}
	}

	private void AttachLobbySession(ClientServerSession session)
	{
		_lobbySession = session;
		session.HelloAnswered += OnLobbyHelloAnswered;
		session.LobbyStateReceived += OnLobbyState;
		session.LobbyChatReceived += OnLobbyChat;
		session.LobbyMatchStartedEvent += OnLobbyMatchStarted;
		session.LobbyKickedEvent += OnLobbyKicked;
		session.LobbyStartWarningReceived += OnLobbyStartWarning;
		session.JoinCodeAnswered += OnJoinCodeAnswered;
		session.MapTransferReplied += OnMapTransferReplied;
		session.ServerLost += OnLobbyAuthorityLost;
	}

	private void DetachLobbySession(ClientServerSession session)
	{
		session.HelloAnswered -= OnLobbyHelloAnswered;
		session.LobbyStateReceived -= OnLobbyState;
		session.LobbyChatReceived -= OnLobbyChat;
		session.LobbyMatchStartedEvent -= OnLobbyMatchStarted;
		session.LobbyKickedEvent -= OnLobbyKicked;
		session.LobbyStartWarningReceived -= OnLobbyStartWarning;
		session.JoinCodeAnswered -= OnJoinCodeAnswered;
		session.MapTransferReplied -= OnMapTransferReplied;
		session.ServerLost -= OnLobbyAuthorityLost;
	}

	/// <summary>
	///     Presents the join code a player typed. The authority compares it and answers with whether the seat is now held, so a
	///     wrong code leaves the player in the room with the field still in front of them.
	/// </summary>
	/// <param name="code">The code the player typed.</param>
	public void PresentJoinCode(string code)
	{
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.PresentJoinCode,
			Text = code ?? string.Empty
		});
	}

	/// <summary>Sets the code this lobby gates its seats with, or clears it when the host empties the field.</summary>
	/// <param name="code">The new code, or empty to open the seats to everybody in the room.</param>
	public void SetJoinCode(string code)
	{
		if (!IsHost) return;

		JoinCode = code ?? string.Empty;
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.SetJoinCode,
			Text = JoinCode
		});
	}

	private void OnJoinCodeAnswered(bool accepted)
	{
		JoinCodeSeatHeld = accepted;
		if (!accepted)
		{
			JoinCodeRefused?.Invoke(TranslationServer.Translate("That join code does not match this lobby's."));
			return;
		}

		JoinCodeRefused?.Invoke(string.Empty);
	}

	private void OnLobbyStartWarning(LobbyStartWarningMessage warning)
	{
		if (!IsHost) return;

		StartWarningReceived?.Invoke(warning.Message ?? string.Empty);
	}

	private void OnLobbyHelloAnswered(ServerHello hello)
	{
		if (!hello.Accepted) return;

		IsHost = hello.IsAdmin;
		if (IsHost)
		{
			ReportLocalMapReadyState(true);
			_lobbySession?.SendLobbyCommand(new LobbyCommand
			{
				Kind = LobbyCommandKind.ReportHostStability,
				Text = HostStability
			});
		}

		PlayerListUpdated?.Invoke();
	}

	private void OnLobbyState(LobbyStateMessage state)
	{
		if (state.Revision < _lobbyRevision) return;
		_lobbyRevision = state.Revision;

		ApplyOpenSlots(state);

		ApplyMatchSettings(state);
		ColorOverlays = NormaliseColorArray(state.ColorOverlays);
		MinimapPlayerColors = NormaliseColorArray(state.MinimapPlayerColors);
		MinimapTeamColors = NormaliseColorArray(state.MinimapTeamColors);
		ProxyOwners = NormaliseIndexArray(state.ProxyOwners);
		LobbyFactions = state.Factions ?? Array.Empty<string>();
		LobbyTeamCount = state.TeamCount > 0 ? state.TeamCount : LobbyLimits.DefaultTeamCount;
		SpectatorDelay = state.SpectatorDelay;
		ShroudMode = Enum.IsDefined(typeof(MatchShroudMode), state.ShroudMode) ? (MatchShroudMode)state.ShroudMode : MatchShroudMode.FromMap;

		if (TeamsTogether != state.TeamsTogether)
		{
			TeamsTogether = state.TeamsTogether;
			TeamsTogetherChanged?.Invoke(TeamsTogether);
		}
		if (!string.IsNullOrEmpty(state.HostStability) && !string.Equals(state.HostStability, HostStability, StringComparison.Ordinal))
		{
			HostStability = state.HostStability;
			HostStabilityUpdated?.Invoke(HostStability);
		}

		if (!string.IsNullOrEmpty(state.MapName) && !string.Equals(state.MapName, ActiveMapName, StringComparison.Ordinal))
		{
			ActiveMapName = state.MapName;
			ActiveMapVersion = string.IsNullOrEmpty(state.MapVersion) ? "1.0.0" : state.MapVersion;
			ActiveMapChanged?.Invoke(ActiveMapName);
			if (!IsHost) _ = EnsureMapDownloadedAsync(ActiveMapName, ActiveMapVersion);
		}

		if (JoinCodeRequired != state.JoinCodeRequired)
		{
			JoinCodeRequired = state.JoinCodeRequired;
			JoinCodeRequirementChanged?.Invoke(JoinCodeRequired);
		}

		ApplySeats(state.Seats);
		RefreshHostedLobbyAdvertisement();
		ApplyCountdown(state);
		MakeSureThisSeatReportsItsMap();
		StartMatchPreparationForCurrentMap();
		PlayerListUpdated?.Invoke();
	}

	/// <summary>
	///     Copies one of the colour arrays the lobby published into a table indexed by player index, replacing one that is
	///     missing or the wrong length with an empty table, because a lobby that states no colour for an index must leave that
	///     index to the colour its seat carries.
	/// </summary>
	/// <param name="published">The array the lobby published.</param>
	/// <returns>One entry per player slot, zero for an index the lobby stated nothing for.</returns>
	private static uint[] NormaliseColorArray(uint[]? published)
	{
		var colors = new uint[LobbyLimits.MaximumSeats + 1];
		if (published == null) return colors;

		int count = Math.Min(published.Length, colors.Length);
		Array.Copy(published, colors, count);
		return colors;
	}

	/// <summary>
	///     Copies the standing-in index array the lobby published into a table indexed by player index, replacing one that is
	///     missing or the wrong length with an empty table, because a lobby that states nothing for an index leaves it drawn
	///     by its own relations.
	/// </summary>
	/// <param name="published">The array the lobby published.</param>
	/// <returns>One entry per player slot, zero for an index that stands in for nobody.</returns>
	private static int[] NormaliseIndexArray(int[]? published)
	{
		var owners = new int[LobbyLimits.MaximumSeats + 1];
		if (published == null) return owners;

		int count = Math.Min(published.Length, owners.Length);
		Array.Copy(published, owners, count);
		return owners;
	}

	/// <summary>
	///     Reads which player slots the lobby leaves open, which is the whole of how the lobby is sized: every row it
	///     published is drawn, and only the open ones accept a player.
	/// </summary>
	/// <param name="state">The lobby state the authority published.</param>
	private void ApplyOpenSlots(LobbyStateMessage state)
	{
		SlotCount = state.SlotCount > 0 ? Math.Min(state.SlotCount, LobbyLimits.MaximumSeats) : LobbyLimits.MaximumSeats;

		for (int slot = 0; slot < _openSlots.Length; slot++) _openSlots[slot] = false;

		int openSlots = 0;
		for (int slot = 1; slot <= SlotCount; slot++)
		{
			bool open = state.OpenSlots != null && slot < state.OpenSlots.Length ? state.OpenSlots[slot] : true;
			_openSlots[slot] = open;
			if (open) openSlots++;
		}

		OpenSlotCount = openSlots;
	}

	/// <summary>Reads the match settings the lobby's map declared, which the panel that edits them is drawn from.</summary>
	/// <param name="state">The lobby state the authority published.</param>
	private void ApplyMatchSettings(LobbyStateMessage state)
	{
		LobbyMatchSettingMessage[] published = state.Settings ?? Array.Empty<LobbyMatchSettingMessage>();

		MatchSettings.Clear();

		for (int index = 0; index < published.Length; index++)
		{
			LobbyMatchSettingMessage message = published[index];
			var setting = new LobbyMatchSetting
			{
				SettingId = message.SettingId,
				Label = message.Label ?? string.Empty,
				Tooltip = message.Tooltip ?? string.Empty,
				Control = Enum.IsDefined(typeof(LobbySettingControl), message.Control) ? (LobbySettingControl)message.Control : LobbySettingControl.Toggle,
				Value = message.Value,
				Minimum = message.Minimum,
				Maximum = message.Maximum,
				Options = message.Options ?? Array.Empty<string>()
			};

			MatchSettings.Add(setting);
		}

		MatchSettingsChanged?.Invoke();
	}

	private void StartMatchPreparationForCurrentMap()
	{
		if (string.IsNullOrWhiteSpace(ActiveMapName)) return;

		bool holdsMap;
		if (IsHost)
		{
			string? hostMapDirectory = GameHost.ResolveMapDirectory(ActiveMapName, ActiveMapVersion);
			holdsMap = !string.IsNullOrEmpty(hostMapDirectory) && Directory.Exists(hostMapDirectory);
		}
		else
		{
			holdsMap = HoldsAdvertisedMapCopy(ActiveMapName, ActiveMapVersion);
		}

		if (!holdsMap) return;

		bool preparedForThisMap = _preparation.IsPrepared && string.Equals(_preparation.MapName, ActiveMapName, StringComparison.Ordinal);
		if (preparedForThisMap)
		{
			if (!_worldPrepared || !string.Equals(_worldPreparedMapName, ActiveMapName, StringComparison.Ordinal)) BeginMatchWorldBuild();
			ReportWorldPreparedIfReady();
			return;
		}

		if (_preparation.IsPreparing(ActiveMapName)) return;

		if (_worldPrepared) GameContext.Host?.DiscardPreparedMatchWorld();
		_worldPrepared = false;
		_worldPreparedMapName = null;

		ReportLocalLoadReadyState(false);
		MatchLoadTrace.Begin("lobby_preparation");
		_preparation.Prepare(ActiveMapName, ActiveMapVersion);
		MatchLoadTrace.Mark("preparation_started");
		MatchLoadTrace.WriteSummary("preparation_started");

		if (_preparation.HasWork) return;
		BeginMatchWorldBuild();
	}

	private void MakeSureThisSeatReportsItsMap()
	{
		if (IsHost) return;

		PlayerInfo? me = PlayerList.Find(IsLocalPlayer);
		if (me == null || me.IsMapReady) return;
		if (string.IsNullOrWhiteSpace(ActiveMapName)) return;

		if (HoldsAdvertisedMapCopy(ActiveMapName, ActiveMapVersion))
		{
			ReportLocalMapReadyState(true);
			return;
		}

		if (_downloadingMapName != ActiveMapName) _ = EnsureMapDownloadedAsync(ActiveMapName, ActiveMapVersion);
	}

	private void ApplySeats(LobbySeat[] seats)
	{
		PlayerList.Clear();
		if (seats == null) return;

		for (int index = 0; index < seats.Length; index++)
		{
			LobbySeat seat = seats[index];
			PlayerList.Add(new PlayerInfo
			{
				PeerId = seat.PeerId,
				Slot = seat.Slot,
				SteamId = seat.SteamId,
				Name = seat.Name ?? string.Empty,
				Faction = seat.Faction ?? string.Empty,
				Team = seat.TeamName ?? string.Empty,
				Color = seat.ColorRgba == 0 ? PlayerColorConfig.GetColor(seat.Slot) : UnpackColor(seat.ColorRgba),
				IsHost = seat.IsHost,
				IsComputer = seat.IsComputer,
				Difficulty = LobbyBotDifficultyRules.Clamp(seat.Difficulty),
				IsReady = seat.IsReady,
				IsMapReady = seat.IsMapReady,
				MapDownloadPercent = seat.MapDownloadPercent,
				IsLoadReady = seat.IsLoadReady,
				IsDisconnected = seat.IsDisconnected,
				Latency = FormatLinkMilliseconds(seat.LatencyMilliseconds),
				Jitter = FormatLinkMilliseconds(seat.JitterMilliseconds),
				PacketLoss = FormatLinkPercent(seat.PacketLossPercent),
				BinaryVersion = RealmVersion.GameBinaryVersion,
			});
		}
	}

	private void ApplyCountdown(LobbyStateMessage state)
	{
		if (state.CountdownActive)
		{
			bool started = !_countdownActive;
			_countdownActive = true;
			_countdownMapName = state.MapName;

			if (started)
			{
				_countdownRemaining = state.CountdownRemainingSeconds;
				CountdownStarted?.Invoke(state.MapName, state.CountdownRemainingSeconds);

				BeginMatchPresentation(state.MapName);
			}
			else if (state.CountdownRemainingSeconds != _countdownRemaining)
			{
				_countdownRemaining = state.CountdownRemainingSeconds;
				CountdownTick?.Invoke(_countdownRemaining);
			}

			return;
		}

		if (!_countdownActive) return;

		_countdownActive = false;
		_countdownMapName = null;
		_countdownRemaining = 0;
		CountdownCancelled?.Invoke();
		EndMatchPresentation();
	}

	private void BeginMatchPresentation(string mapName)
	{
		if (_matchPresentationOpen || _matchStarted) return;

		ActiveMapName = mapName;
		GameContext.ReplayPlayback.StopReplay();

		GameHost? host = GameContext.Host;
		while (host != null && host.IsBuildingMatchWorld) host.AdvanceMatchWorldPreparation();
		if (host != null && host.HasPreparedMatchWorld)
		{
			_worldPrepared = true;
			_worldPreparedMapName = mapName;
		}

		if (!_worldPrepared) return;

		_matchPresentationOpen = true;
		MatchLoadTrace.Begin("start_presentation");
		MatchLoadTrace.Mark("countdown_received");
		GameLog.LogToConsole($"[LobbyManager] Showing the match world for '{mapName}' under the countdown");

		host?.ShowPreparedMatchWorld();
		GameContext.UserInterface?.TransitionTo(GameScreen.InGameHUD);
	}

	private void EndMatchPresentation()
	{
		if (!_matchPresentationOpen) return;

		_matchPresentationOpen = false;
		GameLog.LogToConsole("[LobbyManager] Countdown cancelled; returning to the lobby");
		GameContext.UserInterface?.TransitionTo(GameScreen.LobbyRoom);
	}

	public void ReportLocalLoadReadyState(bool isLoadReady)
	{
		_lobbySession?.SendLobbyCommand(new LobbyCommand
		{
			Kind = LobbyCommandKind.ReportLoadReady,
			Flag = isLoadReady
		});
	}

	private void OnLobbyChat(LobbyChatMessage line)
	{
		string sender = string.IsNullOrEmpty(line.SenderName) ? "System" : line.SenderName;

		int senderSlot = PlayerList.Find(p => p.PeerId == line.SenderPeerId)?.Slot ?? 0;
		if (line.SenderPeerId > 0) ServerChatCommandReceived?.Invoke(senderSlot, line.Message);
		ChatReceived?.Invoke(sender, line.Message, false, line.TimestampUtcTicks);
	}

	private void OnLobbyKicked(LobbyKickedMessage kicked)
	{
		GameLog.LogToConsole($"[LobbyManager] Removed from the lobby: {kicked.Reason}");
		KickReceived?.Invoke(string.IsNullOrEmpty(kicked.Reason) ? "Kicked by Host" : kicked.Reason);

		bool wasBuilding = _matchPresentationOpen;
		var session = _lobbySession;
		if (session != null)
		{
			DetachLobbySession(session);
			session.Dispose();
			_lobbySession = null;
		}

		ResetLobbyState();
		if (wasBuilding) GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
	}

	private void OnLobbyAuthorityLost(string reason)
	{
		if (_matchStarted) return;
		// A kicked connection drops before its reason has been read, and the reconnect that follows is refused by
		// name. That refusal is the kick arriving late rather than a lobby that failed.
		bool kicked = reason.Contains(LobbyLimits.KickedRejection, StringComparison.Ordinal);
		bool wasBuilding = _matchPresentationOpen;
		bool administeredHere = IsHost;
		if (administeredHere && !kicked)
		{
			LastHostError = reason;
			GameLog.LogError($"[LobbyManager] The lobby's authority is gone: {reason}");
		}

		var session = _lobbySession;
		if (session != null)
		{
			DetachLobbySession(session);
			session.Dispose();
			_lobbySession = null;
		}

		ResetLobbyState();

		if (kicked)
		{
			KickReceived?.Invoke("Kicked by Host");
			return;
		}

		ConnectionFailed?.Invoke(administeredHere ? reason : "The host's lobby is no longer running.");
		if (wasBuilding) GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
	}

	private void OnLobbyMatchStarted(LobbyMatchStartedMessage started)
	{
		if (_matchStarted) return;
		_matchStarted = true;
		IsGameStarted = true;
		_countdownActive = false;
		CountdownFinished?.Invoke();

		ClientServerSession? session = _lobbySession;
		if (session == null) return;

		if (_matchPresentationOpen)
		{
			_matchPresentationOpen = false;
			DetachLobbySession(session);
			_lobbySession = null;
			MatchSeed = started.Seed;
			GameContext.RandomService.SetSeed(started.Seed);
			GameSessionStartTime = DateTime.UtcNow;
			GameContext.AttachServerSession(session);
			GameContext.Host?.AttachMatchSession();
			GameContext.Host?.StartMatchRecording();
			MatchLoadTrace.Mark("match_started");
			return;
		}

		DetachLobbySession(session);
		_lobbySession = null;
		GameContext.AttachServerSession(session);
		EnterMatchScene(started.MapName, started.Seed);
	}

	private void EnterMatchScene(string mapName, int matchSeed)
	{
		GameLog.LogToConsole($"[LobbyManager] Match started for '{mapName}' with deterministic random seed {matchSeed}");
		GameContext.ReplayPlayback.StopReplay();
		ActiveMapName = mapName;
		MatchSeed = matchSeed;
		GameContext.RandomService.SetSeed(matchSeed);

		IsGameStarted = true;
		GameSessionStartTime = DateTime.UtcNow;

		GetTree().ChangeSceneToFile("res://GameHost.tscn");
	}

}
