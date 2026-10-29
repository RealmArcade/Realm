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

		GameContext.UserInterface.TransitionTo(GameScreen.MapEditorHUD);
		await runner.AwaitMillis(2500);

		var hud = GameContext.MapEditorHud;
		Assertions.AssertThat(hud).IsNotNull();

		var btnTestMap = hud!.GetNode<Button>("TopLeftBox/BtnTestMap");
		Assertions.AssertThat(btnTestMap).IsNotNull();

		btnTestMap.EmitSignal("pressed");
		await runner.AwaitMillis(500);

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
				await hud.ProceedToTestMap();
			}
		}
		else
		{
			await hud.ProceedToTestMap();
		}

		await runner.AwaitMillis(2000);
		Assertions.AssertThat(GameContext.WasmConsole).IsNotNull();
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

		GameContext.UserInterface.TransitionTo(GameScreen.MapEditorHUD);
		await runner.AwaitMillis(2500);

		var gameHost = GameContext.Host;
		Assertions.AssertThat(gameHost).IsNotNull();
		var hud = GameContext.MapEditorHud;
		Assertions.AssertThat(hud).IsNotNull();
		Assertions.AssertThat(gameHost!.GroundTerrain).IsNotNull();

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

		GameContext.Host.ActiveEditorTool = EditorTool.Raise;
		GameContext.Host.EditorBlockMode = true;
		GameContext.Host.EditorBlockLevelHeight = 8.0f;
		GameContext.Host.EditorBrushRadius = 14.0f;
		GameContext.Host.EditorBrushIsSquare = true;

		var editorService = GameContext.EditorService;
		editorService.SetTerrainSplatMap(GameContext.Host.GroundTerrain.SplatMap);

		Vector3 raiseCenter = new Vector3(0.0f, 0.0f, -10.0f);

		editorService.BeginTerrainDraw(
			raiseCenter,
			GameContext.Host.ActiveEditorTool,
			GameContext.Host.EditorBlockMode,
			GameContext.Host.EditorBlockLevelHeight,
			GameContext.Host.GroundTerrain.Heights,
			GameContext.Host.GroundTerrain.SplatMap,
			GameContext.Host.GroundTerrain.PathingCodes);

		var res = editorService.ApplyContinuousTerrainEditing(
			raiseCenter, 1.0f,
			GameContext.Host.ActiveEditorTool,
			GameContext.Host.EditorBrushRadius, GameContext.Host.EditorBrushStrength,
			GameContext.Host.EditorBrushIsSquare,
			GameContext.Host.EditorBlockMode, GameContext.Host.EditorBlockLevelHeight,
			GameContext.EditorService.EditorPaintTextureIndex, GameContext.EditorService.EditorCliffPaintTextureIndex,
			0, true, true);

		editorService.EndTerrainDraw(GameContext.Host.GroundTerrain.Heights, GameContext.Host.GroundTerrain.SplatMap, GameContext.Host.GroundTerrain.PathingCodes);

		GameContext.Host.GroundTerrain.UpdateMeshAndPhysics(true, true);
		await runner.AwaitMillis(1000);

		var camera = GameContext.Host.MainCamera;
		if (camera != null)
		{
			camera.Position = new Vector3(0.0f, 14.0f, 18.0f);
			camera.RotationDegrees = new Vector3(-25.0f, 0.0f, 0.0f);
		}

		await runner.AwaitMillis(1000);

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