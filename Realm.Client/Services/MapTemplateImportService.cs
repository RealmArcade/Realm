using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Network;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

public class MapTemplateImportService
{
	public const string ImportedFromMapProperty = "Attribution_ImportedFromMap";

	private const string TemplatesRootKey = "Templates";

	private static readonly (string SectionKey, string ObjectType)[] TemplateArraySections =
	{
		("Units", "Unit"),
		("Buildings", "Building"),
		("Resources", "Resource"),
		("Props", "Prop"),
		("Weapons", "Weapon"),
		("Abilities", "Ability"),
		("Upgrades", "Upgrade"),
		("Items", "Item"),
		("Attachments", "Attachment"),
		("Vfx", "VFX")
	};

	private static readonly (string SectionKey, string ObjectType)[] TemplateDictionarySections =
	{
		("textures", "Terrain"),
		("vfx_spritesheets", "Spritesheet"),
		("decals", "Decal"),
		("icons", "Icon"),
		("skyboxes", "Skybox"),
		("ribbons", "Ribbon"),
		("noise_textures", "Noise"),
		("SpawnShader", "Shader"),
		("gdshader", "Shader")
	};

	private static readonly (string SectionKey, string ObjectType)[] ExtraTemplateArraySections =
	{
		("StatusEffects", "Status Effect"),
		("TechTree", "Tech Tree")
	};

	private static readonly Dictionary<string, string[]> ReferenceSectionHints = new(StringComparer.OrdinalIgnoreCase)
	{
		["Abilities"] = new[] { "Abilities" },
		["GrantedAbilities"] = new[] { "Abilities" },
		["UseAbility"] = new[] { "Abilities" },
		["Weapons"] = new[] { "Weapons" },
		["GrantedWeapons"] = new[] { "Weapons" },
		["Upgrades"] = new[] { "Upgrades" },
		["GrantedUpgrades"] = new[] { "Upgrades" },
		["StartingItems"] = new[] { "Items" },
		["ShopItems"] = new[] { "Items" },
		["VisualEffect"] = new[] { "Vfx" },
		["ImpactVisualEffect"] = new[] { "Vfx" },
		["ImpactParticleEffect"] = new[] { "Vfx" },
		["ChildVfxId"] = new[] { "Vfx" },
		["EffectId"] = new[] { "Vfx" },
		["VfxId"] = new[] { "Vfx" },
		["DecalId"] = new[] { "decals" },
		["SummonedUnitId"] = new[] { "Units", "Buildings" },
		["BuildOptions"] = new[] { "Units", "Buildings" },
		["AffectedUnitIds"] = new[] { "Units", "Buildings" },
		["SpawnShader"] = new[] { "SpawnShader" },
		["DeathShader"] = new[] { "SpawnShader" },
		["DespawnShader"] = new[] { "SpawnShader" },
		["AttachmentId"] = new[] { "Attachments" },
		["ParentAttachmentId"] = new[] { "Attachments" },
		["RightHandAttachment"] = new[] { "Attachments" },
		["LeftHandAttachment"] = new[] { "Attachments" },
		["StatusEffects"] = new[] { "StatusEffects" },
		["PassiveStatusEffects"] = new[] { "StatusEffects" },
		["AppliedStatusEffects"] = new[] { "StatusEffects" }
	};

	private static readonly string[] TemplateIdentityKeys = { "TemplateID", "AttachmentId", "VfxId", "Id" };

	private static readonly HashSet<string> ReferencePropertyNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"Abilities",
		"Weapons",
		"Upgrades",
		"BuildOptions",
		"StartingItems",
		"ShopItems",
		"StatusEffects",
		"PassiveStatusEffects",
		"AppliedStatusEffects",
		"SummonedUnitId",
		"UseAbility",
		"GrantedWeapons",
		"GrantedAbilities",
		"GrantedUpgrades",
		"AffectedUnitIds",
		"VisualEffect",
		"ImpactVisualEffect",
		"ImpactParticleEffect",
		"SpawnShader",
		"DeathShader",
		"DespawnShader",
		"ProceduralAnimation",
		"ProceduralMotionId",
		"CooldownLink",
		"Requirement",
		"AttachmentId",
		"ParentAttachmentId",
		"RightHandAttachment",
		"LeftHandAttachment",
		"ChildVfxId",
		"EffectId",
		"VfxId",
		"DecalId"
	};

	private static readonly HashSet<string> ImportableAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".rmesh",
		".rtex",
		".ranim",
		".raud",
		".ogg",
		".wav",
		".mp3",
		".gdshader"
	};

	private static readonly JsonSerializerOptions MetadataWriteOptions = new()
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public static string? ResolveSourceMetadataPath(MapAssetImportSource source)
	{
		string sourceDirectory = GetSourceDirectory(source);
		if (string.IsNullOrEmpty(sourceDirectory)) return null;

		string metadataPath = Path.Combine(sourceDirectory, "metadata.json");
		if (File.Exists(metadataPath)) return metadataPath;

		return TryExtractManifestFile(source.ManifestFilePath, "metadata.json", sourceDirectory) && File.Exists(metadataPath)
			? metadataPath
			: null;
	}

	public IReadOnlyList<MapTemplateImportCandidate> EnumerateTemplates(MapAssetImportSource source, string destinationWorkspacePath)
	{
		var candidates = new List<MapTemplateImportCandidate>();
		JsonObject? sourceRoot = LoadSourceMetadata(source);
		if (sourceRoot == null) return candidates;

		var sourceIndex = new SourceTemplateIndex(sourceRoot);
		JsonObject? destinationRoot = LoadDestinationMetadata(destinationWorkspacePath);

		foreach (var entry in sourceIndex.Entries)
		{
			bool alreadyPresent = destinationRoot != null && ContainsEntry(destinationRoot, entry.SectionKey, entry.IsDictionary, entry.Identity);
			candidates.Add(new MapTemplateImportCandidate(entry.ObjectType, entry.DisplayName, entry.Identity, entry.SectionKey, entry.IsDictionary, alreadyPresent));
		}

		candidates.Sort((left, right) =>
		{
			int typeComparison = string.Compare(left.ObjectType, right.ObjectType, StringComparison.OrdinalIgnoreCase);
			return typeComparison != 0
				? typeComparison
				: string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
		});

		return candidates;
	}

	public MapTemplateImportSummary ImportTemplates(MapAssetImportSource source, string destinationWorkspacePath, IReadOnlyCollection<MapTemplateImportCandidate> selectedTemplates)
	{
		var summary = new MapTemplateImportSummary();
		if (selectedTemplates == null || selectedTemplates.Count == 0) return summary;

		JsonObject? sourceRoot = LoadSourceMetadata(source);
		if (sourceRoot == null)
		{
			summary.FailedTemplates.AddRange(selectedTemplates.Select(candidate => $"{candidate.ObjectType}: {candidate.DisplayName}"));
			return summary;
		}

		string destinationMetadataPath = MetadataService.ResolveMetadataPath(destinationWorkspacePath);
		JsonObject? destinationRoot = LoadDestinationMetadata(destinationWorkspacePath);
		if (destinationRoot == null)
		{
			GD.PrintErr($"[MapTemplateImportService] Destination metadata.json is missing: {destinationMetadataPath}");
			summary.FailedTemplates.AddRange(selectedTemplates.Select(candidate => $"{candidate.ObjectType}: {candidate.DisplayName}"));
			return summary;
		}

		var context = new ImportContext(
			new SourceTemplateIndex(sourceRoot),
			destinationRoot,
			destinationWorkspacePath,
			BuildAssetIndex(source, destinationWorkspacePath),
			$"{source.MapTitle} v{source.MapVersion}",
			summary);

		foreach (var candidate in selectedTemplates)
		{
			if (!context.SourceIndex.TryGetEntry(candidate.SectionKey, candidate.IsDictionary, candidate.Identity, out var entry))
			{
				summary.FailedTemplates.Add($"{candidate.ObjectType}: {candidate.DisplayName}");
				continue;
			}

			ImportTemplate(entry, context);
		}

		ImportReferencedAssets(context);

		if (summary.ImportedTemplates.Count > 0)
		{
			SaveDestinationMetadata(destinationMetadataPath, destinationRoot);
		}

		return summary;
	}

	private void ImportTemplate(TemplateEntry entry, ImportContext context)
	{
		string visitKey = $"{entry.SectionKey}|{entry.IsDictionary}|{entry.Identity}";
		if (!context.Visited.Add(visitKey)) return;

		if (ContainsEntry(context.DestinationRoot, entry.SectionKey, entry.IsDictionary, entry.Identity))
		{
			context.Summary.SkippedTemplates.Add($"{entry.ObjectType}: {entry.DisplayName}");
			ScanReferences(entry.Json, context);
			return;
		}

		var clone = (JsonObject)entry.Json.DeepClone();
		clone[ImportedFromMapProperty] = context.Attribution;
		AddEntry(context.DestinationRoot, entry.SectionKey, entry.IsDictionary, entry.Identity, clone);
		context.Summary.ImportedTemplates.Add($"{entry.ObjectType}: {entry.DisplayName}");

		ScanReferences(clone, context);
	}

	private void ScanReferences(JsonObject entry, ImportContext context)
	{
		foreach (var property in entry)
		{
			if (string.Equals(property.Key, ImportedFromMapProperty, StringComparison.OrdinalIgnoreCase)) continue;

			ScanReferenceNode(property.Key, property.Value, context);
		}
	}

	private void ScanReferenceNode(string propertyName, JsonNode? node, ImportContext context)
	{
		switch (node)
		{
			case JsonObject nestedObject:
				foreach (var property in nestedObject)
				{
					ResolveObjectKey(property.Key, context);
					ScanReferenceNode(property.Key, property.Value, context);
				}

				break;
			case JsonArray array:
				foreach (var item in array)
				{
					if (item is JsonValue itemValue && itemValue.TryGetValue(out string? itemText))
					{
						ResolveStringReference(propertyName, itemText, context);
					}
					else
					{
						ScanReferenceNode(propertyName, item, context);
					}
				}

				break;
			case JsonValue value when value.TryGetValue(out string? text):
				ResolveStringReference(propertyName, text, context);
				break;
		}
	}

	private void ResolveObjectKey(string propertyName, ImportContext context)
	{
		if (string.IsNullOrWhiteSpace(propertyName)) return;

		if (context.SourceIndex.TryGetEntryByFullIdentity(propertyName, out var template))
		{
			ImportTemplate(template, context);
			return;
		}

		if (context.SourceIndex.TryGetEntryBySlug(propertyName, out template))
		{
			ImportTemplate(template, context);
			return;
		}

		if (TryResolveAsset(propertyName, context, out var asset))
		{
			QueueAsset(asset, context);
		}
	}

	private void ResolveStringReference(string propertyName, string? value, ImportContext context)
	{
		if (string.IsNullOrWhiteSpace(value)) return;

		string trimmed = value.Trim();

		if (ReferenceSectionHints.TryGetValue(propertyName, out string[]? hintedSections)
		    && context.SourceIndex.TryGetEntryInSections(hintedSections, trimmed, out var hintedTemplate))
		{
			ImportTemplate(hintedTemplate, context);
			return;
		}

		if (context.SourceIndex.TryGetEntryByFullIdentity(trimmed, out var template))
		{
			ImportTemplate(template, context);
			return;
		}

		if (context.SourceIndex.TryGetEntryByDictionaryKey(trimmed, out template))
		{
			ImportTemplate(template, context);
			return;
		}

		if (ReferencePropertyNames.Contains(propertyName) && context.SourceIndex.TryGetEntryBySlug(trimmed, out template))
		{
			ImportTemplate(template, context);
			return;
		}

		if (TryResolveAsset(trimmed, context, out var asset))
		{
			QueueAsset(asset, context);
		}
	}

	private static bool TryResolveAsset(string rawValue, ImportContext context, out MapAssetImportCandidate asset)
	{
		asset = null!;

		string normalized = rawValue.Trim().Replace('\\', '/');
		if (normalized.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized.Substring(6);
		}

		normalized = normalized.TrimStart('/');
		if (normalized.Length == 0) return false;

		if (context.AssetsByRelativePath.TryGetValue(normalized, out var byRelativePath))
		{
			asset = byRelativePath;
			return true;
		}

		string fileName = Path.GetFileName(normalized);
		if (fileName.Length == 0) return false;

		if (!ImportableAssetExtensions.Contains(Path.GetExtension(fileName))) return false;

		if (!context.AssetsByFileName.TryGetValue(fileName, out var byFileName)) return false;

		asset = byFileName;
		return true;
	}

	private static void QueueAsset(MapAssetImportCandidate asset, ImportContext context)
	{
		if (!context.QueuedAssets.Add(asset.Hash)) return;

		context.ReferencedAssets.Add(asset);
	}

	private void ImportReferencedAssets(ImportContext context)
	{
		if (context.ReferencedAssets.Count == 0) return;

		var assetSummary = GameContext.MapAssetImportService.ImportInto(context.DestinationWorkspacePath, context.ReferencedAssets);
		context.Summary.ImportedAssets.AddRange(assetSummary.Imported);
		context.Summary.SkippedAssets.AddRange(assetSummary.Skipped);
		context.Summary.MissingAssets.AddRange(assetSummary.Missing);
		context.Summary.ConflictAssets.AddRange(assetSummary.Conflicts);
		context.Summary.FailedAssets.AddRange(assetSummary.Failed);
	}

	private static AssetLookup BuildAssetIndex(MapAssetImportSource source, string destinationWorkspacePath)
	{
		var lookup = new AssetLookup();
		IReadOnlyList<MapAssetImportCandidate> candidates = GameContext.MapAssetImportService.EnumerateAssets(source.ManifestFilePath, destinationWorkspacePath);

		foreach (var candidate in candidates)
		{
			if (!ImportableAssetExtensions.Contains(Path.GetExtension(candidate.FileName))) continue;

			lookup.ByFileName.TryAdd(candidate.FileName, candidate);
			lookup.ByRelativePath.TryAdd(NormalizeRelativePath(candidate.RelativePath), candidate);
		}

		return lookup;
	}

	private static JsonObject? LoadSourceMetadata(MapAssetImportSource source)
	{
		string? metadataPath = ResolveSourceMetadataPath(source);
		if (string.IsNullOrEmpty(metadataPath)) return null;

		try
		{
			return JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapTemplateImportService] Failed to read source metadata '{metadataPath}': {ex.Message}");
			return null;
		}
	}

	private static JsonObject? LoadDestinationMetadata(string destinationWorkspacePath)
	{
		string metadataPath = MetadataService.ResolveMetadataPath(destinationWorkspacePath);
		if (!File.Exists(metadataPath)) return null;

		try
		{
			return JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapTemplateImportService] Failed to read destination metadata '{metadataPath}': {ex.Message}");
			return null;
		}
	}

	private static void SaveDestinationMetadata(string metadataPath, JsonObject destinationRoot)
	{
		try
		{
			EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
			File.WriteAllText(metadataPath, destinationRoot.ToJsonString(MetadataWriteOptions));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapTemplateImportService] Failed to save destination metadata '{metadataPath}': {ex.Message}");
		}
	}

	private static void AddEntry(JsonObject destinationRoot, string sectionKey, bool isDictionary, string identity, JsonObject entry)
	{
		if (isDictionary)
		{
			GetOrCreateObject(destinationRoot, sectionKey)[identity] = entry;
			return;
		}

		JsonObject templates = GetOrCreateObject(destinationRoot, TemplatesRootKey);
		GetOrCreateArray(templates, sectionKey).Add(entry);
	}

	private static bool ContainsEntry(JsonObject root, string sectionKey, bool isDictionary, string identity)
	{
		if (isDictionary)
		{
			return root[sectionKey] is JsonObject dictionary && ContainsKeyIgnoreCase(dictionary, identity);
		}

		if (root[TemplatesRootKey] is not JsonObject templates) return false;
		if (templates[sectionKey] is not JsonArray entries) return false;

		foreach (var entry in entries)
		{
			if (entry is JsonObject entryObject && string.Equals(ReadIdentity(entryObject), identity, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static bool ContainsKeyIgnoreCase(JsonObject dictionary, string key)
	{
		foreach (var property in dictionary)
		{
			if (string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	private static JsonObject GetOrCreateObject(JsonObject parent, string key)
	{
		if (parent[key] is JsonObject existing) return existing;

		var created = new JsonObject();
		parent[key] = created;
		return created;
	}

	private static JsonArray GetOrCreateArray(JsonObject parent, string key)
	{
		if (parent[key] is JsonArray existing) return existing;

		var created = new JsonArray();
		parent[key] = created;
		return created;
	}

	private static string ReadIdentity(JsonObject entry)
	{
		foreach (string identityKey in TemplateIdentityKeys)
		{
			if (entry[identityKey] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text))
			{
				return text.Trim();
			}
		}

		return string.Empty;
	}

	private static string GetSlug(string identity)
	{
		int slashIndex = identity.LastIndexOf('/');
		return slashIndex >= 0 ? identity.Substring(slashIndex + 1) : identity;
	}

	private static MapManifest LoadSourceManifest(string manifestFilePath)
	{
		try
		{
			return MapManifest.LoadFromFile(manifestFilePath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapTemplateImportService] Failed to read manifest '{manifestFilePath}': {ex.Message}");
			return new MapManifest();
		}
	}

	private static bool TryExtractManifestFile(string manifestFilePath, string relativePath, string destinationDirectory)
	{
		MapManifest manifest = LoadSourceManifest(manifestFilePath);
		if (manifest.Files == null || !manifest.Files.TryGetValue(relativePath, out string? hash) || string.IsNullOrEmpty(hash)) return false;

		return MapAssetManager.ExtractSingleAsset(relativePath, hash, destinationDirectory);
	}

	private static string GetSourceDirectory(MapAssetImportSource source)
	{
		return string.IsNullOrEmpty(source.ManifestFilePath)
			? string.Empty
			: Path.GetDirectoryName(source.ManifestFilePath) ?? string.Empty;
	}

	private static string NormalizeRelativePath(string relativePath)
	{
		return relativePath.Replace('\\', '/').TrimStart('/');
	}

	private class ImportContext
	{
		public ImportContext(
			SourceTemplateIndex sourceIndex,
			JsonObject destinationRoot,
			string destinationWorkspacePath,
			AssetLookup assets,
			string attribution,
			MapTemplateImportSummary summary)
		{
			SourceIndex = sourceIndex;
			DestinationRoot = destinationRoot;
			DestinationWorkspacePath = destinationWorkspacePath;
			AssetsByFileName = assets.ByFileName;
			AssetsByRelativePath = assets.ByRelativePath;
			Attribution = attribution;
			Summary = summary;
		}

		public SourceTemplateIndex SourceIndex { get; }
		public JsonObject DestinationRoot { get; }
		public string DestinationWorkspacePath { get; }
		public Dictionary<string, MapAssetImportCandidate> AssetsByFileName { get; }
		public Dictionary<string, MapAssetImportCandidate> AssetsByRelativePath { get; }
		public string Attribution { get; }
		public MapTemplateImportSummary Summary { get; }
		public HashSet<string> Visited { get; } = new(StringComparer.OrdinalIgnoreCase);
		public HashSet<string> QueuedAssets { get; } = new(StringComparer.OrdinalIgnoreCase);
		public List<MapAssetImportCandidate> ReferencedAssets { get; } = new();
	}

	private class AssetLookup
	{
		public Dictionary<string, MapAssetImportCandidate> ByFileName { get; } = new(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, MapAssetImportCandidate> ByRelativePath { get; } = new(StringComparer.OrdinalIgnoreCase);
	}

	private class SourceTemplateIndex
	{
		private readonly Dictionary<string, TemplateEntry> _byFullIdentity = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, TemplateEntry> _bySlug = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, TemplateEntry> _byDictionaryKey = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, TemplateEntry> _byLookup = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, Dictionary<string, TemplateEntry>> _bySection = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _ambiguousSlugs = new(StringComparer.OrdinalIgnoreCase);

		public SourceTemplateIndex(JsonObject sourceRoot)
		{
			if (sourceRoot[TemplatesRootKey] is JsonObject templates)
			{
				var registeredSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				foreach (var section in TemplateArraySections)
				{
					RegisterTemplateArray(templates, section.SectionKey, section.ObjectType);
					registeredSections.Add(section.SectionKey);
				}

				foreach (var section in ExtraTemplateArraySections)
				{
					RegisterTemplateArray(templates, section.SectionKey, section.ObjectType);
					registeredSections.Add(section.SectionKey);
				}

				foreach (var property in templates)
				{
					if (property.Value is not JsonArray || registeredSections.Contains(property.Key)) continue;

					RegisterTemplateArray(templates, property.Key, property.Key);
				}
			}

			foreach (var section in TemplateDictionarySections)
			{
				if (sourceRoot[section.SectionKey] is not JsonObject dictionary) continue;

				foreach (var property in dictionary)
				{
					if (property.Value is not JsonObject entryObject) continue;

					var templateEntry = new TemplateEntry(entryObject, section.SectionKey, section.ObjectType, property.Key, true, GetDisplayName(entryObject, property.Key));
					Register(templateEntry);

					_byDictionaryKey.TryAdd(property.Key, templateEntry);
					_byDictionaryKey.TryAdd(Path.GetFileNameWithoutExtension(property.Key), templateEntry);
				}
			}
		}

		public List<TemplateEntry> Entries { get; } = new();

		public bool TryGetEntryInSections(string[] sectionKeys, string value, out TemplateEntry entry)
		{
			string slug = GetSlug(value);

			foreach (string sectionKey in sectionKeys)
			{
				if (!_bySection.TryGetValue(sectionKey, out var sectionEntries)) continue;

				if (sectionEntries.TryGetValue(value, out entry)) return true;
				if (sectionEntries.TryGetValue(slug, out entry)) return true;
			}

			entry = null!;
			return false;
		}

		public bool TryGetEntry(string sectionKey, bool isDictionary, string identity, out TemplateEntry entry)
		{
			return _byLookup.TryGetValue(BuildLookupKey(sectionKey, isDictionary, identity), out entry);
		}

		public bool TryGetEntryByFullIdentity(string identity, out TemplateEntry entry)
		{
			return _byFullIdentity.TryGetValue(identity, out entry);
		}

		public bool TryGetEntryBySlug(string slug, out TemplateEntry entry)
		{
			if (_ambiguousSlugs.Contains(slug))
			{
				entry = null!;
				return false;
			}

			return _bySlug.TryGetValue(slug, out entry);
		}

		public bool TryGetEntryByDictionaryKey(string key, out TemplateEntry entry)
		{
			return _byDictionaryKey.TryGetValue(key, out entry);
		}

		private void RegisterTemplateArray(JsonObject templates, string sectionKey, string objectType)
		{
			if (templates[sectionKey] is not JsonArray entries) return;

			foreach (var entry in entries)
			{
				if (entry is not JsonObject entryObject) continue;

				string identity = ReadIdentity(entryObject);
				if (string.IsNullOrEmpty(identity)) continue;

				var templateEntry = new TemplateEntry(entryObject, sectionKey, objectType, identity, false, GetDisplayName(entryObject, identity));
				Register(templateEntry);
			}
		}

		private void Register(TemplateEntry entry)
		{
			Entries.Add(entry);
			_byFullIdentity.TryAdd(entry.Identity, entry);
			_byLookup.TryAdd(BuildLookupKey(entry.SectionKey, entry.IsDictionary, entry.Identity), entry);

			if (!_bySection.TryGetValue(entry.SectionKey, out var sectionEntries))
			{
				sectionEntries = new Dictionary<string, TemplateEntry>(StringComparer.OrdinalIgnoreCase);
				_bySection[entry.SectionKey] = sectionEntries;
			}

			sectionEntries.TryAdd(entry.Identity, entry);

			string slug = GetSlug(entry.Identity);
			if (string.IsNullOrEmpty(slug)) return;

			sectionEntries.TryAdd(slug, entry);

			if (_bySlug.ContainsKey(slug))
			{
				_ambiguousSlugs.Add(slug);
				return;
			}

			_bySlug[slug] = entry;
		}

		private static string BuildLookupKey(string sectionKey, bool isDictionary, string identity)
		{
			return $"{sectionKey}|{isDictionary}|{identity}";
		}

		private static string GetDisplayName(JsonObject entry, string identity)
		{
			return entry["Name"] is JsonValue nameValue && nameValue.TryGetValue(out string? name) && !string.IsNullOrWhiteSpace(name)
				? name
				: identity;
		}
	}

	private class TemplateEntry
	{
		public TemplateEntry(JsonObject json, string sectionKey, string objectType, string identity, bool isDictionary, string displayName)
		{
			Json = json;
			SectionKey = sectionKey;
			ObjectType = objectType;
			Identity = identity;
			IsDictionary = isDictionary;
			DisplayName = displayName;
		}

		public JsonObject Json { get; }
		public string SectionKey { get; }
		public string ObjectType { get; }
		public string Identity { get; }
		public bool IsDictionary { get; }
		public string DisplayName { get; }
	}
}

public class MapTemplateImportCandidate
{
	public MapTemplateImportCandidate(string objectType, string displayName, string identity, string sectionKey, bool isDictionary, bool alreadyPresent)
	{
		ObjectType = objectType;
		DisplayName = displayName;
		Identity = identity;
		SectionKey = sectionKey;
		IsDictionary = isDictionary;
		AlreadyPresent = alreadyPresent;
	}

	public string ObjectType { get; }
	public string DisplayName { get; }
	public string Identity { get; }
	public string SectionKey { get; }
	public bool IsDictionary { get; }
	public bool AlreadyPresent { get; }

	public string SelectionKey => $"{SectionKey}|{IsDictionary}|{Identity}";
}

public class MapTemplateImportSummary
{
	public List<string> ImportedTemplates { get; } = new();
	public List<string> SkippedTemplates { get; } = new();
	public List<string> FailedTemplates { get; } = new();
	public List<string> ImportedAssets { get; } = new();
	public List<string> SkippedAssets { get; } = new();
	public List<string> MissingAssets { get; } = new();
	public List<string> ConflictAssets { get; } = new();
	public List<string> FailedAssets { get; } = new();
}
