using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Meta;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays a creep camp the ruleset authors and the board places that nobody owns: the monster stands as nobody's unit,
///     killing it pays the gold and experience its template states to the team that landed the blow, and it comes back
///     after the respawn time its template states so the same location is worth contesting again.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeNeutralRespawnTests
{
	private const float AuthoredBountyGold = 55f;
	private const float AuthoredBountyExperience = 40f;
	private const float AuthoredRespawnSeconds = 45f;

	private MeleeMatch _match = null!;

	[SetUp]
	public void SetUp()
	{
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartOnMap(MeleeTestMaps.ObjectiveBoardFolder, new[] { "Vale", "Sable" }, MeleeTestMaps.ScriptPath);
	}

	[TearDown]
	public void TearDown()
	{
		_match.Dispose();
	}

	[Test]
	public void ThePlacedMonsterStandsAsNobodysUnit()
	{
		UnitId creep = OnlyPlacedMonster();

		Assert.That(_match.IsAlive(creep), Is.True);
		Assert.That(_match.Api.GetUnitsOwnedByPlayer(0).Length, Is.EqualTo(1), "The monster is owned by no seat, so every seat may contest it.");
		Assert.That(_match.Owned(1), Does.Not.Contain(creep), "The monster is not part of a player's army.");
	}

	[Test]
	public void KillingTheMonsterPaysItsBountyToTheKillingTeam()
	{
		UnitId creep = OnlyPlacedMonster();
		UnitId hero = _match.Hero(1);
		float goldBefore = _match.Gold(1);
		float experienceBefore = ExperienceOf(hero);

		_match.Kill(creep, hero);

		Assert.That(_match.IsAlive(creep), Is.False, "The monster dies when its health runs out.");
		Assert.That(_match.Gold(1) - goldBefore, Is.EqualTo(AuthoredBountyGold).Within(0.01f), "The bounty the template states is paid in full to a team of one.");
		Assert.That(ExperienceOf(hero) - experienceBefore, Is.EqualTo(AuthoredBountyExperience).Within(0.01f), "The experience the template states goes to the hero whose level the army follows.");
	}

	[Test]
	public void TheMonsterReturnsAfterItsRespawnTime()
	{
		UnitId creep = OnlyPlacedMonster();
		_match.Kill(creep, _match.Hero(1));

		_match.Step(AuthoredRespawnSeconds - 2f);
		Assert.That(_match.Api.GetUnitsOwnedByPlayer(0).Length, Is.EqualTo(0), "The monster does not return before its respawn time.");

		_match.Step(4f);
		UnitId returned = OnlyPlacedMonster();
		Assert.That(_match.IsAlive(returned), Is.True, "The monster stands again once its respawn time has run out.");
		Assert.That(_match.HealthOf(returned), Is.EqualTo(_match.MaxHealthOf(returned)).Within(0.01f), "The monster that returns stands at full health.");
	}

	private UnitId OnlyPlacedMonster()
	{
		ReadOnlySpan<UnitId> neutralUnits = _match.Api.GetUnitsOwnedByPlayer(0);
		Assert.That(neutralUnits.Length, Is.EqualTo(1), "The board places one monster nobody owns.");

		return neutralUnits[0];
	}

	private float ExperienceOf(UnitId unit)
	{
		return _match.TryGet(unit, out Experience experience) ? experience.Current : 0f;
	}
}
