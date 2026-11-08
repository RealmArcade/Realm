using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Gets the elapsed game time in seconds.
	/// </summary>
	float GameElapsedTime { get; }

	/// <summary>
	/// Gets a value indicating whether the match this script runs in lets a player cheat. Read-only: only the
	/// engine decides it, and it answers true exactly for a match a single person plays against the computer, which is
	/// a single player match or a map being tested from the editor. A match that anyone else can join never answers
	/// true, so a script may branch on it without having to ask who is connected.
	/// </summary>
	bool CheatsEnabled { get; }

	/// <summary>
	/// Answers whether the player at the given index is the one hosting this match: the seat a lobby lets administer it,
	/// or the single player of a match only one person plays. A match nobody hosts, which is one an exported server runs
	/// from a roster, answers false for every index. Read-only: the engine decides it, and a script may branch on it to
	/// keep an administrative console command to the person who administers the match.
	/// </summary>
	/// <param name="playerIndex">The player slot to test.</param>
	/// <remarks>
	/// A chat command that only the host may issue is judged here rather than from the unit it was typed with, because a
	/// console command is usually typed with nothing selected.
	/// </remarks>
	/// <returns>True when this slot hosts the match; otherwise false.</returns>
	bool IsHost(int playerIndex);










	/// <summary>
	/// Configures the AI decision profile for the specified player slot using a serialized JSON string.
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	/// <param name="profileJson">The serialized JSON string representing the bot profile weights and personality.</param>
	void SetPlayerBotProfile(int playerIndex, string profileJson);

}
