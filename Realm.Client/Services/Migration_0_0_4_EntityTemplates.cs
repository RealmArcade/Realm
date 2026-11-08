﻿using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace Realm.Client.Services;

/// <summary>
///     The 0.0.4 migration's entity pass: the legacy per-type containers (customUnits, customProps and
///     the rest) fold into Templates under their canonical key, each entity's id becomes a TemplateID
///     and its model path a bare file name, and decals get the same treatment with their defaults.
/// </summary>
internal static class Migration_0_0_4_EntityTemplates
{
	internal static void MigrateEntityCategories(JsonObject metadataRoot, JsonObject templatesObj)
	{
		var entityCategories = new (string LegacyCustomKey, string CanonicalKey, string ObjectType, string LegacyIdKey)[]
		{
			("CustomUnits", "Units", "unit", "UnitId"), ("CustomBuildings", "Buildings", "building", "UnitId"),
			("CustomResources", "Resources", "resource", "UnitId"), ("CustomProps", "Props", "prop", "UnitId"),
			("CustomAbilities", "Abilities", "ability", "AbilityId"), ("CustomWeapons", "Weapons", "weapon", "WeaponId"),
			("CustomUpgrades", "Upgrades", "upgrade", "UpgradeId"), ("CustomItems", "Items", "item", "ItemId"),
			("CustomAttachments", "Attachments", "", ""), ("CustomVfx", "Vfx", "", "")
		};

		foreach (var (legacyKey, canonicalKey, objectType, legacyIdKey) in entityCategories)
		{
			MigrateEntityCategoryList(metadataRoot, templatesObj, legacyKey, canonicalKey);
			NormalizeEntityIdsAndModels(templatesObj, canonicalKey, objectType, legacyIdKey);
		}
	}

	internal static void MigrateEntityCategoryList(JsonObject metadataRoot, JsonObject templatesObj, string legacyKey, string canonicalKey)
	{
		if (metadataRoot.TryGetPropertyValue(legacyKey, out var legacyArrNode) && legacyArrNode is JsonArray legacyArray)
		{
			if (!templatesObj.ContainsKey(canonicalKey) || templatesObj[canonicalKey] is not JsonArray)
			{
				templatesObj[canonicalKey] = new JsonArray();
			}

			var canonicalArray = templatesObj[canonicalKey]!.AsArray();
			foreach (var item in legacyArray)
			{
				if (item != null) canonicalArray.Add(item.DeepClone());
			}

			metadataRoot.Remove(legacyKey);
		}
	}

	internal static void NormalizeEntityIdsAndModels(JsonObject templatesObj, string canonicalKey, string objectType, string legacyIdKey)
	{
		if (string.IsNullOrEmpty(objectType)) return;
		if (!templatesObj.TryGetPropertyValue(canonicalKey, out var arrNode) || arrNode is not JsonArray entityArray) return;

		foreach (var item in entityArray.OfType<JsonObject>())
		{
			string rawId = ExtractRawId(item, legacyIdKey);
			if (!string.IsNullOrWhiteSpace(rawId))
			{
				item["TemplateID"] = TemplateIDHelper.NormalizeTemplateID(objectType, rawId);
			}

			string modelPath = item["ModelPath"]?.ToString() ?? string.Empty;
			if (modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			{
				item["VisualMode"] = "Mesh";
			}
		}
	}

	internal static string ExtractRawId(JsonObject entityObj, string legacyIdKey)
	{
		string rawId = string.Empty;
		if (entityObj.TryGetPropertyValue("TemplateID", out var tid) && tid != null) rawId = tid.ToString();
		else if (entityObj.TryGetPropertyValue("ObjectID", out var oid) && oid != null)
		{
			rawId = oid.ToString();
			entityObj.Remove("ObjectID");
		}
		else if (!string.IsNullOrEmpty(legacyIdKey) && entityObj.TryGetPropertyValue(legacyIdKey, out var lid) && lid != null)
		{
			rawId = lid.ToString();
			entityObj.Remove(legacyIdKey);
		}
		else if (entityObj.TryGetPropertyValue("UnitId", out var uid) && uid != null)
		{
			rawId = uid.ToString();
			entityObj.Remove("UnitId");
		}

		return rawId;
	}

	internal static void MigrateDecals(JsonObject metadataRoot)
	{
		if (!metadataRoot.TryGetPropertyValue("decals", out var decalsNode) || decalsNode is not JsonObject decalsObj) return;

		var normalizedDecals = new JsonObject();
		foreach (var kvp in decalsObj)
		{
			string rawKey = kvp.Key;
			string slug = TemplateIDHelper.GenerateSlug(rawKey);
			string normalizedId = TemplateIDHelper.NormalizeTemplateID("decal", slug);
			var itemObj = kvp.Value as JsonObject ?? new JsonObject();

			CleanHashAndAssetType(itemObj);
			if (string.IsNullOrWhiteSpace(itemObj["TexturePath"]?.ToString()))
			{
				itemObj["TexturePath"] = rawKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? rawKey : $"{slug}.rtex";
			}

			EnsureDecalDefaults(itemObj);
			MergeOrAddNormalizedItem(normalizedDecals, normalizedId, itemObj);
		}

		metadataRoot["decals"] = normalizedDecals;
	}

	internal static void CleanHashAndAssetType(JsonObject itemObj)
	{
		itemObj.Remove("Hash");
		itemObj.Remove("hash");
		itemObj.Remove("AssetType");
		itemObj.Remove("assetType");
		itemObj.Remove("asset_type");
	}

	internal static void EnsureDecalDefaults(JsonObject itemObj)
	{
		if (!itemObj.ContainsKey("Opacity")) itemObj["Opacity"] = 1.0f;
		if (!itemObj.ContainsKey("Brightness")) itemObj["Brightness"] = 1.0f;
		if (!itemObj.ContainsKey("Contrast")) itemObj["Contrast"] = 1.0f;
		if (!itemObj.ContainsKey("Saturation")) itemObj["Saturation"] = 1.0f;
	}

	internal static void MergeOrAddNormalizedItem(JsonObject normalizedCollection, string normalizedId, JsonObject itemObj)
	{
		if (!normalizedCollection.ContainsKey(normalizedId))
		{
			normalizedCollection[normalizedId] = itemObj.DeepClone();
		}
		else if (normalizedCollection[normalizedId] is JsonObject existingObj)
		{
			foreach (var prop in itemObj)
			{
				if (prop.Value != null && (!existingObj.ContainsKey(prop.Key) || existingObj[prop.Key] == null))
				{
					existingObj[prop.Key] = prop.Value.DeepClone();
				}
			}
		}
	}
}
