namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	public void SetPlayerBotProfile(int playerIndex, string profileJson) => Bots.SetProfile(playerIndex, profileJson);

	public string GetPlayerBotProfile(int playerIndex) => Bots.GetProfile(playerIndex);

	public void SetPlayerBotGenre(int playerIndex, string genreName, string? configJson = null) => Bots.SetGenre(playerIndex, genreName, configJson);

	public string GetPlayerBotGenre(int playerIndex) => Bots.GetGenre(playerIndex);

	public void RegisterCustomBotDecision(int playerIndex, string actionId, string intent, float[] featureVector, System.Numerics.Vector3 position, string payload) =>
		Bots.RegisterCustomDecision(playerIndex, actionId, intent, featureVector, position, payload);

	public void ClearCustomBotDecisions(int playerIndex) => Bots.ClearCustomDecisions(playerIndex);

	public string TrainBotProfile(string mapName, int generations = 5, int populationSize = 8, int matchesPerEvaluation = 4, string genre = "rts") =>
		ServerBots.Train(string.IsNullOrWhiteSpace(mapName) ? _paths.MapName : mapName, generations, populationSize, matchesPerEvaluation, genre);
}
