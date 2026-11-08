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
///     deduplication, decal import, the animation directory, raw rtex layer reads and the creator
///     key registration call. None of it holds editor state, so EditorService forwards the public
///     members because they are its IEditorAPI surface.
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
}
