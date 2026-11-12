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
		ComposeReferencedScripts(directory);
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

	/// <summary>
	///     Imports the C# of the maps this folder references so the workspace compiles with those rules in it. A save does
	///     the same thing, and this runs when a folder is opened so a freshly cloned map compiles before its first save.
	/// </summary>
	/// <param name="directory">The map folder being opened.</param>
	public static void ComposeReferencedScripts(string directory)
	{
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

		MapReferences references = MapReferences.Load(directory);
		if (references == null || references.IsEmpty)
		{
			MapScriptComposer.Compose(directory, null, null);
			return;
		}

		MapReferenceService referenceService = GameContext.MapReferenceService;
		if (referenceService == null) return;

		List<ComposedScriptMap>? composed = MapScriptComposer.Compose(directory, references.References, reference =>
		{
			string helperFolder = referenceService.ResolveHelperFolder(reference);
			return string.IsNullOrEmpty(helperFolder) ? referenceService.ResolveStoreFolder(reference) : helperFolder;
		});

		if (composed == null)
		{
			GameLog.LogError(
				$"[MapWorkspaceService] A map that '{directory}' references could not be found, so the composed scripts already in the workspace were kept. " +
				"Download the referenced map or stop referencing it before the script will compile again.");
		}
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

	public static void EnsurePngAssetsConverted(string workspacePath)
	{
		MapPngConversion.EnsurePngAssetsConverted(workspacePath);
	}

	public static Task EnsurePngAssetsConvertedCooperativeAsync(
		string workspacePath,
		Func<int, int, string, Task>? onTextureProgress = null)
	{
		return MapPngConversion.EnsurePngAssetsConvertedCooperativeAsync(workspacePath, onTextureProgress);
	}
	public static float ExtractRtexScaleFactor(string rtexPath)
	{
		return MapTextureMetadata.ExtractRtexScaleFactor(rtexPath);
	}

	public static bool NormalizeTextureEntries(JsonObject root, string? wsPath = null)
	{
		return MapTextureMetadata.NormalizeTextureEntries(root, wsPath);
	}

	public static void NormalizeTextureEntries(MapManifestAssets assets, string workspacePath)
	{
		MapTextureMetadata.NormalizeTextureEntries(assets, workspacePath);
	}

	public static void NormalizeMetadataTextureEntries(string workspacePath)
	{
		MapTextureMetadata.NormalizeMetadataTextureEntries(workspacePath);
	}

	public static void UpdateMetadataGlbHash(string workspacePath, string fileName, string newHash)
	{
		MapTextureMetadata.UpdateMetadataGlbHash(workspacePath, fileName, newHash);
	}

	public static void UpdateMetadataAnimationHash(string workspacePath, string animFileName, string newHash)
	{
		MapTextureMetadata.UpdateMetadataAnimationHash(workspacePath, animFileName, newHash);
	}

	public static void DeduplicateWorkspaceBuildArtifacts(string workspacePath)
	{
		MapTextureMetadata.DeduplicateWorkspaceBuildArtifacts(workspacePath);
	}
}
