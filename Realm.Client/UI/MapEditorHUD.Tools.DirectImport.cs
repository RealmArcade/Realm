// The editor-extension and direct-import entry points: write a metadata JSON asset, convert a raw PNG
// straight to .rtex with a swatch name, run the model optimizer on a GLB, and import a GLB that an
// external editor handed over. These are called from outside the HUD, so they have no widget state.

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
		private void UpdateMetadataJsonAsset(string category, string fileName, string blake3Hash, string? subCategory = null, int columns = 0, int rows = 0, int targetSlot = -1)
		{
			GameContext.EditorService.UpdateMetadataJsonAssetDomain(category, fileName, blake3Hash, subCategory, columns, rows, targetSlot);
		}

		public void ConvertRawTextureDirect(string rawPngPath, string outputRtexPath, string swatchName)
		{
			try
			{
				if (GameContext.Host != null && GameContext.Host.GroundTerrain != null)
				{
					GameContext.Host.GroundTerrain.ProcessAndSaveRawTexture(rawPngPath, outputRtexPath);
				}

				if (File.Exists(outputRtexPath))
				{
					ReadMetadataAndRefreshTextures();
					ShowFeedback($"Successfully processed & imported RTEX texture for {swatchName}!");
				}
				else
				{
					ShowFeedback($"Failed to generate RTEX texture at {outputRtexPath}");
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"ConvertRawTextureDirect error: {ex.Message}");
				ShowFeedback($"Failed to import texture: {ex.Message}");
			}
		}

		public ModelOptimizerService.OptimizationResult OptimizeAndImportGlbDirect(
			byte[] glbBytes,
			int maxTextureResolution = 1024,
			float creaseAngleDegrees = GlbMeshSmoother.DefaultCreaseAngleDegrees,
			float allowedPixelError = 1.5f,
			bool forceReDecimate = false)
		{
			return GameContext.ModelOptimizerService.OptimizeAndImportGlbDirect(glbBytes, maxTextureResolution, creaseAngleDegrees, allowedPixelError, forceReDecimate);
		}

		public void ImportGlbAssetFromExtension(string sourceFilePath, string category)
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;

				var result = GameContext.EditorService.ImportGlbAssetFromExtensionDomain(sourceFilePath, category, wsPath, (cat, fName, hash, sub) => UpdateMetadataJsonAsset(cat, fName, hash, subCategory: sub));

				if (!result.Success)
				{
					ShowFeedback(string.Format(TranslationServer.Translate("Failed to import GLB asset: {0}"), result.ErrorMessage));
					return;
				}

				_entityPaletteController?.SelectCategory(_entityPaletteController.CurrentCategory, triggerAddObject: false);

				if (result.ExtractedAnimationFiles != null && result.ExtractedAnimationFiles.Count > 0)
				{
					ShowFeedback(string.Format(TranslationServer.Translate("Imported GLB {0} and extracted {1} .ranim animation(s)"), result.FileName, result.ExtractedAnimationFiles.Count));
				}
				else
				{
					ShowFeedback(string.Format(TranslationServer.Translate("Imported GLB asset {0} ({1})"), result.FileName, category));
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"ImportGlbAssetFromExtension error: {ex.Message}");
				ShowFeedback(string.Format(TranslationServer.Translate("Failed to import GLB asset: {0}"), ex.Message));
			}
		}
	}
}
