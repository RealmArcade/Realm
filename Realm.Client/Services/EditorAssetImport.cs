using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using NSec.Cryptography;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using HttpClient = System.Net.Http.HttpClient;
using Key = NSec.Cryptography.Key;

namespace Realm.Client.Services;

/// <summary>
///     Bringing an asset file into a map workspace: glTF animation splitting, ranim naming and
///     deduplication, decal import, texture, skybox, sprite and icon import, resolving a decal's
///     texture file, the animation directory, raw rtex layer reads and the creator key registration
///     call. None of it holds editor state, so EditorService forwards the public members because they
///     are its IEditorAPI surface.
/// </summary>
internal static class EditorAssetImport
{
	internal static (bool Success, string ErrorMessage, IReadOnlyList<string> ExtractedAnimationFiles, string FileName) ImportGlbAssetFromExtensionDomain(string sourceFilePath, string category, string wsPath, Action<string, string, string, string?> updateMetadataAction)
	{
		string fileName = Path.GetFileName(sourceFilePath);
		string subCat = category.ToLowerInvariant();
		var importResult = MixamoAnimationImporter.ImportMixamoGlb(sourceFilePath, wsPath, subCat);
		if (!importResult.Success)
		{
			return (false, importResult.ErrorMessage, null!, fileName);
		}

		byte[] glbBytes = File.ReadAllBytes(importResult.StrippedGlbPath);
		string glbBlake3 = RealmMetadataHelper.ComputeBlake3(glbBytes, ".glb");
		updateMetadataAction("glb", fileName, glbBlake3, subCat);
		foreach (var animFile in importResult.ExtractedAnimationFiles)
		{
			string animPath = Path.Combine(wsPath, "Assets", "animations", animFile);
			if (File.Exists(animPath))
			{
				byte[] animBytes = File.ReadAllBytes(animPath);
				string animBlake3 = RealmMetadataHelper.ComputeBlake3(animBytes, ".ranim");
				updateMetadataAction("animations", animFile, animBlake3, null);
			}
		}

		GameContext.Host?.LoadUnitMetadata(wsPath);
		return (true, null!, importResult.ExtractedAnimationFiles, fileName);
	}

	internal static string ResolveAnimationFileName(string sourceFilePath, string? preferredFileName)
	{
		string originalFileName = !string.IsNullOrWhiteSpace(preferredFileName) && !AssetIndexService.IsHexHash(Path.GetFileNameWithoutExtension(preferredFileName))
			? Path.GetFileNameWithoutExtension(preferredFileName)
			: (GameContext.AssetIndexService?.ResolvePrettyFileName(sourceFilePath, preferredFileName) ?? Path.GetFileNameWithoutExtension(sourceFilePath));
		return Path.GetFileNameWithoutExtension(originalFileName);
	}

	internal static (int ImportedCount, int SkippedCount, List<(string FileName, string Blake3)>? NewAnims) Import3DAnimationFileDomain(string sourceFilePath, string originalFileName, string animsDir)
	{
		var extracted = MixamoAnimationImporter.ExtractAnimationsFromFile(sourceFilePath, originalFileName);
		if (extracted.Count == 0)
		{
			return (0, 0, null);
		}

		int importedCount = 0;
		int skippedCount = 0;
		var newAnims = new List<(string FileName, string Blake3)>();

		foreach (var (animName, animData) in extracted)
		{
			var (savedFileName, blake3, alreadyExisted) = MixamoAnimationImporter.SaveAnimationWithDeduplication(animsDir, animName, animData);
			newAnims.Add((savedFileName, blake3));
			if (alreadyExisted) skippedCount++;
			else importedCount++;
		}

		return (importedCount, skippedCount, newAnims);
	}

	internal static (bool Success, string FinalFileName, string NewHash) ImportRanimFileDomain(string sourceFilePath, string? preferredFileName, string animsDir)
	{
		string resolvedName = !string.IsNullOrWhiteSpace(preferredFileName) && !AssetIndexService.IsHexHash(Path.GetFileNameWithoutExtension(preferredFileName))
			? preferredFileName
			: (GameContext.AssetIndexService?.ResolvePrettyFileName(sourceFilePath, preferredFileName) ?? Path.GetFileName(sourceFilePath));
		string fileName = resolvedName;
		byte[] sourceBytes = File.ReadAllBytes(sourceFilePath);
		string newHash = RealmMetadataHelper.ComputeBlake3(sourceBytes, ".ranim");

		string baseName = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
		string finalFileName = $"{baseName}.ranim";
		string targetPath = Path.Combine(animsDir, finalFileName);

		if (File.Exists(targetPath))
		{
			string? replacementName = HandleExistingRanimFileDomain(targetPath, animsDir, baseName, newHash, sourceBytes);
			if (string.IsNullOrEmpty(replacementName)) return (false, Path.GetFileName(targetPath), newHash);
			finalFileName = replacementName;
		}
		else
		{
			File.WriteAllBytes(targetPath, sourceBytes);
		}

		return (true, finalFileName, newHash);
	}

	private static string? HandleExistingRanimFileDomain(string targetPath, string animsDir, string baseName, string newHash, byte[] sourceBytes)
	{
		string existingHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(targetPath), ".ranim");
		if (existingHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		for (int i = 1; i <= 9999; i++)
		{
			string varName = $"{baseName}_{i}.ranim";
			string varPath = Path.Combine(animsDir, varName);
			if (!File.Exists(varPath))
			{
				File.WriteAllBytes(varPath, sourceBytes);
				return varName;
			}
			else
			{
				string varHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(varPath), ".ranim");
				if (varHash.Equals(newHash, StringComparison.OrdinalIgnoreCase))
				{
					return varName;
				}
			}
		}

		return null;
	}

	internal static (bool Success, string Message, string FileName, string Blake3) ImportDecalAssetDomain(string sourceFilePath, string wsPath)
	{
		try
		{
			string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
			string fileName = baseName + ".png";
			string decalsDir = Path.Combine(wsPath, "Assets", "decals");
			Directory.CreateDirectory(decalsDir);
			string targetPath = Path.Combine(decalsDir, fileName);

			var img = Godot.Image.LoadFromFile(sourceFilePath);
			if (img != null)
			{
				img.SavePng(targetPath);
			}
			else
			{
				File.Copy(sourceFilePath, targetPath, true);
			}

			byte[] bytes = File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".png");
			return (true, "", fileName, blake3);
		}
		catch (Exception ex)
		{
			return (false, ex.Message, "", "");
		}
	}

	internal static string PrepareAnimationsDirectory(string wsPath)
	{
		string animsDir = Path.Combine(wsPath, "Assets", "animations");
		Directory.CreateDirectory(animsDir);
		return animsDir;
	}

	internal static byte[]? GetRtexWebpBytes(string localRtex)
	{
		try
		{
			byte[] bytes = File.ReadAllBytes(localRtex);
			return RtexFile.GetLayer(bytes, 0);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Failed to read Rtex bytes: {ex.Message}");
			return null;
		}
	}

	internal static async Task<string?> ProcessCreatorRegistrationAsync(string username, string pubKeyStr, Key key)
	{
		if (!Realm.Shared.NameNormalizationHelper.ValidateUsername(username, out var validationError))
		{
			return JsonSerializer.Serialize(new { Message = validationError ?? "Invalid username." });
		}

		if (!GameContext.Account.IsSignedIn || string.IsNullOrEmpty(GameContext.Account.SessionToken))
		{
			return JsonSerializer.Serialize(new { Message = "Sign in with Steam before registering a map key." });
		}

		return await RegisterCreatorAsync(username, pubKeyStr, key);
	}

	internal static async Task<string?> RegisterCreatorAsync(string username, string pubKeyStr, Key key)
	{
		byte[] payloadBytes = Encoding.UTF8.GetBytes(CreatorRegistrationProof.BuildPayload(username, pubKeyStr, null, null));
		byte[] sigBytes = SignatureAlgorithm.Ed25519.Sign(key, payloadBytes);
		string signatureStr = Convert.ToBase64String(sigBytes);

		var regPayload = new { Username = username, PublicKey = pubKeyStr, Signature = signatureStr };
		string registrationServerUrl = AccountService.AuthorityUrl;

		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
		httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GameContext.Account.SessionToken);
		var content = new StringContent(JsonSerializer.Serialize(regPayload), Encoding.UTF8, "application/json");
		var res = await httpClient.PostAsync(registrationServerUrl + "/api/creators/register", content);

		if (res.IsSuccessStatusCode)
		{
			return null;
		}
		else
		{
			return await res.Content.ReadAsStringAsync();
		}
	}

	public static bool ImportTextureAssetFromExtensionDomain(string sourceFilePath, int slotIndex, string wsPath, GameHost? gameHost, out string cleanBaseName, out string outputRtex, Action<string, string, string, int> updateMetadata)
	{
		cleanBaseName = Path.GetFileNameWithoutExtension(sourceFilePath).ToLowerInvariant().Replace(" ", "_") + ".rtex";
		string texDir = Path.Combine(wsPath, "Assets", "textures");
		Directory.CreateDirectory(texDir);
		outputRtex = Path.Combine(texDir, cleanBaseName);

		string ext = Path.GetExtension(sourceFilePath);
		if (ext.Equals(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			File.Copy(sourceFilePath, outputRtex, true);
		}
		else if (gameHost != null && gameHost.GroundTerrain != null)
		{
			gameHost.GroundTerrain.ProcessAndSaveRawTexture(sourceFilePath, outputRtex);
		}

		if (File.Exists(outputRtex))
		{
			byte[] rtexBytes = File.ReadAllBytes(outputRtex);
			string blake3 = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");
			updateMetadata?.Invoke("textures", cleanBaseName, blake3, slotIndex);
			return true;
		}

		return false;
	}

	public static void UpdateTextureMetadataAndReload(int index, string cleanBaseName, string outputRtex, GameHost? gameHost, Action<string, string, string, int> updateMetadata)
	{
		if (File.Exists(outputRtex))
		{
			byte[] rtexBytes = File.ReadAllBytes(outputRtex);
			string blake3 = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");
			updateMetadata?.Invoke("textures", cleanBaseName, blake3, index);
		}

		if (gameHost != null && gameHost.GroundTerrain != null)
		{
			gameHost.GroundTerrain.ReloadTerrainTextures(true);
		}
	}

	public static void CopyOrProcessTexture(string imagePath, string outputRtex, GameHost? gameHost)
	{
		string ext = Path.GetExtension(imagePath);
		if (ext.Equals(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			File.Copy(imagePath, outputRtex, true);
		}
		else if (gameHost != null && gameHost.GroundTerrain != null)
		{
			gameHost.GroundTerrain.ProcessAndSaveRawTexture(imagePath, outputRtex);
		}
	}

	public static string PrepareTextureOutputDir(string cleanBaseName, string editingDirectory)
	{
		string wsPath = string.IsNullOrEmpty(editingDirectory)
			? MapWorkspaceService.GetDefaultMapFolderGlobalPath()
			: editingDirectory;
		string texDir = Path.Combine(wsPath, "Assets", "textures");
		Directory.CreateDirectory(texDir);
		return Path.Combine(texDir, cleanBaseName);
	}

	public static string ResolveTextureFileName(string imagePath, string? preferredFileName)
	{
		string resolvedName = !string.IsNullOrWhiteSpace(preferredFileName) && !AssetIndexService.IsHexHash(Path.GetFileNameWithoutExtension(preferredFileName))
			? preferredFileName
			: (GameContext.AssetIndexService?.ResolvePrettyFileName(imagePath, preferredFileName) ?? Path.GetFileName(imagePath));
		return Path.GetFileNameWithoutExtension(resolvedName).ToLowerInvariant().Replace(" ", "_") + ".rtex";
	}

	public static (bool Success, string Message, string FileName, string Blake3) ImportSkyboxAsset(string sourceFilePath, string wsPath)
	{
		try
		{
			string fileName = Path.GetFileName(sourceFilePath);
			string skyboxesDir = Path.Combine(wsPath, "Assets", "skyboxes");
			Directory.CreateDirectory(skyboxesDir);
			string targetPath = Path.Combine(skyboxesDir, fileName);

			File.Copy(sourceFilePath, targetPath, true);

			byte[] bytes = File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, fileName);

			return (true, "", fileName, blake3);
		}
		catch (Exception ex)
		{
			return (false, ex.Message, null!, null!);
		}
	}

	public static (bool Success, string Message, string FileName, string Blake3) ImportSpritesheetAsset(string sourceFilePath, string wsPath)
	{
		return ImportImageAsAsset(sourceFilePath, wsPath, "vfx");
	}

	public static (bool Success, string Message, string FileName, string Blake3) ImportIconAsset(string sourceFilePath, string wsPath)
	{
		return ImportImageAsAsset(sourceFilePath, wsPath, "icons");
	}

	private static (bool Success, string Message, string FileName, string Blake3) ImportImageAsAsset(string sourceFilePath, string wsPath, string subFolder)
	{
		try
		{
			string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
			string fileName = baseName + ".png";
			string outputDir = Path.Combine(wsPath, "Assets", subFolder);
			Directory.CreateDirectory(outputDir);
			string targetPath = Path.Combine(outputDir, fileName);

			var img = Image.LoadFromFile(sourceFilePath);
			if (img != null)
			{
				img.SavePng(targetPath);
			}
			else
			{
				File.Copy(sourceFilePath, targetPath, true);
			}

			byte[] bytes = File.ReadAllBytes(targetPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".png");

			return (true, "", fileName, blake3);
		}
		catch (Exception ex)
		{
			return (false, ex.Message, null!, null!);
		}
	}

	public static string ResolveDecalKeyFromMetadata(string decalId, string wsPath)
	{
		try
		{
			var metadata = MapFileService.LoadMetadata(wsPath);
			if (metadata?.Decals != null)
			{
				string filename = Path.GetFileName(decalId);
				string baseKey = Path.GetFileNameWithoutExtension(decalId);
				DecalMetadata? meta = null;

				if (metadata.Decals.TryGetValue(decalId, out var d0)) meta = d0;
				else if (metadata.Decals.TryGetValue(filename, out var d1)) meta = d1;
				else if (metadata.Decals.TryGetValue(baseKey, out var d2)) meta = d2;

				if (meta != null && !string.IsNullOrWhiteSpace(meta.TexturePath))
				{
					return meta.TexturePath;
				}
			}
		}
		catch
		{
		}

		return decalId;
	}

	public static string FindDecalFile(string targetKey, string wsPath)
	{
		string targetFilename = Path.GetFileName(targetKey);
		string candidate1 = Path.Combine(wsPath, "Assets", "decals", targetFilename);
		if (File.Exists(candidate1)) return candidate1;

		if (!targetFilename.Contains('.'))
		{
			string candidate2 = Path.Combine(wsPath, "Assets", "decals", targetFilename + ".png");
			if (File.Exists(candidate2)) return candidate2;
			string candidateRtex = Path.Combine(wsPath, "Assets", "decals", targetFilename + ".rtex");
			if (File.Exists(candidateRtex)) return candidateRtex;
		}

		if (Path.IsPathRooted(targetKey) && File.Exists(targetKey)) return targetKey;

		string candidate3 = Path.Combine(wsPath, targetKey);
		if (File.Exists(candidate3)) return candidate3;

		return "res://icon.svg";
	}

}
