using Arch.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using System.Numerics;

namespace Realm.Ecs.AI.Affordances;

/// <summary>
/// Scans the ECS world for candidate affordances (legal actions) available to a specific player.
/// Generates normalized feature vectors for each affordance.
/// </summary>
public class AffordanceScanner
{
	public const int FeatureCount = 8;

	private readonly List<Entity> _friendlyUnits = new(64);
	private readonly List<Entity> _enemyUnits = new(64);

	public List<GenericAffordance> ScanAffordances(World world, int playerIndex, object? definitionManager = null)
	{
		var affordances = new List<GenericAffordance>(32);
		ScanAffordances(world, playerIndex, affordances, definitionManager);
		return affordances;
	}

	public void ScanAffordances(World world, int playerIndex, List<GenericAffordance> destinationList, object? definitionManager = null)
	{
		destinationList.Clear();
		_friendlyUnits.Clear();
		_enemyUnits.Clear();

		Vector3 friendlyCenter = Vector3.Zero;
		Vector3 enemyCenter = Vector3.Zero;

		var allQuery = new QueryDescription().WithAll<Position>().WithNone<Dead>();

		world.Query(in allQuery, (Entity entity, ref Position pos) =>
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
		var pQuery = new QueryDescription().WithAll<PlayerResources>();
		world.Query(in pQuery, (Entity pe) =>
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

	private static float[] CreateFeatureVector(float costToBank, float targetThreat, float rangeFactor, float healthRatio, float tempoEfficiency, float cooldownReady, float retreatUrgency, float synergyTag)
	{
		return new float[]
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
