using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Blake3;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.UI;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.Services;
using Realm.Shared.Textures;

namespace Realm.Client.Services;

public static partial class MapWorkspaceService
{
	public static bool IsIgnoredPathDomain(string relativePath)
	{
		if (string.IsNullOrEmpty(relativePath)) return false;
		string normalized = relativePath.Replace('\\', '/');
		return IsPathIgnoredByRuleDomain(normalized);
	}

	public static bool IsPathIgnoredByRuleDomain(string normalized)
	{
		if (normalized.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
			normalized.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
			normalized.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
			normalized.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
			normalized.EndsWith(".elf", StringComparison.OrdinalIgnoreCase) ||
			normalized.EndsWith(".jit.elf", StringComparison.OrdinalIgnoreCase) ||
			normalized.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase) ||
			normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
			normalized.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
			normalized.EndsWith(".template.cs", StringComparison.OrdinalIgnoreCase) ||
			Path.GetFileName(normalized).StartsWith("~", StringComparison.OrdinalIgnoreCase) ||
			Path.GetFileName(normalized).Equals("WasmEntryPoint.cs", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return IsIgnoredDirectoryDomain(normalized);
	}

	public static bool IsIgnoredDirectoryDomain(string normalized)
	{
		string[] ignoredDirs = { ".git", ".vs", ".godot", ".idea", "bin", "obj", "lib", "map_backups", "map_upgrades", "backups", ".backups", ".dotnet", ".wasi", ".sidecarcache", ".cache" };
		foreach (var dir in ignoredDirs)
		{
			if (normalized.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase) ||
				normalized.Contains("/" + dir + "/", StringComparison.OrdinalIgnoreCase) ||
				normalized.Equals(dir, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	public const string ScriptOutputDirectoryName = MapFileService.ScriptOutputDirectoryName;

	public const string BuildFlavorFileName = MapFileService.BuildFlavorFileName;

	public const string DebugBuildFlavor = MapFileService.DebugBuildFlavor;

	public const string ReleaseBuildFlavor = MapFileService.ReleaseBuildFlavor;

	public static string GetScriptOutputDirectory(string workspace) => MapFileService.ResolveScriptOutputDirectory(workspace);

	public static string GetBuildFlavorPath(string workspace) => MapFileService.ResolveBuildFlavorPath(workspace);

	public static string? FindExistingWasmFileDomain(string workspace)
	{
		return MapScriptArtifacts.FindCompiledScript(workspace);
	}

	public static string? SearchForWasmInDirectoryDomain(string binDirectory)
	{
		return MapScriptArtifacts.FindCompiledScriptInBin(binDirectory);
	}

	public static string? ReadBuildFlavorDomain(string workspace)
	{
		return MapScriptArtifacts.ReadBuildFlavor(workspace);
	}

	public static void WriteBuildFlavorDomain(string workspace, bool isDebug)
	{
		MapScriptArtifacts.WriteBuildFlavor(workspace, isDebug);
	}

	public static void EnsureSingleScriptOutput(string workspace)
	{
		MapScriptArtifacts.EnsureSingleScriptOutput(workspace);
	}

	public static void TryReloadGroundTerrainTexturesSafeDomain(Action resetMapAction)
	{
		if (GameContext.Host?.GroundTerrain == null) return;
		try
		{
			GameContext.Host.GroundTerrain.ReloadTerrainTextures(false);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"ReloadTerrainTextures error during workspace init: {ex.Message}. Resetting to blank map.");
			resetMapAction?.Invoke();
		}
	}

	public static bool HasUnsavedChangesDomain(Func<string, bool> isIgnoredPathFunc, string tempPath)
	{
		if (string.IsNullOrEmpty(tempPath) || !Directory.Exists(tempPath))
		{
			return false;
		}

		if (string.IsNullOrEmpty(GameContext.EditorService.CurrentDirectoryBlake3))
		{
			return false;
		}

		string currentHash = ComputeDirectoryBlake3Domain(tempPath, isIgnoredPathFunc);
		return !string.Equals(currentHash, GameContext.EditorService.CurrentDirectoryBlake3, StringComparison.OrdinalIgnoreCase);
	}

	public static string ComputeDirectoryBlake3Domain(string directoryPath, Func<string, bool> isIgnoredPathFunc)
	{
		if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath)) return string.Empty;

		try
		{
			var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
				.Where(f => !isIgnoredPathFunc(f.Substring(directoryPath.Length).TrimStart('/', '\\')))
				.OrderBy(f => Path.GetRelativePath(directoryPath, f).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
				.ToList();

			using var hasher = Hasher.New();
			byte[] buffer = new byte[65536];

			foreach (var file in files)
			{
				try
				{
					string relPath = Path.GetRelativePath(directoryPath, file).Replace('\\', '/');
					byte[] pathBytes = Encoding.UTF8.GetBytes(relPath);
					hasher.Update(pathBytes);

					using var fs = File.OpenRead(file);
					int read;
					while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
					{
						hasher.Update(new ReadOnlySpan<byte>(buffer, 0, read));
					}
				}
				catch (Exception ex)
				{
					GameLog.LogError($"[ComputeDirectoryBlake3] Error hashing {file}: {ex.Message}");
				}
			}

			return hasher.Finalize().ToString();
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[ComputeDirectoryBlake3] Error scanning directory {directoryPath}: {ex.Message}");
			return string.Empty;
		}
	}

	public static void RefreshSavedDirectoryHashDomain(Func<string, bool> isIgnoredPathFunc)
	{
		string workspacePath = GameContext.EditorService._editingDirectory;
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;
		try
		{
			GameContext.EditorService.CurrentDirectoryBlake3 = ComputeDirectoryBlake3Domain(workspacePath, isIgnoredPathFunc);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[RefreshSavedDirectoryHash] Error hashing the map folder: {ex.Message}");
		}
	}

	public static void OpenFromCasDomain(string defaultSaveFolder, Func<string, bool> isRestrictedSaveDirectoryFunc, Action<string> saveSettingsAction)
	{
		GameContext.EditorService.LastUsedFolder = defaultSaveFolder;
		GameContext.EditorService.SetCurrentSourceFolder(defaultSaveFolder);
		if (!isRestrictedSaveDirectoryFunc(defaultSaveFolder))
		{
			saveSettingsAction?.Invoke(defaultSaveFolder);
		}
	}

	public static string GetDefaultMapFolderGlobalPath()
	{
		return MapWorkspacePaths.DefaultMapFolderGlobalPath();
	}

	public static void SetActiveWorkspacePathForTesting(string? path)
	{
		MapWorkspacePaths.SetActiveWorkspacePathForTesting(path);
	}

	public static string GetActiveWorkspacePath()
	{
		return MapWorkspacePaths.ActiveWorkspacePath();
	}

	public static string GetActiveMapDirectory()
	{
		return MapWorkspacePaths.ActiveMapDirectory();
	}

	public static string GetEditorWorkspacePath()
	{
		return MapWorkspacePaths.EditorWorkspacePath();
	}

	public static string GetTemplatePath(string fileName)
	{
		return MapWorkspacePaths.TemplatePath(fileName);
	}

	public static string? GetTemplateRoot()
	{
		return MapWorkspacePaths.TemplateRoot();
	}

	public static void EnsureNoiseTexturesGenerated(string directory)
	{
		MapWorkspaceScaffolding.EnsureNoiseTexturesGenerated(directory);
	}

	public static void GenerateVSCodeConfig(string directory)
	{
		MapWorkspaceScaffolding.GenerateVSCodeConfig(directory);
	}

	public static void EnsureAgentSkills(string directory)
	{
		MapWorkspaceScaffolding.EnsureAgentSkills(directory);
	}

	public static void EnsureWitFile(string directory)
	{
		MapWorkspaceScaffolding.EnsureWitFile(directory);
	}

	public static void EnsureLldbScript(string directory)
	{
		MapWorkspaceScaffolding.EnsureLldbScript(directory);
	}

	public static void EnsureCsproj(string directory, string mapName)
	{
		MapWorkspaceScaffolding.EnsureCsproj(directory, mapName);
	}

	public static void EnsureNugetConfig(string directory)
	{
		MapWorkspaceScaffolding.EnsureNugetConfig(directory);
	}

	public static void EnsureDirectoryBuildTargets(string directory)
	{
		MapWorkspaceScaffolding.EnsureDirectoryBuildTargets(directory);
	}

	public static void EnsureApiLib(string directory)
	{
		MapWorkspaceScaffolding.EnsureApiLib(directory);
	}

	public static void EnsureMapScript(string directory, string mapName)
	{
		MapWorkspaceScaffolding.EnsureMapScript(directory, mapName);
	}

	public static void EnsureSourceMapAttributionAttribute(string directory)
	{
		MapWorkspaceScaffolding.EnsureSourceMapAttributionAttribute(directory);
	}

	public static void EnsureWasmEntryPoint(string directory)
	{
		MapWorkspaceScaffolding.EnsureWasmEntryPoint(directory);
	}

	public static void EnsureSolutionFile(string directory, string mapName)
	{
		MapWorkspaceScaffolding.EnsureSolutionFile(directory, mapName);
	}

	public static void SetupWorkspace(string directory, string mapName)
	{
		if (string.IsNullOrEmpty(directory)) return;
		Directory.CreateDirectory(directory);

		GenerateVSCodeConfig(directory);
		EnsureAgentSkills(directory);
		EnsureWitFile(directory);
		EnsureLldbScript(directory);
		EnsureCsproj(directory, mapName);
		EnsureMapScript(directory, mapName);
		EnsureSourceMapAttributionAttribute(directory);
		EnsureWasmEntryPoint(directory);
		WorkspaceMetadataBootstrapper.EnsureMetadata(directory, GetTemplatePath("metadata.json"));
		WorkspaceMetadataBootstrapper.EnsureLicenseFile(directory);
		MapAssetHelper.EnsureManifestJson(directory);
		EnsureSolutionFile(directory, mapName);
		NormalizeMetadataTextureEntries(directory);
		EnsureGlbAssetsOptimized(directory);
		EnsurePngAssetsConverted(directory);
		EnsureNoiseTexturesGenerated(directory);
		MapTemplateGeneratorService.EnsureMapTemplates(directory);
	}

	public static void CleanWorkspaceDirectory(string targetDir)
	{
		if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir)) return;
		try
		{
			foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
			{
				var fileAttributes = File.GetAttributes(file);
				if ((fileAttributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
				{
					File.SetAttributes(file, fileAttributes & ~FileAttributes.ReadOnly);
				}

				File.Delete(file);
			}

			foreach (var directory in Directory.GetDirectories(targetDir))
			{
				Directory.Delete(directory, true);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapWorkspaceService] CleanWorkspaceDirectory error: {ex.Message}");
		}
	}

	public static void CleanWorkspaceBinaries(string directory)
	{
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

		foreach (var folder in new[] { "bin", "obj" })
		{
			string targetDir = Path.Combine(directory, folder);
			if (Directory.Exists(targetDir))
			{
				try
				{
					foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
					{
						var attrs = File.GetAttributes(file);
						if ((attrs & FileAttributes.ReadOnly) != 0)
						{
							File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
						}
					}

					Directory.Delete(targetDir, true);
				}
				catch (Exception ex)
				{
					GameLog.LogError($"[MapWorkspaceService] Failed to clean build folder {folder}: {ex.Message}");
				}
			}
		}
	}

	public static string GetUntitledMapDirectory()
	{
		string localAppDataDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrEmpty(localAppDataDirectory))
		{
			localAppDataDirectory = ProjectSettings.GlobalizePath("user://");
		}

		return Path.Combine(localAppDataDirectory, "Realm", MapNaming.UntitledMapNamePrefix);
	}

	public static bool RebuildUntitledMapFolder(string folderPath, out string errorMessage)
	{
		errorMessage = string.Empty;

		string? templateRoot = GetTemplateRoot();
		if (string.IsNullOrEmpty(templateRoot))
		{
			errorMessage = "the map template could not be found";
			return false;
		}

		string mapName = MapNaming.CreateUntitledMapName();
		try
		{
			Directory.CreateDirectory(folderPath);
			MapWorkspaceScaffolding.ClearFolderBestEffort(folderPath);
			MapWorkspaceScaffolding.CopyFolderBestEffort(templateRoot, folderPath);
		}
		catch (Exception exception)
		{
			errorMessage = exception.Message;
			return false;
		}

		try
		{
			SetupWorkspace(folderPath, mapName);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapWorkspaceService] Could not finish setting up the untitled map folder: {exception.Message}");
		}

		bool mapHasItsName = MapWorkspaceScaffolding.TryRunFileOperation(() => GameContext.MetadataService.UpdateMetadata(folderPath, metadata =>
		{
			metadata.MapProperties ??= new MapInfoMetadata();
			metadata.MapProperties.MapName = mapName;
			metadata.GameBuildNumber = RealmVersion.GameBuildNumber;
		}));

		if (!mapHasItsName || !TerrainFileMatchesTemplate(folderPath, templateRoot))
		{
			errorMessage = "another program is holding a file in the untitled map folder open. Close the embedded editor or stop the build, then try again.";
			return false;
		}

		return true;
	}

	private static bool TerrainFileMatchesTemplate(string folderPath, string templateRoot)
	{
		var writtenTerrain = new FileInfo(Path.Combine(folderPath, "terrain.json"));
		var templateTerrain = new FileInfo(Path.Combine(templateRoot, "terrain.json"));
		if (!writtenTerrain.Exists || !templateTerrain.Exists) return false;

		return writtenTerrain.Length == templateTerrain.Length;
	}

	public static void EnsureGlbAssetsOptimized(string workspacePath)
	{
		MapGlbOptimization.EnsureGlbAssetsOptimized(workspacePath);
	}

	public static Task EnsureGlbAssetsOptimizedCooperativeAsync(
		string workspacePath,
		Func<int, int, string, Task>? onModelProgress = null)
	{
		return MapGlbOptimization.EnsureGlbAssetsOptimizedCooperativeAsync(workspacePath, onModelProgress);
	}

	private static (string AssetType, int Columns, int Rows) DetectPngAssetInfo(string workspacePath, string pngPath, MapMetadata? metadata = null)
	{
		if (TryDetectFromEmbeddedMetadata(pngPath, out var embeddedInfo))
			return embeddedInfo;

		string fileName = Path.GetFileName(pngPath);
		string cleanName = Path.GetFileNameWithoutExtension(pngPath);

		if (TryDetectFromMapAssets(workspacePath, fileName, cleanName, metadata, out var mapAssetInfo))
			return mapAssetInfo;

		return DetectFromPathHeuristics(pngPath);
	}

	private static int GetJsonIntOrDefault(JsonObject node, string key, int defaultValue)
	{
		if (node.TryGetPropertyValue(key, out var valNode) && valNode != null)
		{
			return valNode.GetValue<int>();
		}

		return defaultValue;
	}

	private static bool TryDetectFromEmbeddedMetadata(string pngPath, out (string AssetType, int Columns, int Rows) info)
	{
		info = default;
		string? metaJson = RealmMetadataHelper.ExtractMetadata(pngPath);
		if (string.IsNullOrEmpty(metaJson)) return false;

		try
		{
			if (JsonNode.Parse(metaJson) is not JsonObject node) return false;

			if (!node.TryGetPropertyValue("type", out var typeNode)) return false;
			if (typeNode == null) return false;

			string? type = typeNode.GetValue<string>();
			if (string.IsNullOrEmpty(type)) return false;

			bool isDecal = type.Contains("decal", StringComparison.OrdinalIgnoreCase);
			int defaultGrid = 4;
			if (isDecal)
			{
				defaultGrid = 1;
			}

			int cols = GetJsonIntOrDefault(node, "columns", defaultGrid);
			int rows = GetJsonIntOrDefault(node, "rows", defaultGrid);

			info = (type, cols, rows);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool ContainsAssetKey(Dictionary<string, string>? dict, string fileName, string cleanName)
	{
		if (dict == null) return false;

		if (dict.ContainsKey(fileName) || dict.ContainsKey($"{cleanName}.rtex")) return true;

		return dict.Any(kvp =>
			kvp.Key.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase) ||
			kvp.Key.EndsWith($"/{cleanName}.rtex", StringComparison.OrdinalIgnoreCase));
	}

	private static readonly (string Category, string Type)[] _assetCategoryTypes = new[]
	{
		("Icon", "icon"),
		("Skybox", "skybox"),
		("Terrain", "terrain_texture"),
		("Ribbon", "ribbon_texture"),
		("Noise", "noise_texture")
	};

	private static bool TryDetectFromMapAssets(string workspacePath, string fileName, string cleanName, MapMetadata? metadata, out (string AssetType, int Columns, int Rows) info)
	{
		info = default;
		var assetsObj = MapAssetHelper.LoadAssets(workspacePath);
		if (assetsObj == null) return false;

		var loadedMetadata = metadata ?? MapFileService.LoadMetadata(workspacePath);
		if (loadedMetadata?.Decals != null && (loadedMetadata.Decals.ContainsKey(fileName) || loadedMetadata.Decals.ContainsKey($"{cleanName}.rtex")))
		{
			info = ("decal", 1, 1);
			return true;
		}

		foreach (var (category, type) in _assetCategoryTypes)
		{
			if (ContainsAssetKey(assetsObj.GetCategory(category), fileName, cleanName))
			{
				info = (type, 4, 4);
				return true;
			}
		}

		if (TryDetectFromVfxMetadata(loadedMetadata, fileName, cleanName, out info)) return true;

		return false;
	}

	private static bool TryDetectFromVfxMetadata(MapMetadata? metadata, string fileName, string cleanName, out (string AssetType, int Columns, int Rows) info)
	{
		info = default;
		if (metadata?.VfxSpritesheets == null) return false;

		VfxMetadata? vmeta = null;
		if (metadata.VfxSpritesheets.TryGetValue(fileName, out var v1)) vmeta = v1;
		else if (metadata.VfxSpritesheets.TryGetValue($"{cleanName}.rtex", out var v2)) vmeta = v2;

		if (vmeta == null) return false;

		int cols = vmeta.Columns > 0 ? vmeta.Columns : 4;
		int rows = vmeta.Rows > 0 ? vmeta.Rows : 4;
		info = ("vfx_spritesheet", cols, rows);
		return true;
	}

	private static (string AssetType, int Columns, int Rows) DetectFromPathHeuristics(string pngPath)
	{
		string normalized = pngPath.Replace("\\", "/").ToLowerInvariant();

		if (normalized.Contains("/assets/decals/")) return ("decal", 1, 1);
		if (normalized.Contains("/assets/icons/")) return ("icon", 4, 4);
		if (normalized.Contains("/assets/skyboxes/")) return ("skybox", 4, 4);
		if (normalized.Contains("/assets/vfx/")) return ("vfx_spritesheet", 4, 4);
		if (normalized.Contains("/assets/ribbons/")) return ("ribbon_texture", 4, 4);
		if (normalized.Contains("/assets/noise/")) return ("noise_texture", 4, 4);

		return ("terrain_texture", 4, 4);
	}

	public static void UpdateMetadataConvertedTexture(
		string workspacePath,
		string pngFileName,
		string rtexFileName,
		string assetType,
		string newHash,
		int columns = 4,
		int rows = 4)
	{
		try
		{
			string categoryKey = MapAssetHelper.NormalizeCategoryKey(assetType);
			MapAssetHelper.UpdateManifestAsset(workspacePath, categoryKey, rtexFileName, newHash);
			if (!string.Equals(pngFileName, rtexFileName, StringComparison.OrdinalIgnoreCase))
			{
				MapAssetHelper.RemoveManifestAsset(workspacePath, categoryKey, pngFileName);
			}

			if (string.Equals(categoryKey, "Spritesheet", StringComparison.OrdinalIgnoreCase))
			{
				string slug = TemplateIDHelper.GenerateSlug(rtexFileName);
				string spritesheetTemplateId = TemplateIDHelper.NormalizeTemplateID("spritesheet", slug);
				GameContext.MetadataService.UpdateMetadata(workspacePath, m =>
				{
					m.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
					m.VfxSpritesheets[spritesheetTemplateId] = new VfxMetadata
					{
						TexturePath = rtexFileName,
						Columns = columns,
						Rows = rows
					};
				});
			}
			else if (string.Equals(categoryKey, "Terrain", StringComparison.OrdinalIgnoreCase))
			{
				string slug = TemplateIDHelper.GenerateSlug(rtexFileName);
				string terrainTemplateId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);
				GameContext.MetadataService.UpdateMetadata(workspacePath, m =>
				{
					m.Textures ??= new(StringComparer.OrdinalIgnoreCase);
					if (!m.Textures.TryGetValue(terrainTemplateId, out var tex))
					{
						tex = new TextureMetadata();
						m.Textures[terrainTemplateId] = tex;
					}

					tex.TexturePath = rtexFileName;
					if (tex.ScaleFactor <= 0.0001f)
					{
						string fullRtexPath = Path.Combine(workspacePath, "Assets", "textures", rtexFileName);
						float sf = TextureConverter.CalculateLuminanceScaleFactor(fullRtexPath);
						tex.ScaleFactor = sf <= 0.0001f ? 1.0f : sf;
					}
				});
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapWorkspaceService] Failed to update metadata for converted texture {pngFileName}: {ex.Message}");
		}
	}

	public static void EnsurePngAssetsConverted(string workspacePath)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			string[] pngFiles = Directory.GetFiles(assetsDir, "*.png", SearchOption.AllDirectories);
			if (pngFiles.Length == 0) return;

			GameContext.MetadataService.TryLoadMetadata(workspacePath, out var metadataRoot);

			foreach (string pngPath in pngFiles)
			{
				if (MapWorkspacePaths.IsBuildOutputPath(pngPath)) continue;
				ProcessPngAsset(pngPath, workspacePath, metadataRoot);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapWorkspaceService] EnsurePngAssetsConverted error: {ex.Message}");
		}
	}

	private static void ProcessPngAsset(string pngPath, string workspacePath, MapMetadata? metadataRoot, int? index = null, int? totalFiles = null)
	{
		string pngFileName = Path.GetFileName(pngPath);
		string rtexFileName = Path.ChangeExtension(pngFileName, ".rtex");
		string rtexPath = Path.ChangeExtension(pngPath, ".rtex");

		var (assetType, columns, rows) = DetectPngAssetInfo(workspacePath, pngPath, metadataRoot);

		string progressStr = index.HasValue && totalFiles.HasValue ? $" ({index.Value + 1}/{totalFiles.Value})" : "";
		GameLog.LogToConsole($"[MapWorkspaceService] Converting PNG asset '{pngFileName}'{progressStr} (type: {assetType}) to .rtex...");

		var convResult = TextureConverter.ConvertTextureFile(pngPath, rtexPath, assetType, columns, rows);

		if (convResult.Success && File.Exists(rtexPath))
		{
			FinalizePngConversion(pngPath, rtexPath, workspacePath, pngFileName, rtexFileName, assetType, columns, rows);
		}
		else
		{
			GameLog.LogError($"[MapWorkspaceService] Failed to convert PNG '{pngFileName}': {convResult.ErrorMessage}");
		}
	}

	private static void FinalizePngConversion(string pngPath, string rtexPath, string workspacePath, string pngFileName, string rtexFileName, string assetType, int columns, int rows)
	{
		byte[] rtexBytes = File.ReadAllBytes(rtexPath);
		string newHash = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");

		UpdateMetadataConvertedTexture(workspacePath, pngFileName, rtexFileName, assetType, newHash, columns, rows);

		try
		{
			var attrs = File.GetAttributes(pngPath);
			if ((attrs & FileAttributes.ReadOnly) != 0)
				File.SetAttributes(pngPath, attrs & ~FileAttributes.ReadOnly);

			File.Delete(pngPath);
			GameLog.LogToConsole($"[MapWorkspaceService] Successfully converted '{pngFileName}' to '{rtexFileName}'.");
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapWorkspaceService] Failed to delete PNG {pngPath}: {ex.Message}");
		}
	}

	public static async Task EnsurePngAssetsConvertedCooperativeAsync(
		string workspacePath,
		Func<int, int, string, Task>? onTextureProgress = null)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			string[] pngFiles = Directory.GetFiles(assetsDir, "*.png", SearchOption.AllDirectories);
			if (pngFiles.Length == 0) return;

			var validPngs = pngFiles.Where(f => !MapWorkspacePaths.IsBuildOutputPath(f)).ToList();
			if (validPngs.Count == 0) return;

			GameContext.MetadataService.TryLoadMetadata(workspacePath, out var metadataRoot);

			for (int i = 0; i < validPngs.Count; i++)
			{
				string pngPath = validPngs[i];

				if (onTextureProgress != null)
				{
					try
					{
						await onTextureProgress(i + 1, validPngs.Count, Path.GetFileName(pngPath));
					}
					catch (Exception ex)
					{
						GameLog.LogError($"[MapWorkspaceService] Progress callback error: {ex.Message}");
					}
				}

				ProcessPngAsset(pngPath, workspacePath, metadataRoot, i, validPngs.Count);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapWorkspaceService] EnsurePngAssetsConvertedCooperativeAsync error: {ex.Message}");
		}
	}

	public static float ExtractRtexScaleFactor(string rtexPath)
	{
		if (string.IsNullOrEmpty(rtexPath) || !File.Exists(rtexPath)) return 1.0f;

		try
		{
			string? rtexMeta = RealmMetadataHelper.ExtractMetadata(rtexPath);
			if (string.IsNullOrEmpty(rtexMeta)) return 1.0f;

			var rNode = JsonNode.Parse(rtexMeta);
			if (rNode is not JsonObject rObj) return 1.0f;

			var keys = new[] { "Scale_Factor", "scale_factor", "scaleFactor", "ScaleFactor" };
			foreach (var key in keys)
			{
				if (rObj.TryGetPropertyValue(key, out var sfVal) &&
					float.TryParse(sfVal?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedScale))
				{
					return Math.Clamp(parsedScale, 0.10f, 4.0f);
				}
			}
		}
		catch
		{
		}

		return 1.0f;
	}

	public static bool NormalizeTextureEntries(JsonObject root, string? wsPath = null)
	{
		if (root == null || !(root["textures"] is JsonObject texturesObj)) return false;

		bool modified = false;
		modified |= ConvertSimpleHashesToObjects(texturesObj);
		modified |= EnsureUniqueSwatchIndices(texturesObj);
		modified |= EnsureScaleFactors(texturesObj, wsPath);

		return modified;
	}

	private static bool ConvertSimpleHashesToObjects(JsonObject texturesObj)
	{
		bool modified = false;
		var entriesToConvert = new List<(string Key, string Hash)>();

		foreach (var kvp in texturesObj)
		{
			if (kvp.Value is JsonValue val && val.TryGetValue<string>(out string? hashStr) && hashStr != null)
				entriesToConvert.Add((kvp.Key, hashStr));
		}

		foreach (var (k, h) in entriesToConvert)
		{
			texturesObj[k] = new JsonObject { ["hash"] = h };
			modified = true;
		}

		return modified;
	}

	private static void GatherUsedSwatchIndices(JsonObject texturesObj, HashSet<int> usedIndices)
	{
		foreach (var kvp in texturesObj)
		{
			if (kvp.Value is JsonObject texObj &&
				texObj.TryGetPropertyValue("swatchIndex", out var sIdxNode) &&
				sIdxNode != null && int.TryParse(sIdxNode.ToString(), out int parsedIdx) && parsedIdx >= 0)
			{
				usedIndices.Add(parsedIdx);
			}
		}
	}

	private static bool AssignMissingSwatchIndices(JsonObject texturesObj, HashSet<int> usedIndices)
	{
		bool modified = false;
		int nextAvailable = 0;
		foreach (var kvp in texturesObj)
		{
			if (kvp.Value is not JsonObject texObj) continue;

			bool hasValidIdx = texObj.TryGetPropertyValue("swatchIndex", out var sIdxNode) &&
							   sIdxNode != null && int.TryParse(sIdxNode.ToString(), out int parsedIdx) && parsedIdx >= 0;

			if (hasValidIdx) continue;

			while (usedIndices.Contains(nextAvailable))
				nextAvailable++;

			texObj["swatchIndex"] = nextAvailable;
			usedIndices.Add(nextAvailable);
			modified = true;
		}

		return modified;
	}

	private static bool EnsureUniqueSwatchIndices(JsonObject texturesObj)
	{
		var usedIndices = new HashSet<int>();
		GatherUsedSwatchIndices(texturesObj, usedIndices);
		return AssignMissingSwatchIndices(texturesObj, usedIndices);
	}

	private static bool EnsureScaleFactors(JsonObject texturesObj, string? wsPath)
	{
		bool modified = false;
		foreach (var kvp in texturesObj)
		{
			if (kvp.Value is not JsonObject texObj) continue;

			bool hasValidSf = texObj.TryGetPropertyValue("Scale_Factor", out var sfNode) &&
							  sfNode != null && float.TryParse(sfNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedSf) && parsedSf > 0.0001f;

			if (!hasValidSf)
			{
				float sf = CalculateTextureScaleFactor(kvp.Key, wsPath);
				texObj["Scale_Factor"] = sf;
				modified = true;
			}
		}

		return modified;
	}

	private static float CalculateTextureScaleFactor(string fileName, string? wsPath)
	{
		string rtexFileName = fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".rtex";
		string? rtexPath = FindRtexPath(rtexFileName, wsPath);

		if (string.IsNullOrEmpty(rtexPath) || !File.Exists(rtexPath)) return 1.0f;

		float rtexSf = ExtractRtexScaleFactor(rtexPath);
		if (rtexSf > 0.0001f && MathF.Abs(rtexSf - 1.0f) > 0.001f)
			return rtexSf;

		float calcSf = TextureConverter.CalculateLuminanceScaleFactor(rtexPath);
		return calcSf > 0.0001f ? calcSf : 1.0f;
	}

	private static string? FindRtexPath(string rtexFileName, string? wsPath)
	{
		if (!string.IsNullOrEmpty(wsPath))
		{
			string p1 = Path.Combine(wsPath, "Assets", "textures", rtexFileName);
			if (File.Exists(p1)) return p1;

			string p2 = Path.Combine(wsPath, rtexFileName);
			if (File.Exists(p2)) return p2;
		}

		string? found = PathUtils.FindPath($"Assets/textures/{rtexFileName}");
		if (!string.IsNullOrEmpty(found)) return found;

		return PathUtils.FindPath($"MapTemplate/Assets/textures/{rtexFileName}");
	}

	public static void NormalizeTextureEntries(MapManifestAssets assets, string workspacePath)
	{
		if (assets == null || string.IsNullOrEmpty(workspacePath)) return;
		MapAssetHelper.SaveAssetsToManifest(workspacePath, assets, removeFromMetadata: true);
	}

	public static void NormalizeMetadataTextureEntries(string workspacePath)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		try
		{
			var assets = MapAssetHelper.LoadAssets(workspacePath);
			NormalizeTextureEntries(assets, workspacePath);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapWorkspaceService] NormalizeMetadataTextureEntries error: {ex.Message}");
		}
	}

	public static void UpdateMetadataGlbHash(string workspacePath, string fileName, string newHash)
	{
		try
		{
			string category = MapAssetHelper.NormalizeCategoryKey("Prop");
			MapAssetHelper.UpdateManifestAsset(workspacePath, category, fileName, newHash);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapWorkspaceService] Failed to update manifest.json for {fileName}: {ex.Message}");
		}
	}

	public static void UpdateMetadataAnimationHash(string workspacePath, string animFileName, string newHash)
	{
		try
		{
			MapAssetHelper.UpdateManifestAsset(workspacePath, "animations", animFileName, newHash);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapWorkspaceService] Failed to update manifest.json for {animFileName}: {ex.Message}");
		}
	}

	public static void DeduplicateWorkspaceBuildArtifacts(string workspacePath)
	{
		MapArchiveHelper.DeduplicateWorkspaceBuildArtifacts(workspacePath);
	}
}
