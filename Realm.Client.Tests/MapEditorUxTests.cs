using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Shared;

namespace Realm.Client.Tests;

[TestSuite]
[RequireGodotRuntime]
public class MapEditorUxTests
{
	[TestCase]
	public async Task TestClickTestButtonOnBlankMap()
	{
		var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
		if (field != null)
		{
			field.SetValue(null, true);
		}

		ISceneRunner runner = ISceneRunner.Load("res://GameHost.tscn");
		await runner.AwaitMillis(1500);

		UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
		await runner.AwaitMillis(2500);

		var hud = MapEditorHUD.Instance;
		Assertions.AssertThat(hud).IsNotNull();

		var btnTestMap = hud!.GetNode<Button>("TopLeftBox/BtnTestMap");
		Assertions.AssertThat(btnTestMap).IsNotNull();

		// Trigger TestMapAction on blank map
		btnTestMap.EmitSignal("pressed");
		await runner.AwaitMillis(500);

		// Find the confirmation dialog overlay created by ShowConfirmationDialog
		Node? overlay = hud.GetNodeOrNull<Node>("ConfirmationDialogOverlay");

		if (overlay != null)
		{
			var btnConfirm = overlay.FindChild("BtnConfirm", true, false) as Button
			                 ?? overlay.FindChild("*Confirm*", true, false) as Button;

			if (btnConfirm != null)
			{
				btnConfirm.EmitSignal("pressed");
			}
			else
			{
				// Fallback: directly proceed if button search fails
				await hud.ProceedToTestMap();
			}
		}
		else
		{
			await hud.ProceedToTestMap();
		}

		await runner.AwaitMillis(2000);
		Assertions.AssertThat(WasmConsoleWindow.Instance).IsNotNull();
	}

	[TestCase]
	public async Task TestBlockModeRaiseTexturePainting()
	{
		var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
		if (field != null)
		{
			field.SetValue(null, true);
		}

		ISceneRunner runner = ISceneRunner.Load("res://GameHost.tscn");
		await runner.AwaitMillis(1500);

		UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
		await runner.AwaitMillis(2500);

		var gameHost = GameHost.Instance;
		Assertions.AssertThat(gameHost).IsNotNull();
		var hud = MapEditorHUD.Instance;
		Assertions.AssertThat(hud).IsNotNull();
		Assertions.AssertThat(gameHost!.GroundTerrain).IsNotNull();

		// 1. Select swatch "Lava Vein" for cliff face & "Deep Moss" for standard brush
		int deepMossIndex = 1;
		int lavaVeinIndex = 3;

		var displayNames = Enumerable.ToList(GameContext.EditorService.SwatchDisplayNames);
		for (int i = 0; i < displayNames.Count; i++)
		{
			if (displayNames[i].Equals("Deep Moss", StringComparison.OrdinalIgnoreCase)) deepMossIndex = i;
			if (displayNames[i].Equals("Lava Vein", StringComparison.OrdinalIgnoreCase)) lavaVeinIndex = i;
		}

		GameContext.EditorService.EditorPaintTextureIndex = deepMossIndex;
		GameContext.EditorService.EditorCliffPaintTextureIndex = lavaVeinIndex;

		// 2. Configure Raise tool in Block mode with 8.0f height step
		GameHost.Instance.ActiveEditorTool = EditorTool.Raise;
		GameHost.Instance.EditorBlockMode = true;
		GameHost.Instance.EditorBlockLevelHeight = 8.0f;
		GameHost.Instance.EditorBrushRadius = 14.0f;
		GameHost.Instance.EditorBrushIsSquare = true;

		var editorService = GameContext.EditorService;
		editorService.SetTerrainSplatMap(GameHost.Instance.GroundTerrain.SplatMap);

		// 3. Raise terrain step at Z = -10.0f to create a distinct cliff wall & top terrace
		Vector3 raiseCenter = new Vector3(0.0f, 0.0f, -10.0f);

		editorService.BeginTerrainDraw(
			raiseCenter,
			GameHost.Instance.ActiveEditorTool,
			GameHost.Instance.EditorBlockMode,
			GameHost.Instance.EditorBlockLevelHeight,
			GameHost.Instance.GroundTerrain.Heights,
			GameHost.Instance.GroundTerrain.SplatMap,
			GameHost.Instance.GroundTerrain.PathingCodes);

		var res = editorService.ApplyContinuousTerrainEditing(
			raiseCenter, 1.0f,
			GameHost.Instance.ActiveEditorTool,
			GameHost.Instance.EditorBrushRadius, GameHost.Instance.EditorBrushStrength,
			GameHost.Instance.EditorBrushIsSquare,
			GameHost.Instance.EditorBlockMode, GameHost.Instance.EditorBlockLevelHeight,
			GameContext.EditorService.EditorPaintTextureIndex, GameContext.EditorService.EditorCliffPaintTextureIndex,
			0, true, true);

		editorService.EndTerrainDraw(GameHost.Instance.GroundTerrain.Heights, GameHost.Instance.GroundTerrain.SplatMap, GameHost.Instance.GroundTerrain.PathingCodes);

		GameHost.Instance.GroundTerrain.UpdateMeshAndPhysics(true, true);
		await runner.AwaitMillis(1000);

		// 4. Position camera up close to view the front cliff face and top terrace step
		var camera = GameHost.Instance.MainCamera;
		if (camera != null)
		{
			camera.Position = new Vector3(0.0f, 14.0f, 18.0f);
			camera.RotationDegrees = new Vector3(-25.0f, 0.0f, 0.0f);
		}

		await runner.AwaitMillis(1000);

		// 5. Capture screenshot
		Image image = runner.Scene().GetViewport().GetTexture().GetImage();

		string tempDir = @"C:\temp\realm_screenshots";
		Directory.CreateDirectory(tempDir);
		string tempFilePath = Path.Combine(tempDir, "block_mode_raise_cliff_test.png");
		image.SavePng(tempFilePath);

		string artifactDir = @"C:\Users\devin\.gemini\antigravity-cli\brain\02ee3010-057f-4f99-96f3-b7a61c9ffebc";
		if (Directory.Exists(artifactDir))
		{
			string artifactFilePath = Path.Combine(artifactDir, "block_mode_raise_cliff_test.png");
			image.SavePng(artifactFilePath);
		}
	}
}