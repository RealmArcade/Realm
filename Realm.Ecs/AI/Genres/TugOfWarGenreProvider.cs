using Arch.Core;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using System.Numerics;
using System.Text.Json;

namespace Realm.Ecs.AI.Genres;

public class TugOfWarGenreProvider : IAiGenreProvider
{
	public const int FeatureVectorLength = 8;
	public string GenreName => "tug_of_war";

	public int FeatureCount => FeatureVectorLength;

	public IReadOnlyList<string> FeatureNames { get; } = new string[]
	{
		"CostToBank",
		"LanePressureRatio",
		"CounterAdvantage",
		"IncomeEfficiency",
		"SpawnerCapacityRatio",
		"TechTierUrgency",
		"SpecialWaveReadiness",
		"EmergencyDefense"
	};

	private readonly List<Vector3> _buildSpots = new(32);
	private readonly List<string> _spawnerTypes = new(16);
	private int _incomeUpgradeCost = 100;
	private bool _allowSell;
	private Vector3 _laneTarget = Vector3.Zero;

	private readonly List<Entity> _friendlySpawners = new(32);
	private readonly List<Entity> _friendlyUnits = new(64);
	private readonly List<Entity> _enemyUnits = new(64);
	private readonly QueryDescription _allPositionQuery = new QueryDescription().WithAll<Position>().WithNone<Dead>();
	private readonly QueryDescription _playerResourcesQuery = new QueryDescription().WithAll<PlayerResources>();

	public float[] GetDefaultWeights()
	{
		return new float[FeatureVectorLength]
		{
			-0.5f, // CostToBank
			 0.8f, // LanePressureRatio
			 0.9f, // CounterAdvantage
			 0.7f, // IncomeEfficiency
			 0.6f, // SpawnerCapacityRatio
			 0.5f, // TechTierUrgency
			 0.4f, // SpecialWaveReadiness
			 0.9f  // EmergencyDefense
		};
	}

	public BotProfile CreateDefaultProfile(string mapName)
	{
		return new BotProfile
		{
			Genre = "tug_of_war",
			MapName = mapName,
			Weights = GetDefaultWeights()
		};
	}

	public void ConfigureFromParameters(IReadOnlyDictionary<string, string> parameters)
	{
		ConfigureBuildSpots(parameters);
		ConfigureSpawnerTypes(parameters);
		ConfigureIncomeUpgradeCost(parameters);
		ConfigureAllowSell(parameters);
		ConfigureLaneTarget(parameters);
	}

	private void ConfigureBuildSpots(IReadOnlyDictionary<string, string> parameters)
	{
		if (!parameters.TryGetValue("BuildSpotsJson", out var buildSpotsJson) || string.IsNullOrWhiteSpace(buildSpotsJson)) return;
		try
		{
			var spots = JsonSerializer.Deserialize<List<Vector3>>(buildSpotsJson);
			if (spots == null) return;
			_buildSpots.Clear();
			_buildSpots.AddRange(spots);
		}
		catch { }
	}

	private void ConfigureSpawnerTypes(IReadOnlyDictionary<string, string> parameters)
	{
		if (!parameters.TryGetValue("SpawnerTypesJson", out var spawnerTypesJson) || string.IsNullOrWhiteSpace(spawnerTypesJson)) return;
		try
		{
			var types = JsonSerializer.Deserialize<List<string>>(spawnerTypesJson);
			if (types == null) return;
			_spawnerTypes.Clear();
			_spawnerTypes.AddRange(types);
		}
		catch { }
	}

	private void ConfigureIncomeUpgradeCost(IReadOnlyDictionary<string, string> parameters)
	{
		if (!parameters.TryGetValue("IncomeUpgradeCost", out var incomeCostStr) || !int.TryParse(incomeCostStr, out var incomeCost)) return;
		_incomeUpgradeCost = incomeCost;
	}

	private void ConfigureAllowSell(IReadOnlyDictionary<string, string> parameters)
	{
		if (!parameters.TryGetValue("AllowSell", out var allowSellStr) || !bool.TryParse(allowSellStr, out var allowSell)) return;
		_allowSell = allowSell;
	}

	private void ConfigureLaneTarget(IReadOnlyDictionary<string, string> parameters)
	{
		if (!parameters.TryGetValue("LaneTargetJson", out var laneTargetJson) || string.IsNullOrWhiteSpace(laneTargetJson)) return;
		try
		{
			var target = JsonSerializer.Deserialize<Vector3>(laneTargetJson);
			_laneTarget = target;
		}
		catch { }
	}

	public void ScanAffordances(World world, int playerIndex, List<GenericAffordance> destinationList, object? customContext = null)
	{
		destinationList.Clear();
		_friendlySpawners.Clear();
		_friendlyUnits.Clear();
		_enemyUnits.Clear();

		GatherEntities(world, playerIndex);

		int playerGold = GetPlayerGold(world, playerIndex);

		float lanePressureRatio = _enemyUnits.Count > 0
			? Math.Clamp((float)_enemyUnits.Count / Math.Max(1, _friendlyUnits.Count + _enemyUnits.Count), 0f, 1f)
			: 0.1f;

		float spawnerCapacity = _buildSpots.Count > 0
			? Math.Clamp((float)_friendlySpawners.Count / _buildSpots.Count, 0f, 1f)
			: 0f;

		GenerateBuildAffordances(world, destinationList, playerGold, lanePressureRatio, spawnerCapacity);
		GenerateUpgradeAffordances(world, destinationList, playerGold, lanePressureRatio, spawnerCapacity);
		GenerateEconomyAffordance(destinationList, playerGold, lanePressureRatio, spawnerCapacity);
		GenerateSpecialWaveAffordance(destinationList, playerGold, lanePressureRatio, spawnerCapacity);
	}

	private void GatherEntities(World world, int playerIndex)
	{
		world.Query(in _allPositionQuery, (Entity entity, ref Position pos) =>
		{
			int ownerIndex = GetEntityOwnerIndex(world, entity);

			if (ownerIndex == playerIndex)
			{
				if (world.Has<ProductionQueue>(entity) || world.Has<Building>(entity))
				{
					_friendlySpawners.Add(entity);
				}
				else
				{
					_friendlyUnits.Add(entity);
				}
			}
			else if (ownerIndex >= 0)
			{
				_enemyUnits.Add(entity);
			}
		});
	}

	private static int GetEntityOwnerIndex(World world, Entity entity)
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

	private int GetPlayerGold(World world, int playerIndex)
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

		if (!world.IsAlive(playerEntity) || !world.Has<PlayerResources>(playerEntity)) return 100;

		var resources = world.Get<PlayerResources>(playerEntity).Value;
		return resources.Count > 0 ? resources.Values.FirstOrDefault() : 100;
	}

	private bool IsSpotOccupied(World world, Vector3 spot)
	{
		for (int fIdx = 0; fIdx < _friendlySpawners.Count; fIdx++)
		{
			var spawner = _friendlySpawners[fIdx];
			if (!world.IsAlive(spawner)) continue;
			if (Vector3.DistanceSquared(world.Get<Position>(spawner).Value, spot) < 9.0f)
			{
				return true;
			}
		}
		return false;
	}

	private void GenerateBuildAffordances(World world, List<GenericAffordance> destinationList, int playerGold, float lanePressureRatio, float spawnerCapacity)
	{
		for (int sIdx = 0; sIdx < _buildSpots.Count; sIdx++)
		{
			var spot = _buildSpots[sIdx];
			if (IsSpotOccupied(world, spot)) continue;

			if (_spawnerTypes.Count > 0)
			{
				for (int tIdx = 0; tIdx < _spawnerTypes.Count; tIdx++)
				{
					var spawnerType = _spawnerTypes[tIdx];
					float costRatio = Math.Clamp(100.0f / Math.Max(1, playerGold), 0f, 1f);
					float counterAdvantage = 0.5f + (tIdx % 2 == 0 ? 0.3f : 0.1f);
					float[] fVec = CreateFeatureVector(costRatio, lanePressureRatio, counterAdvantage, 0.6f, spawnerCapacity, 0.4f, 0.0f, lanePressureRatio > 0.6f ? 0.8f : 0.2f);
					destinationList.Add(new GenericAffordance(Entity.Null, CommandIntent.Build, Entity.Null, spot, $"build_{spawnerType}_{sIdx}", fVec));
				}
			}
			else
			{
				float costRatio = Math.Clamp(100.0f / Math.Max(1, playerGold), 0f, 1f);
				float[] fVec = CreateFeatureVector(costRatio, lanePressureRatio, 0.7f, 0.6f, spawnerCapacity, 0.4f, 0.0f, lanePressureRatio > 0.6f ? 0.8f : 0.2f);
				destinationList.Add(new GenericAffordance(Entity.Null, CommandIntent.Build, Entity.Null, spot, $"build_spawner_{sIdx}", fVec));
			}
		}
	}

	private void GenerateUpgradeAffordances(World world, List<GenericAffordance> destinationList, int playerGold, float lanePressureRatio, float spawnerCapacity)
	{
		for (int fIdx = 0; fIdx < _friendlySpawners.Count; fIdx++)
		{
			var spawner = _friendlySpawners[fIdx];
			if (!world.IsAlive(spawner)) continue;

			var pos = world.Get<Position>(spawner).Value;
			float costRatio = Math.Clamp(150.0f / Math.Max(1, playerGold), 0f, 1f);
			float[] fVec = CreateFeatureVector(costRatio, lanePressureRatio, 0.8f, 0.5f, spawnerCapacity, 0.9f, 0.1f, 0.4f);
			destinationList.Add(new GenericAffordance(spawner, CommandIntent.Train, Entity.Null, pos, $"upgrade_spawner_{spawner.Id}", fVec));

			if (!_allowSell) continue;

			float[] sellVec = CreateFeatureVector(0.0f, lanePressureRatio, 0.1f, 0.1f, 1.0f, 0.1f, 0.0f, lanePressureRatio > 0.8f ? 0.5f : 0.0f);
			destinationList.Add(new GenericAffordance(spawner, CommandIntent.Transact, Entity.Null, pos, $"sell_spawner_{spawner.Id}", sellVec));
		}
	}

	private void GenerateEconomyAffordance(List<GenericAffordance> destinationList, int playerGold, float lanePressureRatio, float spawnerCapacity)
	{
		if (_incomeUpgradeCost <= 0) return;

		float costRatio = Math.Clamp((float)_incomeUpgradeCost / Math.Max(1, playerGold), 0f, 1f);
		float[] incomeVec = CreateFeatureVector(costRatio, lanePressureRatio, 0.2f, 0.95f, spawnerCapacity, 0.6f, 0.1f, 0.1f);
		destinationList.Add(new GenericAffordance(Entity.Null, CommandIntent.Transact, Entity.Null, Vector3.Zero, "upgrade_income", incomeVec));
	}

	private void GenerateSpecialWaveAffordance(List<GenericAffordance> destinationList, int playerGold, float lanePressureRatio, float spawnerCapacity)
	{
		float specialWaveCostRatio = Math.Clamp(200.0f / Math.Max(1, playerGold), 0f, 1f);
		float[] waveVec = CreateFeatureVector(specialWaveCostRatio, 1.0f - lanePressureRatio, 0.8f, 0.3f, spawnerCapacity, 0.3f, 0.9f, 0.2f);
		destinationList.Add(new GenericAffordance(Entity.Null, CommandIntent.Transact, Entity.Null, _laneTarget, "send_mercenary_wave", waveVec));
	}

	public void ExecuteAction(World world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback = null)
	{
		if (aff.Intent == CommandIntent.Build || aff.Intent == CommandIntent.Transact || aff.Intent == CommandIntent.Interact)
		{
			customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, aff.Intent.ToString());
			return;
		}

		if (world.IsAlive(aff.SourceEntity) && aff.Intent == CommandIntent.Train)
		{
			if (world.Has<ProductionQueue>(aff.SourceEntity))
			{
				ref var q = ref world.Get<ProductionQueue>(aff.SourceEntity);
				if (q.UnitIds.Count < 5)
				{
					q.UnitIds.Add("spawner_upgrade");
				}
			}
			customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, "Upgrade");
		}
	}

	private static float[] CreateFeatureVector(float costToBank, float lanePressureRatio, float counterAdvantage, float incomeEfficiency, float spawnerCapacityRatio, float techTierUrgency, float specialWaveReadiness, float emergencyDefense)
	{
		return new float[FeatureVectorLength]
		{
			costToBank,
			lanePressureRatio,
			counterAdvantage,
			incomeEfficiency,
			spawnerCapacityRatio,
			techTierUrgency,
			specialWaveReadiness,
			emergencyDefense
		};
	}
}
