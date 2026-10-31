using System.Collections.Generic;
using Realm.Client.Network;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Tags;

namespace Realm.Client.Services;

public readonly record struct MatchPlayerResult(
	int Slot,
	string Name,
	string TeamName,
	string Faction,
	bool IsComputer,
	int Kills,
	int Gold,
	int Wood,
	int Stone,
	int ArmySize,
	int PopulationUsed,
	int PopulationCapacity)
{
	public int TotalResources => Gold + Wood + Stone;
}

public static class MatchScoreboardReader
{
	private static readonly QueryDescription ArmyQuery = new QueryDescription().WithAll<UnitOwnerPlayer, Health>().WithNone<Dead, Building>();

	public static void Collect(List<MatchPlayerResult> destination)
	{
		destination.Clear();

		EcsWorld? world = GameContext.World;
		Entity worldEntity = GameContext.WorldEntity;
		bool hasWorld = world != null && worldEntity != Entity.Null && world.IsAlive(worldEntity);
		if (!hasWorld) return;

		bool hasPlayersState = world!.Has<ScriptPlayersState>(worldEntity);
		ScriptPlayersState playersState = hasPlayersState ? world.Get<ScriptPlayersState>(worldEntity) : default;
		Entity[]? playerEntities = world.Has<NetworkMappingState>(worldEntity) ? world.Get<NetworkMappingState>(worldEntity).PlayerEntities : null;
		int[] armyCounts = CountArmyByPlayer(world);
		List<PlayerInfo>? roster = GameContext.Lobby?.PlayerList;

		for (int slot = 1; slot < ScriptPlayersState.SlotCount; slot++)
		{
			PlayerInfo? seat = FindSeat(roster, slot);
			ScriptPlayer player = hasPlayersState ? playersState.Get(slot) : default;
			bool active = hasPlayersState ? player.Active : seat != null;
			if (!active || (!hasPlayersState && seat == null)) continue;

			ReadPopulation(world, playerEntities, slot, out int populationUsed, out int populationCapacity);

			destination.Add(new MatchPlayerResult(
				slot,
				ResolveName(player, seat, slot, hasPlayersState),
				ResolveTeamName(player, seat, hasPlayersState),
				seat != null ? seat.Faction : string.Empty,
				hasPlayersState ? player.IsComputer : seat != null && seat.PeerId < 0,
				hasPlayersState ? player.KillCount : 0,
				hasPlayersState ? (int)player.Gold : 0,
				hasPlayersState ? (int)player.Wood : 0,
				hasPlayersState ? (int)player.Stone : 0,
				armyCounts[slot],
				populationUsed,
				populationCapacity));
		}
	}

	private static int[] CountArmyByPlayer(EcsWorld world)
	{
		var counts = new int[ScriptPlayersState.SlotCount];
		world.Query(in ArmyQuery, (Entity entity, ref UnitOwnerPlayer owner) =>
		{
			int slot = owner.Value;
			if (slot > 0 && slot < counts.Length) counts[slot]++;
		});

		return counts;
	}

	private static void ReadPopulation(EcsWorld world, Entity[]? playerEntities, int slot, out int populationUsed, out int populationCapacity)
	{
		populationUsed = 0;
		populationCapacity = 0;
		if (playerEntities == null || slot >= playerEntities.Length) return;

		Entity playerEntity = playerEntities[slot];
		if (!world.IsAlive(playerEntity) || !world.Has<PlayerPopulation>(playerEntity)) return;

		PlayerPopulation population = world.Get<PlayerPopulation>(playerEntity);
		populationUsed = population.Current;
		populationCapacity = population.Max;
	}

	private static PlayerInfo? FindSeat(List<PlayerInfo>? roster, int slot)
	{
		if (roster == null) return null;

		for (int index = 0; index < roster.Count; index++)
		{
			PlayerInfo seat = roster[index];
			if (seat != null && seat.Slot == slot) return seat;
		}

		return null;
	}

	private static string ResolveName(ScriptPlayer player, PlayerInfo? seat, int slot, bool hasPlayersState)
	{
		string name = hasPlayersState ? player.Name : string.Empty;
		if (string.IsNullOrWhiteSpace(name) && seat != null) name = seat.Name;
		if (string.IsNullOrWhiteSpace(name)) name = $"Player {slot}";

		return name;
	}

	private static string ResolveTeamName(ScriptPlayer player, PlayerInfo? seat, bool hasPlayersState)
	{
		string teamName = hasPlayersState ? player.TeamName : string.Empty;
		if (string.IsNullOrWhiteSpace(teamName) && seat != null) teamName = seat.Team;
		if (string.IsNullOrWhiteSpace(teamName)) teamName = player.Team == 0 ? "Neutral" : $"Team {player.Team}";

		return teamName;
	}
}
