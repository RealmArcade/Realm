using Arch.Core;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Realm.Ecs.AI.Genres;

public class HeroArenaGenreProvider : IAiGenreProvider
{
	public const int FeatureVectorLength = 8;
	public string GenreName => "hero_arena";

	public int FeatureCount => FeatureVectorLength;

	public IReadOnlyList<string> FeatureNames { get; } = new string[]
	{
		"KillThreat",
		"SurvivalRisk",
		"ManaEfficiency",
		"LastHitPriority",
		"CrowdControlOpportunity",
		"ShopItemUrgency",
		"PowerupProximity",
		"FountainDistance"
	};

	private Vector3 _fountainPosition = Vector3.Zero;
	private float _fountainHealRadius = 15.0f;
	private float _retreatHealthPercent = 0.30f;
	private float _retreatManaPercent = 0.15f;
	private readonly List<Vector3> _runePositions = new(8);
	private readonly List<string> _shopBuildOrder = new(16);

	private readonly List<Entity> _friendlyHeroes = new(8);
	private readonly List<Entity> _enemyHeroes = new(8);
	private readonly List<Entity> _laneCreeps = new(64);
	private readonly QueryDescription _allPositionQuery = new QueryDescription().WithAll<Position>().WithNone<Dead>();
	private readonly QueryDescription _playerResourcesQuery = new QueryDescription().WithAll<PlayerResources>();

	public float[] GetDefaultWeights()
	{
		return new float[FeatureVectorLength]
		{
			 0.9f, // KillThreat
			 0.95f, // SurvivalRisk
			 0.8f, // ManaEfficiency
			 0.85f, // LastHitPriority
			 0.7f, // CrowdControlOpportunity
			 0.6f, // ShopItemUrgency
			 0.5f, // PowerupProximity
			-0.3f  // FountainDistance
		};
	}

	public BotProfile CreateDefaultProfile(string mapName)
	{
		return new BotProfile
		{
			Genre = "hero_arena",
			MapName = mapName,
			Weights = GetDefaultWeights()
		};
	}

	public void ConfigureFromParameters(IReadOnlyDictionary<string, string> parameters)
	{
		ConfigureFountainPosition(parameters);
		ConfigureFountainHealRadius(parameters);
		ConfigureRetreatHealthPercent(parameters);
		ConfigureRetreatManaPercent(parameters);
		ConfigureRunePositions(parameters);
		ConfigureShopBuildOrder(parameters);
	}

	private void ConfigureFountainPosition(IReadOnlyDictionary<string, string> parameters)
	{
		if (!parameters.TryGetValue("FountainPositionJson", out var fountainJson) || string.IsNullOrWhiteSpace(fountainJson))
			return;

		try
		{
			_fountainPosition = JsonSerializer.Deserialize<Vector3>(fountainJson);
		}
		catch { }
	}

	private void ConfigureFountainHealRadius(IReadOnlyDictionary<string, string> parameters)
	{
		if (!parameters.TryGetValue("FountainHealRadius", out var radiusStr) || !float.TryParse(radiusStr, System.Globalization.CultureInfo.InvariantCulture, out var radius))
			return;

		_fountainHealRadius = Math.Max(1.0f, radius);
	}

	private void ConfigureRetreatHealthPercent(IReadOnlyDictionary<string, string> parameters)
	{
		if (!parameters.TryGetValue("RetreatHealthPercent", out var hpStr) || !float.TryParse(hpStr, System.Globalization.CultureInfo.InvariantCulture, out var hp))
			return;

		_retreatHealthPercent = Math.Clamp(hp, 0.05f, 0.95f);
	}

	private void ConfigureRetreatManaPercent(IReadOnlyDictionary<string, string> parameters)
	{
		if (!parameters.TryGetValue("RetreatManaPercent", out var manaStr) || !float.TryParse(manaStr, System.Globalization.CultureInfo.InvariantCulture, out var mana))
			return;

		_retreatManaPercent = Math.Clamp(mana, 0.05f, 0.95f);
	}

	private void ConfigureRunePositions(IReadOnlyDictionary<string, string> parameters)
	{
		if (!parameters.TryGetValue("RunePositionsJson", out var runesJson) || string.IsNullOrWhiteSpace(runesJson))
			return;

		try
		{
			var runes = JsonSerializer.Deserialize<List<Vector3>>(runesJson);
			if (runes == null)
				return;
				
			_runePositions.Clear();
			_runePositions.AddRange(runes);
		}
		catch { }
	}

	private void ConfigureShopBuildOrder(IReadOnlyDictionary<string, string> parameters)
	{
		if (!parameters.TryGetValue("ShopBuildOrderJson", out var shopJson) || string.IsNullOrWhiteSpace(shopJson))
			return;

		try
		{
			var items = JsonSerializer.Deserialize<List<string>>(shopJson);
			if (items == null)
				return;
				
			_shopBuildOrder.Clear();
			_shopBuildOrder.AddRange(items);
		}
		catch { }
	}

	public void ScanAffordances(World world, int playerIndex, List<GenericAffordance> destinationList, object? customContext = null)
	{
		destinationList.Clear();
		PopulateEntityLists(world, playerIndex);

		Entity playerEntity = GetPlayerEntity(world, playerIndex);
		int playerGold = GetPlayerGold(world, playerEntity);

		ProcessHeroAffordances(world, destinationList, playerGold);
	}

	private void PopulateEntityLists(World world, int playerIndex)
	{
		_friendlyHeroes.Clear();
		_enemyHeroes.Clear();
		_laneCreeps.Clear();

		world.Query(in _allPositionQuery, (Entity entity, ref Position pos) =>
		{
			int ownerIndex = GetOwnerIndex(world, entity);

			if (ownerIndex == playerIndex)
			{
				_friendlyHeroes.Add(entity);
			}
			else if (ownerIndex >= 0)
			{
				_enemyHeroes.Add(entity);
			}
			else
			{
				_laneCreeps.Add(entity);
			}
		});
	}

	private int GetOwnerIndex(World world, Entity entity)
	{
		if (world.Has<UnitOwnerPlayer>(entity))
		{
			return world.Get<UnitOwnerPlayer>(entity).PlayerIndex;
		}
		
		if (world.Has<Owner>(entity))
		{
			var pEnt = world.Get<Owner>(entity).PlayerEntity.Value;
			if (world.IsAlive(pEnt) && world.Has<UnitOwnerPlayer>(pEnt))
			{
				return world.Get<UnitOwnerPlayer>(pEnt).PlayerIndex;
			}
		}

		return -1;
	}

	private Entity GetPlayerEntity(World world, int playerIndex)
	{
		Entity playerEntity = Entity.Null;
		world.Query(in _playerResourcesQuery, (Entity pe) =>
		{
			if (world.Has<UnitOwnerPlayer>(pe) && world.Get<UnitOwnerPlayer>(pe).PlayerIndex == playerIndex)
			{
				playerEntity = pe;
			}
			else if (playerEntity == Entity.Null)
			{
				playerEntity = pe;
			}
		});
		return playerEntity;
	}

	private int GetPlayerGold(World world, Entity playerEntity)
	{
		if (!world.IsAlive(playerEntity) || !world.Has<PlayerResources>(playerEntity))
			return 500;

		var resources = world.Get<PlayerResources>(playerEntity).Value;
		return resources.Count > 0 ? resources.Values.FirstOrDefault() : 500;
	}

	private void ProcessHeroAffordances(World world, List<GenericAffordance> destinationList, int playerGold)
	{
		for (int hIdx = 0; hIdx < _friendlyHeroes.Count; hIdx++)
		{
			var hero = _friendlyHeroes[hIdx];
			if (!world.IsAlive(hero)) continue;

			var heroPos = world.Get<Position>(hero).Value;
			float hpRatio = GetHealthRatio(world, hero);

			float distToFountain = Vector3.Distance(heroPos, _fountainPosition);
			float fountainDistRatio = Math.Clamp(distToFountain / 100.0f, 0f, 1f);

			EvaluateFountainRetreat(destinationList, hero, hpRatio, fountainDistRatio);
			EvaluateEnemyCombat(world, destinationList, hero, heroPos, hpRatio, fountainDistRatio);
			EvaluateCreepLastHits(world, destinationList, hero, heroPos, hpRatio, fountainDistRatio);
			EvaluateRuneControl(destinationList, hero, heroPos, hpRatio, fountainDistRatio);
			EvaluateShopPurchases(destinationList, hero, heroPos, playerGold, fountainDistRatio);
		}
	}

	private float GetHealthRatio(World world, Entity entity)
	{
		if (!world.Has<Health>(entity))
			return 1.0f;

		var hp = world.Get<Health>(entity);
		return Math.Clamp(hp.Current / Math.Max(1.0f, hp.Max), 0f, 1f);
	}

	private void EvaluateFountainRetreat(List<GenericAffordance> destinationList, Entity hero, float hpRatio, float fountainDistRatio)
	{
		if (hpRatio > _retreatHealthPercent) return;

		float survivalRisk = Math.Clamp((_retreatHealthPercent - hpRatio) / _retreatHealthPercent + 0.5f, 0.5f, 1.0f);
		float[] retreatVec = CreateFeatureVector(0.0f, survivalRisk, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, fountainDistRatio);
		destinationList.Add(new GenericAffordance(hero, CommandIntent.MoveTo, Entity.Null, _fountainPosition, "retreat_fountain", retreatVec));
	}

	private void EvaluateEnemyCombat(World world, List<GenericAffordance> destinationList, Entity hero, Vector3 heroPos, float hpRatio, float fountainDistRatio)
	{
		for (int eIdx = 0; eIdx < _enemyHeroes.Count; eIdx++)
		{
			var enemy = _enemyHeroes[eIdx];
			if (!world.IsAlive(enemy)) continue;

			var ePos = world.Get<Position>(enemy).Value;
			float enemyDist = Vector3.Distance(heroPos, ePos);
			float enemyHpRatio = GetHealthRatio(world, enemy);

			float killThreat = Math.Clamp(1.0f - enemyHpRatio + (enemyDist < 12.0f ? 0.3f : 0.0f), 0f, 1f);
			float[] attackVec = CreateFeatureVector(killThreat, 1.0f - hpRatio, 0.0f, 0.2f, 0.0f, 0.0f, 0.0f, fountainDistRatio);
			destinationList.Add(new GenericAffordance(hero, CommandIntent.Attack, enemy, ePos, $"harass_hero_{enemy.Id}", attackVec));

			EvaluateSpellsAgainstEnemy(world, destinationList, hero, enemy, ePos, hpRatio, killThreat, fountainDistRatio);
		}
	}

	private void EvaluateSpellsAgainstEnemy(World world, List<GenericAffordance> destinationList, Entity hero, Entity enemy, Vector3 ePos, float hpRatio, float killThreat, float fountainDistRatio)
	{
		if (!world.Has<SpellCooldowns>(hero)) return;

		var cd = world.Get<SpellCooldowns>(hero);
		foreach (var spell in cd.Value)
		{
			if (spell.Value <= 0.0f)
			{
				float[] castVec = CreateFeatureVector(killThreat, 1.0f - hpRatio, 0.9f, 0.0f, 0.85f, 0.0f, 0.0f, fountainDistRatio);
				destinationList.Add(new GenericAffordance(hero, CommandIntent.Cast, enemy, ePos, spell.Key, castVec));
			}
		}
	}

	private void EvaluateCreepLastHits(World world, List<GenericAffordance> destinationList, Entity hero, Vector3 heroPos, float hpRatio, float fountainDistRatio)
	{
		for (int cIdx = 0; cIdx < _laneCreeps.Count; cIdx++)
		{
			var creep = _laneCreeps[cIdx];
			if (!world.IsAlive(creep)) continue;

			var creepPos = world.Get<Position>(creep).Value;
			float creepDist = Vector3.Distance(heroPos, creepPos);
			if (creepDist > 25.0f) continue;

			float creepHpRatio = GetHealthRatio(world, creep);

			if (creepHpRatio <= 0.35f)
			{
				float lastHitPriority = Math.Clamp(1.0f - creepHpRatio + (creepDist < 10.0f ? 0.2f : 0.0f), 0.5f, 1.0f);
				float[] lastHitVec = CreateFeatureVector(0.1f, 1.0f - hpRatio, 0.0f, lastHitPriority, 0.0f, 0.0f, 0.0f, fountainDistRatio);
				destinationList.Add(new GenericAffordance(hero, CommandIntent.Attack, creep, creepPos, $"last_hit_creep_{creep.Id}", lastHitVec));
			}
		}
	}

	private void EvaluateRuneControl(List<GenericAffordance> destinationList, Entity hero, Vector3 heroPos, float hpRatio, float fountainDistRatio)
	{
		for (int rIdx = 0; rIdx < _runePositions.Count; rIdx++)
		{
			var runePos = _runePositions[rIdx];
			float runeDist = Vector3.Distance(heroPos, runePos);
			if (runeDist < 40.0f && hpRatio > 0.5f)
			{
				float runeProximity = Math.Clamp(1.0f - (runeDist / 40.0f), 0.3f, 1.0f);
				float[] runeVec = CreateFeatureVector(0.0f, 0.1f, 0.0f, 0.0f, 0.0f, 0.0f, runeProximity, fountainDistRatio);
				destinationList.Add(new GenericAffordance(hero, CommandIntent.MoveTo, Entity.Null, runePos, $"grab_rune_{rIdx}", runeVec));
			}
		}
	}

	private void EvaluateShopPurchases(List<GenericAffordance> destinationList, Entity hero, Vector3 heroPos, int playerGold, float fountainDistRatio)
	{
		if (_shopBuildOrder.Count == 0 || playerGold < 200) return;

		for (int itemIdx = 0; itemIdx < Math.Min(3, _shopBuildOrder.Count); itemIdx++)
		{
			var itemId = _shopBuildOrder[itemIdx];
			float costRatio = Math.Clamp(200.0f / Math.Max(1, playerGold), 0f, 1f);
			float urgency = 0.8f - (itemIdx * 0.15f);
			float[] shopVec = CreateFeatureVector(costRatio, 0.0f, 0.0f, 0.0f, 0.0f, urgency, 0.0f, fountainDistRatio);
			destinationList.Add(new GenericAffordance(hero, CommandIntent.Transact, Entity.Null, heroPos, $"buy_item_{itemId}", shopVec));
		}
	}

	public void ExecuteAction(World world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback = null)
	{
		if (IsCustomAction(aff.Intent))
		{
			customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, aff.Intent.ToString());
			return;
		}

		if (!world.IsAlive(aff.SourceEntity)) return;

		switch (aff.Intent)
		{
			case CommandIntent.Attack:
				HandleAttackIntent(world, aff);
				break;
			case CommandIntent.MoveTo:
				world.AddOrGet(aff.SourceEntity, new MoveTo(aff.TargetPosition));
				break;
			case CommandIntent.Cast:
				HandleCastIntent(world, playerIndex, aff, customActionCallback);
				break;
			default:
				customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, aff.Intent.ToString());
				break;
		}
	}

	private bool IsCustomAction(CommandIntent intent)
	{
		return intent == CommandIntent.Transact || intent == CommandIntent.Interact;
	}

	private void HandleAttackIntent(World world, in GenericAffordance aff)
	{
		if (world.IsAlive(aff.TargetEntity))
		{
			world.AddOrGet(aff.SourceEntity, new AttackTarget(aff.TargetEntity));
		}
	}

	private void HandleCastIntent(World world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback)
	{
		if (!string.IsNullOrEmpty(aff.PayloadId) && world.Has<SpellCooldowns>(aff.SourceEntity))
		{
			ref var cooldowns = ref world.Get<SpellCooldowns>(aff.SourceEntity);
			cooldowns.Value[aff.PayloadId] = 5.0f;
		}
		customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, "Cast");
	}

	private static float[] CreateFeatureVector(float killThreat, float survivalRisk, float manaEfficiency, float lastHitPriority, float crowdControlOpportunity, float shopItemUrgency, float powerupProximity, float fountainDistance)
	{
		return new float[FeatureVectorLength]
		{
			killThreat,
			survivalRisk,
			manaEfficiency,
			lastHitPriority,
			crowdControlOpportunity,
			shopItemUrgency,
			powerupProximity,
			fountainDistance
		};
	}
}
