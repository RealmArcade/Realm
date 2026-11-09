using System;
using System.IO;
using Realm.Client.Utils;

namespace Realm.Client.Services;

public sealed class EditorWorkspace
{
	public string Directory { get; set; } = string.Empty;

	public bool HasDirectory => !string.IsNullOrEmpty(Directory);

	public string CurrentDirectoryBlake3 { get; set; } = string.Empty;

	public double AutoBackupElapsedSeconds { get; set; }

	public void Clear()
	{
		Directory = string.Empty;
	}

	/// <summary>
	///     Takes the folder the editor currently has as its source folder, if it has one and it still
	///     exists, and reports whether the workspace now points at it.
	/// </summary>
	public bool EnsureDirectory(string? sourceFolder)
	{
		if (HasDirectory) return false;
		if (string.IsNullOrEmpty(sourceFolder) || !System.IO.Directory.Exists(sourceFolder)) return false;

		Directory = Path.GetFullPath(sourceFolder);
		return true;
	}

	public void TouchModTime()
	{
		try
		{
			var wsDir = new DirectoryInfo(Directory);
			wsDir.LastWriteTimeUtc = DateTime.UtcNow;
		}
		catch
		{
		}
	}

	public bool TickBackupInterval(int intervalMins, double delta)
	{
		if (intervalMins <= 0) return false;
		AutoBackupElapsedSeconds += delta;
		double targetSeconds = intervalMins * 60.0;
		if (AutoBackupElapsedSeconds >= targetSeconds)
		{
			AutoBackupElapsedSeconds = 0;
			return true;
		}

		return false;
	}

	public static long GetLastWriteTimeTicks(string path)
	{
		if (!File.Exists(path)) return 0;
		return File.GetLastWriteTimeUtc(path).Ticks;
	}

	public static bool IsRestrictedSaveDirectory(string fullPath, string mapBackups, string userDir, string resDir)
	{
		if (fullPath.Equals(mapBackups, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(mapBackups + "/", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (fullPath.Equals(userDir, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(userDir + "/", StringComparison.OrdinalIgnoreCase) ||
			fullPath.Equals(resDir, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(resDir + "/", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
	}

	public static string GetDefaultDevelopmentMapDirectory(string mapName, string docsDir)
	{
		string cleanName = string.Join("_", mapName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
		if (string.IsNullOrEmpty(cleanName) || MapNaming.IsUntitledMapName(cleanName)) cleanName = "new_map";

		return Path.Combine(docsDir, cleanName);
	}

	public static void CreateMapDirectory(string mapName, string mapDir, string projectRoot, string templateDirSearch)
	{
		System.IO.Directory.CreateDirectory(mapDir);

		string templateDir = templateDirSearch;
		if (!System.IO.Directory.Exists(templateDir))
		{
			templateDir = Path.Combine(projectRoot, "..", "MapTemplate");
		}

		string templateScriptPath = Path.Combine(templateDir, "MapScript.cs");

		string scriptContent;
		if (File.Exists(templateScriptPath))
		{
			scriptContent = File.ReadAllText(templateScriptPath).Replace("__MAP_NAME__", mapName);
		}
		else
		{
			scriptContent = $@"namespace Realm.Maps;


public class {mapName} : IMapScript
{{
    public void Initialize(IGameAPI api)
    {{
    }}

    public void Update(IGameAPI api, float delta)
    {{
    }}
}}
";
		}

		File.WriteAllText(Path.Combine(mapDir, "MapScript.cs"), scriptContent);
		MapJsonFormatter.SaveFormattedJson(Path.Combine(mapDir, "metadata.json"), "{}");
		MapJsonFormatter.SaveFormattedJson(Path.Combine(mapDir, "terrain.json"), "{}");
	}

	public static string ChooseInitialDirectory(string? lastUsedFolder, string? currentSourceFolder, string lastOpenedFolder, string documentsDir, Func<string, bool> isRestrictedSaveDirectory)
	{
		if (!string.IsNullOrEmpty(lastUsedFolder) && !isRestrictedSaveDirectory(lastUsedFolder) && System.IO.Directory.Exists(lastUsedFolder))
		{
			return lastUsedFolder;
		}

		if (!string.IsNullOrEmpty(currentSourceFolder) && !isRestrictedSaveDirectory(currentSourceFolder) && System.IO.Directory.Exists(currentSourceFolder))
		{
			return currentSourceFolder;
		}

		if (!string.IsNullOrEmpty(lastOpenedFolder) && !isRestrictedSaveDirectory(lastOpenedFolder) && System.IO.Directory.Exists(lastOpenedFolder))
		{
			return lastOpenedFolder;
		}

		return documentsDir;
	}
}
