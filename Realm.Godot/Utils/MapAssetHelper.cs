using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Realm.Godot.Services;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.Godot.Utils;

public static class MapAssetHelper
{
	public static JsonObject LoadUnionedAssets(string mapDirectory)
	{
		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		var unionedAssets = new JsonObject();

		string manifestPath = Path.Combine(targetDirectory, "manifest.json");
		string metadataPath = Path.Combine(targetDirectory, "metadata.json");

		JsonObject? metadataRoot = null;
		if (File.Exists(metadataPath))
		{
			try
			{
				string metadataText = File.ReadAllText(metadataPath);
				metadataRoot = JsonNode.Parse(metadataText)?.AsObject();
			}
			catch (Exception exception)
			{
				GD.PrintErr($"[MapAssetHelper] Failed to read metadata.json for assets: {exception.Message}");
			}
		}

		if (File.Exists(manifestPath))
		{
			try
			{
				string manifestText = File.ReadAllText(manifestPath);
				var manifestRoot = JsonNode.Parse(manifestText)?.AsObject();
				if (manifestRoot != null)
				{
					if (manifestRoot["Assets"] is JsonObject manifestAssets)
					{
						MergeAssetsInto(unionedAssets, manifestAssets);
					}
				}
			}
			catch (Exception exception)
			{
				GD.PrintErr($"[MapAssetHelper] Failed to read manifest.json for assets: {exception.Message}");
			}
		}

		if (metadataRoot != null)
		{
			AttachMetadataAttributesToUnionedAssets(unionedAssets, metadataRoot, targetDirectory);
		}

		MapWorkspaceService.NormalizeTextureEntries(unionedAssets, targetDirectory);

		EnsureAllAssetsHaveBlake3Hashes(unionedAssets, targetDirectory);

		return unionedAssets;
	}

	public static (bool IsValid, List<string> MissingFiles) ValidateWorkspaceAssets(string workspacePath)
	{
		var missingFiles = new List<string>();
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath))
		{
			missingFiles.Add(string.IsNullOrEmpty(workspacePath) ? "Workspace path is empty" : $"Workspace directory does not exist: {workspacePath}");
			return (false, missingFiles);
		}

		string assetsDir = Path.Combine(workspacePath, "Assets");
		string manifestPath = Path.Combine(workspacePath, "manifest.json");
		string metadataPath = Path.Combine(workspacePath, "metadata.json");

		if (File.Exists(manifestPath))
		{
			try
			{
				string manifestJson = File.ReadAllText(manifestPath);
				var manifestDoc = JsonNode.Parse(manifestJson)?.AsObject();
				if (manifestDoc != null)
				{
					if (manifestDoc["Assets"] is JsonObject manifestAssets)
					{
						var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
						MapManifest.FlattenAssetsInto(dict, manifestAssets);
						foreach (var kvp in dict)
						{
							string relPath = kvp.Key.TrimStart('/', '\\');
							string fullPath = Path.Combine(workspacePath, relPath);
							if (!File.Exists(fullPath))
							{
								string fileName = Path.GetFileName(relPath);
								string? resolvedModel = FindModelOnDisk(workspacePath, null, fileName);
								if (string.IsNullOrEmpty(resolvedModel) || !File.Exists(resolvedModel))
								{
									missingFiles.Add(relPath);
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapAssetHelper] ValidateWorkspaceAssets error reading manifest: {ex.Message}");
			}
		}

		if (File.Exists(metadataPath))
		{
			try
			{
				string metadataJson = File.ReadAllText(metadataPath);
				var metadataRoot = JsonNode.Parse(metadataJson)?.AsObject();
				if (metadataRoot != null)
				{
					var arrayMappings = new (string ArrayKey, string SubCategory)[]
					{
						("CustomUnits", "units"),
						("CustomBuildings", "buildings"),
						("CustomResources", "resources"),
						("CustomProps", "props"),
						("CustomAttachments", "attachments"),
						("CustomWeapons", "weapons")
					};

					foreach (var (arrayKey, subCat) in arrayMappings)
					{
						if (metadataRoot.TryGetPropertyValue(arrayKey, out var arrNode) && arrNode is JsonArray arr)
						{
							foreach (var itemNode in arr)
							{
								if (itemNode is JsonObject entityObj)
								{
									string modelPath = arrayKey switch
									{
										"CustomWeapons" => entityObj["ProjectileModelPath"]?.ToString() ?? entityObj["ModelPath"]?.ToString() ?? "",
										_ => entityObj["ModelPath"]?.ToString() ?? ""
									};

									if (!string.IsNullOrWhiteSpace(modelPath) && !modelPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase) && !modelPath.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
									{
										string fileName = Path.GetFileName(modelPath);
										string? diskPath = FindModelOnDisk(workspacePath, subCat, fileName);
										if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath))
										{
											string expectedRel = $"Assets/models/{subCat}/{fileName}".Replace('\\', '/');
											if (!expectedRel.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
											{
												expectedRel = Path.ChangeExtension(expectedRel, ".rmesh");
											}
											missingFiles.Add(expectedRel);
										}
									}

									string portraitModel = entityObj["PortraitModelPath"]?.ToString() ?? "";
									if (!string.IsNullOrWhiteSpace(portraitModel) && !portraitModel.StartsWith("res://", StringComparison.OrdinalIgnoreCase) && !portraitModel.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
									{
										string fileName = Path.GetFileName(portraitModel);
										string? diskPath = FindModelOnDisk(workspacePath, subCat, fileName);
										if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath))
										{
											string expectedRel = $"Assets/models/{subCat}/{fileName}".Replace('\\', '/');
											if (!expectedRel.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase)) expectedRel = Path.ChangeExtension(expectedRel, ".rmesh");
											missingFiles.Add(expectedRel);
										}
									}

									string deadModel = entityObj["DeadModelPath"]?.ToString() ?? "";
									if (!string.IsNullOrWhiteSpace(deadModel) && !deadModel.StartsWith("res://", StringComparison.OrdinalIgnoreCase) && !deadModel.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
									{
										string fileName = Path.GetFileName(deadModel);
										string? diskPath = FindModelOnDisk(workspacePath, subCat, fileName);
										if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath))
										{
											string expectedRel = $"Assets/models/{subCat}/{fileName}".Replace('\\', '/');
											if (!expectedRel.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase)) expectedRel = Path.ChangeExtension(expectedRel, ".rmesh");
											missingFiles.Add(expectedRel);
										}
									}

									string placementModel = entityObj["PlacementModelPath"]?.ToString() ?? "";
									if (!string.IsNullOrWhiteSpace(placementModel) && !placementModel.StartsWith("res://", StringComparison.OrdinalIgnoreCase) && !placementModel.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
									{
										string fileName = Path.GetFileName(placementModel);
										string? diskPath = FindModelOnDisk(workspacePath, subCat, fileName);
										if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath))
										{
											string expectedRel = $"Assets/models/{subCat}/{fileName}".Replace('\\', '/');
											if (!expectedRel.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase)) expectedRel = Path.ChangeExtension(expectedRel, ".rmesh");
											missingFiles.Add(expectedRel);
										}
									}
								}
							}
						}
					}

					string[] categoryKeys = new[] { "textures", "decals", "vfx_spritesheets", "noise_textures", "icons", "skyboxes", "ribbons", "animations", "sfx", "music", "other" };
					foreach (var cat in categoryKeys)
					{
						if (metadataRoot.TryGetPropertyValue(cat, out var catNode) && catNode is JsonObject catObj)
						{
							string subFolder = cat switch
							{
								"vfx_spritesheets" => "vfx",
								"animations" => "animations",
								"sfx" => "audio/sfx",
								"music" => "audio/music",
								"icons" => "icons",
								"decals" => "decals",
								"ribbons" => "ribbons",
								"noise_textures" => "noise",
								"skyboxes" => "skyboxes",
								"other" => "other",
								_ => "textures"
							};

							foreach (var itemPair in catObj)
							{
								string fileName = itemPair.Key;
								if (string.IsNullOrWhiteSpace(fileName)) continue;

								string diskPath = subFolder == "other" ? Path.Combine(workspacePath, fileName) : Path.Combine(assetsDir, subFolder, fileName);
								if (!File.Exists(diskPath) && subFolder is "audio/sfx" or "audio/music")
								{
									diskPath = Path.Combine(assetsDir, subFolder.Substring(6), fileName);
								}
								if (!File.Exists(diskPath))
								{
									diskPath = Path.Combine(assetsDir, fileName);
								}
								if (!File.Exists(diskPath))
								{
									diskPath = Path.Combine(workspacePath, fileName);
								}

								if (!File.Exists(diskPath))
								{
									string expectedRel = subFolder == "other" ? fileName : $"Assets/{subFolder}/{fileName}".Replace('\\', '/');
									missingFiles.Add(expectedRel);
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapAssetHelper] ValidateWorkspaceAssets error reading metadata: {ex.Message}");
			}
		}

		var distinctMissing = missingFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		return (distinctMissing.Count == 0, distinctMissing);
	}

	public static (bool IsValid, List<(string RelativePath, long SizeBytes, double SizeMB)> OversizedFiles) ValidateWorkspaceAssetSizes(string workspacePath, long maxSizeBytes = ContentAddressableStorage.MaximumAssetSizeBytes)
	{
		var oversized = new List<(string RelativePath, long SizeBytes, double SizeMB)>();
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath))
		{
			return (true, oversized);
		}

		string fullDirectoryPath = Path.GetFullPath(workspacePath);
		string[] allFiles = Directory.GetFiles(fullDirectoryPath, "*.*", SearchOption.AllDirectories);

		foreach (string filePath in allFiles)
		{
			string relativePath = Path.GetRelativePath(fullDirectoryPath, filePath).Replace('\\', '/');

			if ((relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) && !relativePath.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase)) ||
				relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
				relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
				relativePath.StartsWith(".vscode/", StringComparison.OrdinalIgnoreCase) ||
				relativePath.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase) ||
				relativePath.StartsWith(".sidecarcache/", StringComparison.OrdinalIgnoreCase) ||
				relativePath.StartsWith(".backups/", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".rkey", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(Path.GetFileName(relativePath), "authorship_key.pem", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var fileInfo = new FileInfo(filePath);
			if (fileInfo.Exists && fileInfo.Length > maxSizeBytes)
			{
				double sizeMb = fileInfo.Length / (1024.0 * 1024.0);
				oversized.Add((relativePath, fileInfo.Length, sizeMb));
			}
		}

		return (oversized.Count == 0, oversized);
	}

	public static void SaveAssetsToManifest(string mapDirectory, JsonObject assets, bool removeFromMetadata = true)
	{
		if (assets == null) return;

		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		if (!Directory.Exists(targetDirectory))
		{
			Directory.CreateDirectory(targetDirectory);
		}

		SynchronizeAttributesToMetadata(targetDirectory, assets);

		EnsureAllAssetsHaveBlake3Hashes(assets, targetDirectory);

		string manifestPath = Path.Combine(targetDirectory, "manifest.json");
		JsonObject manifestRoot;

		if (File.Exists(manifestPath))
		{
			try
			{
				manifestRoot = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject() ?? new JsonObject();
			}
			catch
			{
				manifestRoot = new JsonObject();
			}
		}
		else
		{
			manifestRoot = new JsonObject();
		}

		var cleanAssetsForManifest = BuildCleanManifestAssets(assets);
		manifestRoot["Assets"] = cleanAssetsForManifest;
		manifestRoot.Remove("Files");
		manifestRoot.Remove("FileSizes");

		MapJsonFormatter.SaveFormattedJson(manifestPath, manifestRoot);
	}

	public static void UpdateManifestAsset(
		string mapDirectory,
		string category,
		string fileName,
		string blake3Hash,
		string? subCategory = null,
		Action<JsonObject>? customizeEntry = null)
	{
		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		var assets = LoadUnionedAssets(targetDirectory);
		string categoryKey = NormalizeCategoryKey(category);

		if (!assets.ContainsKey(categoryKey) || assets[categoryKey] is not JsonObject)
		{
			assets[categoryKey] = new JsonObject();
		}

		var categoryObject = assets[categoryKey]!.AsObject();

		if (categoryKey == "glb" || !string.IsNullOrEmpty(subCategory))
		{
			string subKey = NormalizeGlbSubCategory(subCategory ?? "props");
			if (!categoryObject.ContainsKey(subKey) || categoryObject[subKey] is not JsonObject)
			{
				categoryObject[subKey] = new JsonObject();
			}
			var subCategoryObject = categoryObject[subKey]!.AsObject();

			JsonObject itemObject;
			if (subCategoryObject.TryGetPropertyValue(fileName, out var existingNode) && existingNode is JsonObject existingObject)
			{
				itemObject = existingObject;
			}
			else
			{
				itemObject = new JsonObject();
			}

			itemObject["hash"] = blake3Hash;
			customizeEntry?.Invoke(itemObject);
			subCategoryObject[fileName] = itemObject;
		}
		else
		{
			JsonObject itemObject;
			if (categoryObject.TryGetPropertyValue(fileName, out var existingNode) && existingNode is JsonObject existingObject)
			{
				itemObject = existingObject;
			}
			else
			{
				itemObject = new JsonObject();
				if (existingNode is JsonValue value)
				{
					itemObject["hash"] = value.ToString();
				}
			}

			itemObject["hash"] = blake3Hash;
			customizeEntry?.Invoke(itemObject);

			if (customizeEntry != null || categoryKey is "textures" or "vfx_spritesheets" or "decals" or "noise_textures")
			{
				categoryObject[fileName] = itemObject;
			}
			else
			{
				categoryObject[fileName] = blake3Hash;
			}
		}

		SaveAssetsToManifest(targetDirectory, assets, removeFromMetadata: true);
	}

	public static void RemoveManifestAsset(
		string mapDirectory,
		string category,
		string fileName,
		string? subCategory = null)
	{
		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		var assets = LoadUnionedAssets(targetDirectory);
		string categoryKey = NormalizeCategoryKey(category);

		if (assets.ContainsKey(categoryKey) && assets[categoryKey] is JsonObject categoryObject)
		{
			if (categoryKey == "glb" || !string.IsNullOrEmpty(subCategory))
			{
				string subKey = NormalizeGlbSubCategory(subCategory ?? "props");
				if (categoryObject.ContainsKey(subKey) && categoryObject[subKey] is JsonObject subCategoryObject)
				{
					subCategoryObject.Remove(fileName);
				}
				foreach (var fallbackSubCategory in new[] { "units", "buildings", "resources", "props", "projectiles", "attachments", "weapons" })
				{
					if (categoryObject.ContainsKey(fallbackSubCategory) && categoryObject[fallbackSubCategory] is JsonObject fallbackSubObject)
					{
						fallbackSubObject.Remove(fileName);
					}
				}
			}
			else
			{
				categoryObject.Remove(fileName);
			}
		}

		SaveAssetsToManifest(targetDirectory, assets, removeFromMetadata: true);

		RemoveAssetFromMetadata(targetDirectory, categoryKey, fileName);
	}

	public static void PruneNonExistentAssetsFromManifest(string mapDirectory)
	{
		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		if (!Directory.Exists(targetDirectory)) return;

		string manifestPath = Path.Combine(targetDirectory, "manifest.json");
		if (!File.Exists(manifestPath)) return;

		try
		{
			string manifestJson = File.ReadAllText(manifestPath);
			var manifestDoc = JsonNode.Parse(manifestJson)?.AsObject();
			if (manifestDoc == null) return;

			string assetsDir = Path.Combine(targetDirectory, "Assets");

			if (manifestDoc["Assets"] is JsonObject assetsObj)
			{
				var categoriesToRemove = new List<string>();

				foreach (var categoryKvp in assetsObj)
				{
					string category = categoryKvp.Key.ToLowerInvariant();
					if (category == "glb" && categoryKvp.Value is JsonObject glbObj)
					{
						var subCategoriesToRemove = new List<string>();
						foreach (var subKvp in glbObj)
						{
							string subCategory = NormalizeGlbSubCategory(subKvp.Key);
							if (subKvp.Value is JsonObject subCatObj)
							{
								var itemsToRemove = new List<string>();
								foreach (var itemKvp in subCatObj)
								{
									string fileName = itemKvp.Key;
									string? diskPath = FindModelOnDisk(targetDirectory, subCategory, fileName);
									if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath))
									{
										itemsToRemove.Add(fileName);
									}
									else
									{
										string existingHash = ExtractHashString(itemKvp.Value);
										if (string.IsNullOrEmpty(existingHash))
										{
											string hash = RealmMetadataHelper.ComputeBlake3(diskPath);
											if (!string.IsNullOrEmpty(hash))
											{
												if (itemKvp.Value is JsonObject itemObj)
												{
													itemObj["hash"] = hash;
												}
												else
												{
													subCatObj[fileName] = hash;
												}
											}
										}
									}
								}
								foreach (var item in itemsToRemove)
								{
									subCatObj.Remove(item);
								}
								if (subCatObj.Count == 0)
								{
									subCategoriesToRemove.Add(subKvp.Key);
								}
							}
						}
						foreach (var sub in subCategoriesToRemove)
						{
							glbObj.Remove(sub);
						}
						if (glbObj.Count == 0)
						{
							categoriesToRemove.Add(categoryKvp.Key);
						}
					}
					else if (categoryKvp.Value is JsonObject catObj)
					{
						string subFolder = category switch
						{
							"vfx" or "vfx_spritesheets" => "vfx",
							"animations" => "animations",
							"sfx" => "audio/sfx",
							"music" => "audio/music",
							"icons" => "icons",
							"decals" => "decals",
							"ribbons" or "ribbon_textures" => "ribbons",
							"noise" or "noise_textures" => "noise",
							"skyboxes" => "skyboxes",
							"textures" => "textures",
							"other" => "other",
							_ => category
						};

						var itemsToRemove = new List<string>();
						foreach (var itemKvp in catObj)
						{
							string fileName = itemKvp.Key;
							string diskPath = subFolder == "other" ? Path.Combine(targetDirectory, fileName) : Path.Combine(assetsDir, subFolder, fileName);
							if (!File.Exists(diskPath) && subFolder is "audio/sfx" or "audio/music")
							{
								diskPath = Path.Combine(assetsDir, subFolder.Substring(6), fileName);
							}
							if (!File.Exists(diskPath))
							{
								diskPath = Path.Combine(assetsDir, fileName);
							}
							if (!File.Exists(diskPath))
							{
								diskPath = Path.Combine(targetDirectory, fileName);
							}

							if (!File.Exists(diskPath))
							{
								itemsToRemove.Add(fileName);
							}
							else
							{
								string existingHash = ExtractHashString(itemKvp.Value);
								if (string.IsNullOrEmpty(existingHash))
								{
									string hash = RealmMetadataHelper.ComputeBlake3(diskPath);
									if (!string.IsNullOrEmpty(hash))
									{
										if (itemKvp.Value is JsonObject itemObj)
										{
											itemObj["hash"] = hash;
										}
										else
										{
											catObj[fileName] = hash;
										}
									}
								}
							}
						}
						foreach (var item in itemsToRemove)
						{
							catObj.Remove(item);
						}
						if (catObj.Count == 0)
						{
							categoriesToRemove.Add(categoryKvp.Key);
						}
					}
				}

				foreach (var cat in categoriesToRemove)
				{
					assetsObj.Remove(cat);
				}
			}

			if (manifestDoc["Files"] is JsonObject filesObj)
			{
				var filesToRemove = new List<string>();
				foreach (var fileKvp in filesObj)
				{
					string relPath = fileKvp.Key.TrimStart('/', '\\');
					string fullPath = Path.Combine(targetDirectory, relPath);
					if (!File.Exists(fullPath))
					{
						string fileName = Path.GetFileName(relPath);
						string? modelDisk = FindModelOnDisk(targetDirectory, null, fileName);
						if (string.IsNullOrEmpty(modelDisk) || !File.Exists(modelDisk))
						{
							filesToRemove.Add(fileKvp.Key);
						}
					}
				}
				foreach (var f in filesToRemove)
				{
					filesObj.Remove(f);
					if (manifestDoc["FileSizes"] is JsonObject sizesObj)
					{
						sizesObj.Remove(f);
					}
				}
			}

			SaveAssetsToManifest(targetDirectory, manifestDoc["Assets"]?.AsObject() ?? new JsonObject(), removeFromMetadata: true);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapAssetHelper] PruneNonExistentAssetsFromManifest error: {ex.Message}");
		}
	}

	public static void EnsureManifestJson(string directory)
	{
		if (string.IsNullOrEmpty(directory)) return;

		string manifestPath = Path.Combine(directory, "manifest.json");
		if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length == 0)
		{
			string templateManifest = MapWorkspaceService.GetTemplatePath("manifest.json");
			if (!string.IsNullOrEmpty(templateManifest) && File.Exists(templateManifest))
			{
				try
				{
					File.Copy(templateManifest, manifestPath, true);
				}
				catch
				{
				}
			}
		}

		if (File.Exists(manifestPath) && new FileInfo(manifestPath).Length > 0)
		{
			try
			{
				var existingRoot = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();
				if (existingRoot != null)
				{
					if (existingRoot.ContainsKey("Assets"))
					{
						return;
					}
				}
			}
			catch
			{
			}
		}

		var unionedAssets = LoadUnionedAssets(directory);
		SaveAssetsToManifest(directory, unionedAssets, removeFromMetadata: false);
	}

	private static JsonObject BuildCleanManifestAssets(JsonObject sourceAssets)
	{
		var cleanAssets = new JsonObject();

		foreach (var categoryKeyValuePair in sourceAssets)
		{
			string category = NormalizeCategoryKey(categoryKeyValuePair.Key);
			if (category == "glb" && categoryKeyValuePair.Value is JsonObject glbObject)
			{
				var cleanGlb = new JsonObject();
				cleanAssets["glb"] = cleanGlb;

				foreach (var subCategoryKeyValuePair in glbObject)
				{
					string subCategory = NormalizeGlbSubCategory(subCategoryKeyValuePair.Key);
					if (subCategoryKeyValuePair.Value is JsonObject subCategoryObject)
					{
						var cleanSub = new JsonObject();
						cleanGlb[subCategory] = cleanSub;

						foreach (var itemKeyValuePair in subCategoryObject)
						{
							string hash = ExtractHashString(itemKeyValuePair.Value);
							if (!string.IsNullOrEmpty(hash))
							{
								cleanSub[itemKeyValuePair.Key] = hash;
							}
						}
					}
				}
			}
			else if (categoryKeyValuePair.Value is JsonObject categoryObject)
			{
				var cleanCategory = new JsonObject();
				cleanAssets[category] = cleanCategory;

				foreach (var itemKeyValuePair in categoryObject)
				{
					string hash = ExtractHashString(itemKeyValuePair.Value);
					if (!string.IsNullOrEmpty(hash))
					{
						cleanCategory[itemKeyValuePair.Key] = hash;
					}
				}
			}
		}

		return cleanAssets;
	}

	private static string ExtractHashString(JsonNode? node)
	{
		if (node is JsonValue value)
		{
			return value.ToString();
		}
		if (node is JsonObject jsonObject && jsonObject.TryGetPropertyValue("hash", out var hashNode) && hashNode != null)
		{
			return hashNode.ToString();
		}
		return string.Empty;
	}

	private static void SynchronizeAttributesToMetadata(string targetDirectory, JsonObject assets)
	{
		string metadataPath = Path.Combine(targetDirectory, "metadata.json");
		JsonObject metadataRoot;

		if (File.Exists(metadataPath))
		{
			try
			{
				metadataRoot = JsonNode.Parse(File.ReadAllText(metadataPath))?.AsObject() ?? new JsonObject();
			}
			catch
			{
				metadataRoot = new JsonObject();
			}
		}
		else
		{
			metadataRoot = new JsonObject();
		}

		metadataRoot.Remove("Assets");
		if (metadataRoot["MapProperties"] is JsonObject mapPropertiesObject)
		{
			mapPropertiesObject.Remove("Assets");
		}

		SyncCategoryAttributesToMetadata(metadataRoot, assets, "textures");
		SyncCategoryAttributesToMetadata(metadataRoot, assets, "decals");
		SyncCategoryAttributesToMetadata(metadataRoot, assets, "vfx_spritesheets");
		SyncCategoryAttributesToMetadata(metadataRoot, assets, "noise_textures");

		SyncModelAttributesToMetadata(metadataRoot, assets);

		SaveLoadService.CleanMetadataJsonSchema(metadataRoot);
		MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);
	}

	private static void SyncCategoryAttributesToMetadata(JsonObject metadataRoot, JsonObject assets, string category)
	{
		string categoryKey = NormalizeCategoryKey(category);
		if (!assets.ContainsKey(categoryKey) || assets[categoryKey] is not JsonObject sourceCategoryObject)
		{
			return;
		}

		if (!metadataRoot.ContainsKey(categoryKey) || metadataRoot[categoryKey] is not JsonObject)
		{
			metadataRoot[categoryKey] = new JsonObject();
		}
		var targetCategoryObject = metadataRoot[categoryKey]!.AsObject();

		var currentAssetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var itemKeyValuePair in sourceCategoryObject)
		{
			string fileName = itemKeyValuePair.Key;
			currentAssetKeys.Add(fileName);

			if (itemKeyValuePair.Value is JsonObject sourceItemObject)
			{
				JsonObject destinationItemObject;
				if (targetCategoryObject.TryGetPropertyValue(fileName, out var existingNode) && existingNode is JsonObject existingObject)
				{
					destinationItemObject = existingObject;
				}
				else
				{
					destinationItemObject = new JsonObject();
					targetCategoryObject[fileName] = destinationItemObject;
				}

				foreach (var property in sourceItemObject)
				{
					if (string.Equals(property.Key, "hash", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					destinationItemObject[property.Key] = property.Value?.DeepClone();
				}
				destinationItemObject.Remove("hash");
			}
		}

		var keysToRemove = targetCategoryObject.Select(pair => pair.Key).Where(key => !currentAssetKeys.Contains(key)).ToList();
		foreach (var key in keysToRemove)
		{
			targetCategoryObject.Remove(key);
		}
	}

	private static void SyncModelAttributesToMetadata(JsonObject metadataRoot, JsonObject assets)
	{
		if (!assets.ContainsKey("glb") || assets["glb"] is not JsonObject glbObject)
		{
			return;
		}

		EnsureMetadataTopLevelObject(metadataRoot, "Models");
		var modelsObject = metadataRoot["Models"]!.AsObject();

		foreach (var subCategoryKeyValuePair in glbObject)
		{
			if (subCategoryKeyValuePair.Value is JsonObject subCategoryObject)
			{
				foreach (var itemKeyValuePair in subCategoryObject)
				{
					string fileName = itemKeyValuePair.Key;
					if (itemKeyValuePair.Value is JsonObject modelProperties)
					{
						if (!modelsObject.ContainsKey(fileName) || modelsObject[fileName] is not JsonObject)
						{
							modelsObject[fileName] = new JsonObject();
						}
						var modelEntry = modelsObject[fileName]!.AsObject();

						if (modelProperties.TryGetPropertyValue("y_offset", out var yOffsetNode) && yOffsetNode != null && float.TryParse(yOffsetNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float yOffset))
						{
							modelEntry["Offsets"] = yOffset;
						}
						if (modelProperties.TryGetPropertyValue("scale", out var scaleNode) && scaleNode != null && float.TryParse(scaleNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
						{
							modelEntry["Scales"] = scale;
						}
						if (modelProperties.TryGetPropertyValue("collision_circle_ratio", out var ratioNode) && ratioNode != null && float.TryParse(ratioNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float ratio))
						{
							modelEntry["CollisionCircleRatios"] = ratio;
						}
						if (modelProperties.TryGetPropertyValue("collision_radius", out var radiusNode) && radiusNode != null && float.TryParse(radiusNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
						{
							modelEntry["ObstacleRadii"] = radius;
						}
						if (modelProperties.TryGetPropertyValue("brightness", out var brightnessNode) && brightnessNode != null && float.TryParse(brightnessNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float brightness))
						{
							modelEntry["Brightness"] = brightness;
						}
						if (modelProperties.TryGetPropertyValue("despill_player_color", out var despillNode) && despillNode != null && bool.TryParse(despillNode.ToString(), out bool despill))
						{
							modelEntry["DespillPlayerColor"] = despill;
						}
						if (modelProperties.TryGetPropertyValue("normalize_luminance", out var normalizeLuminanceNode) && normalizeLuminanceNode != null && bool.TryParse(normalizeLuminanceNode.ToString(), out bool normalizeLuminance))
						{
							modelEntry["NormalizeLuminance"] = normalizeLuminance;
						}
						if (modelProperties.TryGetPropertyValue("ignore_player_color", out var ignorePlayerColorNode) && ignorePlayerColorNode != null && bool.TryParse(ignorePlayerColorNode.ToString(), out bool ignorePlayerColor))
						{
							modelEntry["IgnorePlayerColor"] = ignorePlayerColor;
						}
						if (modelProperties.TryGetPropertyValue("spawn_shader", out var spawnShaderNode) && spawnShaderNode != null)
						{
							string spawnShader = spawnShaderNode.ToString();
							if (!string.IsNullOrWhiteSpace(spawnShader)) modelEntry["SpawnShaders"] = spawnShader;
						}
						if (modelProperties.TryGetPropertyValue("death_shader", out var deathShaderNode) && deathShaderNode != null)
						{
							string deathShader = deathShaderNode.ToString();
							if (!string.IsNullOrWhiteSpace(deathShader)) modelEntry["DeathShaders"] = deathShader;
						}
					}
				}
			}
		}
	}

	private static void EnsureMetadataTopLevelObject(JsonObject metadataRoot, string propertyName)
	{
		if (!metadataRoot.ContainsKey(propertyName) || metadataRoot[propertyName] is not JsonObject)
		{
			metadataRoot[propertyName] = new JsonObject();
		}
	}

	private static void RemoveAssetFromMetadata(string targetDirectory, string categoryKey, string fileName)
	{
		string metadataPath = Path.Combine(targetDirectory, "metadata.json");
		if (!File.Exists(metadataPath)) return;

		try
		{
			var metadataRoot = JsonNode.Parse(File.ReadAllText(metadataPath))?.AsObject();
			if (metadataRoot == null) return;

			bool modified = false;
			if (metadataRoot.TryGetPropertyValue(categoryKey, out var categoryNode) && categoryNode is JsonObject categoryObject)
			{
				if (categoryObject.Remove(fileName))
				{
					modified = true;
				}
			}

			if (categoryKey == "glb")
			{
				if (metadataRoot.TryGetPropertyValue("Models", out var modelsNode) && modelsNode is JsonObject modelsObject)
				{
					if (modelsObject.Remove(fileName))
					{
						modified = true;
					}
				}
			}

			if (modified)
			{
				SaveLoadService.CleanMetadataJsonSchema(metadataRoot);
				MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);
			}
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MapAssetHelper] RemoveAssetFromMetadata error: {exception.Message}");
		}
	}

	private static void AttachMetadataAttributesToUnionedAssets(JsonObject unionedAssets, JsonObject metadataRoot, string targetDirectory)
	{
		if (metadataRoot["textures"] is JsonObject texturesObject)
		{
			MergeCategoryAttributes(unionedAssets, "textures", texturesObject);
		}

		if (metadataRoot["decals"] is JsonObject decalsObject)
		{
			MergeCategoryAttributes(unionedAssets, "decals", decalsObject);
		}

		if (metadataRoot["vfx_spritesheets"] is JsonObject vfxObject)
		{
			MergeCategoryAttributes(unionedAssets, "vfx_spritesheets", vfxObject);
		}

		if (metadataRoot["noise_textures"] is JsonObject noiseObject)
		{
			MergeCategoryAttributes(unionedAssets, "noise_textures", noiseObject);
		}

		if (metadataRoot["icons"] is JsonObject iconsObject)
		{
			MergeCategoryAttributes(unionedAssets, "icons", iconsObject);
		}

		if (metadataRoot["skyboxes"] is JsonObject skyboxesObject)
		{
			MergeCategoryAttributes(unionedAssets, "skyboxes", skyboxesObject);
		}

		if (metadataRoot["ribbons"] is JsonObject ribbonsObject)
		{
			MergeCategoryAttributes(unionedAssets, "ribbons", ribbonsObject);
		}

		if (metadataRoot["animations"] is JsonObject animationsObject)
		{
			MergeCategoryAttributes(unionedAssets, "animations", animationsObject);
		}

		if (metadataRoot["sfx"] is JsonObject sfxObject)
		{
			MergeCategoryAttributes(unionedAssets, "sfx", sfxObject);
		}

		if (metadataRoot["music"] is JsonObject musicObject)
		{
			MergeCategoryAttributes(unionedAssets, "music", musicObject);
		}

		if (metadataRoot["other"] is JsonObject otherObject)
		{
			MergeCategoryAttributes(unionedAssets, "other", otherObject);
		}

		AttachCustomEntitiesToGlb(unionedAssets, metadataRoot, targetDirectory);
		AttachModelMetadataAttributes(unionedAssets, metadataRoot);
	}

	private static void AttachCustomEntitiesToGlb(JsonObject unionedAssets, JsonObject metadataRoot, string targetDirectory)
	{
		var arrayMappings = new (string ArrayKey, string SubCategory)[]
		{
			("CustomUnits", "units"),
			("CustomBuildings", "buildings"),
			("CustomResources", "resources"),
			("CustomProps", "props"),
			("CustomAttachments", "attachments"),
			("CustomWeapons", "weapons")
		};

		foreach (var (arrayKey, subCat) in arrayMappings)
		{
			if (metadataRoot.TryGetPropertyValue(arrayKey, out var arrNode) && arrNode is JsonArray arr)
			{
				foreach (var itemNode in arr)
				{
					if (itemNode is JsonObject entityObj)
					{
						string modelPath = arrayKey switch
						{
							"CustomWeapons" => entityObj["ProjectileModelPath"]?.ToString() ?? entityObj["ModelPath"]?.ToString() ?? "",
							_ => entityObj["ModelPath"]?.ToString() ?? ""
						};

						if (string.IsNullOrEmpty(modelPath))
						{
							string fallbackId = arrayKey switch
							{
								"CustomUnits" => entityObj["UnitId"]?.ToString() ?? "",
								"CustomAttachments" => entityObj["AttachmentId"]?.ToString() ?? "",
								_ => ""
							};
							if (!string.IsNullOrEmpty(fallbackId))
							{
								string? diskCheck = FindModelOnDisk(targetDirectory, subCat, fallbackId, out _);
								if (!string.IsNullOrEmpty(diskCheck))
								{
									modelPath = fallbackId;
								}
							}
						}

						if (!string.IsNullOrEmpty(modelPath))
						{
							string fileName = Path.GetFileName(modelPath);
							string? diskPath = FindModelOnDisk(targetDirectory, subCat, fileName, out string resolvedSub);
							if (!string.IsNullOrEmpty(diskPath))
							{
								fileName = Path.GetFileName(diskPath);
								EnsureGlbEntryExists(unionedAssets, subCat, fileName, diskPath);
							}
						}

						void TryAttachModel(string? path)
						{
							if (string.IsNullOrWhiteSpace(path) || path.StartsWith("res://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
							{
								return;
							}
							string fileName = Path.GetFileName(path);
							string? diskPath = FindModelOnDisk(targetDirectory, subCat, fileName, out string resolvedSub);
							if (!string.IsNullOrEmpty(diskPath))
							{
								fileName = Path.GetFileName(diskPath);
								EnsureGlbEntryExists(unionedAssets, subCat, fileName, diskPath);
							}
						}

						TryAttachModel(entityObj["PortraitModelPath"]?.ToString());
						TryAttachModel(entityObj["DeadModelPath"]?.ToString());
						TryAttachModel(entityObj["PlacementModelPath"]?.ToString());
					}
				}
			}
		}

		if (metadataRoot.TryGetPropertyValue("Models", out var modelsNode) && modelsNode is JsonObject modelsObj)
		{
			foreach (var prop in modelsObj)
			{
				string rawName = prop.Key;
				string? existingSub = FindExistingGlbSubCategory(unionedAssets, rawName);
				if (string.IsNullOrEmpty(existingSub))
				{
					string? diskPath = FindModelOnDisk(targetDirectory, null, rawName, out string foundSub);
					if (!string.IsNullOrEmpty(diskPath))
					{
						string fileName = Path.GetFileName(diskPath);
						existingSub = !string.IsNullOrEmpty(foundSub) ? foundSub : "props";
						EnsureGlbEntryExists(unionedAssets, existingSub, fileName, diskPath);
					}
				}
			}
		}
	}

	private static string DetermineTextureCategory(string relativeToAssets)
	{
		string lower = relativeToAssets.ToLowerInvariant();
		if (lower.Contains("decals/")) return "decals";
		if (lower.Contains("icons/")) return "icons";
		if (lower.Contains("skyboxes/")) return "skyboxes";
		if (lower.Contains("vfx/") || lower.Contains("vfx_spritesheets/")) return "vfx_spritesheets";
		if (lower.Contains("noise/") || lower.Contains("noise_textures/")) return "noise_textures";
		if (lower.Contains("ribbons/") || lower.Contains("ribbon_textures/")) return "ribbons";
		return "textures";
	}

	private static void EnsureGlbEntryExists(JsonObject unionedAssets, string subCategory, string fileName, string? diskPath = null)
	{
		if (!unionedAssets.ContainsKey("glb") || unionedAssets["glb"] is not JsonObject)
		{
			unionedAssets["glb"] = new JsonObject();
		}
		var glbObject = unionedAssets["glb"]!.AsObject();

		string normalizedSub = NormalizeGlbSubCategory(subCategory);
		if (!glbObject.ContainsKey(normalizedSub) || glbObject[normalizedSub] is not JsonObject)
		{
			glbObject[normalizedSub] = new JsonObject();
		}
		var subCategoryObject = glbObject[normalizedSub]!.AsObject();

		if (!subCategoryObject.ContainsKey(fileName))
		{
			var itemObject = new JsonObject();
			if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
			{
				string hash = RealmMetadataHelper.ComputeBlake3(diskPath);
				if (!string.IsNullOrEmpty(hash))
				{
					itemObject["hash"] = hash;
				}
			}
			subCategoryObject[fileName] = itemObject;
		}
		else if (subCategoryObject[fileName] is JsonObject existingItem && !existingItem.ContainsKey("hash") && !string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
		{
			string hash = RealmMetadataHelper.ComputeBlake3(diskPath);
			if (!string.IsNullOrEmpty(hash))
			{
				existingItem["hash"] = hash;
			}
		}
	}

	private static void EnsureCategoryEntryExists(JsonObject unionedAssets, string category, string fileName, string? diskPath = null)
	{
		string categoryKey = NormalizeCategoryKey(category);
		if (!unionedAssets.ContainsKey(categoryKey) || unionedAssets[categoryKey] is not JsonObject)
		{
			unionedAssets[categoryKey] = new JsonObject();
		}
		var categoryObject = unionedAssets[categoryKey]!.AsObject();

		if (!categoryObject.ContainsKey(fileName))
		{
			string hash = (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
				? RealmMetadataHelper.ComputeBlake3(diskPath)
				: string.Empty;

			if (categoryKey is "textures" or "decals" or "vfx_spritesheets" or "noise_textures")
			{
				var itemObject = new JsonObject();
				if (!string.IsNullOrEmpty(hash))
				{
					itemObject["hash"] = hash;
				}
				categoryObject[fileName] = itemObject;
			}
			else
			{
				categoryObject[fileName] = !string.IsNullOrEmpty(hash) ? hash : string.Empty;
			}
		}
		else if (categoryObject[fileName] is JsonObject existingObject && !existingObject.ContainsKey("hash") && !string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
		{
			string hash = RealmMetadataHelper.ComputeBlake3(diskPath);
			if (!string.IsNullOrEmpty(hash))
			{
				existingObject["hash"] = hash;
			}
		}
		else if (categoryObject[fileName] is JsonValue val && string.IsNullOrEmpty(val.ToString()) && !string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
		{
			string hash = RealmMetadataHelper.ComputeBlake3(diskPath);
			if (!string.IsNullOrEmpty(hash))
			{
				categoryObject[fileName] = hash;
			}
		}
	}

	public static string? FindModelOnDisk(string targetDirectory, string? preferredSubCategory, string fileName, out string resolvedSubCategory)
	{
		resolvedSubCategory = !string.IsNullOrEmpty(preferredSubCategory) ? preferredSubCategory : "props";
		if (string.IsNullOrEmpty(targetDirectory) || !Directory.Exists(targetDirectory))
		{
			return null;
		}

		string modelsDir = Path.Combine(targetDirectory, "Assets", "models");
		if (!Directory.Exists(modelsDir))
		{
			return null;
		}

		string[] candidateFiles;
		if (fileName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			candidateFiles = new[] { fileName };
		}
		else
		{
			candidateFiles = new[] { $"{fileName}.rmesh", $"{Path.GetFileNameWithoutExtension(fileName)}.rmesh" };
		}

		if (!string.IsNullOrEmpty(preferredSubCategory))
		{
			string prefSub = NormalizeGlbSubCategory(preferredSubCategory);
			foreach (var cand in candidateFiles)
			{
				string preferredPath = Path.Combine(modelsDir, prefSub, cand);
				if (File.Exists(preferredPath))
				{
					resolvedSubCategory = prefSub;
					return preferredPath;
				}
			}
		}

		string[] subCategories = new[] { "units", "buildings", "resources", "props", "projectiles", "attachments", "weapons" };
		foreach (var sub in subCategories)
		{
			foreach (var cand in candidateFiles)
			{
				string candPath = Path.Combine(modelsDir, sub, cand);
				if (File.Exists(candPath))
				{
					resolvedSubCategory = sub;
					return candPath;
				}
			}
		}

		foreach (var cand in candidateFiles)
		{
			string directPath = Path.Combine(modelsDir, cand);
			if (File.Exists(directPath))
			{
				return directPath;
			}
		}

		return null;
	}

	public static string? FindModelOnDisk(string targetDirectory, string? preferredSubCategory, string fileName)
	{
		return FindModelOnDisk(targetDirectory, preferredSubCategory, fileName, out _);
	}

	private static string? FindExistingGlbSubCategory(JsonObject unionedAssets, string fileName)
	{
		if (unionedAssets["glb"] is JsonObject glbObj)
		{
			string[] candidates = fileName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase)
				? new[] { fileName }
				: new[] { fileName, $"{fileName}.rmesh", $"{Path.GetFileNameWithoutExtension(fileName)}.rmesh" };

			foreach (var subPair in glbObj)
			{
				if (subPair.Value is JsonObject subObj)
				{
					foreach (var cand in candidates)
					{
						if (subObj.ContainsKey(cand))
						{
							return subPair.Key;
						}
					}
				}
			}
		}
		return null;
	}

	private static void EnsureAllAssetsHaveBlake3Hashes(JsonObject unionedAssets, string targetDirectory)
	{
		string assetsDir = Path.Combine(targetDirectory, "Assets");
		bool hasAssetsDir = Directory.Exists(assetsDir);

		foreach (var categoryPair in unionedAssets)
		{
			string category = NormalizeCategoryKey(categoryPair.Key);
			if (category == "glb" && categoryPair.Value is JsonObject glbObj)
			{
				foreach (var subPair in glbObj)
				{
					string subCat = NormalizeGlbSubCategory(subPair.Key);
					if (subPair.Value is JsonObject subObj)
					{
						foreach (var itemPair in subObj)
						{
							string fileName = itemPair.Key;
							string hash = ExtractHashString(itemPair.Value);
							if (string.IsNullOrEmpty(hash))
							{
								string? diskPath = hasAssetsDir ? FindModelOnDisk(targetDirectory, subCat, fileName) : null;
								if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
								{
									hash = RealmMetadataHelper.ComputeBlake3(diskPath);
								}

								if (!string.IsNullOrEmpty(hash))
								{
									if (itemPair.Value is JsonObject itemObj)
									{
										itemObj["hash"] = hash;
									}
									else
									{
										subObj[fileName] = hash;
									}
								}
							}
						}
					}
				}
			}
			else if (categoryPair.Value is JsonObject catObj)
			{
				foreach (var itemPair in catObj)
				{
					string fileName = itemPair.Key;
					string hash = ExtractHashString(itemPair.Value);
					bool isOther = category == "other";
					if (isOther || string.IsNullOrEmpty(hash))
					{
						string subFolder = category switch
						{
							"vfx_spritesheets" => "vfx",
							"animations" => "animations",
							"sfx" => "audio/sfx",
							"music" => "audio/music",
							"icons" => "icons",
							"decals" => "decals",
							"ribbons" => "ribbons",
							"noise_textures" => "noise",
							"skyboxes" => "skyboxes",
							"other" => "other",
							_ => "textures"
						};
						string diskPath = subFolder == "other" ? Path.Combine(targetDirectory, fileName) : Path.Combine(assetsDir, subFolder, fileName);
						if (!File.Exists(diskPath) && subFolder is "audio/sfx" or "audio/music")
						{
							diskPath = Path.Combine(assetsDir, subFolder.Substring(6), fileName);
						}
						if (!File.Exists(diskPath))
						{
							diskPath = Path.Combine(assetsDir, fileName);
						}
						if (!File.Exists(diskPath))
						{
							diskPath = Path.Combine(targetDirectory, fileName);
						}

						if (File.Exists(diskPath))
						{
							hash = RealmMetadataHelper.ComputeBlake3(diskPath);
						}

						if (!string.IsNullOrEmpty(hash))
						{
							if (itemPair.Value is JsonObject itemObj)
							{
								itemObj["hash"] = hash;
							}
							else
							{
								catObj[fileName] = hash;
							}
						}
					}
				}
			}
		}
	}

	private static void MergeCategoryAttributes(JsonObject unionedAssets, string category, JsonObject sourceObject)
	{
		string categoryKey = NormalizeCategoryKey(category);
		if (!unionedAssets.ContainsKey(categoryKey) || unionedAssets[categoryKey] is not JsonObject)
		{
			unionedAssets[categoryKey] = new JsonObject();
		}
		var targetCategoryObject = unionedAssets[categoryKey]!.AsObject();

		foreach (var itemKeyValuePair in sourceObject)
		{
			string fileName = itemKeyValuePair.Key;
			JsonObject targetItemObject;

			if (targetCategoryObject.TryGetPropertyValue(fileName, out var existingNode) && existingNode is JsonObject existingObject)
			{
				targetItemObject = existingObject;
			}
			else
			{
				targetItemObject = new JsonObject();
				if (existingNode is JsonValue val)
				{
					targetItemObject["hash"] = val.ToString();
				}
				targetCategoryObject[fileName] = targetItemObject;
			}

			if (itemKeyValuePair.Value is JsonObject sourceAttributes)
			{
				foreach (var attributeProperty in sourceAttributes)
				{
					if (string.Equals(attributeProperty.Key, "hash", StringComparison.OrdinalIgnoreCase))
					{
						if (!targetItemObject.ContainsKey("hash"))
						{
							targetItemObject["hash"] = attributeProperty.Value?.DeepClone();
						}
					}
					else
					{
						targetItemObject[attributeProperty.Key] = attributeProperty.Value?.DeepClone();
					}
				}
			}
		}
	}

	private static void AttachModelMetadataAttributes(JsonObject unionedAssets, JsonObject metadataRoot)
	{
		if (!unionedAssets.ContainsKey("glb") || unionedAssets["glb"] is not JsonObject glbObject)
		{
			return;
		}

		var modelsObject = metadataRoot["Models"] as JsonObject;

		foreach (var subCategoryKeyValuePair in glbObject)
		{
			if (subCategoryKeyValuePair.Value is JsonObject subCategoryObject)
			{
				foreach (var itemKeyValuePair in subCategoryObject)
				{
					string fileName = itemKeyValuePair.Key;
					string baseName = Path.GetFileNameWithoutExtension(fileName);
					JsonObject modelObject;

					if (itemKeyValuePair.Value is JsonObject existingObject)
					{
						modelObject = existingObject;
					}
					else
					{
						modelObject = new JsonObject();
						if (itemKeyValuePair.Value is JsonValue value)
						{
							modelObject["hash"] = value.ToString();
						}
						subCategoryObject[fileName] = modelObject;
					}

					JsonObject? modelEntry = null;
					if (modelsObject != null)
					{
						if (modelsObject.TryGetPropertyValue(fileName, out var entryNode) && entryNode is JsonObject entryObj)
						{
							modelEntry = entryObj;
						}
						else if (modelsObject.TryGetPropertyValue(baseName, out var baseEntryNode) && baseEntryNode is JsonObject baseEntryObj)
						{
							modelEntry = baseEntryObj;
						}
					}

					if (modelEntry != null)
					{
						if (modelEntry.TryGetPropertyValue("Offsets", out var offsetNode))
						{
							modelObject["y_offset"] = offsetNode?.DeepClone();
						}
						if (modelEntry.TryGetPropertyValue("Scales", out var scaleNode))
						{
							modelObject["scale"] = scaleNode?.DeepClone();
						}
						if (modelEntry.TryGetPropertyValue("CollisionCircleRatios", out var circleNode))
						{
							modelObject["collision_circle_ratio"] = circleNode?.DeepClone();
						}
						if (modelEntry.TryGetPropertyValue("ObstacleRadii", out var radiusNode))
						{
							modelObject["collision_radius"] = radiusNode?.DeepClone();
						}
						if (modelEntry.TryGetPropertyValue("Brightness", out var brightNode))
						{
							modelObject["brightness"] = brightNode?.DeepClone();
						}
						if (modelEntry.TryGetPropertyValue("ColorTint", out var tintNode))
						{
							modelObject["tint"] = tintNode?.DeepClone();
						}
						if (modelEntry.TryGetPropertyValue("DespillPlayerColor", out var despillNode))
						{
							modelObject["despill_player_color"] = despillNode?.DeepClone();
						}
						if (modelEntry.TryGetPropertyValue("NormalizeLuminance", out var lumNode))
						{
							modelObject["normalize_luminance"] = lumNode?.DeepClone();
						}
						if (modelEntry.TryGetPropertyValue("IgnorePlayerColor", out var ipcNode))
						{
							modelObject["ignore_player_color"] = ipcNode?.DeepClone();
						}
						if (modelEntry.TryGetPropertyValue("SpawnShaders", out var spawnNode))
						{
							modelObject["spawn_shader"] = spawnNode?.DeepClone();
						}
						if (modelEntry.TryGetPropertyValue("DeathShaders", out var deathNode))
						{
							modelObject["death_shader"] = deathNode?.DeepClone();
						}
					}
				}
			}
		}
	}

	private static void MergeAssetsInto(JsonObject target, JsonObject source)
	{
		foreach (var categoryKeyValuePair in source)
		{
			string category = NormalizeCategoryKey(categoryKeyValuePair.Key);
			if (category == "glb" && categoryKeyValuePair.Value is JsonObject glbSource)
			{
				if (!target.ContainsKey("glb") || target["glb"] is not JsonObject)
				{
					target["glb"] = new JsonObject();
				}
				var glbTarget = target["glb"]!.AsObject();

				foreach (var subCategoryKeyValuePair in glbSource)
				{
					string subCategory = NormalizeGlbSubCategory(subCategoryKeyValuePair.Key);
					if (subCategoryKeyValuePair.Value is JsonObject subSource)
					{
						if (!glbTarget.ContainsKey(subCategory) || glbTarget[subCategory] is not JsonObject)
						{
							glbTarget[subCategory] = new JsonObject();
						}
						var subTarget = glbTarget[subCategory]!.AsObject();

						foreach (var itemKeyValuePair in subSource)
						{
							MergeItemInto(subTarget, itemKeyValuePair.Key, itemKeyValuePair.Value);
						}
					}
				}
			}
			else if (categoryKeyValuePair.Value is JsonObject categorySource)
			{
				MergeCategoryInto(target, category, categorySource);
			}
		}
	}

	private static void MergeCategoryInto(JsonObject target, string category, JsonObject source)
	{
		string categoryKey = NormalizeCategoryKey(category);
		if (!target.ContainsKey(categoryKey) || target[categoryKey] is not JsonObject)
		{
			target[categoryKey] = new JsonObject();
		}
		var categoryTarget = target[categoryKey]!.AsObject();

		foreach (var itemKeyValuePair in source)
		{
			MergeItemInto(categoryTarget, itemKeyValuePair.Key, itemKeyValuePair.Value);
		}
	}

	private static void MergeItemInto(JsonObject targetContainer, string key, JsonNode? sourceNode)
	{
		if (sourceNode == null) return;

		if (targetContainer.ContainsKey(key) && targetContainer[key] is JsonObject existingObject && sourceNode is JsonObject sourceObject)
		{
			foreach (var property in sourceObject)
			{
				existingObject[property.Key] = property.Value?.DeepClone();
			}
		}
		else
		{
			targetContainer[key] = sourceNode.DeepClone();
		}
	}

	public static string NormalizeCategoryKey(string category)
	{
		string lower = category.ToLowerInvariant();
		return lower switch
		{
			"vfx" or "vfx_spritesheets" or "spritesheets" => "vfx_spritesheets",
			"ribbon" or "ribbons" or "ribbon_textures" => "ribbons",
			"noise" or "noise_textures" => "noise_textures",
			"sound" or "sounds" or "audio" => "sfx",
			_ => lower
		};
	}

	public static string NormalizeGlbSubCategory(string subCategory)
	{
		string lower = subCategory.ToLowerInvariant();
		if (lower.StartsWith("rmesh_")) lower = lower.Substring(6);
		return lower switch
		{
			"unit" or "units" or "character" or "characters" => "units",
			"building" or "buildings" => "buildings",
			"resource" or "resources" or "environment" => "resources",
			"prop" or "props" => "props",
			"projectile" or "projectiles" => "projectiles",
			"attachment" or "attachments" => "attachments",
			"weapon" or "weapons" => "weapons",
			"item" or "items" => "items",
			_ => lower
		};
	}
}
