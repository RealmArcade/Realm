using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;

namespace Realm.Simulation.Tests;

/// <summary>
///     Holds the identifiers a map script reads through shared memory to the identifiers the canonical registry assigns,
///     which is the contract a tag read depends on.
/// </summary>
[TestFixture]
public class SharedComponentIdContractTests
{
	[Test]
	public void EverySharedComponentCarriesItsDeclaredIdentifier()
	{
		var mismatched = new List<string>();

		void Verify<T>(string name, int declaredId)
		{
			int registeredId = ComponentType<T>.Id;
			if (registeredId != declaredId) mismatched.Add($"{name}: declared {declaredId}, registered {registeredId}");
		}

		Verify<Position>("Position", ComponentIds.Position);
		Verify<PreviousPosition>("PreviousPosition", ComponentIds.PreviousPosition);
		Verify<Health>("Health", ComponentIds.Health);
		Verify<Mana>("Mana", ComponentIds.Mana);
		Verify<PlayerPopulation>("PlayerPopulation", ComponentIds.PlayerPopulation);
		Verify<ScriptPlayer>("ScriptPlayer", ComponentIds.ScriptPlayer);
		Verify<UnitOwnerPlayer>("UnitOwnerPlayer", ComponentIds.UnitOwnerPlayer);
		Verify<Attack>("Attack", ComponentIds.Attack);
		Verify<Resistances>("Resistances", ComponentIds.Resistances);
		Verify<MovementStats>("MovementStats", ComponentIds.MovementStats);
		Verify<PlayerResources>("PlayerResources", ComponentIds.PlayerResources);
		Verify<Building>("Building", ComponentIds.Building);
		Verify<Dead>("Dead", ComponentIds.Dead);
		Verify<Hero>("Hero", ComponentIds.Hero);
		Verify<Invulnerable>("Invulnerable", ComponentIds.Invulnerable);
		Verify<Unit>("Unit", ComponentIds.Unit);
		Verify<Experience>("Experience", ComponentIds.Experience);
		Verify<Level>("Level", ComponentIds.Level);
		Verify<ModelScale>("ModelScale", ComponentIds.ModelScale);
		Verify<RotationY>("RotationY", ComponentIds.RotationY);
		Verify<PreviousRotationY>("PreviousRotationY", ComponentIds.PreviousRotationY);
		Verify<CommandsFrozen>("CommandsFrozen", ComponentIds.CommandsFrozen);
		Verify<AnimationsFrozen>("AnimationsFrozen", ComponentIds.AnimationsFrozen);
		Verify<OverheadBarsHidden>("OverheadBarsHidden", ComponentIds.OverheadBarsHidden);

		Assert.That(mismatched, Is.Empty, "A map script reads these components from shared memory by the declared identifier, so the registry has to assign the same one.");
	}
}
