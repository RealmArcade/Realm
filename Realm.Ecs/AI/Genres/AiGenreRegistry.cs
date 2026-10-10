using System.Collections.Concurrent;

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
		Register("tug_of_war", () => new TugOfWarGenreProvider());
		Register("tugofwar", () => new TugOfWarGenreProvider());
		Register("castle_fight", () => new TugOfWarGenreProvider());
		Register("castlefight", () => new TugOfWarGenreProvider());
		Register("desert_strike", () => new TugOfWarGenreProvider());
		Register("desertstrike", () => new TugOfWarGenreProvider());
		Register("nexus_wars", () => new TugOfWarGenreProvider());
		Register("hero_arena", () => new HeroArenaGenreProvider());
		Register("heroarena", () => new HeroArenaGenreProvider());
		Register("moba", () => new HeroArenaGenreProvider());
		Register("dota", () => new HeroArenaGenreProvider());
		Register("footman_frenzy", () => new HeroArenaGenreProvider());
		Register("footmanfrenzy", () => new HeroArenaGenreProvider());
		Register("aos", () => new HeroArenaGenreProvider());
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
