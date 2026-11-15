using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Reachability;
using Realm.Shared.Services;

namespace Realm.Client.Network;

/// <summary>
///     The half of a host handover this screen performs: measuring where everybody stands, offering and answering an
///     offer, rehearsing a candidate's own authority against every seated player, and, once both parties confirmed, moving
///     this screen to the authority the lobby now belongs to.
/// </summary>
public partial class LobbyManager
{
	/// <summary>The fewest answers one advice measurement collects from a seat before its row is read.</summary>
	private const int AdviceProbeSamples = 6;

	private const int RehearsalProbeMilliseconds = 2500;

	private const int AdviceProbeMilliseconds = 600;

	/// <summary>How long one advice round waits, per slot, so twelve screens do not probe each other at the same instant.</summary>
	private const int AdviceStaggerMilliseconds = 250;

	private const int AdviceRoundCooldownMilliseconds = 45000;

	public LobbyHandoverStage HandoverStage { get; private set; }

	/// <summary>The seat that would become the host, or zero while nobody offered.</summary>
	public int HandoverCandidatePeerId { get; private set; }

	/// <summary>The seat that started the conversation, or zero while nobody offered.</summary>
	public int HandoverOffererPeerId { get; private set; }

	/// <summary>The seat a measurement round suggested could carry this lobby better, or zero while nothing was suggested.</summary>
	public int SuggestedCandidatePeerId { get; private set; }

	/// <summary>The sentence the lobby states about its handover, as the key it is translated by.</summary>
	public string HandoverNoticeKey { get; private set; } = string.Empty;

	/// <summary>The sentence a player reads about the lobby's handover, in the language the screen is running in.</summary>
	public string HandoverNoticeText => HandoverNoticeTexts.Translate(HandoverNoticeKey);

	/// <summary>How long the stage the lobby is in has left before it ends on its own.</summary>
	public int HandoverSecondsRemaining { get; private set; }

	/// <summary>The port of the lobby's handover probe relay, or zero when this lobby cannot measure a handover.</summary>
	public int ProbeRelayPort => _probeRelayPort;

	/// <summary>The measurements the two parties of a handover were shown, or an empty report.</summary>
	public LobbyHandoverReport HandoverReport { get; private set; }

	/// <summary>Whether this screen holds a measured report to show.</summary>
	public bool HasHandoverReport { get; private set; }

	/// <summary>Whether a measurement round named this screen as a better-placed host.</summary>
	public bool IsSuggestedHostCandidate => SuggestedCandidatePeerId != 0 && SuggestedCandidatePeerId == LocalPeerId;

	/// <summary>Whether this screen is the seat that would take the lobby over.</summary>
	public bool IsHandoverCandidate => HandoverCandidatePeerId != 0 && HandoverCandidatePeerId == LocalPeerId;

	/// <summary>Whether this screen is one of the two parties a handover is between.</summary>
	public bool IsHandoverParty => HandoverCandidatePeerId != 0 && (IsHandoverCandidate || IsHost);

	/// <summary>Raised whenever anything about the lobby's handover changed, so the room can redraw it.</summary>
	public event Action? HandoverStateChanged;

	/// <summary>Raised when a measurement round suggests this screen and the host should consider a switch.</summary>
	public event Action? HandoverSuggested;

	private int _probeRelayPort;

	private LobbyHandoverProbeService? _handoverProbe;

	private string _handoverProbeHost = string.Empty;

	private string _handoverProbeToken = string.Empty;

	private long _nextProbeAttemptTicks;

	private CancellationTokenSource? _adviceWork;

	private CancellationTokenSource? _rehearsalWork;

	private int _lastAdviceSeatCount;

	private int _lastSuggestedCandidatePeerId;

	private long _nextAdviceRoundTicks;

	private int _handoverRehearsalPort;

	private string _handoverRehearsalAdminToken = string.Empty;

	private string _handoverRehearsalMapHash = string.Empty;

	private LobbyReachabilitySet? _handoverReachability;

	private LobbyHandoverAdoption? _handoverAdoption;

	private LobbyHandoverSeatCredential[] _handoverCredentials = [];

	private string _joinedMapVersion = "1.0.0";

	/// <summary>
	///     The credential this seat was handed when the lobby moved, or empty while it holds the one it joined with. It
	///     outlives the move so a reconnect to the authority that now holds the lobby is the same seat.
	/// </summary>
	private string _movedSeatToken = string.Empty;

	private int _handoverExpectedHumanSeats;

	private CancellationTokenSource? _settleWork;

	/// <summary>Whether this screen offered itself as the lobby's host and may do so now.</summary>
	public bool CanOfferThisSeatAsHost => !IsHost && !IsSinglePlayer && HandoverStage == LobbyHandoverStage.None && !IsCountdownActive && _probeRelayPort > 0 && LocalPeerId > 0;

	/// <summary>Whether this screen hosts the lobby and may offer the role to a named player now.</summary>
	public bool CanOfferHandover => IsHost && !IsSinglePlayer && HandoverStage == LobbyHandoverStage.None && !IsCountdownActive && _probeRelayPort > 0;

	/// <summary>
	///     Whether the two parties have seen a passing rehearsal and can still answer it. The handover is not official on
	///     the verdict; it is official when both have confirmed after reading it.
	/// </summary>
	public bool CanConfirmHandover => IsHandoverParty
		&& HandoverStage == LobbyHandoverStage.Decided
		&& HasHandoverReport
		&& HandoverReport.Verdict == (int)LobbyHandoverVerdict.Passed;

	/// <summary>Offers this screen's own seat as the lobby's host, which the host answers.</summary>
	public void OfferThisSeatAsHost() => SendHandoverCommand(LobbyCommandKind.OfferHostHandover, 0);

	/// <summary>Offers the lobby's host role to one seated player, which that player answers.</summary>
	/// <param name="peerId">The seat the role is offered to.</param>
	public void OfferHandoverTo(int peerId) => SendHandoverCommand(LobbyCommandKind.OfferHostHandover, peerId);

	/// <summary>Accepts the handover this screen is a party to, which begins the rehearsal.</summary>
	public void AcceptHandoverOffer() => SendHandoverCommand(LobbyCommandKind.AcceptHostHandover, 0);

	/// <summary>Refuses the handover this screen is a party to, which ends it for good.</summary>
	public void RefuseHandoverOffer() => SendHandoverCommand(LobbyCommandKind.RefuseHostHandover, 0);

	/// <summary>Cancels the handover this screen is a party to, which returns the lobby to exactly what it was.</summary>
	public void WithdrawHandoverOffer() => SendHandoverCommand(LobbyCommandKind.WithdrawHostHandover, 0);

	/// <summary>Confirms the handover after reading the numbers, which is one of the two yeses it becomes official on.</summary>
	public void ConfirmHandoverOffer() => SendHandoverCommand(LobbyCommandKind.ConfirmHostHandover, 0);

	private void SendHandoverCommand(LobbyCommandKind kind, int targetPeerId)
	{
		ClientServerSession? session = _lobbySession;
		if (session == null || !session.IsRunning) return;

		session.SendLobbyCommand(new LobbyCommand
		{
			Kind = kind,
			TargetPeerId = targetPeerId
		});
	}

	/// <summary>
	///     Applies the handover the lobby published, starts or stops the work it asks this screen for, and tells the room.
	///     The measurements themselves never travel in the published state: they reach the two parties alone.
	/// </summary>
	/// <param name="state">The lobby state the authority published.</param>
	private void ApplyHandoverState(LobbyStateMessage state)
	{
		if (!string.IsNullOrEmpty(state.MapVersion) && !string.Equals(state.MapVersion, _joinedMapVersion, StringComparison.Ordinal)) _joinedMapVersion = state.MapVersion;

		LobbyHandoverStage previousStage = HandoverStage;
		int previousCandidate = HandoverCandidatePeerId;
		int previousOfferer = HandoverOffererPeerId;
		string previousNotice = HandoverNoticeKey;
		int previousSeconds = HandoverSecondsRemaining;

		LobbyHandoverState published = state.Handover;
		HandoverStage = Enum.IsDefined(typeof(LobbyHandoverStage), published.Stage) ? (LobbyHandoverStage)published.Stage : LobbyHandoverStage.None;
		HandoverCandidatePeerId = Math.Max(0, published.CandidatePeerId);
		HandoverOffererPeerId = Math.Max(0, published.OffererPeerId);
		SuggestedCandidatePeerId = Math.Max(0, published.SuggestedCandidatePeerId);
		HandoverNoticeKey = published.Notice ?? string.Empty;
		HandoverSecondsRemaining = Math.Max(0, published.SecondsRemaining);

		int effectiveProbePort = state.ProbeRelayPort > 0 ? state.ProbeRelayPort : 0;
		if (effectiveProbePort != _probeRelayPort)
		{
			_probeRelayPort = effectiveProbePort;
			StopHandoverProbe();
		}

		EnsureHandoverProbe();

		if (HandoverStage == LobbyHandoverStage.Rehearsing && IsHandoverCandidate && _rehearsalWork == null) StartRehearsal();
		if (previousStage == LobbyHandoverStage.Rehearsing && HandoverStage != LobbyHandoverStage.Rehearsing)
		{
			// The authority the candidate put up is only given back when the offer ended before it was confirmed: once the
			// lobby is committing, that authority is the one the seats are about to arrive at.
			CancelRehearsal(HandoverStage is LobbyHandoverStage.None or LobbyHandoverStage.Offered);
		}

		if (HandoverStage == LobbyHandoverStage.None && previousStage != LobbyHandoverStage.None && previousStage != LobbyHandoverStage.Committing)
		{
			HasHandoverReport = false;
			HandoverReport = default;
		}

		if (SuggestedCandidatePeerId != _lastSuggestedCandidatePeerId)
		{
			_lastSuggestedCandidatePeerId = SuggestedCandidatePeerId;
			if (SuggestedCandidatePeerId != 0 && (IsHost || IsSuggestedHostCandidate)) HandoverSuggested?.Invoke();
		}

		ScheduleAdviceRound();

		if (previousStage != HandoverStage || previousCandidate != HandoverCandidatePeerId || previousOfferer != HandoverOffererPeerId || !string.Equals(previousNotice, HandoverNoticeKey, StringComparison.Ordinal) || previousSeconds != HandoverSecondsRemaining) HandoverStateChanged?.Invoke();
	}

	/// <summary>
	///     Opens this screen's probe socket once the lobby publishes a relay and this seat has a probe token to name itself
	///     by. A screen that cannot open one simply contributes no advice and can still be seated in a handover.
	/// </summary>
	private void EnsureHandoverProbe()
	{
		if (_probeRelayPort <= 0 || _handoverProbe != null) return;
		if (string.IsNullOrEmpty(_connectedHostIp)) return;
		if (System.Environment.TickCount64 < _nextProbeAttemptTicks) return;
		_nextProbeAttemptTicks = System.Environment.TickCount64 + 5000;

		string ownToken = MySeatProbeToken();
		if (ownToken.Length == 0) return;

		if (_handoverProbe != null && _handoverProbeToken == ownToken && string.Equals(_handoverProbeHost, _connectedHostIp, StringComparison.Ordinal)) return;

		StopHandoverProbe();
		IPAddress? relayAddress = ResolveRelayAddress(_connectedHostIp);
		if (relayAddress == null) return;

		LobbyHandoverProbeService? probe = LobbyHandoverProbeService.TryStart(relayAddress, _probeRelayPort, ownToken, out string failure);
		if (probe == null)
		{
			GameLog.LogToConsole($"[LobbyManager] This screen cannot measure a host handover: {failure}");
			return;
		}

		_handoverProbe = probe;
		_handoverProbeHost = _connectedHostIp;
		_handoverProbeToken = ownToken;
	}

	private static IPAddress? ResolveRelayAddress(string host)
	{
		if (IPAddress.TryParse(host, out IPAddress? parsed)) return parsed;

		try
		{
			IPAddress[] addresses = Dns.GetHostAddresses(host);
			for (int index = 0; index < addresses.Length; index++)
			{
				if (addresses[index].AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return addresses[index];
			}

			return addresses.Length > 0 ? addresses[0] : null;
		}
		catch (Exception exception) when (exception is System.Net.Sockets.SocketException or ArgumentException)
		{
			return null;
		}
	}

	private string MySeatProbeToken()
	{
		int localPeerId = LocalPeerId;
		PlayerInfo? me = null;
		if (localPeerId > 0) me = PlayerList.Find(player => player.PeerId == localPeerId);
		if (me == null) me = PlayerList.Find(player => !player.IsComputer && player.AccountId != 0 && player.AccountId == AccountId);
		return me?.ProbeToken ?? string.Empty;
	}

	private void StopHandoverProbe()
	{
		_handoverProbe?.Dispose();
		_handoverProbe = null;
		_handoverProbeHost = string.Empty;
		_handoverProbeToken = string.Empty;
	}

	/// <summary>
	///     Runs one advice round when the lobby changed since the last one: every seated screen measures a path to every
	///     other, and the rows are handed to the authority, which is the only place they meet. A round is bounded, staggered
	///     by slot and cooled down, so a lobby is never measured by a storm of probes.
	/// </summary>
	private void ScheduleAdviceRound()
	{
		if (HandoverStage != LobbyHandoverStage.None || IsCountdownActive || IsGameStarted) return;
		if (_adviceWork != null || _handoverProbe == null) return;
		if (System.Environment.TickCount64 < _nextAdviceRoundTicks) return;

		int humanSeats = PlayerList.Count(player => !player.IsComputer && player.PeerId > 0 && !player.IsDisconnected);
		if (humanSeats < 3 || humanSeats == _lastAdviceSeatCount) return;

		PlayerInfo? me = PlayerList.Find(player => player.PeerId == LocalPeerId);
		int mySlot = me?.Slot ?? 0;

		_lastAdviceSeatCount = humanSeats;
		_nextAdviceRoundTicks = System.Environment.TickCount64 + AdviceRoundCooldownMilliseconds;

		var work = new CancellationTokenSource();
		_adviceWork = work;
		_ = Task.Run(async () => await RunAdviceRoundAsync(mySlot, work).ConfigureAwait(false));
	}

	private async Task RunAdviceRoundAsync(int mySlot, CancellationTokenSource work)
	{
		CancellationToken token = work.Token;
		try
		{
			await Task.Delay(500 + Math.Max(0, mySlot) * AdviceStaggerMilliseconds, token).ConfigureAwait(false);

			LobbyHandoverProbeService? probe = _handoverProbe;
			if (probe == null || !probe.IsRegistered) return;

			var samples = new List<LobbyHandoverLinkSample>(PlayerList.Count);
			foreach (PlayerInfo player in PlayerList.ToArray())
			{
				if (token.IsCancellationRequested) return;
				if (player.IsComputer || player.PeerId <= 0) continue;
				if (player.PeerId == LocalPeerId) continue;
				if (string.IsNullOrEmpty(player.ProbeToken)) continue;

				LobbyHandoverLinkSample sample = await probe.MeasureAsync(player.ProbeToken, player.Slot, TimeSpan.FromMilliseconds(AdviceProbeMilliseconds)).ConfigureAwait(false);
				samples.Add(sample);
			}

			if (token.IsCancellationRequested || samples.Count == 0) return;

			ClientServerSession? session = _lobbySession;
			session?.SendLobbyCommand(new LobbyCommand
			{
				Kind = LobbyCommandKind.ReportHandoverMeasurements,
				Measurements = new LobbyHandoverLinkRow
				{
					ObserverSlot = mySlot,
					Samples = samples.ToArray()
				}
			});
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			if (ReferenceEquals(_adviceWork, work)) _adviceWork = null;
		}
	}

	/// <summary>
	///     Begins the rehearsal this screen has to run because it is the candidate: its own authority is put up for the
	///     lobby's seats before anything is given up, its endpoint is measured the way publishing requires, and every seated
	///     player is probed. The report it produces is judged by the same rules the lobby judges it by.
	/// </summary>
	private void StartRehearsal()
	{
		if (_rehearsalWork != null) return;

		var work = new CancellationTokenSource();
		_rehearsalWork = work;
		_ = Task.Run(async () => await RehearseHandoverAsync(work.Token).ConfigureAwait(false));
	}

	private void CancelRehearsal(bool abandonAuthority)
	{
		CancellationTokenSource? work = _rehearsalWork;
		_rehearsalWork = null;
		work?.Cancel();

		if (!abandonAuthority) return;

		_handoverCredentials = [];
		_handoverAdoption = null;
		_handoverReachability = null;
		_handoverRehearsalPort = 0;
		_handoverRehearsalAdminToken = string.Empty;
		_handoverRehearsalMapHash = string.Empty;

		if (_handoverRehearsalPort > 0 || _rehearsalAuthorityWasStarted)
		{
			GameContext.LocalAuthority.Pause();
			_rehearsalAuthorityWasStarted = false;
		}
	}

	private bool _rehearsalAuthorityWasStarted;

	private async Task RehearseHandoverAsync(CancellationToken token)
	{
		LobbyHandoverReport report;
		try
		{
			report = await MeasureRehearsalAsync(token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[LobbyManager] The host handover rehearsal stopped: {exception.GetType().Name}: {exception.Message}");
			report = default;
			report.Failure = exception.Message;
			report.MeasuredUtcTicks = DateTime.UtcNow.Ticks;
			report.Verdict = (int)LobbyHandoverVerdict.NotMeasured;
		}

		if (token.IsCancellationRequested) return;

		Callable.From(() =>
		{
			if (token.IsCancellationRequested) return;
			if (_rehearsalWork != null && _rehearsalWork.Token.Equals(token)) _rehearsalWork = null;

			HandoverReport = report;
			HasHandoverReport = true;
			_lobbySession?.SendLobbyCommand(new LobbyCommand
			{
				Kind = LobbyCommandKind.ReportHostHandoverReport,
				Report = report
			});
			HandoverStateChanged?.Invoke();
		}).CallDeferred();
	}

	private async Task<LobbyHandoverReport> MeasureRehearsalAsync(CancellationToken token)
	{
		var report = new LobbyHandoverReport
		{
			MeasuredUtcTicks = DateTime.UtcNow.Ticks,
			Verdict = (int)LobbyHandoverVerdict.NotMeasured
		};

		string? mapDirectory = GameHost.ResolveMapDirectory(ActiveMapName, ActiveMapVersion);
		if (string.IsNullOrEmpty(mapDirectory))
		{
			report.Failure = "this screen holds no map folder for the lobby's board";
			return report;
		}

		LobbyHandoverAdoption? adoption = BuildAdoption(mapDirectory);
		if (adoption == null)
		{
			report.Failure = "the lobby's seats could not be stated as a lobby this screen can take over";
			return report;
		}

		_handoverAdoption = adoption;
		ClientServerSession? session = _lobbySession;
		if (session == null)
		{
			report.Failure = "this screen is not connected to the lobby it would take over";
			return report;
		}

		var start = new ServerLobbyStart(
			ProjectSettings.GlobalizePath(mapDirectory),
			ActiveMapName,
			Math.Max(1, MaxPlayers),
			Path.Combine(OS.GetUserDataDir(), "logs"),
			OS.GetUserDataDir(),
			LocalizationManager.GetCurrentLanguageCode());

		LocalAuthorityReply started = await session.TryStartAdoptedLobbyAsync(start, DefaultGamePort, adoption).ConfigureAwait(false);
		if (!started.Started)
		{
			GameLog.LogToConsole($"[LobbyManager] The host handover authority could not take UDP port {DefaultGamePort} ({started.Failure}), so it takes the port the system chooses.");
			started = await session.TryStartAdoptedLobbyAsync(start, 0, adoption).ConfigureAwait(false);
		}

		if (!started.Started)
		{
			report.Failure = $"the lobby could not be put up on this machine: {started.Failure}";
			return report;
		}

		_handoverRehearsalPort = started.Port;
		_handoverRehearsalAdminToken = session.AdminToken;
		_handoverRehearsalMapHash = started.MapHash;
		_rehearsalAuthorityWasStarted = true;

		// The port is opened in the firewall and on the router exactly as publishing asks for it, so the measurement that
		// follows describes a lobby that could be published rather than one that merely listens.
		await OpenLobbyEndpointAsync(started.Port).ConfigureAwait(false);
		LobbyReachabilitySet reachability = await RehearsePublishedEndpointsAsync(RegistryServerUrl, started.Port).ConfigureAwait(false);
		_handoverReachability = reachability;

		report.CandidateEndpointVerified = reachability.Verified.Count > 0;
		if (!report.CandidateEndpointVerified)
		{
			report.Failure = $"no registry node reached this machine's game port: {reachability.FirstFailure?.Detail ?? "it did not answer"}";
			report.CandidateStabilityRank = HostStabilityRanks.Rank(HostStability);
			report.CurrentHostStabilityRank = HostStabilityRanks.Rank(HostStability);
			report.Seats = BuildSeatMetrics([], token);
			report.MapMatched = MapMatchesLobby();
			return report;
		}

		LobbyEndpointInfo primary = reachability.Verified.Find(endpoint => endpoint.FamilyValue == LobbyAddressFamily.IPv4) ?? reachability.Verified[0];
		report.CandidateEndpointAddress = primary.DialAddress;
		report.CandidateEndpointPort = primary.Port;

		report.MapMatched = MapMatchesLobby();
		report.CandidateStabilityRank = HostStabilityRanks.Rank(HostStability);
		report.CurrentHostStabilityRank = HostStabilityRanks.Rank(HostStability);

		List<LobbyHandoverLinkSample> measured = await MeasureEverySeatAsync(token).ConfigureAwait(false);
		report.Seats = BuildSeatMetrics(measured, token);
		report.Credentials = _handoverCredentials;
		report.Verdict = (int)HostHandoverRules.Verdict(report);

		if (string.IsNullOrEmpty(report.Failure) && report.Verdict != (int)LobbyHandoverVerdict.Passed) report.Failure = "the measured numbers did not pass the handover rules";
		return report;
	}

	private bool MapMatchesLobby()
	{
		// The folder was resolved for the version this lobby is playing, so holding it is what the authority requires: the
		// new host runs the artifact in that folder and nothing recompiles it for the match.
		return !string.IsNullOrEmpty(GameHost.ResolveMapDirectory(ActiveMapName ?? string.Empty, _joinedMapVersion));
	}

	private LobbyHandoverAdoption? BuildAdoption(string mapDirectory)
	{
		MintSeatCredentials();

		var seats = new List<LobbyHandoverAdoptedSeat>(PlayerList.Count);
		foreach (PlayerInfo player in PlayerList)
		{
			if (player.Slot <= 0) continue;
			bool isComputer = player.IsComputer || player.PeerId < 0;
			string credential = isComputer ? string.Empty : CredentialForSlot(player.Slot);

			seats.Add(new LobbyHandoverAdoptedSeat(
				player.Slot,
				player.AccountId,
				player.Name,
				player.Faction,
				player.Team,
				player.TeamIndex,
				PackColor(player.Color),
				isComputer ? (int)LobbyBotDifficultyRules.Clamp((int)player.Difficulty) : 0,
				credential));
		}

		if (seats.Count == 0) return null;

		var settings = new List<LobbyHandoverAdoptedSetting>(MatchSettings.Count);
		for (int index = 0; index < MatchSettings.Count; index++) settings.Add(new LobbyHandoverAdoptedSetting(MatchSettings[index].SettingId, MatchSettings[index].Value));

		return new LobbyHandoverAdoption(
			ActiveLobbyId ?? string.Empty,
			ActiveMapName,
			ActiveMapVersion,
			CopyOpenSlotTable(),
			seats.ToArray(),
			settings.ToArray(),
			TeamsTogether,
			(int)ShroudMode,
			SpectatorDelay,
			LobbyTeamCount,
			LobbyFactions.ToArray());
	}

	private string CredentialForSlot(int slot)
	{
		for (int index = 0; index < _handoverCredentials.Length; index++)
		{
			if (_handoverCredentials[index].Slot == slot) return _handoverCredentials[index].SessionToken;
		}

		return string.Empty;
	}

	private LobbyHandoverSeatCredential[] MintSeatCredentials()
	{
		var credentials = new LobbyHandoverSeatCredential[PlayerList.Count];
		int count = 0;
		foreach (PlayerInfo player in PlayerList)
		{
			if (player.IsComputer || player.PeerId <= 0 || player.Slot <= 0) continue;
			credentials[count++] = new LobbyHandoverSeatCredential
			{
				Slot = player.Slot,
				AccountId = player.AccountId,
				SessionToken = RealmProtocol.CreateToken()
			};
		}

		Array.Resize(ref credentials, count);
		_handoverCredentials = credentials;
		return credentials;
	}

	private bool[] CopyOpenSlotTable()
	{
		var slots = new bool[Realm.Ecs.EcsSharedMemory.MAX_PLAYERS];
		for (int slot = 0; slot < slots.Length; slot++) slots[slot] = IsSlotOpen(slot);
		return slots;
	}

	private async Task<List<LobbyHandoverLinkSample>> MeasureEverySeatAsync(CancellationToken token)
	{
		var samples = new List<LobbyHandoverLinkSample>(PlayerList.Count);
		LobbyHandoverProbeService? probe = _handoverProbe;
		if (probe == null) return samples;

		// A probe that has not registered yet cannot measure anything, and reporting its silence as a failed seat would be a
		// different statement from a seat that answers nothing. The registration is given a few seconds to land.
		long registrationDeadline = System.Environment.TickCount64 + 6000;
		while (!probe.IsRegistered && !token.IsCancellationRequested && System.Environment.TickCount64 < registrationDeadline)
		{
			await Task.Delay(200, token).ConfigureAwait(false);
		}

		foreach (PlayerInfo player in PlayerList.ToArray())
		{
			if (token.IsCancellationRequested) break;
			if (player.IsComputer || player.PeerId <= 0 || player.PeerId == LocalPeerId) continue;
			if (string.IsNullOrEmpty(player.ProbeToken)) continue;

			LobbyHandoverLinkSample sample = await probe.MeasureAsync(player.ProbeToken, player.Slot, TimeSpan.FromMilliseconds(RehearsalProbeMilliseconds)).ConfigureAwait(false);
			samples.Add(sample);
		}

		return samples;
	}

	private LobbyHandoverSeatMetric[] BuildSeatMetrics(List<LobbyHandoverLinkSample> samples, CancellationToken token)
	{
		var metrics = new List<LobbyHandoverSeatMetric>(PlayerList.Count);
		foreach (PlayerInfo player in PlayerList)
		{
			if (player.IsComputer || player.Slot <= 0) continue;
			if (player.PeerId <= 0) continue;

			var metric = new LobbyHandoverSeatMetric
			{
				Slot = player.Slot,
				AccountId = player.AccountId,
				Name = player.Name,
				CurrentHostMeasured = player.LatencyMilliseconds >= 0,
				CurrentHostRoundTripMilliseconds = Math.Max(0, player.LatencyMilliseconds),
				CurrentHostJitterMilliseconds = Math.Max(0, player.JitterMilliseconds),
				CurrentHostPacketLossPercent = Math.Max(0f, player.PacketLossPercent)
			};

			if (player.PeerId == LocalPeerId)
			{
				// The candidate would be the host, so its own link to itself is not a trip anybody pays for.
				metric.CandidateMeasured = true;
				metric.CandidateRoundTripMilliseconds = 0;
				metric.CandidateSamples = HostHandoverRules.MinimumSamplesPerSeat;
			}
			else
			{
				for (int index = 0; index < samples.Count; index++)
				{
					if (samples[index].TargetSlot != player.Slot) continue;
					metric.CandidateMeasured = samples[index].Measured;
					metric.CandidateRoundTripMilliseconds = samples[index].RoundTripMilliseconds;
					metric.CandidateJitterMilliseconds = samples[index].JitterMilliseconds;
					metric.CandidatePacketLossPercent = samples[index].PacketLossPercent;
					metric.CandidateSamples = samples[index].Samples;
					break;
				}
			}

			metrics.Add(metric);
		}

		_ = token;
		return metrics.ToArray();
	}

	private void ResetHandoverState()
	{
		HandoverStage = LobbyHandoverStage.None;
		HandoverCandidatePeerId = 0;
		HandoverOffererPeerId = 0;
		SuggestedCandidatePeerId = 0;
		HandoverNoticeKey = string.Empty;
		HandoverSecondsRemaining = 0;
		HandoverReport = default;
		HasHandoverReport = false;
		_probeRelayPort = 0;
		_lastAdviceSeatCount = 0;
		_lastSuggestedCandidatePeerId = 0;
		_nextAdviceRoundTicks = 0;
		_nextProbeAttemptTicks = 0;
		_handoverCredentials = [];
		_handoverAdoption = null;
		_handoverReachability = null;

		_adviceWork?.Cancel();
		_adviceWork = null;
		_settleWork?.Cancel();
		_settleWork = null;
		CancelRehearsal(abandonAuthority: true);
		StopHandoverProbe();
	}
}
