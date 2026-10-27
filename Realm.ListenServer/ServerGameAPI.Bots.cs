namespace Realm.ListenServer;

/// <summary>
///     The opponents a match plays itself, as a map describes them. A map hands a slot a profile it trained or saved, a
///     genre that decides differently, or the decisions it made for that slot, and the authority keeps the bot that
///     plays it: a bot is simulated here, so what it does reaches every client as the match it is part of.
/// </summary>
public partial class ServerGameAPI
{
	/// <summary>Gives a slot the profile the map trained for it or saved for it.</summary>
	public void SetPlayerBotProfile(int playerIndex, string profileJson) => Bots.SetProfile(playerIndex, profileJson);

	/// <summary>The profile a slot is playing with, so a map can keep it or hand back a changed one.</summary>
	public string GetPlayerBotProfile(int playerIndex) => Bots.GetProfile(playerIndex);

	/// <summary>Hands a slot a genre that decides differently, with the settings that genre reads.</summary>
	public void SetPlayerBotGenre(int playerIndex, string genreName, string? configJson = null) => Bots.SetGenre(playerIndex, genreName, configJson);

	/// <summary>The genre a slot is playing.</summary>
	public string GetPlayerBotGenre(int playerIndex) => Bots.GetGenre(playerIndex);

	/// <summary>Adds one decision the map made itself to what its bot may choose from.</summary>
	public void RegisterCustomBotDecision(int playerIndex, string actionId, string intent, float[] featureVector, System.Numerics.Vector3 position, string payload) =>
		Bots.RegisterCustomDecision(playerIndex, actionId, intent, featureVector, position, payload);

	/// <summary>Clears the decisions the map made for a slot.</summary>
	public void ClearCustomBotDecisions(int playerIndex) => Bots.ClearCustomDecisions(playerIndex);

	/// <summary>Plays a map against itself and hands back the profile that won, as text the map can keep.</summary>
	public string TrainBotProfile(string mapName, int generations = 5, int populationSize = 8, int matchesPerEvaluation = 4, string genre = "rts") =>
		ServerBots.Train(string.IsNullOrWhiteSpace(mapName) ? _paths.MapName : mapName, generations, populationSize, matchesPerEvaluation, genre);
}
