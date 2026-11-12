using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Core;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the inspector on the authority: a hero carries its innate as a passive command card and its bulwark as a buff
///     a player can hover, a building's worker crew carries the buff its building gave it, and the stat sheet's old
///     readings rows are no longer published at all. The buff text is read from the presentation stream the client's stat
///     panel consumes.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeInspectorTests
{
	private static readonly Vector3 SitePosition = new(-50f, 0f, -50f);

	private MeleeMatch _match = null!;

	[SetUp]
	public void SetUp()
	{
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartDuel();
	}

	[TearDown]
	public void TearDown()
	{
		_match.Dispose();
	}

	[Test]
	public void TheHeroCarriesItsInnateAsAPassiveCommandCard()
	{
		var innate = _match.Api.Abilities.GetDefinition("ability/innate_unbroken");

		Assert.That(innate, Is.Not.Null, "Vale's innate has to be an ability definition the command grid can draw.");
		Assert.That(innate!.IsPassive, Is.True, "An innate is never cast, so its card must be a passive one.");
		Assert.That(innate.TooltipTemplate, Does.Contain("[Innate]"), "The card tooltip has to name the ability as the hero's innate.");
		Assert.That(innate.TooltipTemplate, Does.Contain("armor"), "The card tooltip has to describe what the innate does.");
		Assert.That(innate.IconPath, Is.Not.Empty, "The innate needs the icon its command card draws.");
		Assert.That(innate.GridX, Is.InRange(0, 3), "The innate has to sit inside the four-column command grid.");
		Assert.That(innate.GridY, Is.InRange(0, 2), "The innate has to sit inside the three-row command grid.");

		UnitId hero = _match.Hero(1);
		Assert.That(_match.TryGet(hero, out AbilityState abilityState), Is.True, "The hero has to carry an ability list.");

		var abilities = new List<string>();
		abilityState.CopyTo(abilities);
		Assert.That(abilities, Does.Contain("ability/innate_unbroken"), "The innate has to be part of the list the command grid draws.");
	}

	[Test]
	public void TheBulwarkIsABuffOnTheHeroAndTheNexus()
	{
		UnitId hero = _match.Hero(1);
		UnitId nexus = _match.Nexus(1);

		_match.Step(1.2f);

		Assert.That(_match.Api.UnitHasBuff(hero, "buff/bulwark"), Is.True, "The hero's bulwark has to be a buff the stat sheet can list.");
		Assert.That(_match.Api.UnitHasBuff(nexus, "buff/bulwark"), Is.True, "The Nexus carries the bulwark as a buff too.");

		Assert.That(_match.UnitTooltipText(1, hero, "buff.bulwark.name"), Is.EqualTo("Bulwark"), "The buff icon has to be named Bulwark.");
		Assert.That(_match.UnitTooltipText(1, hero, "buff.bulwark.description"), Does.Contain("Nearby soldiers: 0"),
			"The hero's hover text has to state the soldiers it counts.");
		Assert.That(_match.UnitTooltipText(1, hero, "buff.bulwark.icon"), Does.EndWith(".webp"), "The buff needs the icon the strip draws.");

		Assert.That(_match.UnitTooltipText(1, nexus, "buff.bulwark.description"), Does.Contain("Allied buildings: 1"),
			"The Nexus hover text has to state the buildings its bulwark counts.");
	}

	[Test]
	public void AWorkerCarriesTheBuffItsBuildingGaveIt()
	{
		_match.PlaceCompletedBuilding(SitePosition);
		_match.StepUntilWorkers(1);
		_match.Step(0.2f);

		IReadOnlyList<UnitId> workers = _match.Workers(1);
		Assert.That(workers.Count, Is.GreaterThan(0), "A finished building has to raise a worker crew.");

		UnitId worker = workers[0];
		Assert.That(_match.TryGet(worker, out Buffs buffs), Is.True, "A worker has to carry the buff its building applied.");
		Assert.That(buffs.Count, Is.GreaterThan(0), "The worker's building has to leave at least one buff on it.");

		string buffId = buffs.GetKey(0) ?? string.Empty;
		Assert.That(buffId, Is.Not.Empty, "The buff has to have an identifier the map can describe.");

		string core = buffId.StartsWith("buff/", System.StringComparison.OrdinalIgnoreCase) ? buffId["buff/".Length..] : buffId;
		string name = _match.UnitTooltipText(1, worker, $"buff.{core}.name");
		string description = _match.UnitTooltipText(1, worker, $"buff.{core}.description");
		string icon = _match.UnitTooltipText(1, worker, $"buff.{core}.icon");

		Assert.That(name, Does.EndWith("Crew"), $"The buff has to be named for the building that gave it, not '{buffId}'.");
		Assert.That(description, Is.Not.Empty, "The buff has to describe the stats its building raised.");
		Assert.That(icon, Does.EndWith(".webp"), "The buff needs the icon the strip draws.");
	}

	[Test]
	public void TheOldInspectorRowsAreNoLongerPublished()
	{
		UnitId building = _match.PlaceCompletedBuilding(SitePosition);
		UnitId hero = _match.Hero(1);
		_match.Step(0.2f);

		Assert.That(_match.UnitTooltipText(1, building, "readout.0.name"), Is.Empty, "The stat sheet no longer draws a readings block.");
		Assert.That(_match.UnitTooltipText(1, building, "readout.0.value"), Is.Empty, "The stat sheet no longer draws a readings block.");
		Assert.That(_match.UnitTooltipText(1, hero, "readout.0.name"), Is.Empty, "The hero's innate is a command card now, not a reading.");
		Assert.That(_match.UnitTooltipText(1, hero, "readout.1.value"), Is.Empty, "The relic count is the inventory's to show, not a reading.");
	}
}
