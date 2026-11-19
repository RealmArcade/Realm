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
	///     publishes it: the authority is listening and the Steam lobby carries its map, seats and join code, and only then
	///     can Steam place a player in it.
	/// </summary>
	public async Task<bool> HostLobbyAsync(string mapPathName, string mapDisplayName, string? explicitVersion = null)
	{
		return await PrepareHostedLobbyAsync(mapPathName, mapDisplayName, explicitVersion) && await PublishPreparedLobbyAsync();
	}

	/// <summary>
	///     Opens the lobby this screen will host without publishing it. The authority is started, this screen takes the seat
	///     that administers it, the map's own lobby rules have run, and the settings the match will play are the ones the
	///     lobby state names - so a host can read them and confirm them before anybody can see the lobby. Nothing about the
	///     machine's network is touched here: the authority's listener and the Steam lobby that advertises it are opened by
	///     <see cref="PublishPreparedLobbyAsync" />.
	/// </summary>
	public async Task<bool> PrepareHostedLobbyAsync(string mapPathName, string mapDisplayName, string? explicitVersion = null)
	{
		IsSinglePlayer = false;
		InitializeHostState(mapPathName, explicitVersion);
		LobbyPort = 0;
		HostedMapDisplayName = mapDisplayName;
		HostedMapVersion = string.IsNullOrWhiteSpace(explicitVersion) ? "1.0.0" : explicitVersion;

		string? installedMapDirectory = GameHost.ResolveMapDirectory(mapPathName, ActiveMapVersion);
		if (string.IsNullOrEmpty(installedMapDirectory))
		{
			LastHostError = $"The map folder for '{mapPathName}' could not be found.";
			return false;
		}

		// The lobby plays the installed package itself when nothing about it has to change, rather than copying it first: what
		// it advertises is read from the map's manifest, which the copy would carry unchanged, and every file a joiner asks
		// for is served from the same folder either way. Two folders are still copied - one Steam owns, which it rewrites on
		// an update under a match already running, and one an older build left behind, which has to be migrated durably.
		string? mapDirectory = GameContext.MapSessionCopy.TryResolvePlayableDirectory(installedMapDirectory);
		if (string.IsNullOrEmpty(mapDirectory))
		{
			LastHostError = GameContext.MapSessionCopy.LastRefusal;
			GameLog.LogError($"[LobbyManager] '{mapPathName}' cannot be played: {LastHostError}");
			return false;
		}

		ActiveMapSessionDirectory = mapDirectory;

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

		// The match scene reads this screen's seat from the session, so a lobby host is registered here exactly as a joiner
		// and a direct single-player match already are. Without it the world build falls back to matching the transport's
		// peer id against the lobby's seat list, and the two are not the same id.
		GameContext.AttachServerSession(session);

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
		// A refusal that the fallback then survives is remembered for the rest of the session, so the next lobby does not
		// pay for a whole start it already knows will fail before it gets to the one that works.
		MatchStartResult lobbyStarted = _preferredLobbyPort is int preferredPort
			? await session.TryStartLobbyAsync(start, preferredPort)
			: await session.TryStartLobbyAsync(start);
		if (!lobbyStarted.Started && _preferredLobbyPort is int refusedPort)
		{
			GameLog.LogToConsole($"[LobbyManager] UDP port {refusedPort} could not be taken for this lobby ({lobbyStarted.Failure}), so the socket takes the port the system chooses.");
			lobbyStarted = await session.TryStartLobbyAsync(start);
			if (lobbyStarted.Started) _preferredLobbyPort = null;
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

		// The admission wait above is the confirmation that this screen can play its own seat: the hello it resolved on came
		// back over the loopback connection this screen opened to the authority it launched, and the seat it administers was
		// granted with it. Waiting a second time asks for a hello that has already arrived and times out on a lobby the host
		// is already sitting in, which is what made every hosted Steam lobby fail its own seat check on port 8999.
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

		PublishHostedLobbyMetadata(!string.IsNullOrEmpty(JoinCode));
	}

	/// <summary>
	///     Rewrites the advertisement Steam holds for this lobby. It is written when the lobby is published and again whenever
	///     the map or the number of seats changes, so the browser never offers a lobby whose stated map or size is not the one
	///     the host is playing. Nothing that is match state belongs here: seats, teams and settings live in the lobby protocol.
	/// </summary>
	/// <param name="requiresCode">Whether the lobby gates its seats with a join code, whose text is never published.</param>
	private void PublishHostedLobbyMetadata(bool requiresCode)
	{
		_publishedAdvertisementMap = ActiveMapName;
		_publishedAdvertisementSeats = MaxPlayers;
		ulong steamLobbyId = SteamLobbyId;
		if (steamLobbyId == 0) return;

		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.MapKey, ActiveMapName);
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.MapVersionKey, HostedMapVersion);
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.MapWorkshopIdKey, DeliverableMapWorkshopId().ToString(CultureInfo.InvariantCulture));
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.MapHashKey, AdvertisedMapHash());
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.EngineGenerationKey, RealmVersion.EngineGeneration.ToString(CultureInfo.InvariantCulture));
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.GameBuildKey, RealmVersion.GameBuildNumber);
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.ModeKey, "Melee");
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.SeatsKey, MaxPlayers.ToString(CultureInfo.InvariantCulture));
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.RequiresCodeKey, requiresCode ? "1" : "0");
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.MapTrustKey, _publishedMapTrust.IsTrusted ? "1" : "0");
		SteamLobbyDirectory.SetData(steamLobbyId, SteamLobbyDirectory.MapTrustReasonKey, ((int)_publishedMapTrust.Reason).ToString(CultureInfo.InvariantCulture));
	}

	/// <summary>
	///     The package hash this lobby advertises, which is the hash of the manifest the host will play from. A joiner holds
	///     the same package only when its manifest is the same file, and reading that costs one file read on either side,
	///     so the join can decide whether it needs the host's copy before it connects. A folder with no manifest states
	///     nothing, and the join is then settled by what is installed.
	/// </summary>
	/// <returns>The package hash, or an empty string when the map states none.</returns>
	private string AdvertisedMapHash()
	{
		string? mapDirectory = GameHost.ResolveMapDirectory(ActiveMapName, ActiveMapVersion);
		return string.IsNullOrEmpty(mapDirectory) ? string.Empty : MapSessionCopyService.ReadStatedPackageHash(mapDirectory);
	}

	/// <summary>
	///     The Workshop item Steam can hand this lobby's exact map from, or zero when Steam cannot. A map whose files were
	///     imported from a package is a copy this machine holds even when its author published one, because the item Steam
	///     would deliver is the folder it installs and not this copy; a joiner can then only be given what the host has,
	///     and the advertisement says so by naming no item.
	/// </summary>
	/// <returns>The item whose installed folder the lobby plays from, or zero.</returns>
	private ulong DeliverableMapWorkshopId()
	{
		if (_publishedMapWorkshopId == 0) return 0;

		string? mapDirectory = GameHost.ResolveMapDirectory(ActiveMapName, ActiveMapVersion);
		string? itemFolder = SteamWorkshopService.GetInstalledFolder(_publishedMapWorkshopId);
		if (string.IsNullOrEmpty(mapDirectory) || string.IsNullOrEmpty(itemFolder)) return 0;

		string relative = Path.GetRelativePath(Path.GetFullPath(itemFolder), Path.GetFullPath(mapDirectory));
		bool insideTheItem = !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
		return insideTheItem ? _publishedMapWorkshopId : 0;
	}

	/// <summary>
	///     Reads how much Steam vouches for the map this lobby will run. The host is the side that holds the map, so it is
	///     the side that states the mark: a joiner cannot judge a map it has not downloaded, and asking every joiner to
	///     query the item would put a Workshop round trip in front of every join. A map that is not on the Workshop states
	///     itself as untrusted, which is exactly what a local map is.
	/// </summary>
	/// <remarks>
	///     The item this lobby names is the one this account published for the map when there is one, because that is the
	///     item the host's own copy came from and the only one every joiner can be given. A name lookup across every
	///     creator can answer an item somebody else published under the same map name, which no joiner could match against
	///     the host's package.
	/// </remarks>
	/// <returns>The mark this lobby advertises.</returns>
	private async Task<MapTrustState> ResolveHostedMapTrustAsync()
	{
		if (!SteamWorkshopService.IsAvailable) return MapTrustPolicy.EvaluateUnpublished();

		try
		{
			WorkshopMapEntry? item = await SteamWorkshopService.FindOwnedByMapNameAsync(ActiveMapName, GameContext.Steam.SteamId, CancellationToken.None)
				?? await MapProvisioningService.FindLatestPublishedAsync(ActiveMapName);
			if (item == null) return MapTrustPolicy.EvaluateUnpublished();

			WorkshopMapEntry published = item.Value;
			_publishedMapWorkshopId = published.PublishedFileId;
			return MapTrustPolicy.Evaluate(published.PublishedFileId, published.AcceptedForUse, published.VotesUp, published.VotesDown, published.TimeUpdated);
		}
		catch (Exception exception)
		{
			GameLog.LogToConsole($"[LobbyManager] the Workshop could not vouch for '{ActiveMapName}', so the lobby states it as untrusted: {exception.Message}");
			return MapTrustPolicy.EvaluateUnpublished();
		}
	}

	/// <summary>
	///     Publishes the lobby <see cref="PrepareHostedLobbyAsync" /> opened by creating the Steam lobby that stands for it.
	///     Steam owns the directory, the membership and the invites, so there is no port to open, no router to ask, no public
	///     node to reach the lobby first and no registry to register with: the lobby exists when Steam says it does, and a
	///     failure takes it down rather than leaving it open to an address nobody was given.
	/// </summary>
	/// <returns>Whether Steam now holds a lobby for this match.</returns>
	public async Task<bool> PublishPreparedLobbyAsync()
	{
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
		PublishHostedLobbyMetadata(requiresCode);

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
	///     binds the loopback address, takes whatever port it finds free, and no Steam lobby is opened to advertise it.
	/// </summary>
	/// <param name="mapPathName">The map whose lobby is opened.</param>
	/// <param name="explicitVersion">The map version to play, or the newest the screen holds when omitted.</param>
	/// <returns>Whether the lobby is open and administered by this screen.</returns>
	public async Task<bool> HostSinglePlayerLobbyAsync(string mapPathName, string? explicitVersion = null)
	{
		IsSinglePlayer = true;
		InitializeHostState(mapPathName, explicitVersion);
		LobbyPort = 0;

		// Nothing about this lobby is on Steam: there is no lobby to advertise, no membership and no code to compare. Clearing
		// the id keeps every later Steam-bound call a no-op rather than pointing it at the last lobby this screen hosted.
		SteamLobbyId = 0;
		ActiveLobbyId = string.Empty;

		string? installedMapDirectory = GameHost.ResolveMapDirectory(mapPathName, ActiveMapVersion);
		if (string.IsNullOrEmpty(installedMapDirectory))
		{
			LastHostError = $"The map folder for '{mapPathName}' could not be found.";
			GD.PushError($"[LobbyManager] {LastHostError}");
			return false;
		}

		// A single-player lobby advertises nothing and transfers nothing, so a map of the player's own whose data this build
		// does not migrate is read where it sits: opening the lobby then costs nothing proportional to the package, which is
		// what the editor's own playtest has always done and why this path was slower than it. Two folders are still copied:
		// one an older build left behind, because it has to be migrated durably, and one Steam installed as a Workshop item,
		// because Steam rewrites that folder on its next update underneath a match already running on it.
		string? mapDirectory = GameContext.MapSessionCopy.TryResolvePlayableDirectory(installedMapDirectory);
		if (string.IsNullOrEmpty(mapDirectory))
		{
			LastHostError = GameContext.MapSessionCopy.LastRefusal;
			GD.PushError($"[LobbyManager] {LastHostError}");
			return false;
		}

		ActiveMapSessionDirectory = mapDirectory;

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

		// The match scene reads this screen's seat from the session, so a lobby host is registered here exactly as a joiner
		// and a direct single-player match already are. Without it the world build falls back to matching the transport's
		// peer id against the lobby's seat list, and the two are not the same id.
		GameContext.AttachServerSession(session);

		var start = new ServerLobbyStart(
			ProjectSettings.GlobalizePath(mapDirectory),
			mapPathName,
			MaxPlayers,
			Path.Combine(OS.GetUserDataDir(), "logs"),
			OS.GetUserDataDir(),
			LocalizationManager.GetCurrentLanguageCode(),
			Cheats: true,
			LoopbackOnly: true);

		ReportHostLobbyStage(HostLobbyStage.LoadingMap);
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

		ReportHostLobbyStage(HostLobbyStage.AdmittingHost);

		// The hello above is the confirmation: it came back over the loopback connection this screen opened to the authority
		// it launched, which is the same seat check a hosted lobby makes before it is worth publishing. Asking a second time
		// would wait on a hello that has already arrived and time out on a lobby that is already playable.
		string? admissionFailure = await WaitForLobbyAdmissionAsync(session, Tr("The match did not start in time."));
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

		PlayerListUpdated?.Invoke();
		return true;
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
