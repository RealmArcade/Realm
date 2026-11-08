﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Realm.Client.Network;
using Realm.Client.Utils;
using Realm.Shared.Services;

namespace Realm.Client.Services;

/// <summary>
///     Reading a template's JSON for search terms: finding the object a slug names, collecting the text a
///     template carries, and following the asset references it makes into the asset index's names.
/// </summary>
internal static class TemplateJsonSearch
{
	internal static JsonObject? FindTemplateOrEntryObject(JsonObject rootObj, string templateSlug)
	{
		if (rootObj.TryGetPropertyValue("Templates", out var templatesNode) && templatesNode is JsonObject templatesObj)
		{
			foreach (var kvp in templatesObj)
			{
				if (kvp.Value is JsonArray arr)
				{
					foreach (var item in arr)
					{
						if (item is JsonObject obj && IsTemplateMatch(obj, templateSlug))
						{
							return obj;
						}
					}
				}
			}
		}

		string[] dictionaryKeys = { "textures", "vfx_spritesheets", "decals", "SpawnShader" };
		var (_, slug) = TemplateIDHelper.ParseTemplateID(templateSlug);

		foreach (string dictKey in dictionaryKeys)
		{
			if (rootObj.TryGetPropertyValue(dictKey, out var dictNode) && dictNode is JsonObject dictObj)
			{
				foreach (var kvp in dictObj)
				{
					if (string.Equals(kvp.Key, templateSlug, StringComparison.OrdinalIgnoreCase) ||
					    (!string.IsNullOrEmpty(slug) && string.Equals(kvp.Key, slug, StringComparison.OrdinalIgnoreCase)) ||
					    (!string.IsNullOrEmpty(slug) && string.Equals(Path.GetFileNameWithoutExtension(kvp.Key), slug, StringComparison.OrdinalIgnoreCase)))
					{
						if (kvp.Value is JsonObject itemObj)
						{
							return itemObj;
						}
					}
				}
			}
		}

		return null;
	}

	internal static bool IsTemplateMatch(JsonObject obj, string templateSlug)
	{
		string? slug = GetTemplateSlugFromJsonObject(obj);
		if (string.IsNullOrEmpty(slug)) return false;

		if (string.Equals(slug, templateSlug, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (slug.Contains('/'))
		{
			var parts = slug.Split('/');
			if (parts.Length > 1 && string.Equals(parts[1], templateSlug, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		var (_, parsedSlug) = TemplateIDHelper.ParseTemplateID(templateSlug);
		if (!string.IsNullOrEmpty(parsedSlug))
		{
			if (string.Equals(slug, parsedSlug, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (slug.Contains('/'))
			{
				var parts = slug.Split('/');
				if (parts.Length > 1 && string.Equals(parts[1], parsedSlug, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}

		return false;
	}

	internal static string? GetTemplateSlugFromJsonObject(JsonObject obj)
	{
		if (obj.TryGetPropertyValue("TemplateID", out var tidNode) && tidNode != null)
		{
			return tidNode.ToString();
		}
		if (obj.TryGetPropertyValue("template_id", out var tidNodeLower) && tidNodeLower != null)
		{
			return tidNodeLower.ToString();
		}
		if (obj.TryGetPropertyValue("AttachmentId", out var aidNode) && aidNode != null)
		{
			return aidNode.ToString();
		}
		if (obj.TryGetPropertyValue("VfxId", out var vidNode) && vidNode != null)
		{
			return vidNode.ToString();
		}
		if (obj.TryGetPropertyValue("DecalId", out var didNode) && didNode != null)
		{
			return didNode.ToString();
		}
		if (obj.TryGetPropertyValue("decal_id", out var didNodeLower) && didNodeLower != null)
		{
			return didNodeLower.ToString();
		}

		return null;
	}

	internal static void TraverseJsonForTermsAndAssetReferences(JsonNode node, HashSet<string> op1Terms, List<string> assetReferences)
	{
		if (node is JsonObject obj)
		{
			foreach (var kvp in obj)
			{
				if (kvp.Value == null) continue;

				if (kvp.Value is JsonValue val && val.TryGetValue<string>(out string? strVal) && !string.IsNullOrWhiteSpace(strVal))
				{
					if (IsAssetExtension(strVal))
					{
						assetReferences.Add(strVal);
					}
					else
					{
						SplitAndAddTerms(strVal, op1Terms);
					}
				}
				else
				{
					TraverseJsonForTermsAndAssetReferences(kvp.Value, op1Terms, assetReferences);
				}
			}
		}
		else if (node is JsonArray arr)
		{
			foreach (var item in arr)
			{
				if (item == null) continue;

				if (item is JsonValue val && val.TryGetValue<string>(out string? strVal) && !string.IsNullOrWhiteSpace(strVal))
				{
					if (IsAssetExtension(strVal))
					{
						assetReferences.Add(strVal);
					}
					else
					{
						SplitAndAddTerms(strVal, op1Terms);
					}
				}
				else
				{
					TraverseJsonForTermsAndAssetReferences(item, op1Terms, assetReferences);
				}
			}
		}
	}

	internal static bool IsAssetExtension(string? value)
	{
		if (value == null)
		{
			return false;
		}

		string lower = value.ToLowerInvariant();
		return lower.EndsWith(".rtex") || lower.EndsWith(".raud") || lower.EndsWith(".rmesh") || lower.EndsWith(".ranim");
	}

	internal static void SplitAndAddTerms(string text, HashSet<string> termsSet)
	{
		string[] words = EditorAssetOperations.NonAlphaRegex().Split(text);
		foreach (string word in words)
		{
			if (!string.IsNullOrWhiteSpace(word) && word.Length > 1)
			{
				termsSet.Add(word.Trim().ToLowerInvariant());
			}
		}
	}

	internal static void ExtractTermsFromAssetReference(string wsPath, string assetRef, HashSet<string> termsSet)
	{
		string fileName = Path.GetFileName(assetRef);
		string fileNameWithoutExt = Path.GetFileNameWithoutExtension(assetRef);
		SplitAndAddTerms(fileNameWithoutExt, termsSet);

		string? fullDiskPath = null;
		if (File.Exists(assetRef))
		{
			fullDiskPath = assetRef;
		}
		else
		{
			string directWs = Path.Combine(wsPath, assetRef.TrimStart('/', '\\'));
			if (File.Exists(directWs))
			{
				fullDiskPath = directWs;
			}
			else
			{
				string assetsWs = Path.Combine(wsPath, "Assets", assetRef.TrimStart('/', '\\'));
				if (File.Exists(assetsWs))
				{
					fullDiskPath = assetsWs;
				}
				else
				{
					var manifest = MapFileService.LoadManifest(wsPath);
					if (manifest.Assets != null)
					{
						var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
						MapManifest.FlattenAssetsInto(dict, manifest.Assets);
						if (dict.TryGetValue(fileName, out string? hash) && !string.IsNullOrEmpty(hash))
						{
							fullDiskPath = MapAssetManager.Storage.FindAssetFilePath(hash);
						}
					}
				}
			}
		}

		if (fullDiskPath != null && File.Exists(fullDiskPath))
		{
			string? metaJson = RealmMetadataHelper.ExtractMetadata(fullDiskPath);
			if (!string.IsNullOrEmpty(metaJson))
			{
				try
				{
					var metaObj = JsonNode.Parse(metaJson) as JsonObject;
					if (metaObj != null && metaObj.TryGetPropertyValue("tags", out var tagsNode) && tagsNode is JsonArray tagArr)
					{
						foreach (var t in tagArr)
						{
							if (t != null)
							{
								SplitAndAddTerms(t.ToString(), termsSet);
							}
						}
					}
				}
				catch
				{
				}
			}
		}
	}
}
