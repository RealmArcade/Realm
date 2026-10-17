namespace Realm.Maps;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.MapAPI;

public static class MeleeRulesExtensions
{
	public static MeleeRules EnableMeleeRules(this IGameAPI api)
	{
		var rules = new MeleeRules(api);
		rules.Initialize();
		return rules;
	}

	public static void HandleUnderAttackAlert(this IGameAPI api, int targetPlayer, string unitId, Vector3? position = null)
	{
		if (targetPlayer < 0) return;
		var player = api.GetPlayer(targetPlayer);
		if (!player.IsActive) return;

		string alertMsg = MeleeRules.IsBaseBuilding(unitId)
			? "⚠️ YOUR BASE IS UNDER ATTACK!"
			: $"⚠️ {unitId.ToUpper()} is under attack!";

		player.ShowFeedbackText(alertMsg, new Vector3(1.0f, 0.2f, 0.1f));
		player.PlayWarningSound();
		if (position.HasValue)
		{
			player.PingMinimap(position.Value);
		}
	}
}

public class MeleeRules
{
	private const float UnderAttackAlertCooldown = 8.0f;
	private readonly IGameAPI _gameApi;
	private readonly HashSet<int> _eliminatedPlayerIndices = new();
	private readonly Dictionary<int, float> _lastUnderAttackAlertTimeByPlayer = new();

	public MeleeRules(IGameAPI gameApi)
	{
		_gameApi = gameApi ?? throw new ArgumentNullException(nameof(gameApi));
	}

	public void Initialize()
	{
		RegisterMeleeDefinitions();
		InitializePlayerResources();
		_gameApi.OnUnitCreated += HandleUnitCreated;
		_gameApi.OnUnitDied += HandleUnitDied;
		_gameApi.OnUnitDamaged += HandleUnitDamaged;
	}

	private void RegisterMeleeDefinitions()
	{
		_gameApi.RegisterPlacementRadius("castle", 5.0f);
		_gameApi.RegisterPlacementRadius("tower", 2.5f);
		_gameApi.RegisterPlacementRadius("goldmine", 4.0f);
		_gameApi.RegisterPlacementRadius("logo", 1.0f);
		_gameApi.RegisterPlacementRadius("flag", 1.0f);
		_gameApi.RegisterPlacementRadius("rune", 1.0f);

		foreach (var dropoffKey in new[] { "castle", "town_hall", "townhall", "base", "main" })
		{
			_gameApi.RegisterResourceDropoffBuilding(dropoffKey);
		}

		foreach (var workerKey in new[] { "worker", "builder", "chad", "kevin" })
		{
			_gameApi.RegisterResourceWorkerUnit(workerKey);
		}

		foreach (var primaryBuildingId in new[] { "castle", "town_hall", "base", "main" })
		{
			_gameApi.RegisterPrimaryBuilding(primaryBuildingId);
		}

		_gameApi.RegisterResourceNodeType("goldmine", "gold");
		_gameApi.RegisterResourceNodeType("tree", "wood");
		_gameApi.RegisterResourceNodeType("rock", "stone");
	}
	private void InitializePlayerResources()
	{
		foreach (var player in _gameApi.GetActivePlayers())
		{
			if (player.Gold <= 0f) player.Gold = 500f;
			if (player.Wood <= 0f) player.Wood = 200f;
			if (player.Stone <= 0f) player.Stone = 100f;

			int baseBuildingCount = 0;
			foreach (var u in player.GetUnits())
			{
				if (u.IsBuilding && IsBaseBuilding(u.TemplateID))
				{
					baseBuildingCount++;
				}
			}
			player.MaxPopulation = Math.Max(20, baseBuildingCount * 20);
		}
	}

	private void HandleUnitCreated(UnitId unit)
	{
		if (unit.IsBuilding && IsBaseBuilding(unit.TemplateID))
		{
			var player = _gameApi.GetPlayer(unit.Player);
			player.MaxPopulation += 20;
		}
	}

	private void HandleUnitDied(UnitId unit, UnitId killer)
	{
		if (unit.IsBuilding && IsBaseBuilding(unit.TemplateID))
		{
			var player = _gameApi.GetPlayer(unit.Player);
			player.MaxPopulation = Math.Max(0, player.MaxPopulation - 20);

			int remainingBaseCount = 0;
			foreach (var u in player.GetUnits())
			{
				if (u.IsBuilding && IsBaseBuilding(u.TemplateID) && u.UniqueId != unit.UniqueId)
				{
					remainingBaseCount++;
				}
			}
			if (remainingBaseCount == 0)
			{
				HandlePlayerElimination(player);
			}
		}
	}

	private void HandleUnitDamaged(UnitId victim, UnitId attacker, float damage)
	{
		if (!victim.IsValid || damage <= 0.01f) return;

		int targetPlayer = victim.Player;
		if (targetPlayer < 0) return;

		var player = _gameApi.GetPlayer(targetPlayer);
		if (!player.IsActive) return;

		if (attacker.IsValid && player.IsAlliedWith(attacker.Player)) return;

		float currentTime = _gameApi.GameElapsedTime;
		if (_lastUnderAttackAlertTimeByPlayer.TryGetValue(targetPlayer, out float lastAlertTime))
		{
			if (currentTime - lastAlertTime < UnderAttackAlertCooldown) return;
		}

		_lastUnderAttackAlertTimeByPlayer[targetPlayer] = currentTime;
		_gameApi.HandleUnderAttackAlert(targetPlayer, victim.TemplateID, victim.Position);
	}

	private void HandlePlayerElimination(PlayerId eliminatedPlayer)
	{
		if (!_eliminatedPlayerIndices.Add(eliminatedPlayer.Index)) return;

		eliminatedPlayer.TriggerDefeat("Your base has been destroyed!");

		var remainingPlayers = new List<PlayerId>();
		foreach (var p in _gameApi.GetActivePlayers())
		{
			if (!_eliminatedPlayerIndices.Contains(p.Index))
			{
				remainingPlayers.Add(p);
			}
		}
		if (remainingPlayers.Count == 0) return;

		int firstTeam = -1;
		bool allSameTeam = true;
		foreach (var p in remainingPlayers)
		{
			if (firstTeam == -1) firstTeam = p.Team;
			else if (p.Team != firstTeam)
			{
				allSameTeam = false;
				break;
			}
		}

		if (allSameTeam)
		{
			foreach (var winner in remainingPlayers)
			{
				winner.TriggerVictory();
			}
		}
	}

	public static bool IsBaseBuilding(string unitId)
	{
		if (string.IsNullOrEmpty(unitId)) return false;
		string lowerId = unitId.ToLowerInvariant();
		return lowerId == "castle" || lowerId == "building/castle" || lowerId == "town_hall" || lowerId == "base" || lowerId == "main" || lowerId.EndsWith("/castle") || lowerId.EndsWith("/town_hall");
	}
}
