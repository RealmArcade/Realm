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
}
