// The conversion pipeline, one private step per stage: auto-rig, team-colour masking, the model
// conversion itself, the canonical asset type, the metadata save and the temp-dir cleanup, then the
// finished/error hand-off back to the dialog.

using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Shared;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using Realm.Client.Utils;


namespace Realm.Client.UI.MapEditor;

public partial class ConvertGlbDialog
{
	private void StartConversionTask(string sourcePath, string cleanBase, string fileName, string subCategory, string wsPath, string destPath, bool doAutoRig, bool doTeamColor, bool autoCorrectChromaKey, Color maskColor, string referenceImagePath)
	{
		Task.Run(() =>
		{
			string tempWorkingDir = Path.Combine(Path.GetTempPath(), $"realm_glb_conv_{Guid.NewGuid():N}");
			Directory.CreateDirectory(tempWorkingDir);
			string? errorMessage = null;
			string? resultPath = null;

			try
			{
				string currentPath = Path.Combine(tempWorkingDir, Path.GetFileName(sourcePath));
				File.Copy(sourcePath, currentPath, true);

				if (doAutoRig)
				{
					if (!PerformAutoRigging(ref currentPath, tempWorkingDir, cleanBase, out errorMessage)) return;
				}

				string? chromaKeyHex = null;
				if (doTeamColor)
				{
					if (!PerformTeamColorMasking(ref currentPath, tempWorkingDir, cleanBase, maskColor, autoCorrectChromaKey, doAutoRig, ref chromaKeyHex, out errorMessage)) return;
				}

				if (!PerformModelConversion(currentPath, destPath, subCategory, chromaKeyHex, referenceImagePath, out errorMessage, out byte[]? outputBytes)) return;

				PerformFinalMetadataSave(destPath, fileName, subCategory, wsPath, outputBytes, out resultPath);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[ConvertGlbDialog] Conversion error: {ex.Message}");
				errorMessage = string.Format(TranslationServer.Translate("Conversion error: {0}"), ex.Message);
			}
			finally
			{
				CleanupTempDir(tempWorkingDir);
			}

			CallDeferred(nameof(OnConversionFinished), resultPath ?? string.Empty, errorMessage ?? string.Empty);
		});
	}

	private bool PerformAutoRigging(ref string currentPath, string tempWorkingDir, string cleanBase, out string? errorMessage)
	{
		errorMessage = null;
		SetProgressStatus(TranslationServer.Translate("Step 1/4: Auto-rigging skeleton..."), 10, false);
		string riggedPath = Path.Combine(tempWorkingDir, $"{cleanBase}_rigged.glb");
		var rigResult = GlbAutoRigger.RigHumanoid(currentPath, riggedPath, new GlbAutoRiggerOptions
		{
			LogCallback = (msg) => GameLog.LogToConsole($"[ConvertGlb] {msg}")
		});

		if (!rigResult.Success)
		{
			errorMessage = string.Format(TranslationServer.Translate("Auto-rigging failed: {0}"), rigResult.ErrorMessage);
			return false;
		}

		currentPath = riggedPath;
		return true;
	}

	private bool PerformTeamColorMasking(ref string currentPath, string tempWorkingDir, string cleanBase, Color maskColor, bool autoCorrectChromaKey, bool doAutoRig, ref string? chromaKeyHex, out string? errorMessage)
	{
		errorMessage = null;
		SetProgressStatus(TranslationServer.Translate("Step 2/4: Applying team color mask..."), doAutoRig ? 25 : 15, false);
		string maskedPath = Path.Combine(tempWorkingDir, $"{cleanBase}_masked.glb");
		string hexColor = $"#{maskColor.ToHtml(false)}";
		chromaKeyHex = hexColor;
		var maskResult = GlbPlayerColorProcessor.ProcessFile(currentPath, maskedPath, new GlbPlayerColorOptions
		{
			ChromaKey = hexColor,
			AutoCorrectChromaKey = autoCorrectChromaKey
		});

		if (!maskResult.Success)
		{
			errorMessage = string.Format(TranslationServer.Translate("Team color mask failed: {0}"), maskResult.ErrorMessage);
			return false;
		}

		if (!string.IsNullOrEmpty(maskResult.DetectedChromaKey))
		{
			chromaKeyHex = maskResult.DetectedChromaKey;
		}

		currentPath = maskedPath;
		return true;
	}

	private bool PerformModelConversion(string currentPath, string destPath, string subCategory, string? chromaKeyHex, string referenceImagePath, out string? errorMessage, out byte[]? outputBytes)
	{
		errorMessage = null;
		outputBytes = null;
		SetProgressStatus(TranslationServer.Translate("Step 3/4: Optimizing geometry & packaging RMESH..."), 40, false);
		int maxRes = subCategory is "attachments" or "items" ? 512 : 1024;
		string canonicalAssetType = GetCanonicalAssetType(subCategory);

		byte[]? sourceProfileImageWebp;
		try
		{
			sourceProfileImageWebp = EncodeReferenceImage(referenceImagePath);
		}
		catch (Exception ex)
		{
			errorMessage = string.Format(TranslationServer.Translate("Failed to encode reference image: {0}"), ex.Message);
			return false;
		}

		var convRes = ModelConverter.ConvertToRmesh(
			currentPath,
			destPath,
			canonicalAssetType,
			force: true,
			options: new OptimizationOptions
			{
				SimplificationRatio = 0.5f,
				MaxTextureResolution = maxRes,
				ForceReDecimate = true
			},
			chromaKey: chromaKeyHex,
			sourceProfileImageWebp: sourceProfileImageWebp);

		if (!convRes.Success)
		{
			errorMessage = string.Format(TranslationServer.Translate("Conversion failed: {0}"), convRes.ErrorMessage);
			return false;
		}

		outputBytes = convRes.OutputBytes;
		return true;
	}

	private static byte[]? EncodeReferenceImage(string referenceImagePath)
	{
		if (string.IsNullOrWhiteSpace(referenceImagePath)) return null;
		return ImageFormatConverter.EncodeWebpBytes(referenceImagePath, RmeshFile.SourceProfileImageQuality);
	}

	private string GetCanonicalAssetType(string subCategory)
	{
		return subCategory switch
		{
			"units" => "Character",
			"buildings" => "Building",
			"attachments" or "items" => "Item",
			_ => "Prop"
		};
	}

	private void PerformFinalMetadataSave(string destPath, string fileName, string subCategory, string wsPath, byte[]? outputBytes, out string? resultPath)
	{
		SetProgressStatus(TranslationServer.Translate("Step 4/4: Computing bounds & saving metadata..."), 80, false);

		string hash = outputBytes != null
			? RealmMetadataHelper.ComputeBlake3(outputBytes, ".rmesh")
			: RealmMetadataHelper.ComputeBlake3(destPath);

		string canonicalCat = subCategory switch
		{
			"units" => "Character",
			"buildings" => "Building",
			"attachments" or "items" or "weapons" or "projectiles" => "Item",
			_ => "Prop"
		};
		MapAssetHelper.UpdateManifestAsset(wsPath, canonicalCat, fileName, hash);

		resultPath = destPath;
	}

	private void CleanupTempDir(string tempWorkingDir)
	{
		try
		{
			if (Directory.Exists(tempWorkingDir))
				Directory.Delete(tempWorkingDir, true);
		}
		catch
		{
		}
	}

	private void OnConversionFinished(string resultPath, string errorMessage)
	{
		SetConvertingState(false);

		if (!string.IsNullOrEmpty(errorMessage))
		{
			HandleConversionError(errorMessage);
			return;
		}

		_progressBar.Value = 100;

		string fileName = Path.GetFileName(resultPath);
		string subCategory = GetSubCategoryForApply();
		float defaultScale = GetDefaultScaleForSubCategory(subCategory);

		try
		{
			CalculateAndSaveMetadata(resultPath, fileName, subCategory, defaultScale);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[ConvertGlbDialog] Bounds calculation error: {ex.Message}");
		}

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Converted and imported 3D model '{0}'!"), fileName));
		GameContext.AssetIndexService.RescanAllDirectories();
		_onConvertedCallback?.Invoke(resultPath);
		CloseDialog();
	}

}