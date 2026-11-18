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
using Steamworks;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Services.Steam;
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
	public async Task<bool> HostSinglePlayerGame(string mapPathName, string mapDisplayName, string? mapVersion = null, bool debugMapScript = false)
	{
		IsSinglePlayer = true;
		IsHost = true;
		ActiveMapName = mapPathName;
		ActiveMapVersion = !string.IsNullOrWhiteSpace(mapVersion) ? mapVersion : "1.0.0";
		PlayerList.Clear();
		LastLobbyWarning = null;

		string? mapDirectory = GameHost.ResolveMapDirectory(mapPathName, ActiveMapVersion);

		MapMetadata metadata;
		try
		{
			metadata = MapFileService.LoadMetadata(mapDirectory ?? string.Empty);
		}
		catch (JsonException exception)
		{
			LastHostError = string.Format(Tr("The map cannot be loaded by this build: {0}"), exception.Message);
			GD.PushError($"[LobbyManager] {LastHostError}");
			IsGameStarted = false;
			return false;
		}

		MaxPlayers = metadata.MapProperties?.GetPlayablePlayerSlots() ?? MapInfoMetadata.DefaultPlayablePlayerSlots;
		int requestedAiCount = Math.Max(0, metadata.MapProperties?.TestAiPlayerCount ?? 1);
		int aiCount = Math.Min(requestedAiCount, Math.Max(0, MaxPlayers - 1));
		if (aiCount < requestedAiCount)
		{
			LastLobbyWarning = string.Format(Tr("Only {0} of {1} requested AI players fit into the {2} playable slots of this map."), aiCount, requestedAiCount, MaxPlayers);
			GD.PushWarning($"[LobbyManager] {LastLobbyWarning}");
		}

		int? testSeed = metadata.MapProperties?.TestRandomSeed;

		GameContext.RandomService.SetSeed(testSeed);
		MatchSeed = GameContext.RandomService.Seed;

		// The seat plan is read from the map's own test settings, which is the same reading the editor warns about what a
		// run will show with, so a randomized run seats the player where the editor said it would.
		MapTestSeatingPlan seating = MapTestSeating.Plan(metadata.MapProperties, MaxPlayers);
		int humanSlot = seating.HumanSlot;
		var aiSlots = new List<int>(seating.ComputerSlots);

		var hostPlayer = new PlayerInfo
		{
			PeerId = 1,
			Slot = humanSlot,
			SteamId = GameContext.Steam.SteamId,
			Name = PlayerDisplayName,
			Faction = "HUMAN",
			Team = $"Team {humanSlot}",
			Color = PlayerColorConfig.GetColor(humanSlot),
			IsHost = true,
			Latency = "0 ms",
			Jitter = "0 ms",
			PacketLoss = "0%",
			BinaryVersion = RealmVersion.GameBinaryVersion,
		};
		PlayerList.Add(hostPlayer);

		for (int i = 0; i < aiSlots.Count; i++)
		{
			int slot = aiSlots[i];
			var botPlayer = new PlayerInfo
			{
				PeerId = -(slot + 100),
				Slot = slot,
				Name = $"Bot {slot}",
				Faction = "AI",
				Team = $"Team {slot}",
				Color = PlayerColorConfig.GetColor(slot),
				IsHost = false,
				IsComputer = true,
				Latency = "0 ms",
				Jitter = "0 ms",
				PacketLoss = "0%",
				BinaryVersion = RealmVersion.GameBinaryVersion
			};
			PlayerList.Add(botPlayer);
		}

		if (!await TryStartAuthority(mapDirectory, mapPathName, wasmDebug: debugMapScript)) return false;

		IsGameStarted = true;
		CallDeferred(nameof(EnterMatchScene), mapPathName, MatchSeed);
		return true;
	}

	private async Task<bool> TryStartAuthority(string? mapDirectory, string mapPathName, int authorityPort = 0, bool openToOthers = false, bool wasmDebug = false)
	{
		LastAuthorityLogPath = string.Empty;
		LastAuthorityConsoleLogPath = string.Empty;

		if (string.IsNullOrEmpty(mapDirectory))
		{
			LastHostError = $"The map folder for '{mapPathName}' could not be found.";
			GD.PushError($"[LobbyManager] {LastHostError}");
			IsGameStarted = false;
			return false;
		}

		var channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork");
		if (channel == null)
		{
			LastHostError = "This build has no channel to an authority.";
			GD.PushError($"[LobbyManager] {LastHostError}");
			IsGameStarted = false;
			return false;
		}

		GameContext.ServerSession?.Dispose();

		var session = new ClientServerSession(channel);
		var start = new ServerMatchStart(
			ProjectSettings.GlobalizePath(mapDirectory),
			mapPathName,
			MatchSeed,
			Path.Combine(OS.GetUserDataDir(), "logs"),
			OS.GetUserDataDir(),
			LocalizationManager.GetCurrentLanguageCode(),
			AllowsCheats,
			openToOthers,
			wasmDebug,
			BuildMatchRoster(),
			ShroudMode: ShroudMode > MatchShroudMode.FromMap ? ShroudMode : EffectiveShroudMode,
			Settings: BuildLobbySettingSet());

		MatchStartResult started = await session.TryStartAsync(start, authorityPort);
		LastAuthorityLogPath = started.LogPath;
		LastAuthorityConsoleLogPath = started.ConsoleLogPath;
		if (!started.Started)
		{
			LastHostError = started.Failure;
			GD.PushError($"[LobbyManager] {started.Failure}");
			IsGameStarted = false;
			session.Dispose();
			return false;
		}

		GameContext.AttachServerSession(session);
		return true;
	}

	/// <summary>
	///     Opens this screen's lobby, takes its seat in it and lets the map state the defaults the lobby starts with, and then
	///     publishes it: the port is opened, a registry node is asked to reach it, and only then is the lobby registered.
	/// </summary>
	public async Task<bool> HostLobbyAsync(string mapPathName, string mapDisplayName, string? explicitVersion = null)
	{
		return await PrepareHostedLobbyAsync(mapPathName, mapDisplayName, explicitVersion) && await PublishPreparedLobbyAsync();
	}

	/// <summary>
	///     Opens the lobby this screen will host without publishing it. The authority is started, this screen takes the seat
	///     that administers it, the map's own lobby rules have run, and the settings the match will play are the ones the
	///     lobby state names - so a host can read them and confirm them before anybody can see the lobby. Nothing about the
	///     machine's network is touched here: the port is opened, verified and registered by
	///     <see cref="PublishPreparedLobbyAsync" />.
	/// </summary>
	public async Task<bool> PrepareHostedLobbyAsync(string mapPathName, string mapDisplayName, string? explicitVersion = null)
	{
		IsSinglePlayer = false;
		InitializeHostState(mapPathName, explicitVersion);
		LobbyPort = 0;
		HostedMapDisplayName = mapDisplayName;
		HostedMapVersion = string.IsNullOrWhiteSpace(explicitVersion) ? "1.0.0" : explicitVersion;

		string? mapDirectory = GameHost.ResolveMapDirectory(mapPathName, ActiveMapVersion);
		if (string.IsNullOrEmpty(mapDirectory))
		{
			LastHostError = $"The map folder for '{mapPathName}' could not be found.";
			return false;
		}

		var channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork");
		if (channel == null)
		{
			LastHostError = "This build has no channel to an authority.";
			return false;
		}

		GameContext.ServerSession?.Dispose();
		GameContext.DetachServerSession();

		var session = new ClientServerSession(channel);
		session.ConfigureLobbyIdentity(PlayerDisplayName, GameContext.Steam.SteamId);
		AttachLobbySession(session);

		// Steam's lobby is created before the authority is told to start, because it is what the authority admits peers
		// against: a match that started without one would have to admit everybody or nobody.
		if (!GameContext.Steam.IsAvailable)
		{
			LastHostError = GameContext.Steam.UnavailableExplanation;
			GameLog.LogError($"[LobbyManager] {LastHostError}");
			TeardownHostedLobby(session);
			return false;
		}

		SteamLobbyId = await SteamLobbyDirectory.CreateAsync(HostedVisibility, MaxPlayers, !string.IsNullOrEmpty(JoinCode));
		if (SteamLobbyId == 0)
		{
			LastHostError = TranslationServer.Translate("Steam could not create this lobby. Try again in a moment.");
			GameLog.LogError($"[LobbyManager] {LastHostError}");
			TeardownHostedLobby(session);
			return false;
		}

		session.SteamLobbyId = SteamLobbyId;
		ActiveLobbyId = SteamLobbyId.ToString();
		AttachSteamLobbyEvents();

		var start = new ServerLobbyStart(
			ProjectSettings.GlobalizePath(mapDirectory),
			mapPathName,
			MaxPlayers,
			Path.Combine(OS.GetUserDataDir(), "logs"),
			OS.GetUserDataDir(),
			LocalizationManager.GetCurrentLanguageCode(),
			JoinCode: JoinCode);

		ReportHostLobbyStage(HostLobbyStage.LoadingMap);

		// The port is asked for by name first, because a lobby on a port that never changes is the only kind a player can
		// forward once and be reachable at forever. The system's choice is the fallback for the case that makes a fixed
		// port impossible - another instance already holding it - and it is taken only after the named port was refused.
		MatchStartResult lobbyStarted = await session.TryStartLobbyAsync(start, DefaultGamePort);
		if (!lobbyStarted.Started)
		{
			GameLog.LogToConsole($"[LobbyManager] UDP port {DefaultGamePort} could not be taken for this lobby ({lobbyStarted.Failure}), so the socket takes the port the system chooses.");
			lobbyStarted = await session.TryStartLobbyAsync(start);
		}
		LastAuthorityLogPath = lobbyStarted.LogPath;
		LastAuthorityConsoleLogPath = lobbyStarted.ConsoleLogPath;
		if (!lobbyStarted.Started)
		{
			LastHostError = lobbyStarted.Failure;
			GD.PushError($"[LobbyManager] {lobbyStarted.Failure}");
			TeardownHostedLobby(session);
			return false;
		}

		ReportHostLobbyStage(HostLobbyStage.AdmittingHost);
		string? admissionFailure = await WaitForLobbyAdmissionAsync(session);
		if (admissionFailure != null)
		{
			LastHostError = admissionFailure;
			GD.PushError($"[LobbyManager] {admissionFailure}");
			TeardownHostedLobby(session);
			return false;
		}

		if (!session.IsAdmin)
		{
			LastHostError = "The lobby's authority did not grant this client the capability that administers it.";
			GD.PushError($"[LobbyManager] {LastHostError}");
			TeardownHostedLobby(session);
			return false;
		}

		IsHost = true;

		LobbyPort = session.Port;

		// First, and before anything is opened or registered: this screen has to be able to play its own seat. A lobby the
		// host cannot control its own player in is not one anybody else should be shown either. The check waits for the
		// lobby to be described back over loopback, which is the last step of entering it as host.
		ReportHostLobbyStage(HostLobbyStage.AdmittingHost);
		if (!await VerifyLocalSeatBeforePublishingAsync(session))
		{
			TeardownHostedLobby(session);
			return false;
		}

		PlayerListUpdated?.Invoke();
		return true;
	}

	/// <summary>
	///     Rewrites the advertisement when the map or the number of seats it states has changed, which is the only part of it
	///     a host can change from the room. A lobby nobody has published yet has no advertisement to rewrite.
	/// </summary>
	private void RefreshHostedLobbyAdvertisement()
	{
		if (!IsHost || SteamLobbyId == 0) return;
		if (string.Equals(_publishedAdvertisementMap, ActiveMapName, StringComparison.Ordinal) && _publishedAdvertisementSeats == MaxPlayers) return;

		PublishHostedLobbyMetadata(_lobbySession?.MapHash ?? string.Empty, !string.IsNullOrEmpty(JoinCode));
	}

	/// <summary>
	///     Rewrites the advertisement Steam holds for this lobby. It is written when the lobby is published and again whenever
	///     the map or the number of seats changes, so the browser never offers a lobby whose stated map or size is not the one
	///     the host is playing. Nothing that is match state belongs here: seats, teams and settings live in the lobby protocol.
	/// </summary>
	/// <param name="mapHash">The content hash of the map the host runs.</param>
	/// <param name="requiresCode">Whether the lobby gates its seats with a join code, whose text is never published.</param>
	private void PublishHostedLobbyMetadata(string mapHash, bool requiresCode)
	{
		_publishedAdvertisementMap = ActiveMapName;
		_publishedAdvertisementSeats = MaxPlayers;
		ulong steamLobbyId = SteamLobbyId;
		if (steamLobbyId == 0) return;

		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.MapKey, ActiveMapName);
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.MapVersionKey, HostedMapVersion);
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.MapHashKey, mapHash);
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.EngineGenerationKey, RealmVersion.EngineGeneration.ToString(CultureInfo.InvariantCulture));
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.GameBuildKey, RealmVersion.GameBuildNumber);
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.ModeKey, "Melee");
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.SeatsKey, MaxPlayers.ToString(CultureInfo.InvariantCulture));
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.RequiresCodeKey, requiresCode ? "1" : "0");
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.MapTrustKey, _publishedMapTrust.IsTrusted ? "1" : "0");
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.MapTrustReasonKey, ((int)_publishedMapTrust.Reason).ToString(CultureInfo.InvariantCulture));
	}

	/// <summary>
	///     Reads how much Steam vouches for the map this lobby will run. The host is the side that holds the map, so it is
	///     the side that states the mark: a joiner cannot judge a map it has not downloaded, and asking every joiner to
	///     query the item would put a Workshop round trip in front of every join. A map that is not on the Workshop states
	///     itself as untrusted, which is exactly what a local map is.
	/// </summary>
	/// <returns>The mark this lobby advertises.</returns>
	private async Task<MapTrustState> ResolveHostedMapTrustAsync()
	{
		if (!SteamWorkshopService.IsAvailable) return MapTrustPolicy.EvaluateUnpublished();

		try
		{
			WorkshopMapEntry? item = await MapProvisioningService.FindLatestPublishedAsync(ActiveMapName);
			if (item == null) return MapTrustPolicy.EvaluateUnpublished();

			WorkshopMapEntry published = item.Value;
			return MapTrustPolicy.Evaluate(published.PublishedFileId, published.VotesUp, published.VotesDown, published.TimeUpdated);
		}
		catch (Exception exception)
		{
			GameLog.LogToConsole($"[LobbyManager] the Workshop could not vouch for '{ActiveMapName}', so the lobby states it as untrusted: {exception.Message}");
			return MapTrustPolicy.EvaluateUnpublished();
		}
	}

	/// <summary>
	///     Publishes the lobby <see cref="PrepareHostedLobbyAsync" /> opened: the port is opened in the local firewall and on
	///     the router, a registry node is asked to reach it, and the lobby is registered only once that node answered. The
	///     host may have changed any setting the lobby offers in between, and the match plays the settings the lobby holds
	///     when it starts.
	/// </summary>
	public Task<bool> PublishPreparedLobbyAsync() => PublishPreparedLobbyAsync(keepLobbyOnFailure: false);

	/// <summary>
	///     Publishes the prepared lobby without taking it away when it cannot be listed, which is what a host already sitting
	///     in its room needs: the lobby is open and reachable by anybody who is given its address, so a registry that cannot
	///     confirm it costs the host the listing rather than the lobby. The host is told in the room's own chat, because the
	///     screen that asked for the lobby has already moved on to it.
	/// </summary>
	/// <returns>Whether the lobby is listed on a registry node. False means it is open to players holding its address.</returns>
	public Task<bool> PublishPreparedLobbyDirectlyOnFailureAsync() => PublishPreparedLobbyAsync(keepLobbyOnFailure: true);

	/// <summary>
	///     Publishes the prepared lobby by creating the Steam lobby that stands for it. Steam owns the directory, the
	///     membership and the invites, so there is no port to open, no router to ask, no public node to reach the lobby first
	///     and no registry to register with: the lobby exists when Steam says it does, and it is found because Steam knows
	///     about it.
	/// </summary>
	/// <param name="keepLobbyOnFailure">
	///     Kept for the callers that used to keep a reachable-but-unlisted lobby open. Steam either created the lobby or it did
	///     not, so a failure here always takes the lobby down and this flag no longer changes anything.
	/// </param>
	/// <returns>Whether Steam now holds a lobby for this match.</returns>
	private async Task<bool> PublishPreparedLobbyAsync(bool keepLobbyOnFailure)
	{
		_ = keepLobbyOnFailure;

		ClientServerSession? prepared = _lobbySession;
		if (prepared == null || !prepared.IsRunning || !IsHost)
		{
			LastHostError = "There is no lobby waiting to be published.";
			GameLog.LogError($"[LobbyManager] {LastHostError}");
			return false;
		}

		if (!SteamLobbyDirectory.IsAvailable)
		{
			LastHostError = GameContext.Steam.UnavailableExplanation;
			GameLog.LogError($"[LobbyManager] {LastHostError}");
			TeardownHostedLobby(prepared);
			return false;
		}

		string mapDisplayName = string.IsNullOrEmpty(HostedMapDisplayName) ? ActiveMapName : HostedMapDisplayName;

		bool requiresCode = !string.IsNullOrEmpty(JoinCode);
		ulong steamLobbyId = SteamLobbyId;

		if (steamLobbyId == 0)
		{
			LastHostError = TranslationServer.Translate("This lobby has no Steam lobby behind it.");
			GameLog.LogError($"[LobbyManager] {LastHostError}");
			TeardownHostedLobby(prepared);
			return false;
		}

		IsUnlisted = HostedVisibility != ELobbyType.k_ELobbyTypePublic;
		SteamLobbyDirectory.SetVisibility(steamLobbyId, HostedVisibility);

		// Steam metadata is the advertisement, not the lobby state: seats, teams and settings stay in the lobby protocol.
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.HostSteamIdKey, GameContext.Steam.SteamId.ToString(CultureInfo.InvariantCulture));
		_publishedMapTrust = await ResolveHostedMapTrustAsync();
		PublishHostedLobbyMetadata(prepared.MapHash, requiresCode);

		LobbyPort = prepared.Port;

		NotifyHostLine(HostedVisibility == ELobbyType.k_ELobbyTypePublic
			? TranslationServer.Translate("This lobby is listed in Steam now; your friends can also join from their Steam friends list.")
			: TranslationServer.Translate("This lobby is open. Invite your Steam friends; they join from their friends list."));

		if (requiresCode) NotifyHostLine(TranslationServer.Translate("Players standing in this lobby need the join code before they can take a seat."));

		PlayerListUpdated?.Invoke();
		return true;
	}

	/// <summary>
	///     Keeps this lobby out of the lobby browser, or offers it there again. The lobby itself does not change: the same
	///     seat, the same Steam room and the same invited players stay, and only what a browsing player is shown changes.
	///     Offering it again is Steam's own visibility, so a lobby that becomes public is listed as soon as Steam says so.
	/// </summary>
	/// <param name="unlisted">Whether this lobby should stop being offered in the browser.</param>
	/// <returns>Whether the lobby is now in the state that was asked for.</returns>
	public Task<bool> SetLobbyUnlistedAsync(bool unlisted)
	{
		if (!IsHost || IsSinglePlayer) return Task.FromResult(false);

		HostedVisibility = unlisted ? ELobbyType.k_ELobbyTypePrivate : ELobbyType.k_ELobbyTypePublic;
		if (SteamLobbyId != 0) SteamLobbyDirectory.SetVisibility(SteamLobbyId, HostedVisibility);

		IsUnlisted = unlisted;
		NotifyHostLine(unlisted
			? TranslationServer.Translate("This lobby is private now, so it is not offered in the lobby browser. Players you invite can still join.")
			: TranslationServer.Translate("This lobby is offered in the lobby browser again."));
		PlayerListUpdated?.Invoke();
		return Task.FromResult(IsUnlisted == unlisted);
	}

	/// <summary>
	///     Opens a lobby only this screen can reach, so a single player picks a map, adds the AI players they want and starts
	///     the match from the same screen a hosted match begins in. Nothing about the machine's network is used: the authority
	///     binds the loopback address, takes whatever port it finds free, and no registry is told the lobby exists.
	/// </summary>
	/// <param name="mapPathName">The map whose lobby is opened.</param>
	/// <param name="explicitVersion">The map version to play, or the newest the screen holds when omitted.</param>
	/// <returns>Whether the lobby is open and administered by this screen.</returns>
	public async Task<bool> HostSinglePlayerLobbyAsync(string mapPathName, string? explicitVersion = null)
	{
		IsSinglePlayer = true;
		InitializeHostState(mapPathName, explicitVersion);
		LobbyPort = 0;

		string? mapDirectory = GameHost.ResolveMapDirectory(mapPathName, ActiveMapVersion);
		if (string.IsNullOrEmpty(mapDirectory))
		{
			LastHostError = $"The map folder for '{mapPathName}' could not be found.";
			GD.PushError($"[LobbyManager] {LastHostError}");
			return false;
		}

		var channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork");
		if (channel == null)
		{
			LastHostError = "This build has no channel to an authority.";
			GD.PushError($"[LobbyManager] {LastHostError}");
			return false;
		}

		GameContext.ServerSession?.Dispose();
		GameContext.DetachServerSession();

		var session = new ClientServerSession(channel);
		session.ConfigureLobbyIdentity(PlayerDisplayName, GameContext.Steam.SteamId);
		AttachLobbySession(session);

		var start = new ServerLobbyStart(
			ProjectSettings.GlobalizePath(mapDirectory),
			mapPathName,
			MaxPlayers,
			Path.Combine(OS.GetUserDataDir(), "logs"),
			OS.GetUserDataDir(),
			LocalizationManager.GetCurrentLanguageCode(),
			Cheats: true,
			LoopbackOnly: true);

		MatchStartResult lobbyStarted = await session.TryStartLobbyAsync(start);
		LastAuthorityLogPath = lobbyStarted.LogPath;
		LastAuthorityConsoleLogPath = lobbyStarted.ConsoleLogPath;
		if (!lobbyStarted.Started)
		{
			LastHostError = lobbyStarted.Failure;
			GD.PushError($"[LobbyManager] {lobbyStarted.Failure}");
			TeardownHostedLobby(session);
			return false;
		}

		string? admissionFailure = await WaitForLobbyAdmissionAsync(session);
		if (admissionFailure != null)
		{
			LastHostError = admissionFailure;
			GD.PushError($"[LobbyManager] {admissionFailure}");
			TeardownHostedLobby(session);
			return false;
		}

		if (!session.IsAdmin)
		{
			LastHostError = "The lobby's authority did not grant this client the capability that administers it.";
			GD.PushError($"[LobbyManager] {LastHostError}");
			TeardownHostedLobby(session);
			return false;
		}

		IsHost = true;
		LobbyPort = session.Port;

		if (!await VerifyLocalSeatBeforePublishingAsync(session))
		{
			TeardownHostedLobby(session);
			return false;
		}

		PlayerListUpdated?.Invoke();
		return true;
	}

	/// <summary>
	///     Checks the local round trip every hosted lobby depends on, whether or not anybody else will see it. A lobby only
	///     this machine plays still has to answer this machine, and a lobby other players can see has to answer this machine
	///     before it is worth publishing: the host controls its own player over this path, and nothing between this screen and
	///     the authority it launched is allowed to break that.
	/// </summary>
	private async Task<bool> VerifyLocalSeatBeforePublishingAsync(ClientServerSession session)
	{
		string? admissionFailure = await WaitForLobbyAdmissionAsync(session);
		if (admissionFailure == null) return true;

		LastHostError = $"this screen's own seat could not be confirmed over loopback on port {session.Port}: {admissionFailure}";
		GameLog.LogError($"[LobbyManager] {LastHostError}");
		return false;
	}

	/// <summary>States the step a host's lobby has reached, which the create screen shows while it waits.</summary>
	/// <param name="stage">The step that has begun.</param>
	private void ReportHostLobbyStage(HostLobbyStage stage) => HostLobbyStageChanged?.Invoke(stage);

	/// <summary>
	///     Collects the values of the match settings the lobby's map declared, which is what a match started from this screen
	///     plays with. A map that declared none hands over nothing and uses its own constants.
	/// </summary>
	/// <returns>The values the settings hold.</returns>
	private LobbySettingSet BuildLobbySettingSet()
	{
		var settings = new LobbySettingSet();
		for (int index = 0; index < MatchSettings.Count; index++)
		{
			settings.Set(MatchSettings[index].SettingId, MatchSettings[index].Value);
		}

		return settings;
	}

	private void InitializeHostState(string mapPathName, string? explicitVersion)
	{
		IsUnlisted = false;
		IsHost = true;
		MaxPlayers = ResolvePlayablePlayerSlots(mapPathName, explicitVersion);
		LastLobbyWarning = null;
		HostStability = HostStabilityTracker.GetOverallStability();
		IsGameStarted = false;
		SpectatorDelay = false;
		TeamsTogether = false;
		LastHostError = null;
		_downloadingMapName = null;
		PlayerList.Clear();
		ActiveMapName = mapPathName;
		ActiveMapVersion = !string.IsNullOrWhiteSpace(explicitVersion) ? explicitVersion : "1.0.0";
	}

	private static int ResolvePlayablePlayerSlots(string mapPathName, string? version)
	{
		try
		{
			string? manifestPath = MapAssetManager.FindManifestPath(mapPathName, version);
			string? mapDirectory = string.IsNullOrEmpty(manifestPath) ? null : Path.GetDirectoryName(manifestPath);
			if (string.IsNullOrEmpty(mapDirectory)) return MapInfoMetadata.DefaultPlayablePlayerSlots;

			var metadata = MapFileService.LoadMetadata(mapDirectory);
			return metadata.MapProperties?.GetPlayablePlayerSlots() ?? MapInfoMetadata.DefaultPlayablePlayerSlots;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[LobbyManager] Failed to resolve playable player slots for '{mapPathName}': {ex.Message}");
			return MapInfoMetadata.DefaultPlayablePlayerSlots;
		}
	}

}
