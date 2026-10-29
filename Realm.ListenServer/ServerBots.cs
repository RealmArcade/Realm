using System.Numerics;
using System.Text.Json;
using Realm.Ecs;
using Realm.Ecs.AI;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Genres;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.AI.Training;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;

namespace Realm.ListenServer;

public class ServerBots
{
	public const string DefaultGenreName = "rts";

	private readonly SimulationContext _context;
	private readonly string _mapName;
	private readonly string _dataDirectory;
	private readonly ServerLog _log;
	private readonly Dictionary<int, BotController> _controllers = new();
	private readonly Dictionary<int, List<GenericAffordance>> _customDecisionsPerPlayer = new();

	public ServerBots(SimulationContext context, string mapName, string dataDirectory, ServerLog log)
	{
		_context = context;
		_mapName = mapName;
		_dataDirectory = dataDirectory;
		_log = log;
	}

	public Action<int, string, string, Vector3, string>? CustomActionExecuted { get; set; }

	public void Step(float deltaSeconds)
	{
		for (int playerIndex = 0; playerIndex < ScriptPlayersState.SlotCount; playerIndex++)
		{
			if (!IsComputerControlled(playerIndex)) continue;

			if (!_controllers.TryGetValue(playerIndex, out BotController? bot))
			{
				bot = CreateDefaultController(playerIndex);
				_controllers[playerIndex] = bot;
			}

			bot.Tick(_context.World, playerIndex, deltaSeconds);
		}
	}

	public bool IsComputerControlled(int playerIndex)
	{
		if (playerIndex == 0) return true;

		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return false;
		if (!_context.World.Has<ScriptPlayersState>(worldEntity)) return false;

		ScriptPlayersState players = _context.World.Get<ScriptPlayersState>(worldEntity);
		return players.IsValidSlot(playerIndex) && players.Get(playerIndex).IsComputer;
	}

	public void SetProfile(int playerIndex, string profileJson)
	{
		if (string.IsNullOrWhiteSpace(profileJson)) return;

		BotProfile profile;
		try
		{
			profile = BotProfile.FromJson(profileJson);
		}
		catch (JsonException exception)
		{
			_log.Write($"a map handed player {playerIndex} a bot profile this build cannot read: {exception.Message}");
			return;
		}

		BotController bot = GetOrCreateController(playerIndex, profile);
		bot.LoadProfile(profile);
		_log.Write($"player {playerIndex} now plays the profile the map handed it");
	}

	public string GetProfile(int playerIndex)
	{
		return _controllers.TryGetValue(playerIndex, out BotController? bot) ? bot.Profile.ToJson() : string.Empty;
	}

	public void SetGenre(int playerIndex, string genreName, string? configJson)
	{
		if (string.IsNullOrWhiteSpace(genreName)) return;

		IAiGenreProvider genre = AiGenreRegistry.Get(genreName);
		ConfigureGenre(genre, configJson);
		ConfigureCustomScanner(genre, playerIndex);

		if (_controllers.TryGetValue(playerIndex, out BotController? bot))
		{
			bot.GenreProvider = genre;
			bot.LoadProfile(genre.CreateDefaultProfile(_mapName));
			_log.Write($"player {playerIndex} now plays the '{genreName}' genre");
			return;
		}

		var created = new BotController(playerIndex, genre.CreateDefaultProfile(_mapName), genre);
		created.CustomActionCallback = AnnounceCustomAction;
		_controllers[playerIndex] = created;
		_log.Write($"player {playerIndex} seated with the '{genreName}' genre");
	}

	public string GetGenre(int playerIndex)
	{
		return _controllers.TryGetValue(playerIndex, out BotController? bot) ? bot.GenreProvider.GenreName : DefaultGenreName;
	}

	public void RegisterCustomDecision(int playerIndex, string actionId, string intent, float[] featureVector, Vector3 position, string payload)
	{
		if (!_customDecisionsPerPlayer.TryGetValue(playerIndex, out List<GenericAffordance>? decisions))
		{
			decisions = new List<GenericAffordance>();
			_customDecisionsPerPlayer[playerIndex] = decisions;
		}

		var parsedIntent = Enum.TryParse(intent, true, out CommandIntent commandIntent) ? commandIntent : CommandIntent.Interact;
		decisions.Add(new GenericAffordance(Entity.Null, parsedIntent, Entity.Null, position, actionId, featureVector));
	}

	public void ClearCustomDecisions(int playerIndex)
	{
		if (_customDecisionsPerPlayer.TryGetValue(playerIndex, out List<GenericAffordance>? decisions)) decisions.Clear();
	}

	public void ForgetPlayer(int playerIndex)
	{
		_controllers.Remove(playerIndex);
		_customDecisionsPerPlayer.Remove(playerIndex);
	}

	public static string Train(string mapName, int generations, int populationSize, int matchesPerEvaluation, string genre)
	{
		IAiGenreProvider provider = AiGenreRegistry.Get(genre);
		var trainer = new SelfPlayTrainer();
		return trainer.TrainSelfPlay(mapName, generations, populationSize, matchesPerEvaluation, genreProvider: provider).ToJson();
	}

	public void LoadSavedProfile(int playerIndex, BotController bot)
	{
		string path = Path.Combine(_dataDirectory, $"{_mapName}_bot.json");
		if (!File.Exists(path)) return;

		try
		{
			bot.LoadProfile(BotProfile.FromJson(File.ReadAllText(path)));
			_log.Write($"player {playerIndex} plays the profile saved as '{path}'");
		}
		catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
		{
			_log.Write($"could not read the saved bot profile '{path}': {exception.Message}");
		}
	}

	private BotController CreateDefaultController(int playerIndex)
	{
		var bot = new BotController(playerIndex);
		bot.CustomActionCallback = AnnounceCustomAction;
		LoadSavedProfile(playerIndex, bot);
		return bot;
	}

	private BotController GetOrCreateController(int playerIndex, BotProfile profile)
	{
		if (_controllers.TryGetValue(playerIndex, out BotController? existing)) return existing;

		var created = new BotController(playerIndex, profile);
		created.CustomActionCallback = AnnounceCustomAction;
		_controllers[playerIndex] = created;
		return created;
	}

	private void AnnounceCustomAction(int playerIndex, string actionId, Vector3 position, string intent)
	{
		CustomActionExecuted?.Invoke(playerIndex, actionId, intent, position, string.Empty);
	}

	private static void ConfigureGenre(IAiGenreProvider genre, string? configJson)
	{
		if (string.IsNullOrWhiteSpace(configJson)) return;

		try
		{
			Dictionary<string, string>? parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(configJson);
			if (parameters != null) genre.ConfigureFromParameters(parameters);
		}
		catch (JsonException)
		{
		}
	}

	private void ConfigureCustomScanner(IAiGenreProvider genre, int playerIndex)
	{
		if (genre is not CustomGenreProvider customGenre) return;

		customGenre.CustomScanner = (world, scanningPlayerIndex, destination, scannerContext) =>
		{
			if (!_customDecisionsPerPlayer.TryGetValue(playerIndex, out List<GenericAffordance>? decisions) || decisions == null) return;
			if (scanningPlayerIndex != playerIndex) return;

			destination.AddRange(decisions);
		};
	}
}
