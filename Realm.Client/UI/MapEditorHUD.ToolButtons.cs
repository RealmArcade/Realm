using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using NSec.Cryptography;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.Services.Publishing;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = System.Environment;
using HttpClient = System.Net.Http.HttpClient;
using Key = Godot.Key;
using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using TerrainCell = Realm.Ecs.Components.Terrain.TerrainCell;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;

using static Godot.Control;
using Realm.Client.Utils;

namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		private static bool IsSwatchCompatibleTool(EditorTool tool) =>
			EditorToolCatalog.IsSwatchCompatibleTool(tool);

		public void UpdateTextureLabels()
		{
			if (GameContext.Host == null) return;

			EnsureSwatchDataCurrent();

			var (terrainName, cliffName) = GameContext.EditorService.GetTextureNamesDomain();
			int terrainIdx = GameContext.EditorService.Tools.PaintTextureIndex;
			int cliffIdx = GameContext.EditorService.Tools.CliffPaintTextureIndex;

			if (_lblTerrainTexture != null) _lblTerrainTexture.Text = TranslationServer.Translate(terrainName);
			if (_lblCliffTexture != null) _lblCliffTexture.Text = TranslationServer.Translate(cliffName);

			Button? terrainSwatch = Swatches.GetButton(terrainIdx);
			Button? cliffSwatch = Swatches.GetButton(cliffIdx);

			HighlightSwatch(terrainSwatch);
			HighlightCliffSwatch(cliffSwatch);

			if (_btnReplaceTexture != null)
			{
				_btnReplaceTexture.Text = $"{UnicodeIcons.UPLOAD} {TranslationServer.Translate("REPLACE TEXTURE")}";
				_btnReplaceTexture.TooltipText = TranslationServer.Translate("Replace all instances of a texture with another texture across the map");
			}
		}

		private void UpdateBrushSizeVisibility()
		{
			if (_brushPanel?.SizeBox == null || GameContext.Host == null) return;

			var tool = GameContext.Host.ActiveEditorTool;
			_brushPanel.SizeBox.Visible = tool != EditorTool.FloodFill && tool != EditorTool.FloodFillPathing;
		}

		private void UpdateBrushShapeVisibility()
		{
			if (_brushPanel?.ShapeButton == null || GameContext.Host == null) return;

			var tool = GameContext.Host.ActiveEditorTool;
			_brushPanel.ShapeButton.Visible = tool != EditorTool.FloodFill && tool != EditorTool.FloodFillPathing;
		}

		private void UpdateBrushStrengthVisibility()
		{
			if (_brushPanel?.StrengthSlider != null && _brushPanel.StrengthSlider.GetParent() is Control strengthParent)
			{
				if (GameContext.Host == null) return;
				var tool = GameContext.Host.ActiveEditorTool;
				bool blockModeEnabled = (_brushPanel?.BlockModeCheck != null && _brushPanel.BlockModeCheck.ButtonPressed);

				bool isClumpPlacement = tool == EditorTool.PlacePropClump ||
				                        ((tool == EditorTool.PlaceUnit || tool == EditorTool.PlaceProp || tool == EditorTool.PlaceDecal) &&
				                         (_placementPanel?.ClumpModeCheck != null && _placementPanel.ClumpModeCheck.ButtonPressed));

				if (tool == EditorTool.Raise || tool == EditorTool.Lower)
				{
					strengthParent.Visible = !blockModeEnabled;
				}
				else if (tool == EditorTool.Height)
				{
					strengthParent.Visible = false;
				}
				else
				{
					strengthParent.Visible = (tool != EditorTool.PaintPathing &&
					                          tool != EditorTool.FloodFillPathing &&
					                          !isClumpPlacement &&
					                          tool != EditorTool.Plateau &&
					                          tool != EditorTool.Ramp);
				}
			}
		}
	}
}
