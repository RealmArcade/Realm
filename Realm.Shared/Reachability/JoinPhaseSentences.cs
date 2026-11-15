namespace Realm.Shared.Reachability;

/// <summary>
///     The sentence a join failure is shown with, chosen by the phase that broke. Each phase has its own because the thing a
///     player has to change is different for each one: a lobby server that is down, a lobby server that will not say where
///     the lobby is, a host that never punched, a host whose port never answered, and an authority that refused are five
///     different problems. The sentences live here, beside the phases and as constants, so a phase added without one is
///     caught by a test rather than quietly shown as the generic line, and so the text a screen translates is still a
///     literal the localization generator can see.
/// </summary>
public static class JoinPhaseSentences
{
	/// <summary>The sentence for a lobby server that could not be reached.</summary>
	public const string Registry = "The lobby server could not be reached, so this lobby could not be found.";

	/// <summary>The sentence for an answer that did not say where the lobby is.</summary>
	public const string Resolve = "The lobby server answered without saying where this lobby is.";

	/// <summary>The sentence for a screen that could not report its own endpoint.</summary>
	public const string Report = "This screen could not tell the lobby where it is, so the host could not send toward it.";

	/// <summary>The sentence for a host that never punched this screen.</summary>
	public const string Punch = "The lobby's host never sent toward this screen, so the way back was never opened. The host may need UPnP enabled or this UDP port forwarded.";

	/// <summary>The sentence for a host that punched and whose game port stayed silent.</summary>
	public const string Answer = "The lobby's host sent toward this screen but its game port never answered. The host may need this UDP port forwarded, or this network may need a VPN or an overlay network.";

	/// <summary>The sentence for an authority that refused this screen.</summary>
	public const string Admission = "The lobby's authority refused this screen.";

	/// <summary>The sentence for a lobby that admitted this screen and never described itself.</summary>
	public const string Session = "The lobby admitted this screen but never described itself to it.";

	/// <summary>The sentence for a failure this build cannot name, which is the one line that states nothing specific.</summary>
	public const string Unnamed = "The lobby's authority did not answer in time.";

	/// <summary>
	///     The sentence for a phase. The text is also the translation key: a build with no translation for it shows the
	///     English sentence, which is the behaviour every other string in the game already has.
	/// </summary>
	/// <param name="phase">The phase that broke, named from <see cref="JoinPhases" />.</param>
	/// <returns>The sentence to show.</returns>
	public static string For(string? phase) => phase switch
	{
		JoinPhases.Registry => Registry,
		JoinPhases.Resolve => Resolve,
		JoinPhases.Report => Report,
		JoinPhases.Punch => Punch,
		JoinPhases.Answer => Answer,
		JoinPhases.Admission => Admission,
		JoinPhases.Session => Session,
		_ => Unnamed
	};

	/// <summary>The sentence for a failure this build cannot name.</summary>
	/// <returns>The sentence to show.</returns>
	public static string Generic() => Unnamed;

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
	///     The step as the lobby's own chat states it, which is the same phase said from the host's side: a joiner reads what
	///     it has to change, and the host reads what the lobby it is running did or did not do. Every phase has its own line
	///     here for the same reason it has its own sentence on the joining screen.
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
