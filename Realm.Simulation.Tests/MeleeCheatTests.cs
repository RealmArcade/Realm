using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Realm.Shared.Simulation;

namespace Realm.Simulation.Tests;

/// <summary>
///     Pins the words a player types into chat. The authority is the only thing that decides whether a typed line does
///     something, so every word the client offers has to be one this build accepts, and a line that names nothing has to
///     be refused rather than silently ignored.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeCheatTests
{
	private static IEnumerable<string> ChatWords => CheatCatalog.Words;

	[TestCaseSource(nameof(ChatWords))]
	public void TheAuthorityAcceptsTheCheatWord(string chatWord)
	{
		MeleeTestMaps.RequireMaps();
		using MeleeMatch match = MeleeMatch.StartDuel(cheatsEnabled: true);

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
		MeleeTestMaps.RequireMaps();
		using MeleeMatch match = MeleeMatch.StartDuel(cheatsEnabled: true);

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
}
