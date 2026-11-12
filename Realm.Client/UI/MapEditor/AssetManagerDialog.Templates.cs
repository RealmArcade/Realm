// Template scaffolding for an imported asset: one Create*Template per asset kind, plus the terrain and
// texture upserts that are not templates. Every entry point is static and takes the workspace path, so
// the file has no dialog state in it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.EditorAPI;
using Realm.Shared.Audio;
using Realm.Shared.Textures;
using Realm.Client.Utils;


namespace Realm.Client.UI.MapEditor;

public partial class AssetManagerDialog
{
	public static void EnsureTemplateForAsset(string wsPath, string category, string fileName, string hash = "")
	{
		if (string.IsNullOrWhiteSpace(wsPath) || string.IsNullOrWhiteSpace(fileName)) return;

		try
		{
			string slug = TemplateIDHelper.GenerateSlug(fileName);
			string normalizedCategory = MapAssetHelper.NormalizeCategoryKey(category);

			GameContext.MetadataService.UpdateMetadata(wsPath, meta => { CreateTemplateMetadataForCategory(meta, normalizedCategory, slug, fileName); });

			UpdateTerrainAndTexturesUI(normalizedCategory);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetManagerDialog] EnsureTemplateForAsset error: {ex.Message}");
		}
	}

	private static void UpdateTerrainAndTexturesUI(string normalizedCategory)
	{
		if (normalizedCategory.Equals("terrain", StringComparison.OrdinalIgnoreCase) || normalizedCategory.Equals("textures", StringComparison.OrdinalIgnoreCase))
		{
			GameContext.MapEditorHud?.SetupTextureSwatches(false);
			if (GameContext.Host?.GroundTerrain != null)
			{
				GameContext.Host.GroundTerrain.ClearLiveSwatchOverrides();
				GameContext.Host.GroundTerrain.ReloadTerrainTextures(true);
			}
		}
	}

	private static void CreateTemplateMetadataForCategory(MapMetadata meta, string normalizedCategory, string slug, string fileName)
	{
		switch (normalizedCategory.ToLowerInvariant())
		{
			case "character" or "unit" or "characters" or "units":
				CreateUnitTemplate(meta, slug, fileName, 1.0f, 9);
				break;
			case "building" or "buildings":
				CreateUnitTemplate(meta, slug, fileName, 1.5f, 32);
				break;
			case "prop" or "props":
				CreatePropTemplate(meta, slug, fileName);
				break;
			case "resource" or "resources":
				CreateResourceTemplate(meta, slug, fileName);
				break;
			case "item" or "items":
				CreateItemTemplate(meta, slug, fileName);
				break;
			case "decal" or "decals":
				CreateDecalTemplate(meta, slug, fileName);
				break;
			case "terrain" or "textures":
				CreateTerrainTemplate(meta, slug, fileName);
				break;
			case "spritesheet" or "spritesheets" or "vfx":
				CreateVfxSpritesheetTemplate(meta, slug, fileName);
				break;
			case "icon" or "icons":
				CreateIconTemplate(meta, slug, fileName);
				break;
			case "ribbon" or "ribbons":
				CreateRibbonTemplate(meta, slug, fileName);
				break;
			case "noise":
				CreateNoiseTemplate(meta, fileName);
				break;
			case "skybox" or "skyboxes":
				CreateSkyboxTemplate(meta, slug, fileName);
				break;
			case "shader" or "shaders" or "gdshader":
				CreateShaderTemplate(meta, slug);
				break;
			case "spawnshader" or "spawnshaders":
				CreateSpawnShaderTemplate(meta, slug);
				break;
		}
	}

	private static void CreateUnitTemplate(MapMetadata meta, string slug, string fileName, float scale, int pathingType)
	{
		string templateId = TemplateIDHelper.NormalizeTemplateID("unit", slug);
		meta.Templates ??= new();
		meta.Templates.Units ??= new();
		if (!meta.Templates.Units.Any(u => string.Equals(u.TemplateID, templateId, StringComparison.OrdinalIgnoreCase)))
		{
			meta.Templates.Units.Add(new UnitMetadata
			{
				TemplateID = templateId,
				Name = slug,
				Description = "",
				ModelPath = fileName,
				Scale = scale,
				PathingType = pathingType,
				DespillPlayerColor = false,
				NormalizeLuminance = true
			});
		}
	}

	private static void CreatePropTemplate(MapMetadata meta, string slug, string fileName)
	{
		string templateId = TemplateIDHelper.NormalizeTemplateID("prop", slug);
		meta.Templates ??= new();
		meta.Templates.Props ??= new();
		if (!meta.Templates.Props.Any(p => string.Equals(p.TemplateID, templateId, StringComparison.OrdinalIgnoreCase)))
		{
			meta.Templates.Props.Add(new PropMetadata
			{
				TemplateID = templateId,
				Name = slug,
				Description = "",
				ModelPath = fileName,
				Scale = 1.25f,
				PathingType = 255,
				DespillPlayerColor = false,
				NormalizeLuminance = true,
				IgnorePlayerColor = true
			});
		}
	}

	private static void CreateResourceTemplate(MapMetadata meta, string slug, string fileName)
	{
		string templateId = TemplateIDHelper.NormalizeTemplateID("resource", slug);
		meta.Templates ??= new();
		meta.Templates.Resources ??= new();
		if (!meta.Templates.Resources.Any(r => string.Equals(r.TemplateID, templateId, StringComparison.OrdinalIgnoreCase)))
		{
			meta.Templates.Resources.Add(new ResourceMetadata
			{
				TemplateID = templateId,
				Name = slug,
				Description = "",
				ModelPath = fileName,
				Scale = 2.75f,
				PathingType = 255,
				DespillPlayerColor = false,
				NormalizeLuminance = true,
				IgnorePlayerColor = true
			});
		}
	}

	private static void CreateItemTemplate(MapMetadata meta, string slug, string fileName)
	{
		if (!fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)) return;

		string templateId = TemplateIDHelper.NormalizeTemplateID("item", slug);
		meta.Templates ??= new();
		meta.Templates.Items ??= new();
		if (!meta.Templates.Items.Any(i => string.Equals(i.TemplateID, templateId, StringComparison.OrdinalIgnoreCase)))
		{
			meta.Templates.Items.Add(new ItemMetadata
			{
				TemplateID = templateId,
				Name = slug,
				Description = "",
				ItemClass = "consumable",
				IconPath = fileName,
				CanDrop = true
			});
		}
	}

	private static void CreateDecalTemplate(MapMetadata meta, string slug, string fileName)
	{
		string templateId = TemplateIDHelper.NormalizeTemplateID("decal", slug);
		meta.Decals ??= new(StringComparer.OrdinalIgnoreCase);
		if (!meta.Decals.ContainsKey(templateId))
		{
			meta.Decals[templateId] = new DecalMetadata
			{
				TexturePath = fileName,
				Opacity = 1.0f,
				Brightness = 1.0f,
				Contrast = 1.0f,
				Saturation = 1.0f
			};
		}
	}

	private static void CreateTerrainTemplate(MapMetadata meta, string slug, string fileName)
	{
		string templateId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);
		meta.Textures ??= new(StringComparer.OrdinalIgnoreCase);
		if (!meta.Textures.ContainsKey(templateId))
		{
			int nextFreeSlot = FindFirstFreeTextureSlot(meta.Textures);

			meta.Textures[templateId] = new TextureMetadata
			{
				TexturePath = fileName,
				SwatchIndex = nextFreeSlot,
				ScaleFactor = 1.0f,
				Brightness = 1.0f,
				Contrast = 1.0f,
				Saturation = 1.0f,
				TileMode = "Stochastic",
				DefaultPathingCode = 8 | 32 | 4,
				DecalBombingRules = new(),
				VfxBombingRules = new()
			};
		}
	}

	private static int FindFirstFreeTextureSlot(Dictionary<string, TextureMetadata> textures)
	{
		var occupiedSlots = new bool[TextureSwatchSlots.MaxSlots];
		foreach (var existingTex in textures.Values)
		{
			if (existingTex != null && existingTex.SwatchIndex >= 0 && existingTex.SwatchIndex < TextureSwatchSlots.MaxSlots)
			{
				occupiedSlots[existingTex.SwatchIndex] = true;
			}
		}

		int nextFreeSlot = TextureSwatchSlots.FirstFreeSlot(occupiedSlots);
		return nextFreeSlot < 0 ? 0 : nextFreeSlot;
	}

	private static void CreateVfxSpritesheetTemplate(MapMetadata meta, string slug, string fileName)
	{
		string templateId = TemplateIDHelper.NormalizeTemplateID("spritesheet", slug);
		meta.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
		if (!meta.VfxSpritesheets.ContainsKey(templateId))
		{
			meta.VfxSpritesheets[templateId] = new VfxMetadata
			{
				TexturePath = fileName,
				Columns = 4,
				Rows = 4,
				Fps = 20.0f
			};
		}
	}

	private static void CreateIconTemplate(MapMetadata meta, string slug, string fileName)
	{
		string templateId = TemplateIDHelper.NormalizeTemplateID("icon", slug);
		meta.Icons ??= new(StringComparer.OrdinalIgnoreCase);
		if (!meta.Icons.ContainsKey(templateId))
		{
			meta.Icons[templateId] = new IconMetadata
			{
				TexturePath = fileName
			};
		}
	}

	private static void CreateRibbonTemplate(MapMetadata meta, string slug, string fileName)
	{
		string templateId = TemplateIDHelper.NormalizeTemplateID("ribbon", slug);
		meta.Ribbons ??= new(StringComparer.OrdinalIgnoreCase);
		if (!meta.Ribbons.ContainsKey(templateId))
		{
			meta.Ribbons[templateId] = new RibbonMetadata
			{
				TexturePath = fileName
			};
		}
	}

	private static void CreateNoiseTemplate(MapMetadata meta, string fileName)
	{
		meta.NoiseTextures ??= new(StringComparer.OrdinalIgnoreCase);
		if (!meta.NoiseTextures.ContainsKey(fileName))
		{
			meta.NoiseTextures[fileName] = new TextureMetadata
			{
				TexturePath = fileName,
				ScaleFactor = 1.0f
			};
		}
	}

	private static void CreateSkyboxTemplate(MapMetadata meta, string slug, string fileName)
	{
		string templateId = TemplateIDHelper.NormalizeTemplateID("skybox", slug);
		meta.Skyboxes ??= new(StringComparer.OrdinalIgnoreCase);
		if (!meta.Skyboxes.ContainsKey(templateId))
		{
			meta.Skyboxes[templateId] = new SkyboxMetadata
			{
				TexturePath = fileName
			};
		}
	}

	private static void CreateShaderTemplate(MapMetadata meta, string slug)
	{
		string templateId = TemplateIDHelper.NormalizeTemplateID("gdshader", slug);
		meta.GdShaders ??= new(StringComparer.OrdinalIgnoreCase);
		if (!meta.GdShaders.ContainsKey(templateId))
		{
			meta.GdShaders[templateId] = new ShaderMetadata();
		}
	}

	private static void CreateSpawnShaderTemplate(MapMetadata meta, string slug)
	{
		string templateId = TemplateIDHelper.NormalizeTemplateID("SpawnShader", slug);
		meta.SpawnShaders ??= new(StringComparer.OrdinalIgnoreCase);
		if (!meta.SpawnShaders.ContainsKey(templateId))
		{
			meta.SpawnShaders[templateId] = new SpawnShaderMetadata
			{
				Name = slug
			};
		}
	}
}
