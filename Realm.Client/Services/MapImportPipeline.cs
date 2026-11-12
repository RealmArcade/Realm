using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;

namespace Realm.Client.Services;

/// <summary>
///     Bringing a map into local storage: the packages shipped beside the executable, a .rmap archive
///     and a plain folder all end here, where the manifest is stored in the CAS, the files are
///     extracted or copied into the archive directory, and the result is validated before it is indexed.
/// </summary>
public static class MapImportPipeline
{
	private const string EmbeddedMapsDirectoryName = "EmbeddedMaps";

	public static int ImportEmbeddedMaps()
	{
		int importedCount = 0;
		foreach (string embeddedMapsDirectory in GetEmbeddedMapDirectories())
		{
			string[] archivePaths;
			try
			{
				archivePaths = Directory.GetFiles(embeddedMapsDirectory, "*.rmap", SearchOption.TopDirectoryOnly);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				MapAssetManager.LogErr($"[MapStorageService] Embedded map folder '{embeddedMapsDirectory}' could not be read: {exception.Message}");
				continue;
			}

			foreach (string archivePath in archivePaths)
			{
				if (ImportEmbeddedMap(archivePath))
				{
					importedCount++;
				}
			}
		}

		return importedCount;
	}

	private static IEnumerable<string> GetEmbeddedMapDirectories()
	{
		string executableDirectory = Path.GetDirectoryName(OS.GetExecutablePath()) ?? string.Empty;
		if (string.IsNullOrEmpty(executableDirectory) || !Directory.Exists(executableDirectory))
		{
			yield break;
		}

		string executableSiblingDirectory = Path.Combine(executableDirectory, EmbeddedMapsDirectoryName);
		if (Directory.Exists(executableSiblingDirectory))
		{
			yield return executableSiblingDirectory;
		}

		string[] exportDataDirectories;
		try
		{
			exportDataDirectories = Directory.GetDirectories(executableDirectory, "data_*", SearchOption.TopDirectoryOnly);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			MapAssetManager.LogErr($"[MapStorageService] Embedded map folders could not be discovered: {exception.Message}");
			yield break;
		}

		foreach (string exportDataDirectory in exportDataDirectories)
		{
			string exportEmbeddedMapsDirectory = Path.Combine(exportDataDirectory, EmbeddedMapsDirectoryName);
			if (Directory.Exists(exportEmbeddedMapsDirectory))
			{
				yield return exportEmbeddedMapsDirectory;
			}
		}
	}

	private static bool ImportEmbeddedMap(string archivePath)
	{
		string archiveFileName = Path.GetFileName(archivePath);

		try
		{
			using ZipArchive zipArchive = ZipFile.OpenRead(archivePath);
			var (manifestJson, _) = MapArchiveHelper.ReadManifestFromArchive(zipArchive);
			if (string.IsNullOrWhiteSpace(manifestJson))
			{
				MapAssetManager.LogErr($"[MapStorageService] Embedded map '{archiveFileName}' carries no manifest.json and was skipped.");
				return false;
			}

			var manifest = MapManifest.LoadFromJson(manifestJson);
			if (manifest == null)
			{
				MapAssetManager.LogErr($"[MapStorageService] Embedded map '{archiveFileName}' has a manifest.json that could not be parsed and was skipped.");
				return false;
			}

			var headerInfo = MapArchiveHelper.ReadHeaderFromManifest(manifest);
			(string mapTitle, string mapVersion) = DetermineMapTitleAndVersion(manifest, headerInfo, archivePath);
			manifest.MapName = mapTitle;
			manifest.Version = mapVersion;

			string manifestBlake3 = MapAssetManager.ComputeManifestBlake3(manifest);
			string targetDirectory = Path.Combine(MapAssetManager.GlobalArchiveDirectory, mapTitle, mapVersion, manifestBlake3);
			if (IsImportedMapComplete(targetDirectory, manifest))
			{
				MapAssetManager.Log($"[MapStorageService] Embedded map '{mapTitle}' v{mapVersion} is already imported.");
				return false;
			}

			var importResult = ImportMapFromArchiveAsync(archivePath, null).GetAwaiter().GetResult();
			if (!importResult.Success)
			{
				MapAssetManager.LogErr($"[MapStorageService] Embedded map '{mapTitle}' v{mapVersion} could not be imported: {importResult.Message}");
				return false;
			}

			MapAssetManager.Log($"[MapStorageService] Imported embedded map '{mapTitle}' v{mapVersion} from '{archiveFileName}'.");
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
		{
			MapAssetManager.LogErr($"[MapStorageService] Embedded map '{archiveFileName}' could not be imported: {exception.Message}");
			return false;
		}
	}

	private static bool IsImportedMapComplete(string targetDirectory, MapManifest manifest)
	{
		if (!Directory.Exists(targetDirectory) || !File.Exists(Path.Combine(targetDirectory, "manifest.json")))
		{
			return false;
		}

		if (!ValidateImportedMap(targetDirectory, manifest).IsValid)
		{
			return false;
		}

		return true;
	}

	public static Task<(bool Success, string Message, string? MapTitle, string? MapVersion)> ImportMapFromArchiveAsync(
		string archivePath,
		Action<float>? progressCallback)
	{
		using var zipArchive = ZipFile.OpenRead(archivePath);
		var (manifestJson, rootPrefix) = MapArchiveHelper.ReadManifestFromArchive(zipArchive);
		if (string.IsNullOrWhiteSpace(manifestJson))
		{
			return Task.FromResult((false, "No manifest.json found in the selected map package.", (string?)null, (string?)null));
		}

		var manifest = MapManifest.LoadFromJson(manifestJson);
		if (manifest == null)
		{
			return Task.FromResult((false, "Failed to parse manifest.json.", (string?)null, (string?)null));
		}

		var headerInfo = MapArchiveHelper.ReadHeaderFromManifest(manifest);
		(string mapTitle, string mapVersion) = DetermineMapTitleAndVersion(manifest, headerInfo, archivePath);
		manifest.MapName = mapTitle;
		manifest.Version = mapVersion;

		string manifestBlake3 = MapAssetManager.ComputeManifestBlake3(manifest);
		byte[] manifestBytes = Encoding.UTF8.GetBytes(manifest.ToJson());
		MapAssetManager.Storage.StoreAsset(manifestBytes, ".json", precomputedBlake3: manifestBlake3);

		string targetDirectory = Path.Combine(MapAssetManager.GlobalArchiveDirectory, mapTitle, mapVersion, manifestBlake3);
		if (Directory.Exists(targetDirectory))
		{
			Directory.Delete(targetDirectory, true);
		}

		Directory.CreateDirectory(targetDirectory);

		MapArchiveHelper.ExtractArchiveToCasAndTarget(
			zipArchive,
			MapAssetManager.Storage,
			targetDirectory,
			manifest,
			rootPrefix,
			p => progressCallback?.Invoke(p * 0.90f)
		);

		string targetManifestPath = Path.Combine(targetDirectory, "manifest.json");
		MapFileService.WriteAllTextAtomically(targetManifestPath, manifest.ToJson());

		var validation = ValidateImportedMap(targetDirectory, manifest);
		if (!validation.IsValid)
		{
			CleanupOnValidationFailure(targetDirectory);
			return Task.FromResult((false, validation.ErrorMessage, (string?)null, (string?)null));
		}

		progressCallback?.Invoke(0.95f);
		GameContext.AssetIndexService.RegisterManifest(manifest, targetManifestPath, isP2P: false);
		progressCallback?.Invoke(1.0f);
		return Task.FromResult((true, "Map imported successfully.", (string?)mapTitle, (string?)mapVersion));
	}

	public static (string MapTitle, string MapVersion) DetermineMapTitleAndVersion(MapManifest manifest, RmapHeaderInfo? headerInfo, string archivePath)
	{
		string mapTitle = headerInfo != null && !string.IsNullOrWhiteSpace(headerInfo.MapName)
			? headerInfo.MapName.Trim()
			: (!string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName.Trim() : Path.GetFileNameWithoutExtension(archivePath));
		string mapVersion = headerInfo != null && !string.IsNullOrWhiteSpace(headerInfo.Version)
			? headerInfo.Version.Trim()
			: (!string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0");
		return (mapTitle, mapVersion);
	}

	private static void CleanupOnValidationFailure(string targetDirectory)
	{
		try
		{
			if (Directory.Exists(targetDirectory))
			{
				Directory.Delete(targetDirectory, true);
			}
		}
		catch
		{
		}
	}

	public static Task<(bool Success, string Message, string? MapTitle, string? MapVersion)> ImportMapFromFolderAsync(
		string folderPath,
		Action<float>? progressCallback)
	{
		var manifestFiles = FindManifestFilesExcludingBackups(folderPath);
		if (manifestFiles.Count == 0)
		{
			return Task.FromResult((false, "No manifest.json found in the selected map folder.", (string?)null, (string?)null));
		}

		if (manifestFiles.Count > 1)
		{
			throw new InvalidOperationException($"Multiple manifest.json files found in the selected folder ({manifestFiles.Count} found). Import aborted.");
		}

		string manifestFilePath = manifestFiles[0];
		string manifestSourceDir = Path.GetDirectoryName(manifestFilePath) ?? folderPath;
		string manifestJson = File.ReadAllText(manifestFilePath);
		var manifest = MapManifest.LoadFromJson(manifestJson);
		if (manifest == null)
		{
			return Task.FromResult((false, "Failed to parse manifest.json.", (string?)null, (string?)null));
		}

		string mapTitle = !string.IsNullOrWhiteSpace(manifest.MapName)
			? manifest.MapName.Trim()
			: Path.GetFileName(manifestSourceDir) ?? "ImportedMap";
		string mapVersion = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";
		manifest.MapName = mapTitle;
		manifest.Version = mapVersion;

		string manifestBlake3 = MapAssetManager.ComputeManifestBlake3(manifest);
		byte[] manifestBytes = Encoding.UTF8.GetBytes(manifest.ToJson());
		MapAssetManager.Storage.StoreAsset(manifestBytes, ".json", precomputedBlake3: manifestBlake3);

		string targetDirectory = Path.Combine(MapAssetManager.GlobalArchiveDirectory, mapTitle, mapVersion, manifestBlake3);
		Directory.CreateDirectory(targetDirectory);

		string targetManifestPath = Path.Combine(targetDirectory, "manifest.json");
		MapFileService.WriteAllTextAtomically(targetManifestPath, manifest.ToJson());

		ProcessImportedManifestFiles(manifest, manifestSourceDir, targetDirectory, progressCallback);

		CopyFolderCandidateFiles(manifestSourceDir, targetDirectory, manifest);

		var validation = ValidateImportedMap(targetDirectory, manifest);
		if (!validation.IsValid)
		{
			CleanupOnValidationFailure(targetDirectory);
			return Task.FromResult((false, validation.ErrorMessage, (string?)null, (string?)null));
		}

		GameContext.AssetIndexService.RegisterManifest(manifest, targetManifestPath, isP2P: false);
		return Task.FromResult((true, "Map imported successfully.", (string?)mapTitle, (string?)mapVersion));
	}

	private static void ProcessImportedManifestFiles(MapManifest manifest, string manifestSourceDir, string targetDirectory, Action<float>? progressCallback)
	{
		if (manifest.Files == null) return;

		int totalFiles = manifest.Files.Count;
		int processed = 0;
		long lastProgressReportTicks = 0;

		foreach (var kvp in manifest.Files)
		{
			ProcessSingleImportedFile(kvp.Key, kvp.Value, manifestSourceDir, targetDirectory);

			processed++;
			if (totalFiles > 0)
			{
				long now = Environment.TickCount64;
				if (now - lastProgressReportTicks >= 100 || processed == totalFiles)
				{
					lastProgressReportTicks = now;
					progressCallback?.Invoke((float)processed / totalFiles);
				}
			}
		}
	}

	private static void ProcessSingleImportedFile(string fileKey, string hash, string manifestSourceDir, string targetDirectory)
	{
		string relativePath = GetRelativePath(fileKey);
		string normHash = ContentAddressableStorage.NormalizeBlake3Hash(hash);
		string candidateFilePath = GetCandidateFilePath(manifestSourceDir, relativePath);

		string? casFilePath = EnsureAssetInCas(candidateFilePath, normHash);

		CreateDestinationLink(targetDirectory, relativePath, casFilePath, candidateFilePath);
	}

	public static string GetRelativePath(string fileKey)
	{
		string relativePath = fileKey.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
			? fileKey.Substring(6)
			: fileKey;
		return relativePath.TrimStart('/', '\\');
	}

	private static string GetCandidateFilePath(string manifestSourceDir, string relativePath)
	{
		string candidateFilePath = Path.Combine(manifestSourceDir, relativePath);
		if (File.Exists(candidateFilePath)) return candidateFilePath;

		string fileName = Path.GetFileName(relativePath);
		string altPath = Path.Combine(manifestSourceDir, "Assets", fileName);
		return File.Exists(altPath) ? altPath : candidateFilePath;
	}

	private static string? EnsureAssetInCas(string candidateFilePath, string normHash)
	{
		string? casFilePath = MapAssetManager.Storage.FindAssetFilePath(normHash);
		if ((casFilePath == null || !File.Exists(casFilePath)) && File.Exists(candidateFilePath))
		{
			using var fileStream = new FileStream(candidateFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
			string ext = Path.GetExtension(candidateFilePath).ToLowerInvariant();
			MapAssetManager.Storage.StoreAsset(fileStream, ext, precomputedBlake3: normHash);
			casFilePath = MapAssetManager.Storage.FindAssetFilePath(normHash);
		}

		return casFilePath;
	}

	private static void CreateDestinationLink(string targetDirectory, string relativePath, string? casFilePath, string candidateFilePath)
	{
		string destFilePath = Path.Combine(targetDirectory, relativePath);
		string? destFileDir = Path.GetDirectoryName(destFilePath);
		if (!string.IsNullOrEmpty(destFileDir) && !Directory.Exists(destFileDir))
		{
			Directory.CreateDirectory(destFileDir);
		}

		if (casFilePath != null && File.Exists(casFilePath))
		{
			HardLinkHelper.CreateHardLinkOrCopy(destFilePath, casFilePath);
		}
		else if (File.Exists(candidateFilePath))
		{
			HardLinkHelper.CreateHardLinkOrCopy(destFilePath, candidateFilePath);
		}
	}

	private static void CopyFolderCandidateFiles(string sourceDir, string targetDir, MapManifest manifest)
	{
		if (!Directory.Exists(sourceDir))
		{
			return;
		}

		var candidateFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)
			.Where(file => ShouldCopyCandidateFile(file, sourceDir, manifest));

		foreach (string file in candidateFiles)
		{
			CopySingleCandidateFile(file, sourceDir, targetDir);
		}
	}

	private static bool ShouldCopyCandidateFile(string file, string sourceDir, MapManifest manifest)
	{
		string rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
		if (IsIgnoredRelativePath(rel))
		{
			return false;
		}

		return manifest.IsCandidateFile(rel);
	}

	private static bool IsIgnoredRelativePath(string rel)
	{
		return rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
		       rel.StartsWith(".backups/", StringComparison.OrdinalIgnoreCase) ||
		       rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
		       rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
		       rel.Equals("manifest.json", StringComparison.OrdinalIgnoreCase);
	}

	private static void CopySingleCandidateFile(string file, string sourceDir, string targetDir)
	{
		string rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
		string dest = Path.Combine(targetDir, rel.Replace('/', Path.DirectorySeparatorChar));
		string? parent = Path.GetDirectoryName(dest);

		if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
		{
			Directory.CreateDirectory(parent);
		}

		if (!File.Exists(dest))
		{
			HardLinkHelper.CreateHardLinkOrCopy(dest, file);
		}
	}

	public static (bool IsValid, string ErrorMessage) ValidateImportedMap(string targetDirectory, MapManifest manifest)
	{
		var wasmValidation = ValidateWasmFiles(targetDirectory);
		if (!wasmValidation.IsValid)
		{
			return wasmValidation;
		}

		var missingFilesValidation = ValidateMissingFiles(targetDirectory, manifest);
		if (!missingFilesValidation.IsValid)
		{
			return missingFilesValidation;
		}

		return (true, string.Empty);
	}

	public static (bool IsValid, string ErrorMessage) ValidateWasmFiles(string targetDirectory)
	{
		bool hasCsproj = Directory.GetFiles(targetDirectory, "*.csproj", SearchOption.TopDirectoryOnly).Length > 0;
		bool hasCsFiles = Directory.GetFiles(targetDirectory, "*.cs", SearchOption.AllDirectories).Any(f => !f.Contains("obj"));

		if (!hasCsproj && !hasCsFiles)
		{
			return (true, string.Empty);
		}

		var wasmFiles = Directory.GetFiles(targetDirectory, "*.wasm", SearchOption.AllDirectories)
			.Where(f => !f.Contains("native") && !f.Contains("obj"))
			.ToList();

		if (wasmFiles.Count == 0)
		{
			return (false, "Map is missing pre-compiled WASM binary. Maps containing scripts must be exported from the Map Editor before importing.");
		}

		return (true, string.Empty);
	}

	public static (bool IsValid, string ErrorMessage) ValidateMissingFiles(string targetDirectory, MapManifest manifest)
	{
		if (manifest.Files == null || manifest.Files.Count == 0)
		{
			return (true, string.Empty);
		}

		var missingFiles = new List<string>();
		foreach (var kvp in manifest.Files)
		{
			string rel = kvp.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? kvp.Key.Substring(6) : kvp.Key;
			rel = rel.TrimStart('/', '\\');
			string destFilePath = Path.Combine(targetDirectory, rel);
			if (!File.Exists(destFilePath))
			{
				string normHash = ContentAddressableStorage.NormalizeBlake3Hash(kvp.Value);
				string? casFilePath = MapAssetManager.Storage.FindAssetFilePath(normHash);
				if (casFilePath != null && File.Exists(casFilePath))
				{
					HardLinkHelper.CreateHardLinkOrCopy(destFilePath, casFilePath, overwrite: false);
				}
				else if (rel.StartsWith("Assets/models/", StringComparison.OrdinalIgnoreCase))
				{
					string fileName = Path.GetFileName(rel);
					string? diskPath = MapAssetHelper.FindModelOnDisk(targetDirectory, null, fileName);
					if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
					{
						HardLinkHelper.CreateHardLinkOrCopy(destFilePath, diskPath, overwrite: false);
					}
				}

				if (!File.Exists(destFilePath))
				{
					missingFiles.Add(rel);
				}
			}
		}

		if (missingFiles.Count > 0)
		{
			string missingFilesStr = string.Join(", ", missingFiles.Take(5));
			string suffix = missingFiles.Count > 5 ? "..." : "";
			return (false, $"Map is missing required asset files: {missingFilesStr}{suffix}");
		}

		return (true, string.Empty);
	}

	private static List<string> FindManifestFilesExcludingBackups(string rootDirectory)
	{
		var results = new List<string>();
		if (!Directory.Exists(rootDirectory))
		{
			return results;
		}

		var queue = new Queue<string>();
		queue.Enqueue(rootDirectory);

		while (queue.Count > 0)
		{
			string currentDir = queue.Dequeue();
			string candidate = Path.Combine(currentDir, "manifest.json");
			if (File.Exists(candidate))
			{
				results.Add(candidate);
			}

			try
			{
				foreach (string subDir in Directory.GetDirectories(currentDir))
				{
					string dirName = Path.GetFileName(subDir);
					if (string.Equals(dirName, ".backups", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					queue.Enqueue(subDir);
				}
			}
			catch
			{
			}
		}

		return results;
	}
}
