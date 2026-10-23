using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.AI.Genres.Common;

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

	private readonly List<(Entity Entity, Vector3 Position)> _friendlyUnits = new(64);
	private int _enemyUnitCount;
	private Entity _lowestHpEnemy;
	private float _lowestEnemyHp;
	private Vector3 _lowestHpEnemyPos;
	private Vector3 _enemyCenter;
	private Vector3 _friendlyCenter;
	private int _scanPlayerIndex;
	private Entity _scanPlayerEntity;

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
			0.6f // Synergy
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
		if (!float.TryParse(strVal, CultureInfo.InvariantCulture, out var floatVal)) return;

		field = Math.Clamp(floatVal, min, max);
	}

	private static void UpdateVectorListParam(IReadOnlyDictionary<string, string> parameters, string key, List<Vector3> list)
	{
		if (!parameters.TryGetValue(key, out var jsonStr) || string.IsNullOrWhiteSpace(jsonStr)) return;

		try
		{
			var parsedList = JsonSerializer.Deserialize<List<Vector3>>(jsonStr);
			if (parsedList == null) return;

			list.Clear();
			list.AddRange(parsedList);
		}
		catch
		{
		}
	}


	private static int GetPlayerGold(EcsWorld world, Entity playerEntity)
	{
		if (!world.IsAlive(playerEntity) || !world.Has<PlayerResources>(playerEntity)) return 1000;

		return world.Get<PlayerResources>(playerEntity).Gold;
	}

	private static float GetUnitHealthRatio(EcsWorld world, Entity unit)
	{
		if (!world.Has<Health>(unit)) return 1.0f;

		var hp = world.Get<Health>(unit);
		return Math.Clamp(hp.Current / Math.Max(1.0f, hp.Max), 0f, 1f);
	}

	public void ScanAffordances(EcsWorld world, int playerIndex, List<GenericAffordance> destinationList, object? customContext = null)
	{
		destinationList.Clear();
		_friendlyUnits.Clear();

		Vector3 friendlyCenterAcc = Vector3.Zero;
		Vector3 enemyCenterAcc = Vector3.Zero;
		int enemyCount = 0;
		Entity lowestHpEnemy = Entity.Null;
		float lowestEnemyHp = float.MaxValue;
		Vector3 lowestHpEnemyPos = Vector3.Zero;

		foreach (var entity in world.Query(in _allPositionQuery))
		{
			ref var pos = ref world.Get<Position>(entity);
			int ownerIndex = AiGenreUtils.GetOwnerIndex(world, entity);
			if (ownerIndex == playerIndex)
			{
				_friendlyUnits.Add((entity, pos.Value));
				friendlyCenterAcc += pos.Value;
			}
			else if (ownerIndex > 0)
			{
				enemyCount++;
				enemyCenterAcc += pos.Value;
				if (world.Has<Health>(entity))
				{
					float eHp = world.Get<Health>(entity).Current;
					if (eHp < lowestEnemyHp)
					{
						lowestEnemyHp = eHp;
						lowestHpEnemy = entity;
						lowestHpEnemyPos = pos.Value;
					}
				}
			}
		}

		_enemyUnitCount = enemyCount;
		_lowestHpEnemy = lowestHpEnemy;
		_lowestHpEnemyPos = lowestHpEnemyPos;

		Vector3 friendlyCenter = _friendlyUnits.Count > 0 ? friendlyCenterAcc / _friendlyUnits.Count : Vector3.Zero;
		Vector3 enemyCenter = _enemyUnitCount > 0 ? enemyCenterAcc / _enemyUnitCount : Vector3.Zero;
		_friendlyCenter = friendlyCenter;
		_enemyCenter = enemyCenter;

		Entity foundPlayerEntity = Entity.Null;
		foreach (var pe in world.Query(in _playerResourcesQuery))
		{
			if (foundPlayerEntity == Entity.Null)
			{
				foundPlayerEntity = pe;
			}

			if (world.Has<UnitOwnerPlayer>(pe) && world.Get<UnitOwnerPlayer>(pe).PlayerIndex == playerIndex)
			{
				foundPlayerEntity = pe;
				break;
			}
		}

		int playerGold = GetPlayerGold(world, foundPlayerEntity);

		for (int i = 0; i < _friendlyUnits.Count; i++)
		{
			var (unit, unitPos) = _friendlyUnits[i];
			if (!world.IsAlive(unit)) continue;

			float healthRatio = GetUnitHealthRatio(world, unit);

			EvaluateCombatAffordances(world, unit, unitPos, healthRatio, enemyCenter, destinationList);
			EvaluateProductionAffordances(world, unit, unitPos, playerGold, destinationList);
			EvaluateSpellAffordances(world, unit, healthRatio, enemyCenter, destinationList);
		}

		EvaluateBaseExpansions(playerGold, destinationList);
	}

	private void EvaluateCombatAffordances(EcsWorld world, Entity unit, Vector3 unitPos, float healthRatio, Vector3 enemyCenter, List<GenericAffordance> destinationList)
	{
		if (_enemyUnitCount == 0) return;

		Entity lowestHpEnemy = _lowestHpEnemy;
		Vector3 closestEnemyPos = lowestHpEnemy != Entity.Null ? _lowestHpEnemyPos : enemyCenter;
		float minEnemyDist = Vector3.Distance(unitPos, closestEnemyPos);

		if (world.IsAlive(lowestHpEnemy))
		{
			float threatScore = Math.Clamp(0.9f * _aggressionBias, 0f, 1f);
			float[] fVec = CreateFeatureVector(0.0f, threatScore, Math.Clamp(minEnemyDist / 50.0f, 0f, 1f), healthRatio, 1.0f, 0.0f, 0.8f, 0.5f);
			destinationList.Add(new GenericAffordance(unit, CommandIntent.Attack, lowestHpEnemy, _lowestHpEnemyPos, "focus_low_hp", fVec));
		}

		float kitingThreshold = 10.0f * _kitingDistanceBias;
		if (minEnemyDist < kitingThreshold)
		{
			Vector3 diff = unitPos - closestEnemyPos;
			Vector3 dir = diff.LengthSquared() > 0.0001f ? Vector3.Normalize(diff) : Vector3.UnitX;
			Vector3 retreatVector = dir * (15.0f * _kitingDistanceBias) + unitPos;
			float[] fVec = CreateFeatureVector(0.0f, 0.2f, 0.1f, healthRatio, 0.3f, 0.0f, 0.9f, 0.1f);
			destinationList.Add(new GenericAffordance(unit, CommandIntent.MoveTo, Entity.Null, retreatVector, "kiting_retreat", fVec));
		}

		if (healthRatio <= _retreatHealthPercent)
		{
			Vector3 diff = unitPos - closestEnemyPos;
			Vector3 dir = diff.LengthSquared() > 0.0001f ? Vector3.Normalize(diff) : Vector3.UnitX;
			Vector3 rallyDest = _rallyPoints.Count > 0 ? _rallyPoints[0] : (dir * 20.0f + unitPos);
			float retreatUrgency = Math.Clamp((_retreatHealthPercent - healthRatio) / _retreatHealthPercent + 0.5f, 0.5f, 1.0f);
			float[] fVec = CreateFeatureVector(0.0f, 0.1f, 0.1f, healthRatio, 0.2f, 0.0f, retreatUrgency, 0.1f);
			destinationList.Add(new GenericAffordance(unit, CommandIntent.MoveTo, Entity.Null, rallyDest, "emergency_retreat", fVec));
		}

		float aggCenterScore = Math.Clamp(0.8f * _aggressionBias, 0f, 1f);
		float[] attackCenterVec = CreateFeatureVector(0.0f, aggCenterScore, Math.Clamp(Vector3.Distance(unitPos, enemyCenter) / 50.0f, 0f, 1f), healthRatio, 0.8f, 0.0f, 0.5f, 0.5f);
		destinationList.Add(new GenericAffordance(unit, CommandIntent.MoveTo, Entity.Null, enemyCenter, "threat_centroid", attackCenterVec));
	}

	private static void EvaluateProductionAffordances(EcsWorld world, Entity unit, Vector3 unitPos, int playerGold, List<GenericAffordance> destinationList)
	{
		if (!world.Has<ProductionQueue>(unit)) return;

		ref var queue = ref world.Get<ProductionQueue>(unit);
		if (queue.Count < 5)
		{
			float costRatio = Math.Clamp(100.0f / Math.Max(1, playerGold), 0f, 1f);
			float[] fVec = CreateFeatureVector(costRatio, 0.5f, 0.0f, 1.0f, 0.6f, 0.0f, 0.0f, 0.7f);
			destinationList.Add(new GenericAffordance(unit, CommandIntent.Train, Entity.Null, unitPos, "train_unit", fVec));
		}
	}

	private void EvaluateSpellAffordances(EcsWorld world, Entity unit, float healthRatio, Vector3 enemyCenter, List<GenericAffordance> destinationList)
	{
		if (!world.Has<SpellCooldowns>(unit)) return;

		var cd = world.Get<SpellCooldowns>(unit);
		foreach (var kvp in cd.Value)
		{
			if (kvp.Value <= 0.0f)
			{
				float[] fVec = CreateFeatureVector(0.1f, 0.9f, 0.2f, healthRatio, 0.9f, 1.0f, 0.1f, 0.9f);
				destinationList.Add(new GenericAffordance(unit, CommandIntent.Cast, _lowestHpEnemy, enemyCenter, kvp.Key, fVec));
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
			destinationList.Add(new GenericAffordance(_friendlyUnits[0].Entity, CommandIntent.MoveTo, Entity.Null, expPos, $"expand_base_{expIdx}", fVec));
		}
	}

	public void ExecuteAction(EcsWorld world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback = null)
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

	private static void ExecuteAttack(EcsWorld world, in GenericAffordance aff)
	{
		if (world.IsAlive(aff.TargetEntity))
		{
			world.AddOrGet(aff.SourceEntity, new AttackTarget(aff.TargetEntity));
		}
	}

	private static void ExecuteTrain(EcsWorld world, in GenericAffordance aff)
	{
		if (!world.Has<ProductionQueue>(aff.SourceEntity)) return;

		ref var q = ref world.Get<ProductionQueue>(aff.SourceEntity);
		if (q.Count < 5)
		{
			q.Enqueue("grunt");
		}
	}

	private static void ExecuteCast(EcsWorld world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback)
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