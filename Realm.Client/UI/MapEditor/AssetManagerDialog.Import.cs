using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.EditorAPI;
using Realm.Shared.Audio;
using Realm.Shared.Textures;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

public partial class AssetManagerDialog
{
	private void OpenImportFileDialog()
	{
		if (!AssetPackService.IsInstalled())
		{
			ShowAssetPackDownloadPrompt();
			return;
		}

		OpenAssetBrowserForImport();
	}

	private void ShowAssetPackDownloadPrompt()
	{
		GameContext.UserInterface?.ShowConfirmationDialog(
			TranslationServer.Translate("The editor art assets (models, textures, audio and effects) are published as a separate ASSET_PACK download. Download the newest version now to import them into this map."),
			onConfirm: StartAssetPackDownload,
			confirmText: TranslationServer.Translate("DOWNLOAD ASSET_PACK"),
			cancelText: TranslationServer.Translate("NOT NOW"),
			onCancel: OpenAssetBrowserForImport,
			showCancel: true,
			titleOverride: TranslationServer.Translate("Download Editor Art Assets?"));
	}

	// Passed as the confirmation dialog's onConfirm callback, so the signature has to stay void. The download had no
	// catch, and a fault left _assetPackDownloadRunning true for the rest of the session while the import button
	// stayed disabled, with nothing in the log to say why.
	private async void StartAssetPackDownload()
	{
		try
		{
			await DownloadAssetPackAsync();
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[AssetManagerDialog] Downloading the ASSET_PACK failed: {exception}");
		}
	}

	private async Task DownloadAssetPackAsync()
	{
		if (_assetPackDownloadRunning) return;

		_assetPackDownloadRunning = true;
		_btnImportAsset.Disabled = true;
		UpdateAssetPackStatus(TranslationServer.Translate("Searching the server for the newest ASSET_PACK version..."), UIStyle.ColorGoldDull);

		bool downloaded = await AssetPackService.DownloadLatestAsync(progress =>
		{
			int percent = (int)MathF.Round(progress * 100.0f);
			string progressText = string.Format(TranslationServer.Translate("Downloading ASSET_PACK... {0}%"), percent);
			Callable.From(() => UpdateAssetPackStatus(progressText, UIStyle.ColorCyanGlow)).CallDeferred();
		});

		_assetPackDownloadRunning = false;
		if (!GodotObject.IsInstanceValid(this)) return;

		_btnImportAsset.Disabled = false;

		if (!downloaded)
		{
			UpdateAssetPackStatus(TranslationServer.Translate("ASSET_PACK could not be downloaded. Check your internet connection and try again."), new Color(0.9f, 0.3f, 0.3f));
			return;
		}

		UpdateAssetPackStatus(TranslationServer.Translate("ASSET_PACK downloaded. Refreshing the asset index..."), UIStyle.ColorCyanGlow);
		RefreshAssetList();
		OpenAssetBrowserForImport();
	}

	private void UpdateAssetPackStatus(string statusText, Color statusColor)
	{
		if (_lblAssetPackStatus == null || !GodotObject.IsInstanceValid(_lblAssetPackStatus)) return;

		_lblAssetPackStatus.Text = statusText;
		_lblAssetPackStatus.AddThemeColorOverride("font_color", statusColor);
		_lblAssetPackStatus.Visible = true;
	}

	private void OpenAssetBrowserForImport()
	{
		string[] extensions = _selectedAssetType switch
		{
			"Character" or "Building" or "Prop" or "Item" => new[] { ".rmesh" },
			"Terrain" or "Spritesheet" or "vfx_radial" or "vfx_vertical" or "Icon" or "Decal" or "Ribbon" or "Noise" or "Skybox" => new[] { ".rtex" },
			"Animation" => new[] { ".ranim" },
			"SoundEffect" or "Music" => new[] { ".raud" },
			_ => new[] { ".rmesh", ".rtex", ".ranim", ".raud" }
		};

		string? requiredType = _selectedAssetType == "All" ? null : _selectedAssetType;
		Hud?.OpenAssetBrowser($"Import Asset ({_selectedAssetType})", extensions, (IReadOnlyList<AssetBrowserSelection> selections) => OnImportFilesSelected(selections), requireRealmMetadata: true, requiredAssetType: requiredType);
	}

	private enum AssetImportOutcome
	{
		Imported,
		Skipped,
		Conflict,
		Failed
	}

	private void OnImportFilesSelected(IReadOnlyList<AssetBrowserSelection> selections)
	{
		if (selections == null || selections.Count == 0) return;

		if (selections.Count == 1)
		{
			OnImportFileSelected(selections[0].FilePath, selections[0].PreferredFileName);
			return;
		}

		var importedNames = new List<string>();
		int skippedCount = 0;
		int conflictCount = 0;
		int failedCount = 0;

		foreach (var selection in selections)
		{
			switch (OnImportFileSelected(selection.FilePath, selection.PreferredFileName, showFeedback: false))
			{
				case AssetImportOutcome.Imported:
					importedNames.Add(DetermineImportFileName(selection.FilePath, selection.PreferredFileName));
					break;
				case AssetImportOutcome.Skipped:
					skippedCount++;
					break;
				case AssetImportOutcome.Conflict:
					conflictCount++;
					break;
				default:
					failedCount++;
					break;
			}
		}

		RefreshAssetList();

		string summary = conflictCount > 0
			? string.Format(
				TranslationServer.Translate("Imported {0} assets, skipped {1} already present, refused {2} that would overwrite a different file, {3} failed."),
				importedNames.Count,
				skippedCount,
				conflictCount,
				failedCount)
			: string.Format(
				TranslationServer.Translate("Imported {0} assets, skipped {1} already present, {2} failed."),
				importedNames.Count,
				skippedCount,
				failedCount);

		Hud?.ShowFeedback(summary);
	}

	private AssetImportOutcome OnImportFileSelected(string sourceFilePath, string? preferredFileName = null, bool showFeedback = true)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return AssetImportOutcome.Failed;

		string wsPath = GetWorkspacePath();
		string fileName = DetermineImportFileName(sourceFilePath, preferredFileName);
		string ext = Path.GetExtension(fileName).ToLowerInvariant();

		try
		{
			string targetCategory = DetermineImportTargetCategory(sourceFilePath, ext);
			string subFolder = GetAssetSubFolder(targetCategory, targetCategory.ToLowerInvariant());

			string destDir = Path.Combine(wsPath, "Assets", subFolder);
			Directory.CreateDirectory(destDir);
			string destPath = Path.Combine(destDir, fileName);

			string sourceHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(sourceFilePath), ext);
			if (File.Exists(destPath))
			{
				if (string.Equals(RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(destPath), ext), sourceHash, StringComparison.OrdinalIgnoreCase))
				{
					return AssetImportOutcome.Skipped;
				}

				GD.PushWarning($"[AssetManagerDialog] Refused to import '{fileName}': '{destPath}' already exists with different content.");
				if (showFeedback)
				{
					Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Asset '{0}' already exists with different content. Delete or rename it before importing."), fileName));
				}

				return AssetImportOutcome.Conflict;
			}

			File.Copy(sourceFilePath, destPath, false);
			RealmMetadataHelper.EnsureMetadata(destPath);

			byte[] finalBytes = File.ReadAllBytes(destPath);
			string hash = RealmMetadataHelper.ComputeBlake3(finalBytes, ext);

			MapAssetHelper.UpdateManifestAsset(wsPath, targetCategory, fileName, hash);
			EnsureTemplateForAsset(wsPath, targetCategory, fileName, hash);

			if (showFeedback)
			{
				Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported asset '{0}' into {1}"), fileName, targetCategory));
				RefreshAssetListAndSelect(fileName);
			}

			return AssetImportOutcome.Imported;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetManagerDialog] OnImportFileSelected error: {ex.Message}");
			if (showFeedback)
			{
				Hud?.ShowFeedback($"Error importing asset: {ex.Message}");
			}

			return AssetImportOutcome.Failed;
		}
	}

	private string DetermineImportFileName(string sourceFilePath, string? preferredFileName)
	{
		return !string.IsNullOrWhiteSpace(preferredFileName) && !AssetIndexService.IsHexHash(Path.GetFileNameWithoutExtension(preferredFileName))
			? preferredFileName
			: (GameContext.AssetIndexService?.ResolvePrettyFileName(sourceFilePath, preferredFileName) ?? Path.GetFileName(sourceFilePath));
	}

	private string DetermineImportTargetCategory(string sourceFilePath, string ext)
	{
		string? embeddedAssetType = RealmMetadataHelper.ExtractAssetType(sourceFilePath);
		if (!string.IsNullOrEmpty(embeddedAssetType))
		{
			return MapAssetHelper.NormalizeCategoryKey(embeddedAssetType);
		}

		if (_selectedAssetType != "All")
		{
			return _selectedAssetType;
		}

		return ext switch
		{
			".rmesh" => "Prop",
			".rtex" => "Terrain",
			".ranim" => "Animation",
			".raud" => "SoundEffect",
			_ => "Prop"
		};
	}

	private void OnImportShaderPressed()
	{
		var err = DisplayServer.FileDialogShow(
			TranslationServer.Translate("Select Shader File to Import"),
			PathUtils.GetProjectRoot(),
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			new[] { "*.json,*.gdshader,*.shader ; Supported Shader Files (*.json, *.gdshader, *.shader)", "*.json ; JSON Shader Files (*.json)", "*.gdshader,*.shader ; Godot Shader Files (*.gdshader, *.shader)" },
			Callable.From((bool status, string[] selectedPaths, int selectedFilterIndex) =>
			{
				if (status && selectedPaths.Length > 0)
				{
					ImportShaderFromFile(selectedPaths[0]);
				}
			})
		);

		if (err != Error.Ok)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Failed to show file dialog"));
		}
	}

	private void ImportShaderFromFile(string sourceFilePath)
	{
		if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;

		string wsPath = GetWorkspacePath();
		string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();

		try
		{
			if (ext == ".json")
			{
				ImportShaderFromJson(sourceFilePath, wsPath);
				return;
			}

			if (ext is ".gdshader" or ".shader")
			{
				ImportGdShader(sourceFilePath, wsPath);
				return;
			}

			Hud?.ShowFeedback(TranslationServer.Translate("Unrecognized shader file format."));
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetManagerDialog] ImportShaderFromFile error: {ex.Message}");
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Error importing shader: {0}"), ex.Message));
		}
	}

	private void ImportShaderFromJson(string sourceFilePath, string wsPath)
	{
		string jsonText = File.ReadAllText(sourceFilePath);
		var rootNode = JsonNode.Parse(jsonText);
		if (rootNode is not JsonObject rootObj) return;

		if (rootObj.TryGetPropertyValue("shaders", out var shadersNode) && shadersNode is JsonObject shadersObj)
		{
			ImportMultipleShadersFromJson(shadersObj, wsPath);
			return;
		}

		if (rootObj.ContainsKey("transition_mode") || rootObj.ContainsKey("edge_color") || rootObj.ContainsKey("name") || rootObj.ContainsKey("TransitionMode"))
		{
			ImportSingleShaderFromJson(rootObj, sourceFilePath, wsPath);
		}
	}

	private void ImportMultipleShadersFromJson(JsonObject shadersObj, string wsPath)
	{
		int count = 0;
		string lastKey = "";
		foreach (var kvp in shadersObj)
		{
			if (kvp.Value == null) continue;

			var config = CustomShaderConfig.FromJson(kvp.Key, kvp.Value);
			SpawnDeathShaderManager.SaveCustomShader(config, wsPath);
			EnsureTemplateForAsset(wsPath, "SpawnShader", config.Key, string.Empty);
			lastKey = config.Key;
			count++;
		}

		RefreshAssetListAndSelect(lastKey);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported {0} shaders successfully."), count));
	}

	private void ImportSingleShaderFromJson(JsonObject rootObj, string sourceFilePath, string wsPath)
	{
		string defaultKey = Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(" ", "_");
		string key = rootObj.TryGetPropertyValue("key", out var keyNode) && !string.IsNullOrWhiteSpace(keyNode?.ToString())
			? keyNode.ToString()
			: (rootObj.TryGetPropertyValue("Key", out var keyNodeCap) && !string.IsNullOrWhiteSpace(keyNodeCap?.ToString()) ? keyNodeCap.ToString() : defaultKey);

		var config = CustomShaderConfig.FromJson(key, rootObj);
		SpawnDeathShaderManager.SaveCustomShader(config, wsPath);
		EnsureTemplateForAsset(wsPath, "SpawnShader", config.Key, string.Empty);
		RefreshAssetListAndSelect(config.Key);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported shader '{0}' successfully."), config.Name));
	}

	private void ImportGdShader(string sourceFilePath, string wsPath)
	{
		string fileName = Path.GetFileName(sourceFilePath);
		string targetDir = Path.Combine(wsPath, "Assets", "shaders");
		Directory.CreateDirectory(targetDir);
		string destPath = Path.Combine(targetDir, fileName);
		File.Copy(sourceFilePath, destPath, true);

		EnsureTemplateForAsset(wsPath, "Shader", fileName, string.Empty);
		RefreshAssetListAndSelect(fileName);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Imported shader '{0}' successfully."), fileName));
	}
}
