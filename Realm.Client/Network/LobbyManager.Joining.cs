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
	public async Task<bool> JoinLobbyAsync(string lobbyId)
	{
		InitializeClientState(lobbyId);
		await EnsureNatTypeMeasuredAsync();

		string clientPublicIp = _hostPublicIp ?? "127.0.0.1";
		int clientPublicPort = _hostPublicPort > 0 ? _hostPublicPort : ENetPort;

		try
		{
			var response = await SendJoinRequestAsync(lobbyId, clientPublicIp, clientPublicPort);
			if (response == null || !response.IsSuccessStatusCode)
			{
				var errorText = response != null ? await response.Content.ReadAsStringAsync() : "All registry nodes offline";
				GameLog.LogError($"[LobbyManager] Failed to join lobby: {errorText}");
				ConnectionFailed?.Invoke("Failed to coordinate join.");
				return false;
			}

			var (connectIp, connectPort) = await ResolveHostConnectionInfoAsync(response, clientPublicIp);
			_connectedHostIp = connectIp;
			return await EstablishLobbyConnectionAsync(connectIp, connectPort, IsPrivateIp(connectIp));
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[LobbyManager] Join error: {ex.Message}");
			ConnectionFailed?.Invoke(ex.Message);
			return false;
		}
	}

	public async Task<bool> JoinByIpAsync(string targetIp, int targetPort = DefaultGamePort)
	{
		string directLobbyId = $"direct-{Guid.NewGuid():N}";
		InitializeClientState(directLobbyId);
		_connectedHostIp = targetIp;
		bool joined = await EstablishLobbyConnectionAsync(targetIp, targetPort, IsPrivateIp(targetIp));

		// Only an address that seated this player is worth going back to: a typo, or a lobby that never answered, is not
		// somewhere to send them a second time.
		if (joined) DirectConnectHistory.Remember(targetIp, targetPort);
		return joined;
	}

	private void InitializeClientState(string lobbyId)
	{
		ReleaseSeedingSlot();

		IsHost = false;
		IsGameStarted = false;
		ActiveLobbyId = lobbyId;
		LastLobbyWarning = null;
		LastHostError = null;
		_downloadingMapName = null;
		PlayerList.Clear();
	}

	private async Task<bool> EstablishLobbyConnectionAsync(string connectIp, int connectPort, bool isLocalConnection)
	{
		var channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork");
		if (channel == null)
		{
			LastHostError = "This build has no channel to an authority.";
			ConnectionFailed?.Invoke(LastHostError);
			return false;
		}

		if (!isLocalConnection)
		{
			await UdpHolePuncher.PunchHoleAsync(connectIp, connectPort, ENetPort);
			await Task.Delay(50);
		}

		GameContext.ServerSession?.Dispose();
		GameContext.DetachServerSession();

		var session = new ClientServerSession(channel);
		session.ConfigureLobbyIdentity(PlayerDisplayName, LobbySessionToken, AccountId, GameContext.Steam.SteamId);
		AttachLobbySession(session);

		if (!session.TryJoinLobby(connectIp, connectPort, LocalizationManager.GetCurrentLanguageCode(), isLocalConnection ? 0 : ENetPort, out string failure))
		{
			LastHostError = failure;
			GameLog.LogError($"[LobbyManager] {failure}");
			Disconnect();
			ConnectionFailed?.Invoke(failure);
			return false;
		}

		_isConnectedToHost = true;

		string? admissionFailure = await WaitForLobbyAdmissionAsync(session);
		if (admissionFailure != null)
		{
			LobbyJoinError = string.Equals(admissionFailure, LobbyLimits.KickedRejection, StringComparison.Ordinal)
				? "You were removed from this lobby."
				: admissionFailure;
			GameLog.LogError($"[LobbyManager] {admissionFailure}");
			Disconnect();
			ConnectionFailed?.Invoke("Direct connection handshake failed.");
			return false;
		}

		IsHost = session.IsAdmin;
		return true;
	}

	private async Task<string?> WaitForLobbyAdmissionAsync(ClientServerSession session)
	{
		var decided = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		_admissionTcs = decided;

		void OnHello(ServerHello hello) => decided.TrySetResult(hello.Accepted ? null : hello.Rejection);
		void OnLost(string reason) => decided.TrySetResult(reason);

		session.HelloAnswered += OnHello;
		session.ServerLost += OnLost;

		try
		{
			var completed = await Task.WhenAny(decided.Task, Task.Delay(TimeSpan.FromSeconds(15)));
			return completed == decided.Task ? decided.Task.Result : "The lobby's authority did not answer in time.";
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
		session.MapTransferReplied -= OnMapTransferReplied;
		session.ServerLost -= OnLobbyAuthorityLost;
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

		ApplySeats(state.Seats);
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
			holdsMap = MapAssetManager.IsMapDownloaded(ActiveMapName, ActiveMapVersion);
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

		if (MapAssetManager.IsMapDownloaded(ActiveMapName, ActiveMapVersion))
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
				AccountId = seat.AccountId,
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
				SessionToken = seat.SessionToken ?? string.Empty
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
		if (line.SenderPeerId > 0 && line.SenderAccountId == 0) sender = UnverifiedNameMark.Apply(sender);

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
		int hostedPort = session?.Port ?? ENetPort;
		if (session != null)
		{
			DetachLobbySession(session);
			session.Dispose();
			_lobbySession = null;
		}

		ReleaseHostedEndpoint(hostedPort);
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

		if (IsHost) UnregisterActiveLobbyFromRegistry();

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
