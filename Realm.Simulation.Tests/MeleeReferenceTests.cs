using System.Numerics;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Realm.Ecs;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Terrain;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Simulation.Tests;

/// <summary>
///     Plays the Secluded Crest board composed with the melee ruleset the way a save composes it: the terrain, props and
///     models this map owns, with the rules, units and relics the referenced Melee Ruleset contributes.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MeleeReferenceTests
{
	private const string NexusTemplateId = "building/nexus";
	private const int PlayedTicks = 1500;

	private static readonly string[] FilesBothMapsCarry =
	{
		".gitignore", "AGENTS.md", "Directory.Build.targets", "LICENSE.md", "MapScript.csproj", "NuGet.config"
	};

	private ComposedMeleeMap? _map;

	[OneTimeSetUp]
	public void OneTimeSetUp()
	{
		MeleeTestMaps.RequireMaps();
		_map = ComposedMeleeMap.Assemble();
	}

	[OneTimeTearDown]
	public void OneTimeTearDown()
	{
		_map?.Dispose();
	}

	private ComposedMeleeMap Map => _map!;

	[Test]
	public void TheBoardThisMapOwnsStaysItsOwn()
	{
		foreach (string relativePath in new[] { ComposedMeleeMap.TerrainFileName, ComposedMeleeMap.HeightsFileName })
		{
			Assert.That(Map.HasFile(relativePath), Is.True, $"'{relativePath}' is this map's own board and has to stay in place.");
			Assert.That(
				File.ReadAllBytes(Map.AbsolutePathOf(relativePath)),
				Is.EqualTo(File.ReadAllBytes(ComposedMeleeMap.ParentPathOf(relativePath))),
				$"The composed board has to be this map's own '{relativePath}'.");
			Assert.That(
				File.GetAttributes(Map.AbsolutePathOf(relativePath)).HasFlag(FileAttributes.ReadOnly),
				Is.False,
				$"'{relativePath}' is this map's own file, so it stays editable.");
		}
	}

	[Test]
	public void TheReferencedMapContributesTheRulesAndEveryFileOnlyItCarries()
	{
		Assert.That(Map.HasFile(ComposedMeleeMap.RulesScriptFileName), Is.True, "The board keeps an entry script of its own, which starts the rulesets it composes.");
		Assert.That(
			File.ReadAllBytes(Map.AbsolutePathOf(ComposedMeleeMap.RulesScriptFileName)),
			Is.EqualTo(File.ReadAllBytes(ComposedMeleeMap.ParentPathOf(ComposedMeleeMap.RulesScriptFileName))),
			"The entry script this board authors is its own orchestrator, so the save leaves it exactly as the author wrote it.");

		Assert.That(Map.HasFile(ComposedMeleeMap.ComposedRulesScriptFileName), Is.True, "The rules the referenced map holds have to arrive, or the board plays by no rules at all.");

		string sourceText = File.ReadAllText(ComposedMeleeMap.ReferencedPathOf(ComposedMeleeMap.RulesScriptFileName));
		string composedText = File.ReadAllText(Map.AbsolutePathOf(ComposedMeleeMap.ComposedRulesScriptFileName));
		Assert.That(composedText, Does.Contain($"namespace {ComposedMeleeMap.ComposedRulesNamespace}"), "The composed rules keep a namespace of their own, so the board and the ruleset both declaring MapScript still compile.");
		Assert.That(
			NormalizeAccessibility(composedText),
			Is.EqualTo(NormalizeAccessibility(sourceText.Replace("namespace Realm.Maps;", $"namespace {ComposedMeleeMap.ComposedRulesNamespace};").Replace("\r\n", "\n"))),
			"The composed rules are the ruleset's own script with its namespace moved and its accessibility widened, not rewritten in any other way.");
		Assert.That(
			DeclaresMemberAccessibility(composedText, "private"),
			Is.False,
			"A private member would stay out of reach of the map that composed it, so every one of them is widened.");
		Assert.That(
			composedText.Contains(" sealed ", StringComparison.Ordinal),
			Is.False,
			"A sealed type or member could not be overridden by the map that composed it.");
		Assert.That(
			Map.HasFile(ComposedMeleeMap.ComposedRulesAffinityFileName),
			Is.True,
			"Every source of the ruleset composes, including the partial files it spreads its rules across, so a rule can live in a file of its own.");

		int carriedFromReferenced = 0;
		foreach (string relativePath in MapReferenceResolver.EnumeratePackageFiles(ComposedMeleeMap.ReferencedFolder))
		{
			if (relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

			carriedFromReferenced++;
			Assert.That(
				Map.HasFile(relativePath),
				Is.True,
				$"'{relativePath}' belongs to the referenced map's package, so the composition has to carry it: a map is a package, not a list of files to pick from.");

			string parentPath = ComposedMeleeMap.ParentPathOf(relativePath);
			if (File.Exists(parentPath)) continue;

			Assert.That(
				File.ReadAllBytes(Map.AbsolutePathOf(relativePath)),
				Is.EqualTo(File.ReadAllBytes(ComposedMeleeMap.ReferencedPathOf(relativePath))),
				$"'{relativePath}' is only the referenced map's, so the save has to take it unchanged.");
		}

		Assert.That(carriedFromReferenced, Is.GreaterThan(0), "The referenced map carries a package, which is what it is referenced for.");
	}

	[Test]
	public void ThisMapsOwnFilesWinEveryCollision()
	{
		foreach (string relativePath in FilesBothMapsCarry)
		{
			Assert.That(Map.HasFile(relativePath), Is.True, $"'{relativePath}' has to stay in the composed folder.");
			Assert.That(
				File.ReadAllBytes(Map.AbsolutePathOf(relativePath)),
				Is.EqualTo(File.ReadAllBytes(ComposedMeleeMap.ParentPathOf(relativePath))),
				$"'{relativePath}' is declared by no priority, so the collision keeps this map's own copy.");
			Assert.That(
				File.GetAttributes(Map.AbsolutePathOf(relativePath)).HasFlag(FileAttributes.ReadOnly),
				Is.False,
				$"'{relativePath}' is this map's own file, so it stays editable.");
		}

		Assert.That(
			Map.FirstPass.ReplacedFiles,
			Is.Empty,
			"Nothing this map authored is overwritten: the only file both maps author is the script, and scripts are composed rather than replaced.");
		Assert.That(Map.FirstPass.ConflictingFiles, Is.Empty, "Every colliding file is settled, either by the default rule or by a stated priority.");
	}

	[Test]
	public void TheComposedMetadataHoldsBothMapsUnderThisBoardsIdentity()
	{
		string partialPath = ComposedMeleeMap.ParentPathOf(MapMetadataMerger.MetadataPartialFileName);
		string referencedMetadataPath = ComposedMeleeMap.ReferencedPathOf("metadata.json");

		var boardMetadata = JsonNode.Parse(File.ReadAllText(partialPath))!;
		var referencedMetadata = JsonNode.Parse(File.ReadAllText(referencedMetadataPath))!;

		List<string> boardBuildings = ComposedMeleeMap.TemplateIdsOf(partialPath, "Buildings").ToList();
		Assert.That(boardBuildings, Is.Not.Empty, "The board states the buildings it places itself, which is what makes the next assertion mean something.");

		var boardUnits = new HashSet<string>(ComposedMeleeMap.TemplateIdsOf(partialPath, "Units"));
		string referencedOnlyUnit = ComposedMeleeMap.TemplateIdsOf(referencedMetadataPath, "Units")
			.First(identifier => !boardUnits.Contains(identifier));

		var merged = Map.ReadMetadata();
		var mergedBuildings = new HashSet<string>(ComposedMeleeMap.TemplateIdsOf(Map.AbsolutePathOf("metadata.json"), "Buildings"));
		var mergedUnits = new HashSet<string>(ComposedMeleeMap.TemplateIdsOf(Map.AbsolutePathOf("metadata.json"), "Units"));

		foreach (string identifier in boardBuildings)
		{
			Assert.That(mergedBuildings, Does.Contain(identifier), $"This board's own building '{identifier}' has to survive the merge.");
		}

		Assert.That(mergedUnits, Does.Contain(referencedOnlyUnit), "The referenced map's units have to be merged in, or its rules name nobody.");
		Assert.That(merged["Models"]?.AsObject().Count ?? 0, Is.GreaterThan(0), "This board's model table has to stay, because the referenced map authors none.");

		Assert.That(
			merged["MapProperties"]?["MapName"]?.GetValue<string>(),
			Is.EqualTo(boardMetadata["MapProperties"]?["MapName"]?.GetValue<string>()),
			"The map identity stays this board's.");
		Assert.That(
			merged["MapProperties"]?["ShroudType"]?.GetValue<string>(),
			Is.EqualTo(boardMetadata["MapProperties"]?["ShroudType"]?.GetValue<string>()),
			"The terrain states how it is seen, so its property stands over the referenced map's.");

		string? boardBuild = boardMetadata["GameBuildNumber"]?.GetValue<string>();
		string? rulesetBuild = referencedMetadata["GameBuildNumber"]?.GetValue<string>();
		Assert.That(
			merged["GameBuildNumber"]?.GetValue<string>(),
			Is.EqualTo(boardBuild ?? rulesetBuild),
			"A build number this board states stands over the ruleset's, and one it leaves unstated arrives from the ruleset.");

		string? referencedDescription = referencedMetadata["MapProperties"]?["MapDescription"]?.GetValue<string>();
		Assert.That(boardMetadata["MapProperties"]?["MapDescription"], Is.Null, "This board leaves its description unstated, which is what makes the next assertion mean something.");
		Assert.That(referencedDescription, Is.Not.Null.And.Not.Empty, "The ruleset describes the game it plays.");
		Assert.That(
			merged["MapProperties"]?["MapDescription"]?.GetValue<string>(),
			Is.EqualTo(referencedDescription),
			"A property this board leaves null states nothing, so the referenced map's value arrives instead of being erased.");
	}

	[Test]
	public void TheBoardHoldsTheInstancesAndTheRulesHoldTheTemplates()
	{
		var board = JsonNode.Parse(File.ReadAllText(ComposedMeleeMap.ParentPathOf(ComposedMeleeMap.TerrainFileName)))!;

		Assert.That(
			board["SpawnAnchors"]?.AsArray().Count ?? 0,
			Is.EqualTo(4),
			"The board states where its four seats begin, because a position on the board is the board's to place.");

		int placedNodes = 0;
		foreach (JsonNode? prop in board["Props"]?.AsArray() ?? new JsonArray())
		{
			string? templateId = prop?["TemplateId"]?.GetValue<string>();
			if (templateId == null || !templateId.StartsWith("resource/melee_", StringComparison.Ordinal)) continue;
			placedNodes++;
		}

		Assert.That(placedNodes, Is.EqualTo(12), "The board places the twelve resource nodes the rules gather from.");
		Assert.That(
			File.Exists(ComposedMeleeMap.ReferencedPathOf("terrain.json")),
			Is.False,
			"The ruleset holds no board of its own, so the board it is composed into is the only placement there is.");

		string referencedMetadataPath = ComposedMeleeMap.ReferencedPathOf("metadata.json");
		Assert.That(
			ComposedMeleeMap.TemplateIdsOf(referencedMetadataPath, "Resources"),
			Does.Contain("resource/melee_gold_mine").And.Contain("resource/melee_woodland"),
			"The ruleset is where the node templates live, which is what makes a board able to place them.");
	}

	[Test]
	public void ThePartialMetadataHoldsOnlyThisMapsOverrides()
	{
		Assert.That(Map.HasFile(MapMetadataMerger.MetadataPartialFileName), Is.True, "The save has to record what this map overrides, or a referenced map's upgrade cannot be separated from it.");

		string parentMetadataPath = ComposedMeleeMap.ParentPathOf("metadata.json");
		var parentMetadata = JsonNode.Parse(File.ReadAllText(parentMetadataPath))!;
		var partial = Map.ReadMetadataPartial();

		Assert.That(
			partial["MapProperties"]?["MapName"]?.GetValue<string>(),
			Is.EqualTo(parentMetadata["MapProperties"]?["MapName"]?.GetValue<string>()),
			"The name this board states is an override of the referenced map's.");
		Assert.That(
			partial["MapProperties"]?["MapDescription"],
			Is.Null,
			"This board states no description, so recording one would freeze the referenced map's value at the version the reference was taken from.");

		var referenced = JsonNode.Parse(File.ReadAllText(ComposedMeleeMap.ReferencedPathOf("metadata.json")));
		var partialObject = partial.AsObject();
		foreach (KeyValuePair<string, JsonNode?> property in referenced.AsObject())
		{
			if (!partialObject.ContainsKey(property.Key)) continue;

			Assert.That(
				JsonNode.DeepEquals(partial[property.Key], property.Value),
				Is.False,
				$"'{property.Key}' holds the referenced map's own value, so recording it as this map's override would freeze that value.");
		}
	}

	[Test]
	public void SavingAgainWritesNothing()
	{
		Assert.That(Map.SecondPass.CopiedFiles, Is.Empty, "A save straight after the first has no referenced file left to write.");
		Assert.That(Map.SecondPass.ReplacedFiles, Is.Empty, "A second save must not overwrite a file the first one already took.");

		var merged = Map.ReadMetadata();
		MapReferenceResolutionResult third = MapReferenceResolver.Apply(Map.Folder);
		Assert.That(third.Success, Is.True, third.Message);
		Assert.That(JsonNode.DeepEquals(Map.ReadMetadata(), merged), Is.True, "Merging is stable, so a save cannot drift the merged metadata.");
	}

	[Test]
	public void FilesTakenFromTheReferencedMapAreReadOnly()
	{
		string composedDirectory = Map.AbsolutePathOf(ComposedMeleeMap.ComposedRulesFolder);
		string[] composedFiles = Directory.GetFiles(composedDirectory, "*", SearchOption.AllDirectories);
		Assert.That(composedFiles, Is.Not.Empty, "The referenced map has to contribute its sources for this to say anything.");

		foreach (string filePath in composedFiles)
		{
			Assert.That(
				File.GetAttributes(filePath).HasFlag(FileAttributes.ReadOnly),
				Is.True,
				$"'{Path.GetFileName(filePath)}' is generated from the referenced map's code, so it is read-only and an edit to it would be reverted by the next save.");
		}

		foreach (string relativePath in Map.FirstPass.CopiedFiles)
		{
			Assert.That(
				File.GetAttributes(Map.AbsolutePathOf(relativePath)).HasFlag(FileAttributes.ReadOnly),
				Is.True,
				$"'{relativePath}' is copied from the referenced map, so it is read-only as well.");
		}
	}

	[Test]
	public void TheComposedManifestNamesEveryFileItCarries()
	{
		MapManifest manifest = Map.Manifest;

		Assert.That(manifest.Files.Keys, Does.Contain(ComposedMeleeMap.TerrainFileName), "The board has to be downloadable, which means the manifest names it.");
		Assert.That(manifest.Files.Keys, Does.Contain(ComposedMeleeMap.HeightsFileName), "The height field beside the terrain has to be named with it, or the board cannot be baked.");
		Assert.That(manifest.Files.Keys, Does.Contain(ComposedMeleeMap.RulesScriptFileName), "The rules script has to be named, or a download leaves the map without one.");
		Assert.That(manifest.Files.Keys, Does.Contain("metadata.json"), "The merged rules have to be named, or the map downloads with no templates.");
		Assert.That(manifest.Files.Keys, Does.Contain($"bin/script/{Path.GetFileName(Map.ScriptPath)}"), "The one script the map runs has to be named.");
		Assert.That(manifest.Files.Keys, Does.Contain(MapReferences.FileName), "The references travel inside the package, so another author can extend the map it was built from.");
		Assert.That(manifest.Files.Keys, Does.Not.Contain(MapMetadataMerger.MetadataPartialFileName), "The override file is the author's, not content a player needs.");

		var listedHashes = new HashSet<string>(manifest.Files.Values.Select(ContentAddressableStorage.NormalizeBlake3Hash));
		foreach (string filePath in Directory.GetFiles(Map.Folder, "*", SearchOption.AllDirectories))
		{
			string relativePath = Path.GetRelativePath(Map.Folder, filePath).Replace('\\', '/');
			if (string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
			if (string.Equals(relativePath, MapMetadataMerger.MetadataPartialFileName, StringComparison.OrdinalIgnoreCase)) continue;
			if (MapManifest.IsGeneratedWorkspaceFile(relativePath)) continue;
			if (relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)) continue;
			if (relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) && !relativePath.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase)) continue;
			if (relativePath.EndsWith(".elf", StringComparison.OrdinalIgnoreCase)) continue;

			string extension = Path.GetExtension(filePath).ToLowerInvariant();
			string hash = ContentAddressableStorage.NormalizeBlake3Hash(RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(filePath), extension));
			Assert.That(listedHashes, Does.Contain(hash), $"The map carries '{relativePath}' without the manifest naming its content, so a download would leave it behind.");
		}
	}

	[Test]
	public void TheComposedMapCarriesExactlyOneScript()
	{
		string[] scripts = Directory.GetFiles(MapFileService.ResolveScriptOutputDirectory(Map.Folder), "*.wasm", SearchOption.AllDirectories);
		Assert.That(scripts, Has.Length.EqualTo(1), "A composed map carries exactly one compiled script.");
		Assert.That(File.ReadAllBytes(scripts[0]), Is.EqualTo(File.ReadAllBytes(Map.ScriptPath)), "The script the map runs is the one compiled in its own folder.");
	}

	[Test]
	public void TheCommittedReferencesFilePointsAtTheRulesMap()
	{
		MapReferences references = MapReferences.Load(ComposedMeleeMap.ParentFolder);
		Assert.That(references, Is.Not.Null, "The board has to keep the references it is composed from.");

		var validation = references!.Validate();
		Assert.That(validation.IsValid, Is.True, validation.ErrorMessage);
		Assert.That(references.References, Has.Count.EqualTo(1), "The board references exactly one map.");

		MapReference reference = references.References[0];
		Assert.That(reference.MapName, Is.EqualTo(ComposedMeleeMap.ReferencedMapName), "The reference names the rules map.");
		Assert.That(reference.ManifestBlake3, Is.EqualTo(ComposedMeleeMap.ReferencedManifestBlake3), "The reference is pinned to the rules map's current manifest.");

		Assert.That(
			references.TryGetConflictPriority(ComposedMeleeMap.RulesScriptFileName, out _),
			Is.False,
			"Both maps author a script, and a script is composed rather than kept from one of them, so no priority has to settle it.");
		Assert.That(
			references.TryGetConflictPriority(ComposedMeleeMap.TerrainFileName, out _),
			Is.False,
			"The board is the only map that authors terrain, so nothing has to be settled for it either.");
	}

	[Test]
	public void TheComposedMapIsSeatedWithBothMapsContent()
	{
		using MeleeMatch match = MeleeMatch.StartOnMap(Map.Folder, new[] { "Vale", "Sable" }, Map.ScriptPath);

		Assert.That(match.Simulation.Map.Warnings, Is.Empty, "Every template the composed terrain and rules name has to resolve.");

		for (int slot = 1; slot <= match.PlayerCount; slot++)
		{
			Assert.That(match.IsAlive(match.Hero(slot)), Is.True, $"Player {slot} has to be seated with its hero on the composed map.");
			Assert.That(match.IsAlive(match.Nexus(slot)), Is.True, $"Player {slot} has to be seated with its Nexus on the composed map.");
			Assert.That(match.TemplateOf(match.Nexus(slot)), Is.EqualTo(NexusTemplateId), "The Nexus comes from the ruleset, not the terrain map.");

			Vector3 anchor = match.Api.GetSpawnAnchor(slot);
			Vector3 nexus = match.PositionOf(match.Nexus(slot));
			Assert.That(
				Vector2.Distance(new Vector2(anchor.X, anchor.Z), new Vector2(nexus.X, nexus.Z)),
				Is.LessThan(1f),
				$"Player {slot}'s Nexus has to stand at the anchor this board declares, not at a position the ruleset invented.");
		}

		Assert.That(match.Simulation.Map.SaveData.Props?.Count ?? 0, Is.GreaterThan(0), "The composed board has to carry the props the terrain places.");
		Assert.That(match.Simulation.Map.HasNavMesh, Is.True, "The board's height field has to bake into a navmesh the ruleset's units walk on.");
	}

	[Test]
	public void TheComposedMatchPlaysOutOnItsTerrain()
	{
		using MeleeMatch match = MeleeMatch.StartOnMap(Map.Folder, new[] { "Vale", "Sable" }, Map.ScriptPath);

		UnitId hero = match.Hero(1);
		Vector3 startPosition = match.PositionOf(hero);

		match.Step(20f);

		Assert.That(match.IsAlive(hero), Is.True, "The hero has to stand through the opening of a composed match.");
		Assert.That(match.Gold(1), Is.GreaterThan(0f), "The ruleset's own starting economy has to arrive with the composed metadata.");
		int goldNodes = 0;
		int woodNodes = 0;
		foreach (ResourceNodeId node in match.Api.GetResourceNodes())
		{
			string nodeType = match.Api.GetResourceNodeType(node);
			if (nodeType == "gold") goldNodes++;
			if (nodeType == "wood") woodNodes++;
		}

		Assert.That(goldNodes, Is.EqualTo(6), "The ruleset's six gold mines have to be on the composed board.");
		Assert.That(woodNodes, Is.EqualTo(6), "The ruleset's six woodlands have to be on the composed board.");
		Assert.That(match.Simulation.IsGameOver, Is.False, "Nothing ends a composed match in its first twenty seconds.");
		Assert.That(Vector3.Distance(match.PositionOf(hero), startPosition), Is.LessThan(50f), "The hero stays on the terrain it was seated on.");
		Assert.That(match.TryGet(match.Nexus(1), out TerrainState _), Is.False, "The Nexus is not a terrain entity.");
	}

	/// <summary>
	///     Places the creep camps the board authored on the composed map: the two camps and the boss the terrain states come
	///     through the composition as nobody's units, and the bounty and respawn time they are worth come from the templates
	///     the ruleset authors.
	/// </summary>
	[Test]
	public void TheBoardPlacesTheCreepCampsItsRulesetAuthors()
	{
		using MeleeMatch match = MeleeMatch.StartOnMap(Map.Folder, new[] { "Vale", "Sable" }, Map.ScriptPath);

		int boars = 0;
		int giants = 0;
		foreach (UnitId unit in match.Api.GetUnitsOwnedByPlayer(0))
		{
			string templateId = match.TemplateOf(unit);
			if (templateId == "unit/crest_boar") boars++;
			if (templateId == "unit/ridge_giant") giants++;
		}

		Assert.That(boars, Is.EqualTo(4), "The board places two boar camps of two animals each, and every one of them stands as nobody's unit.");
		Assert.That(giants, Is.EqualTo(1), "The board places the boss that holds the middle.");

		UnitMetadata? boar = match.Simulation.Map.Catalog.FindUnitOrBuilding("unit/crest_boar");
		Assert.That(boar, Is.Not.Null, "The camp the board places has to resolve its template out of the merged metadata.");
		Assert.That(boar!.GoldBounty, Is.GreaterThan(0f), "A camp is only worth contesting because its template states a bounty.");
		Assert.That(boar.RespawnSeconds, Is.GreaterThan(0f), "A camp returns only because its template states a respawn time.");

		UnitMetadata? giant = match.Simulation.Map.Catalog.FindUnitOrBuilding("unit/ridge_giant");
		Assert.That(giant, Is.Not.Null, "The boss has to resolve its template too.");
		Assert.That(giant!.GoldBounty, Is.GreaterThan(boar.GoldBounty), "A boss is worth more than the animals of a camp.");
	}

	/// <summary>
	///     Plays the composed map as a game rather than a seating check: the ruleset's deck drafts and finishes a building
	///     on the board's terrain, and the building raises its soldier and pays its income while the board keeps its props.
	/// </summary>
	[Test]
	public void TheComposedMatchPlaysItsRulesetOnTheBoardTerrain()
	{
		using MeleeMatch match = MeleeMatch.StartOnMap(Map.Folder, new[] { "Vale", "Sable" }, Map.ScriptPath);

		Assert.That(match.Simulation.Map.Warnings, Is.Empty, "The ruleset's templates and the board's terrain both have to resolve before a game is played on them.");
		int authoredProps = match.Simulation.Map.SaveData.Props?.Count ?? 0;
		Assert.That(authoredProps, Is.GreaterThan(0), "The board has to place props for the long run to say anything.");
		float startingGold = match.Gold(1);
		float startingWood = match.Wood(1);

		UnitId site = DraftBuildingOnTheComposedTerrain(match);

		match.StepTicks(PlayedTicks);

		Assert.That(match.IsUnderConstruction(site), Is.False, "The ruleset's construction clock has to finish the drafted site on the composed board.");
		Assert.That(match.IsAlive(match.Hero(1)), Is.True, $"The hero has to stand through {PlayedTicks} ticks of a composed game.");
		Assert.That(match.StepUntilSoldiers(1), Is.GreaterThan(0), "The finished building has to raise its soldier on the board's terrain.");
		Assert.That(match.Gold(1) - startingGold + match.Wood(1) - startingWood, Is.GreaterThan(0f), "The drafted building's income has to arrive through the composed metadata.");
		Assert.That(match.Simulation.IsGameOver, Is.False, "Nothing ends the game while the player only builds.");
		Assert.That(match.Simulation.Map.SaveData.Props?.Count ?? 0, Is.EqualTo(authoredProps), "The board's props have to stay on the board for the whole game.");
		Assert.That(match.Simulation.Map.HasNavMesh, Is.True, "The board's height field has to bake into the navmesh the soldier walks from.");
	}

	/// <summary>
	///     Gathers on the composed board: the ruleset's resource templates have to arrive through the metadata merge with the
	///     fields that make a node harvestable, or the board places props that look like nodes and yield nothing.
	/// </summary>
	[Test]
	public void TheComposedBoardsNodesCanBeWorked()
	{
		using MeleeMatch match = MeleeMatch.StartOnMap(Map.Folder, new[] { "Vale", "Sable" }, Map.ScriptPath);

		Assert.That(match.TotalResourceAmount(), Is.GreaterThan(0f), "The board's props have to become resource nodes that hold something.");
		foreach (ResourceNodeId node in match.Api.GetResourceNodes())
		{
			string nodeType = match.Api.GetResourceNodeType(node);
			if (nodeType != "gold" && nodeType != "wood") continue;

			Assert.That(
				match.World.Has<Harvestable>(new Entity(node.Id)),
				Is.True,
				$"The {nodeType} node has to keep the harvestable fields its ruleset template states, or it is mined at a fixed rate instead of by the worker's attack.");
		}

		UnitId site = DraftBuildingOnTheComposedTerrain(match);
		Assert.That(match.IsAlive(site), Is.True, "The drafted building has to stand for its worker to be raised.");

		match.Step(MeleeMatch.LongestAuthoredConstructionSeconds + 20f);
		Assert.That(match.StepUntilWorkers(1), Is.GreaterThan(0), "A finished building raises the worker that works it.");

		float before = match.TotalResourceAmount();
		match.Step(90f);

		Assert.That(
			match.TotalResourceAmount(),
			Is.LessThan(before),
			"A worker on the composed board has to take resource out of a node, which takes the ruleset's harvestable fields arriving through the merge.");
	}

	/// <summary>
	///     Drafts and locks a building where the composed terrain accepts one. The board's props decide which ground is
	///     free, so the site is searched for rather than assumed.
	/// </summary>
	private static string NormalizeAccessibility(string text)
	{
		string normalized = text.Replace("\r\n", "\n");
		string[] withoutModifiers = normalized.Split(
			new[] { "private protected", "protected internal", "private", "protected", "internal", "public", "sealed" },
			StringSplitOptions.None);

		var words = new List<string>();
		foreach (string part in withoutModifiers)
		{
			words.AddRange(part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		}

		return string.Join(' ', words);
	}

	private static bool DeclaresMemberAccessibility(string text, string accessibility)
	{
		foreach (string line in text.Split('\n'))
		{
			string trimmed = line.TrimStart();
			if (trimmed.StartsWith(accessibility + " ", StringComparison.Ordinal) || trimmed.StartsWith(accessibility + "\t", StringComparison.Ordinal)) return true;
		}

		return false;
	}

	private static UnitId DraftBuildingOnTheComposedTerrain(MeleeMatch match)
	{
		Vector3[] candidateSites =
		{
			new(-50f, 0f, -50f), new(-46f, 0f, -50f), new(-54f, 0f, -50f), new(-50f, 0f, -46f),
			new(-50f, 0f, -54f), new(-46f, 0f, -46f), new(-42f, 0f, -50f), new(-50f, 0f, -42f)
		};

		foreach (Vector3 sitePosition in candidateSites)
		{
			match.SetWood(1, 500f);
			DraftOffer offer = match.BeginConstruction(1, sitePosition);
			if (offer.AbilityIds.Length == 0) continue;

			match.LockDraftOption(1, offer.AbilityIds[0], sitePosition);
			foreach (UnitId building in match.Buildings(1))
			{
				if (!match.IsUnderConstruction(building)) continue;

				// A site is raised by the hero working at it, so the right-click that puts the hero to work is part of
				// drafting a building, not something the site does on its own.
				match.Raise(1, match.Hero(1), building);
				return building;
			}

			match.AbandonDraft(1);
		}

		Assert.Fail("No candidate site on the composed terrain accepted a drafted building.");
		return UnitId.Invalid;
	}
}
