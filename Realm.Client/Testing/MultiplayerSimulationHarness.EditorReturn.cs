using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Common;

namespace Realm.Client.Testing;

/// <summary>
///     Reproduces what the editor's Test Map button does to the world it returns to: keeps the editor's world, plays the
///     map through the lobby exactly as a playtest does, and comes back through the same calls the Back to Editor button
///     makes. What the editor is standing on afterwards is reported, because the only reliable statement about it is the
///     numbers the world holds.
/// </summary>
public partial class MultiplayerSimulationHarness
{
	private const float EditorWarmupSeconds = 3.0f;
	private const float PlaytestSeconds = 12.0f;
	private const float EditorReturnSettleSeconds = 8.0f;

	/// <summary>
	///     Attaches the scenario to the tree's root, because the screen that runs it is the one the playtest replaces.
	///     This instance leaves the process to the node it adds.
	/// </summary>
	private async Task<int> RunEditorReturnLauncherAsync()
	{
		_probeOwnsProcess = true;

		// The tree is still setting up this scene while it runs, so the probe is attached once a frame has passed.
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var probeScene = GD.Load<PackedScene>("res://Testing/MultiplayerSimulationHarness.tscn");
		Node probe = probeScene.Instantiate();
		probe.Name = "EditorReturnProbe";
		SetPrivateProperty(probe, "Role", "editorreturnwork");
		GetTree().Root.AddChild(probe);
		return 0;
	}

	/// <summary>
	///     Stands in the map editor, plays the map, then returns to the editor the way its own button does, and reports
	///     what the editor's world holds afterwards compared with what it held before the playtest.
	/// </summary>
	private async Task<int> RunEditorReturnWorkAsync()
	{
		_probeOwnsProcess = true;
		var report = new List<string>();
		int exitCode = 0;

		try
		{
			GameContext.SessionState.IsMapEditorMode = true;
			PackedScene hostScene = GD.Load<PackedScene>("res://GameHost.tscn");
			Node editorHost = hostScene.Instantiate();
			editorHost.Name = "EditorGameHost";
			GetTree().CurrentScene.AddChild(editorHost);
			await ToSignal(GetTree().CreateTimer(EditorWarmupSeconds), SceneTreeTimer.SignalName.Timeout);

			GameHost editor = GameContext.Host ?? throw new InvalidOperationException("The editor's world never came up.");
			editor.StartMapEditorMode();
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			// The editor is given something of its own to come back to, so coming back from the playtest can be told
			// apart from simply being emptied.
			int placedUnits = PlaceEditorMarkers(editor);
			report.Add($"editor_before placed_units={placedUnits}");

			MapStateSnapshot editorSnapshot = MapStateSnapshot.CreateSnapshot();
			if (editorSnapshot == null) throw new InvalidOperationException("The editor's world produced no snapshot.");
			GameContext.SessionState.EditorWorldState = editorSnapshot;
			report.Add(DescribeWorld("editor_before", editor));
			report.Add($"editor_before snapshot={editorSnapshot.Width}x{editorSnapshot.Depth} units={editorSnapshot.Units.Count} props={editorSnapshot.Props.Count}");

			GameContext.SessionState.IsTestMode = true;
			editor.ExitMapEditorMode();
			bool playtestStarted = GameContext.Lobby != null && await GameContext.Lobby.HostSinglePlayerGame(_mapFolder, "Test Map");
			if (!playtestStarted)
			{
				report.Add($"playtest_started=0 error={GameContext.Lobby?.LastHostError}");
				exitCode = 1;
			}
			else
			{
				bool started = await WaitUntilAsync(
					() => GameContext.Host != null && !ReferenceEquals(GameContext.Host, editor) && (GameContext.NetworkService?.LastAppliedSnapshotSequenceForDiagnostics ?? -1) >= 0,
					180f);
				report.Add($"playtest_started={(started ? 1 : 0)}");

				await ToSignal(GetTree().CreateTimer(PlaytestSeconds), SceneTreeTimer.SignalName.Timeout);
				GameHost matchHost = GameContext.Host ?? throw new InvalidOperationException("The playtest's world went away.");
				report.Add(DescribeWorld("playtest_end", matchHost));

				// Exactly what the screen's own Back to Editor button does.
				GameContext.SessionState.IsTestMode = false;
				GameContext.SessionState.ReturningFromTest = true;
				GameContext.Lobby?.Disconnect();
				GameContext.UserInterface?.TransitionTo(GameScreen.MapEditorHUD);
				await ToSignal(GetTree().CreateTimer(EditorReturnSettleSeconds), SceneTreeTimer.SignalName.Timeout);

				GameHost? after = GameContext.Host;
				report.Add($"return hostChanged={(!ReferenceEquals(after, matchHost) ? 1 : 0)} returningFromTest={(GameContext.SessionState.ReturningFromTest ? 1 : 0)} keptSnapshot={(GameContext.SessionState.EditorWorldState != null ? 1 : 0)} mapEditorMode={(GameContext.SessionState.IsMapEditorMode ? 1 : 0)} editorAlive={(GodotObject.IsInstanceValid(editor) ? 1 : 0)}");
				report.Add(after == null ? "return_world=missing" : DescribeWorld("editor_after", after));

				bool matchesSnapshot = after != null
				                       && after.AllUnits.Count == editorSnapshot.Units.Count
				                       && after.AllProps.Count == editorSnapshot.Props.Count
				                       && (after.GroundTerrain?.Width ?? -1) == editorSnapshot.Width
				                       && (after.GroundTerrain?.Depth ?? -1) == editorSnapshot.Depth;
				report.Add($"editor_after matches_snapshot={(matchesSnapshot ? 1 : 0)} placed_units={placedUnits}");
				if (!matchesSnapshot) exitCode = 1;
				exitCode = 0;
			}
		}
		catch (Exception exception)
		{
			report.Add($"scenario_failed={exception}");
			exitCode = 1;
		}
		finally
		{
			foreach (string line in report) GD.Print($"[EDITOR_RETURN] {line}");
			try
			{
				File.WriteAllText(Path.Combine(_outputDirectory, "editor_return_report.txt"), new StringBuilder().AppendJoin('\n', report).ToString());
			}
			catch (IOException)
			{
			}

			GetTree().Quit(exitCode);
		}

		return exitCode;
	}

	/// <summary>
	///     Puts a few authored objects into the editor's world, which is what a map author would have on screen before a
	///     playtest, and reports how many were accepted.
	/// </summary>
	private int PlaceEditorMarkers(GameHost editor)
	{
		editor.LoadUnitMetadata(_mapFolder);
		string unitId = GameHost.UnitRegistry.Keys.Select(key => key.ToString()).Where(id => !string.IsNullOrEmpty(id)).OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault() ?? string.Empty;
		if (string.IsNullOrEmpty(unitId)) return 0;

		int placed = 0;
		if (editor.SpawnUnitExternal(unitId, new Vector3(12f, 0f, 12f), 0f, 1f, 0) != null) placed++;
		if (editor.SpawnUnitExternal(unitId, new Vector3(18f, 0f, 14f), 45f, 1f, 0) != null) placed++;
		return placed;
	}

	/// <summary>
	///     What a world holds, in the terms the editor paints from: the nodes it draws, the entities those nodes stand
	///     for, and the entities the renderers read instead of a node.
	/// </summary>
	private static string DescribeWorld(string label, GameHost host)
	{
		EcsWorld? world = GameContext.World;
		int units = -1;
		int props = -1;
		int loot = -1;
		int total = -1;
		if (world != null)
		{
			QueryDescription unitQuery = QueryCache.AllPositionAndUnitOwnerPlayerNoneDeadQuery;
			QueryDescription propQuery = QueryCache.AllPropIdentityAndPositionQuery;
			QueryDescription lootQuery = QueryCache.AllGroundLootAndPositionQuery;
			units = world.CountEntities(in unitQuery);
			props = world.CountEntities(in propQuery);
			loot = world.CountEntities(in lootQuery);
			total = world.CountEntities(default);
		}

		int terrainWidth = host.GroundTerrain?.Width ?? -1;
		int terrainDepth = host.GroundTerrain?.Depth ?? -1;
		return $"{label} nodes={host.AllUnits.Count} props={host.AllProps.Count} ecsUnits={units} ecsProps={props} ecsLoot={loot} ecsTotal={total} terrain={terrainWidth}x{terrainDepth}";
	}
}
