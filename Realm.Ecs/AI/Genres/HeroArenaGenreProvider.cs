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
		if (parameters.TryGetValue("FountainPositionJson", out var fountainJson) && !string.IsNullOrWhiteSpace(fountainJson))
		{
			try
			{
				var pos = JsonSerializer.Deserialize<Vector3>(fountainJson);
				_fountainPosition = pos;
			}
			catch { }
		}

		if (parameters.TryGetValue("FountainHealRadius", out var radiusStr) && float.TryParse(radiusStr, System.Globalization.CultureInfo.InvariantCulture, out var radius))
		{
			_fountainHealRadius = Math.Max(1.0f, radius);
		}

		if (parameters.TryGetValue("RetreatHealthPercent", out var hpStr) && float.TryParse(hpStr, System.Globalization.CultureInfo.InvariantCulture, out var hp))
		{
			_retreatHealthPercent = Math.Clamp(hp, 0.05f, 0.95f);
		}

		if (parameters.TryGetValue("RetreatManaPercent", out var manaStr) && float.TryParse(manaStr, System.Globalization.CultureInfo.InvariantCulture, out var mana))
		{
			_retreatManaPercent = Math.Clamp(mana, 0.05f, 0.95f);
		}

		if (parameters.TryGetValue("RunePositionsJson", out var runesJson) && !string.IsNullOrWhiteSpace(runesJson))
		{
			try
			{
				var runes = JsonSerializer.Deserialize<List<Vector3>>(runesJson);
				if (runes != null)
				{
					_runePositions.Clear();
					_runePositions.AddRange(runes);
				}
			}
			catch { }
		}

		if (parameters.TryGetValue("ShopBuildOrderJson", out var shopJson) && !string.IsNullOrWhiteSpace(shopJson))
		{
			try
			{
				var items = JsonSerializer.Deserialize<List<string>>(shopJson);
				if (items != null)
				{
					_shopBuildOrder.Clear();
					_shopBuildOrder.AddRange(items);
				}
			}
			catch { }
		}
	}

	public void ScanAffordances(World world, int playerIndex, List<GenericAffordance> destinationList, object? customContext = null)
	{
		destinationList.Clear();
		_friendlyHeroes.Clear();
		_enemyHeroes.Clear();
		_laneCreeps.Clear();

		world.Query(in _allPositionQuery, (Entity entity, ref Position pos) =>
		{
			int ownerIndex = -1;
			if (world.Has<UnitOwnerPlayer>(entity))
			{
				ownerIndex = world.Get<UnitOwnerPlayer>(entity).PlayerIndex;
			}
			else if (world.Has<Owner>(entity))
			{
				var pEnt = world.Get<Owner>(entity).PlayerEntity.Value;
				if (world.IsAlive(pEnt) && world.Has<UnitOwnerPlayer>(pEnt))
				{
					ownerIndex = world.Get<UnitOwnerPlayer>(pEnt).PlayerIndex;
				}
			}

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

		int playerGold = 500;
		if (world.IsAlive(playerEntity) && world.Has<PlayerResources>(playerEntity))
		{
			var resources = world.Get<PlayerResources>(playerEntity).Value;
			if (resources.Count > 0)
			{
				playerGold = resources.Values.FirstOrDefault();
			}
		}

		for (int hIdx = 0; hIdx < _friendlyHeroes.Count; hIdx++)
		{
			var hero = _friendlyHeroes[hIdx];
			if (!world.IsAlive(hero)) continue;

			var heroPos = world.Get<Position>(hero).Value;
			float hpRatio = 1.0f;
			if (world.Has<Health>(hero))
			{
				var hp = world.Get<Health>(hero);
				hpRatio = Math.Clamp(hp.Current / Math.Max(1.0f, hp.Max), 0f, 1f);
			}

			float distToFountain = Vector3.Distance(heroPos, _fountainPosition);
			float fountainDistRatio = Math.Clamp(distToFountain / 100.0f, 0f, 1f);

			// 1. Fountain Retreat / Healing
			if (hpRatio <= _retreatHealthPercent)
			{
				float survivalRisk = Math.Clamp((_retreatHealthPercent - hpRatio) / _retreatHealthPercent + 0.5f, 0.5f, 1.0f);
				float[] retreatVec = CreateFeatureVector(0.0f, survivalRisk, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, fountainDistRatio);
				destinationList.Add(new GenericAffordance(hero, CommandIntent.MoveTo, Entity.Null, _fountainPosition, "retreat_fountain", retreatVec));
			}

			// 2. Enemy Hero Combat & Ability Combos
			for (int eIdx = 0; eIdx < _enemyHeroes.Count; eIdx++)
			{
				var enemy = _enemyHeroes[eIdx];
				if (!world.IsAlive(enemy)) continue;

				var ePos = world.Get<Position>(enemy).Value;
				float enemyDist = Vector3.Distance(heroPos, ePos);
				float enemyHpRatio = 1.0f;
				if (world.Has<Health>(enemy))
				{
					var eHp = world.Get<Health>(enemy);
					enemyHpRatio = Math.Clamp(eHp.Current / Math.Max(1.0f, eHp.Max), 0f, 1f);
				}

				float killThreat = Math.Clamp(1.0f - enemyHpRatio + (enemyDist < 12.0f ? 0.3f : 0.0f), 0f, 1f);
				float[] attackVec = CreateFeatureVector(killThreat, 1.0f - hpRatio, 0.0f, 0.2f, 0.0f, 0.0f, 0.0f, fountainDistRatio);
				destinationList.Add(new GenericAffordance(hero, CommandIntent.Attack, enemy, ePos, $"harass_hero_{enemy.Id}", attackVec));

				// Check spells/abilities on hero
				if (world.Has<SpellCooldowns>(hero))
				{
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
			}

			// 3. Last-Hitting & Denying Creeps
			for (int cIdx = 0; cIdx < _laneCreeps.Count; cIdx++)
			{
				var creep = _laneCreeps[cIdx];
				if (!world.IsAlive(creep)) continue;

				var creepPos = world.Get<Position>(creep).Value;
				float creepDist = Vector3.Distance(heroPos, creepPos);
				if (creepDist > 25.0f) continue;

				float creepHpRatio = 1.0f;
				if (world.Has<Health>(creep))
				{
					var cHp = world.Get<Health>(creep);
					creepHpRatio = Math.Clamp(cHp.Current / Math.Max(1.0f, cHp.Max), 0f, 1f);
				}

				if (creepHpRatio <= 0.35f)
				{
					float lastHitPriority = Math.Clamp(1.0f - creepHpRatio + (creepDist < 10.0f ? 0.2f : 0.0f), 0.5f, 1.0f);
					float[] lastHitVec = CreateFeatureVector(0.1f, 1.0f - hpRatio, 0.0f, lastHitPriority, 0.0f, 0.0f, 0.0f, fountainDistRatio);
					destinationList.Add(new GenericAffordance(hero, CommandIntent.Attack, creep, creepPos, $"last_hit_creep_{creep.Id}", lastHitVec));
				}
			}

			// 4. Powerup / Rune Control
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

			// 5. Item Purchases from Shop Build Order
			if (_shopBuildOrder.Count > 0 && playerGold >= 200)
			{
				for (int itemIdx = 0; itemIdx < Math.Min(3, _shopBuildOrder.Count); itemIdx++)
				{
					var itemId = _shopBuildOrder[itemIdx];
					float costRatio = Math.Clamp(200.0f / Math.Max(1, playerGold), 0f, 1f);
					float urgency = 0.8f - (itemIdx * 0.15f);
					float[] shopVec = CreateFeatureVector(costRatio, 0.0f, 0.0f, 0.0f, 0.0f, urgency, 0.0f, fountainDistRatio);
					destinationList.Add(new GenericAffordance(hero, CommandIntent.Transact, Entity.Null, heroPos, $"buy_item_{itemId}", shopVec));
				}
			}
		}
	}

	public void ExecuteAction(World world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback = null)
	{
		if (aff.Intent == CommandIntent.Transact || aff.Intent == CommandIntent.Interact)
		{
			customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, aff.Intent.ToString());
			return;
		}

		if (!world.IsAlive(aff.SourceEntity)) return;

		switch (aff.Intent)
		{
			case CommandIntent.Attack:
				if (world.IsAlive(aff.TargetEntity))
				{
					world.AddOrGet(aff.SourceEntity, new AttackTarget(aff.TargetEntity));
				}
				break;

			case CommandIntent.MoveTo:
				world.AddOrGet(aff.SourceEntity, new MoveTo(aff.TargetPosition));
				break;

			case CommandIntent.Cast:
				if (!string.IsNullOrEmpty(aff.PayloadId) && world.Has<SpellCooldowns>(aff.SourceEntity))
				{
					ref var cooldowns = ref world.Get<SpellCooldowns>(aff.SourceEntity);
					cooldowns.Value[aff.PayloadId] = 5.0f;
				}
				customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, "Cast");
				break;

			default:
				customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, aff.Intent.ToString());
				break;
		}
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
