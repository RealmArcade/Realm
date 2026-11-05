namespace Realm.MapAPI;

/// <summary>
///     A map script that decides its lobby's roster. The authority runs it at tick zero, before any match exists, and
///     hands it <see cref="ILobbyAPI" /> instead of <see cref="IGameAPI" />; the match itself then runs
///     <see cref="IWasmModule" /> as usual.
/// </summary>
/// <remarks>
///     A map that does not implement this interface runs the authority's own lobby rules, which leave the colours and
///     teams the lobby assigns on admission alone. The methods are called on the authority's simulation thread, once
///     before the lobby fills and then once per lobby tick, so they must not block and must not be confused with the
///     match tick.
/// </remarks>
public interface ILobbyScript
{
	/// <summary>
	///     States the roster this map offers: its factions, how many players the lobby seats, and the faction, team and
	///     colour each slot starts with. It runs once when the lobby's world is prepared.
	/// </summary>
	/// <param name="api">The roster this lobby currently holds.</param>
	void LobbyInitialize(ILobbyAPI api);

	/// <summary>
	///     Keeps the roster current while players join, leave and change their seats. It runs once per lobby tick, which
	///     is the lobby's own cadence and not the simulation's.
	/// </summary>
	/// <param name="api">The roster this lobby currently holds.</param>
	/// <param name="deltaSeconds">The time since the previous lobby tick.</param>
	void LobbyUpdate(ILobbyAPI api, float deltaSeconds);

	/// <summary>
	///     The warning the lobby's host must read and accept before the match starts, which is how a map stops a start its
	///     seats make unfair - one team with nobody to fight, or one team larger than another. It is asked only when the
	///     host asks to start, never on a lobby tick, so it costs nothing while the lobby fills.
	/// </summary>
	/// <remarks>
	///     Only the host reads the answer, because only the host can start or abandon the match it warns about. Accepting
	///     the warning starts the match the host asked for; declining leaves the lobby exactly as it was.
	/// </remarks>
	/// <param name="api">The roster this lobby currently holds.</param>
	/// <param name="message">The warning the host should read, or empty when there is nothing to warn about.</param>
	/// <returns>Whether the host must read a warning before the match starts.</returns>
	bool TryGetStartWarning(ILobbyAPI api, out string message);
}
