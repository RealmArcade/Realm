using System.Collections.Generic;
using Realm.Client.Core;

namespace Realm.Client.Services;

/// <summary>
/// The editor's tool catalogue: the blurb each tool shows while it is active, the paste anchors a copied area can be
/// pinned to, the spoke counts the symmetry overlay offers, and which tools paint a texture swatch.
/// </summary>
public static class EditorToolCatalog
{
	public static readonly string[] PasteAnchorNames = new string[]
	{
		"CENTER",
		"TOP-LEFT",
		"TOP-RIGHT",
		"BOTTOM-RIGHT",
		"BOTTOM-LEFT"
	};

	public static readonly Dictionary<EditorTool, string> ToolInfoTexts = new()
	{
		{ EditorTool.Raise, "TOOL: Raise Heights\n\nDrag left click on the map ground to elevate terrain. Adjust size and strength in settings." },
		{ EditorTool.Ramp, "TOOL: Ramping\n\nLeft-click once on the terrain to set the Ramp Start Point. Left-click again to set the Ramp End Point. The tool will smoothly interpolate heights between the two points. Press Right-click or Escape to cancel." },
		{ EditorTool.Lower, "TOOL: Lower Heights\n\nDrag left click on the map ground to depress terrain. Adjust size and strength in settings." },
		{ EditorTool.Height, "TOOL: Exact Height\n\nDrag left click on the map ground to set terrain to exact block height. Adjust height in settings." },
		{ EditorTool.Plateau, "TOOL: Plateau\n\nDrag left click to flatten terrain to the elevation of your initial click point." },
		{ EditorTool.Smooth, "TOOL: Smooth Terrain\n\nDrag left click to average neighbor vertex heights and smooth out rugged elevations." },
		{ EditorTool.PaintTexture, "TOOL: Texture Painting\n\nDrag left click to paint texture layers onto the vertices of the terrain mesh." },
		{ EditorTool.FloodFill, "TOOL: Flood Fill\n\nClick once on the terrain map to flood-fill an area sharing the same texture color until hitting a boundary (cliff or different texture). Uses selected texture swatch." },
		{ EditorTool.SelectArea, "TOOL: Area Select\n\nDrag left click to select a rectangular area of the map. Press Ctrl+C to copy the area." },
		{ EditorTool.PasteArea, "TOOL: Area Paste\n\nClick on the terrain to paste the copied area. Use the Affected Layers checkboxes to filter what is pasted (Textures, HeightMap, Units / Props, Pathing)." },
		{ EditorTool.PlaceDecal, "TOOL: Place Decal\n\nLeft-click on the ground to project a decorative decal. Snapping, scaling, and rotation apply." },
		{ EditorTool.DeleteObject, "TOOL: Object Eraser\n\nLeft-click directly on any unit or prop in 3D scene to erase and remove it from the map." },
		{ EditorTool.SelectMove, "TOOL: Select / Move\n\nLeft-click directly on any unit, prop, or decal to select it. Hold and drag left click to move it. Drag on empty ground to marquee-select every object whose origin falls inside the rectangle. Shift adds to the selection and Ctrl toggles it. Use Alt + MouseWheel to scale, or Delete to delete." },
		{ EditorTool.Eyedropper, "TOOL: Eyedropper / Picker\n\nLeft-click directly on any unit, prop, or decal to copy and select it as the active placement tool. Click on terrain to copy its texture color, or hold Shift to copy its height." },
		{ EditorTool.Noise, "TOOL: Roughen Terrain\n\nDrag left-click to apply random height variations/noise to ruggedize the terrain surface. Adjust size and strength in settings." },
		{ EditorTool.Water, "TOOL: Water Flood Fill\n\nClick on terrain to flood-fill water bounded by cliff walls. Uses selected Water Profile or toggles water." },
		{ EditorTool.PaintPathing, "TOOL: Pathing Layer Painting\n\nDrag left click to paint pathing properties (ground, flying, water, etc.) onto the map. Use checkboxes to select layers, and Mode to Add/Remove." },
		{ EditorTool.FloodFillPathing, "TOOL: Flood Fill Pathing\n\nClick once on the terrain map to flood-fill pathing properties (ground, flying, water, etc.) across an area sharing the same texture color until hitting a boundary. Use checkboxes to select layers, and Mode to Add/Remove." },
		{ EditorTool.Measure, "TOOL: Tape Measure\n\nClick on the terrain to set measurement origin. Move the cursor or click again to lock the endpoint. Displays live Euclidean distance, Manhattan distance, grid delta, and slope." },
		{ EditorTool.None, "Select a tool from the panels to begin terrain modification." }
	};

	public static readonly int[] SpokeCountOptions = new[] { 2, 3, 4, 5, 6, 8, 12, 16 };

	public static bool IsSwatchCompatibleTool(EditorTool tool) => tool switch
	{
		EditorTool.Raise => true,
		EditorTool.Lower => true,
		EditorTool.Height => true,
		EditorTool.Smooth => true,
		EditorTool.Plateau => true,
		EditorTool.Noise => true,
		EditorTool.Ramp => true,
		EditorTool.Water => true,
		EditorTool.PaintTexture => true,
		EditorTool.FloodFill => true,
		EditorTool.Eyedropper => true,
		EditorTool.SelectArea => true,
		_ => false
	};
}
