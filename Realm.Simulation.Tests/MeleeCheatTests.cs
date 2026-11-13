using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Meta;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     Pins the words a player types into chat and the effect each one applies. The authority is the only thing that
///     decides whether a typed line does something, so every word the client offers has to be accepted and every cheat
///     has to change the state it names without reaching a seat that did not ask for it.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeCheatTests
{
	private static IEnumerable<string> ChatWords => CheatCatalog.Words;

	private static MeleeMatch StartCheats(MatchOptionSet? matchOptions = null)
	{
		MeleeTestMaps.RequireMaps();
		return MeleeMatch.StartDuel(matchOptions: matchOptions, cheatsEnabled: true);
	}

	[TestCaseSource(nameof(ChatWords))]
	public void TheAuthorityAcceptsTheCheatWord(string chatWord)
	{
		using MeleeMatch match = StartCheats();

		ServerCommandResult result = match.SubmitCheat(1, chatWord);

		Assert.That(result.HasRejection, Is.False, $"The authority answers '{chatWord}': {result.RejectionReason}");
	}

	[Test]
	public void EveryCheatKindIsReachableFromAWord()
	{
		var reachableKinds = new HashSet<Type>();
		foreach (string word in CheatCatalog.Words)
		{
			Assert.That(CheatCatalog.TryParse(word, out CheatKind? kind), Is.True, $"The catalog states '{word}'.");
			reachableKinds.Add(kind!.GetType());
		}

		Type[] declaredKinds = typeof(CheatKind)
			.GetNestedTypes(BindingFlags.Public)
			.Where(type => type.IsSubclassOf(typeof(CheatKind)))
			.ToArray();

		foreach (Type declaredKind in declaredKinds)
		{
			var declaration = (CheatKind)Activator.CreateInstance(declaredKind)!;
			Assert.That(declaration.Result, Is.Not.EqualTo(CheatResult.None), $"{declaredKind.Name} states the notice it shows.");
		}

		Assert.That(reachableKinds, Is.EquivalentTo(declaredKinds), "Every cheat the build knows is reachable from a typed word.");
	}

	[Test]
	public void TheAuthorityRefusesAWordThatIsNotACheat()
	{
		using MeleeMatch match = StartCheats();

		ServerCommandResult result = match.SubmitCheat(1, "notacheat");

		Assert.That(result.HasRejection, Is.True, "A line that names no cheat is refused.");
		Assert.That(result.RejectionReason, Does.Contain("not a cheat"), result.RejectionReason);
	}

	[Test]
	public void AMatchThatDoesNotAllowCheatsRefusesTheLine()
	{
		MeleeTestMaps.RequireMaps();
		using MeleeMatch match = MeleeMatch.StartDuel();

		ServerCommandResult result = match.SubmitCheat(1, "aura");

		Assert.That(result.HasRejection, Is.True, "A match that did not enable cheats refuses the line.");
		Assert.That(result.RejectionReason, Does.Contain("does not allow cheats"), result.RejectionReason);
	}

	[Test]
	public void SecureTheBagFillsTheCallersResources()
	{
		using MeleeMatch match = StartCheats();

		match.SubmitCheat(1, "securethebag");

		Assert.That(match.Gold(1), Is.EqualTo(9999f), "The cheat fills the caller's gold.");
		Assert.That(match.Wood(1), Is.EqualTo(9999f), "The cheat fills the caller's wood.");
		Assert.That(match.Gold(2), Is.LessThan(9999f), "Another seat's purse is not filled.");
	}

	[Test]
	public void GigachadEmpowersOnlyTheCallersUnits()
	{
		using MeleeMatch match = StartCheats();
		UnitId hero = match.Hero(1);
		UnitId enemyHero = match.Hero(2);
		float enemyHealth = match.MaxHealthOf(enemyHero);

		match.SubmitCheat(1, "gigachad");

		Assert.That(match.HealthOf(hero), Is.EqualTo(9000f), "The caller's hero is raised to the cheat's health.");
		Assert.That(match.AttackDamageOf(hero), Is.EqualTo(9001f), "The caller's hero is given the cheat's attack.");
		Assert.That(match.MaxHealthOf(enemyHero), Is.EqualTo(enemyHealth), "Another seat's units are not touched.");
	}

	[Test]
	public void NeedForSpeedMovesOnlyTheSelectedUnits()
	{
		using MeleeMatch match = StartCheats();
		UnitId hero = match.Hero(1);
		UnitId enemyHero = match.Hero(2);
		float enemySpeed = match.SpeedOf(enemyHero);

		match.SubmitCheat(1, "needforspeed", hero);

		Assert.That(match.SpeedOf(hero), Is.EqualTo(25f), "The selected unit is given the cheat's speed.");
		Assert.That(match.SpeedOf(enemyHero), Is.EqualTo(enemySpeed), "An unselected unit keeps its authored speed.");
	}

	[Test]
	public void ThanosSnapKillsOnlyTheAiSeat()
	{
		using MeleeMatch match = StartCheats();
		match.World.Set(match.PlayerEntity(2), new Name("Enemy_AI"));

		UnitId ownHero = match.Hero(1);
		UnitId enemyHero = match.Hero(2);

		match.SubmitCheat(1, "thanossnap");

		Assert.That(match.IsAlive(enemyHero), Is.False, "The AI seat loses its units.");
		Assert.That(match.IsAlive(ownHero), Is.True, "The caller keeps its own units.");
	}

	[Test]
	public void CookedKillsOnlyTheCallersSelectedUnits()
	{
		using MeleeMatch match = StartCheats();
		UnitId ownHero = match.Hero(1);
		UnitId enemyHero = match.Hero(2);

		match.SubmitCheat(1, "cooked", ownHero, enemyHero);

		Assert.That(match.IsAlive(ownHero), Is.False, "The selected unit is cooked through the damage pipeline.");
		Assert.That(match.IsAlive(enemyHero), Is.True, "A cheat cannot reach another seat's unit.");
	}

	[Test]
	public void EzClapEndsTheMatch()
	{
		using MeleeMatch match = StartCheats();
		Assert.That(match.IsGameOver, Is.False, "The match is running before the cheat.");

		match.SubmitCheat(1, "ezclap");

		Assert.That(match.IsGameOver, Is.True, "The cheat ends the match.");
	}

	[Test]
	public void AuraRevealsTheBoard()
	{
		var options = new MatchOptionSet();
		options.Set(MatchOption.ShroudMode, (int)MatchShroudMode.ExplorationShroud);
		using MeleeMatch match = StartCheats(options);

		Assert.That(match.ShroudDisabled, Is.False, "Shroud is on before the cheat.");

		match.SubmitCheat(1, "aura");

		Assert.That(match.ShroudDisabled, Is.True, "The cheat stops shroud hiding the board.");
	}

	[Test]
	public void SpeedrunTogglesFastBuild()
	{
		using MeleeMatch match = StartCheats();
		Assert.That(match.FastBuildEnabled, Is.False, "Fast build is off before the cheat.");

		match.SubmitCheat(1, "speedrun");
		Assert.That(match.FastBuildEnabled, Is.True, "The first line turns fast build on.");

		match.SubmitCheat(1, "speedrun");
		Assert.That(match.FastBuildEnabled, Is.False, "The second line turns it back off.");
	}

	[Test]
	public void SkibidiTogglesUnlimitedPower()
	{
		using MeleeMatch match = StartCheats();
		Assert.That(match.UnlimitedPowerEnabled, Is.False, "Unlimited power is off before the cheat.");

		match.SubmitCheat(1, "skibidi");
		Assert.That(match.UnlimitedPowerEnabled, Is.True, "The first line turns unlimited power on.");

		match.SubmitCheat(1, "skibidi");
		Assert.That(match.UnlimitedPowerEnabled, Is.False, "The second line turns it back off.");
	}
}
