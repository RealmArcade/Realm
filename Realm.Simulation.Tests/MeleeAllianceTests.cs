using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Tags;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays an alliance declared in a lobby: two seats on one team share the bounty a neutral monster pays, do not count
///     as each other's enemies, and keep each other alive through the victory rule that a team stays in the match while one
///     of its nexuses stands.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeAllianceTests
{
	private static readonly Vector3 DefenderPosition = new(-50f, 0f, -50f);

	private const float CreepBountyGold = 55f;

	private MeleeMatch _match = null!;

	[TearDown]
	public void TearDown()
	{
		_match?.Dispose();
	}

	[Test]
	public void AlliedSeatsSplitTheBountyANeutralMonsterPays()
	{
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartOnMap(MeleeTestMaps.ObjectiveBoardFolder, new[] { "Vale", "Sable", "Corvus" }, MeleeTestMaps.ScriptPath, teams: new[] { 1, 1, 2 });

		UnitId monster = OnlyPlacedMonster();
		float firstBefore = _match.Gold(1);
		float secondBefore = _match.Gold(2);
		float enemyBefore = _match.Gold(3);

		_match.Kill(monster, _match.Hero(2));

		float allyShare = _match.Gold(1) - firstBefore;
		float killerShare = _match.Gold(2) - secondBefore;

		Assert.That(allyShare, Is.EqualTo(MathF.Floor(CreepBountyGold / 2f)).Within(0.01f), "Gold is shared across a team, so an ally receives its half of the bounty.");
		Assert.That(killerShare, Is.EqualTo(MathF.Ceiling(CreepBountyGold / 2f)).Within(0.01f), "The seat that landed the killing blow receives the other half plus the odd coin an even split cannot divide.");
		Assert.That(allyShare + killerShare, Is.EqualTo(CreepBountyGold).Within(0.01f), "The whole bounty reaches the team that earned it, with no gold lost to rounding.");
		Assert.That(_match.Gold(3) - enemyBefore, Is.Zero, "A team that was not part of the kill receives nothing.");
	}

	[Test]
	public void AnAllyIsNotAnEnemy()
	{
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartWithTeams(new[] { "Vale", "Sable", "Corvus" }, new[] { 1, 1, 2 });

		Assert.That(_match.ReadPlayer(1).Team, Is.EqualTo(_match.ReadPlayer(2).Team), "The two allied seats have to sit on one team.");
		Assert.That(_match.Api.ArePlayersAllied(1, 2), Is.True, "The team the lobby declared has to reach the script as an alliance.");
		Assert.That(_match.Api.ArePlayersAllied(1, 3), Is.False, "A seat on another team is not an ally of this one.");

		UnitId attacker = _match.SpawnSoldier(1, "unit/axeman", DefenderPosition);
		UnitId ally = _match.SpawnSoldier(2, "unit/axeman", DefenderPosition + new Vector3(1.5f, 0f, 0f));
		float allyHealth = _match.HealthOf(ally);

		_match.Api.IssueAttackOrder(attacker, ally);
		_match.Step(8f);

		Assert.That(_match.IsAlive(ally), Is.True, "An ally ordered onto an ally has to survive the order.");
		Assert.That(_match.HealthOf(ally), Is.EqualTo(allyHealth).Within(0.01f), "An ally's blow must not be resolved against an ally.");
	}

	[Test]
	public void ATeamStaysInTheMatchWhileAnAlliedNexusStands()
	{
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartWithTeams(new[] { "Vale", "Sable", "Corvus" }, new[] { 1, 1, 2 });

		UnitId enemyNexus = _match.Nexus(3);
		Assert.That(enemyNexus.IsValid, Is.True, "Every seat has to start with the nexus it is eliminated by.");

		_match.Kill(enemyNexus, _match.Hero(1));
		_match.Step(1f);

		Assert.That(_match.ReadPlayer(3).Active, Is.False, "A team whose last nexus is destroyed leaves the match.");
		Assert.That(_match.ReadPlayer(1).Active, Is.True, "The winning team stays in the match.");
		Assert.That(_match.IsAlive(_match.Nexus(1)), Is.True, "The allied nexus is untouched by the team elimination.");

		_match.Kill(_match.Nexus(2), _match.Hero(3));
		_match.Step(1f);

		Assert.That(_match.ReadPlayer(2).Active, Is.True, "A player whose own nexus is destroyed stays active while an allied nexus survives.");
		Assert.That(_match.IsAlive(_match.Hero(2)), Is.True, "An ally that lost its nexus keeps playing, so its army stays on the board.");
	}

	private UnitId OnlyPlacedMonster()
	{
		ReadOnlySpan<UnitId> neutralUnits = _match.Api.GetUnitsOwnedByPlayer(0);
		Assert.That(neutralUnits.Length, Is.EqualTo(1), "The board places one monster nobody owns.");

		return neutralUnits[0];
	}
}
