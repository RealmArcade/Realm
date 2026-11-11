using System.Linq;
using NUnit.Framework;

namespace Realm.Simulation.Tests;

/// <summary>
///     Mirrors the authored numbers of the melee content set, so a test states what the map promises rather than reading it
///     back out of the map it is checking.
/// </summary>
internal static class MeleeContent
{
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

	public static int MaximumChargesOf(string buildingTemplateId)
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

	public static float RechargeSecondsOf(string buildingTemplateId)
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

	public static float GoldPerSwingOf(string buildingTemplateId)
	{
		return buildingTemplateId switch
		{
			"building/bulwark_hall" => 30f,
			"building/warband_yard" => 16f,
			"building/spear_lodge" => 10f,
			"building/archery_range" => 0f,
			"building/bolt_foundry" => 0f,
			"building/sling_camp" => 0f,
			"building/mender_shrine" => 14f,
			"building/signal_post" => 6f,
			"building/vigil_bastion" => 10f,
			_ => throw new AssertionException($"The draw offered '{buildingTemplateId}', which no building profile pays.")
		};
	}

	public static float WoodPerSwingOf(string buildingTemplateId)
	{
		return buildingTemplateId switch
		{
			"building/bulwark_hall" => 0f,
			"building/warband_yard" => 0f,
			"building/spear_lodge" => 0f,
			"building/archery_range" => 22f,
			"building/bolt_foundry" => 8f,
			"building/sling_camp" => 16f,
			"building/mender_shrine" => 0f,
			"building/signal_post" => 0f,
			"building/vigil_bastion" => 0f,
			_ => throw new AssertionException($"The draw offered '{buildingTemplateId}', which no building profile pays.")
		};
	}
}
