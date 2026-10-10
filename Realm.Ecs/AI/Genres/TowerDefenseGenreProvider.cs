using Arch.Core;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using System.Numerics;
using System.Text.Json;

namespace Realm.Ecs.AI.Genres;

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

		TryLoadBuildSpots(parameters);
		TryLoadTowers(parameters);
		TryLoadAllowSell(parameters);
	}

	private void TryLoadBuildSpots(IReadOnlyDictionary<string, string> parameters)
	{
		if (!parameters.TryGetValue("BuildSpotsJson", out var spotsJson) || string.IsNullOrWhiteSpace(spotsJson))
			return;

		try
		{
			var spots = JsonSerializer.Deserialize<List<Vector3>>(spotsJson);
			if (spots == null) return;

			BuildSpots.Clear();
			BuildSpots.AddRange(spots);
		}
		catch { }
	}

	private void TryLoadTowers(IReadOnlyDictionary<string, string> parameters)
	{
		if (!parameters.TryGetValue("TowersJson", out var towersJson) || string.IsNullOrWhiteSpace(towersJson))
			return;

		try
		{
			var towers = JsonSerializer.Deserialize<List<TowerTypeDefinition>>(towersJson);
			if (towers == null) return;

			AvailableTowers.Clear();
			AvailableTowers.AddRange(towers);
		}
		catch { }
	}

	private void TryLoadAllowSell(IReadOnlyDictionary<string, string> parameters)
	{
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

		AnalyzeEntities(world, playerIndex, out Vector3 creepCenter, out float minCreepDistToExit);

		int creepCount = _activeCreeps.Count;
		if (creepCount > 0) creepCenter /= creepCount;

		int playerGold = GetPlayerGold(world, playerIndex);

		float waveThreat = Math.Clamp(creepCount / 20.0f, 0f, 1f);
		float leakUrgency = creepCount > 0 ? Math.Clamp((50.0f - minCreepDistToExit) / 50.0f, 0f, 1f) : 0f;
		float interestThreshold = Math.Clamp(playerGold / 1000.0f, 0f, 1f);

		var occupiedPositions = GetOccupiedPositions(world);

		EvaluateBuildSpots(destinationList, playerGold, creepCenter, creepCount, waveThreat, leakUrgency, interestThreshold, occupiedPositions);
		EvaluateExistingTowers(world, destinationList, playerGold, creepCenter, creepCount, waveThreat, leakUrgency, interestThreshold);
	}

	private void AnalyzeEntities(World world, int playerIndex, out Vector3 creepCenter, out float minCreepDistToExit)
	{
		Vector3 center = Vector3.Zero;
		float minDist = float.MaxValue;

		world.Query(in _enemyCreepQuery, (Entity e, ref Position p, ref Health hp) =>
		{
			int ownerIndex = GetOwnerIndex(world, e);

			if (ownerIndex == playerIndex)
			{
				_existingTowers.Add(e);
				return;
			}

			_activeCreeps.Add(e);
			center += p.Value;

			float distToExit = p.Value.Length();
			if (distToExit < minDist) minDist = distToExit;
		});

		creepCenter = center;
		minCreepDistToExit = minDist;
	}

	private int GetOwnerIndex(World world, Entity e)
	{
		if (world.Has<UnitOwnerPlayer>(e))
			return world.Get<UnitOwnerPlayer>(e).PlayerIndex;

		if (world.Has<Owner>(e))
		{
			var pEnt = world.Get<Owner>(e).PlayerEntity.Value;
			if (world.IsAlive(pEnt) && world.Has<UnitOwnerPlayer>(pEnt))
				return world.Get<UnitOwnerPlayer>(pEnt).PlayerIndex;
		}

		return -1;
	}

	private int GetPlayerGold(World world, int playerIndex)
	{
		Entity playerEntity = Entity.Null;
		world.Query(in _playerResourcesQuery, (Entity pe) =>
		{
			if (world.Has<UnitOwnerPlayer>(pe) && world.Get<UnitOwnerPlayer>(pe).PlayerIndex == playerIndex)
				playerEntity = pe;
			else if (playerEntity == Entity.Null)
				playerEntity = pe;
		});

		if (world.IsAlive(playerEntity) && world.Has<PlayerResources>(playerEntity))
		{
			var res = world.Get<PlayerResources>(playerEntity).Value;
			if (res.Count > 0) return res.Values.FirstOrDefault();
		}

		return 500;
	}

	private List<Vector3> GetOccupiedPositions(World world)
	{
		var occupiedPositions = new List<Vector3>(_existingTowers.Count);
		for (int i = 0; i < _existingTowers.Count; i++)
		{
			var tower = _existingTowers[i];
			if (world.IsAlive(tower) && world.Has<Position>(tower))
				occupiedPositions.Add(world.Get<Position>(tower).Value);
		}
		return occupiedPositions;
	}

	private void EvaluateBuildSpots(List<GenericAffordance> destinationList, int playerGold, Vector3 creepCenter, int creepCount, float waveThreat, float leakUrgency, float interestThreshold, List<Vector3> occupiedPositions)
	{
		for (int sIdx = 0; sIdx < BuildSpots.Count; sIdx++)
		{
			var spot = BuildSpots[sIdx];
			if (IsSpotOccupied(spot, occupiedPositions)) continue;

			float distToCreepCenter = creepCount > 0 ? Vector3.Distance(spot, creepCenter) : 50.0f;
			float strategicValue = Math.Clamp((60.0f - distToCreepCenter) / 60.0f, 0.1f, 1.0f);

			EvaluateTowerDef(destinationList, spot, playerGold, strategicValue, waveThreat, leakUrgency, interestThreshold);
		}
	}

	private bool IsSpotOccupied(Vector3 spot, List<Vector3> occupiedPositions)
	{
		for (int oIdx = 0; oIdx < occupiedPositions.Count; oIdx++)
		{
			if (Vector3.DistanceSquared(spot, occupiedPositions[oIdx]) < 4.0f) return true;
		}
		return false;
	}

	private void EvaluateTowerDef(List<GenericAffordance> destinationList, Vector3 spot, int playerGold, float strategicValue, float waveThreat, float leakUrgency, float interestThreshold)
	{
		for (int tIdx = 0; tIdx < AvailableTowers.Count; tIdx++)
		{
			var towerDef = AvailableTowers[tIdx];
			if (playerGold < towerDef.Cost) continue;

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

			destinationList.Add(new GenericAffordance(Entity.Null, CommandIntent.Build, Entity.Null, spot, $"build_tower:{towerDef.Id}", fVec));
		}
	}

	private void EvaluateExistingTowers(World world, List<GenericAffordance> destinationList, int playerGold, Vector3 creepCenter, int creepCount, float waveThreat, float leakUrgency, float interestThreshold)
	{
		for (int i = 0; i < _existingTowers.Count; i++)
		{
			var tower = _existingTowers[i];
			if (!world.IsAlive(tower)) continue;

			var towerPos = world.Has<Position>(tower) ? world.Get<Position>(tower).Value : Vector3.Zero;
			EvaluateTowerUpgrade(destinationList, tower, towerPos, playerGold, creepCenter, creepCount, waveThreat, leakUrgency, interestThreshold);
			EvaluateTowerSell(destinationList, tower, towerPos, playerGold, waveThreat, leakUrgency);
		}
	}

	private void EvaluateTowerUpgrade(List<GenericAffordance> destinationList, Entity tower, Vector3 towerPos, int playerGold, Vector3 creepCenter, int creepCount, float waveThreat, float leakUrgency, float interestThreshold)
	{
		var towerDef = AvailableTowers.FirstOrDefault();
		if (towerDef == null || string.IsNullOrEmpty(towerDef.NextUpgradeId) || playerGold < towerDef.UpgradeCost) return;

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

		destinationList.Add(new GenericAffordance(tower, CommandIntent.Interact, Entity.Null, towerPos, $"upgrade_tower:{towerDef.NextUpgradeId}", fVec));
	}

	private void EvaluateTowerSell(List<GenericAffordance> destinationList, Entity tower, Vector3 towerPos, int playerGold, float waveThreat, float leakUrgency)
	{
		if (!AllowSell || playerGold >= 100 || leakUrgency <= 0.7f) return;

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

		destinationList.Add(new GenericAffordance(tower, CommandIntent.Transact, Entity.Null, towerPos, "sell_tower", fVec));
	}

	public void ExecuteAction(World world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback = null)
	{
		customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, aff.Intent.ToString());
	}
}
