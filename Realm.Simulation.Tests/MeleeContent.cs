using System.Linq;
using NUnit.Framework;

namespace Realm.Simulation.Tests;

/// <summary>
///     Mirrors the authored numbers of the melee content set, so a test states what the map promises rather than reading it
///     back out of the map it is checking.
/// </summary>
internal static class MeleeContent
{
	public const float ProductionMana = 100f;

	public const string Shieldbearer = "unit/shieldbearer";
	public const string Axeman = "unit/axeman";

	/// <summary>
	///     Every package in the shared building deck, which is also the set of commands a construction draw may reveal.
	/// </summary>
	public static readonly string[] DraftableBuildings =
	{
		"building/bulwark_hall",
		"building/warband_yard",
		"building/spear_lodge",
		"building/archery_range",
		"building/sling_camp",
		"building/bolt_foundry",
		"building/mender_shrine",
		"building/signal_post",
		"building/vigil_bastion"
	};

	/// <summary>
	///     Every soldier template the deck can raise. A building's worker is not one of them: it carries the economy and
	///     never joins the army, so a test that counts what a building fields must not count it.
	/// </summary>
	public static readonly string[] SoldierTemplateIds = DraftableBuildings.Select(SoldierFor).Distinct().ToArray();

	public static string SoldierFor(string buildingTemplateId)
	{
		return buildingTemplateId switch
		{
			"building/bulwark_hall" => Shieldbearer,
			"building/warband_yard" => Axeman,
			"building/spear_lodge" => "unit/spear_militia",
			"building/archery_range" => "unit/bowman",
			"building/sling_camp" => "unit/slinger",
			"building/bolt_foundry" => "unit/bolt_thrower",
			"building/mender_shrine" => "unit/mender",
			"building/signal_post" => "unit/standard_bearer",
			"building/vigil_bastion" => "unit/warder",
			_ => throw new AssertionException($"The draw offered '{buildingTemplateId}', which no soldier profile grants.")
		};
	}

	public static string SoldierAbilityFor(string buildingTemplateId)
	{
		return buildingTemplateId switch
		{
			"building/bulwark_hall" => "ability/raise_shieldbearer",
			"building/warband_yard" => "ability/raise_axeman",
			"building/spear_lodge" => "ability/raise_spear_militia",
			"building/archery_range" => "ability/raise_bowman",
			"building/sling_camp" => "ability/raise_slinger",
			"building/bolt_foundry" => "ability/raise_bolt_thrower",
			"building/mender_shrine" => "ability/raise_mender",
			"building/signal_post" => "ability/raise_standard_bearer",
			"building/vigil_bastion" => "ability/raise_warder",
			_ => throw new AssertionException($"The draw offered '{buildingTemplateId}', which no soldier profile grants.")
		};
	}

	public static string WorkerAbilityFor(string buildingTemplateId)
	{
		return buildingTemplateId switch
		{
			"building/bulwark_hall" => "ability/raise_worker_bulwark_hall",
			"building/warband_yard" => "ability/raise_worker_warband_yard",
			"building/spear_lodge" => "ability/raise_worker_spear_lodge",
			"building/archery_range" => "ability/raise_worker_archery_range",
			"building/sling_camp" => "ability/raise_worker_sling_camp",
			"building/bolt_foundry" => "ability/raise_worker_bolt_foundry",
			"building/mender_shrine" => "ability/raise_worker_mender_shrine",
			"building/signal_post" => "ability/raise_worker_signal_post",
			"building/vigil_bastion" => "ability/raise_worker_vigil_bastion",
			_ => throw new AssertionException($"The draw offered '{buildingTemplateId}', which no worker profile serves.")
		};
	}

	public static int CapacityOf(string buildingTemplateId)
	{
		return buildingTemplateId switch
		{
			"building/bulwark_hall" => 6,
			"building/warband_yard" => 6,
			"building/spear_lodge" => 5,
			"building/archery_range" => 8,
			"building/sling_camp" => 8,
			"building/bolt_foundry" => 2,
			"building/mender_shrine" => 4,
			"building/signal_post" => 3,
			"building/vigil_bastion" => 3,
			_ => throw new AssertionException($"The draw offered '{buildingTemplateId}', which no soldier profile grants.")
		};
	}

	/// <summary>The mana pool a building of that package holds at whole, which is one production per stored soldier and worker.</summary>
	public static int MaximumManaOf(string buildingTemplateId)
	{
		return (int)((SoldierProductionsOf(buildingTemplateId) + WorkerProductionsOf(buildingTemplateId)) * ProductionMana);
	}

	public static int SoldierProductionsOf(string buildingTemplateId)
	{
		return buildingTemplateId switch
		{
			"building/bulwark_hall" => 3,
			"building/warband_yard" => 3,
			"building/spear_lodge" => 3,
			"building/archery_range" => 4,
			"building/sling_camp" => 4,
			"building/bolt_foundry" => 1,
			"building/mender_shrine" => 2,
			"building/signal_post" => 2,
			"building/vigil_bastion" => 2,
			_ => throw new AssertionException($"The draw offered '{buildingTemplateId}', which no soldier profile grants.")
		};
	}

	public static float SoldierProductionSecondsOf(string buildingTemplateId)
	{
		return buildingTemplateId switch
		{
			"building/bulwark_hall" => 22f,
			"building/warband_yard" => 20f,
			"building/spear_lodge" => 24f,
			"building/archery_range" => 18f,
			"building/sling_camp" => 16f,
			"building/bolt_foundry" => 30f,
			"building/mender_shrine" => 26f,
			"building/signal_post" => 28f,
			"building/vigil_bastion" => 26f,
			_ => throw new AssertionException($"The draw offered '{buildingTemplateId}', which no soldier profile grants.")
		};
	}

	public static int WorkerCapacityOf(string buildingTemplateId)
	{
		return buildingTemplateId switch
		{
			"building/bulwark_hall" => 2,
			"building/warband_yard" => 2,
			"building/spear_lodge" => 2,
			"building/archery_range" => 3,
			"building/sling_camp" => 3,
			"building/bolt_foundry" => 1,
			"building/mender_shrine" => 1,
			"building/signal_post" => 2,
			"building/vigil_bastion" => 2,
			_ => throw new AssertionException($"The draw offered '{buildingTemplateId}', which no worker profile serves.")
		};
	}

	public static int WorkerProductionsOf(string buildingTemplateId)
	{
		return buildingTemplateId switch
		{
			"building/bulwark_hall" => 1,
			"building/warband_yard" => 2,
			"building/spear_lodge" => 1,
			"building/archery_range" => 2,
			"building/sling_camp" => 3,
			"building/bolt_foundry" => 1,
			"building/mender_shrine" => 1,
			"building/signal_post" => 1,
			"building/vigil_bastion" => 1,
			_ => throw new AssertionException($"The draw offered '{buildingTemplateId}', which no worker profile serves.")
		};
	}

	public static float WorkerProductionSecondsOf(string buildingTemplateId)
	{
		return buildingTemplateId switch
		{
			"building/bulwark_hall" => 45f,
			"building/warband_yard" => 30f,
			"building/spear_lodge" => 35f,
			"building/archery_range" => 25f,
			"building/sling_camp" => 20f,
			"building/bolt_foundry" => 50f,
			"building/mender_shrine" => 40f,
			"building/signal_post" => 30f,
			"building/vigil_bastion" => 35f,
			_ => throw new AssertionException($"The draw offered '{buildingTemplateId}', which no worker profile serves.")
		};
	}

	public static string WorkerResourceOf(string buildingTemplateId)
	{
		return buildingTemplateId switch
		{
			"building/archery_range" or "building/bolt_foundry" or "building/sling_camp" => "wood",
			_ => "gold"
		};
	}

	public static string WorkerItemOf(string buildingTemplateId)
	{
		return WorkerResourceOf(buildingTemplateId) == "wood" ? "item/wood" : "item/gold";
	}

	public const string WorkerTemplateId = "unit/worker";
}
