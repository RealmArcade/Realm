using System;
using System.Collections.Generic;
using Realm.Ecs;
using Realm.Ecs.Components.Core;

namespace Realm.Client.Services;

/// <summary>
///     Who is whose ally. Every order this service takes about teams and alliances lands here, and the table
///     it derives is written onto the world entity for the simulation to read. Until the authority's own
///     table arrives, derivation is this screen's best guess from the teams it knows plus the explicit
///     allied pairs; after that the authority's answer stands and derivation stops.
/// </summary>
internal sealed class PlayerRelationshipTable
{
	private readonly Func<int, int> _teamOf;
	private readonly Func<Entity> _worldEntityOf;

	internal PlayerRelationshipTable(Func<int, int> teamOf, Func<Entity> worldEntityOf)
	{
		_teamOf = teamOf;
		_worldEntityOf = worldEntityOf;
	}

	private readonly HashSet<long> _alliedPairs = new();

	private readonly Dictionary<long, PlayerRelationship> _explicitRelationships = new();

	private static long MakePairKey(int p1, int p2)
	{
		int min = Math.Min(p1, p2);
		int max = Math.Max(p1, p2);
		return ((long)min << 32) | (uint)max;
	}

	internal void SetPlayerTeam(int playerIndex, int teamIndex)
	{
		int stringId = StringRegistry.GetOrAdd(teamIndex == 0 ? "Neutral" : $"Team {teamIndex}");

		if (SimulationService.HasScriptPlayersState())
		{
			ref var playersState = ref SimulationService.World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex))
			{
				playersState.SetTeam(playerIndex, teamIndex, stringId);
				var player = playersState.Get(playerIndex);
				SimulationService.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in player);
			}
		}

		var worldEntity = GameContext.Simulation.WorldEntity;
		if (GameContext.World != null && worldEntity != Entity.Null && SimulationService.World.Has<NetworkMappingState>(worldEntity))
		{
			var playerEntities = SimulationService.World.Get<NetworkMappingState>(worldEntity).PlayerEntities;
			if (playerIndex >= 0 && playerIndex < playerEntities.Length && playerEntities[playerIndex] != Entity.Null && SimulationService.World.IsAlive(playerEntities[playerIndex]))
			{
				if (SimulationService.World.Has<PlayerTeam>(playerEntities[playerIndex]))
				{
					ref var pt = ref SimulationService.World.Get<PlayerTeam>(playerEntities[playerIndex]);
					pt.TeamIndex = teamIndex;
					pt.TeamNameStringId = stringId;
				}
				else
				{
					SimulationService.World.Add(playerEntities[playerIndex], new PlayerTeam(teamIndex, stringId));
				}
			}
		}

		if (GameContext.Lobby != null && GameContext.Lobby.PlayerList != null)
		{
			var list = GameContext.Lobby.PlayerList;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].Slot == playerIndex)
				{
					list[i].TeamIndex = teamIndex;
					list[i].Team = teamIndex == 0 ? "Neutral" : $"Team {teamIndex}";
					break;
				}
			}
		}

		RecalculatePlayerRelationships();
	}

	internal string GetPlayerTeamName(int playerIndex)
	{
		if (playerIndex >= 0 && playerIndex < 25 && GameContext.World != null)
		{
			var buffer = SimulationService.World.GetBuffer<ScriptPlayer>();
			if (buffer != null)
			{
				int stringId = buffer.Get(playerIndex).TeamNameStringId;
				if (stringId != 0)
				{
					return StringRegistry.Get(stringId);
				}
			}
		}

		int teamIndex = _teamOf(playerIndex);
		return teamIndex == 0 ? "Neutral" : $"Team {teamIndex}";
	}

	internal void SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied)
	{
		long key = MakePairKey(playerIndex, otherPlayerIndex);
		if (allied)
		{
			_alliedPairs.Add(key);
			_explicitRelationships[key] = PlayerRelationship.Ally;
		}
		else
		{
			_alliedPairs.Remove(key);
			_explicitRelationships.Remove(key);
		}

		RecalculatePlayerRelationships();
	}

	internal void SetPlayerRelationship(int playerIndex, int otherPlayerIndex, PlayerRelationship relationship)
	{
		long key = MakePairKey(playerIndex, otherPlayerIndex);
		_explicitRelationships[key] = relationship;
		if (relationship == PlayerRelationship.Ally)
		{
			_alliedPairs.Add(key);
		}
		else
		{
			_alliedPairs.Remove(key);
		}

		RecalculatePlayerRelationships();
	}

	internal PlayerRelationship GetPlayerRelationship(int playerIndex, int otherPlayerIndex)
	{
		if (playerIndex == otherPlayerIndex) return PlayerRelationship.Ally;

		var worldEntity = _worldEntityOf();
		if (GameContext.World != null && worldEntity != Entity.Null && SimulationService.World.Has<PlayerRelationshipMatrix>(worldEntity))
		{
			return SimulationService.World.Get<PlayerRelationshipMatrix>(worldEntity).GetRelationship(playerIndex, otherPlayerIndex);
		}

		if (ArePlayersAllied(playerIndex, otherPlayerIndex))
		{
			return PlayerRelationship.Ally;
		}

		if (ArePlayersEnemies(playerIndex, otherPlayerIndex))
		{
			return PlayerRelationship.Enemy;
		}

		return PlayerRelationship.Neutral;
	}

	internal bool ArePlayersAllied(int playerIndex, int otherPlayerIndex)
	{
		if (playerIndex == otherPlayerIndex) return true;

		var worldEntity = _worldEntityOf();
		if (GameContext.World != null && worldEntity != Entity.Null && SimulationService.World.Has<PlayerRelationshipMatrix>(worldEntity))
		{
			return SimulationService.World.Get<PlayerRelationshipMatrix>(worldEntity).AreAllied(playerIndex, otherPlayerIndex);
		}

		if (_alliedPairs.Contains(MakePairKey(playerIndex, otherPlayerIndex))) return true;

		int t1 = _teamOf(playerIndex);
		int t2 = _teamOf(otherPlayerIndex);
		if (t1 != 0 && t2 != 0 && t1 == t2) return true;

		return false;
	}

	internal bool ArePlayersEnemies(int playerIndex, int otherPlayerIndex)
	{
		if (playerIndex == otherPlayerIndex) return false;

		var worldEntity = _worldEntityOf();
		if (GameContext.World != null && worldEntity != Entity.Null && SimulationService.World.Has<PlayerRelationshipMatrix>(worldEntity))
		{
			return SimulationService.World.Get<PlayerRelationshipMatrix>(worldEntity).AreEnemies(playerIndex, otherPlayerIndex);
		}

		if (_alliedPairs.Contains(MakePairKey(playerIndex, otherPlayerIndex))) return false;

		int t1 = _teamOf(playerIndex);
		int t2 = _teamOf(otherPlayerIndex);
		if (t1 != 0 && t2 != 0)
		{
			return t1 != t2;
		}

		return playerIndex != otherPlayerIndex;
	}

	/// <summary>
	///     Whether this match's relationship table is the authority's. A screen keeps its own reading of who is whose ally
	///     only until the authority describes one, because the table it derives can only guess at what a map decided: a
	///     map's wave clones belong to slots the screen has never been told a team for, and every one of them would read as
	///     hostile. Once the authority's table is here, deriving over it would answer a question this screen has already
	///     been answered.
	/// </summary>
	internal bool RelationshipsAreAuthoritative { get; private set; }

	internal void MarkRelationshipsAuthoritative() => RelationshipsAreAuthoritative = true;

	internal void RestartRelationshipDerivation() => RelationshipsAreAuthoritative = false;

	internal void RecalculatePlayerRelationships()
	{
		if (RelationshipsAreAuthoritative) return;

		var worldEntity = _worldEntityOf();
		if (GameContext.World == null || worldEntity == Entity.Null || !SimulationService.World.IsAlive(worldEntity)) return;

		PlayerRelationshipMatrix matrix = default;

		for (int i = 0; i < PlayerRelationshipMatrix.MaxPlayers; i++)
		{
			for (int j = 0; j < PlayerRelationshipMatrix.MaxPlayers; j++)
			{
				if (i == j)
				{
					matrix.SetRelationship(i, j, PlayerRelationship.Ally);
					continue;
				}

				long key = MakePairKey(i, j);
				if (_explicitRelationships.TryGetValue(key, out var explicitRel))
				{
					matrix.SetRelationship(i, j, explicitRel);
				}
				else if (_alliedPairs.Contains(key))
				{
					matrix.SetRelationship(i, j, PlayerRelationship.Ally);
				}
				else
				{
					int t1 = _teamOf(i);
					int t2 = _teamOf(j);
					if (t1 != 0 && t2 != 0)
					{
						matrix.SetRelationship(i, j, t1 == t2 ? PlayerRelationship.Ally : PlayerRelationship.Enemy);
					}
					else if (t1 == 0 && t2 == 0)
					{
						matrix.SetRelationship(i, j, PlayerRelationship.Neutral);
					}
					else
					{
						matrix.SetRelationship(i, j, PlayerRelationship.Enemy);
					}
				}
			}
		}

		if (SimulationService.World.Has<PlayerRelationshipMatrix>(worldEntity))
		{
			SimulationService.World.Set(worldEntity, matrix);
		}
		else
		{
			SimulationService.World.Add(worldEntity, matrix);
		}
	}

	internal void SetPlayerActiveDomain(int playerIndex, bool active)
	{
		if (SimulationService.HasScriptPlayersState())
		{
			ref var playersState = ref SimulationService.World.Get<ScriptPlayersState>(GameContext.Simulation.WorldEntity);
			if (playersState.IsValidSlot(playerIndex))
			{
				playersState.SetActive(playerIndex, active);
				var player = playersState.Get(playerIndex);
				SimulationService.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in player);
			}
		}
	}
}
