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
            string category = categoryKeyValuePair.Key;
            var categoryObject = categoryKeyValuePair.Value;

            if (string.Equals(category, "other", StringComparison.OrdinalIgnoreCase))
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
                continue;
            }

            string subFolder = category switch
            {
                "Character" => "models/units",
                "Building" => "models/buildings",
                "Prop" => "models/props",
                "Item" => "models/items",
                "Spritesheet" => "vfx",
                "vfx_radial" => "vfx_radial",
                "vfx_vertical" => "vfx_vertical",
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
                _ => category.ToLowerInvariant()
            };

            foreach (var itemKeyValuePair in categoryObject)
            {
                string rawKey = itemKeyValuePair.Key.Replace('\\', '/').TrimStart('/');
                if (rawKey.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
                {
                    rawKey = rawKey.Substring(6).TrimStart('/');
                }
                if (rawKey.StartsWith($"Assets/{subFolder}/", StringComparison.OrdinalIgnoreCase))
                {
                    rawKey = rawKey.Substring($"Assets/{subFolder}/".Length);
                }

                string extension = Path.GetExtension(rawKey).ToLowerInvariant();
                if (string.IsNullOrEmpty(extension))
                {
                    extension = category switch
                    {
                        "Animation" => ".ranim",
                        "SoundEffect" or "Music" => ".raud",
                        "Character" or "Building" or "Prop" or "Item" => ".rmesh",
                        _ => ".rtex"
                    };
                }
                string hash = itemKeyValuePair.Value;
                if (!string.IsNullOrEmpty(hash))
                {
                    string assetKey = (!string.IsNullOrEmpty(extension) && hash.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                        ? hash
                        : $"{hash}{extension}";
                    string relativePath = $"Assets/{subFolder}/{rawKey}".Replace('\\', '/');
                    destinationFiles[relativePath] = assetKey;
                }
            }
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
                string category = subCategory switch
                {
                    "units" or "characters" => "Character",
                    "buildings" => "Building",
                    "props" or "resources" => "Prop",
                    "items" or "projectiles" or "attachments" or "weapons" => "Item",
                    _ => "Prop"
                };

                var dict = assets.GetCategory(category);
                if (dict == null)
                {
                    dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    assets.SetCategory(category, dict);
                }
                dict[fileName] = hash;
            }
            else if (relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = relativePath.Split('/');
                string folder = parts.Length >= 3 ? parts[1].ToLowerInvariant() : "textures";
                string category = folder switch
                {
                    "vfx" => "Spritesheet",
                    "vfx_radial" => "vfx_radial",
                    "vfx_vertical" => "vfx_vertical",
                    "animations" => "Animation",
                    "decals" => "Decal",
                    "icons" => "Icon",
                    "ribbons" => "Ribbon",
                    "noise" => "Noise",
                    "skyboxes" => "Skybox",
                    "audio" when parts.Length >= 4 && parts[2].Equals("music", StringComparison.OrdinalIgnoreCase) => "Music",
                    "audio" when parts.Length >= 4 && parts[2].Equals("sfx", StringComparison.OrdinalIgnoreCase) => "SoundEffect",
                    "audio" => "SoundEffect",
                    "textures" => "Terrain",
                    "shaders" => "Shader",
                    _ => folder
                };

                var dict = assets.GetCategory(category);
                if (dict == null)
                {
                    dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    assets.SetCategory(category, dict);
                }

                string key = (category is "Music" or "SoundEffect")
                    ? (parts.Length > 3 ? string.Join("/", parts.Skip(3)) : fileName)
                    : (parts.Length > 2 ? string.Join("/", parts.Skip(2)) : fileName);

                dict[key] = hash;
            }
            else
            {
                string category = "other";
                var dict = assets.GetCategory(category);
                if (dict == null)
                {
                    dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    assets.SetCategory(category, dict);
                }
                dict[relativePath] = hash;
            }
        }
        return assets;
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
                relativePath.StartsWith(".vs/", StringComparison.OrdinalIgnoreCase) ||
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
        MapManifestAssets? existingAssets = null;
        if (File.Exists(manifestJsonPath))
        {
            try
            {
                var existing = MapFileService.LoadManifest(manifestJsonPath);
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
                    if ((manifest.GreenlitReferences == null || manifest.GreenlitReferences.Count == 0) && existing.GreenlitReferences != null && existing.GreenlitReferences.Count > 0)
                    {
                        manifest.GreenlitReferences = new List<string>(existing.GreenlitReferences);
                    }
                    if (existing.Assets != null)
                    {
                        existingAssets = existing.Assets;
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

    public Dictionary<string, string>? GetCategory(string category)
    {
        return category switch
        {
            "Animation" or "animations" => Animation,
            "Building" or "buildings" => Building,
            "Character" or "characters" or "units" => Character,
            "Decal" or "decals" => Decal,
            "Icon" or "icons" => Icon,
            "Item" or "items" or "projectiles" or "attachments" or "weapons" => Item,
            "Music" or "music" => Music,
            "Noise" or "noise" or "noise_textures" => Noise,
            "Other" or "other" => Other,
            "Prop" or "props" or "resources" => Prop,
            "Ribbon" or "ribbons" or "ribbon_textures" => Ribbon,
            "Shader" or "shaders" => Shader,
            "Skybox" or "skyboxes" => Skybox,
            "SoundEffect" or "sfx" or "audio" or "sounds" => SoundEffect,
            "Spritesheet" or "spritesheets" or "vfx" or "vfx_spritesheets" => Spritesheet,
            "Terrain" or "textures" => Terrain,
            "vfx_radial" => VfxRadial,
            "vfx_vertical" => VfxVertical,
            _ => GetFromAdditionalProperties(category)
        };
    }

    public void SetCategory(string category, Dictionary<string, string> dictionary)
    {
        switch (category)
        {
            case "Animation": Animation = dictionary; break;
            case "Building": Building = dictionary; break;
            case "Character": Character = dictionary; break;
            case "Decal": Decal = dictionary; break;
            case "Icon": Icon = dictionary; break;
            case "Item": Item = dictionary; break;
            case "Music": Music = dictionary; break;
            case "Noise": Noise = dictionary; break;
            case "Other" or "other": Other = dictionary; break;
            case "Prop": Prop = dictionary; break;
            case "Ribbon": Ribbon = dictionary; break;
            case "Shader": Shader = dictionary; break;
            case "Skybox": Skybox = dictionary; break;
            case "SoundEffect": SoundEffect = dictionary; break;
            case "Spritesheet": Spritesheet = dictionary; break;
            case "Terrain": Terrain = dictionary; break;
            case "vfx_radial": VfxRadial = dictionary; break;
            case "vfx_vertical": VfxVertical = dictionary; break;
            default:
                AdditionalProperties ??= new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.OrdinalIgnoreCase);
                AdditionalProperties[category] = System.Text.Json.JsonSerializer.SerializeToElement(dictionary);
                break;
        }
    }

    public IEnumerable<KeyValuePair<string, Dictionary<string, string>>> GetAllCategories()
    {
        if (Animation != null) yield return new("Animation", Animation);
        if (Building != null) yield return new("Building", Building);
        if (Character != null) yield return new("Character", Character);
        if (Decal != null) yield return new("Decal", Decal);
        if (Icon != null) yield return new("Icon", Icon);
        if (Item != null) yield return new("Item", Item);
        if (Music != null) yield return new("Music", Music);
        if (Noise != null) yield return new("Noise", Noise);
        if (Other != null) yield return new("other", Other);
        if (Prop != null) yield return new("Prop", Prop);
        if (Ribbon != null) yield return new("Ribbon", Ribbon);
        if (Shader != null) yield return new("Shader", Shader);
        if (Skybox != null) yield return new("Skybox", Skybox);
        if (SoundEffect != null) yield return new("SoundEffect", SoundEffect);
        if (Spritesheet != null) yield return new("Spritesheet", Spritesheet);
        if (Terrain != null) yield return new("Terrain", Terrain);
        if (VfxRadial != null) yield return new("vfx_radial", VfxRadial);
        if (VfxVertical != null) yield return new("vfx_vertical", VfxVertical);
        if (AdditionalProperties != null)
        {
            foreach (var kvp in AdditionalProperties)
            {
                var dict = GetFromAdditionalProperties(kvp.Key);
                if (dict != null)
                {
                    yield return new(kvp.Key, dict);
                }
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
