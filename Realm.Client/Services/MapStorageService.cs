using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using NSec.Cryptography;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;
using HttpClient = System.Net.Http.HttpClient;
using Key = NSec.Cryptography.Key;

namespace Realm.Client.Services;

public class MapStorageService
{
	public string GetExportMapDefaultFileName(string mapTitle, string mapVersion, string manifestBlake3)
	{
		string cleanMapName = string.Join("_", mapTitle.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (string.IsNullOrEmpty(cleanMapName) || MapNaming.IsUntitledMapName(cleanMapName)) cleanMapName = "MapExport";

		string cleanMapVersion = string.Join("_", mapVersion.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (string.IsNullOrEmpty(cleanMapVersion)) cleanMapVersion = "1.0.0";

		string normHash = !string.IsNullOrEmpty(manifestBlake3) ? ContentAddressableStorage.NormalizeBlake3Hash(manifestBlake3) : string.Empty;
		string shortHash = normHash.Length >= 4 ? normHash.Substring(0, 4) : (normHash.Length > 0 ? normHash : "0000");

		return $"{cleanMapName}_{cleanMapVersion}_{shortHash}.rmap";
	}

	public (bool Success, List<string> MissingAssets, List<(string RelativePath, long SizeBytes, double SizeMB)> OversizedAssets) ValidateWorkspaceForExportDomain(string wsPath)
	{
		MapAssetHelper.PruneNonExistentAssetsFromManifest(wsPath);
		var (assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(wsPath);
		if (!assetsValid)
		{
			return (false, missingAssets, new List<(string RelativePath, long SizeBytes, double SizeMB)>());
		}

		var (sizesValid, oversizedAssets) = MapAssetHelper.ValidateWorkspaceAssetSizes(wsPath);
		if (!sizesValid)
		{
			return (false, new List<string>(), oversizedAssets);
		}

		return (true, new List<string>(), new List<(string RelativePath, long SizeBytes, double SizeMB)>());
	}

	public string ResolveExportDestinationPath(string destinationPath, string mapTitle, string mapVersion, string manifestBlake3)
	{
		if (Directory.Exists(destinationPath))
		{
			return Path.Combine(destinationPath, GetExportMapDefaultFileName(mapTitle, mapVersion, manifestBlake3));
		}

		return destinationPath;
	}

	public string TryGetLocalRtexPath(int i, string wsPath, List<string> swatchPaths, List<string> swatchNames)
	{
		if (i >= 0 && i < swatchPaths.Count && !string.IsNullOrEmpty(swatchPaths[i]) && File.Exists(swatchPaths[i]))
		{
			return swatchPaths[i];
		}

		string texName = (i >= 0 && i < swatchNames.Count) ? swatchNames[i] : $"swatch_{i}";
		if (string.IsNullOrEmpty(texName) || texName.EndsWith("(Empty)")) return "";

		string rtexName = texName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? texName : $"{texName}.rtex";
		rtexName = ResolveRtexNameFromMetadata(rtexName, texName, wsPath);

		return ResolveLocalRtexFile(rtexName, wsPath);
	}

	public string ResolveRtexNameFromMetadata(string rtexName, string texName, string wsPath)
	{
		try
		{
			var metadata = MapFileService.LoadMetadata(wsPath);
			if (metadata != null)
			{
				var texMeta = metadata.GetTerrainTexture(texName) ?? metadata.GetTerrainTexture($"terrain/{texName}");
				if (texMeta != null && !string.IsNullOrWhiteSpace(texMeta.TexturePath))
				{
					string metaRtexName = texMeta.TexturePath;
					if (!metaRtexName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
					{
						metaRtexName += ".rtex";
					}

					return Path.GetFileName(metaRtexName);
				}
			}
		}
		catch
		{
		}

		return rtexName;
	}

	public string ResolveLocalRtexFile(string rtexName, string wsPath)
	{
		string localRtex = Path.Combine(wsPath, "Assets", "textures", rtexName);
		if (File.Exists(localRtex)) return localRtex;

		localRtex = Path.Combine(wsPath, rtexName);
		if (File.Exists(localRtex)) return localRtex;

		string? found = PathUtils.FindPath($"Assets/textures/{rtexName}");
		if (!string.IsNullOrEmpty(found) && File.Exists(found)) return found;

		string? foundTemplate = PathUtils.FindPath($"MapTemplate/Assets/textures/{rtexName}");
		if (!string.IsNullOrEmpty(foundTemplate) && File.Exists(foundTemplate)) return foundTemplate;

		return "";
	}

	public string CurrentMapDirectory { get; set; } = string.Empty;

	public static string FormatBytes(long bytes)
	{
		if (bytes < 1024) return $"{bytes} B";
		if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
		if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
		return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
	}

	public IReadOnlyList<DownloadedMapInfo> GetDownloadedMaps()
	{
		return DownloadedMapDiscovery.Collect(includeBundledMaps: false);
	}

	public IReadOnlyList<DownloadedMapInfo> GetMapsAvailableForImport()
	{
		return DownloadedMapDiscovery.Collect(includeBundledMaps: true);
	}

	public bool DeleteMapVersion(string mapTitle, string mapVersion, string? manifestHash = null)
	{
		if (string.IsNullOrWhiteSpace(mapTitle) || string.IsNullOrWhiteSpace(mapVersion))
		{
			return false;
		}

		string? targetDir = FindTargetDirectory(mapTitle, mapVersion, manifestHash);

		if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
		{
			return false;
		}

		try
		{
			Directory.Delete(targetDir, true);
			CleanupEmptyParentDirectories(targetDir);

			if (!string.IsNullOrEmpty(manifestHash))
			{
				MapAssetManager.Storage.RemoveSidecarCache(manifestHash);
			}

			MapAssetManager.PruneGlobalArchiveSync();
			return true;
		}
		catch (Exception ex)
		{
			MapAssetManager.LogErr($"[MapStorageService] Error deleting map version '{mapTitle}' v{mapVersion}: {ex.Message}");
			return false;
		}
	}

	private string? FindTargetDirectory(string mapTitle, string mapVersion, string? manifestHash)
	{
		string? manifestPath = MapAssetManager.FindManifestPath(mapTitle, mapVersion, manifestHash);
		if (manifestPath != null && File.Exists(manifestPath))
		{
			return Path.GetDirectoryName(manifestPath);
		}

		string globalArchive = MapAssetManager.GlobalArchiveDirectory;
		string candidate = Path.Combine(globalArchive, mapTitle, mapVersion);

		if (Directory.Exists(candidate))
		{
			return candidate;
		}

		if (!string.IsNullOrEmpty(manifestHash))
		{
			string normHash = ContentAddressableStorage.NormalizeBlake3Hash(manifestHash);
			string candidateWithHash = Path.Combine(globalArchive, mapTitle, mapVersion, normHash);
			if (Directory.Exists(candidateWithHash))
			{
				return candidateWithHash;
			}
		}

		return null;
	}

	private void CleanupEmptyParentDirectories(string targetDir)
	{
		string? parentDir = Path.GetDirectoryName(targetDir);
		if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir) && Directory.GetFileSystemEntries(parentDir).Length == 0)
		{
			Directory.Delete(parentDir, true);
			string? grandparentDir = Path.GetDirectoryName(parentDir);
			if (!string.IsNullOrEmpty(grandparentDir) && Directory.Exists(grandparentDir) && Directory.GetFileSystemEntries(grandparentDir).Length == 0)
			{
				Directory.Delete(grandparentDir, true);
			}
		}
	}

	public async Task<(bool HasNewer, string? NewerVersion, string? MapId)> CheckForNewerVersionAsync(
		string mapTitle,
		List<string> localVersions,
		string? registryServerUrl = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(mapTitle)) return (false, null, null);

		string serverUrl = GetRegistryServerUrl(registryServerUrl);
		var distClient = new DistributionClient(serverUrl);

		try
		{
			var discoveryResult = await CheckDiscoveryMapsAsync(distClient, mapTitle, localVersions, cancellationToken);
			if (discoveryResult.MapId != null) return discoveryResult;

			return await CheckManifestAsync(distClient, mapTitle, localVersions, cancellationToken);
		}
		catch
		{
			return (false, null, null);
		}
	}

	private string GetRegistryServerUrl(string? registryServerUrl)
	{
		return !string.IsNullOrWhiteSpace(registryServerUrl)
			? registryServerUrl
			: (GodotObject.IsInstanceValid(GameContext.Lobby) ? GameContext.Lobby.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl());
	}

	private async Task<(bool HasNewer, string? NewerVersion, string? MapId)> CheckDiscoveryMapsAsync(DistributionClient distClient, string mapTitle, List<string> localVersions, CancellationToken cancellationToken)
	{
		var discoveryMaps = await distClient.GetDiscoveryMapsAsync(cancellationToken);
		var matchingDto = discoveryMaps.FirstOrDefault(m =>
			string.Equals(m.Title, mapTitle, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(m.MapId, mapTitle, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(m.Title.Replace('_', ' '), mapTitle.Replace('_', ' '), StringComparison.OrdinalIgnoreCase));

		if (matchingDto != null && !string.IsNullOrWhiteSpace(matchingDto.Version))
		{
			bool isNewer = IsVersionNewer(matchingDto.Version, localVersions);
			return (isNewer, isNewer ? matchingDto.Version : null, matchingDto.MapId);
		}

		return (false, null, null);
	}

	private async Task<(bool HasNewer, string? NewerVersion, string? MapId)> CheckManifestAsync(DistributionClient distClient, string mapTitle, List<string> localVersions, CancellationToken cancellationToken)
	{
		var manifest = await distClient.GetManifestAsync(mapTitle, cancellationToken);
		if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Version))
		{
			bool isNewer = IsVersionNewer(manifest.Version, localVersions);
			return (isNewer, isNewer ? manifest.Version : null, mapTitle);
		}

		return (false, null, null);
	}

	public async Task<bool> DownloadUpdatedVersionAsync(
		string mapId,
		string? registryServerUrl = null,
		Action<float>? progressCallback = null,
		CancellationToken cancellationToken = default)
	{
		string serverUrl = !string.IsNullOrWhiteSpace(registryServerUrl)
			? registryServerUrl
			: (GodotObject.IsInstanceValid(GameContext.Lobby) ? GameContext.Lobby.RegistryServerUrl : ServersConfigHelper.GetDefaultServerUrl());

		var distClient = new MapDistributionClient();
		return await distClient.DownloadMapPackageFromRegistryAsync(mapId, serverUrl, progressCallback, cancellationToken);
	}

	public async Task<bool> ExportMapAsync(string sourceDirectory, string destinationRmapPath, Action<float, string>? progressCallback = null, int compressionLevel = 1)
	{
		if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
		{
			return false;
		}

		try
		{
			WorkspaceMetadataBootstrapper.EnsureLicenseFile(sourceDirectory);
			await Task.Run(() => MapArchiveHelper.CreateRmapArchive(
				sourceDirectory,
				destinationRmapPath,
				progressCallback: progressCallback,
				compressionLevel: compressionLevel));
			return true;
		}
		catch (Exception ex)
		{
			MapAssetManager.LogErr($"[MapStorageService] Export failed: {ex.Message}");
			return false;
		}
	}

	public int ImportEmbeddedMaps() => MapImportPipeline.ImportEmbeddedMaps();

	public Task<(bool Success, string Message, string? MapTitle, string? MapVersion)> ImportMapAsync(
		string sourcePath,
		Action<float>? progressCallback = null)
	{
		if (string.IsNullOrWhiteSpace(sourcePath))
		{
			return Task.FromResult((false, "Invalid source path provided.", (string?)null, (string?)null));
		}

		try
		{
			if (File.Exists(sourcePath))
			{
				if (sourcePath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase))
				{
					return MapImportPipeline.ImportMapFromArchiveAsync(sourcePath, progressCallback);
				}
				else if (sourcePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
				{
					string folder = Path.GetDirectoryName(sourcePath) ?? sourcePath;
					return MapImportPipeline.ImportMapFromFolderAsync(folder, progressCallback);
				}
				else
				{
					return Task.FromResult((false, "Unsupported file format. Please select an .rmap map package or a map folder.", (string?)null, (string?)null));
				}
			}
			else if (Directory.Exists(sourcePath))
			{
				return MapImportPipeline.ImportMapFromFolderAsync(sourcePath, progressCallback);
			}
			else
			{
				return Task.FromResult((false, $"Specified source path does not exist: {sourcePath}", (string?)null, (string?)null));
			}
		}
		catch (Exception ex)
		{
			MapAssetManager.LogErr($"[MapStorageService] Import failed: {ex.Message}");
			return Task.FromResult((false, $"Import failed: {ex.Message}", (string?)null, (string?)null));
		}
	}


	private static bool IsVersionNewer(string serverVersionStr, List<string> localVersionStrs)
	{
		if (string.IsNullOrWhiteSpace(serverVersionStr)) return false;

		if (!DownloadedMapDiscovery.TryParseVersion(serverVersionStr, out var serverVer))
		{
			return !localVersionStrs.Any(lv => string.Equals(lv.Trim(), serverVersionStr.Trim(), StringComparison.OrdinalIgnoreCase));
		}

		foreach (var localStr in localVersionStrs)
		{
			if (DownloadedMapDiscovery.TryParseVersion(localStr, out var localVer))
			{
				if (serverVer <= localVer)
				{
					return false;
				}
			}
		}

		return true;
	}


	public async Task SaveMapStateForExportAsync(string editingDirectory, bool isWindows)
	{
		string tempTerrainPath = Path.Combine(editingDirectory, "terrain.json");
		WorkspaceMetadataBootstrapper.EnsureLicenseFile(editingDirectory);
		MapWorkspaceService.EnsureWasmEntryPoint(editingDirectory);
		if (GameContext.Host is not { } host)
		{
			throw new InvalidOperationException("[MapStorageService] Cannot save map state for export without an active game host.");
		}

		host.SaveMapToFile(tempTerrainPath, performReload: false);
		host.EditorHasUnsavedChanges = false;

		if (isWindows && OperatingSystem.IsWindows())
		{
			await GameContext.VSCode.SaveAllOpenFilesAsync();
		}
	}

	public (bool assetsValid, List<string> missingAssets, bool sizesValid, List<(string RelativePath, long SizeBytes, double SizeMB)> oversizedAssets) ValidateMapAssetsForExport(string editingDirectory)
	{
		MapAssetHelper.PruneNonExistentAssetsFromManifest(editingDirectory);
		var (assetsValid, missingAssets) = MapAssetHelper.ValidateWorkspaceAssets(editingDirectory);
		var (sizesValid, oversizedAssets) = MapAssetHelper.ValidateWorkspaceAssetSizes(editingDirectory);
		return (assetsValid, missingAssets, sizesValid, oversizedAssets);
	}

	public static string? FindWasmPath(string editingDirectory)
	{
		return MapWorkspaceService.SearchForWasmInDirectoryDomain(Path.Combine(editingDirectory, "bin"));
	}

	public bool CheckCompiledWasmBinaryExists(string editingDirectory)
	{
		var csprojFiles = Directory.GetFiles(editingDirectory, "*.csproj", SearchOption.TopDirectoryOnly);
		if (csprojFiles.Length > 0 || File.Exists(Path.Combine(editingDirectory, "MapScript.cs")))
		{
			string? wasmPath = FindWasmPath(editingDirectory);

			if (string.IsNullOrEmpty(wasmPath) || !File.Exists(wasmPath))
			{
				return false;
			}
		}

		return true;
	}

	public void DeduplicateWorkspaceBuildArtifacts(string editingDirectory)
	{
		MapWorkspaceService.DeduplicateWorkspaceBuildArtifacts(editingDirectory);
	}

	public async Task OptimizeMapAssetsCooperativeAsync(string editingDirectory, Func<int, int, string, Task> progressCallback)
	{
		await MapWorkspaceService.EnsureGlbAssetsOptimizedCooperativeAsync(editingDirectory, progressCallback);
	}

	public async Task ConvertMapTexturesCooperativeAsync(string editingDirectory, Func<int, int, string, Task> progressCallback)
	{
		await MapWorkspaceService.EnsurePngAssetsConvertedCooperativeAsync(editingDirectory, progressCallback);
	}

	public void GenerateMapManifest(string editingDirectory, string mapTitle, string mapVersion, string author)
	{
		string metaJsonPath = Path.Combine(editingDirectory, "metadata.json");
		var extracted = ExtractTitleAndVersionFromMetadataFile(metaJsonPath, mapTitle, mapVersion);
		mapTitle = extracted.mapTitle;
		mapVersion = extracted.mapVersion;

		var manifest = MapManifest.CreateFromDirectory(editingDirectory, mapTitle, author, mapVersion);
		GameContext.MapAttributionService.ApplyAttribution(manifest, editingDirectory);
		string manifestJsonPath = Path.Combine(editingDirectory, "manifest.json");
		MapFileService.WriteAllTextAtomically(manifestJsonPath, manifest.ToJson());
	}

	public (string mapTitle, string mapVersion) ExtractTitleAndVersionFromMetadataFile(string metaJsonPath, string defaultTitle, string defaultVersion)
	{
		if (!File.Exists(metaJsonPath)) return (defaultTitle, defaultVersion);

		try
		{
			var doc = JsonNode.Parse(File.ReadAllText(metaJsonPath));
			if (doc == null) return (defaultTitle, defaultVersion);

			if (doc["MapProperties"] is JsonObject props)
			{
				string title = props["MapTitle"]?.ToString() ?? props["MapName"]?.ToString() ?? defaultTitle;
				string version = props["MapVersion"]?.ToString() ?? props["Version"]?.ToString() ?? defaultVersion;
				return (title, version);
			}
			else
			{
				string title = doc["MapName"]?.ToString() ?? doc["MapTitle"]?.ToString() ?? defaultTitle;
				string version = doc["Version"]?.ToString() ?? doc["MapVersion"]?.ToString() ?? defaultVersion;
				return (title, version);
			}
		}
		catch
		{
			return (defaultTitle, defaultVersion);
		}
	}

	public async Task<string> CreateMapArchiveAsync(string editingDirectory, string destinationPath, bool fullExport, Action<float, string> progressCallback)
	{
		var excludedPaths = !fullExport ? MapNormalizationHelper.GetExcludedRelativePaths(editingDirectory) : null;
		return await Task.Run(() => MapArchiveHelper.CreateRmapArchive(editingDirectory, destinationPath, progressCallback, compressionLevel: 1, fullExport: fullExport, excludedRelativePaths: excludedPaths));
	}

	public bool HasNewerSourceFiles(string workspace, DateTime wasmTime, string? currentSourceFolder)
	{
		var sourceDirs = new List<string> { workspace };
		if (!string.IsNullOrEmpty(currentSourceFolder) && Directory.Exists(currentSourceFolder) && !currentSourceFolder.Equals(workspace, StringComparison.OrdinalIgnoreCase))
		{
			sourceDirs.Add(currentSourceFolder);
		}

		foreach (var dir in sourceDirs)
		{
			var dependencyFiles = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
				.Where(f =>
				{
					string rel = f.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
					return !rel.StartsWith("bin", StringComparison.OrdinalIgnoreCase) && !rel.StartsWith("obj", StringComparison.OrdinalIgnoreCase);
				})
				.Concat(Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly))
				.Concat(Directory.GetFiles(dir, "metadata.json", SearchOption.TopDirectoryOnly))
				.Concat(Directory.Exists(Path.Combine(dir, "lib"))
					? Directory.GetFiles(Path.Combine(dir, "lib"), "*.dll", SearchOption.TopDirectoryOnly)
					: Array.Empty<string>())
				.Concat(Directory.Exists(Path.Combine(dir, "wit"))
					? Directory.GetFiles(Path.Combine(dir, "wit"), "*.wit", SearchOption.TopDirectoryOnly)
					: Array.Empty<string>());

			if (dependencyFiles.Any(f => File.GetLastWriteTimeUtc(f) > wasmTime))
			{
				return true;
			}
		}

		return false;
	}

	public bool CheckMapApiDependency(string workspace)
	{
		string mapApiDll = Path.Combine(workspace, "lib", "Realm.MapAPI.dll");
		if (!File.Exists(mapApiDll))
		{
			return false;
		}

		return true;
	}

	public async Task<(bool success, int exitCode, bool streamHadErrors)> ExecuteDotnetPublishDomainAsync(string workspace, string csproj, Action<string> onOutput, Action<string> onError, bool isDebug = false)
	{
		return await Task.Run(() =>
		{
			bool streamHadErrors = false;
			string resolvedWasiSdk = WasiSdkResolver.ResolveWasiSdkPath();
			string csprojName = Path.GetFileName(csproj);
			string configuration = isDebug ? MapWorkspaceService.DebugBuildFlavor : MapWorkspaceService.ReleaseBuildFlavor;
			string scriptOutputDirectory = MapWorkspaceService.GetScriptOutputDirectory(workspace);
			string dotnetExecutablePath = DotnetSdkResolver.ResolveSdkExecutablePath();
			if (string.IsNullOrEmpty(dotnetExecutablePath))
			{
				onError?.Invoke("[COMPILER ERROR] The .NET SDK that compiles map scripts is not installed. Choose Test Map again once the background download completes.");
				return (false, -1, true);
			}

			ClearScriptOutput(workspace, scriptOutputDirectory);

			var compileProcess = new Process();
			compileProcess.StartInfo.FileName = dotnetExecutablePath;
			compileProcess.StartInfo.Arguments = $"publish \"{csprojName}\" -c {configuration} -r wasi-wasm -o \"{scriptOutputDirectory}\" -p:WASI_SDK_PATH=\"{resolvedWasiSdk}\"{ResolveBuildPropertyOverrides(isDebug)}";
			compileProcess.StartInfo.EnvironmentVariables["WASI_SDK_PATH"] = resolvedWasiSdk;
			compileProcess.StartInfo.WorkingDirectory = workspace;
			compileProcess.StartInfo.CreateNoWindow = true;
			compileProcess.StartInfo.UseShellExecute = false;
			compileProcess.StartInfo.RedirectStandardOutput = true;
			compileProcess.StartInfo.RedirectStandardError = true;

			compileProcess.OutputDataReceived += (s, e) =>
			{
				if (!string.IsNullOrEmpty(e.Data))
				{
					if (e.Data.Contains(": error ") || e.Data.Contains("Build FAILED")) streamHadErrors = true;
					onOutput?.Invoke(e.Data);
				}
			};

			compileProcess.ErrorDataReceived += (s, e) =>
			{
				if (!string.IsNullOrEmpty(e.Data))
				{
					if (e.Data.Contains(": error ") || e.Data.Contains("Build FAILED"))
					{
						streamHadErrors = true;
						onError?.Invoke("[COMPILER ERROR] " + e.Data);
					}
					else if (e.Data.Contains(": warning ")) onError?.Invoke("[COMPILER WARNING] " + e.Data);
					else onError?.Invoke(e.Data);
				}
			};

			try
			{
				compileProcess.Start();
			}
			catch (Exception exception)
			{
				onError?.Invoke("[COMPILER ERROR] The map compiler could not be started: " + exception.Message);
				return (false, -1, true);
			}

			compileProcess.BeginOutputReadLine();
			compileProcess.BeginErrorReadLine();
			compileProcess.WaitForExit();

			bool succeeded = compileProcess.ExitCode == 0 && !streamHadErrors;
			if (succeeded) RecordScriptOutput(workspace, scriptOutputDirectory, isDebug);

			return (succeeded, compileProcess.ExitCode, streamHadErrors);
		});
	}

	private const string DebugBuildPropertyOverrides = " -p:Optimize=false -p:DebugSymbols=true -p:DebugType=embedded -p:StripSymbols=false -p:IlcGenerateCompleteTypeMetadata=true";

	private const string ReleaseBuildPropertyOverrides = " -p:Optimize=true -p:DebugSymbols=true -p:DebugType=embedded -p:StripSymbols=false";

	private static string ResolveBuildPropertyOverrides(bool isDebug) => isDebug ? DebugBuildPropertyOverrides : ReleaseBuildPropertyOverrides;

	private static void ClearScriptOutput(string workspace, string scriptOutputDirectory)
	{
		try
		{
			if (Directory.Exists(scriptOutputDirectory)) Directory.Delete(scriptOutputDirectory, true);
			string legacyBinRoot = Path.Combine(workspace, "bin");
			if (!Directory.Exists(legacyBinRoot)) return;

			foreach (string strayScript in Directory.GetFiles(legacyBinRoot, "*.wasm", SearchOption.TopDirectoryOnly))
			{
				File.Delete(strayScript);
			}
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapStorageService] Could not clear the previous script output: {exception.Message}");
		}
	}

	private static void RecordScriptOutput(string workspace, string scriptOutputDirectory, bool isDebug)
	{
		try
		{
			MapWorkspaceService.WriteBuildFlavorDomain(workspace, isDebug);

			foreach (string copiedLibraryFile in Directory.GetFiles(scriptOutputDirectory, "Realm.*", SearchOption.TopDirectoryOnly))
			{
				File.Delete(copiedLibraryFile);
			}
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapStorageService] Could not record the script output of '{workspace}': {exception.Message}");
		}
	}

	public async Task<bool> SignMapMetadataAsync(string workspace, string mapTitle, string mapVersion, string authorName, Key authorshipKey, string publicKeyBase64, string seedServerUrl)
	{
		string metadataJsonPath = Path.Combine(workspace, "metadata.json");
		if (!File.Exists(metadataJsonPath)) return false;

		try
		{
			if (JsonNode.Parse(File.ReadAllText(metadataJsonPath)) is not JsonObject mapDoc) return false;

			var contributorsList = ExtractExistingContributors(mapDoc);
			if (!string.IsNullOrWhiteSpace(authorName)) contributorsList.Add(authorName);

			var authorCounts = await CollectWorkspaceAssetAuthorsAsync(workspace, mapTitle, mapVersion, authorName, seedServerUrl, contributorsList);

			UpdateMetadataContributorsAndAttributions(mapDoc, contributorsList, authorCounts, publicKeyBase64);

			string lineEnding = MapJsonFormatter.DetectLineEnding(metadataJsonPath);
			byte[] unsignedBytes = Encoding.UTF8.GetBytes(ApplyLineEnding(MapJsonFormatter.FormatNode(mapDoc), lineEnding));
			string mapBlake3 = RealmMetadataHelper.ComputeBlake3(unsignedBytes, ".json");
			byte[] signatureBytes = SignatureAlgorithm.Ed25519.Sign(authorshipKey, Encoding.UTF8.GetBytes($"{mapBlake3}.json"));

			mapDoc["signature"] = Convert.ToBase64String(signatureBytes);
			AtomicFileWriter.WriteAllBytes(metadataJsonPath, Encoding.UTF8.GetBytes(ApplyLineEnding(MapJsonFormatter.FormatNode(mapDoc), lineEnding)));
			MapFileService.NotifyFileWritten(metadataJsonPath);
			return true;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapStorageService] Could not sign '{metadataJsonPath}': {exception.Message}");
			return false;
		}
	}

	private static string ApplyLineEnding(string text, string lineEnding)
	{
		return lineEnding == "\r\n" ? text.Replace("\n", "\r\n") : text;
	}

	private async Task<Dictionary<string, int>> CollectWorkspaceAssetAuthorsAsync(string workspace, string mapTitle, string mapVersion, string authorName, string seedServerUrl, HashSet<string> contributorsList)
	{
		var authorCounts = new Dictionary<string, int>();
		if (string.IsNullOrWhiteSpace(seedServerUrl)) return authorCounts;

		List<string> referencedHashes;
		try
		{
			referencedHashes = MapManifest.CreateFromDirectory(workspace, mapTitle, authorName, mapVersion).Files.Values.ToList();
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapStorageService] Could not index the assets of '{workspace}' for attribution: {exception.Message}");
			return authorCounts;
		}

		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
		return await CollectAssetAuthorsAsync(referencedHashes, seedServerUrl, httpClient, contributorsList);
	}

	public HashSet<string> ExtractExistingContributors(JsonObject mapDoc)
	{
		var contributorsList = new HashSet<string>();
		if (mapDoc.TryGetPropertyValue("Contributors", out var contNode) && contNode is JsonArray arr)
		{
			foreach (var node in arr)
			{
				if (node != null) contributorsList.Add(node.GetValue<string>());
			}
		}

		return contributorsList;
	}

	public async Task<Dictionary<string, int>> CollectAssetAuthorsAsync(List<string> referencedHashes, string seedServerUrl, HttpClient httpClient, HashSet<string> contributorsList)
	{
		var authorCounts = new Dictionary<string, int>();
		foreach (var hash in referencedHashes)
		{
			await ProcessAssetAuthorAsync(hash, seedServerUrl, httpClient, contributorsList, authorCounts);
		}

		return authorCounts;
	}

	public async Task ProcessAssetAuthorAsync(string hash, string seedServerUrl, HttpClient httpClient, HashSet<string> contributorsList, Dictionary<string, int> authorCounts)
	{
		try
		{
			var assetAuthorRes = await httpClient.GetAsync(seedServerUrl + "/api/publish_map/asset_author/" + hash);
			if (!assetAuthorRes.IsSuccessStatusCode) return;

			var assetAuthorJson = await assetAuthorRes.Content.ReadAsStringAsync();
			var assetMeta = JsonNode.Parse(assetAuthorJson);
			string? author = assetMeta?["AuthorUsername"]?.GetValue<string>();

			if (string.IsNullOrEmpty(author)) return;

			contributorsList.Add(author);
			if (!authorCounts.ContainsKey(author)) authorCounts[author] = 0;
			authorCounts[author]++;
		}
		catch
		{
		}
	}

	public void UpdateMetadataContributorsAndAttributions(JsonObject mapDoc, HashSet<string> contributorsList, Dictionary<string, int> authorCounts, string pubKeyStr)
	{
		var newContributorsArr = new JsonArray();
		foreach (var cont in contributorsList) newContributorsArr.Add(cont);
		mapDoc["Contributors"] = newContributorsArr;

		var attributionsArr = new JsonArray();
		var sortedAuthors = authorCounts.Keys.ToList();
		sortedAuthors.Sort((a, b) => authorCounts[b].CompareTo(authorCounts[a]));
		foreach (var a in sortedAuthors) attributionsArr.Add(a);
		mapDoc["Attributions"] = attributionsArr;
		mapDoc["EngineVersion"] = RealmVersion.GameBinaryVersion;
		mapDoc["author_key"] = pubKeyStr;
		if (mapDoc.ContainsKey("signature")) mapDoc.Remove("signature");
	}
	public string ResolveMapDirectoryDomain(string customPath, string currentDir, string defaultPath, bool ensureGlbOptimized, Func<string, string> globalizePath)
	{
		string path = string.IsNullOrEmpty(customPath) ? Path.Combine(currentDir ?? defaultPath, "terrain.json") : customPath;
		string absolutePath = globalizePath(path);
		if (Path.GetDirectoryName(absolutePath) is { } mapDirectory)
		{
			this.CurrentMapDirectory = mapDirectory;
			if (ensureGlbOptimized)
			{
				MapWorkspaceService.EnsureGlbAssetsOptimized(mapDirectory);
				MapWorkspaceService.EnsurePngAssetsConverted(mapDirectory);
			}
		}

		return absolutePath;
	}
}