using System.Numerics;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the soldier abilities the melee set gives no button: the Mender's heal, the Warder's shield and the Standard
///     Bearer's aura, each fired by the map's own auto-cast rule rather than by a player order.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeAutoCastTests
{
	private const float MendHeal = 22f;
	private const float MendCooldownSeconds = 2.2f;
	private const float StandardAuraBuffSeconds = 6f;

	private static readonly Vector3 CasterPosition = new(-40f, 0f, -50f);
	private static readonly Vector3 BesideCasterPosition = new(-38f, 0f, -50f);
	private static readonly Vector3 FarFromCasterPosition = new(-40f, 0f, -34f);

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
	public void TheMenderHealsTheMostWoundedAllyBesideIt()
	{
		_match.SpawnSoldier(1, "unit/mender", CasterPosition);
		UnitId ally = _match.SpawnSoldier(1, "unit/shieldbearer", BesideCasterPosition);
		_match.Api.SetUnitHealth(ally, _match.MaxHealthOf(ally) * 0.5f);

		float beforeHeal = _match.HealthOf(ally);
		_match.Step(0.5f);

		Assert.That(_match.HealthOf(ally), Is.GreaterThan(beforeHeal + MendHeal * 0.5f), "The Mender has to heal the wounded soldier beside it without any order.");
	}

	[Test]
	public void TheMenderWaitsOutItsOwnCooldown()
	{
		_match.SpawnSoldier(1, "unit/mender", CasterPosition);
		UnitId ally = _match.SpawnSoldier(1, "unit/shieldbearer", BesideCasterPosition);
		_match.Api.SetUnitHealth(ally, _match.MaxHealthOf(ally) * 0.4f);

		_match.Step(0.5f);
		float afterFirstHeal = _match.HealthOf(ally);

		_match.Step(MendCooldownSeconds - 1f);
		Assert.That(_match.HealthOf(ally), Is.LessThan(afterFirstHeal + MendHeal * 0.5f), "A second heal inside the 2.2 second cooldown must not land.");

		_match.Step(2f);
		Assert.That(_match.HealthOf(ally), Is.GreaterThan(afterFirstHeal + MendHeal * 0.5f), "The heal has to return once the cooldown runs out.");
	}

	[Test]
	public void TheMenderIgnoresAnAllyOutOfItsReach()
	{
		_match.SpawnSoldier(1, "unit/mender", CasterPosition);
		UnitId ally = _match.SpawnSoldier(1, "unit/shieldbearer", FarFromCasterPosition);
		_match.Api.SetUnitHealth(ally, _match.MaxHealthOf(ally) * 0.5f);

		float beforeHeal = _match.HealthOf(ally);
		_match.Step(1f);

		Assert.That(_match.HealthOf(ally), Is.LessThan(beforeHeal + MendHeal * 0.5f), "A Mender heals within its range of eight, not across the board.");
	}

	[Test]
	public void TheWarderShieldsTheWeakestAllyBesideIt()
	{
		_match.SpawnSoldier(1, "unit/warder", CasterPosition);
		UnitId ally = _match.SpawnSoldier(1, "unit/shieldbearer", BesideCasterPosition);
		_match.Api.SetUnitHealth(ally, _match.MaxHealthOf(ally) * 0.4f);

		_match.Step(0.5f);

		Assert.That(_match.TryGet(ally, out AbsorbShield shield), Is.True, "The Warding Aegis has to leave an absorb pool on the ally it shielded.");
		Assert.That(shield.Total(), Is.EqualTo(60f).Within(1f), "The Aegis shields for its authored sixty points.");

		float healthWithShield = _match.HealthOf(ally);
		float shieldBeforeHit = shield.Total();
		_match.Api.DealTypedDamage(_match.Hero(2), ally, 30f, "physical");

		Assert.That(_match.HealthOf(ally), Is.EqualTo(healthWithShield).Within(0.01f), "A hit the shield can cover must not reach the shielded soldier's health.");
		Assert.That(_match.TryGet(ally, out AbsorbShield afterHit), Is.True, "The shield has to keep whatever the hit did not spend.");
		Assert.That(afterHit.Total(), Is.LessThan(shieldBeforeHit), "The shield has to spend the damage it covered.");
		Assert.That(afterHit.Total(), Is.GreaterThanOrEqualTo(shieldBeforeHit - 30f), "The shield must not spend more than the resolved hit.");
	}

	[Test]
	public void TheStandardBearerKeepsItsAuraOnTheSoldiersAroundIt()
	{
		Vector3 heroPosition = _match.PositionOf(_match.Hero(1));
		_match.SpawnSoldier(1, "unit/standard_bearer", heroPosition + new Vector3(4f, 0f, 0f));
		UnitId near = _match.SpawnSoldier(1, "unit/axeman", heroPosition + new Vector3(5f, 0f, 0f));
		UnitId far = _match.SpawnSoldier(1, "unit/axeman", heroPosition + new Vector3(24f, 0f, 0f));

		_match.Step(0.5f);

		Assert.That(_match.Api.UnitHasBuff(near, "buff/standard_aura"), Is.True, "The Standard Bearer has to put its aura on the soldiers beside it.");
		Assert.That(_match.Api.UnitHasBuff(far, "buff/standard_aura"), Is.False, "A soldier beyond the aura's reach must not carry it.");
		Assert.That(_match.SpeedOf(near), Is.EqualTo(_match.SpeedOf(far) * 1.1f).Within(_match.SpeedOf(far) * 0.02f), "The aura has to move the soldier beside the bearer ten percent faster.");

		_match.Step(StandardAuraBuffSeconds + 1f);
		Assert.That(_match.Api.UnitHasBuff(near, "buff/standard_aura"), Is.True, "The aura has to be renewed so it never lapses while the bearer stands.");
	}

	[Test]
	public void ASoldierWithoutAnAutoCastRuleIsNeverCast()
	{
		UnitId axeman = _match.SpawnSoldier(1, "unit/axeman", CasterPosition);
		UnitId ally = _match.SpawnSoldier(1, "unit/shieldbearer", BesideCasterPosition);
		_match.Api.SetUnitHealth(ally, _match.MaxHealthOf(ally) * 0.4f);

		float beforeStep = _match.HealthOf(ally);
		_match.Step(1f);

		Assert.That(_match.IsAlive(axeman), Is.True, "The axeman stays on the field.");
		Assert.That(_match.TryGet(ally, out AbsorbShield _), Is.False, "An ordinary soldier must not cast a support ability it was never given.");
		Assert.That(_match.HealthOf(ally), Is.LessThan(beforeStep + MendHeal * 0.5f), "An army with no Mender in it must not heal itself.");
	}
}
