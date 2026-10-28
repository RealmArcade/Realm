using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Replication;
using Realm.ListenServer;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.ReplicationVerifier;

/// <summary>
///     Runs a real map against a real authority for a few simulated seconds and reports what the match did: whether the
///     map's own script created units, whether a construction site a worker was sent to finishes, whether the events a
///     map subscribes to are raised, and whether the description of the world carries the content a peer needs to name
///     an ability list. It is the check a unit test cannot make, because what is under test is a compiled map.
/// </summary>
public static class MapSmokeRun
{
	public static int Run(string mapPath, int seconds)
	{
		int failures = 0;
		int checks = 0;

		void Check(bool condition, string description)
		{
			checks++;
			if (condition) return;

			failures++;
			Console.WriteLine($"FAIL: {description}");
		}

		Console.WriteLine($"map smoke: '{mapPath}' for {seconds}s");

		string logPath = Path.Combine(Path.GetTempPath(), $"realm-map-smoke-{Environment.ProcessId}.log");
		using var log = new ServerLog(logPath);
		LoadedMap map = MapWorldLoader.Load(mapPath);
		foreach (string warning in map.Warnings) Console.WriteLine($"map warning: {warning}");

		var paths = ServerMatchPaths.ForMap(map, mapPath, Path.Combine(Path.GetTempPath(), "realm-map-smoke-data"), Path.GetDirectoryName(logPath) ?? string.Empty);
		using var simulation = new ServerSimulation(map, paths, log, 1, cheatsEnabled: true);

		ServerGameAPI api = simulation.ScriptApi;
		var created = new List<int>();
		var died = new List<int>();
		var built = new List<int>();
		var spawnOrigins = new Dictionary<Entity, Vector3>();
		api.OnUnitCreated += unit =>
		{
			created.Add(unit.Value);
			if (map.World.TryGet<Position>(new Entity(unit.Value), out Position position)) spawnOrigins[new Entity(unit.Value)] = position.Value;
		};
		api.OnUnitDied += (unit, killer) => died.Add(unit.Value);
		api.OnConstructionFinished += unit => built.Add(unit.Value);

		Console.WriteLine($"script: {(ServerMapScript.TryFindScript(mapPath, out string wasmPath) ? wasmPath : "none")}");
		Console.WriteLine($"catalog: {map.Catalog.Units.Count} unit(s), {map.Catalog.Buildings.Count} building(s), map reports {map.UnitCount} placed unit(s) and {map.PropCount} prop(s)");
		int unitsAfterLoad = CountUnits(map.World);
		Check(unitsAfterLoad > 0, "the map brought units into the world");

		simulation.TryAssignPlayerSlot(1, out int playerSlot, out _);
		Check(playerSlot == 1, "the first peer is seated on player slot 1");

		for (int second = 0; second < seconds; second++)
		{
			for (int tick = 0; tick < 20; tick++) simulation.Step();
		}

		WorldSnapshot baseline = simulation.BuildSnapshotForPeer(1, simulation.AdvanceSnapshotSequence(), forceBaseline: true);
		Check(baseline.Units.Count > 0, "the description of the world names the units the player can see");
		Check(baseline.Catalog.Count > 0, "the description carries the text a peer needs to name content");

		int abilityListPayloads = 0;
		for (int index = 0; index < baseline.ColdPayloads.Count; index++)
		{
			if ((ColdPayloadRole)baseline.ColdPayloads[index].Role == ColdPayloadRole.AbilityList) abilityListPayloads++;
		}

		int unitsWithNamedAbilities = 0;
		foreach (Entity entity in map.World.Query<AbilityState>())
		{
			ref AbilityState state = ref map.World.Get<AbilityState>(entity);
			if (state.Count == 0 || state.Get(0) == null) continue;

			unitsWithNamedAbilities++;
		}

		Console.WriteLine($"ability lists: {abilityListPayloads} payload(s) described, {unitsWithNamedAbilities} unit(s) with a named ability");
		Check(unitsWithNamedAbilities > 0, "the authority can name the abilities a unit offers");
		Check(abilityListPayloads > 0, "the description carries the payload behind an ability list");

		int namedAbilityChanges = 0;
		for (int index = 0; index < baseline.AbilityStates.Count; index++)
		{
			if (!string.IsNullOrEmpty(baseline.AbilityStates[index].AbilityId)) namedAbilityChanges++;
		}

		Console.WriteLine($"ability state: {baseline.AbilityStates.Count} change(s), {namedAbilityChanges} about an ability their unit does not list");
		Check(namedAbilityChanges > 0, "the map can withhold an option its unit does not list among its own abilities");

		int registeredCards = 0;
		int describedLabels = 0;
		ServerPresentationEvent[] joinDescription = simulation.DescribePresentationStateFor(1);
		for (int index = 0; index < joinDescription.Length; index++)
		{
			switch (joinDescription[index].Kind)
			{
				case PresentationKind.AbilityRegistered:
				case PresentationKind.PlayerAbilityRegistered:
					registeredCards++;
					break;
				case PresentationKind.StaticTextCreated:
					describedLabels++;
					break;
			}
		}

		Console.WriteLine($"join description: {joinDescription.Length} request(s), {registeredCards} ability card(s), {describedLabels} label(s)");
		Check(registeredCards > 0, "a peer joining is told the ability cards the map registered before it arrived");
		Check(describedLabels > 0, "a peer joining is told the labels the map put over what it created");

		string consoleAnswer = TryConsoleCommand(simulation, "-addai 2");
		Console.WriteLine($"console: '-addai 2' answered with '{consoleAnswer}'");
		Check(consoleAnswer.Contains("AI", StringComparison.OrdinalIgnoreCase), "a console command reaches the map and is answered");

		bool constructionFinished = FinishConstructionSite(map, simulation, api, built, out string constructionReport);
		Console.WriteLine($"construction: {constructionReport}");
		Check(constructionFinished, "a worker finished the site it was sent to build");
		Check(built.Count > 0, "the map was told a construction site finished");

		Check(PlayOutTheDraft(map, simulation, seconds, spawnOrigins), "the units a wave fields walk to where the map ordered them");

		Console.WriteLine($"units created after setup: {created.Count}, died: {died.Count}, sites finished: {built.Count}");

		int aliveUnits = CountUnits(map.World);
		Console.WriteLine($"alive units at the end: {aliveUnits} (was {unitsAfterLoad} after load)");
		Check(aliveUnits > 0, "the match still holds units after running");

		Console.WriteLine($"map smoke: {checks - failures}/{checks} checks passed");
		return failures > 0 ? 1 : 0;
	}

	private static int CountUnits(EcsWorld world)
	{
		int count = 0;
		foreach (Entity entity in world.Query<Position, UnitOwnerPlayer>())
		{
			if (!world.IsAlive(entity)) continue;

			count++;
		}

		return count;
	}

	/// <summary>
	///     Puts a worker on a site the way an order does, runs the match until the site finishes and reports what the
	///     world and the map were told. A site that never finishes is what a match without a construction tick looks
	///     like, so this is the check that a worker's work reaches anything at all.
	/// </summary>
	private static bool FinishConstructionSite(LoadedMap map, ServerSimulation simulation, ServerGameAPI api, List<int> built, out string report)
	{
		report = string.Empty;

		string templateId = string.Empty;
		foreach (var entry in map.Catalog.Units)
		{
			if (entry.Value == null) continue;

			templateId = entry.Key;
			break;
		}

		if (templateId.Length == 0)
		{
			foreach (var entry in map.Catalog.Buildings)
			{
				if (entry.Value == null) continue;

				templateId = entry.Key;
				break;
			}
		}

		if (templateId.Length == 0)
		{
			report = "the map declares no template to build";
			return false;
		}

		Entity worker = Entity.Null;
		int workerPlayerIndex = -1;
		foreach (Entity entity in map.World.Query<Position, UnitOwnerPlayer>())
		{
			if (map.World.Has<Building>(entity)) continue;
			if (!map.World.TryGet<UnitOwnerPlayer>(entity, out UnitOwnerPlayer owner)) continue;

			worker = entity;
			workerPlayerIndex = owner.PlayerIndex;
			break;
		}

		if (worker == Entity.Null || workerPlayerIndex < 0)
		{
			report = "the map has no unit to hand the site to";
			return false;
		}

		// A site costs resources, so the player is given enough of each to pay for the one the smoke run places.
		api.SetPlayerGold(workerPlayerIndex, 100000f);
		api.SetPlayerWood(workerPlayerIndex, 100000f);
		api.SetPlayerStone(workerPlayerIndex, 100000f);

		PlayerEntity playerEntity = map.GetPlayerEntity(workerPlayerIndex);
		Vector3 position = map.World.Get<Position>(worker).Value + new Vector3(6f, 0f, 0f);

		if (!BuildingPlacementService.TryPlace(map.World, map.Catalog, templateId, position, playerEntity, worker, out Entity site))
		{
			report = $"'{templateId}' could not be placed";
			return false;
		}

		int builtBefore = built.Count;

		for (int tick = 0; tick < 20 * 120; tick++)
		{
			simulation.Step();
			if (!map.World.Has<ConstructionState>(site)) break;
		}

		if (!map.World.Has<ConstructionState>(site))
		{
			report = $"'{templateId}' finished and the map was {(built.Count > builtBefore ? "told about it" : "not told about it")}";
			return true;
		}

		report = $"'{templateId}' is still under construction after 120 simulated seconds (progress {map.World.Get<ConstructionState>(site).Progress:F2} of {map.World.Get<ConstructionState>(site).TotalBuildTime:F2})";
		return false;
	}

	/// <summary>
	///     Leaves the seated computer player to draft and field a wave, and reports what the match produced: the units the
	///     authority created for a clone wave, and the labels the map drew over the heroes it drafted. A map whose draft
	///     never finishes fields nothing, which is what a match whose construction sites never complete looks like.
	/// </summary>
	private static bool PlayOutTheDraft(LoadedMap map, ServerSimulation simulation, int seconds, Dictionary<Entity, Vector3> spawnOrigins)
	{
		int createdBefore = CountUnits(map.World);
		for (int second = 0; second < seconds; second++)
		{
			for (int tick = 0; tick < 20; tick++) simulation.Step();
		}

		int createdAfter = CountUnits(map.World);
		int proxyOwned = 0;

		foreach (Entity entity in map.World.Query<Position, UnitOwnerPlayer>())
		{
			if (!map.World.TryGet<UnitOwnerPlayer>(entity, out UnitOwnerPlayer owner)) continue;
			if (owner.PlayerIndex >= ProxySlotOffset) proxyOwned++;
		}

		int walkers = 0;
		foreach (KeyValuePair<Entity, Vector3> origin in spawnOrigins)
		{
			if (!map.World.IsAlive(origin.Key)) continue;
			if (!map.World.TryGet<Position>(origin.Key, out Position position)) continue;
			if (Vector3.Distance(origin.Value, position.Value) > 2f) walkers++;
		}

		int labelledHeroes = 0;
		ServerPresentationEvent[] presentations = simulation.CollectPresentation();
		for (int index = 0; index < presentations.Length; index++)
		{
			if (presentations[index].Kind == PresentationKind.StaticTextCreated) labelledHeroes++;
		}

		Console.WriteLine($"draft: {createdAfter - createdBefore} unit(s) appeared, {proxyOwned} owned by a clone seat, {walkers} of the {spawnOrigins.Count} new unit(s) walked off their spawn, {labelledHeroes} label(s) drawn");
		return walkers > 0;
	}

	/// <summary>The first player slot the map uses for the units it fields on a player's behalf.</summary>
	private const int ProxySlotOffset = 12;

	/// <summary>Types a line into the map's console the way a peer does, and reports what the map said back.</summary>
	private static string TryConsoleCommand(ServerSimulation simulation, string command)
	{
		simulation.ApplyConsoleCommand(1, new ScriptConsoleCommand { Command = command, SelectedUnitEntityId = 0 });

		ServerPresentationEvent[] events = simulation.CollectPresentation();
		for (int index = 0; index < events.Length; index++)
		{
			string text = events[index].Text;
			if (string.IsNullOrEmpty(text)) continue;
			if (!text.Contains("AI", StringComparison.OrdinalIgnoreCase)) continue;

			return text;
		}

		return events.Length == 0 ? "nothing" : $"{events.Length} presentation(s), none about the command";
	}
}
