using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Realm.Ecs.AI.Genres;

public static class AiGenreRegistry
{
	private static readonly ConcurrentDictionary<string, Func<IAiGenreProvider>> _factories = new(StringComparer.OrdinalIgnoreCase);

	static AiGenreRegistry()
	{
		Register("rts", () => new StandardRtsGenreProvider());
		Register("melee", () => new StandardRtsGenreProvider());
		Register("tower_defense", () => new TowerDefenseGenreProvider());
		Register("td", () => new TowerDefenseGenreProvider());
		Register("auto_battler", () => new AutoBattlerGenreProvider());
		Register("autobattler", () => new AutoBattlerGenreProvider());
		Register("custom", () => new CustomGenreProvider());
	}

	public static void Register(string genreName, Func<IAiGenreProvider> factory)
	{
		if (string.IsNullOrWhiteSpace(genreName) || factory == null) return;
		_factories[genreName.Trim()] = factory;
	}

	public static IAiGenreProvider Get(string genreName)
	{
		if (TryGet(genreName, out var provider) && provider != null)
		{
			return provider;
		}

		return new StandardRtsGenreProvider();
	}

	public static bool TryGet(string genreName, out IAiGenreProvider? provider)
	{
		provider = null;
		if (string.IsNullOrWhiteSpace(genreName)) return false;

		if (_factories.TryGetValue(genreName.Trim(), out var factory) && factory != null)
		{
			provider = factory();
			return true;
		}

		return false;
	}

	public static IReadOnlyCollection<string> GetRegisteredGenres()
	{
		return _factories.Keys.ToArray();
	}
}
