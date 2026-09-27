using System;
using System.IO;
using Godot;

namespace Realm.Godot.Services;

public class MapSaveDataService
{
    public void WriteSavedData(string mapName, string fileName, string content)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..") ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            GD.PrintErr($"[Sandbox block] Blocked invalid or traversal save file path: '{fileName}'");
            return;
        }

        string mapNameOnly = Path.GetFileNameWithoutExtension(mapName);
        if (string.IsNullOrWhiteSpace(mapNameOnly))
        {
            mapNameOnly = "DefaultMap";
        }

        string targetDir = Path.Combine(OS.GetUserDataDir(), "saved_data", mapNameOnly);
        Directory.CreateDirectory(targetDir);

        string targetFile = Path.Combine(targetDir, fileName);

        if (File.Exists(targetFile))
        {
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

        File.WriteAllText(targetFile, content);
    }

    public string ReadSavedData(string mapName, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..") ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            GD.PrintErr($"[Sandbox block] Blocked invalid or traversal save file path: '{fileName}'");
            return string.Empty;
        }

        string mapNameOnly = Path.GetFileNameWithoutExtension(mapName);
        if (string.IsNullOrWhiteSpace(mapNameOnly))
        {
            mapNameOnly = "DefaultMap";
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
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..") ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        string mapNameOnly = Path.GetFileNameWithoutExtension(mapName);
        if (string.IsNullOrWhiteSpace(mapNameOnly))
        {
            mapNameOnly = "DefaultMap";
        }

        string targetFile = Path.Combine(OS.GetUserDataDir(), "saved_data", mapNameOnly, fileName);
        return File.Exists(targetFile);
    }
}
