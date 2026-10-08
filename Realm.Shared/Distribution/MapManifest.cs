using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using Realm.Shared.Metadata;
using Realm.Shared.Serialization;
using Realm.Shared.Services;

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
    public List<string> GreenlitReferences { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MapManifestAssets? Assets { get; set; }

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

    public static void FlattenAssetsInto(Dictionary<string, string> destinationFiles, MapManifestAssets assets)
    {
        foreach (var categoryKeyValuePair in assets.GetAllCategories())
        {
            ProcessFlattenCategory(destinationFiles, categoryKeyValuePair.Key, categoryKeyValuePair.Value);
        }
    }

    private static void ProcessFlattenCategory(Dictionary<string, string> destinationFiles, string category, Dictionary<string, string> categoryObject)
    {
        if (string.Equals(category, "other", StringComparison.OrdinalIgnoreCase))
        {
            ProcessOtherFlattenCategory(destinationFiles, categoryObject);
            return;
        }

        string subFolder = GetSubFolderForCategory(category);
        foreach (var itemKeyValuePair in categoryObject)
        {
            ProcessStandardFlattenItem(destinationFiles, category, subFolder, itemKeyValuePair.Key, itemKeyValuePair.Value);
        }
    }

    private static void ProcessOtherFlattenCategory(Dictionary<string, string> destinationFiles, Dictionary<string, string> categoryObject)
    {
        foreach (var itemKeyValuePair in categoryObject)
        {
            string rawKey = itemKeyValuePair.Key.Replace('\\', '/').TrimStart('/');
            string hash = itemKeyValuePair.Value;
            if (!string.IsNullOrEmpty(hash))
            {
                destinationFiles[rawKey] = hash;
            }
        }
    }

    private static string GetSubFolderForCategory(string category)
    {
        string? modelOrVfx = GetModelOrVfxSubFolder(category);
        if (modelOrVfx != null)
            return modelOrVfx;

        string? audioOrVisual = GetAudioOrVisualSubFolder(category);
        if (audioOrVisual != null)
            return audioOrVisual;

        return category.ToLowerInvariant();
    }

    private static string? GetModelOrVfxSubFolder(string category)
    {
        return category switch
        {
            "Character" => "models/units",
            "Building" => "models/buildings",
            "Prop" => "models/props",
            "Item" => "models/items",
            "Spritesheet" or "vfx_spritesheets" or "vfxspritesheets" or "vfx" => "vfx_spritesheets",
            "vfx_radial" => "vfx_radial",
            "vfx_vertical" => "vfx_vertical",
            _ => null
        };
    }

    private static string? GetAudioOrVisualSubFolder(string category)
    {
        return category switch
        {
            "Animation" => "animations",
            "SoundEffect" => "audio/sfx",
            "Music" => "audio/music",
            "Icon" => "icons",
            "Decal" => "decals",
            "Ribbon" => "ribbons",
            "Noise" => "noise",
            "Skybox" => "skyboxes",
            "Terrain" => "textures",
            "Shader" => "shaders",
            _ => null
        };
    }

    private static string GetExtensionForCategory(string category)
    {
        return category switch
        {
            "Animation" => ".ranim",
            "SoundEffect" or "Music" => ".raud",
            "Character" or "Building" or "Prop" or "Item" => ".rmesh",
            _ => ".rtex"
        };
    }

    private static void ProcessStandardFlattenItem(Dictionary<string, string> destinationFiles, string category, string subFolder, string key, string hash)
    {
        string rawKey = key.Replace('\\', '/').TrimStart('/');
        if (rawKey.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            rawKey = rawKey.Substring(6).TrimStart('/');
        
        string prefix = $"Assets/{subFolder}/";
        if (rawKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            rawKey = rawKey.Substring(prefix.Length);

        int slashIdx = rawKey.IndexOf('/');
        if (slashIdx >= 0)
            rawKey = rawKey.Substring(slashIdx + 1);
        rawKey = Path.GetFileName(rawKey);

        string extension = Path.GetExtension(rawKey).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
        {
            extension = GetExtensionForCategory(category);
            rawKey = $"{rawKey}{extension}";
        }
        
        if (!string.IsNullOrEmpty(hash))
        {
            string assetKey = (!string.IsNullOrEmpty(extension) && hash.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                ? hash
                : $"{hash}{extension}";
            string relativePath = $"{prefix}{rawKey}";
            destinationFiles[relativePath] = assetKey;
        }
    }

    public static Dictionary<string, string> FlattenAssetsToFiles(MapManifestAssets? assets)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (assets != null)
        {
            FlattenAssetsInto(files, assets);
        }
        return files;
    }

    public static MapManifestAssets UnflattenFilesToAssets(IDictionary<string, string> files)
    {
        var assets = new MapManifestAssets();
        foreach (var keyValuePair in files)
        {
            ProcessUnflattenFile(assets, keyValuePair.Key, keyValuePair.Value);
        }
        return assets;
    }

    private static void ProcessUnflattenFile(MapManifestAssets assets, string rawPath, string rawHash)
    {
        string relativePath = CleanRelativePath(rawPath);
        string extension = Path.GetExtension(relativePath).ToLowerInvariant();
        string hash = CleanHash(rawHash, extension);
        string fileName = Path.GetFileName(relativePath);

        if (relativePath.StartsWith("Assets/models/", StringComparison.OrdinalIgnoreCase))
        {
            ProcessUnflattenModel(assets, relativePath, fileName, hash);
        }
        else if (relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            ProcessUnflattenStandard(assets, relativePath, fileName, hash);
        }
        else
        {
            ProcessUnflattenOther(assets, relativePath, hash);
        }
    }

    private static string CleanRelativePath(string rawPath)
    {
        string relativePath = rawPath.Replace('\\', '/');
        if (relativePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = relativePath.Substring(6);
        }
        return relativePath.TrimStart('/');
    }

    private static string CleanHash(string rawHash, string extension)
    {
        if (!string.IsNullOrEmpty(extension) && rawHash.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return rawHash.Substring(0, rawHash.Length - extension.Length);
        }
        return rawHash;
    }

    private static void ProcessUnflattenModel(MapManifestAssets assets, string relativePath, string fileName, string hash)
    {
        string[] parts = relativePath.Split('/');
        string subCategory = parts.Length >= 4 ? parts[2].ToLowerInvariant() : "props";
        string category = GetCategoryFromModelSubCategory(subCategory);

        var dict = GetOrCreateCategoryDict(assets, category);
        dict[fileName] = hash;
    }

    private static string GetCategoryFromModelSubCategory(string subCategory)
    {
        return subCategory switch
        {
            "units" or "characters" => "Character",
            "buildings" => "Building",
            "props" or "resources" => "Prop",
            "items" or "projectiles" or "attachments" or "weapons" => "Item",
            _ => "Prop"
        };
    }

    private static void ProcessUnflattenStandard(MapManifestAssets assets, string relativePath, string fileName, string hash)
    {
        string[] parts = relativePath.Split('/');
        string folder = parts.Length >= 3 ? parts[1].ToLowerInvariant() : "textures";
        string category = GetCategoryFromStandardFolder(folder, parts);

        var dict = GetOrCreateCategoryDict(assets, category);

        string key = (category is "Music" or "SoundEffect")
            ? (parts.Length > 3 ? string.Join("/", parts.Skip(3)) : fileName)
            : (parts.Length > 2 ? string.Join("/", parts.Skip(2)) : fileName);

        dict[key] = hash;
    }

    private static string GetCategoryFromStandardFolder(string folder, string[] parts)
    {
        string? vfxCategory = GetVfxCategory(folder);
        if (vfxCategory != null)
            return vfxCategory;

        string? audioCategory = GetAudioCategory(folder, parts);
        if (audioCategory != null)
            return audioCategory;

        string? visualCategory = GetVisualCategory(folder);
        if (visualCategory != null)
            return visualCategory;

        return folder;
    }

    private static string? GetVfxCategory(string folder)
    {
        return folder switch
        {
            "vfx_spritesheets" or "vfx" => "Spritesheet",
            "vfx_radial" => "vfx_radial",
            "vfx_vertical" => "vfx_vertical",
            _ => null
        };
    }

    private static string? GetAudioCategory(string folder, string[] parts)
    {
        if (folder != "audio")
            return null;

        if (parts.Length >= 4 && parts[2].Equals("music", StringComparison.OrdinalIgnoreCase))
            return "Music";

        if (parts.Length >= 4 && parts[2].Equals("sfx", StringComparison.OrdinalIgnoreCase))
            return "SoundEffect";

        return "SoundEffect";
    }

    private static string? GetVisualCategory(string folder)
    {
        return folder switch
        {
            "animations" => "Animation",
            "decals" => "Decal",
            "icons" => "Icon",
            "ribbons" => "Ribbon",
            "noise" => "Noise",
            "skyboxes" => "Skybox",
            "textures" => "Terrain",
            "shaders" => "Shader",
            _ => null
        };
    }

    private static void ProcessUnflattenOther(MapManifestAssets assets, string relativePath, string hash)
    {
        var dict = GetOrCreateCategoryDict(assets, "other");
        dict[relativePath] = hash;
    }

    private static Dictionary<string, string> GetOrCreateCategoryDict(MapManifestAssets assets, string category)
    {
        var dict = assets.GetCategory(category);
        if (dict == null)
        {
            dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            assets.SetCategory(category, dict);
        }
        return dict;
    }

    public static void MergeCustomPropertiesIntoAssets(MapManifestAssets targetAssets, MapManifestAssets sourceAssets)
    {
        foreach (var categoryPair in sourceAssets.GetAllCategories())
        {
            string category = categoryPair.Key;
            var catSource = categoryPair.Value;
            var catTarget = targetAssets.GetCategory(category);
            if (catTarget == null)
            {
                catTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                targetAssets.SetCategory(category, catTarget);
            }
            foreach (var itemPair in catSource)
            {
                string sourceKey = itemPair.Key;
                string? targetMatchKey = null;

                if (catTarget.ContainsKey(sourceKey))
                {
                    targetMatchKey = sourceKey;
                }
                else
                {
                    string sourceBaseName = Path.GetFileName(sourceKey);
                    foreach (var targetKvp in catTarget)
                    {
                        if (string.Equals(Path.GetFileName(targetKvp.Key), sourceBaseName, StringComparison.OrdinalIgnoreCase) ||
                            targetKvp.Key.EndsWith("/" + sourceKey, StringComparison.OrdinalIgnoreCase))
                        {
                            targetMatchKey = targetKvp.Key;
                            break;
                        }
                    }
                }

                if (targetMatchKey != null)
                {
                    catTarget[targetMatchKey] = itemPair.Value;
                }
                else
                {
                    catTarget[sourceKey] = itemPair.Value;
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

        if (!Directory.Exists(directoryPath)) return manifest;

        string fullDirectoryPath = Path.GetFullPath(directoryPath);
        ScanDirectoryForManifestFiles(fullDirectoryPath, manifest);

        string manifestJsonPath = Path.Combine(fullDirectoryPath, "manifest.json");
        MapManifestAssets? existingAssets = null;
        if (File.Exists(manifestJsonPath))
        {
            existingAssets = LoadAndMergeExistingManifest(manifestJsonPath, manifest);
        }

        if (string.IsNullOrEmpty(manifest.MapName)) manifest.MapName = Path.GetFileName(fullDirectoryPath);
        if (string.IsNullOrEmpty(manifest.Version)) manifest.Version = "1.0.0";
        if (manifest.Tags == null) manifest.Tags = new List<string>();

        manifest.Assets = UnflattenFilesToAssets(manifest.Files);
        if (existingAssets != null)
        {
            MergeCustomPropertiesIntoAssets(manifest.Assets, existingAssets);
        }

        return manifest;
    }

    private static void ScanDirectoryForManifestFiles(string fullDirectoryPath, MapManifest manifest)
    {
        string[] allFiles = Directory.GetFiles(fullDirectoryPath, "*.*", SearchOption.AllDirectories);

        foreach (string filePath in allFiles)
        {
            string relativePath = Path.GetRelativePath(fullDirectoryPath, filePath).Replace('\\', '/');

            if (IsIgnoredPath(relativePath)) continue;

            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length == 0) continue;

            byte[] fileBytes = File.ReadAllBytes(filePath);
            if (fileBytes.Length == 0) continue;

            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(fileBytes, extension);
            string assetKey = string.IsNullOrEmpty(extension) ? canonicalBlake3 : $"{canonicalBlake3}{extension}";

            manifest.Files[relativePath] = assetKey;
            manifest.FileSizes![relativePath] = fileBytes.Length;
        }
    }

    private static bool IsIgnoredPath(string relativePath)
    {
        if (IsIgnoredBuildDirectory(relativePath))
            return true;

        if (IsIgnoredHiddenDirectory(relativePath))
            return true;

        if (IsIgnoredExactFile(relativePath))
            return true;

        if (IsIgnoredArchiveExtension(relativePath))
            return true;

        if (IsIgnoredTemporaryExtension(relativePath))
            return true;

        return false;
    }

    private static bool IsIgnoredBuildDirectory(string relativePath)
    {
        if (relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) && !relativePath.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase))
            return true;

        return relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredHiddenDirectory(string relativePath)
    {
        return relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith(".vscode/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith(".vs/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith(".sidecarcache/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith(".backups/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredExactFile(string relativePath)
    {
        if (string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(Path.GetFileName(relativePath), "authorship_key.pem", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredArchiveExtension(string relativePath)
    {
        return relativePath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredTemporaryExtension(string relativePath)
    {
        return relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(".rkey", StringComparison.OrdinalIgnoreCase);
    }

    private static MapManifestAssets? LoadAndMergeExistingManifest(string manifestJsonPath, MapManifest manifest)
    {
        try
        {
            var existing = MapFileService.LoadManifest(manifestJsonPath);
            if (existing == null) return null;

            MergeBasicProperties(manifest, existing);
            MergeCollectionProperties(manifest, existing);

            return existing.Assets;
        }
        catch
        {
            return null;
        }
    }

    private static void MergeBasicProperties(MapManifest manifest, MapManifest existing)
    {
        if (string.IsNullOrEmpty(manifest.MapName) && !string.IsNullOrEmpty(existing.MapName))
            manifest.MapName = existing.MapName;

        if (string.IsNullOrEmpty(manifest.Author) && !string.IsNullOrEmpty(existing.Author))
            manifest.Author = existing.Author;

        if (ShouldMergeVersion(manifest, existing))
            manifest.Version = existing.Version;

        if (string.IsNullOrEmpty(manifest.Description) && !string.IsNullOrEmpty(existing.Description))
            manifest.Description = existing.Description;
    }

    private static void MergeCollectionProperties(MapManifest manifest, MapManifest existing)
    {
        if (ShouldMergeList(manifest.Tags, existing.Tags))
            manifest.Tags = new List<string>(existing.Tags);

        if (ShouldMergeList(manifest.Maintainers, existing.Maintainers))
            manifest.Maintainers = new List<string>(existing.Maintainers);

        if (ShouldMergeList(manifest.GreenlitReferences, existing.GreenlitReferences))
            manifest.GreenlitReferences = new List<string>(existing.GreenlitReferences);
    }

    private static bool ShouldMergeVersion(MapManifest manifest, MapManifest existing)
    {
        return (string.IsNullOrEmpty(manifest.Version) || manifest.Version == "1.0.0") && !string.IsNullOrEmpty(existing.Version);
    }

    private static bool ShouldMergeList(List<string>? manifestList, List<string>? existingList)
    {
        return (manifestList == null || manifestList.Count == 0) && existingList != null && existingList.Count > 0;
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

        return MapFileService.SaveManifestToJson(this);
    }

    public void SaveToFile(string filePath)
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

        MapFileService.SaveManifest(filePath, this);
    }

    public static MapManifest LoadFromJson(string json)
    {
        return MapFileService.LoadManifestFromJson(json);
    }

    public static string GenerateJsonSchema()
    {
        return RealmJsonSchemaExporter.GenerateJsonSchema(typeof(MapManifest));
    }

    public string ComputeManifestBlake3()
    {
        string json = ToJson();
        return RealmMetadataHelper.ComputeBlake3(System.Text.Encoding.UTF8.GetBytes(json), ".json");
    }

    public static MapManifest LoadFromFile(string filePath)
    {
        return MapFileService.LoadManifest(filePath);
    }

    private static readonly HashSet<string> ValidCandidateFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "manifest.json", "metadata.json", "terrain.json", "Coordinates.cs", "MapScript.cs",
        "WasmEntryPoint.cs", "Directory.Build.targets", "Directory.Build.props", "global.json",
        "NuGet.config", "AGENTS.md", "LICENSE.md", "LICENSE", ".gitignore"
    };

    private static readonly HashSet<string> ValidCandidateExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".slnx", ".sln", ".wasm", ".exr", ".png", ".jpg", ".jpeg",
        ".ranim", ".rtex", ".rmesh", ".ogg", ".wav", ".mp3"
    };

    private static readonly string[] ValidCandidatePrefixes = { "terrain_", "thumbnail", "preview", "icon" };
    private static readonly string[] ValidCandidateDirPrefixes = { "lib/", "wit/", "locale/", "bin/", "Assets/", ".vscode/" };

    public bool IsCandidateFile(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        string norm = relativePath.Replace('\\', '/').TrimStart('/');

        if (Files.ContainsKey(norm)) return true;

        string fileName = Path.GetFileName(norm);
        if (string.IsNullOrEmpty(fileName)) return false;

        if (HasFileName(fileName)) return true;

        if (IsCandidateFileName(fileName)) return true;
        if (IsCandidateNormPath(norm)) return true;

        return false;
    }

    private static bool IsCandidateFileName(string fileName)
    {
        if (ValidCandidateFileNames.Contains(fileName)) return true;

        foreach (var ext in ValidCandidateExtensions)
        {
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
        }

        foreach (var prefix in ValidCandidatePrefixes)
        {
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool IsCandidateNormPath(string norm)
    {
        foreach (var dir in ValidCandidateDirPrefixes)
        {
            if (norm.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

public class MapManifestAssets
{
    [JsonPropertyName("Animation")]
    public Dictionary<string, string>? Animation { get; set; }

    [JsonPropertyName("Building")]
    public Dictionary<string, string>? Building { get; set; }

    [JsonPropertyName("Character")]
    public Dictionary<string, string>? Character { get; set; }

    [JsonPropertyName("Decal")]
    public Dictionary<string, string>? Decal { get; set; }

    [JsonPropertyName("Icon")]
    public Dictionary<string, string>? Icon { get; set; }

    [JsonPropertyName("Item")]
    public Dictionary<string, string>? Item { get; set; }

    [JsonPropertyName("Music")]
    public Dictionary<string, string>? Music { get; set; }

    [JsonPropertyName("Noise")]
    public Dictionary<string, string>? Noise { get; set; }

    [JsonPropertyName("Prop")]
    public Dictionary<string, string>? Prop { get; set; }

    [JsonPropertyName("Ribbon")]
    public Dictionary<string, string>? Ribbon { get; set; }

    [JsonPropertyName("Shader")]
    public Dictionary<string, string>? Shader { get; set; }

    [JsonPropertyName("Skybox")]
    public Dictionary<string, string>? Skybox { get; set; }

    [JsonPropertyName("SoundEffect")]
    public Dictionary<string, string>? SoundEffect { get; set; }

    [JsonPropertyName("Spritesheet")]
    public Dictionary<string, string>? Spritesheet { get; set; }

    [JsonPropertyName("Terrain")]
    public Dictionary<string, string>? Terrain { get; set; }

    [JsonPropertyName("vfx_radial")]
    public Dictionary<string, string>? VfxRadial { get; set; }

    [JsonPropertyName("vfx_vertical")]
    public Dictionary<string, string>? VfxVertical { get; set; }

    [JsonPropertyName("Other")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Other { get; set; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalProperties { get; set; }

    [System.Runtime.CompilerServices.IndexerName("CategoryItems")]
    public Dictionary<string, string>? this[string category]
    {
        get => GetCategory(category);
        set
        {
            if (value != null) SetCategory(category, value);
        }
    }

    public bool ContainsCategory(string category)
    {
        return GetCategory(category) != null;
    }

    private static readonly Dictionary<string, Func<MapManifestAssets, Dictionary<string, string>?>> CategoryGetters = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Animation", a => a.Animation }, { "animations", a => a.Animation },
        { "Building", a => a.Building }, { "buildings", a => a.Building },
        { "Character", a => a.Character }, { "characters", a => a.Character }, { "units", a => a.Character },
        { "Decal", a => a.Decal }, { "decals", a => a.Decal },
        { "Icon", a => a.Icon }, { "icons", a => a.Icon },
        { "Item", a => a.Item }, { "items", a => a.Item }, { "projectiles", a => a.Item }, { "attachments", a => a.Item }, { "weapons", a => a.Item },
        { "Music", a => a.Music },
        { "Noise", a => a.Noise }, { "noise_textures", a => a.Noise },
        { "Other", a => a.Other },
        { "Prop", a => a.Prop }, { "props", a => a.Prop }, { "resources", a => a.Prop },
        { "Ribbon", a => a.Ribbon }, { "ribbons", a => a.Ribbon }, { "ribbon_textures", a => a.Ribbon },
        { "Shader", a => a.Shader }, { "shaders", a => a.Shader },
        { "Skybox", a => a.Skybox }, { "skyboxes", a => a.Skybox },
        { "SoundEffect", a => a.SoundEffect }, { "sfx", a => a.SoundEffect }, { "audio", a => a.SoundEffect }, { "sounds", a => a.SoundEffect },
        { "Spritesheet", a => a.Spritesheet }, { "spritesheets", a => a.Spritesheet }, { "vfx", a => a.Spritesheet }, { "vfx_spritesheets", a => a.Spritesheet },
        { "Terrain", a => a.Terrain }, { "textures", a => a.Terrain },
        { "vfx_radial", a => a.VfxRadial },
        { "vfx_vertical", a => a.VfxVertical }
    };

    private static readonly Dictionary<string, Action<MapManifestAssets, Dictionary<string, string>>> CategorySetters = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Animation", (a, d) => a.Animation = d },
        { "Building", (a, d) => a.Building = d },
        { "Character", (a, d) => a.Character = d },
        { "Decal", (a, d) => a.Decal = d },
        { "Icon", (a, d) => a.Icon = d },
        { "Item", (a, d) => a.Item = d },
        { "Music", (a, d) => a.Music = d },
        { "Noise", (a, d) => a.Noise = d },
        { "Other", (a, d) => a.Other = d },
        { "Prop", (a, d) => a.Prop = d },
        { "Ribbon", (a, d) => a.Ribbon = d },
        { "Shader", (a, d) => a.Shader = d },
        { "Skybox", (a, d) => a.Skybox = d },
        { "SoundEffect", (a, d) => a.SoundEffect = d },
        { "Spritesheet", (a, d) => a.Spritesheet = d },
        { "Terrain", (a, d) => a.Terrain = d },
        { "vfx_radial", (a, d) => a.VfxRadial = d },
        { "vfx_vertical", (a, d) => a.VfxVertical = d }
    };

    private static readonly string[] CategoryProperties = {
        "Animation", "Building", "Character", "Decal", "Icon", "Item", "Music", "Noise", "other",
        "Prop", "Ribbon", "Shader", "Skybox", "SoundEffect", "Spritesheet", "Terrain", "vfx_radial", "vfx_vertical"
    };

    public Dictionary<string, string>? GetCategory(string category)
    {
        if (CategoryGetters.TryGetValue(category, out var getter))
        {
            return getter(this);
        }
        return GetFromAdditionalProperties(category);
    }

    public void SetCategory(string category, Dictionary<string, string> dictionary)
    {
        if (CategorySetters.TryGetValue(category, out var setter))
        {
            setter(this, dictionary);
            return;
        }
        
        AdditionalProperties ??= new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.OrdinalIgnoreCase);
        AdditionalProperties[category] = System.Text.Json.JsonSerializer.SerializeToElement(dictionary);
    }

    public IEnumerable<KeyValuePair<string, Dictionary<string, string>>> GetAllCategories()
    {
        foreach (var prop in CategoryProperties)
        {
            var dict = CategoryGetters[prop](this);
            if (dict != null) yield return new(prop, dict);
        }

        if (AdditionalProperties != null)
        {
            foreach (var kvp in AdditionalProperties)
            {
                var dict = GetFromAdditionalProperties(kvp.Key);
                if (dict != null) yield return new(kvp.Key, dict);
            }
        }
    }

    private Dictionary<string, string>? GetFromAdditionalProperties(string category)
    {
        if (AdditionalProperties != null && AdditionalProperties.TryGetValue(category, out var element) && element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in element.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String ? prop.Value.GetString() ?? string.Empty : prop.Value.ToString();
            }
            return dict;
        }
        return null;
    }
}
