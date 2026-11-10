using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using NSec.Cryptography;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Services;
using Realm.Shared.Distribution;
using Environment = System.Environment;
using HttpClient = System.Net.Http.HttpClient;
using Key = NSec.Cryptography.Key;
using static Godot.Control;
using Realm.Client.Utils;

namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		private Texture2D? LoadSwatchTextureInternal(int i)
		{
			string wsPath = GameContext.EditorService._editingDirectory;
			if (i >= 0 && i < GameContext.EditorService.Swatches.DisplayNames.Count && GameContext.EditorService.Swatches.DisplayNames[i].EndsWith("(Empty)"))
			{
				return null;
			}

			var (swatchPaths, swatchNames) = (GameContext.EditorService.Swatches.Paths, GameContext.EditorService.Swatches.DisplayNames);
			string localRtex = GameContext.MapStorageService.TryGetLocalRtexPath(i, wsPath, swatchPaths, swatchNames);

			if (File.Exists(localRtex))
			{
				Texture2D? loadedTex = TryLoadRtexTexture(localRtex);
				if (loadedTex != null) return loadedTex;
			}

			if (i >= 0 && i < GameContext.EditorService.Swatches.Paths.Count && ResourceLoader.Exists(GameContext.EditorService.Swatches.Paths[i]))
			{
				return TrimmedTextureLoader.LoadTrimmedTexture(GameContext.EditorService.Swatches.Paths[i]);
			}

			return null;
		}


		private Texture2D? TryLoadRtexTexture(string localRtex)
		{
			try
			{
				byte[]? webpBytes = GameContext.EditorService.GetRtexWebpBytes(localRtex);
				if (webpBytes != null && webpBytes.Length > 0)
				{
					var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
					if (img.LoadWebpFromBuffer(webpBytes) != Error.Ok)
					{
						img.LoadPngFromBuffer(webpBytes);
					}

					return ImageTexture.CreateFromImage(img);
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"Failed to load swatch preview: {ex.Message}");
			}

			return null;
		}

		private void UpdateTextureMetadataAndReload(int index, string cleanBaseName, string outputRtex)
		{
			GameContext.EditorService.UpdateTextureMetadataAndReload(index, cleanBaseName, outputRtex, GameContext.Host, (cat, name, blake, slot) => UpdateMetadataJsonAsset(cat, name, blake, targetSlot: slot));
			SetupTextureSwatches(false);
		}

		public void ImportTextureAssetFromExtension(string sourceFilePath, int slotIndex)
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;

				bool success = GameContext.EditorService.ImportTextureAssetFromExtensionDomain(
					sourceFilePath, slotIndex, wsPath, GameContext.Host,
					out string cleanBaseName, out string outputRtex,
					(cat, name, blake, slot) => UpdateMetadataJsonAsset(cat, name, blake, targetSlot: slot));

				if (success)
				{
					ReadMetadataAndRefreshTextures();
					ShowFeedback(string.Format(TranslationServer.Translate("Successfully imported custom texture for {0}!"), cleanBaseName));
				}
				else
				{
					ShowFeedback(string.Format(TranslationServer.Translate("Failed to generate RTEX texture at {0}"), outputRtex));
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"ImportTextureAssetFromExtension error: {ex.Message}");
				ShowFeedback(string.Format(TranslationServer.Translate("Failed to import texture: {0}"), ex.Message));
			}
		}


		public void ReadMetadataAndRefreshTextures()
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;
				string metadataPath = Path.Combine(wsPath, "metadata.json");

				string texDir = Path.Combine(wsPath, "Assets", "textures");
				Directory.CreateDirectory(texDir);

				if (File.Exists(metadataPath))
				{
					MapWorkspaceService.NormalizeMetadataTextureEntries(wsPath);
				}

				InvalidateMetadataCache();

				Swatches.InvalidateTextureCache();
				if (GameContext.Host != null && GameContext.Host.GroundTerrain != null)
				{
					GameContext.Host.GroundTerrain.ReloadTerrainTextures(true);
				}

				SetupTextureSwatches(false);
				RefreshSkyboxList();
				if (File.Exists(metadataPath))
				{
					GameContext.Host?.LoadModelYOffsetsFromMetadataJson(wsPath);
					GameContext.Host?.LoadUnitMetadata(wsPath);
				}

				_entityPaletteController?.SelectCategory(_entityPaletteController.CurrentCategory, triggerAddObject: false);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"ReadMetadataAndRefreshTextures error: {ex.Message}");
			}
		}

		public void ImportMixamoOrAnimationDialog()
		{
			OpenAssetBrowser("Select Animation (.ranim)", new[] { ".ranim" }, (path, preferredName) => { ImportAnimationAssetFromExtension(path, preferredName); });
		}

		public void ImportAnimationAssetFromExtension(string sourceFilePath, string? preferredFileName = null)
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;
				string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
				string animsDir = GameContext.EditorService.PrepareAnimationsDirectory(wsPath);

				if (ext == ".glb" || ext == ".gltf" || ext == ".fbx")
				{
					Import3DAnimationFile(sourceFilePath, preferredFileName, animsDir);
				}
				else
				{
					ImportRanimFile(sourceFilePath, preferredFileName, animsDir);
				}
			}
			catch (Exception ex)
			{
				GameLog.LogError($"ImportAnimationAssetFromExtension error: {ex.Message}");
				ShowFeedback(string.Format(TranslationServer.Translate("Failed to import animation: {0}"), ex.Message));
			}
		}

		private void Import3DAnimationFile(string sourceFilePath, string? preferredFileName, string animsDir)
		{
			string originalFileName = ResolveAnimationFileName(sourceFilePath, preferredFileName);
			var result = GameContext.EditorService.Import3DAnimationFileDomain(sourceFilePath, originalFileName, animsDir);

			if (result.ImportedCount == 0 && result.SkippedCount == 0)
			{
				ShowFeedback(TranslationServer.Translate("No animations found in file."));
				return;
			}

			if (result.NewAnims != null)
			{
				foreach (var anim in result.NewAnims)
				{
					UpdateMetadataJsonAsset("animations", anim.FileName, anim.Blake3);
				}
			}

			if (result.ImportedCount > 0)
			{
				ShowFeedback(string.Format(TranslationServer.Translate("Successfully imported {0} animation(s) (.ranim)!"), result.ImportedCount));
			}
			else
			{
				ShowFeedback(TranslationServer.Translate("Animation already imported (identical BLAKE3 hash)."));
			}
		}

		private string ResolveAnimationFileName(string sourceFilePath, string? preferredFileName)
		{
			return GameContext.EditorService.ResolveAnimationFileName(sourceFilePath, preferredFileName);
		}

		private void ImportRanimFile(string sourceFilePath, string? preferredFileName, string animsDir)
		{
			var result = GameContext.EditorService.ImportRanimFileDomain(sourceFilePath, preferredFileName, animsDir);
			if (result.Success)
			{
				UpdateMetadataJsonAsset("animations", result.FinalFileName, result.NewHash);
				ShowFeedback(string.Format(TranslationServer.Translate("Imported animation {0}"), result.FinalFileName));
			}
			else
			{
				UpdateMetadataJsonAsset("animations", result.FinalFileName, result.NewHash);
				ShowFeedback(TranslationServer.Translate("Animation already imported (identical BLAKE3 hash)."));
			}
		}

		public void ImportDecalAssetFromExtension(string sourceFilePath)
		{
			string wsPath = GameContext.EditorService._editingDirectory;

			var result = GameContext.EditorService.ImportDecalAssetDomain(sourceFilePath, wsPath);
			if (result.Success)
			{
				UpdateMetadataJsonAsset("decals", result.FileName, result.Blake3);
				ShowFeedback($"Imported decal {result.FileName}");
			}
			else
			{
				GameLog.LogError($"ImportDecalAssetFromExtension error: {result.Message}");
				ShowFeedback($"Failed to import decal: {result.Message}");
			}
		}

		public void ImportSkyboxAssetFromExtension(string sourceFilePath)
		{
			string wsPath = GameContext.EditorService._editingDirectory;
			var result = GameContext.EditorService.ImportSkyboxAsset(sourceFilePath, wsPath);
			if (result.Success)
			{
				UpdateMetadataJsonAsset("skyboxes", result.FileName, result.Blake3);
				RefreshSkyboxList();
				ShowFeedback($"Imported skybox {result.FileName}");
			}
			else
			{
				GameLog.LogError($"ImportSkyboxAssetFromExtension error: {result.Message}");
				ShowFeedback($"Failed to import skybox: {result.Message}");
			}
		}

		public void ImportSpritesheetAssetFromExtension(string sourceFilePath, int columns = 4, int rows = 4)
		{
			string wsPath = GameContext.EditorService._editingDirectory;
			var result = GameContext.EditorService.ImportSpritesheetAsset(sourceFilePath, wsPath);
			if (result.Success)
			{
				UpdateMetadataJsonAsset("vfx_spritesheets", result.FileName, result.Blake3, columns: columns, rows: rows);
				ShowFeedback($"Imported VFX spritesheet {result.FileName}");
			}
			else
			{
				GameLog.LogError($"ImportSpritesheetAssetFromExtension error: {result.Message}");
				ShowFeedback($"Failed to import VFX spritesheet: {result.Message}");
			}
		}

		public void ImportIconAssetFromExtension(string sourceFilePath)
		{
			string wsPath = GameContext.EditorService._editingDirectory;
			var result = GameContext.EditorService.ImportIconAsset(sourceFilePath, wsPath);
			if (result.Success)
			{
				UpdateMetadataJsonAsset("icons", result.FileName, result.Blake3);
				ShowFeedback($"Imported 2D Icon {result.FileName}");
			}
			else
			{
				GameLog.LogError($"ImportIconAssetFromExtension error: {result.Message}");
				ShowFeedback($"Failed to import 2D Icon: {result.Message}");
			}
		}

		public void ImportAudioAssetFromExtension(string sourceFilePath, string audioType)
		{
			string wsPath = GameContext.EditorService._editingDirectory;

			var result = GameContext.AudioService.ImportAudioAsset(sourceFilePath, audioType, wsPath);

			if (result.Success)
			{
				UpdateMetadataJsonAsset(result.AudioCategory, result.FileName, result.Blake3);
				ShowFeedback($"Imported audio {result.FileName} ({audioType})");
			}
			else
			{
				GameLog.LogError($"ImportAudioAssetFromExtension error: {result.Message}");
				ShowFeedback($"Failed to import audio asset: {result.Message}");
			}
		}
	}
}
