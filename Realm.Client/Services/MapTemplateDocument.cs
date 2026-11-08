using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Client.Core;
using Realm.Client.Network;

namespace Realm.Client.Services;

/// <summary>
///     Reads and writes the metadata document a template import merges into: the source and destination files, the
///     section containers entries are added to, and the identity a template is known by.
/// </summary>
internal static class MapTemplateDocument
{
		internal const string TemplatesRootKey = "Templates";
		internal static readonly string[] TemplateIdentityKeys = { "TemplateID", "AttachmentId", "VfxId", "Id" };
		internal static readonly JsonSerializerOptions MetadataWriteOptions = new()
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		};

	internal static string? ResolveSourceMetadataPath(MapAssetImportSource source)
	{
		string sourceDirectory = GetSourceDirectory(source);
		if (string.IsNullOrEmpty(sourceDirectory)) return null;

		string metadataPath = Path.Combine(sourceDirectory, "metadata.json");
		if (File.Exists(metadataPath)) return metadataPath;

		return TryExtractManifestFile(source.ManifestFilePath, "metadata.json", sourceDirectory) && File.Exists(metadataPath)
			? metadataPath
			: null;
	}

	internal static MapManifest LoadSourceManifest(string manifestFilePath)
	{
		try
		{
			return MapManifest.LoadFromFile(manifestFilePath);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapTemplateImportService] Failed to read manifest '{manifestFilePath}': {ex.Message}");
			return new MapManifest();
		}
	}

	internal static bool TryExtractManifestFile(string manifestFilePath, string relativePath, string destinationDirectory)
	{
		MapManifest manifest = LoadSourceManifest(manifestFilePath);
		if (manifest.Files == null || !manifest.Files.TryGetValue(relativePath, out string? hash) || string.IsNullOrEmpty(hash)) return false;

		return MapAssetManager.ExtractSingleAsset(relativePath, hash, destinationDirectory);
	}

	internal static string GetSourceDirectory(MapAssetImportSource source)
	{
		return string.IsNullOrEmpty(source.ManifestFilePath)
			? string.Empty
			: Path.GetDirectoryName(source.ManifestFilePath) ?? string.Empty;
	}

	internal static JsonObject? LoadSourceMetadata(MapAssetImportSource source)
	{
		string? metadataPath = ResolveSourceMetadataPath(source);
		if (string.IsNullOrEmpty(metadataPath)) return null;

		try
		{
			return JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapTemplateImportService] Failed to read source metadata '{metadataPath}': {ex.Message}");
			return null;
		}
	}

	internal static JsonObject? LoadDestinationMetadata(string destinationWorkspacePath)
	{
		string metadataPath = MetadataService.ResolveMetadataPath(destinationWorkspacePath);
		if (!File.Exists(metadataPath)) return null;

		try
		{
			return JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapTemplateImportService] Failed to read destination metadata '{metadataPath}': {ex.Message}");
			return null;
		}
	}

	internal static void SaveDestinationMetadata(string metadataPath, JsonObject destinationRoot)
	{
		try
		{
			File.WriteAllText(metadataPath, destinationRoot.ToJsonString(MetadataWriteOptions));
			EditorWorkspaceWatcher.NoteInternalFileWrite(metadataPath);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapTemplateImportService] Failed to save destination metadata '{metadataPath}': {ex.Message}");
		}
	}

	internal static void AddEntry(JsonObject destinationRoot, string sectionKey, bool isDictionary, string identity, JsonObject entry)
	{
		if (isDictionary)
		{
			GetOrCreateObject(destinationRoot, sectionKey)[identity] = entry;
			return;
		}

		JsonObject templates = GetOrCreateObject(destinationRoot, TemplatesRootKey);
		GetOrCreateArray(templates, sectionKey).Add(entry);
	}

	internal static bool ContainsEntry(JsonObject root, string sectionKey, bool isDictionary, string identity)
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

	internal static bool ContainsKeyIgnoreCase(JsonObject dictionary, string key)
	{
		foreach (var property in dictionary)
		{
			if (string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	internal static JsonObject GetOrCreateObject(JsonObject parent, string key)
	{
		if (parent[key] is JsonObject existing) return existing;

		var created = new JsonObject();
		parent[key] = created;
		return created;
	}

	internal static JsonArray GetOrCreateArray(JsonObject parent, string key)
	{
		if (parent[key] is JsonArray existing) return existing;

		var created = new JsonArray();
		parent[key] = created;
		return created;
	}

	internal static string ReadIdentity(JsonObject entry)
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

	internal static string GetSlug(string identity)
	{
		int slashIndex = identity.LastIndexOf('/');
		return slashIndex >= 0 ? identity.Substring(slashIndex + 1) : identity;
	}

	internal static string NormalizeRelativePath(string relativePath)
	{
		return relativePath.Replace('\\', '/').TrimStart('/');
	}
}
