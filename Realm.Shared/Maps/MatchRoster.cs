using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.MapAPI;

namespace Realm.Shared.Maps;

/// <summary>
///     The players a match starts with before a lobby hands out slots. Both processes build the same roster, so a
///     player slot means the same thing to whoever is simulating and whoever is rendering.
/// </summary>
public static class MatchRoster
{
	public const int NeutralSlot = 0;

	/// <summary>
	///     Slot zero is the neutral player the map's own scenery belongs to. The remaining slots are the match
	///     participants, and only the first one starts with resources and is active.
	/// </summary>
	public static ScriptPlayer[] CreateDefaultPlayers()
	{
		var players = new ScriptPlayer[EcsWorld.MAX_PLAYERS];

		players[NeutralSlot] = new ScriptPlayer
		{
			Gold = 0f,
			Wood = 0f,
			Stone = 0f,
			Active = true,
			IsComputer = true,
			Team = 0,
			Name = "Neutral",
			KillCount = 0,
			TeamNameStringId = StringRegistry.GetOrAdd("Neutral")
		};

		for (int index = 1; index < EcsWorld.MAX_PLAYERS; index++)
		{
			players[index] = new ScriptPlayer
			{
				Gold = index == 1 ? 500f : 0f,
				Wood = index == 1 ? 400f : 0f,
				Stone = index == 1 ? 200f : 0f,
				Active = index == 1,
				IsComputer = false,
				Team = index,
				Name = $"Player {index}",
				KillCount = 0,
				TeamNameStringId = StringRegistry.GetOrAdd($"Team {index}")
			};
		}

		return players;
	}

	/// <summary>The player entity a slot belongs to, falling back to the neutral slot for a slot a map did not use.</summary>
	public static PlayerEntity ResolvePlayer(Entity[] playerEntities, int playerIndex)
	{
		if (playerEntities == null || playerEntities.Length == 0) return new PlayerEntity(Entity.Null);

		int clamped = playerIndex < 0 || playerIndex >= playerEntities.Length ? NeutralSlot : playerIndex;
		return new PlayerEntity(playerEntities[clamped]);
	}
}
