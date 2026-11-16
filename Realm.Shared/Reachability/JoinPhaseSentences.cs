namespace Realm.Shared.Reachability;

/// <summary>
///     The sentences a join failure is stated with. A player is told the one thing that is true wherever the handshake broke -
///     this network could not carry a lobby connection - and the step that broke is kept for the log and for the lobby's own
///     report, where it is read by whoever is diagnosing the match rather than by the player who wanted to play it.
/// </summary>
public static class JoinPhaseSentences
{
	/// <summary>
	///     The sentence a joining screen states for a failure at any step. It is also the translation key, so it is the text
	///     every surface that explains a restriction shows.
	/// </summary>
	public const string RestrictiveNetwork = "Your ISP has a restrictive firewall that cannot be bypassed. You may have trouble hosting or joining lobbies. Try joining a different lobby, playing single-player, or contacting us on discord for troubleshooting advice.";

	/// <summary>
	///     The sentence for a phase. Every phase states the same sentence: the step is an internal detail of the handshake,
	///     and a player cannot act on the difference between a punch that never arrived and a port that stayed silent.
	/// </summary>
	/// <param name="phase">The phase that broke, named from <see cref="JoinPhases" />. It is stated in the log, not in the sentence.</param>
	/// <returns>The sentence to show.</returns>
	public static string For(string? phase) => RestrictiveNetwork;

	/// <summary>The sentence for a failure this build cannot name.</summary>
	/// <returns>The sentence to show.</returns>
	public static string Generic() => RestrictiveNetwork;

	/// <summary>How the lobby's host reads a player that could not find the lobby through the lobby server.</summary>
	public const string HostRegistry = "the lobby could not be found through the lobby server";

	/// <summary>How the host reads a player whose lobby server did not say where the lobby is.</summary>
	public const string HostResolve = "the lobby server did not say where the lobby is";

	/// <summary>How the host reads a player that could not report where it is.</summary>
	public const string HostReport = "the player could not report where it is, so this lobby could not send toward it";

	/// <summary>How the host reads a player whose answer never arrived.</summary>
	public const string HostPunch = "the player never received the answer this lobby sent toward it";

	/// <summary>How the host reads a player this lobby's game port never answered.</summary>
	public const string HostAnswer = "this lobby's game port never answered the player";

	/// <summary>How the host reads a player this lobby refused.</summary>
	public const string HostAdmission = "this lobby refused the player";

	/// <summary>How the host reads a player this lobby admitted and never described itself to.</summary>
	public const string HostSession = "this lobby admitted the player but never described itself to it";

	/// <summary>The line a host reads for a step this build cannot name.</summary>
	public const string HostUnnamed = "the handshake failed";

	/// <summary>
	///     The step as the lobby's own chat states it, which is the same phase said from the host's side: the host reads what
	///     the lobby it is running did or did not do, which is what tells a host whose side of the handshake broke. Every
	///     phase has its own line here because the host is the reader who can act on the difference.
	/// </summary>
	/// <param name="phase">The phase the joiner reported.</param>
	/// <returns>The step, phrased for the host.</returns>
	public static string HostLineFor(string? phase) => phase switch
	{
		JoinPhases.Registry => HostRegistry,
		JoinPhases.Resolve => HostResolve,
		JoinPhases.Report => HostReport,
		JoinPhases.Punch => HostPunch,
		JoinPhases.Answer => HostAnswer,
		JoinPhases.Admission => HostAdmission,
		JoinPhases.Session => HostSession,
		_ => HostUnnamed
	};
}
