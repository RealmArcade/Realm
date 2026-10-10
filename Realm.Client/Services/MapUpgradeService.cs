using Godot;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.Distribution;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Realm.Client.Services;

public class MapUpgradeService
{
	public static void MergeCategoryInto(MapManifestAssets targetAssets, string category, JsonObject sourceObject)
	{
		var categoryTarget = targetAssets.GetCategory(category);
		if (categoryTarget == null)
		{
			categoryTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			targetAssets.SetCategory(category, categoryTarget);
		}

		foreach (var pair in sourceObject)
		{
			if (pair.Value is JsonObject sourceObj)
			{
				string hash = sourceObj["hash"]?.ToString() ?? sourceObj["blake3"]?.ToString() ?? string.Empty;
				if (!string.IsNullOrEmpty(hash))
				{
					categoryTarget[pair.Key] = hash;
				}
			}
			else if (pair.Value != null)
			{
				categoryTarget[pair.Key] = pair.Value.ToString();
			}
		}
	}
	private readonly WorldAccessor _worldAccessor;
	private readonly List<IMapMigration> _migrations = new();

	public static MapUpgradeService Instance => ServiceLocator.Get<MapUpgradeService>();

	public MapUpgradeService(WorldAccessor worldAccessor)
	{
		_worldAccessor = worldAccessor;
		RegisterMigrations();
	}

	private void RegisterMigrations()
	{
		_migrations.Add(new Migration_0_0_1_InitialCanonicalFormat());
		_migrations.Add(new Migration_0_0_2_NormalizeModelProperties());
		_migrations.Add(new Migration_0_0_3_WaterProfilesAndShaders());
		_migrations.Add(new Migration_0_0_4_TemplateIDPrefixes());
	}

	public string GetMapBuildNumber(string mapDirectory)
	{
		if (string.IsNullOrEmpty(mapDirectory))
		{
			return "v0.0.0";
		}

		string metadataPath = File.Exists(mapDirectory) && Path.GetFileName(mapDirectory).Equals("metadata.json", StringComparison.OrdinalIgnoreCase)
			? mapDirectory
			: Path.Combine(mapDirectory, "metadata.json");

		if (!File.Exists(metadataPath))
		{
			return "v0.0.0";
		}

		try
		{
			string json = File.ReadAllText(metadataPath);
			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty("GameBuildNumber", out var prop) && prop.ValueKind == JsonValueKind.String)
			{
				string? val = prop.GetString();
				if (!string.IsNullOrWhiteSpace(val))
				{
					return val.Trim();
				}
			}
		}
		catch
		{
		}

		return "v0.0.0";
	}

	public bool NeedsUpgrade(string mapDirectory, out string currentVersion, out string targetVersion)
	{
		currentVersion = GetMapBuildNumber(mapDirectory);
		targetVersion = RealmVersion.GameBuildNumber;
		return !string.Equals(currentVersion, targetVersion, StringComparison.OrdinalIgnoreCase);
	}

	public bool IsMapNewerThanGame(string mapVersionOrDirectory, string? gameVersion = null)
	{
		string mapBuildNumber = mapVersionOrDirectory;
		if (!string.IsNullOrEmpty(mapVersionOrDirectory) && (Directory.Exists(mapVersionOrDirectory) || File.Exists(mapVersionOrDirectory)))
		{
			mapBuildNumber = GetMapBuildNumber(mapVersionOrDirectory);
		}

		string targetGameVersion = !string.IsNullOrWhiteSpace(gameVersion) ? gameVersion : RealmVersion.GameBuildNumber;
		var parsedMapVersion = ParseBuildVersion(mapBuildNumber);
		var parsedGameVersion = ParseBuildVersion(targetGameVersion);

		return parsedMapVersion.CompareTo(parsedGameVersion) > 0;
	}

	public static Version ParseBuildVersion(string? versionString)
	{
		if (string.IsNullOrWhiteSpace(versionString)) return new Version(0, 0, 0, 0);

		string cleaned = CleanVersionString(versionString);

		if (Version.TryParse(cleaned, out var parsedVersion))
		{
			return new Version(
				Math.Max(0, parsedVersion.Major),
				Math.Max(0, parsedVersion.Minor),
				Math.Max(0, parsedVersion.Build),
				parsedVersion.Revision >= 0 ? parsedVersion.Revision : 0
			);
		}

		return ParseVersionFallback(cleaned);
	}

	private static string CleanVersionString(string versionString)
	{
		string cleaned = versionString.Trim().TrimStart('v', 'V').Trim();
		int separatorIndex = cleaned.IndexOfAny(new[] { '-', '_', '+', ' ', '(' });
		if (separatorIndex >= 0)
		{
			cleaned = cleaned.Substring(0, separatorIndex).Trim();
		}
		return cleaned;
	}

	private static Version ParseVersionFallback(string cleaned)
	{
		var parts = cleaned.Split('.');
		int[] parsed = new int[4];

		for (int i = 0; i < parts.Length && i < 4; i++)
		{
			if (int.TryParse(parts[i], out int val))
			{
				parsed[i] = val;
			}
		}

		if (parts.Length > 0 && int.TryParse(parts[0], out _))
		{
			return new Version(parsed[0], parsed[1], parsed[2], parsed[3]);
		}
		
		return new Version(0, 0, 0, 0);
	}

	public List<IMapMigration> GetPendingMigrations(string currentVersion, string targetVersion)
	{
		var pending = new List<IMapMigration>();
		string current = string.IsNullOrWhiteSpace(currentVersion) ? "v0.0.0" : currentVersion.Trim();
		string target = targetVersion.Trim();

		while (!string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
		{
			var nextMigration = _migrations.FirstOrDefault(m => string.Equals(m.FromVersion, current, StringComparison.OrdinalIgnoreCase));
			if (nextMigration == null)
			{
				break;
			}
			pending.Add(nextMigration);
			current = nextMigration.ToVersion;
		}

		return pending;
	}

	public UpgradeResult UpgradeMap(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		string initialVersion = GetMapBuildNumber(mapDirectory);
		string targetVersion = RealmVersion.GameBuildNumber;

		if (IsMapNewerThanGame(initialVersion, targetVersion))
		{
			return new UpgradeResult
			{
				Success = false,
				ErrorMessage = $"Map build {initialVersion} is newer than current game build {targetVersion}. Downgrading is not supported.",
				InitialVersion = initialVersion,
				FinalVersion = initialVersion
			};
		}

		var pendingMigrations = GetPendingMigrations(initialVersion, targetVersion);
		if (pendingMigrations.Count == 0)
		{
			return new UpgradeResult
			{
				Success = true,
				InitialVersion = initialVersion,
				FinalVersion = initialVersion,
				StepResults = new List<MigrationResult>()
			};
		}

		CreateMapBackup(mapDirectory, initialVersion);

		var result = new UpgradeResult
		{
			InitialVersion = initialVersion,
			FinalVersion = initialVersion
		};

		for (int i = 0; i < pendingMigrations.Count; i++)
		{
			var migration = pendingMigrations[i];
			progress?.Report(new MigrationProgressUpdate(
				migration.Description,
				i + 1,
				pendingMigrations.Count,
				$"Applying migration {migration.FromVersion} -> {migration.ToVersion}..."
			));

			var stepResult = migration.Up(mapDirectory, progress);
			result.StepResults.Add(stepResult);

			if (!stepResult.Success)
			{
				result.Success = false;
				result.ErrorMessage = stepResult.ErrorMessage ?? $"Failed at migration {migration.FromVersion} -> {migration.ToVersion}";
				return result;
			}

			result.FinalVersion = migration.ToVersion;
		}

		result.Success = string.Equals(result.FinalVersion, targetVersion, StringComparison.OrdinalIgnoreCase);
		return result;
	}

	public Task<UpgradeResult> UpgradeMapAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		return Task.Run(() => UpgradeMap(mapDirectory, progress));
	}

	private static void CreateMapBackup(string mapDirectory, string currentVersion)
	{
		try
		{
			string loadedFolderName = Path.GetFileName(mapDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (string.IsNullOrEmpty(loadedFolderName))
			{
				loadedFolderName = "map";
			}

			string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string userDataDir;
			try
			{
				userDataDir = OS.GetUserDataDir();
				if (string.IsNullOrEmpty(userDataDir))
				{
					userDataDir = ProjectSettings.GlobalizePath("user://");
				}
			}
			catch
			{
				string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
				userDataDir = Path.Combine(appData, "Godot", "app_userdata", "Realm");
			}

			string fullUserDataDir = Path.GetFullPath(userDataDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string upgradeBackupsRoot = Path.GetFullPath(Path.Combine(fullUserDataDir, "map_upgrades")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string targetBackupDir = Path.GetFullPath(Path.Combine(upgradeBackupsRoot, $"{loadedFolderName}_{timestamp}")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			CopyDirectoryContentsSafe(mapDirectory, targetBackupDir, upgradeBackupsRoot);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapUpgradeService] Warning: Failed to create map backup: {ex.Message}");
		}
	}

	private static void CopyDirectoryContentsSafe(string sourceDir, string targetDir, string? backupsRoot = null)
	{
		var source = new DirectoryInfo(sourceDir);
		if (!source.Exists) return;

		string normalizedTarget = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string? normalizedBackupsRoot = !string.IsNullOrEmpty(backupsRoot) ? Path.GetFullPath(backupsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : null;

		Directory.CreateDirectory(targetDir);
		CopyFilesSafe(source, targetDir);
		CopySubDirectoriesSafe(source, targetDir, normalizedTarget, normalizedBackupsRoot, backupsRoot);
	}

	private static void CopyFilesSafe(DirectoryInfo source, string targetDir)
	{
		foreach (var file in source.GetFiles())
		{
			if (file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
			file.CopyTo(Path.Combine(targetDir, file.Name), true);
		}
	}

	private static void CopySubDirectoriesSafe(DirectoryInfo source, string targetDir, string normalizedTarget, string? normalizedBackupsRoot, string? backupsRoot)
	{
		var excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".git", "bin", "obj", ".godot", ".vs", ".vscode", ".idea", "map_upgrades", "map_backups", "backups", ".backups", ".dotnet", ".wasi", ".sidecarcache", ".cache"
		};

		foreach (var dir in source.GetDirectories())
		{
			if (excludedFolders.Contains(dir.Name) || dir.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase)) continue;

			string subDirFull = Path.GetFullPath(dir.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			if (ShouldIgnoreDirectory(subDirFull, normalizedTarget, normalizedBackupsRoot)) continue;

			CopyDirectoryContentsSafe(dir.FullName, Path.Combine(targetDir, dir.Name), backupsRoot);
		}
	}

	private static bool ShouldIgnoreDirectory(string subDirFull, string normalizedTarget, string? normalizedBackupsRoot)
	{
		if (subDirFull.IndexOf("map_backups", StringComparison.OrdinalIgnoreCase) >= 0 ||
			subDirFull.IndexOf("map_upgrades", StringComparison.OrdinalIgnoreCase) >= 0 ||
			subDirFull.IndexOf(".backups", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}

		if (!string.IsNullOrEmpty(normalizedBackupsRoot) &&
			(string.Equals(subDirFull, normalizedBackupsRoot, StringComparison.OrdinalIgnoreCase) ||
			 subDirFull.StartsWith(normalizedBackupsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
		{
			return true;
		}

		if (string.Equals(subDirFull, normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
			subDirFull.StartsWith(normalizedTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
			normalizedTarget.StartsWith(subDirFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
	}
}
