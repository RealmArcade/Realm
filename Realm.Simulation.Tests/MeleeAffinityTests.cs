using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Core;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the free-for-all anti-kingmaker rule on the authority: a duel is fought at full effectiveness, a third team
///     that joins an existing fight starts out nearly harmless, what it contributes raises its effectiveness while the
///     established participants lose ground, and the engine actually scales the blows with the multiplier the rule states.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeAffinityTests
{
	private static readonly Vector3 FirstAttackerPosition = new(-50f, 0f, -50f);
	private static readonly Vector3 SecondAttackerPosition = new(-50f, 0f, -46f);
	private static readonly Vector3 DefenderPosition = new(-46f, 0f, -48f);

	private MeleeMatch _match = null!;

	[TearDown]
	public void TearDown()
	{
		_match?.Dispose();
	}

	[Test]
	public void AFreshDuelIsFoughtAtFullEffectiveness()
	{
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartDuel();

		_match.Step(1.5f);

		Assert.That(_match.Effectiveness(1, 2), Is.EqualTo(PlayerDamageEffectiveness.FullEffectiveness).Within(0.001f), "Two teams that have just met fight each other at full strength.");
		Assert.That(_match.Effectiveness(2, 1), Is.EqualTo(PlayerDamageEffectiveness.FullEffectiveness).Within(0.001f), "The rule is stated for both directions of a duel.");
	}

	[Test]
	public void TheEngineScalesDamageByTheEffectivenessAPlayerCarries()
	{
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.StartDuel();

		UnitId attacker = _match.SpawnSoldier(1, "unit/axeman", FirstAttackerPosition);
		UnitId firstVictim = _match.SpawnSoldier(2, "unit/axeman", DefenderPosition);
		UnitId secondVictim = _match.SpawnSoldier(2, "unit/axeman", DefenderPosition);

		float firstBefore = _match.HealthOf(firstVictim);
		_match.Api.DealTypedDamage(attacker, firstVictim, 40f, "physical");
		float fullBlow = firstBefore - _match.HealthOf(firstVictim);
		Assert.That(fullBlow, Is.GreaterThan(0f), "A blow at full effectiveness has to cost the victim health.");

		_match.Api.SetPlayerDamageEffectiveness(1, 2, 0.5f);

		float secondBefore = _match.HealthOf(secondVictim);
		_match.Api.DealTypedDamage(attacker, secondVictim, 40f, "physical");
		float scaledBlow = secondBefore - _match.HealthOf(secondVictim);

		Assert.That(scaledBlow, Is.EqualTo(fullBlow * 0.5f).Within(fullBlow * 0.05f), "The multiplier a rule states has to scale the damage the engine resolves.");
	}

	[Test]
	public void ATeamJoiningAnExistingFightStartsNearlyHarmlessAndGrows()
	{
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.Start(new[] { "Vale", "Sable", "Corvus" });

		UnitId firstAttacker = _match.SpawnSoldier(1, "unit/axeman", FirstAttackerPosition);
		UnitId joiner = _match.SpawnSoldier(3, "unit/axeman", SecondAttackerPosition);
		UnitId defender = _match.SpawnSoldier(2, "unit/spear_militia", DefenderPosition);
		_match.Api.SetUnitMaxHealth(defender, 1000000f);
		_match.Api.SetUnitHealth(defender, 1000000f);

		for (int blow = 0; blow < 4; blow++)
		{
			_match.Api.DealTypedDamage(firstAttacker, defender, 40f, "physical");
			_match.Api.DealTypedDamage(joiner, defender, 40f, "physical");
		}

		_match.Step(1.5f);

		float joined = _match.Effectiveness(3, 2);
		float established = _match.Effectiveness(1, 2);

		Assert.That(joined, Is.LessThan(0.9f), "A team that arrives on a fight already in progress may not deal full damage from its first blow.");
		Assert.That(established, Is.LessThan(PlayerDamageEffectiveness.FullEffectiveness), "The team already fighting loses ground to the newcomer.");
		Assert.That(joined, Is.EqualTo(established).Within(0.05f), "Two teams contributing equally to one fight carry the same effectiveness.");

		for (int blow = 0; blow < 40; blow++)
		{
			_match.Api.DealTypedDamage(joiner, defender, 400f, "physical");
		}

		_match.Step(1.5f);

		Assert.That(_match.Effectiveness(3, 2), Is.GreaterThan(joined), "Contributing meaningful damage has to raise a joining team's effectiveness.");
	}

	[Test]
	public void ParticipationFadesSoAnAbandonedFightReturnsToFullEffectiveness()
	{
		MeleeTestMaps.RequireMaps();
		_match = MeleeMatch.Start(new[] { "Vale", "Sable", "Corvus" });

		UnitId firstAttacker = _match.SpawnSoldier(1, "unit/axeman", FirstAttackerPosition);
		UnitId joiner = _match.SpawnSoldier(3, "unit/axeman", SecondAttackerPosition);
		UnitId defender = _match.SpawnSoldier(2, "unit/spear_militia", DefenderPosition);
		_match.Api.SetUnitMaxHealth(defender, 10000000f);
		_match.Api.SetUnitHealth(defender, 10000000f);

		for (int blow = 0; blow < 6; blow++)
		{
			_match.Api.DealTypedDamage(firstAttacker, defender, 40f, "physical");
			_match.Api.DealTypedDamage(joiner, defender, 40f, "physical");
		}

		_match.Step(1.5f);
		float contested = _match.Effectiveness(1, 2);
		Assert.That(contested, Is.LessThan(PlayerDamageEffectiveness.FullEffectiveness), "A contested fight is not fought at full effectiveness.");

		_match.Step(600f);

		Assert.That(_match.Effectiveness(1, 2), Is.EqualTo(PlayerDamageEffectiveness.FullEffectiveness).Within(0.02f), "Participation that has faded out of the window returns the fight to full effectiveness.");
	}
}
