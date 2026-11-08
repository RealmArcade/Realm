using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Services;

/// <summary>
/// The editor's user-facing text: the tool names and the status line under the cursor. Kept apart from
/// the editor itself because it is presentation, and because the status line is the only place that has
/// to explain a pathing cell's layers.
/// </summary>
public static class EditorToolLabels
{
	public static string GetLocalizedToolName(EditorTool tool)
	{
		return tool switch
		{
			EditorTool.None => TranslationServer.Translate("NONE"),
			EditorTool.Raise => TranslationServer.Translate("RAISE"),
			EditorTool.Lower => TranslationServer.Translate("LOWER"),
			EditorTool.Height => TranslationServer.Translate("HEIGHT"),
			EditorTool.Smooth => TranslationServer.Translate("SMOOTH"),
			EditorTool.Plateau => TranslationServer.Translate("PLATEAU"),
			EditorTool.PaintTexture => TranslationServer.Translate("PAINT TEXTURE"),
			EditorTool.PlaceUnit => TranslationServer.Translate("PLACE UNIT"),
			EditorTool.PlaceProp => TranslationServer.Translate("PLACE PROP"),
			EditorTool.PlaceDecal => TranslationServer.Translate("PLACE DECAL"),
			EditorTool.PlaceVfx => TranslationServer.Translate("PLACE VFX"),
			EditorTool.DeleteObject => TranslationServer.Translate("DELETE OBJECT"),
			EditorTool.SelectMove => TranslationServer.Translate("SELECT / MOVE"),
			EditorTool.Eyedropper => TranslationServer.Translate("EYEDROPPER"),
			EditorTool.Noise => TranslationServer.Translate("NOISE"),
			EditorTool.Ramp => TranslationServer.Translate("RAMP"),
			EditorTool.PlacePropClump => TranslationServer.Translate("PLACE CLUMP"),
			EditorTool.FloodFill => TranslationServer.Translate("FLOOD FILL"),
			EditorTool.SelectArea => TranslationServer.Translate("SELECT AREA"),
			EditorTool.PasteArea => TranslationServer.Translate("PASTE AREA"),
			EditorTool.PaintPathing => TranslationServer.Translate("PAINT PATHING"),
			EditorTool.FloodFillPathing => TranslationServer.Translate("FLOOD FILL PATHING"),
			EditorTool.DrawCoordinate => TranslationServer.Translate("DRAW COORD"),
			EditorTool.Water => TranslationServer.Translate("WATER"),
			EditorTool.Measure => TranslationServer.Translate("MEASURE"),
			_ => tool.ToString()
		};
	}

	public static string GetTerrainStatusString(Vector3 pos, EditorTool tool, string activePlaceId)
	{
		string formattedToolName = GetLocalizedToolName(tool);
		if (!string.IsNullOrEmpty(activePlaceId))
		{
			string shortId = System.IO.Path.GetFileName(activePlaceId).ToUpper();
			formattedToolName += $" ({shortId})";
		}

		string status = string.Format(TranslationServer.Translate("ACTIVE TOOL: {0} | Pos: {1}, {2}, {3}"), formattedToolName, pos.X.ToString("F1"), pos.Z.ToString("F1"), pos.Y.ToString("F1"));

		ref var terrain = ref EditorService.GetTerrainState();

		if (terrain.Cells != null && tool == EditorTool.PaintPathing)
		{
			float fx = pos.X / terrain.QuadSize + terrain.Width / 2.0f;
			float fz = pos.Z / terrain.QuadSize + terrain.Depth / 2.0f;
			int cx = Mathf.Clamp((int)Mathf.Round(fx), 0, terrain.Width - 1);
			int cz = Mathf.Clamp((int)Mathf.Round(fz), 0, terrain.Depth - 1);

			if (terrain.PathingCodes != null)
			{
				int code = terrain.PathingCodes[cx, cz];
				var layers = new List<string>();
				if ((code & EditableTerrain.PATHING_GROUND) != 0)
				{
					layers.Add(TranslationServer.Translate("Ground"));
				}

				if ((code & EditableTerrain.PATHING_FLYING) != 0)
				{
					layers.Add(TranslationServer.Translate("Flying"));
				}

				if ((code & EditableTerrain.PATHING_SHALLOW_WATER) != 0)
				{
					layers.Add(TranslationServer.Translate("Shallow Water"));
				}

				if ((code & EditableTerrain.PATHING_DEEP_WATER) != 0)
				{
					layers.Add(TranslationServer.Translate("Deep Water"));
				}

				if ((code & EditableTerrain.PATHING_BUILDABLE) != 0)
				{
					layers.Add(TranslationServer.Translate("Buildable"));
				}

				string layersStr = layers.Count > 0 ? string.Join(", ", layers) : TranslationServer.Translate("None");
				status += $" | {TranslationServer.Translate("Path")}: {layersStr}";
			}
		}

		return status;
	}
}
