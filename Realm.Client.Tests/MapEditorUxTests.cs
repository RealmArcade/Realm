using GdUnit4;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Shared;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Realm.Client.Core;

namespace Realm.Client.Tests;

[TestSuite]
[RequireGodotRuntime]
public class MapEditorUxTests
{
	//[TestCase]
	public async Task CaptureMapEditorScreenshot()
	{
		// Prevent the creator agreement modal from showing and darkening the screen
		var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
		if (field != null)
		{
			field.SetValue(null, true);
		}

		ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
		await runner.AwaitMillis(1500);

		UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
		await runner.AwaitMillis(2500);

		var hud = MapEditorHUD.Instance;
		if (hud != null)
		{
			var btnPaint = hud.GetNode<Button>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelDecoVBox/BtnTextureBrush");
			hud.TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.PaintTexture, btnPaint);
			await runner.AwaitMillis(1000);
		}
		var sw = new StringWriter();
		if (hud != null)
		{
			sw.WriteLine($"--- DIAGNOSTICS START ---");
			sw.WriteLine($"hud: not null");
			sw.WriteLine($"ActiveModule in ViewModel: {hud.ViewModel?.ActiveModule}");
			sw.WriteLine($"ActiveEditorTool in GameHost: {Realm.Client.Core.GameHost.Instance?.ActiveEditorTool}");
			
			var panelTerrain = hud.GetNodeOrNull<Control>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelTerrainVBox");
			var panelDeco = hud.GetNodeOrNull<Control>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelDecoVBox");
			var panelPathing = hud.GetNodeOrNull<Control>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelPathingVBox");
			var panelClip = hud.GetNodeOrNull<Control>("RightSlidePanel/RightScroll/AccordionContainer/ToolAccordion/ContentTool/PanelClipboard");
			
			sw.WriteLine($"panelTerrain visible: {panelTerrain?.Visible}, null: {panelTerrain == null}");
			sw.WriteLine($"panelDeco visible: {panelDeco?.Visible}, null: {panelDeco == null}");
			sw.WriteLine($"panelPathing visible: {panelPathing?.Visible}, null: {panelPathing == null}");
			sw.WriteLine($"panelClip visible: {panelClip?.Visible}, null: {panelClip == null}");
			
			var minimap = hud.GetNodeOrNull<Control>("LeftSlidePanel/LeftScroll/LeftVBox/ViewportAccordion/ContentViewport/MinimapFrame");
			sw.WriteLine($"minimap visible: {minimap?.Visible}, null: {minimap == null}, size: {minimap?.Size}");
			
			var containerPathing = hud.GetNodeOrNull<Control>("RightSlidePanel/RightScroll/AccordionContainer/ToolSettingsAccordion/ContentToolSettings/ContainerPathing");
			sw.WriteLine($"containerPathing visible: {containerPathing?.Visible}, null: {containerPathing == null}");
			
			sw.WriteLine($"--- DIAGNOSTICS END ---");
		}
		else
		{
			sw.WriteLine("HUD IS NULL!");
		}

		File.WriteAllText(@"C:\temp\Realm\diagnostics.txt", sw.ToString());

		global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();
		string artifactDir = @"C:\Users\Devin\.gemini\antigravity-cli\brain\3f6febad-2673-47d8-b400-9b856eaa33d0";
		Directory.CreateDirectory(artifactDir);
		string filePath = Path.Combine(artifactDir, "map_editor_ux.png");
		image.SavePng(filePath);
	}

	//[TestCase]
	public void ConvertDefaultTileSheets()
	{
		var terrain = new EditableTerrain();
		var files = Directory.GetFiles(@"C:\temp\Realm\Realm.Client\Assets\2d\TileSheets", "*.png");
		foreach (var file in files)
		{
			string rtexPath = file.Replace(".png", ".rtex");
			System.Console.WriteLine($"Converting {file} to {rtexPath}");
			terrain.ProcessAndSaveRawTexture(file, rtexPath);
			if (!File.Exists(rtexPath))
			{
				throw new System.Exception($"Failed to convert {file} to {rtexPath}.");
			}
		}
	}

	[TestCase]
	public async Task VerifyTerrainSplatTransitions()
	{
		var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
		if (field != null)
		{
			field.SetValue(null, true);
		}

		ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
		await runner.AwaitMillis(1500);

		UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
		await runner.AwaitMillis(2500);

		var gameHost = Realm.Client.Core.GameHost.Instance;
		var camera = gameHost.MainCamera;
		if (camera != null)
		{
			camera.Position = new Vector3(0.0f, 35.0f, 25.0f);
			camera.RotationDegrees = new Vector3(-55.0f, 0.0f, 0.0f);
		}

		var terrain = gameHost.GroundTerrain;

		int width = terrain.Width;
		int depth = terrain.Depth;
		var splatMap = terrain.SplatMap;

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				if (x <= width / 2)
				{
					splatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
				}
				else
				{
					splatMap[x, z] = TerrainSplatWeights.CreateSolid(1);
				}
			}
		}

		var editorService = ServiceLocator.Get<EditorService>();
		editorService.SetTerrainSplatMap(splatMap);

		terrain.UpdateMeshAndPhysics(true, true);
		await runner.AwaitMillis(1000);

		global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();
		string artifactDir = @"C:\Users\Devin\.gemini\antigravity-cli\brain\7943d49b-03f6-4917-bd98-2a87e32bae94";
		Directory.CreateDirectory(artifactDir);
		string filePath = Path.Combine(artifactDir, "terrain_transition_test.png");
		image.SavePng(filePath);
	}

	//[TestCase]
	public async Task TestClickTestButtonOnBlankMap()
	{
		var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
		if (field != null)
		{
			field.SetValue(null, true);
		}

		ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
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
		Assertions.AssertThat(UI.WasmConsoleWindow.Instance).IsNotNull();
	}

	[TestCase]
	public async Task RandomGenCustomSeedScreenshot()
	{
		//NOTE: there seems to be a race condition in this test, if mesh screenshot is flat run a 2nd time.
		var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
		if (field != null)
		{
			field.SetValue(null, true);
		}

		ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
		await runner.AwaitMillis(1500);

		UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
		await runner.AwaitMillis(2500);

		var gameHost = Realm.Client.Core.GameHost.Instance;
		Assertions.AssertThat(gameHost).IsNotNull();

		MapGenerator.GenerateMap(
			gameHost,
			hillsDensity: 9,
			terrainRoughness: 7,
			mountainHeight: 9,
			chokeWidth: 7,
			waterLevel: 9,
			treeDensity: 8,
			resourceAbundance: 9,
			decoDensity: 10,
			seedString: "246272"
		);

		await runner.AwaitMillis(2000);

		var camera = gameHost.MainCamera;
		if (camera != null)
		{
			camera.Position = new Vector3(0.0f, 95.0f, 75.0f);
			camera.RotationDegrees = new Vector3(-55.0f, 0.0f, 0.0f);
		}

		await runner.AwaitMillis(1000);

		global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();
		string tempDir = @"C:\temp\realm_screenshots";
		Directory.CreateDirectory(tempDir);

		string baseFileName = "terrain_screenshot";
		int index = 1;
		string filePath = Path.Combine(tempDir, $"{baseFileName}_{index}.png");
		while (File.Exists(filePath))
		{
			index++;
			filePath = Path.Combine(tempDir, $"{baseFileName}_{index}.png");
		}

		image.SavePng(filePath);
	}

	[TestCase]
	public async Task TestBlockModeRaiseTexturePainting()
	{
		var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
		if (field != null)
		{
			field.SetValue(null, true);
		}

		ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
		await runner.AwaitMillis(1500);

		UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
		await runner.AwaitMillis(2500);

		var gameHost = Realm.Client.Core.GameHost.Instance;
		Assertions.AssertThat(gameHost).IsNotNull();
		var hud = MapEditorHUD.Instance;
		Assertions.AssertThat(hud).IsNotNull();
		Assertions.AssertThat(gameHost!.GroundTerrain).IsNotNull();

		// 1. Select swatch "Lava Vein" for cliff face & "Deep Moss" for standard brush
		int deepMossIndex = 1;
		int lavaVeinIndex = 3;

		var displayNamesField = typeof(MapEditorHUD).GetField("_swatchDisplayNames", BindingFlags.NonPublic | BindingFlags.Instance);
		if (displayNamesField?.GetValue(hud) is System.Collections.Generic.List<string> displayNames)
		{
			for (int i = 0; i < displayNames.Count; i++)
			{
				if (displayNames[i].Equals("Deep Moss", System.StringComparison.OrdinalIgnoreCase)) deepMossIndex = i;
				if (displayNames[i].Equals("Lava Vein", System.StringComparison.OrdinalIgnoreCase)) lavaVeinIndex = i;
			}
		}

		gameHost.EditorPaintTextureIndex = deepMossIndex;
		gameHost.EditorCliffPaintTextureIndex = lavaVeinIndex;

		// 2. Configure Raise tool in Block mode with 8.0f height step
		gameHost.ActiveEditorTool = GameHost.EditorTool.Raise;
		gameHost.EditorBlockMode = true;
		gameHost.EditorBlockLevelHeight = 8.0f;
		gameHost.EditorBrushRadius = 14.0f;
		gameHost.EditorBrushIsSquare = true;

		var editorService = ServiceLocator.Get<EditorService>();
		editorService.SetTerrainSplatMap(gameHost.GroundTerrain.SplatMap);

		// 3. Raise terrain step at Z = -10.0f to create a distinct cliff wall & top terrace
		Vector3 raiseCenter = new Vector3(0.0f, 0.0f, -10.0f);

		editorService.BeginTerrainDraw(
			raiseCenter,
			gameHost.ActiveEditorTool,
			gameHost.EditorBlockMode,
			gameHost.EditorBlockLevelHeight,
			gameHost.GroundTerrain.Heights,
			gameHost.GroundTerrain.SplatMap,
			gameHost.GroundTerrain.PathingCodes);

		var res = editorService.ApplyContinuousTerrainEditing(
			raiseCenter, 1.0f,
			gameHost.ActiveEditorTool,
			gameHost.EditorBrushRadius, gameHost.EditorBrushStrength,
			gameHost.EditorBrushIsSquare,
			gameHost.EditorBlockMode, gameHost.EditorBlockLevelHeight,
			gameHost.EditorPaintTextureIndex, gameHost.EditorCliffPaintTextureIndex,
			0, true, true);

		editorService.EndTerrainDraw(gameHost.GroundTerrain.Heights, gameHost.GroundTerrain.SplatMap, gameHost.GroundTerrain.PathingCodes);

		gameHost.GroundTerrain.UpdateMeshAndPhysics(true, true);
		await runner.AwaitMillis(1000);

		// 4. Position camera up close to view the front cliff face and top terrace step
		var camera = gameHost.MainCamera;
		if (camera != null)
		{
			camera.Position = new Vector3(0.0f, 14.0f, 18.0f);
			camera.RotationDegrees = new Vector3(-25.0f, 0.0f, 0.0f);
		}

		await runner.AwaitMillis(1000);

		// 5. Capture screenshot
		global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();

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

	[TestCase]
	public async Task TestExactMouseClickInEditorGUI()
	{
		var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
		if (field != null)
		{
			field.SetValue(null, true);
		}

		ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
		await runner.AwaitMillis(1500);

		UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
		await runner.AwaitMillis(2500);

		var gameHost = Realm.Client.Core.GameHost.Instance;
		Assertions.AssertThat(gameHost).IsNotNull();
		var hud = MapEditorHUD.Instance;
		Assertions.AssertThat(hud).IsNotNull();
		Assertions.AssertThat(gameHost!.GroundTerrain).IsNotNull();

		// Match user's UI settings from uploaded image:
		// Brush Swatch = Lava Vein
		// Cliff Swatch = Deep Moss
		int lavaVeinIndex = 3;
		int deepMossIndex = 1;

		var displayNamesField = typeof(MapEditorHUD).GetField("_swatchDisplayNames", BindingFlags.NonPublic | BindingFlags.Instance);
		if (displayNamesField?.GetValue(hud) is System.Collections.Generic.List<string> displayNames)
		{
			for (int i = 0; i < displayNames.Count; i++)
			{
				if (displayNames[i].Equals("Lava Vein", System.StringComparison.OrdinalIgnoreCase)) lavaVeinIndex = i;
				if (displayNames[i].Equals("Deep Moss", System.StringComparison.OrdinalIgnoreCase)) deepMossIndex = i;
			}
		}

		gameHost.EditorPaintTextureIndex = lavaVeinIndex;
		gameHost.EditorCliffPaintTextureIndex = deepMossIndex;

		// Tool: Raise in Block Mode, Step Height 4.0m, Size 6, Square Brush
		gameHost.ActiveEditorTool = GameHost.EditorTool.Raise;
		gameHost.EditorBlockMode = true;
		gameHost.EditorBlockLevelHeight = 4.0f;
		gameHost.EditorBrushRadius = 6.0f;
		gameHost.EditorBrushIsSquare = true;

		var editorService = ServiceLocator.Get<EditorService>();
		editorService.SetTerrainSplatMap(gameHost.GroundTerrain.SplatMap);

		// Single click at center position (0, 0, 0)
		Vector3 clickPos = new Vector3(0.0f, 0.0f, 0.0f);

		editorService.BeginTerrainDraw(
			clickPos,
			gameHost.ActiveEditorTool,
			gameHost.EditorBlockMode,
			gameHost.EditorBlockLevelHeight,
			gameHost.GroundTerrain.Heights,
			gameHost.GroundTerrain.SplatMap,
			gameHost.GroundTerrain.PathingCodes);

		var res = editorService.ApplyContinuousTerrainEditing(
			clickPos, 1.0f,
			gameHost.ActiveEditorTool,
			gameHost.EditorBrushRadius, gameHost.EditorBrushStrength,
			gameHost.EditorBrushIsSquare,
			gameHost.EditorBlockMode, gameHost.EditorBlockLevelHeight,
			gameHost.EditorPaintTextureIndex, gameHost.EditorCliffPaintTextureIndex,
			0, true, true);

		editorService.EndTerrainDraw(gameHost.GroundTerrain.Heights, gameHost.GroundTerrain.SplatMap, gameHost.GroundTerrain.PathingCodes);

		gameHost.GroundTerrain.UpdateMeshAndPhysics(true, true);
		await runner.AwaitMillis(1000);
		gameHost.GroundTerrain.SetWireframeMode(false);
		await runner.AwaitMillis(1000);

		// Diagnostic logging of terrain heights, splat maps, and rendered GPU wall slope geometry
		int w = gameHost.GroundTerrain.Width;
		int d = gameHost.GroundTerrain.Depth;
		int centerGridX = (w - 1) / 2;
		int centerGridZ = (d - 1) / 2;

		var sw = new System.IO.StringWriter();
		sw.WriteLine("=================== CPU DATA STRUCTURE DIAGNOSTICS ===================");
		sw.WriteLine($"Terrain Width={w}, Depth={d}, QuadSize={gameHost.GroundTerrain.QuadSize:F1}");
		sw.WriteLine($"Center Grid: X={centerGridX}, Z={centerGridZ}");

		float[,] heights = gameHost.GroundTerrain.Heights;

		sw.WriteLine("\n--- Heights[,] Array (-5 to +5 around center) ---");
		for (int z = centerGridZ - 5; z <= centerGridZ + 5; z++)
		{
			var line = new System.Text.StringBuilder();
			for (int x = centerGridX - 5; x <= centerGridX + 5; x++)
			{
				line.Append($"{heights[x, z],5:F1} ");
			}
			sw.WriteLine(line.ToString());
		}

		sw.WriteLine("\n=================== GPU RENDERED MESH DIAGNOSTICS ===================");
		var allVertices = new System.Collections.Generic.List<Vector3>();
		var allNormals = new System.Collections.Generic.List<Vector3>();

		var terrainField = typeof(EditableTerrain).GetField("_chunks", BindingFlags.NonPublic | BindingFlags.Instance);
		if (terrainField?.GetValue(gameHost.GroundTerrain) is System.Collections.IEnumerable chunksEnum)
		{
			int cCount = 0;
			foreach (var chunkObj in chunksEnum)
			{
				if (chunkObj == null) continue;
				cCount++;
				var meshField = chunkObj.GetType().GetField("ArrayMesh");
				if (meshField?.GetValue(chunkObj) is ArrayMesh arrayMesh && arrayMesh.GetSurfaceCount() > 0)
				{
					var surfArrays = arrayMesh.SurfaceGetArrays(0);
					if (surfArrays != null && surfArrays.Count > (int)Mesh.ArrayType.Normal)
					{
						var verts = surfArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
						var norms = surfArrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
						allVertices.AddRange(verts);
						allNormals.AddRange(norms);
					}
				}
			}
			sw.WriteLine($"Total Terrain Chunks Processed: {cCount}");
		}

		sw.WriteLine($"Total Rendered Mesh Vertices Across Chunks: {allVertices.Count}");

		var groundVerts = new System.Collections.Generic.List<Vector3>();
		var apexVerts = new System.Collections.Generic.List<Vector3>();
		var wallVerts = new System.Collections.Generic.List<Vector3>();
		var wallNormals = new System.Collections.Generic.List<Vector3>();

		for (int i = 0; i < allVertices.Count; i++)
		{
			Vector3 v = allVertices[i];
			Vector3 n = allNormals[i];

			if (v.Y > 0.05f && v.Y < 3.95f)
			{
				wallVerts.Add(v);
				wallNormals.Add(n);
			}
			else if (v.Y >= 3.95f)
			{
				apexVerts.Add(v);
			}
			else if (v.Y <= 0.05f)
			{
				groundVerts.Add(v);
			}
		}

		sw.WriteLine($"Count of Ground Vertices (Y ~ 0): {groundVerts.Count}");
		sw.WriteLine($"Count of Apex Vertices (Y ~ 4): {apexVerts.Count}");
		sw.WriteLine($"Count of Vertical Wall Vertices: {wallVerts.Count}");

		sw.WriteLine("\n--- VERTICAL WALL COLUMN & SLOPE ANGLE ANALYSIS ---");
		var wallColumns = new System.Collections.Generic.Dictionary<(int xKey, int zKey), System.Collections.Generic.List<Vector3>>();
		foreach (var wv in wallVerts)
		{
			int xKey = (int)System.Math.Round(wv.X * 100.0f);
			int zKey = (int)System.Math.Round(wv.Z * 100.0f);
			var key = (xKey, zKey);
			if (!wallColumns.ContainsKey(key))
			{
				wallColumns[key] = new System.Collections.Generic.List<Vector3>();
			}
			wallColumns[key].Add(wv);
		}

		sw.WriteLine($"Total Wall Grid Column Locations (XZ): {wallColumns.Count}");
		foreach (var kvp in wallColumns)
		{
			float posX = kvp.Key.xKey / 100.0f;
			float posZ = kvp.Key.zKey / 100.0f;
			var list = kvp.Value;
			float minY = 999f, maxY = -999f;
			foreach (var v in list)
			{
				if (v.Y < minY) minY = v.Y;
				if (v.Y > maxY) maxY = v.Y;
			}

			sw.WriteLine($"Wall Column at (X={posX:F2}, Z={posZ:F2}): Min Y={minY:F2}, Max Y={maxY:F2}, Height Delta={maxY - minY:F2}");
		}

		sw.WriteLine("\n--- FOOTPRINT BOUNDS ANALYSIS ---");
		if (apexVerts.Count > 0)
		{
			float minX = 999, maxX = -999, minZ = 999, maxZ = -999;
			foreach (var v in apexVerts)
			{
				if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
				if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
			}
			sw.WriteLine($"Apex Footprint: X=[{minX:F2} to {maxX:F2}] (Width={(maxX - minX):F2}), Z=[{minZ:F2} to {maxZ:F2}] (Depth={(maxZ - minZ):F2})");
		}

		if (wallVerts.Count > 0)
		{
			float minX = 999, maxX = -999, minZ = 999, maxZ = -999;
			foreach (var v in wallVerts)
			{
				if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
				if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
			}
			sw.WriteLine($"Wall Rim/Base Outer Bounds: X=[{minX:F2} to {maxX:F2}], Z=[{minZ:F2} to {maxZ:F2}]");
		}

		sw.WriteLine("\n--- CLIFF WALL PERIMETER ENCLOSURE DIAGNOSTICS ---");
		// Count Apex quads and evaluate perimeter wall edges
		int apexQuadCount = 0;
		int expectedWestWalls = 0, expectedEastWalls = 0, expectedNorthWalls = 0, expectedSouthWalls = 0;

		for (int z = 0; z < d - 1; z++)
		{
			for (int x = 0; x < w - 1; x++)
			{
				if (heights[x, z] > heights[x, z] + 0.001f)
				{
					apexQuadCount++;
					if (x == 0 || heights[x - 1, z] <= heights[x - 1, z] + 0.001f) expectedWestWalls++;
					if (x == w - 2 || heights[x + 1, z] <= heights[x + 1, z] + 0.001f) expectedEastWalls++;
					if (z == 0 || heights[x, z - 1] <= heights[x, z - 1] + 0.001f) expectedNorthWalls++;
					if (z == d - 2 || heights[x, z + 1] <= heights[x, z + 1] + 0.001f) expectedSouthWalls++;
				}
			}
		}

		int totalExpectedWallQuads = expectedWestWalls + expectedEastWalls + expectedNorthWalls + expectedSouthWalls;
		sw.WriteLine($"Total Apex Quads: {apexQuadCount}");
		sw.WriteLine($"Expected Perimeter Walls -> West: {expectedWestWalls}, East: {expectedEastWalls}, North: {expectedNorthWalls}, South: {expectedSouthWalls}");
		sw.WriteLine($"Total Expected Wall Quad Segments: {totalExpectedWallQuads}");
		sw.WriteLine($"Cliff Perimeter Fully Enclosed: {expectedWestWalls > 0 && expectedEastWalls > 0 && expectedNorthWalls > 0 && expectedSouthWalls > 0}");

		string diagText = sw.ToString();
		System.IO.File.WriteAllText(@"C:\temp\realm_screenshots\wall_slope_diagnostics.txt", diagText);
		global::Godot.GD.Print(diagText);

		// Position camera to match the angle from uploaded image
		var camera = gameHost.MainCamera;
		if (camera != null)
		{
			camera.Position = new Vector3(0.0f, 25.0f, 35.0f);
			camera.RotationDegrees = new Vector3(-35.0f, 0.0f, 0.0f);
		}

		await runner.AwaitMillis(1000);

		// Capture screenshot
		global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();

		string tempDir = @"C:\temp\realm_screenshots";
		Directory.CreateDirectory(tempDir);
		string tempFilePath = Path.Combine(tempDir, "exact_ui_click_reproduction_test.png");
		image.SavePng(tempFilePath);

		string artifactDir = @"C:\Users\devin\.gemini\antigravity-cli\brain\02ee3010-057f-4f99-96f3-b7a61c9ffebc";
		if (Directory.Exists(artifactDir))
		{
			string artifactFilePath = Path.Combine(artifactDir, "exact_ui_click_reproduction_test.png");
			image.SavePng(artifactFilePath);
		}
	}

	[TestCase]
	public async Task TestExactMouseClickLowerToolInEditorGUI()
	{
		var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
		if (field != null)
		{
			field.SetValue(null, true);
		}

		ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
		await runner.AwaitMillis(1500);

		UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
		await runner.AwaitMillis(2500);

		var gameHost = Realm.Client.Core.GameHost.Instance;
		Assertions.AssertThat(gameHost).IsNotNull();

		gameHost.ActiveEditorTool = GameHost.EditorTool.Lower;
		gameHost.EditorBlockMode = true;
		gameHost.EditorBlockLevelHeight = 4.0f;
		gameHost.EditorBrushRadius = 6.0f;
		gameHost.EditorBrushIsSquare = true;

		var editorService = ServiceLocator.Get<EditorService>();
		editorService.SetTerrainSplatMap(gameHost.GroundTerrain.SplatMap);

		Vector3 clickPos = new Vector3(0.0f, 0.0f, 0.0f);

		editorService.BeginTerrainDraw(
			clickPos,
			gameHost.ActiveEditorTool,
			gameHost.EditorBlockMode,
			gameHost.EditorBlockLevelHeight,
			gameHost.GroundTerrain.Heights,
			gameHost.GroundTerrain.SplatMap,
			gameHost.GroundTerrain.PathingCodes);

		var res = editorService.ApplyContinuousTerrainEditing(
			clickPos, 1.0f,
			gameHost.ActiveEditorTool,
			gameHost.EditorBrushRadius, gameHost.EditorBrushStrength,
			gameHost.EditorBrushIsSquare,
			gameHost.EditorBlockMode, gameHost.EditorBlockLevelHeight,
			gameHost.EditorPaintTextureIndex, gameHost.EditorCliffPaintTextureIndex,
			0, true, true);

		editorService.EndTerrainDraw(gameHost.GroundTerrain.Heights, gameHost.GroundTerrain.SplatMap, gameHost.GroundTerrain.PathingCodes);

		gameHost.GroundTerrain.UpdateMeshAndPhysics(true, true);
		await runner.AwaitMillis(1000);
		// gameHost.Instance.GroundTerrain.SetWireframeMode(true);
		await runner.AwaitMillis(1000);

		int w = gameHost.GroundTerrain.Width;
		int d = gameHost.GroundTerrain.Depth;
		int centerGridX = (w - 1) / 2;
		int centerGridZ = (d - 1) / 2;

		var sw = new System.IO.StringWriter();
		sw.WriteLine("=================== CPU DATA STRUCTURE DIAGNOSTICS (LOWER TOOL) ===================");
		sw.WriteLine($"Terrain Width={w}, Depth={d}, QuadSize={gameHost.GroundTerrain.QuadSize:F1}");
		sw.WriteLine($"Center Grid: X={centerGridX}, Z={centerGridZ}");

		float[,] heights = gameHost.GroundTerrain.Heights;

		sw.WriteLine("\n--- Heights[,] Array (-5 to +5 around center) ---");
		for (int z = centerGridZ - 5; z <= centerGridZ + 5; z++)
		{
			var line = new System.Text.StringBuilder();
			for (int x = centerGridX - 5; x <= centerGridX + 5; x++)
			{
				line.Append($"{heights[x, z],5:F1} ");
			}
			sw.WriteLine(line.ToString());
		}

		sw.WriteLine("\n=================== GPU RENDERED MESH DIAGNOSTICS (LOWER TOOL) ===================");
		var allVertices = new System.Collections.Generic.List<Vector3>();
		var allNormals = new System.Collections.Generic.List<Vector3>();

		var terrainField = typeof(EditableTerrain).GetField("_chunks", BindingFlags.NonPublic | BindingFlags.Instance);
		if (terrainField?.GetValue(gameHost.GroundTerrain) is System.Collections.IEnumerable chunksEnum)
		{
			int cCount = 0;
			foreach (var chunkObj in chunksEnum)
			{
				if (chunkObj == null) continue;
				cCount++;
				var meshField = chunkObj.GetType().GetField("ArrayMesh");
				if (meshField?.GetValue(chunkObj) is ArrayMesh arrayMesh && arrayMesh.GetSurfaceCount() > 0)
				{
					var surfArrays = arrayMesh.SurfaceGetArrays(0);
					if (surfArrays != null && surfArrays.Count > (int)Mesh.ArrayType.Normal)
					{
						var verts = surfArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
						var norms = surfArrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
						allVertices.AddRange(verts);
						allNormals.AddRange(norms);
					}
				}
			}
			sw.WriteLine($"Total Terrain Chunks Processed: {cCount}");
		}

		sw.WriteLine($"Total Rendered Mesh Vertices Across Chunks: {allVertices.Count}");

		var groundVerts = new System.Collections.Generic.List<Vector3>();
		var trenchVerts = new System.Collections.Generic.List<Vector3>();
		var wallVerts = new System.Collections.Generic.List<Vector3>();
		var wallNormals = new System.Collections.Generic.List<Vector3>();

		for (int i = 0; i < allVertices.Count; i++)
		{
			Vector3 v = allVertices[i];
			Vector3 n = allNormals[i];

			if (v.Y < -0.05f && v.Y > -3.95f)
			{
				wallVerts.Add(v);
				wallNormals.Add(n);
			}
			else if (v.Y <= -3.95f)
			{
				trenchVerts.Add(v);
			}
			else if (v.Y >= -0.05f)
			{
				groundVerts.Add(v);
			}
		}

		sw.WriteLine($"Count of Ground Vertices (Y ~ 0): {groundVerts.Count}");
		sw.WriteLine($"Count of Trench Floor Vertices (Y ~ -4): {trenchVerts.Count}");
		sw.WriteLine($"Count of Vertical Wall Vertices: {wallVerts.Count}");

		sw.WriteLine("\n--- VERTICAL WALL COLUMN & SLOPE ANGLE ANALYSIS ---");
		var wallColumns = new System.Collections.Generic.Dictionary<(int xKey, int zKey), System.Collections.Generic.List<Vector3>>();
		foreach (var wv in wallVerts)
		{
			int xKey = (int)System.Math.Round(wv.X * 100.0f);
			int zKey = (int)System.Math.Round(wv.Z * 100.0f);
			var key = (xKey, zKey);
			if (!wallColumns.ContainsKey(key))
			{
				wallColumns[key] = new System.Collections.Generic.List<Vector3>();
			}
			wallColumns[key].Add(wv);
		}

		sw.WriteLine($"Total Wall Grid Column Locations (XZ): {wallColumns.Count}");
		foreach (var kvp in wallColumns)
		{
			float posX = kvp.Key.xKey / 100.0f;
			float posZ = kvp.Key.zKey / 100.0f;
			var list = kvp.Value;
			float minY = 999f, maxY = -999f;
			foreach (var v in list)
			{
				if (v.Y < minY) minY = v.Y;
				if (v.Y > maxY) maxY = v.Y;
			}

			sw.WriteLine($"Wall Column at (X={posX:F2}, Z={posZ:F2}): Min Y={minY:F2}, Max Y={maxY:F2}, Height Delta={maxY - minY:F2}");
		}

		sw.WriteLine("\n--- FOOTPRINT BOUNDS ANALYSIS ---");
		if (trenchVerts.Count > 0)
		{
			float minX = 999, maxX = -999, minZ = 999, maxZ = -999;
			foreach (var v in trenchVerts)
			{
				if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
				if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
			}
			sw.WriteLine($"Trench Floor Footprint: X=[{minX:F2} to {maxX:F2}] (Width={(maxX - minX):F2}), Z=[{minZ:F2} to {maxZ:F2}] (Depth={(maxZ - minZ):F2})");
		}

		if (wallVerts.Count > 0)
		{
			float minX = 999, maxX = -999, minZ = 999, maxZ = -999;
			foreach (var v in wallVerts)
			{
				if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
				if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
			}
			sw.WriteLine($"Wall Rim/Base Outer Bounds: X=[{minX:F2} to {maxX:F2}], Z=[{minZ:F2} to {maxZ:F2}]");
		}

		string diagText = sw.ToString();
		System.IO.File.WriteAllText(@"C:\temp\realm_screenshots\lower_wall_slope_diagnostics.txt", diagText);
		global::Godot.GD.Print(diagText);

		var camera = gameHost.MainCamera;
		if (camera != null)
		{
			camera.Position = new Vector3(0.0f, 25.0f, 35.0f);
			camera.RotationDegrees = new Vector3(-35.0f, 0.0f, 0.0f);
		}

		await runner.AwaitMillis(1000);

		global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();
		string tempDir = @"C:\temp\realm_screenshots";
		Directory.CreateDirectory(tempDir);
		string tempFilePath = Path.Combine(tempDir, "lower_exact_ui_click_reproduction_test.png");
		image.SavePng(tempFilePath);
	}

	[TestCase]
	public async Task TestMapEdgeRaiseCliffHairlineFractures()
	{
		var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
		if (field != null)
		{
			field.SetValue(null, true);
		}

		ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
		await runner.AwaitMillis(1500);

		UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
		await runner.AwaitMillis(2500);

		var gameHost = Realm.Client.Core.GameHost.Instance;
		Assertions.AssertThat(gameHost).IsNotNull();
		var hud = MapEditorHUD.Instance;
		Assertions.AssertThat(hud).IsNotNull();
		Assertions.AssertThat(gameHost!.GroundTerrain).IsNotNull();

		int lavaVeinIndex = 3;
		int deepMossIndex = 1;

		var displayNamesField = typeof(MapEditorHUD).GetField("_swatchDisplayNames", BindingFlags.NonPublic | BindingFlags.Instance);
		if (displayNamesField?.GetValue(hud) is System.Collections.Generic.List<string> displayNames)
		{
			for (int i = 0; i < displayNames.Count; i++)
			{
				if (displayNames[i].Equals("Lava Vein", System.StringComparison.OrdinalIgnoreCase)) lavaVeinIndex = i;
				if (displayNames[i].Equals("Deep Moss", System.StringComparison.OrdinalIgnoreCase)) deepMossIndex = i;
			}
		}

		gameHost.EditorPaintTextureIndex = lavaVeinIndex;
		gameHost.EditorCliffPaintTextureIndex = deepMossIndex;

		// Tool: Raise in Block Mode, step height 24.0m, small cursor radius 1.0f
		gameHost.ActiveEditorTool = GameHost.EditorTool.Raise;
		gameHost.EditorBlockMode = true;
		gameHost.EditorBlockLevelHeight = 24.0f;
		gameHost.EditorBrushRadius = 1.0f;
		gameHost.EditorBrushIsSquare = true;

		var editorService = ServiceLocator.Get<EditorService>();
		editorService.SetTerrainSplatMap(gameHost.GroundTerrain.SplatMap);

		float halfW = (gameHost.GroundTerrain.Width - 1) / 2.0f * gameHost.GroundTerrain.QuadSize;
		float halfD = (gameHost.GroundTerrain.Depth - 1) / 2.0f * gameHost.GroundTerrain.QuadSize;

		// Draw raised terrain along the edge of the map border
		float startX = -halfW + 10.0f;
		float endX = halfW - 10.0f;
		float startZ = -halfD + 2.0f;
		float endZ = -halfD + 10.0f;
		Vector3 strokeStart = new Vector3(startX, 0.0f, startZ);

		editorService.BeginTerrainDraw(
			strokeStart,
			gameHost.ActiveEditorTool,
			gameHost.EditorBlockMode,
			gameHost.EditorBlockLevelHeight,
			gameHost.GroundTerrain.Heights,
			gameHost.GroundTerrain.SplatMap,
			gameHost.GroundTerrain.PathingCodes);

		int steps = 120;
		for (int i = 0; i <= steps; i++)
		{
			float t = (float)i / steps;
			float currX = Mathf.Lerp(startX, endX, t);
			float currZ = Mathf.Lerp(startZ, endZ, t);
			Vector3 currPos = new Vector3(currX, 0.0f, currZ);
			editorService.ApplyContinuousTerrainEditing(
				currPos, 1.0f,
				gameHost.ActiveEditorTool,
				gameHost.EditorBrushRadius, gameHost.EditorBrushStrength,
				gameHost.EditorBrushIsSquare,
				gameHost.EditorBlockMode, gameHost.EditorBlockLevelHeight,
				gameHost.EditorPaintTextureIndex, gameHost.EditorCliffPaintTextureIndex,
				0, true, true);
		}

		editorService.EndTerrainDraw(gameHost.GroundTerrain.Heights, gameHost.GroundTerrain.SplatMap, gameHost.GroundTerrain.PathingCodes);

		gameHost.GroundTerrain.UpdateMeshAndPhysics(true, true);
		await runner.AwaitMillis(1000);

		// Pan camera to view the map border cliff face
		var camera = gameHost.MainCamera;
		if (camera != null)
		{
			camera.Position = new Vector3(0.0f, 35.0f, -90.0f);
			camera.RotationDegrees = new Vector3(-35.0f, 0.0f, 0.0f);
		}

		await runner.AwaitMillis(1000);

		global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();

		string tempDir = @"C:\temp\realm_screenshots";
		Directory.CreateDirectory(tempDir);
		string tempFilePath = Path.Combine(tempDir, "map_edge_tall_step_cliff_reproduction.png");
		image.SavePng(tempFilePath);

		string artifactDir = @"C:\Users\devin\.gemini\antigravity-cli\brain\b8ffea97-4c62-4552-b4df-4098a5c5f68c";
		Directory.CreateDirectory(artifactDir);
		string artifactFilePath = Path.Combine(artifactDir, "map_edge_tall_step_cliff_reproduction.png");
		image.SavePng(artifactFilePath);
	}

	[TestCase]
	public async Task TestCustomWeaponProjectileLayersAndVisualRendering()
	{
		if (LobbyManager.Instance == null)
		{
			return;
		}

		// Prevent creator agreement modal from blocking test
		var agreementField = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
		agreementField?.SetValue(null, true);

		// 1. Setup temp map workspace
		string tempMapDir = Path.Combine(Path.GetTempPath(), "Realm_CustomProjectile_TestMap");
		if (Directory.Exists(tempMapDir))
		{
			try { Directory.Delete(tempMapDir, true); } catch { }
		}
		Directory.CreateDirectory(tempMapDir);

		MapWorkspaceService.SetupWorkspace(tempMapDir, "CustomProjMap");

		// 2. Prepare Asset folders and copy assets
		string projectilesDir = Path.Combine(tempMapDir, "Assets", "models", "projectiles");
		string ribbonsDir = Path.Combine(tempMapDir, "Assets", "ribbons");
		Directory.CreateDirectory(projectilesDir);
		Directory.CreateDirectory(ribbonsDir);

		string srcGlb = FindAssetInWorkspace("spiked_orb_projectile.glb");
		string srcRibbon = FindAssetInWorkspace("void_whisper_shadow_veil.png");

		Assertions.AssertThat(File.Exists(srcGlb)).IsTrue();
		Assertions.AssertThat(File.Exists(srcRibbon)).IsTrue();

		File.Copy(srcGlb, Path.Combine(projectilesDir, "spiked_orb_projectile.glb"), true);
		File.Copy(srcRibbon, Path.Combine(ribbonsDir, "void_whisper_shadow_veil.png"), true);

		// 3. Write metadata.json with custom weapon (Layer 1: spiked orb model, Layer 2: fireball preset, Layer 3: void_whisper ribbon)
		// and 2 custom units (Ranged Caster vs Target Dummy)
		string metadataJson = @"
{
  ""MapProperties"": {
	""MapName"": ""CustomProjMap"",
	""Author"": ""TestAuthor"",
	""Description"": ""Custom Projectile 3-Layer Test""
  },
  ""Templates"": {
	""Weapons"": [
      {
		""WeaponId"": ""spiked_fireball_weapon"",
		""Name"": ""Spiked Fireball Launcher"",
		""Damage"": 30.0,
		""Range"": 20.0,
		""AttackCooldown"": 0.8,
		""ProjectileSpeed"": 16.0,
		""ProjectileModelPath"": ""Assets/models/projectiles/spiked_orb_projectile.glb"",
		""ArcHeight"": 3.5,
		""HomingWeight"": 0.25,
		""TumbleAngularVelocity"": { ""X"": 4.0, ""Y"": 3.0, ""Z"": 1.5 },
		""ShaderEffectType"": ""fire"",
		""BaseColor"": ""#261e19"",
		""EmissionColor"": ""#ff5500"",
		""EmissionEnergy"": 5.0,
		""FresnelPower"": 2.5,
		""FresnelColor"": ""#ff9922"",
		""FresnelFactor"": 2.0,
		""NoiseScale"": 3.5,
		""UvScrollSpeed1"": { ""X"": 0.4, ""Y"": 0.2 },
		""UvScrollSpeed2"": { ""X"": -0.3, ""Y"": 0.4 },
		""ThresholdCutoff"": 0.45,
		""ThresholdSmoothness"": 0.1,
		""RibbonTexture"": ""Assets/ribbons/void_whisper_shadow_veil.png"",
		""RibbonColor"": ""#ff7711"",
		""RibbonWidth"": 0.45,
		""RibbonLifetime"": 0.6,
		""RibbonTaper"": true,
		""RibbonAdditive"": true
      }
    ],
	""Units"": [
      {
		""UnitId"": ""fire_orb_mage"",
		""Name"": ""Fire Orb Mage"",
		""MaxHp"": 250.0,
		""Damage"": 30.0,
		""Range"": 20.0,
		""AttackCooldown"": 0.8,
		""Speed"": 6.0,
		""ScanRadius"": 30.0,
		""ModelPath"": ""Assets/models/units/soldier.glb"",
		""Weapons"": [""spiked_fireball_weapon""]
      },
      {
		""UnitId"": ""enemy_training_dummy"",
		""Name"": ""Enemy Training Dummy"",
		""MaxHp"": 600.0,
		""Damage"": 0.0,
		""Range"": 0.0,
		""Speed"": 0.0,
		""ScanRadius"": 0.0,
		""ModelPath"": ""Assets/models/units/worker.glb""
      }
    ]
  },
  ""Assets"": {
	""glb"": {
	  ""projectiles"": {
		""spiked_orb_projectile.glb"": ""local_hash""
      }
    },
	""ribbon_textures"": {
	  ""void_whisper_shadow_veil.png"": ""local_hash""
    }
  }
}";
		File.WriteAllText(Path.Combine(tempMapDir, "metadata.json"), metadataJson);

		// 4. Write MapScript.cs that spawns the 2 enemy units
		string mapScript = @"
namespace Realm.Maps;

using Realm.MapAPI;
using System.Numerics;

public class CustomProjMap : IWasmModule
{
    public void Initialize(IGameAPI api)
    {
        // Unit 1: Ranged attacker (Player, isEnemy = false)
		var caster = api.SpawnUnit(""fire_orb_mage"", new Vector3(-8f, 0f, 0f), false);
        // Unit 2: Enemy target dummy (Enemy, isEnemy = true)
		var dummy = api.SpawnUnit(""enemy_training_dummy"", new Vector3(8f, 0f, 0f), true);
		api.BroadcastMessage(""units_spawned"");
    }

    public void Update(IGameAPI api, float delta)
    {
    }
}";
		File.WriteAllText(Path.Combine(tempMapDir, "MapScript.cs"), mapScript);

		// 5. Compile to WASM using wasi-sdk
		var compileProcess = new System.Diagnostics.Process();
		string resolvedWasiSdk = WasiSdkResolver.ResolveWasiSdkPath();
		compileProcess.StartInfo.FileName = "dotnet";
		compileProcess.StartInfo.Arguments = $"publish \"CustomProjMap.csproj\" -c Release -r wasi-wasm -p:WASI_SDK_PATH=\"{resolvedWasiSdk}\"";
		compileProcess.StartInfo.EnvironmentVariables["WASI_SDK_PATH"] = resolvedWasiSdk;
		compileProcess.StartInfo.WorkingDirectory = tempMapDir;
		compileProcess.StartInfo.CreateNoWindow = true;
		compileProcess.StartInfo.UseShellExecute = false;
		compileProcess.Start();
		compileProcess.WaitForExit();
		if (compileProcess.ExitCode != 0)
		{
			throw new System.Exception($"Wasm compilation failed (exit code {compileProcess.ExitCode})");
		}

		string wasmPath = Directory.GetFiles(Path.Combine(tempMapDir, "bin"), "*.wasm", SearchOption.AllDirectories).OrderByDescending(f => File.GetLastWriteTimeUtc(f)).FirstOrDefault();
		Assertions.AssertThat(!string.IsNullOrEmpty(wasmPath) && File.Exists(wasmPath)).IsTrue();

		// 6. Configure LobbyManager & GameHost to launch game with custom map
		LobbyManager.Instance.IsSinglePlayer = true;
		PropertyInfo isHostProp = typeof(LobbyManager).GetProperty("IsHost", BindingFlags.Public | BindingFlags.Instance);
		isHostProp?.SetValue(LobbyManager.Instance, true);
		LobbyManager.Instance.IsGameStarted = true;
		LobbyManager.Instance.ActiveMapName = tempMapDir;
		Realm.Client.Core.GameHost.PendingMapScriptPath = wasmPath;

		LobbyManager.Instance.PlayerList.Clear();
		LobbyManager.PlayerInfo playerInfo = new LobbyManager.PlayerInfo
		{
			PeerId = 1,
			Slot = 0,
			Name = LobbyManager.Instance.AuthenticatedUsername,
			Faction = "HUMAN",
			Team = "Team 1",
			Color = new global::Godot.Color(0.2f, 0.6f, 1.0f),
			IsHost = true,
			BinaryVersion = RealmVersion.GameBinaryVersion
		};
		PropertyInfo localPlayerProp = typeof(LobbyManager).GetProperty("LocalPlayer", BindingFlags.Public | BindingFlags.Instance);
		localPlayerProp?.SetValue(LobbyManager.Instance, playerInfo);
		LobbyManager.Instance.PlayerList.Add(playerInfo);

		// 7. Launch Main.tscn Scene Runner
		ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
		await runner.AwaitMillis(1500);

		GameHost gameHost = Realm.Client.Core.GameHost.Instance;
		Assertions.AssertThat(gameHost).IsNotNull();

		var sw = new StringWriter();
		sw.WriteLine("=== CUSTOM WEAPON PROJECTILE DIAGNOSTICS ===");
		sw.WriteLine($"UnitRegistry Count: {Realm.Client.Core.GameHost.UnitRegistry.Count}");
		sw.WriteLine($"WeaponRegistry Count: {Realm.Client.Core.GameHost.WeaponRegistry.Count}");
		sw.WriteLine($"Has fire_orb_mage in UnitRegistry: {Realm.Client.Core.GameHost.UnitRegistry.ContainsKey("fire_orb_mage")}");
		sw.WriteLine($"Has spiked_fireball_weapon in WeaponRegistry: {Realm.Client.Core.GameHost.WeaponRegistry.ContainsKey("spiked_fireball_weapon")}");

		Assertions.AssertThat(Realm.Client.Core.GameHost.UnitRegistry.ContainsKey("fire_orb_mage")).IsTrue();
		Assertions.AssertThat(Realm.Client.Core.GameHost.WeaponRegistry.ContainsKey("spiked_fireball_weapon")).IsTrue();

		var registeredWeapon = Realm.Client.Core.GameHost.WeaponRegistry["spiked_fireball_weapon"];
		sw.WriteLine($"Weapon Name: {registeredWeapon.Name}");
		sw.WriteLine($"Weapon ProjectileModelPath: {registeredWeapon.ProjectileModelPath}");
		sw.WriteLine($"Weapon ShaderEffectType: {registeredWeapon.ShaderEffectType}");
		sw.WriteLine($"Weapon EmissionColor: {registeredWeapon.EmissionColor}");
		sw.WriteLine($"Weapon RibbonTexture: {registeredWeapon.RibbonTexture}");

		// Wait for combat ticks and projectile to spawn
		bool projectileObserved = false;
		VisualProjectile3D activeProjectile = null;

		for (int frame = 0; frame < 30; frame++)
		{
			await runner.AwaitMillis(100);

			var pool = VisualProjectilePool.Instance;
			foreach (var proj in pool.AllProjectiles)
			{
				if (proj != null && proj.IsActive)
				{
					projectileObserved = true;
					activeProjectile = proj;
					break;
				}
			}

			if (projectileObserved) break;
		}

		sw.WriteLine($"Projectile Observed during combat: {projectileObserved}");
		Assertions.AssertThat(projectileObserved).IsTrue();
		Assertions.AssertThat(activeProjectile).IsNotNull();

		if (activeProjectile != null)
		{
			// LAYER 1 DIAGNOSTICS: Mesh Container & Model
			sw.WriteLine("--- LAYER 1: 3D MODEL & MESH CONTAINER ---");
			sw.WriteLine($"MeshContainer visible: {activeProjectile.MeshContainer?.Visible}");
			sw.WriteLine($"CustomModelInstance null: {activeProjectile.CustomModelInstance == null}");
			if (activeProjectile.CustomModelInstance != null)
			{
				sw.WriteLine($"CustomModelInstance Name: {activeProjectile.CustomModelInstance.Name}");
				sw.WriteLine($"CustomModelInstance Child Count: {activeProjectile.CustomModelInstance.GetChildCount()}");
			}
			sw.WriteLine($"FallbackMeshInstance visible: {activeProjectile.FallbackMeshInstance?.Visible}");
			sw.WriteLine($"Projectile Position: {activeProjectile.GlobalPosition}");
			sw.WriteLine($"IsFlying: {activeProjectile.IsFlying}");
			sw.WriteLine($"ElapsedFlightTime: {activeProjectile.ElapsedFlightTime:F2} / {activeProjectile.TotalFlightDuration:F2}");

			// LAYER 2 DIAGNOSTICS: Custom Uber Shader & Parameters
			sw.WriteLine("--- LAYER 2: UBER-SHADER MATERIAL ---");
			var shaderMat = activeProjectile.UberShaderMaterial;
			sw.WriteLine($"UberShaderMaterial null: {shaderMat == null}");
			if (shaderMat != null)
			{
				sw.WriteLine($"Shader Attached: {shaderMat.Shader != null}");
				sw.WriteLine($"base_color: {shaderMat.GetShaderParameter("base_color")}");
				sw.WriteLine($"emission_color: {shaderMat.GetShaderParameter("emission_color")}");
				sw.WriteLine($"emission_energy: {shaderMat.GetShaderParameter("emission_energy")}");
				sw.WriteLine($"fresnel_color: {shaderMat.GetShaderParameter("fresnel_color")}");
				sw.WriteLine($"fresnel_factor: {shaderMat.GetShaderParameter("fresnel_factor")}");
				sw.WriteLine($"noise_scale: {shaderMat.GetShaderParameter("noise_scale")}");
				sw.WriteLine($"threshold_cutoff: {shaderMat.GetShaderParameter("threshold_cutoff")}");
			}

			// LAYER 3 DIAGNOSTICS: Ribbon Trail Emitter & Material
			sw.WriteLine("--- LAYER 3: RIBBON TRAIL EMITTER ---");
			var trail = activeProjectile.TrailEmitter;
			var ribbonMat = activeProjectile.RibbonMaterial;
			sw.WriteLine($"TrailEmitter null: {trail == null}");
			sw.WriteLine($"TrailEmitter Emitting: {trail?.Emitting}");
			sw.WriteLine($"RibbonMaterial null: {ribbonMat == null}");
			if (ribbonMat != null)
			{
				sw.WriteLine($"Ribbon AlbedoColor: {ribbonMat.AlbedoColor}");
				sw.WriteLine($"Ribbon Emission: {ribbonMat.Emission}");
				sw.WriteLine($"Ribbon AlbedoTexture null: {ribbonMat.AlbedoTexture == null}");
				if (ribbonMat.AlbedoTexture != null)
				{
					sw.WriteLine($"Ribbon AlbedoTexture Size: {ribbonMat.AlbedoTexture.GetWidth()}x{ribbonMat.AlbedoTexture.GetHeight()}");
				}
			}
		}

		string tempScreenshotsDir = Path.Combine(Path.GetTempPath(), "Realm_Projectile_Screenshots");
		Directory.CreateDirectory(tempScreenshotsDir);
		string screenshotPath = Path.Combine(tempScreenshotsDir, "custom_projectile_in_flight.png");
		global::Godot.Image screenshot = runner.Scene().GetViewport().GetTexture().GetImage();
		screenshot.SavePng(screenshotPath);
		sw.WriteLine($"Screenshot saved to: {screenshotPath}");

		string diagPath = Path.Combine(tempScreenshotsDir, "projectile_diagnostics.txt");
		File.WriteAllText(diagPath, sw.ToString());
		GD.Print(sw.ToString());

		// Assertions verifying Layer 1, 2, and 3
		Assertions.AssertThat(activeProjectile.MeshContainer).IsNotNull();
		Assertions.AssertThat(activeProjectile.UberShaderMaterial).IsNotNull();
		Assertions.AssertThat(activeProjectile.TrailEmitter).IsNotNull();
		Assertions.AssertThat(activeProjectile.RibbonMaterial).IsNotNull();
		Assertions.AssertThat(activeProjectile.RibbonMaterial.AlbedoTexture).IsNotNull();
	}

	[TestCase]
	public async Task TestCliffDarkReproduction()
	{
		var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
		if (field != null)
		{
			field.SetValue(null, true);
		}

		ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
		await runner.AwaitMillis(1500);

		UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
		await runner.AwaitMillis(2500);

		var hud = MapEditorHUD.Instance;
		Assertions.AssertThat(hud).IsNotNull();

		var overlay = hud!.GetNodeOrNull<Control>("ConfirmationOverlay");
		if (overlay != null)
		{
			var btnDiscard = overlay.FindChild("BtnCancel", true, false) as Button 
				?? overlay.FindChild("*Discard*", true, false) as Button;
			if (btnDiscard != null)
			{
				btnDiscard.EmitSignal("pressed");
			}
			else
			{
				overlay.QueueFree();
			}
			await runner.AwaitMillis(500);
		}

		string mapFolder = @"C:\temp\Cliff_Dark";
		if (!Directory.Exists(mapFolder))
		{
			throw new Exception($"Map folder '{mapFolder}' not found.");
		}

		bool loadOk = hud!.LoadMapFolder(mapFolder);
		Assertions.AssertThat(loadOk).IsTrue();
		await runner.AwaitMillis(1500);

		var gameHost = Realm.Client.Core.GameHost.Instance;
		Assertions.AssertThat(gameHost).IsNotNull();

		var camera = gameHost!.MainCamera;
		if (camera != null)
		{
			camera.Position = new Vector3(0.0f, 30.0f, 35.0f);
			camera.RotationDegrees = new Vector3(-45.0f, 0.0f, 0.0f);
		}
		await runner.AwaitMillis(1000);

		var terrain = gameHost.GroundTerrain;
		Assertions.AssertThat(terrain).IsNotNull();

		var sw = new StringWriter();
		sw.WriteLine("=== CLIFF DIAGNOSTICS ===");
		sw.WriteLine($"Terrain Width: {terrain!.Width}, Depth: {terrain.Depth}");
		sw.WriteLine($"Terrain Material: {terrain.Material != null}");
		if (terrain.Material != null)
		{
			sw.WriteLine($"shroud_enabled: {terrain.Material.GetShaderParameter("shroud_enabled")}");
			sw.WriteLine($"enable_macro_noise: {terrain.Material.GetShaderParameter("enable_macro_noise")}");
			sw.WriteLine($"enable_height_blend: {terrain.Material.GetShaderParameter("enable_height_blend")}");
			sw.WriteLine($"enable_normal_mapping: {terrain.Material.GetShaderParameter("enable_normal_mapping")}");
			sw.WriteLine($"cliff_jitter_strength: {terrain.Material.GetShaderParameter("cliff_jitter_strength")}");

			var swatchAlbedo = terrain.Material.GetShaderParameter("swatch_albedo_params");
			sw.WriteLine($"swatch_albedo_params type: {swatchAlbedo.VariantType}");
			if (swatchAlbedo.Obj is Vector4[] albArr)
			{
				for (int i = 0; i < Math.Min(albArr.Length, 5); i++)
				{
					sw.WriteLine($"  swatch_albedo[{i}]: {albArr[i]}");
				}
			}
			var swatchHeight = terrain.Material.GetShaderParameter("swatch_height_params");
			if (swatchHeight.Obj is Vector4[] hArr)
			{
				for (int i = 0; i < Math.Min(hArr.Length, 5); i++)
				{
					sw.WriteLine($"  swatch_height[{i}]: {hArr[i]}");
				}
			}
			var swatchP = terrain.Material.GetShaderParameter("swatch_params");
			if (swatchP.Obj is Vector4[] pArr)
			{
				for (int i = 0; i < Math.Min(pArr.Length, 5); i++)
				{
					sw.WriteLine($"  swatch_params[{i}]: {pArr[i]}");
				}
			}
		}

		if (terrain.CliffSplatMap != null)
		{
			sw.WriteLine($"CliffSplatMap size: {terrain.CliffSplatMap.GetLength(0)}x{terrain.CliffSplatMap.GetLength(1)}");
			int nonDefaultCount = 0;
			for (int z = 0; z < terrain.CliffSplatMap.GetLength(1); z++)
			{
				for (int x = 0; x < terrain.CliffSplatMap.GetLength(0); x++)
				{
					var s = terrain.CliffSplatMap[x, z];
					if (s.Index0 != 0 || s.Index1 != 0 || s.Weight0 > 0 || s.Weight1 > 0)
					{
						nonDefaultCount++;
					}
				}
			}
			sw.WriteLine($"Non-default cliff splats: {nonDefaultCount}");
			sw.WriteLine($"Sample splats [0,0]: {terrain.CliffSplatMap[0, 0].Index0}:{terrain.CliffSplatMap[0, 0].Weight0}, [16,16]: {terrain.CliffSplatMap[16, 16].Index0}:{terrain.CliffSplatMap[16, 16].Weight0}");
		}

		var overlayAfter = hud.GetNodeOrNull<Control>("ConfirmationOverlay");
		if (overlayAfter != null)
		{
			overlayAfter.QueueFree();
			await runner.AwaitMillis(200);
		}

		string artifactDir = @"C:\Users\devin\.gemini\antigravity-cli\brain\be6525ca-9349-44ef-a3e3-02dba87e5752";
		Directory.CreateDirectory(artifactDir);
		string screenshotPath = Path.Combine(artifactDir, "cliff_dark_test.png");
		global::Godot.Image screenshot = runner.Scene().GetViewport().GetTexture().GetImage();
		screenshot.SavePng(screenshotPath);
		sw.WriteLine($"Screenshot saved to: {screenshotPath}");

		// Sample center pixels where cliff should be visible
		int cliffDarkPixels = 0;
		int totalSampled = 0;
		float totalLuminance = 0f;
		for (int y = screenshot.GetHeight() / 3; y < screenshot.GetHeight() * 2 / 3; y += 4)
		{
			for (int x = screenshot.GetWidth() / 4; x < screenshot.GetWidth() * 3 / 4; x += 4)
			{
				Color pixel = screenshot.GetPixel(x, y);
				float lum = pixel.R * 0.2126f + pixel.G * 0.7152f + pixel.B * 0.0722f;
				totalLuminance += lum;
				totalSampled++;
				if (lum < 0.15f)
				{
					cliffDarkPixels++;
				}
			}
		}
		float avgLum = totalSampled > 0 ? totalLuminance / totalSampled : 0f;
		sw.WriteLine($"Sampled {totalSampled} pixels in center: Avg luminance = {avgLum:F3}, Dark pixels (<0.15) = {cliffDarkPixels} ({cliffDarkPixels * 100f / totalSampled:F1}%)");

		string diagPath = Path.Combine(artifactDir, "cliff_diagnostics.txt");
		File.WriteAllText(diagPath, sw.ToString());
		GD.Print(sw.ToString());

		// Cliff pixels should no longer be pitch black
		Assertions.AssertThat(avgLum).IsGreater(0.18f);
		Assertions.AssertThat(cliffDarkPixels * 100f / totalSampled).IsLess(25.0f);
	}

	private static string FindAssetInWorkspace(string filename)
	{
		string[] candidates = new[]
		{
			Path.GetFullPath(filename),
			Path.GetFullPath(Path.Combine("..", filename)),
			Path.GetFullPath(Path.Combine("..", "..", filename)),
			Path.GetFullPath(Path.Combine("Realm.Client", filename)),
			Path.GetFullPath(Path.Combine("Assets", filename))
		};
		foreach (var c in candidates)
		{
			if (File.Exists(c)) return c;
		}
		return filename;
	}

	[TestCase]
	public void TestIsIgnoredPathFilters()
	{
		Assertions.AssertThat(MapEditorHUD.IsIgnoredPath(".vscode/settings.json")).IsFalse();
		Assertions.AssertThat(MapEditorHUD.IsIgnoredPath("subfolder/.vscode/tasks.json")).IsFalse();
		Assertions.AssertThat(MapEditorHUD.IsIgnoredPath(".vscode/metadata.schema.json")).IsFalse();
		Assertions.AssertThat(MapEditorHUD.IsIgnoredPath("export.rmap")).IsTrue();
		Assertions.AssertThat(MapEditorHUD.IsIgnoredPath("temp.tmp")).IsTrue();
		Assertions.AssertThat(MapEditorHUD.IsIgnoredPath("debug.log")).IsTrue();
		Assertions.AssertThat(MapEditorHUD.IsIgnoredPath("backup.bak")).IsTrue();
		Assertions.AssertThat(MapEditorHUD.IsIgnoredPath("terrain.json")).IsFalse();
		Assertions.AssertThat(MapEditorHUD.IsIgnoredPath("metadata.json")).IsFalse();
		Assertions.AssertThat(MapEditorHUD.IsIgnoredPath("Assets/icons/my_icon.rtex")).IsFalse();
	}
}
