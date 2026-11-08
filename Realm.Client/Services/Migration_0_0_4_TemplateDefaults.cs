﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Realm.Client.Services;

/// <summary>
///     The fallback templates the 0.0.4 migration writes for assets that its manifest pass finds without a
///     matching template: a unit, building, prop, resource, decal, terrain texture, spritesheet, icon or
///     shader entry generated from the asset's file name, plus the small lookups those checks share.
/// </summary>
internal static class Migration_0_0_4_TemplateDefaults
{
	internal static JsonObject CreateDefaultShaderConfig(
		string name,
		int transitionMode,
		int direction,
		string edgeColorHex,
		float edgeWidth,
		float edgeEmission,
		float noiseScale,
		float noiseRoughness,
		float fresnelPower,
		float vertexDisplacement,
		float alphaFade,
		float duration)
	{
		return new JsonObject
		{
			["Name"] = name,
			["TransitionMode"] = transitionMode,
			["Direction"] = direction,
			["EdgeColor"] = edgeColorHex,
			["EdgeWidth"] = edgeWidth,
			["EdgeEmission"] = edgeEmission,
			["NoiseScale"] = noiseScale,
			["NoiseRoughness"] = noiseRoughness,
			["FresnelPower"] = fresnelPower,
			["VertexDisplacement"] = vertexDisplacement,
			["AlphaFade"] = alphaFade,
			["Duration"] = duration,
			["AssetType"] = "SpawnShader"
		};
	}

	internal static bool ModelExistsInOtherCategories(JsonObject templatesObj, string fileName, string slug)
	{
		return new[] { "Units", "Buildings", "Resources", "Props" }
			.Where(cat => templatesObj.ContainsKey(cat) && templatesObj[cat] is JsonArray)
			.SelectMany(cat => templatesObj[cat]!.AsArray().OfType<JsonObject>())
			.Any(item =>
				string.Equals(item["ModelPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(TemplateIDHelper.GenerateSlug(item["ModelPath"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(TemplateIDHelper.GenerateSlug(item["TemplateID"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase));
	}

	internal static void EnsureTemplateForManifestAsset(JsonObject metadataRoot, JsonObject templatesObj, string category, string fileName, string hash)
	{
		if (string.IsNullOrWhiteSpace(fileName)) return;

		string slug = TemplateIDHelper.GenerateSlug(fileName);
		string normalizedCategory = MapAssetHelper.NormalizeCategoryKey(category);

		switch (normalizedCategory.ToLowerInvariant())
		{
			case "character" or "unit" or "characters" or "units":
				EnsureUnitTemplate(templatesObj, fileName, slug);
				break;
			case "building" or "buildings":
				EnsureBuildingTemplate(templatesObj, fileName, slug);
				break;
			case "prop" or "props":
				EnsurePropTemplate(templatesObj, fileName, slug);
				break;
			case "resource" or "resources":
				EnsureResourceTemplate(templatesObj, fileName, slug);
				break;
			case "decal" or "decals":
				EnsureDecalTemplate(metadataRoot, fileName, slug, hash);
				break;
			case "terrain" or "textures":
				EnsureTerrainTemplate(metadataRoot, fileName, slug, hash);
				break;
			case "spritesheet" or "spritesheets" or "vfx":
				EnsureSpritesheetTemplate(metadataRoot, fileName, slug, hash);
				break;
			case "icon" or "icons":
				EnsureIconTemplate(metadataRoot, slug, hash);
				break;
			case "shader" or "shaders" or "gdshader":
				EnsureShaderTemplate(metadataRoot, slug, hash);
				break;
		}
	}

	internal static void EnsureUnitTemplate(JsonObject templatesObj, string fileName, string slug)
	{
		string unitTemplateId = TemplateIDHelper.NormalizeTemplateID("unit", slug);
		var unitsArr = GetOrAddArray(templatesObj, "Units");

		if (CheckIfTemplateExists(unitsArr, unitTemplateId, fileName, slug) || ModelExistsInOtherCategories(templatesObj, fileName, slug)) return;

		unitsArr.Add(new JsonObject
		{
			["TemplateID"] = unitTemplateId,
			["Name"] = slug,
			["Description"] = "",
			["ModelPath"] = fileName,
			["VisualMode"] = GetVisualMode(fileName),
			["Scale"] = 1.0f,
			["PathingType"] = 9,
			["DespillPlayerColor"] = false,
			["NormalizeLuminance"] = true
		});
	}

	internal static JsonArray GetOrAddArray(JsonObject templatesObj, string key)
	{
		if (!templatesObj.ContainsKey(key) || templatesObj[key] is not JsonArray)
		{
			templatesObj[key] = new JsonArray();
		}

		return (JsonArray)templatesObj[key]!;
	}

	internal static bool CheckIfTemplateExists(JsonArray array, string templateId, string fileName, string slug)
	{
		return array.OfType<JsonObject>().Any(t =>
			string.Equals(t["TemplateID"]?.ToString(), templateId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.GenerateSlug(t["TemplateID"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(t["ModelPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.GenerateSlug(t["ModelPath"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase));
	}

	internal static string GetVisualMode(string fileName)
	{
		return fileName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) ? "3D Mesh (.rmesh)" : "GroundPlane";
	}

	internal static void EnsureBuildingTemplate(JsonObject templatesObj, string fileName, string slug)
	{
		string buildingTemplateId = TemplateIDHelper.NormalizeTemplateID("building", slug);
		var buildingsArr = GetOrAddArray(templatesObj, "Buildings");

		if (CheckIfTemplateExists(buildingsArr, buildingTemplateId, fileName, slug) || ModelExistsInOtherCategories(templatesObj, fileName, slug)) return;

		buildingsArr.Add(new JsonObject
		{
			["TemplateID"] = buildingTemplateId,
			["Name"] = slug,
			["Description"] = "",
			["ModelPath"] = fileName,
			["VisualMode"] = GetVisualMode(fileName),
			["Scale"] = 1.5f,
			["PathingType"] = 32,
			["DespillPlayerColor"] = false,
			["NormalizeLuminance"] = true
		});
	}

	internal static void EnsurePropTemplate(JsonObject templatesObj, string fileName, string slug)
	{
		string propTemplateId = TemplateIDHelper.NormalizeTemplateID("prop", slug);
		var propsArr = GetOrAddArray(templatesObj, "Props");

		if (CheckIfTemplateExists(propsArr, propTemplateId, fileName, slug) || ModelExistsInOtherCategories(templatesObj, fileName, slug)) return;

		propsArr.Add(new JsonObject
		{
			["TemplateID"] = propTemplateId,
			["Name"] = slug,
			["Description"] = "",
			["ModelPath"] = fileName,
			["VisualMode"] = GetVisualMode(fileName),
			["Scale"] = 1.25f,
			["PathingType"] = 255,
			["DespillPlayerColor"] = false,
			["NormalizeLuminance"] = true,
			["IgnorePlayerColor"] = true
		});
	}

	internal static void EnsureResourceTemplate(JsonObject templatesObj, string fileName, string slug)
	{
		string resourceTemplateId = TemplateIDHelper.NormalizeTemplateID("resource", slug);
		var resourcesArr = GetOrAddArray(templatesObj, "Resources");

		if (CheckIfTemplateExists(resourcesArr, resourceTemplateId, fileName, slug) || ModelExistsInOtherCategories(templatesObj, fileName, slug)) return;

		resourcesArr.Add(new JsonObject
		{
			["TemplateID"] = resourceTemplateId,
			["Name"] = slug,
			["Description"] = "",
			["ModelPath"] = fileName,
			["VisualMode"] = GetVisualMode(fileName),
			["Scale"] = 2.75f,
			["PathingType"] = 255,
			["DespillPlayerColor"] = false,
			["NormalizeLuminance"] = true,
			["IgnorePlayerColor"] = true
		});
	}

	internal static void EnsureDecalTemplate(JsonObject metadataRoot, string fileName, string slug, string hash)
	{
		string decalTemplateId = TemplateIDHelper.NormalizeTemplateID("decal", slug);
		var decalsObj = GetOrAddObject(metadataRoot, "decals");

		if (CheckIfDecalExists(decalsObj, decalTemplateId, fileName, slug)) return;

		decalsObj[decalTemplateId] = new JsonObject
		{
			["Hash"] = hash ?? string.Empty,
			["TexturePath"] = fileName,
			["Opacity"] = 1.0f,
			["Brightness"] = 1.0f,
			["Contrast"] = 1.0f,
			["Saturation"] = 1.0f
		};
	}

	internal static JsonObject GetOrAddObject(JsonObject parent, string key)
	{
		if (!parent.ContainsKey(key) || parent[key] is not JsonObject)
		{
			parent[key] = new JsonObject();
		}

		return (JsonObject)parent[key]!;
	}

	internal static bool CheckIfDecalExists(JsonObject decalsObj, string templateId, string fileName, string slug)
	{
		foreach (var kvp in decalsObj)
		{
			if (IsDecalMatch(kvp.Key, kvp.Value as JsonObject, templateId, fileName, slug))
			{
				return true;
			}
		}

		return false;
	}

	internal static bool IsDecalMatch(string key, JsonObject? dObj, string templateId, string fileName, string slug)
	{
		if (string.Equals(key, templateId, StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(key, slug, StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(key, fileName, StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(TemplateIDHelper.GenerateSlug(key), slug, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (dObj == null) return false;

		string? texturePath = GetStringValue(dObj, "TexturePath");
		if (texturePath == null) return false;

		return string.Equals(texturePath, fileName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(texturePath, slug, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(TemplateIDHelper.GenerateSlug(texturePath), slug, StringComparison.OrdinalIgnoreCase);
	}

	internal static void EnsureTerrainTemplate(JsonObject metadataRoot, string fileName, string slug, string hash)
	{
		string terrainTemplateId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);
		if (!metadataRoot.ContainsKey("textures") || metadataRoot["textures"] is not JsonObject)
		{
			metadataRoot["textures"] = new JsonObject();
		}

		var texturesObj = metadataRoot["textures"]!.AsObject();
		bool exists = texturesObj.Any(kvp =>
			string.Equals(kvp.Key, terrainTemplateId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(kvp.Key, slug, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(kvp.Key, fileName, StringComparison.OrdinalIgnoreCase) ||
			(kvp.Value is JsonObject tObj && string.Equals(tObj["AssetType"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase)));
		if (!exists)
		{
			texturesObj[terrainTemplateId] = new JsonObject
			{
				["Hash"] = hash ?? string.Empty,
				["AssetType"] = fileName,
				["ScaleFactor"] = 1.0f,
				["Brightness"] = 1.0f,
				["Contrast"] = 1.0f,
				["Saturation"] = 1.0f,
				["TileMode"] = "Stochastic",
				["DefaultPathingCode"] = 8 | 32 | 4,
				["DecalBombingRules"] = new JsonArray(),
				["VfxBombingRules"] = new JsonArray()
			};
		}
	}

	internal static void EnsureSpritesheetTemplate(JsonObject metadataRoot, string fileName, string slug, string hash)
	{
		string spritesheetTemplateId = TemplateIDHelper.NormalizeTemplateID("spritesheet", slug);
		if (!metadataRoot.ContainsKey("vfx_spritesheets") || metadataRoot["vfx_spritesheets"] is not JsonObject)
		{
			metadataRoot["vfx_spritesheets"] = new JsonObject();
		}

		var vfxObj = metadataRoot["vfx_spritesheets"]!.AsObject();
		bool exists = vfxObj.Any(kvp =>
			string.Equals(kvp.Key, spritesheetTemplateId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(kvp.Key, slug, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(kvp.Key, fileName, StringComparison.OrdinalIgnoreCase) ||
			(kvp.Value is JsonObject sObj && string.Equals(sObj["AssetType"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase)));
		if (!exists)
		{
			vfxObj[spritesheetTemplateId] = new JsonObject
			{
				["Hash"] = hash ?? string.Empty,
				["AssetType"] = fileName,
				["Columns"] = 4,
				["Rows"] = 4,
				["Fps"] = 20.0f
			};
		}
	}

	internal static void EnsureIconTemplate(JsonObject metadataRoot, string slug, string hash)
	{
		string iconTemplateId = TemplateIDHelper.NormalizeTemplateID("icon", slug);
		if (!metadataRoot.ContainsKey("icons") || metadataRoot["icons"] is not JsonObject)
		{
			metadataRoot["icons"] = new JsonObject();
		}

		var iconsObj = metadataRoot["icons"]!.AsObject();
		bool exists = iconsObj.Any(kvp => string.Equals(kvp.Key, iconTemplateId, StringComparison.OrdinalIgnoreCase));
		if (!exists)
		{
			iconsObj[iconTemplateId] = new JsonObject
			{
				["Hash"] = hash ?? string.Empty
			};
		}
	}

	internal static void EnsureShaderTemplate(JsonObject metadataRoot, string slug, string hash)
	{
		string shaderTemplateId = TemplateIDHelper.NormalizeTemplateID("gdshader", slug);
		if (!metadataRoot.ContainsKey("gdshader") || metadataRoot["gdshader"] is not JsonObject)
		{
			metadataRoot["gdshader"] = new JsonObject();
		}

		var shaderObj = metadataRoot["gdshader"]!.AsObject();
		bool exists = shaderObj.Any(kvp => string.Equals(kvp.Key, shaderTemplateId, StringComparison.OrdinalIgnoreCase));
		if (!exists)
		{
			shaderObj[shaderTemplateId] = new JsonObject
			{
				["Hash"] = hash ?? string.Empty
			};
		}
	}

	internal static void UnionCategoryInto(JsonObject targetContainer, string targetCategory, JsonObject sourceObject)
	{
		if (!targetContainer.ContainsKey(targetCategory) || targetContainer[targetCategory] is not JsonObject)
		{
			targetContainer[targetCategory] = new JsonObject();
		}

		var categoryTarget = targetContainer[targetCategory]!.AsObject();

		foreach (var pair in sourceObject)
		{
			if (categoryTarget.ContainsKey(pair.Key) && categoryTarget[pair.Key] is JsonObject existingObj && pair.Value is JsonObject sourceObj)
			{
				foreach (var prop in sourceObj)
				{
					existingObj[prop.Key] = prop.Value?.DeepClone();
				}
			}
			else if (pair.Value != null)
			{
				categoryTarget[pair.Key] = pair.Value.DeepClone();
			}
		}
	}

	internal static string? GetStringValue(JsonObject obj, string key)
	{
		return obj.TryGetPropertyValue(key, out var node) ? node?.ToString() : null;
	}
}
