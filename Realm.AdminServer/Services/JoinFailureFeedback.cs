using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Realm.AdminServer.Models;
using Realm.Shared.Reachability;

namespace Realm.AdminServer.Services;

/// <summary>
///     Turns the reports joining players send about a failed join into a line the lobby's host reads, without letting those
///     reports become a way to flood a lobby's chat. One lobby is given a bounded number of lines a minute, an identical
///     failure from one address collapses into the line already stated for it, and enough distinct failures make the lobby
///     stop claiming a reachability its own players have disproved.
/// </summary>
/// <remarks>
///     Everything here is per lobby and the work under each lock is a dictionary update: no lock is held across a socket, a
///     file or another lobby's state, so two players failing on two lobbies cannot wait on each other.
/// </remarks>
public class JoinFailureFeedback
{
	/// <summary>What became of one report.</summary>
	public enum Outcome
	{
		/// <summary>The report reached the lobby's host as a new line.</summary>
		Reported,

		/// <summary>The same failure from the same address was already stated, so it was counted and not repeated.</summary>
		Collapsed,

		/// <summary>The lobby is not listed here, so there is nobody to tell.</summary>
		UnknownLobby,

		/// <summary>This lobby or this source has used its allowance for the minute.</summary>
		RateLimited
	}

	private const int WindowMilliseconds = 60_000;

	private readonly ConcurrentDictionary<string, LobbyFailures> _byLobby = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, SourceFailures> _bySource = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, WebSocket> _hostConnections;
	private readonly LobbyRegistry _lobbies;
	private readonly Action<string> _log;

	public JoinFailureFeedback(ConcurrentDictionary<string, WebSocket> hostConnections, LobbyRegistry lobbies, Action<string> log)
	{
		_hostConnections = hostConnections;
		_lobbies = lobbies;
		_log = log;
	}

	/// <summary>
	///     Reads one joining player's failure report. A report for a lobby this node does not hold, one over the allowance of
	///     its source or its lobby, and one that repeats a failure already stated are all answered without a line reaching
	///     anybody.
	/// </summary>
	/// <param name="report">What the joining player stated.</param>
	/// <param name="sourceAddress">The address the report arrived from, which is what the allowance is counted against.</param>
	/// <returns>What became of the report.</returns>
	public async Task<Outcome> ReportAsync(JoinFailureReport report, string sourceAddress)
	{
		if (report == null || string.IsNullOrWhiteSpace(report.LobbyId)) return Outcome.UnknownLobby;
		if (!_lobbies.TryGet(report.LobbyId, out LobbyInfo? lobby) || lobby == null) return Outcome.UnknownLobby;
		if (!JoinPhases.IsKnown(report.Phase)) return Outcome.RateLimited;

		long now = Environment.TickCount64;
		string source = string.IsNullOrWhiteSpace(sourceAddress) ? "unknown" : sourceAddress;
		if (!TakeSourceAllowance(source, now)) return Outcome.RateLimited;

		LobbyFailures failures = _byLobby.GetOrAdd(report.LobbyId, _ => new LobbyFailures());
		string endpoint = Sanitise(report.Endpoint);

		// A failure is collapsed and counted against the address that reported it, never against the endpoint the report
		// names. Every player failing to reach one lobby names that same lobby endpoint, so counting endpoints would
		// collapse every real failure into one line and never gather the distinct players a downgrade is decided from; and
		// the endpoint is text the reporter chooses, so counting it would let one caller invent the distinct failures that
		// take a lobby off the list. The address is the one thing this node observes for itself.
		string key = $"{report.Phase}|{source}";

		bool collapses;
		int distinct;
		bool downgrade;
		lock (failures.Gate)
		{
			failures.Prune(now);
			if (failures.LinesInWindow >= JoinFailureLimits.MaximumLinesPerLobbyPerMinute) return Outcome.RateLimited;

			collapses = failures.WasStatedRecently(key, now);
			if (!collapses)
			{
				failures.Remember(key, source, now);
				failures.LinesInWindow++;
			}

			distinct = failures.DistinctSources;
			downgrade = distinct >= JoinFailureLimits.DistinctFailuresBeforeDowngrade && failures.TryTakeDowngrade();
		}

		if (collapses) return Outcome.Collapsed;

		if (downgrade && lobby.ReachabilityClass != ReachabilityClasses.Unverified)
		{
			lobby.ReachabilityClass = ReachabilityClasses.Downgrade(lobby.ReachabilityClass);
			_log($"lobby {report.LobbyId} is downgraded to '{lobby.ReachabilityClass}': {distinct} players could not reach it in the last minute");
		}

		string text = Describe(report, distinct, lobby.ReachabilityClass, lobby.MappingBehaviour);
		await SendAsync(lobby.LobbyId, text, report.Phase, endpoint, distinct, lobby.ReachabilityClass).ConfigureAwait(false);
		return Outcome.Reported;
	}

	/// <summary>
	///     Forgets everything stated about a lobby, which is what closing it does. Nothing about a lobby outlives the lobby.
	/// </summary>
	/// <param name="lobbyId">The lobby that is no longer open.</param>
	public void Forget(string lobbyId) => _byLobby.TryRemove(lobbyId, out _);

	/// <summary>
	///     Writes the line a host reads. It says how many players could not get in, which step of the handshake failed from
	///     where, and what the measurement of the lobby's network says to try; nothing the joining player wrote is passed
	///     through, so a report can only ever be one of these sentences.
	/// </summary>
	private static string Describe(JoinFailureReport report, int distinct, string? reachabilityClass, string? mappingBehaviour)
	{
		string step = report.Phase switch
		{
			JoinPhases.Registry => "the lobby could not be found through the lobby server",
			JoinPhases.Resolve => "the lobby server did not say where the lobby is",
			JoinPhases.Report => "the player could not report where it is, so this lobby could not send toward it",
			JoinPhases.Punch => "the player never received the answer this lobby sent toward it",
			JoinPhases.Answer => "the lobby's game port never answered the player",
			JoinPhases.Admission => "the lobby refused the player",
			JoinPhases.Session => "the lobby admitted the player but never described itself to it",
			_ => "the handshake failed"
		};

		string advice = reachabilityClass == ReachabilityClasses.Unverified
			? "This lobby is no longer listed as reachable; enable UPnP on the router or forward this UDP port, and share the direct address meanwhile."
			: mappingBehaviour == MappingBehaviours.PerDestination
				? "This router gives every destination a different outside port, so a punch cannot open it; forward this UDP port, or use a VPN or overlay network."
				: "Other players may need UPnP enabled on this router, or this UDP port forwarded to this machine.";

		return $"{distinct} player(s) could not reach this lobby in the last minute; {step} ({report.Attempts} attempt(s) at {Sanitise(report.Endpoint)}). {advice}";
	}

	private static string Sanitise(string? text)
	{
		if (string.IsNullOrEmpty(text)) return string.Empty;

		var builder = new StringBuilder(Math.Min(text.Length, JoinFailureLimits.MaximumEndpointCharacters));
		foreach (char character in text)
		{
			if (builder.Length >= JoinFailureLimits.MaximumEndpointCharacters) break;
			if (char.IsControl(character)) continue;
			builder.Append(character);
		}

		return builder.ToString();
	}

	private async Task SendAsync(string lobbyId, string text, string phase, string endpoint, int distinct, string? reachabilityClass)
	{
		if (!_hostConnections.TryGetValue(lobbyId, out WebSocket? hostSocket) || hostSocket.State != WebSocketState.Open) return;

		bool sent = await HostSignaling.SendAsync(hostSocket, HostSignaling.Encode(new { Action = "JoinFailed", Text = text, Phase = phase, Endpoint = endpoint, DistinctFailures = distinct, ReachabilityClass = reachabilityClass })).ConfigureAwait(false);
		if (!sent) _log($"a join failure for lobby {lobbyId} could not be handed to its host: its signalling channel refused the write");
	}

	private bool TakeSourceAllowance(string sourceAddress, long now)
	{
		SourceFailures source = _bySource.GetOrAdd(sourceAddress, _ => new SourceFailures());
		lock (source.Gate)
		{
			source.Prune(now);
			if (source.Count >= JoinFailureLimits.MaximumReportsPerSourcePerMinute) return false;
			source.Count++;
			return true;
		}
	}

	private class LobbyFailures
	{
		private readonly Dictionary<string, long> _statedAtMilliseconds = new(StringComparer.Ordinal);
		private readonly HashSet<string> _sourcesInWindow = new(StringComparer.Ordinal);
		private bool _downgradedInWindow;

		public object Gate { get; } = new();

		public int LinesInWindow { get; set; }

		public int DistinctSources => _sourcesInWindow.Count;

		/// <summary>
		///     Claims this window's one downgrade, so a lobby steps down once for the players who failed to reach it rather
		///     than once per player: a lobby that four players could not reach is one lobby with a problem, not a lobby with
		///     four problems to answer.
		/// </summary>
		/// <returns>Whether this report is the one that may downgrade the lobby.</returns>
		public bool TryTakeDowngrade()
		{
			if (_downgradedInWindow) return false;
			_downgradedInWindow = true;
			return true;
		}

		public void Prune(long now)
		{
			if (_statedAtMilliseconds.Count > 0)
			{
				var expired = new List<string>();
				foreach (var pair in _statedAtMilliseconds)
				{
					if (now - pair.Value >= JoinFailureLimits.CollapseWindowMilliseconds) expired.Add(pair.Key);
				}

				foreach (string key in expired) _statedAtMilliseconds.Remove(key);
			}

			if (now - _windowStartedMilliseconds >= WindowMilliseconds)
			{
				_windowStartedMilliseconds = now;
				LinesInWindow = 0;
				_downgradedInWindow = false;
				_sourcesInWindow.Clear();
			}
		}

		private long _windowStartedMilliseconds = Environment.TickCount64;

		public bool WasStatedRecently(string key, long now)
		{
			return _statedAtMilliseconds.TryGetValue(key, out long stated) && now - stated < JoinFailureLimits.CollapseWindowMilliseconds;
		}

		public void Remember(string key, string source, long now)
		{
			_statedAtMilliseconds[key] = now;
			_sourcesInWindow.Add(source);
		}
	}

	private class SourceFailures
	{
		private long _windowStartedMilliseconds = Environment.TickCount64;

		public object Gate { get; } = new();

		public int Count { get; set; }

		public void Prune(long now)
		{
			if (now - _windowStartedMilliseconds < WindowMilliseconds) return;

			_windowStartedMilliseconds = now;
			Count = 0;
		}
	}
}
