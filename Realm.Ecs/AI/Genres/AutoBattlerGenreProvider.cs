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

public class AutoBattlerUnitDef
{
	public string Id { get; set; } = "warrior_tier1";
	public int Cost { get; set; } = 1;
	public string PrimaryTrait { get; set; } = "Warrior";
	public string OriginTrait { get; set; } = "Human";
	public bool IsFrontline { get; set; } = true;
}

public class AutoBattlerGenreProvider : IAiGenreProvider
{
	public const int AbFeatureCount = 8;
	public string GenreName => "auto_battler";

	public int FeatureCount => AbFeatureCount;

	public IReadOnlyList<string> FeatureNames { get; } = new string[]
	{
		"CostToBank",
		"SynergyTraitScore",
		"UpgradeProximity",
		"BoardPositioningBalance",
		"EconomyInterestThreshold",
		"ShopRerollVsLevelUtility",
		"HpPreservationUrgency",
		"ItemEquipSynergy"
	};

	public List<Vector3> BoardSlots { get; } = new();
	public List<Vector3> BenchSlots { get; } = new();
	public List<AutoBattlerUnitDef> ShopPool { get; } = new();
	public List<string> CurrentShopOfferings { get; } = new();
	public int RerollCost { get; set; } = 2;
	public int LevelUpCost { get; set; } = 4;
	public int MaxBoardUnits { get; set; } = 6;

	private readonly List<Entity> _friendlyBoardUnits = new(16);
	private readonly List<Entity> _friendlyBenchUnits = new(16);
	private readonly QueryDescription _playerResourcesQuery = new QueryDescription().WithAll<PlayerResources>();

	public AutoBattlerGenreProvider()
	{
		ShopPool.Add(new AutoBattlerUnitDef { Id = "footman", Cost = 1, PrimaryTrait = "Warrior", OriginTrait = "Human", IsFrontline = true });
		ShopPool.Add(new AutoBattlerUnitDef { Id = "archer", Cost = 1, PrimaryTrait = "Ranger", OriginTrait = "Elf", IsFrontline = false });
		ShopPool.Add(new AutoBattlerUnitDef { Id = "mage", Cost = 2, PrimaryTrait = "Mage", OriginTrait = "Human", IsFrontline = false });
		ShopPool.Add(new AutoBattlerUnitDef { Id = "knight", Cost = 3, PrimaryTrait = "Warrior", OriginTrait = "Human", IsFrontline = true });
		ShopPool.Add(new AutoBattlerUnitDef { Id = "assassin", Cost = 2, PrimaryTrait = "Rogue", OriginTrait = "Undead", IsFrontline = true });

		for (int i = 0; i < 5; i++)
		{
			CurrentShopOfferings.Add(ShopPool[i % ShopPool.Count].Id);
		}
	}

	public float[] GetDefaultWeights()
	{
		return new float[AbFeatureCount]
		{
			-0.35f, // CostToBank
			 0.90f, // SynergyTraitScore
			 0.85f, // UpgradeProximity
			 0.70f, // BoardPositioningBalance
			 0.60f, // EconomyInterestThreshold
			 0.50f, // ShopRerollVsLevelUtility
			 0.80f, // HpPreservationUrgency
			 0.40f  // ItemEquipSynergy
		};
	}

	public BotProfile CreateDefaultProfile(string mapName)
	{
		return new BotProfile
		{
			Genre = "auto_battler",
			MapName = mapName,
			DecisionIntervalSeconds = 1.0f,
			Weights = GetDefaultWeights()
		};
	}

	public void ConfigureFromParameters(IReadOnlyDictionary<string, string> parameters)
	{
		if (parameters == null) return;

		if (parameters.TryGetValue("BoardSlotsJson", out var boardJson) && !string.IsNullOrWhiteSpace(boardJson))
		{
			try
			{
				var slots = JsonSerializer.Deserialize<List<Vector3>>(boardJson);
				if (slots != null)
				{
					BoardSlots.Clear();
					BoardSlots.AddRange(slots);
				}
			}
			catch { }
		}

		if (parameters.TryGetValue("BenchSlotsJson", out var benchJson) && !string.IsNullOrWhiteSpace(benchJson))
		{
			try
			{
				var slots = JsonSerializer.Deserialize<List<Vector3>>(benchJson);
				if (slots != null)
				{
					BenchSlots.Clear();
					BenchSlots.AddRange(slots);
				}
			}
			catch { }
		}

		if (parameters.TryGetValue("ShopOfferingsJson", out var shopJson) && !string.IsNullOrWhiteSpace(shopJson))
		{
			try
			{
				var offerings = JsonSerializer.Deserialize<List<string>>(shopJson);
				if (offerings != null)
				{
					CurrentShopOfferings.Clear();
					CurrentShopOfferings.AddRange(offerings);
				}
			}
			catch { }
		}

		if (parameters.TryGetValue("RerollCost", out var rCost) && int.TryParse(rCost, out int rc))
		{
			RerollCost = rc;
		}

		if (parameters.TryGetValue("LevelUpCost", out var lCost) && int.TryParse(lCost, out int lc))
		{
			LevelUpCost = lc;
		}
	}

	public void ScanAffordances(World world, int playerIndex, List<GenericAffordance> destinationList, object? customContext = null)
	{
		destinationList.Clear();
		_friendlyBoardUnits.Clear();
		_friendlyBenchUnits.Clear();

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

		int playerGold = 25;
		int playerHealth = 100;
		if (world.IsAlive(playerEntity))
		{
			if (world.Has<PlayerResources>(playerEntity))
			{
				var res = world.Get<PlayerResources>(playerEntity).Value;
				if (res.Count > 0) playerGold = res.Values.FirstOrDefault();
			}
			if (world.Has<Health>(playerEntity))
			{
				playerHealth = (int)world.Get<Health>(playerEntity).Current;
			}
		}

		float hpUrgency = Math.Clamp((100 - playerHealth) / 100.0f, 0f, 1f);
		float interestThreshold = Math.Clamp((playerGold % 10) / 10.0f, 0f, 1f);

		for (int sIdx = 0; sIdx < CurrentShopOfferings.Count; sIdx++)
		{
			string offeringId = CurrentShopOfferings[sIdx];
			var unitDef = ShopPool.FirstOrDefault(u => string.Equals(u.Id, offeringId, StringComparison.OrdinalIgnoreCase))
						  ?? new AutoBattlerUnitDef { Id = offeringId, Cost = 1 };

			if (playerGold >= unitDef.Cost)
			{
				float costRatio = Math.Clamp((float)unitDef.Cost / Math.Max(1, playerGold), 0f, 1f);
				float synergyScore = string.Equals(unitDef.PrimaryTrait, "Warrior", StringComparison.OrdinalIgnoreCase) ? 0.85f : 0.6f;
				float upgradeProx = 0.7f;
				float posBalance = unitDef.IsFrontline ? 0.8f : 0.6f;

				float[] fVec = new float[AbFeatureCount]
				{
					costRatio,
					synergyScore,
					upgradeProx,
					posBalance,
					interestThreshold,
					0.4f,
					hpUrgency,
					0.5f
				};

				destinationList.Add(new GenericAffordance(
					Entity.Null,
					CommandIntent.Transact,
					Entity.Null,
					Vector3.Zero,
					$"buy_shop_unit:{unitDef.Id}:{sIdx}",
					fVec
				));
			}
		}

		if (playerGold >= RerollCost)
		{
			float costRatio = Math.Clamp((float)RerollCost / Math.Max(1, playerGold), 0f, 1f);
			float[] fVec = new float[AbFeatureCount]
			{
				costRatio,
				0.3f,
				0.5f,
				0.3f,
				interestThreshold,
				0.75f,
				hpUrgency,
				0.2f
			};

			destinationList.Add(new GenericAffordance(
				Entity.Null,
				CommandIntent.Interact,
				Entity.Null,
				Vector3.Zero,
				"reroll_shop",
				fVec
			));
		}

		if (playerGold >= LevelUpCost)
		{
			float costRatio = Math.Clamp((float)LevelUpCost / Math.Max(1, playerGold), 0f, 1f);
			float[] fVec = new float[AbFeatureCount]
			{
				costRatio,
				0.6f,
				0.2f,
				0.5f,
				interestThreshold,
				0.65f,
				hpUrgency,
				0.3f
			};

			destinationList.Add(new GenericAffordance(
				Entity.Null,
				CommandIntent.Interact,
				Entity.Null,
				Vector3.Zero,
				"levelup_shop",
				fVec
			));
		}
	}

	public void ExecuteAction(World world, int playerIndex, in GenericAffordance aff, Action<int, string, Vector3, string>? customActionCallback = null)
	{
		customActionCallback?.Invoke(playerIndex, aff.PayloadId, aff.TargetPosition, aff.Intent.ToString());
	}
}
