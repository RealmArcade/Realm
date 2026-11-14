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

		bool randomize = metadata.MapProperties?.TestRandomizeSlots ?? false;
		int? testSeed = metadata.MapProperties?.TestRandomSeed;

		GameContext.RandomService.SetSeed(testSeed);
		MatchSeed = GameContext.RandomService.Seed;

		int humanSlot = 1;
		var aiSlots = new List<int>();

		if (!randomize)
		{
			humanSlot = 1;
			for (int i = 0; i < aiCount; i++)
			{
				aiSlots.Add(2 + i);
			}
		}
		else
		{
			var available = Enumerable.Range(1, MaxPlayers).ToList();
			var rng = (testSeed.HasValue && testSeed.Value != 0) ? new Random(testSeed.Value) : new Random();
			int n = available.Count;
			while (n > 1)
			{
				n--;
				int k = rng.Next(n + 1);
				(available[k], available[n]) = (available[n], available[k]);
			}

			humanSlot = available[0];
			for (int i = 0; i < aiCount; i++)
			{
				aiSlots.Add(available[1 + i]);
			}
		}

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
			Options: MatchOptions);

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
		HiddenSettings = 0;
		HostedMapDisplayName = mapDisplayName;
		HostedMapVersion = string.IsNullOrWhiteSpace(explicitVersion) ? "1.0.0" : explicitVersion;

		ReportHostLobbyStage(HostLobbyStage.TestingNetwork);
		await RunNatTypeTestAsync();
		if (LocalNatType == NatType.Symmetric)
		{
			LastHostError = "Lobby creation rejected: Symmetric NAT is not supported.";
			GameLog.LogError("[LobbyManager] Lobby creation rejected: Symmetric NAT is not supported.");
			return false;
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

		ReportHostLobbyStage(HostLobbyStage.OpeningPort);

		// Asked for on every attempt, because a dismissed prompt leaves the port closed and is almost always a misclick
		// rather than a decision to host unreachable lobbies.
		LastFirewallGrant = await GameContext.WindowsFirewall.RequestInboundUdpForPortAsync(prepared.Port);

		await ResolvePublicEndpointAsync(prepared.Port);

		bool upnpSuccess = await GameContext.Upnp.OpenPortAsync(prepared.Port);
		bool isRestrictedNat = LocalNatType is NatType.RestrictedCone or NatType.PortRestrictedCone or NatType.Symmetric;
		IsRestrictedHost = !upnpSuccess && isRestrictedNat;
		if (IsRestrictedHost)
		{
			GameLog.LogToConsole("[LobbyManager] Warning: UPnP port mapping not available on restricted NAT. External players may require Direct Connect.");
		}

		Diagnostics.StartHostListener(prepared.Port + 1);

		// The lobby is only published once a public node has reached its game port, so nobody sees a lobby that a
		// timeout is waiting behind.
		bool registered = await RegisterLobbyWithRegistryAsync(ActiveMapName, mapDisplayName, HostedMapVersion, prepared.Port);
		if (!registered)
		{
			TeardownHostedLobby(prepared);
			return false;
		}

		PlayerListUpdated?.Invoke();
		return true;
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

	private void InitializeHostState(string mapPathName, string? explicitVersion)
	{
		ReleaseSeedingSlot();

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
