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

namespace Realm.Ecs.AI.Genres;

public class StandardRtsGenreProvider : IAiGenreProvider
{
	public const int StandardFeatureCount = 8;
	public string GenreName => "rts";

	public int FeatureCount => StandardFeatureCount;

	public IReadOnlyList<string> FeatureNames { get; } = new string[]
	{
		"CostToBank",
		"TargetThreat",
		"RangeFactor",
		"HealthRatio",
		"TempoEfficiency",
		"CooldownReady",
		"RetreatUrgency",
		"SynergyTag"
	};

	private float _retreatHealthPercent = 0.25f;
	private float _kitingDistanceBias = 1.0f;
	private float _aggressionBias = 1.0f;
	private readonly List<Vector3> _expansionLocations = new(8);
	private readonly List<Vector3> _rallyPoints = new(8);

	private readonly List<Entity> _friendlyUnits = new(64);
	private readonly List<Entity> _enemyUnits = new(64);
	private readonly QueryDescription _allPositionQuery = new QueryDescription().WithAll<Position>().WithNone<Dead>();
	private readonly QueryDescription _playerResourcesQuery = new QueryDescription().WithAll<PlayerResources>();

	public float[] GetDefaultWeights()
	{
		return new float[StandardFeatureCount]
		{
			-0.5f, // Cost
			 0.8f, // Target Threat
			 0.2f, // Range
			 0.5f, // Health Ratio
			 0.7f, // Tempo
			 0.9f, // Cooldown
			 0.4f, // Retreat Urgency
			 0.6f  // Synergy
		};
	}

	public BotProfile CreateDefaultProfile(string mapName)
	{
		return new BotProfile
		{
			Genre = "rts",
			MapName = mapName,
			Weights = GetDefaultWeights()
		};
	}

	public void ConfigureFromParameters(IReadOnlyDictionary<string, string> parameters)
	{
		UpdateFloatParam(parameters, "RetreatHealthPercent", ref _retreatHealthPercent, 0.05f, 0.95f);
		UpdateFloatParam(parameters, "KitingDistanceBias", ref _kitingDistanceBias, 0.2f, 3.0f);
		UpdateFloatParam(parameters, "AggressionBias", ref _aggressionBias, 0.1f, 3.0f);

		UpdateVectorListParam(parameters, "ExpansionLocationsJson", _expansionLocations);
		UpdateVectorListParam(parameters, "RallyPointsJson", _rallyPoints);
	}

	private static void UpdateFloatParam(IReadOnlyDictionary<string, string> parameters, string key, ref float field, float min, float max)
	{
		if (!parameters.TryGetValue(key, out var strVal)) return;
		if (!float.TryParse(strVal, System.Globalization.CultureInfo.InvariantCulture, out var floatVal)) return;

		field = Math.Clamp(floatVal, min, max);
	}

	private static void UpdateVectorListParam(IReadOnlyDictionary<string, string> parameters, string key, List<Vector3> list)
	{
		if (!parameters.TryGetValue(key, out var jsonStr) || string.IsNullOrWhiteSpace(jsonStr)) return;

		try
		{
			var parsedList = System.Text.Json.JsonSerializer.Deserialize<List<Vector3>>(jsonStr);
			if (parsedList == null) return;
			
			list.Clear();
			list.AddRange(parsedList);
		}
		catch { }
	}

	private static int GetOwnerIndex(World world, Entity entity)
	{
		if (world.Has<UnitOwnerPlayer>(entity))
		{
			return world.Get<UnitOwnerPlayer>(entity).PlayerIndex;
		}

		if (!world.Has<Owner>(entity)) return -1;
		
		var pEnt = world.Get<Owner>(entity).PlayerEntity.Value;
		if (world.IsAlive(pEnt) && world.Has<UnitOwnerPlayer>(pEnt))
		{
			return world.Get<UnitOwnerPlayer>(pEnt).PlayerIndex;
		}

		return -1;
	}

	private static int GetPlayerGold(World world, Entity playerEntity)
	{
		if (!world.IsAlive(playerEntity) || !world.Has<PlayerResources>(playerEntity)) return 1000;
		
		var resources = world.Get<PlayerResources>(playerEntity).Value;
		return resources.Count > 0 ? resources.Values.FirstOrDefault() : 1000;
	}

	private static float GetUnitHealthRatio(World world, Entity unit)
	{
		if (!world.Has<Health>(unit)) return 1.0f;
		
		var hp = world.Get<Health>(unit);
		return Math.Clamp(hp.Current / Math.Max(1.0f, hp.Max), 0f, 1f);
	}

	public void ScanAffordances(World world, int playerIndex, List<GenericAffordance> destinationList, object? customContext = null)
	{
		destinationList.Clear();
		_friendlyUnits.Clear();
		_enemyUnits.Clear();

		Vector3 friendlyCenter = Vector3.Zero;
		Vector3 enemyCenter = Vector3.Zero;

		world.Query(in _allPositionQuery, (Entity entity, ref Position pos) =>
		{
			int ownerIndex = GetOwnerIndex(world, entity);

			if (ownerIndex == playerIndex)
			{
				_friendlyUnits.Add(entity);
				friendlyCenter += pos.Value;
			}
			else if (ownerIndex >= 0)
			{
				_enemyUnits.Add(entity);
				enemyCenter += pos.Value;
			}
		});

		if (_friendlyUnits.Count > 0) friendlyCenter /= _friendlyUnits.Count;
		if (_enemyUnits.Count > 0) enemyCenter /= _enemyUnits.Count;

		Entity playerEntity = Entity.Null;
		world.Query(in _playerResourcesQuery, (Entity pe) =>
		{
			if (playerEntity == Entity.Null)
			{
				playerEntity = pe;
			}
			
			if (world.Has<UnitOwnerPlayer>(pe) && world.Get<UnitOwnerPlayer>(pe).PlayerIndex == playerIndex)
			{
				playerEntity = pe;
			}
		});

		int playerGold = GetPlayerGold(world, playerEntity);

		for (int i = 0; i < _friendlyUnits.Count; i++)
		{
			var unit = _friendlyUnits[i];
			if (!world.IsAlive(unit)) continue;

			var unitPos = world.Get<Position>(unit).Value;
			float healthRatio = GetUnitHealthRatio(world, unit);

			EvaluateCombatAffordances(world, unit, unitPos, healthRatio, enemyCenter, destinationList);
			EvaluateProductionAffordances(world, unit, unitPos, playerGold, destinationList);
			EvaluateSpellAffordances(world, unit, healthRatio, enemyCenter, destinationList);
		}

		EvaluateBaseExpansions(playerGold, destinationList);
	}

	private void EvaluateCombatAffordances(World world, Entity unit, Vector3 unitPos, float healthRatio, Vector3 enemyCenter, List<GenericAffordance> destinationList)
	{
		if (_enemyUnits.Count == 0) return;

		FindEnemyTargets(world, unitPos, out Entity lowestHpEnemy, out Vector3 closestEnemyPos, out float minEnemyDist);

		if (world.IsAlive(lowestHpEnemy))
		{
			float threatScore = Math.Clamp(0.9f * _aggressionBias, 0f, 1f);
			float[] fVec = CreateFeatureVector(0.0f, threatScore, Math.Clamp(minEnemyDist / 50.0f, 0f, 1f), healthRatio, 1.0f, 0.0f, 0.8f, 0.5f);
			destinationList.Add(new GenericAffordance(unit, CommandIntent.Attack, lowestHpEnemy, world.Get<Position>(lowestHpEnemy).Value, "focus_low_hp", fVec));
		}

		float kitingThreshold = 10.0f * _kitingDistanceBias;
		if (minEnemyDist < kitingThreshold)
		{
			Vector3 retreatVector = Vector3.Normalize(unitPos - closestEnemyPos) * (15.0f * _kitingDistanceBias) + unitPos;
			float[] fVec = CreateFeatureVector(0.0f, 0.2f, 0.1f, healthRatio, 0.3f, 0.0f, 0.9f, 0.1f);
			destinationList.Add(new GenericAffordance(unit, CommandIntent.MoveTo, Entity.Null, retreatVector, "kiting_retreat", fVec));
		}

		if (healthRatio <= _retreatHealthPercent)
		{
			Vector3 rallyDest = _rallyPoints.Count > 0 ? _rallyPoints[0] : (Vector3.Normalize(unitPos - closestEnemyPos) * 20.0f + unitPos);
			float retreatUrgency = Math.Clamp((_retreatHealthPercent - healthRatio) / _retreatHealthPercent + 0.5f, 0.5f, 1.0f);
			float[] fVec = CreateFeatureVector(0.0f, 0.1f, 0.1f, healthRatio, 0.2f, 0.0f, retreatUrgency, 0.1f);
			destinationList.Add(new GenericAffordance(unit, CommandIntent.MoveTo, Entity.Null, rallyDest, "emergency_retreat", fVec));
		}

		float aggCenterScore = Math.Clamp(0.8f * _aggressionBias, 0f, 1f);
		float[] attackCenterVec = CreateFeatureVector(0.0f, aggCenterScore, Math.Clamp(Vector3.Distance(unitPos, enemyCenter) / 50.0f, 0f, 1f), healthRatio, 0.8f, 0.0f, 0.5f, 0.5f);
		destinationList.Add(new GenericAffordance(unit, CommandIntent.MoveTo, Entity.Null, enemyCenter, "threat_centroid", attackCenterVec));
	}

	private void FindEnemyTargets(World world, Vector3 unitPos, out Entity lowestHpEnemy, out Vector3 closestEnemyPos, out float minEnemyDist)
	{
		lowestHpEnemy = Entity.Null;
		float minHp = float.MaxValue;
		closestEnemyPos = Vector3.Zero;
		minEnemyDist = float.MaxValue;

		for (int eIdx = 0; eIdx < _enemyUnits.Count; eIdx++)
		{
			var enemy = _enemyUnits[eIdx];
			if (!world.IsAlive(enemy)) continue;
			
			var ePos = world.Get<Position>(enemy).Value;
			float dist = Vector3.Distance(unitPos, ePos);
			if (dist < minEnemyDist)
			{
				minEnemyDist = dist;
				closestEnemyPos = ePos;
			}

			if (world.Has<Health>(enemy))
			{
				float eHp = world.Get<Health>(enemy).Current;
				if (eHp < minHp)
				{
					minHp = eHp;
					lowestHpEnemy = enemy;
				}
			}
		}
	}

	private static void EvaluateProductionAffordances(World world, Entity unit, Vector3 unitPos, int playerGold, List<GenericAffordance> destinationList)
	{
		if (!world.Has<ProductionQueue>(unit)) return;

		ref var queue = ref world.Get<ProductionQueue>(unit);
		if (queue.UnitIds.Count < 5)
		{
			float costRatio = Math.Clamp(100.0f / Math.Max(1, playerGold), 0f, 1f);
			float[] fVec = CreateFeatureVector(costRatio, 0.5f, 0.0f, 1.0f, 0.6f, 0.0f, 0.0f, 0.7f);
			destinationList.Add(new GenericAffordance(unit, CommandIntent.Train, Entity.Null, unitPos, "train_unit", fVec));
		}
	}

	private void EvaluateSpellAffordances(World world, Entity unit, float healthRatio, Vector3 enemyCenter, List<GenericAffordance> destinationList)
	{
		if (!world.Has<SpellCooldowns>(unit)) return;

		var cd = world.Get<SpellCooldowns>(unit);
		foreach (var kvp in cd.Value)
		{
			if (kvp.Value <= 0.0f)
			{
				float[] fVec = CreateFeatureVector(0.1f, 0.9f, 0.2f, healthRatio, 0.9f, 1.0f, 0.1f, 0.9f);
				destinationList.Add(new GenericAffordance(unit, CommandIntent.Cast, _enemyUnits.Count > 0 ? _enemyUnits[0] : Entity.Null, enemyCenter, kvp.Key, fVec));
			}
		}
	}

	private void EvaluateBaseExpansions(int playerGold, List<GenericAffordance> destinationList)
	{
		if (_expansionLocations.Count == 0 || playerGold < 400 || _friendlyUnits.Count == 0) return;

		for (int expIdx = 0; expIdx < _expansionLocations.Count; expIdx++)
		{
			var expPos = _expansionLocations[expIdx];
			float costRatio = Math.Clamp(400.0f / Math.Max(1, playerGold), 0f, 1f);
			float[] fVec = CreateFeatureVector(costRatio, 0.3f, 0.5f, 1.0f, 0.9f, 0.0f, 0.0f, 0.8f);
			destinationList.Add(new GenericAffordance(_friendlyUnits[0], CommandIntent.MoveTo, Entity.Null, expPos, $"expand_base_{expIdx}", fVec));
		}
	}

	public void ExecuteAction(World world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback = null)
	{
		if (aff.Intent == CommandIntent.Interact || aff.Intent == CommandIntent.Transact)
		{
			customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, aff.Intent.ToString());
			return;
		}

		if (!world.IsAlive(aff.SourceEntity)) return;

		switch (aff.Intent)
		{
			case CommandIntent.Attack:
				ExecuteAttack(world, in aff);
				break;

			case CommandIntent.MoveTo:
				world.AddOrGet(aff.SourceEntity, new MoveTo(aff.TargetPosition));
				break;

			case CommandIntent.Train:
				ExecuteTrain(world, in aff);
				break;

			case CommandIntent.Cast:
				ExecuteCast(world, playerIndex, in aff, customActionCallback);
				break;

			default:
				customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, aff.Intent.ToString());
				break;
		}
	}

	private static void ExecuteAttack(World world, in GenericAffordance aff)
	{
		if (world.IsAlive(aff.TargetEntity))
		{
			world.AddOrGet(aff.SourceEntity, new AttackTarget(aff.TargetEntity));
		}
	}

	private static void ExecuteTrain(World world, in GenericAffordance aff)
	{
		if (!world.Has<ProductionQueue>(aff.SourceEntity)) return;

		ref var q = ref world.Get<ProductionQueue>(aff.SourceEntity);
		if (q.UnitIds.Count < 5)
		{
			q.UnitIds.Add("grunt");
		}
	}

	private static void ExecuteCast(World world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback)
	{
		if (!string.IsNullOrEmpty(aff.PayloadId) && world.Has<SpellCooldowns>(aff.SourceEntity))
		{
			ref var cooldowns = ref world.Get<SpellCooldowns>(aff.SourceEntity);
			cooldowns.Value[aff.PayloadId] = 5.0f;
		}
		customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, "Cast");
	}

	private static float[] CreateFeatureVector(float costToBank, float targetThreat, float rangeFactor, float healthRatio, float tempoEfficiency, float cooldownReady, float retreatUrgency, float synergyTag)
	{
		return new float[StandardFeatureCount]
		{
			costToBank,
			targetThreat,
			rangeFactor,
			healthRatio,
			tempoEfficiency,
			cooldownReady,
			retreatUrgency,
			synergyTag
		};
	}
}
