using Godot;
using System;
using System.IO;

namespace Realm.Client.Services;

public class MapSaveDataService
{
    public void WriteSavedData(string mapName, string fileName, string content)
    {
        if (IsInvalidPathString(fileName))
        {
            GD.PrintErr($"[Sandbox block] Blocked invalid or traversal save file path: '{fileName}'");
            return;
        }

        string mapNameOnly = GetMapNameOnly(mapName);
        if (IsInvalidPathString(mapNameOnly))
        {
            GD.PrintErr($"[Sandbox block] Blocked invalid or traversal map name: '{mapName}'");
            return;
        }

        string targetDir = Path.Combine(OS.GetUserDataDir(), "saved_data", mapNameOnly);
        Directory.CreateDirectory(targetDir);
        string targetFile = Path.Combine(targetDir, fileName);

        CreateBackupFile(mapNameOnly, fileName, targetFile);

        File.WriteAllText(targetFile, content);
    }

    public string ReadSavedData(string mapName, string fileName)
    {
        if (IsInvalidPathString(fileName))
        {
            GD.PrintErr($"[Sandbox block] Blocked invalid or traversal save file path: '{fileName}'");
            return string.Empty;
        }

        string mapNameOnly = GetMapNameOnly(mapName);
        if (IsInvalidPathString(mapNameOnly))
        {
            GD.PrintErr($"[Sandbox block] Blocked invalid or traversal map name: '{mapName}'");
            return string.Empty;
        }

        string targetDir = Path.Combine(OS.GetUserDataDir(), "saved_data", mapNameOnly);
        string targetFile = Path.Combine(targetDir, fileName);

        if (!File.Exists(targetFile))
        {
            return string.Empty;
        }

        return File.ReadAllText(targetFile);
    }

    public bool SavedDataExists(string mapName, string fileName)
    {
        if (IsInvalidPathString(fileName))
        {
            return false;
        }

        string mapNameOnly = GetMapNameOnly(mapName);
        if (IsInvalidPathString(mapNameOnly))
        {
            return false;
        }

        string targetFile = Path.Combine(OS.GetUserDataDir(), "saved_data", mapNameOnly, fileName);
        return File.Exists(targetFile);
    }

    private static bool IsInvalidPathString(string path)
    {
        return string.IsNullOrWhiteSpace(path) ||
               path.Contains("..") ||
               path.Contains('/') ||
               path.Contains('\\') ||
               path.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
    }

    private static string GetMapNameOnly(string mapName)
    {
        string mapNameOnly = Path.GetFileNameWithoutExtension(mapName);
        return string.IsNullOrWhiteSpace(mapNameOnly) ? "DefaultMap" : mapNameOnly;
    }

    private static void CreateBackupFile(string mapNameOnly, string fileName, string targetFile)
    {
        if (!File.Exists(targetFile))
        {
            return;
        }

        try
        {
            string backupsDir = Path.Combine(OS.GetUserDataDir(), "saved_data_backups", mapNameOnly);
            Directory.CreateDirectory(backupsDir);

            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
            string backupFileName = $"{fileNameWithoutExt}_{timestamp}.rsav";
            string backupPath = Path.Combine(backupsDir, backupFileName);

            File.Copy(targetFile, backupPath, true);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MapSaveDataService] Failed to create backup before overwrite: {ex.Message}");
        }
    }
}
