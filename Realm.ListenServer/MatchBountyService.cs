using System;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;

namespace Realm.ListenServer;

/// <summary>
///     Pays the authored bounty of every unit that dies to a player's kill: gold to the killer's team and experience to
///     the killer's hero, which is the unit whose level every soldier of that player follows. A bounty is read from the
///     victim's own template, so any map that states GoldBounty or XpBounty on a unit gets paid for every one of them
///     without a rule written for a particular unit. What a unit pays is then multiplied by its own bounty multiplier,
///     which is how a level's bounty growth makes a monster worth more than the flat number its template states.
/// </summary>
public class MatchBountyService
{
	private readonly LoadedMap _map;
	private readonly ServerGameAPI _api;

	public MatchBountyService(LoadedMap map, ServerGameAPI api)
	{
		_map = map;
		_api = api;
	}

	/// <summary>
	///     Counts the kill on the killer's scoreboard and pays what the victim's template states. A kill by nothing, a
	///     kill of a unit its own team owns, and a kill of a unit whose template states no bounty all pay nothing, but an
	///     enemy kill still counts.
	/// </summary>
	/// <param name="victim">The unit that died.</param>
	/// <param name="killer">The unit that landed the killing blow.</param>
	public void PayKill(UnitId victim, UnitId killer)
	{
		if (!victim.IsValid || !killer.IsValid) return;

		Entity victimEntity = new(victim.Value);
		Entity killerEntity = new(killer.Value);

		// The victim is already marked dead by the time the kill is published, so the question is whether its body is
		// still in the world, not whether it is standing. The killer has to be standing to earn anything.
		if (!_map.World.IsAlive(victimEntity) || !IsAlive(killer)) return;
		if (!_map.World.TryGet<UnitOwnerPlayer>(killerEntity, out UnitOwnerPlayer killerOwner)) return;
		int killerPlayerIndex = killerOwner.Value;
		if (killerPlayerIndex <= 0) return;
		if (!IsEnemyKill(victimEntity, killerPlayerIndex)) return;

		RecordKill(killerPlayerIndex);

		if (!_map.World.TryGet<DefinitionId>(victimEntity, out DefinitionId definitionId)) return;

		UnitMetadata? metadata = _map.Catalog.FindUnitOrBuilding(definitionId.Value);
		if (metadata == null) return;

		float bountyMultiplier = ReadBountyMultiplier(victimEntity);
		float payableGold = metadata.GoldBounty * bountyMultiplier;
		float payableExperience = metadata.XpBounty * bountyMultiplier;
		if (payableGold <= 0f && payableExperience <= 0f) return;

		PayGold(killerPlayerIndex, payableGold);
		PayExperience(killerPlayerIndex, killer, payableExperience);
	}

	/// <summary>
	///     Reads the bounty multiplier the victim's own stat snapshot states, which is where a level's bounty growth lands.
	///     Anything that states no multiplier is worth exactly its authored bounty.
	/// </summary>
	private float ReadBountyMultiplier(Entity victimEntity)
	{
		if (!_map.World.TryGet(victimEntity, out Stats stats)) return 1f;
		if (!stats.TryGetValue(new StatId(StatKeys.BountyMultiplier), out float multiplier)) return 1f;

		return multiplier > 0f ? multiplier : 1f;
	}

	private bool IsEnemyKill(Entity victimEntity, int killerPlayerIndex)
	{
		if (!_map.World.TryGet<UnitOwnerPlayer>(victimEntity, out UnitOwnerPlayer victimOwner)) return true;

		int victimPlayerIndex = victimOwner.Value;
		return victimPlayerIndex <= 0 || ReadTeam(victimPlayerIndex) != ReadTeam(killerPlayerIndex);
	}

	private void RecordKill(int killerPlayerIndex)
	{
		Entity worldEntity = _map.WorldEntity;
		if (!_map.World.IsAlive(worldEntity) || !_map.World.Has<ScriptPlayersState>(worldEntity)) return;

		ref ScriptPlayersState players = ref _map.World.Get<ScriptPlayersState>(worldEntity);
		if (!players.IsValidSlot(killerPlayerIndex)) return;

		players.SetKills(killerPlayerIndex, players.Get(killerPlayerIndex).KillCount + 1);

		ScriptPlayer updated = players.Get(killerPlayerIndex);
		_map.World.GetBuffer<ScriptPlayer>().Set(killerPlayerIndex, in updated);
	}

	private void PayGold(int killerPlayerIndex, float bountyGold)
	{
		if (bountyGold <= 0f) return;

		int members = 0;
		for (int slot = 1; slot < EcsSharedMemory.MAX_PLAYERS; slot++)
		{
			if (IsActiveTeamMember(killerPlayerIndex, slot)) members++;
		}

		if (members == 0) return;

		int wholeBounty = (int)MathF.Round(bountyGold);
		int share = wholeBounty / members;
		int remainder = wholeBounty - share * members;

		for (int slot = 1; slot < EcsSharedMemory.MAX_PLAYERS; slot++)
		{
			if (!IsActiveTeamMember(killerPlayerIndex, slot)) continue;

			int payable = share;
			if (slot == killerPlayerIndex)
			{
				payable += remainder;
				remainder = 0;
			}

			_api.AdjustPlayerGold(slot, payable);
		}
	}

	private void PayExperience(int killerPlayerIndex, UnitId killer, float bountyExperience)
	{
		if (bountyExperience <= 0f) return;

		UnitId recipient = FindHero(killerPlayerIndex);
		if (!recipient.IsValid) recipient = killer;
		if (!recipient.IsValid) return;

		float current = _map.World.TryGet(new Entity(recipient.Value), out Experience experience) ? experience.Current : 0f;
		_api.SetUnitExperience(recipient, current + bountyExperience);
	}

	private UnitId FindHero(int playerIndex)
	{
		ReadOnlySpan<UnitId> owned = _api.GetUnitsOwnedByPlayer(playerIndex);
		for (int index = 0; index < owned.Length; index++)
		{
			UnitId unit = owned[index];
			if (!IsAlive(unit)) continue;
			if (_map.World.Has<Hero>(new Entity(unit.Value))) return unit;
		}

		return UnitId.Invalid;
	}

	private bool IsActiveTeamMember(int killerPlayerIndex, int candidateSlot)
	{
		if (candidateSlot == killerPlayerIndex) return true;
		if (ReadTeam(candidateSlot) != ReadTeam(killerPlayerIndex)) return false;

		Entity worldEntity = _map.WorldEntity;
		if (!_map.World.IsAlive(worldEntity) || !_map.World.Has<ScriptPlayersState>(worldEntity)) return false;

		ScriptPlayersState players = _map.World.Get<ScriptPlayersState>(worldEntity);
		if (!players.IsValidSlot(candidateSlot)) return false;

		return players.Get(candidateSlot).Active;
	}

	private int ReadTeam(int playerSlot)
	{
		Entity worldEntity = _map.WorldEntity;
		if (!_map.World.IsAlive(worldEntity) || !_map.World.Has<ScriptPlayersState>(worldEntity)) return 0;

		ScriptPlayersState players = _map.World.Get<ScriptPlayersState>(worldEntity);
		return players.IsValidSlot(playerSlot) ? players.Get(playerSlot).Team : 0;
	}

	private bool IsAlive(UnitId unit)
	{
		if (!unit.IsValid) return false;

		Entity entity = new(unit.Value);
		if (!_map.World.IsAlive(entity)) return false;

		return !_map.World.Has<Dead>(entity);
	}
}
