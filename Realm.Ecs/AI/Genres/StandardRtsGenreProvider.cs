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
			if (world.Has<UnitOwnerPlayer>(pe) && world.Get<UnitOwnerPlayer>(pe).PlayerIndex == playerIndex)
			{
				playerEntity = pe;
			}
			else if (playerEntity == Entity.Null)
			{
				playerEntity = pe;
			}
		});

		int playerGold = 1000;
		if (world.IsAlive(playerEntity) && world.Has<PlayerResources>(playerEntity))
		{
			var resources = world.Get<PlayerResources>(playerEntity).Value;
			if (resources.Count > 0)
			{
				playerGold = resources.Values.FirstOrDefault();
			}
		}

		for (int i = 0; i < _friendlyUnits.Count; i++)
		{
			var unit = _friendlyUnits[i];
			if (!world.IsAlive(unit)) continue;

			var unitPos = world.Get<Position>(unit).Value;
			float healthRatio = 1.0f;
			if (world.Has<Health>(unit))
			{
				var hp = world.Get<Health>(unit);
				healthRatio = Math.Clamp(hp.Current / Math.Max(1.0f, hp.Max), 0f, 1f);
			}

			if (_enemyUnits.Count > 0)
			{
				Entity lowestHpEnemy = Entity.Null;
				float minHp = float.MaxValue;
				Vector3 closestEnemyPos = Vector3.Zero;
				float minEnemyDist = float.MaxValue;

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

				if (world.IsAlive(lowestHpEnemy))
				{
					float[] fVec = CreateFeatureVector(0.0f, 0.9f, Math.Clamp(minEnemyDist / 50.0f, 0f, 1f), healthRatio, 1.0f, 0.0f, 0.8f, 0.5f);
					destinationList.Add(new GenericAffordance(unit, CommandIntent.Attack, lowestHpEnemy, world.Get<Position>(lowestHpEnemy).Value, "focus_low_hp", fVec));
				}

				if (minEnemyDist < 10.0f)
				{
					Vector3 retreatVector = Vector3.Normalize(unitPos - closestEnemyPos) * 15.0f + unitPos;
					float[] fVec = CreateFeatureVector(0.0f, 0.2f, 0.1f, healthRatio, 0.3f, 0.0f, 0.9f, 0.1f);
					destinationList.Add(new GenericAffordance(unit, CommandIntent.MoveTo, Entity.Null, retreatVector, "kiting_retreat", fVec));
				}

				float[] attackCenterVec = CreateFeatureVector(0.0f, 0.8f, Math.Clamp(Vector3.Distance(unitPos, enemyCenter) / 50.0f, 0f, 1f), healthRatio, 0.8f, 0.0f, 0.5f, 0.5f);
				destinationList.Add(new GenericAffordance(unit, CommandIntent.MoveTo, Entity.Null, enemyCenter, "threat_centroid", attackCenterVec));
			}

			if (world.Has<ProductionQueue>(unit))
			{
				ref var queue = ref world.Get<ProductionQueue>(unit);
				if (queue.UnitIds.Count < 5)
				{
					float costRatio = Math.Clamp(100.0f / Math.Max(1, playerGold), 0f, 1f);
					float[] fVec = CreateFeatureVector(costRatio, 0.5f, 0.0f, 1.0f, 0.6f, 0.0f, 0.0f, 0.7f);
					destinationList.Add(new GenericAffordance(unit, CommandIntent.Train, Entity.Null, unitPos, "train_unit", fVec));
				}
			}

			if (world.Has<SpellCooldowns>(unit))
			{
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
				if (world.IsAlive(aff.TargetEntity))
				{
					world.AddOrGet(aff.SourceEntity, new AttackTarget(aff.TargetEntity));
				}
				break;

			case CommandIntent.MoveTo:
				world.AddOrGet(aff.SourceEntity, new MoveTo(aff.TargetPosition));
				break;

			case CommandIntent.Train:
				if (world.Has<ProductionQueue>(aff.SourceEntity))
				{
					ref var q = ref world.Get<ProductionQueue>(aff.SourceEntity);
					if (q.UnitIds.Count < 5)
					{
						q.UnitIds.Add("grunt");
					}
				}
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
