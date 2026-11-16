namespace Realm.Shared.Reachability;

/// <summary>
///     The one sentence a host is told when their lobby has no public listing. It is stated in the lobby room's chat when a
///     lobby server refuses to list the lobby and above the addresses in the invite window, so the two surfaces that tell a
///     host how players reach them cannot drift apart.
/// </summary>
public static class LobbyPublicationSentences
{
	/// <summary>
	///     The sentence stating that the lobby has no public listing, and that its address is how players join it. It is also
	///     the translation key, so it is the text every surface that says this shows.
	/// </summary>
	public const string PublicLobbyNotCreated = "Failed to create public lobby. Send IP address directly to friends so they can join.";
}
