using Arch.Core;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Realm.Ecs.AI.Genres;

public class TowerTypeDefinition
{
	public string Id { get; set; } = "arrow_tower";
	public int Cost { get; set; } = 100;
	public float Damage { get; set; } = 25f;
	public float Range { get; set; } = 15f;
	public float AttackSpeed { get; set; } = 1.0f;
	public string DamageType { get; set; } = "Physical";
	public bool HasSlow { get; set; } = false;
	public string? NextUpgradeId { get; set; }
	public int UpgradeCost { get; set; } = 150;
}

public class TowerDefenseGenreProvider : IAiGenreProvider
{
	public const int TdFeatureCount = 8;
	public string GenreName => "tower_defense";

	public int FeatureCount => TdFeatureCount;

	public IReadOnlyList<string> FeatureNames { get; } = new string[]
	{
		"CostToBank",
		"CreepWaveThreat",
		"PositionStrategicValue",
		"DamagePerGoldEfficiency",
		"DamageTypeAffinity",
		"SlowSynergy",
		"CriticalLeakUrgency",
		"EconomyInterestThreshold"
	};

	public List<Vector3> BuildSpots { get; } = new();
	public List<TowerTypeDefinition> AvailableTowers { get; } = new();
	public bool AllowSell { get; set; } = false;

	private readonly List<Entity> _existingTowers = new(32);
	private readonly List<Entity> _activeCreeps = new(64);
	private readonly QueryDescription _enemyCreepQuery = new QueryDescription().WithAll<Position, Health>().WithNone<Dead>();
	private readonly QueryDescription _playerResourcesQuery = new QueryDescription().WithAll<PlayerResources>();

	public TowerDefenseGenreProvider()
	{
		AvailableTowers.Add(new TowerTypeDefinition { Id = "arrow_tower", Cost = 100, Damage = 25f, Range = 15f, DamageType = "Physical", NextUpgradeId = "sniper_tower", UpgradeCost = 150 });
		AvailableTowers.Add(new TowerTypeDefinition { Id = "cannon_tower", Cost = 175, Damage = 60f, Range = 12f, DamageType = "Siege", NextUpgradeId = "mortar_tower", UpgradeCost = 225 });
		AvailableTowers.Add(new TowerTypeDefinition { Id = "frost_tower", Cost = 125, Damage = 15f, Range = 14f, DamageType = "Magic", HasSlow = true, NextUpgradeId = "blizzard_tower", UpgradeCost = 200 });
	}

	public float[] GetDefaultWeights()
	{
		return new float[TdFeatureCount]
		{
			-0.45f, // CostToBank
			 0.85f, // CreepWaveThreat
			 0.65f, // PositionStrategicValue
			 0.75f, // DamagePerGoldEfficiency
			 0.50f, // DamageTypeAffinity
			 0.70f, // SlowSynergy
			 0.95f, // CriticalLeakUrgency
			 0.40f  // EconomyInterestThreshold
		};
	}

	public BotProfile CreateDefaultProfile(string mapName)
	{
		return new BotProfile
		{
			Genre = "tower_defense",
			MapName = mapName,
			DecisionIntervalSeconds = 1.25f,
			Weights = GetDefaultWeights()
		};
	}

	public void ConfigureFromParameters(IReadOnlyDictionary<string, string> parameters)
	{
		if (parameters == null) return;

		if (parameters.TryGetValue("BuildSpotsJson", out var spotsJson) && !string.IsNullOrWhiteSpace(spotsJson))
		{
			try
			{
				var spots = JsonSerializer.Deserialize<List<Vector3>>(spotsJson);
				if (spots != null)
				{
					BuildSpots.Clear();
					BuildSpots.AddRange(spots);
				}
			}
			catch { }
		}

		if (parameters.TryGetValue("TowersJson", out var towersJson) && !string.IsNullOrWhiteSpace(towersJson))
		{
			try
			{
				var towers = JsonSerializer.Deserialize<List<TowerTypeDefinition>>(towersJson);
				if (towers != null)
				{
					AvailableTowers.Clear();
					AvailableTowers.AddRange(towers);
				}
			}
			catch { }
		}

		if (parameters.TryGetValue("AllowSell", out var allowSellStr) && bool.TryParse(allowSellStr, out bool sellVal))
		{
			AllowSell = sellVal;
		}
	}

	public void ScanAffordances(World world, int playerIndex, List<GenericAffordance> destinationList, object? customContext = null)
	{
		destinationList.Clear();
		_existingTowers.Clear();
		_activeCreeps.Clear();

		Vector3 creepCenter = Vector3.Zero;
		float minCreepDistToExit = float.MaxValue;
		Vector3 leakThreatPos = Vector3.Zero;

		world.Query(in _enemyCreepQuery, (Entity e, ref Position p, ref Health hp) =>
		{
			int ownerIndex = -1;
			if (world.Has<UnitOwnerPlayer>(e))
			{
				ownerIndex = world.Get<UnitOwnerPlayer>(e).PlayerIndex;
			}
			else if (world.Has<Owner>(e))
			{
				var pEnt = world.Get<Owner>(e).PlayerEntity.Value;
				if (world.IsAlive(pEnt) && world.Has<UnitOwnerPlayer>(pEnt))
				{
					ownerIndex = world.Get<UnitOwnerPlayer>(pEnt).PlayerIndex;
				}
			}

			if (ownerIndex != playerIndex)
			{
				_activeCreeps.Add(e);
				creepCenter += p.Value;

				float distToExit = p.Value.Length();
				if (distToExit < minCreepDistToExit)
				{
					minCreepDistToExit = distToExit;
					leakThreatPos = p.Value;
				}
			}
			else
			{
				_existingTowers.Add(e);
			}
		});

		int creepCount = _activeCreeps.Count;
		if (creepCount > 0)
		{
			creepCenter /= creepCount;
		}

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
			var res = world.Get<PlayerResources>(playerEntity).Value;
			if (res.Count > 0)
			{
				playerGold = res.Values.FirstOrDefault();
			}
		}

		float waveThreat = Math.Clamp(creepCount / 20.0f, 0f, 1f);
		float leakUrgency = creepCount > 0 ? Math.Clamp((50.0f - minCreepDistToExit) / 50.0f, 0f, 1f) : 0f;
		float interestThreshold = Math.Clamp(playerGold / 1000.0f, 0f, 1f);

		var occupiedPositions = new List<Vector3>(_existingTowers.Count);
		for (int i = 0; i < _existingTowers.Count; i++)
		{
			var tower = _existingTowers[i];
			if (world.IsAlive(tower) && world.Has<Position>(tower))
			{
				occupiedPositions.Add(world.Get<Position>(tower).Value);
			}
		}

		for (int sIdx = 0; sIdx < BuildSpots.Count; sIdx++)
		{
			var spot = BuildSpots[sIdx];
			bool isOccupied = false;
			for (int oIdx = 0; oIdx < occupiedPositions.Count; oIdx++)
			{
				if (Vector3.DistanceSquared(spot, occupiedPositions[oIdx]) < 4.0f)
				{
					isOccupied = true;
					break;
				}
			}

			if (isOccupied) continue;

			float distToCreepCenter = creepCount > 0 ? Vector3.Distance(spot, creepCenter) : 50.0f;
			float strategicValue = Math.Clamp((60.0f - distToCreepCenter) / 60.0f, 0.1f, 1.0f);

			for (int tIdx = 0; tIdx < AvailableTowers.Count; tIdx++)
			{
				var towerDef = AvailableTowers[tIdx];
				if (playerGold >= towerDef.Cost)
				{
					float costRatio = Math.Clamp((float)towerDef.Cost / Math.Max(1, playerGold), 0f, 1f);
					float dps = towerDef.Damage * towerDef.AttackSpeed;
					float dpsPerGold = Math.Clamp((dps / towerDef.Cost) * 4.0f, 0f, 1f);
					float affinity = string.Equals(towerDef.DamageType, "Magic", StringComparison.OrdinalIgnoreCase) ? 0.8f : 0.6f;
					float slowFactor = towerDef.HasSlow ? 1.0f : 0.0f;

					float[] fVec = new float[TdFeatureCount]
					{
						costRatio,
						waveThreat,
						strategicValue,
						dpsPerGold,
						affinity,
						slowFactor,
						leakUrgency,
						interestThreshold
					};

					destinationList.Add(new GenericAffordance(
						Entity.Null,
						CommandIntent.Build,
						Entity.Null,
						spot,
						$"build_tower:{towerDef.Id}",
						fVec
					));
				}
			}
		}

		for (int i = 0; i < _existingTowers.Count; i++)
		{
			var tower = _existingTowers[i];
			if (!world.IsAlive(tower)) continue;

			var towerPos = world.Has<Position>(tower) ? world.Get<Position>(tower).Value : Vector3.Zero;
			var towerDef = AvailableTowers.FirstOrDefault();
			if (towerDef != null && !string.IsNullOrEmpty(towerDef.NextUpgradeId) && playerGold >= towerDef.UpgradeCost)
			{
				float costRatio = Math.Clamp((float)towerDef.UpgradeCost / Math.Max(1, playerGold), 0f, 1f);
				float strategicValue = creepCount > 0 ? Math.Clamp((60.0f - Vector3.Distance(towerPos, creepCenter)) / 60.0f, 0.1f, 1f) : 0.5f;

				float[] fVec = new float[TdFeatureCount]
				{
					costRatio,
					waveThreat,
					strategicValue,
					0.9f,
					0.7f,
					towerDef.HasSlow ? 1.0f : 0.2f,
					leakUrgency,
					interestThreshold
				};

				destinationList.Add(new GenericAffordance(
					tower,
					CommandIntent.Interact,
					Entity.Null,
					towerPos,
					$"upgrade_tower:{towerDef.NextUpgradeId}",
					fVec
				));
			}

			if (AllowSell && playerGold < 100 && leakUrgency > 0.7f)
			{
				float[] fVec = new float[TdFeatureCount]
				{
					0.0f,
					waveThreat,
					0.1f,
					0.2f,
					0.0f,
					0.0f,
					leakUrgency,
					0.1f
				};

				destinationList.Add(new GenericAffordance(
					tower,
					CommandIntent.Transact,
					Entity.Null,
					towerPos,
					"sell_tower",
					fVec
				));
			}
		}
	}

	public void ExecuteAction(World world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback = null)
	{
		customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, aff.Intent.ToString());
	}
}
