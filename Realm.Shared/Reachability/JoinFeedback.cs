namespace Realm.Shared.Reachability;

/// <summary>
///     The steps a join goes through, named once so that the screen that failed, the log it wrote, the host's diagnostics and
///     the command-line joiner all say the same word for the same step. A failure is reported against exactly one of these,
///     which is what makes "which step broke" answerable without reading a log.
/// </summary>
public static class JoinPhases
{
	/// <summary>The join could not be resolved through a registry node, or no node answered at all.</summary>
	public const string Registry = "registry";

	/// <summary>The answer a registry node gave did not state an endpoint this screen could use.</summary>
	public const string Resolve = "resolve";

	/// <summary>The screen could not tell the lobby's host where it is, so the host cannot send toward it.</summary>
	public const string Report = "report";

	/// <summary>The host's punch is what opens the way back; the host either never sent one or it never arrived.</summary>
	public const string Punch = "punch";

	/// <summary>The lobby's transport never answered this screen's connection attempt.</summary>
	public const string Answer = "answer";

	/// <summary>The lobby's authority answered and refused this screen, or never said who this screen is seated as.</summary>
	public const string Admission = "admission";

	/// <summary>The lobby admitted this screen but never described the lobby back to it.</summary>
	public const string Session = "session";

	/// <summary>Every phase, in the order a join goes through them.</summary>
	public static IReadOnlyList<string> All { get; } = new[] { Registry, Resolve, Report, Punch, Answer, Admission, Session };

	/// <summary>Whether a name is one of the phases this build states.</summary>
	/// <param name="phase">The name a report carries.</param>
	/// <returns>Whether the report names a phase this build understands.</returns>
	public static bool IsKnown(string? phase) => phase != null && All.Contains(phase);
}

/// <summary>
///     The route a joining screen uses to tell the registry node that its join failed. Nothing about the player is stated
///     beyond the phase, the endpoint that was tried and how many attempts were made, and the node never trusts any of it
///     for anything but a chat line the lobby's host reads.
/// </summary>
public static class JoinFailureRoutes
{
	/// <summary>The route a join-failure report is posted to.</summary>
	public const string Report = "/lobbies/join-failure";
}

/// <summary>
///     What a joining screen states about a join that failed: the lobby it was for, the phase that broke, the endpoint it
///     was aimed at and how long the attempt was given. It carries no credentials and no player identity, because the host
///     needs to know that somebody could not get in, not who.
/// </summary>
/// <param name="LobbyId">The lobby the join was for.</param>
/// <param name="Phase">The step that failed, named from <see cref="JoinPhases" />.</param>
/// <param name="Endpoint">The endpoint the join was aimed at.</param>
/// <param name="Attempts">How many connection attempts the screen made.</param>
/// <param name="ElapsedMilliseconds">How long the failed attempt was given.</param>
public record JoinFailureReport(string LobbyId, string Phase, string Endpoint, int Attempts, int ElapsedMilliseconds);

/// <summary>
///     How much a registry node accepts from the join-failure route, so a hostile joiner cannot turn a host's lobby chat into
///     a flood. Every limit is on the node, because the node is the only party a joiner cannot bypass.
/// </summary>
public static class JoinFailureLimits
{
	/// <summary>The longest endpoint a report may name, which is longer than any address and port can be written.</summary>
	public const int MaximumEndpointCharacters = 64;

	/// <summary>How many reports one source address may send in a minute.</summary>
	public const int MaximumReportsPerSourcePerMinute = 8;

	/// <summary>How many lines one lobby may be given in a minute, which is what stops a chat flood reaching its players.</summary>
	public const int MaximumLinesPerLobbyPerMinute = 6;

	/// <summary>How long identical failures from one address collapse into the line already stated for them.</summary>
	public const int CollapseWindowMilliseconds = 60_000;

	/// <summary>
	///     How many distinct players failing inside one collapse window make the lobby's stated reachability a claim its own
	///     players have disproved. Three strangers failing at three different addresses in a minute is a lobby that is not
	///     reachable, however the one node that measured it answered. It is counted by the address each report arrived from,
	///     which is the only part of a report the node observes rather than reads.
	/// </summary>
	public const int DistinctFailuresBeforeDowngrade = 3;
}
