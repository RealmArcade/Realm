using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public class MapManifest
{
    private Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _fileNamesSet;

    public string MapName { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public List<string> Maintainers { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Assets { get; set; }

    [JsonIgnore]
    public Dictionary<string, string> Files
    {
        get
        {
            EnsureFilesFromAssets();
            return _files;
        }
        set
        {
            _files = value ?? new(StringComparer.OrdinalIgnoreCase);
            _fileNamesSet = null;
        }
    }

    [JsonIgnore]
    public Dictionary<string, long>? FileSizes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private void EnsureFilesFromAssets()
    {
        if (Assets != null && _files.Count == 0)
        {
            FlattenAssetsInto(_files, Assets);
            _fileNamesSet = null;
        }
    }

    public bool HasFileName(string fileName)
    {
        if (_fileNamesSet == null)
        {
            EnsureFilesFromAssets();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _files.Keys)
            {
                string fn = Path.GetFileName(key);
                if (!string.IsNullOrEmpty(fn))
                {
                    set.Add(fn);
                }
            }
            _fileNamesSet = set;
        }
        return _fileNamesSet.Contains(fileName);
    }

    public static void FlattenAssetsInto(Dictionary<string, string> destinationFiles, JsonObject assets)
    {
        foreach (var categoryKeyValuePair in assets)
        {
            string category = categoryKeyValuePair.Key.ToLowerInvariant();
            if (category == "glb" && categoryKeyValuePair.Value is JsonObject glbObject)
            {
                foreach (var subCategoryKeyValuePair in glbObject)
                {
                    string subCategory = subCategoryKeyValuePair.Key.ToLowerInvariant();
                    if (subCategoryKeyValuePair.Value is JsonObject subCategoryObject)
                    {
                        foreach (var itemKeyValuePair in subCategoryObject)
                        {
                            string fileName = itemKeyValuePair.Key;
                            string extension = Path.GetExtension(fileName).ToLowerInvariant();
                            if (string.IsNullOrEmpty(extension))
                            {
                                extension = ".rmesh";
                            }
                            string hash = ExtractHashFromNode(itemKeyValuePair.Value);
                            if (!string.IsNullOrEmpty(hash))
                            {
                                string assetKey = (!string.IsNullOrEmpty(extension) && hash.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                                    ? hash
                                    : $"{hash}{extension}";
                                string relativePath = $"Assets/models/{subCategory}/{fileName}".Replace('\\', '/');
                                destinationFiles[relativePath] = assetKey;
                            }
                        }
                    }
                }
            }
            else if (categoryKeyValuePair.Value is JsonObject categoryObject)
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

                foreach (var itemKeyValuePair in categoryObject)
                {
                    string fileName = itemKeyValuePair.Key;
                    string extension = Path.GetExtension(fileName).ToLowerInvariant();
                    if (string.IsNullOrEmpty(extension))
                    {
                        extension = category switch
                        {
                            "animations" => ".ranim",
                            "sfx" or "music" => ".raud",
                            "units" or "buildings" or "props" or "items" or "attachments" or "doodads" or "projectiles" or "decorations" => ".rmesh",
                            _ => ".rtex"
                        };
                    }
                    string hash = ExtractHashFromNode(itemKeyValuePair.Value);
                    if (!string.IsNullOrEmpty(hash))
                    {
                        string assetKey = (!string.IsNullOrEmpty(extension) && hash.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                            ? hash
                            : $"{hash}{extension}";
                        string relativePath = subFolder == "other" ? fileName : $"Assets/{subFolder}/{fileName}".Replace('\\', '/');
                        destinationFiles[relativePath] = assetKey;
                    }
                }
            }
        }
    }

    private static string ExtractHashFromNode(JsonNode? node)
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

    public static Dictionary<string, string> FlattenAssetsToFiles(JsonObject? assets)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (assets != null)
        {
            FlattenAssetsInto(files, assets);
        }
        return files;
    }

    public static JsonObject UnflattenFilesToAssets(IDictionary<string, string> files)
    {
        var assets = new JsonObject();
        foreach (var keyValuePair in files)
        {
            string relativePath = keyValuePair.Key.Replace('\\', '/');
            if (relativePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath.Substring(6);
            }
            relativePath = relativePath.TrimStart('/');

            string hash = keyValuePair.Value;
            string fileName = Path.GetFileName(relativePath);
            string extension = Path.GetExtension(relativePath).ToLowerInvariant();
            if (!string.IsNullOrEmpty(extension) && hash.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                hash = hash.Substring(0, hash.Length - extension.Length);
            }

            if (relativePath.StartsWith("Assets/models/", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = relativePath.Split('/');
                string subCategory = parts.Length >= 4 ? parts[2].ToLowerInvariant() : "props";
                if (!assets.ContainsKey("glb") || assets["glb"] is not JsonObject)
                {
                    assets["glb"] = new JsonObject();
                }
                var glbObject = assets["glb"]!.AsObject();
                if (!glbObject.ContainsKey(subCategory) || glbObject[subCategory] is not JsonObject)
                {
                    glbObject[subCategory] = new JsonObject();
                }
                glbObject[subCategory]!.AsObject()[fileName] = hash;
            }
            else if (relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = relativePath.Split('/');
                string folder = parts.Length >= 3 ? parts[1].ToLowerInvariant() : "textures";
                string category = folder switch
                {
                    "vfx" => "vfx_spritesheets",
                    "animations" => "animations",
                    "decals" => "decals",
                    "icons" => "icons",
                    "ribbons" => "ribbons",
                    "noise" => "noise_textures",
                    "skyboxes" => "skyboxes",
                    "audio" when parts.Length >= 4 && parts[2].Equals("music", StringComparison.OrdinalIgnoreCase) => "music",
                    "audio" when parts.Length >= 4 && parts[2].Equals("sfx", StringComparison.OrdinalIgnoreCase) => "sfx",
                    "audio" => "sfx",
                    "textures" => "textures",
                    _ => folder
                };

                if (!assets.ContainsKey(category) || assets[category] is not JsonObject)
                {
                    assets[category] = new JsonObject();
                }
                assets[category]!.AsObject()[fileName] = hash;
            }
            else
            {
                string category = "other";
                if (!assets.ContainsKey(category) || assets[category] is not JsonObject)
                {
                    assets[category] = new JsonObject();
                }
                assets[category]!.AsObject()[relativePath] = hash;
            }
        }
        return assets;
    }

    public static void MergeCustomPropertiesIntoAssets(JsonObject targetAssets, JsonObject sourceAssets)
    {
        foreach (var categoryPair in sourceAssets)
        {
            string category = categoryPair.Key.ToLowerInvariant();
            if (category == "glb" && categoryPair.Value is JsonObject glbSource)
            {
                if (targetAssets["glb"] is JsonObject glbTarget)
                {
                    foreach (var subPair in glbSource)
                    {
                        string subCat = subPair.Key.ToLowerInvariant();
                        if (subPair.Value is JsonObject subSource && glbTarget[subCat] is JsonObject subTarget)
                        {
                            foreach (var itemPair in subSource)
                            {
                                if (itemPair.Value is JsonObject itemObj && subTarget[itemPair.Key] != null)
                                {
                                    JsonObject targetItemObj;
                                    if (subTarget[itemPair.Key] is JsonObject existingObj)
                                    {
                                        targetItemObj = existingObj;
                                    }
                                    else
                                    {
                                        string currentHash = subTarget[itemPair.Key]!.ToString();
                                        targetItemObj = new JsonObject { ["hash"] = currentHash };
                                        subTarget[itemPair.Key] = targetItemObj;
                                    }

                                    foreach (var prop in itemObj)
                                    {
                                        if (!string.Equals(prop.Key, "hash", StringComparison.OrdinalIgnoreCase))
                                        {
                                            targetItemObj[prop.Key] = prop.Value?.DeepClone();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else if (categoryPair.Value is JsonObject catSource && targetAssets[category] is JsonObject catTarget)
            {
                foreach (var itemPair in catSource)
                {
                    if (itemPair.Value is JsonObject itemObj && catTarget[itemPair.Key] != null)
                    {
                        JsonObject targetItemObj;
                        if (catTarget[itemPair.Key] is JsonObject existingObj)
                        {
                            targetItemObj = existingObj;
                        }
                        else
                        {
                            string currentHash = catTarget[itemPair.Key]!.ToString();
                            targetItemObj = new JsonObject { ["hash"] = currentHash };
                            catTarget[itemPair.Key] = targetItemObj;
                        }

                        foreach (var prop in itemObj)
                        {
                            if (!string.Equals(prop.Key, "hash", StringComparison.OrdinalIgnoreCase))
                            {
                                targetItemObj[prop.Key] = prop.Value?.DeepClone();
                            }
                        }
                    }
                }
            }
        }
    }

    public static MapManifest CreateFromDirectory(
        string directoryPath,
        string mapName,
        string author,
        string version = "1.0.0",
        string description = "",
        List<string>? tags = null)
    {
        var manifest = new MapManifest
        {
            MapName = mapName,
            Author = author,
            Version = version,
            Description = description,
            Tags = tags ?? new List<string>()
        };

        if (!Directory.Exists(directoryPath))
        {
            return manifest;
        }

        string fullDirectoryPath = Path.GetFullPath(directoryPath);
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
                relativePath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
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
            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                continue;
            }

            byte[] fileBytes = File.ReadAllBytes(filePath);
            if (fileBytes.Length == 0)
            {
                continue;
            }

            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(fileBytes, extension);
            string assetKey = string.IsNullOrEmpty(extension) ? canonicalBlake3 : $"{canonicalBlake3}{extension}";

            manifest.Files[relativePath] = assetKey;
            manifest.FileSizes![relativePath] = fileBytes.Length;
        }

        string manifestJsonPath = Path.Combine(fullDirectoryPath, "manifest.json");
        JsonObject? existingAssets = null;
        if (File.Exists(manifestJsonPath))
        {
            try
            {
                var existing = LoadFromFile(manifestJsonPath);
                if (existing != null)
                {
                    if (string.IsNullOrEmpty(manifest.MapName) && !string.IsNullOrEmpty(existing.MapName))
                    {
                        manifest.MapName = existing.MapName;
                    }
                    if (string.IsNullOrEmpty(manifest.Author) && !string.IsNullOrEmpty(existing.Author))
                    {
                        manifest.Author = existing.Author;
                    }
                    if ((string.IsNullOrEmpty(manifest.Version) || manifest.Version == "1.0.0") && !string.IsNullOrEmpty(existing.Version))
                    {
                        manifest.Version = existing.Version;
                    }
                    if (string.IsNullOrEmpty(manifest.Description) && !string.IsNullOrEmpty(existing.Description))
                    {
                        manifest.Description = existing.Description;
                    }
                    if ((manifest.Tags == null || manifest.Tags.Count == 0) && existing.Tags != null && existing.Tags.Count > 0)
                    {
                        manifest.Tags = new List<string>(existing.Tags);
                    }
                    if ((manifest.Maintainers == null || manifest.Maintainers.Count == 0) && existing.Maintainers != null && existing.Maintainers.Count > 0)
                    {
                        manifest.Maintainers = new List<string>(existing.Maintainers);
                    }
                    if (existing.Assets != null)
                    {
                        existingAssets = existing.Assets.DeepClone() as JsonObject;
                    }
                }
            }
            catch
            {
            }
        }

        if (string.IsNullOrEmpty(manifest.MapName))
        {
            manifest.MapName = Path.GetFileName(fullDirectoryPath);
        }
        if (string.IsNullOrEmpty(manifest.Version))
        {
            manifest.Version = "1.0.0";
        }
        if (manifest.Tags == null)
        {
            manifest.Tags = new List<string>();
        }

        manifest.Assets = UnflattenFilesToAssets(manifest.Files);
        if (existingAssets != null)
        {
            MergeCustomPropertiesIntoAssets(manifest.Assets, existingAssets);
        }

        return manifest;
    }

    public string ToJson(bool writeIndented = true)
    {
        if (_files.Count > 0)
        {
            var unflattened = UnflattenFilesToAssets(_files);
            if (Assets != null)
            {
                MergeCustomPropertiesIntoAssets(unflattened, Assets);
            }
            Assets = unflattened;
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented
        };
        return JsonSerializer.Serialize(this, options);
    }

    public void SaveToFile(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, ToJson());
    }

    public static MapManifest? LoadFromJson(string json)
    {
        var manifest = JsonSerializer.Deserialize<MapManifest>(json);
        if (manifest != null)
        {
            if (manifest.Assets != null && manifest._files.Count == 0)
            {
                manifest.EnsureFilesFromAssets();
            }
            else if (manifest._files.Count == 0)
            {
                try
                {
                    var node = JsonNode.Parse(json);
                    if (node is JsonObject rootObj)
                    {
                        var assetsObj = new JsonObject();
                        foreach (var kvp in rootObj)
                        {
                            if (kvp.Value is JsonObject catObj &&
                                !string.Equals(kvp.Key, "MapName", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(kvp.Key, "Author", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(kvp.Key, "Version", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(kvp.Key, "Description", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(kvp.Key, "Tags", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(kvp.Key, "GameBuildNumber", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(kvp.Key, "Assets", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(kvp.Key, "Files", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(kvp.Key, "FileSizes", StringComparison.OrdinalIgnoreCase))
                            {
                                assetsObj[kvp.Key] = catObj.DeepClone();
                            }
                        }
                        if (assetsObj.Count > 0)
                        {
                            manifest.Assets = assetsObj;
                            manifest.EnsureFilesFromAssets();
                        }
                    }
                }
                catch { }
            }
        }
        return manifest;
    }

    public string ComputeManifestBlake3()
    {
        string json = ToJson();
        return RealmMetadataHelper.ComputeBlake3(System.Text.Encoding.UTF8.GetBytes(json), ".json");
    }

    public static MapManifest? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        string json = File.ReadAllText(filePath);
        return LoadFromJson(json);
    }

    public bool IsCandidateFile(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        string norm = relativePath.Replace('\\', '/').TrimStart('/');

        if (Files.ContainsKey(norm))
        {
            return true;
        }

        string fileName = Path.GetFileName(norm);
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        if (HasFileName(fileName))
        {
            return true;
        }

        if (string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "metadata.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "terrain.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "Coordinates.cs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "MapScript.cs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "WasmEntryPoint.cs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "Directory.Build.targets", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "global.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "NuGet.config", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "AGENTS.md", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "LICENSE.md", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "LICENSE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, ".gitignore", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".exr", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".ranim", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("terrain_", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("thumbnail", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("preview", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("icon", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (norm.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("wit/", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("locale/", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith(".vscode/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
