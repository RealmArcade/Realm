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
using Realm.Shared.Reachability;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Network;
public partial class LobbyManager
{
	public async Task<bool> HostSinglePlayerGame(string mapPathName, string mapDisplayName, string? mapVersion = null, bool debugMapScript = false)
	{
		ReleaseSeedingSlot();

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
			AccountId = AccountId,
			Name = PlayerDisplayName,
			Faction = "HUMAN",
			Team = $"Team {humanSlot}",
			Color = PlayerColorConfig.GetColor(humanSlot),
			IsHost = true,
			Latency = "0 ms",
			Jitter = "0 ms",
			PacketLoss = "0%",
			BinaryVersion = RealmVersion.GameBinaryVersion,
			SessionToken = LobbySessionToken
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
				BinaryVersion = RealmVersion.GameBinaryVersion,
				SessionToken = string.Empty
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
		LastHostFailure = null;
		LastFirewallGrant = default;
		LobbyPort = 0;
		HostedMapDisplayName = mapDisplayName;
		HostedMapVersion = string.IsNullOrWhiteSpace(explicitVersion) ? "1.0.0" : explicitVersion;

		ReportHostLobbyStage(HostLobbyStage.TestingNetwork);
		await RunNatTypeTestAsync();

		// The classification is advisory: it says how hard this network usually is to reach, not whether this lobby can be
		// reached. Whether anybody can connect is decided by the reachability probe before the lobby is published, and by
		// the connections players make afterwards, so a symmetric NAT is recorded and hosting goes ahead.
		if (LocalNatType == NatType.Symmetric)
		{
			GameLog.LogToConsole("[LobbyManager] This network reported a symmetric NAT. Hosting continues; the lobby is published only once a public node reaches it.");
		}

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
		session.ConfigureLobbyIdentity(PlayerDisplayName, LobbySessionToken, AccountId, GameContext.Steam.SteamId);
		AttachLobbySession(session);

		var start = new ServerLobbyStart(
			ProjectSettings.GlobalizePath(mapDirectory),
			mapPathName,
			MaxPlayers,
			Path.Combine(OS.GetUserDataDir(), "logs"),
			OS.GetUserDataDir(),
			LocalizationManager.GetCurrentLanguageCode());

		ReportHostLobbyStage(HostLobbyStage.LoadingMap);
		MatchStartResult lobbyStarted = await session.TryStartLobbyAsync(start, ENetPort);
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
	///     Publishes the lobby <see cref="PrepareHostedLobbyAsync" /> opened: the port is opened in the local firewall and on
	///     the router, a registry node is asked to reach it, and the lobby is registered only once that node answered. The
	///     host may have changed any setting the lobby offers in between, and the match plays the settings the lobby holds
	///     when it starts.
	/// </summary>
	public async Task<bool> PublishPreparedLobbyAsync()
	{
		ClientServerSession? prepared = _lobbySession;
		if (prepared == null || !prepared.IsRunning || !IsHost)
		{
			LastHostError = "There is no lobby waiting to be published.";
			GameLog.LogError($"[LobbyManager] {LastHostError}");
			return false;
		}

		string mapDisplayName = string.IsNullOrEmpty(HostedMapDisplayName) ? ActiveMapName : HostedMapDisplayName;

		IsDirectLobby = false;
		ReportHostLobbyStage(HostLobbyStage.OpeningPort);
		await OpenLobbyEndpointAsync(prepared.Port);

		Diagnostics.StartHostListener(prepared.Port + 1);

		// The lobby is only published once a public node has reached its game port, so nobody sees a lobby that a
		// timeout is waiting behind.
		bool registered = await RegisterLobbyWithRegistryAsync(ActiveMapName, mapDisplayName, HostedMapVersion, prepared.Port);
		if (!registered)
		{
			TeardownHostedLobby(prepared);
			return false;
		}

		StartHostReachabilityKeepAlive();

		PlayerListUpdated?.Invoke();
		return true;
	}

	/// <summary>
	///     Opens a lobby that only a player holding its address can reach, so hosting does not depend on a registry: the
	///     lobby is prepared exactly as a listed one is, its port is opened in the local firewall and on the router, and it
	///     is not registered anywhere. The host hands the address the lobby room shows to the players who should join.
	/// </summary>
	/// <param name="mapPathName">The map whose lobby is opened.</param>
	/// <param name="mapDisplayName">The name the lobby lists the map under.</param>
	/// <param name="explicitVersion">The map version to play, or the newest the screen holds when omitted.</param>
	/// <returns>Whether the lobby is open, administered by this screen and waiting for players it can be told about.</returns>
	public async Task<bool> HostDirectLobbyAsync(string mapPathName, string mapDisplayName, string? explicitVersion = null)
	{
		return await PrepareHostedLobbyAsync(mapPathName, mapDisplayName, explicitVersion) && await PublishDirectLobbyAsync();
	}

	/// <summary>
	///     Opens the joining half of the lobby <see cref="PrepareHostedLobbyAsync" /> prepared without a registry: the port is
	///     opened for players on other machines and the address they can reach is measured. Nothing can refuse a direct
	///     lobby for reachability, because no public node was asked to reach it - the host shares the address and the players
	///     who receive it are the check. What a router accepting a mapping does decide is whether the address outside this
	///     network is offered at all, because an address nothing confirmed is one a host would be sending at random.
	/// </summary>
	/// <returns>Whether the lobby now accepts players on its address.</returns>
	public async Task<bool> PublishDirectLobbyAsync()
	{
		ClientServerSession? prepared = _lobbySession;
		if (prepared == null || !prepared.IsRunning || !IsHost)
		{
			LastHostError = "There is no lobby waiting to be shared.";
			GameLog.LogError($"[LobbyManager] {LastHostError}");
			return false;
		}

		ReportHostLobbyStage(HostLobbyStage.OpeningPort);
		await OpenLobbyEndpointAsync(prepared.Port);

		IsDirectLobby = true;
		ActiveLobbyId = null;
		GameLog.LogToConsole($"[LobbyManager] This lobby is not listed anywhere; share {DirectConnectAddress} with the players who should join.");
		if (PublicConnectAddress is { Length: > 0 } publicAddress)
		{
			GameLog.LogToConsole($"[LobbyManager] This lobby's router accepted a forward, so players outside this network join at {publicAddress}.");
		}
		else if (MeasuredPublicConnectAddress is { Length: > 0 } measuredAddress)
		{
			GameLog.LogToConsole($"[LobbyManager] Nothing confirmed this lobby is reachable from outside; {measuredAddress} is the address its router maps the port to, which works once UDP port {prepared.Port} is forwarded.");
		}
		else if (RouterReachability == RouterReachability.PublicAddressUnmeasured)
		{
			GameLog.LogToConsole($"[LobbyManager] This network measured no outside address of its own, so nothing confirmed this lobby is reachable from outside; the router's forward for UDP port {prepared.Port} is untested.");
		}

		PlayerListUpdated?.Invoke();
		return true;
	}

	/// <summary>
	///     Makes a prepared lobby's game port reachable and measures the address it is reached on. A dismissed firewall prompt
	///     is recorded rather than treated as a failure: hosting still works on this machine, and the lobby room states what
	///     other machines will meet.
	/// </summary>
	/// <param name="port">The game port the lobby listens on.</param>
	private async Task OpenLobbyEndpointAsync(int port)
	{
		// Asked for on every attempt, because a dismissed prompt leaves the port closed and is almost always a misclick
		// rather than a decision to host unreachable lobbies.
		LastFirewallGrant = await GameContext.WindowsFirewall.RequestInboundUdpForPortAsync(port);

		DiscoverOfferedEndpoints(port);

		// A machine that offers an IPv6 address is promising a family it has never seen its own game port answer in, and that
		// promise rests on Godot's transport holding a socket in both families. Asking this machine's own loopback over IPv6
		// is the cheapest way to find out it no longer does, and it costs one datagram when it does. It is asked on loopback
		// so the answer is about the transport rather than about a firewall: no inbound rule is consulted for traffic this
		// machine sends itself.
		if (FirstOffer(LobbyAddressFamily.IPv6) is { } offeredIpv6)
		{
			bool answersOverIpv6 = await LobbyPortCheck.AnswersAsync(IPAddress.IPv6Loopback, port, cancellationToken: CancellationToken.None);
			if (answersOverIpv6)
			{
				GameLog.LogToConsole($"[LobbyManager] The game port answers over IPv6 on this machine, so {offeredIpv6.Describe()} is a route players with IPv6 can take.");
			}
			else
			{
				GameLog.LogError($"[LobbyManager] This machine holds the IPv6 address {offeredIpv6.Describe()}, but its own game port did not answer over IPv6. Nothing about the router or the firewall is involved in this check, so the transport is what to look at: a build whose ENet no longer binds both address families looks exactly like this.");
			}
		}

		// A lobby offered over IPv6 alone is a lobby with nothing for a router to do: the address it is reached at is the
		// one this machine holds, so there is no translation to map and no public endpoint to measure. The router half is
		// skipped entirely rather than allowed to fail, because nothing was asked of it.
		if (!_offersIpv4)
		{
			_hostPublicIp = null;
			_hostPublicPort = 0;
			_routerReachability = RouterReachability.NoRouterForward;
			IsRestrictedHost = false;

			if (_offeredIpv6Endpoint is { } onlyEndpoint)
			{
				GameLog.LogToConsole($"[LobbyManager] This machine holds no IPv4 address other than loopback, so the lobby is offered over IPv6 only at {onlyEndpoint.Describe()}; that address needs no router mapping.");
			}

			return;
		}

		await ResolvePublicEndpointAsync(port);

		bool upnpSuccess = await GameContext.Upnp.OpenPortAsync(port);

		// A mapping is the only confirmation a lobby that is not listed anywhere can gather on its own, but accepting one is
		// not by itself proof: the address the mapping serves traffic at is the router's own outside face, and only that
		// face reaching the network the measurement saw makes the measured address one somebody outside can join at. A
		// router behind another one accepts the mapping and answers with a private address of its own, and the forward it
		// holds then carries nothing from the carrier's network inward.
		// The port advertised is the one the mapping states rather than the one the STUN query saw the router rewrite,
		// because a static forward sets the external port itself.
		string routerExternalAddress = upnpSuccess ? GameContext.Upnp.QueryExternalAddress() : string.Empty;
		string measuredAddress = _hostPublicIp ?? string.Empty;

		// Recorded before the confirmed case below returns, because a lobby that was warned about on an earlier attempt has
		// nothing else to clear the warning with: a later attempt that does secure a forward would otherwise leave the
		// lobby room saying the router is doing nothing beside the sentence confirming that it is.
		bool isRestrictedNat = LocalNatType is NatType.RestrictedCone or NatType.PortRestrictedCone or NatType.Symmetric;
		IsRestrictedHost = !upnpSuccess && isRestrictedNat;
		if (IsRestrictedHost)
		{
			GameLog.LogToConsole($"[LobbyManager] Warning: UPnP port mapping not available on {LocalNatType} NAT. External players may require Direct Connect.");
		}

		if (!upnpSuccess)
		{
			_routerReachability = RouterReachability.NoRouterForward;
		}
		else if (routerExternalAddress.Length == 0)
		{
			_routerReachability = RouterReachability.RouterAddressUnreadable;
		}
		else if (measuredAddress.Length == 0)
		{
			// The measurement is the only thing that says which network this machine sits behind, so without one there are
			// not two addresses to compare and the router cannot be called a second network: it may well be the only one,
			// and sending the host to look for a forward somewhere else would be a search for a machine that is not there.
			_routerReachability = RouterReachability.PublicAddressUnmeasured;
		}
		else if (string.Equals(routerExternalAddress, measuredAddress, StringComparison.Ordinal))
		{
			_routerReachability = RouterReachability.Confirmed;
		}
		else
		{
			_routerReachability = RouterReachability.BehindAnotherNetwork;
		}

		if (_routerReachability == RouterReachability.Confirmed)
		{
			_hostPublicPort = port;
			return;
		}

		if (upnpSuccess)
		{
			string reason = _routerReachability switch
			{
				RouterReachability.RouterAddressUnreadable =>
					$"the router accepted a forward for UDP port {port} but stated no outside address of its own",
				RouterReachability.PublicAddressUnmeasured =>
					$"the router accepted a forward for UDP port {port} and gives its outside address as '{routerExternalAddress}', but this network measured no outside address of its own to compare it with",
				_ => $"the router accepted a forward for UDP port {port} but gives its outside address as '{routerExternalAddress}' while this network measured '{measuredAddress}'"
			};
			GameLog.LogToConsole($"[LobbyManager] {reason}; nothing outside was reached through that forward, so the measured address is offered as the guess it is.");
		}
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
		LastHostFailure = null;
		LastFirewallGrant = default;
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
		session.ConfigureLobbyIdentity(PlayerDisplayName, LobbySessionToken, AccountId, GameContext.Steam.SteamId);
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
		HostReachabilityResult loopback = await VerifyLobbyLoopbackAsync(session);
		if (loopback.Verified) return true;

		LastHostFailure = loopback;
		LastHostError = $"this screen's own seat could not be confirmed over loopback on port {session.Port}: {loopback.Detail}";
		GameLog.LogError($"[LobbyManager] {LastHostError}");
		return false;
	}

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
		ReleaseSeedingSlot();

		IsDirectLobby = false;
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
