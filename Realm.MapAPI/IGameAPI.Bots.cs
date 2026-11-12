// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Retrieves the serialized JSON string of the active AI bot decision profile for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	/// <returns>The serialized JSON string representing the active bot profile, or an empty string if not configured.</returns>
	string GetPlayerBotProfile(int playerIndex);

	/// <summary>
	/// Sets the AI sub-genre provider for the specified player slot (e.g., "rts", "tower_defense", "auto_battler", "custom").
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	/// <param name="genreName">The identifier of the AI genre provider.</param>
	/// <param name="configJson">Optional JSON configuration parameters for the genre provider.</param>
	void SetPlayerBotGenre(int playerIndex, string genreName, string? configJson = null);

	/// <summary>
	/// Retrieves the identifier of the active AI sub-genre provider for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	/// <returns>The genre identifier string (e.g., "rts", "tower_defense", "auto_battler").</returns>
	string GetPlayerBotGenre(int playerIndex);

	/// <summary>
	/// Registers a map-defined custom candidate action for evaluation by the bot utility decision loop. While a seat
	/// holds at least one registered candidate the bot scores only those, because they carry their own feature vector
	/// and its own weights; the genre scan resumes once the map clears them.
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	/// <param name="actionId">The unique action identifier.</param>
	/// <param name="intent">The action intent category (e.g., "Build", "Cast", "Transact", "Interact").</param>
	/// <param name="featureVector">The normalized feature vector scored against the AI weights.</param>
	/// <param name="position">The target world coordinate for the action.</param>
	/// <param name="payload">Optional metadata or argument string passed when the action executes.</param>
	void RegisterCustomBotDecision(int playerIndex, string actionId, string intent, float[] featureVector, Vector3 position, string payload = "");

	/// <summary>
	/// Clears all registered custom bot candidate actions for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	void ClearCustomBotDecisions(int playerIndex);

	/// <summary>
	/// Triggered when an autonomous AI bot executes a custom map-defined action.
	/// Parameters are: playerIndex, actionId, intent, targetPosition, payload.
	/// </summary>
	event Action<int, string, string, Vector3, string>? OnBotCustomActionExecuted;

	/// <summary>
	/// Executes an automated self-play genetic training simulation on CPU and returns the resulting serialized bot profile JSON.
	/// </summary>
	/// <param name="mapName">The identifier or name of the map being trained.</param>
	/// <param name="generations">The number of evolutionary generations to execute.</param>
	/// <param name="populationSize">The candidate population size per generation.</param>
	/// <param name="matchesPerEvaluation">The number of evaluation matches each candidate plays per generation.</param>
	/// <param name="genre">The AI sub-genre provider identifier (e.g. "rts", "tower_defense", "auto_battler").</param>
	/// <returns>The serialized JSON string of the best evolved bot profile.</returns>
	string TrainBotProfile(string mapName, int generations = 5, int populationSize = 8, int matchesPerEvaluation = 4, string genre = "rts");
}
