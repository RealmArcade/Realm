using System;
using System.Collections.Generic;
using System.Text.Json;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.AI;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Genres;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.Common;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Services;

/// <summary>
///     The bots this screen drives: one controller per player slot, its profile, the genre provider that
///     decides what it wants, and the per-player feed of decisions a custom genre scanner reads. A script
///     sets either a profile or a genre, and the same slot keeps its controller across both.
/// </summary>
internal sealed class PlayerBotDirector
{
	private readonly Dictionary<int, BotController> _controllers = new();
	private readonly Dictionary<int, List<GenericAffordance>> _customDecisions = new();
	private readonly Action<int, string, string, Vector3> _customAction;

	internal PlayerBotDirector(Action<int, string, string, Vector3> customAction)
	{
		_customAction = customAction;
	}

	internal void SetPlayerBotProfileDomain(int playerIndex, string profileJson)
	{
		if (string.IsNullOrWhiteSpace(profileJson)) return;
		try
		{
			var profile = BotProfile.FromJson(profileJson);
			SetBotProfile(playerIndex, profile);
		}
		catch
		{
		}
	}

	internal void SetBotProfile(int playerIndex, BotProfile profile)
	{
		if (!_controllers.TryGetValue(playerIndex, out var bot))
		{
			bot = new BotController(playerIndex, profile);
			bot.CustomActionCallback = (pIdx, actId, pos, intent) => _customAction(pIdx, actId, intent, pos);
			_controllers[playerIndex] = bot;
		}
		else
		{
			bot.LoadProfile(profile);
		}
	}

	internal string GetPlayerBotProfileDomain(int playerIndex)
	{
		var profile = GetBotProfile(playerIndex);
		return profile != null ? profile.ToJson() : string.Empty;
	}

	internal BotProfile? GetBotProfile(int playerIndex)
	{
		return _controllers.TryGetValue(playerIndex, out var bot) ? bot.Profile : null;
	}

	internal void SetPlayerBotGenreDomain(int playerIndex, string genreName, string? configJson)
	{
		SetBotGenre(playerIndex, genreName, configJson);
	}

	internal void SetBotGenre(int playerIndex, string genreName, string? configJson = null)
	{
		var genreProvider = AiGenreRegistry.Get(genreName);
		ConfigureGenreProvider(genreProvider, configJson);
		ConfigureCustomProvider(genreName, genreProvider);
		UpdateOrCreateBotController(playerIndex, genreProvider);
	}

	internal void ConfigureGenreProvider(IAiGenreProvider genreProvider, string? configJson)
	{
		if (string.IsNullOrWhiteSpace(configJson)) return;
		try
		{
			var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(configJson);
			if (dict != null) genreProvider.ConfigureFromParameters(dict);
		}
		catch
		{
		}
	}

	internal void ConfigureCustomProvider(string genreName, IAiGenreProvider genreProvider)
	{
		if (!string.Equals(genreName, "custom", StringComparison.OrdinalIgnoreCase)) return;
		if (genreProvider is not CustomGenreProvider customProvider) return;

		customProvider.CustomScanner = (world, pIdx, dest, ctx) =>
		{
			if (_customDecisions.TryGetValue(pIdx, out var list) && list != null) dest.AddRange(list);
		};
	}

	internal void UpdateOrCreateBotController(int playerIndex, IAiGenreProvider genreProvider)
	{
		if (!_controllers.TryGetValue(playerIndex, out var bot))
		{
			var profile = genreProvider.CreateDefaultProfile(GameContext.Simulation.ActiveMapName ?? "GenericMap");
			bot = new BotController(playerIndex, profile, genreProvider);
			bot.CustomActionCallback = (pIdx, actId, pos, intent) => _customAction(pIdx, actId, intent, pos);
			_controllers[playerIndex] = bot;
		}
		else
		{
			bot.GenreProvider = genreProvider;
			var profile = genreProvider.CreateDefaultProfile(bot.Profile.MapName);
			bot.LoadProfile(profile);
		}
	}

	internal string GetPlayerBotGenreDomain(int playerIndex)
	{
		return GetBotGenre(playerIndex);
	}

	internal string GetBotGenre(int playerIndex)
	{
		return _controllers.TryGetValue(playerIndex, out var bot) ? bot.GenreProvider.GenreName : "rts";
	}

	internal void RegisterCustomBotDecisionDomain(int playerIndex, string actionId, string intent, float[] featureVector, Vector3 position, string payload)
	{
		RegisterCustomBotDecision(playerIndex, actionId, intent, featureVector, position, payload);
	}

	internal void RegisterCustomBotDecision(int playerIndex, string actionId, string intent, float[] featureVector, Vector3 position, string payload)
	{
		if (!_customDecisions.TryGetValue(playerIndex, out var list))
		{
			list = new List<GenericAffordance>();
			_customDecisions[playerIndex] = list;
		}

		CommandIntent cmdIntent = CommandIntent.Interact;
		if (Enum.TryParse<CommandIntent>(intent, true, out var parsedIntent))
		{
			cmdIntent = parsedIntent;
		}

		list.Add(new GenericAffordance(
			Entity.Null,
			cmdIntent,
			Entity.Null,
			position,
			actionId,
			featureVector
		));
	}

	internal void ClearCustomBotDecisions(int playerIndex)
	{
		if (_customDecisions.TryGetValue(playerIndex, out var list))
		{
			list.Clear();
		}
	}
}
