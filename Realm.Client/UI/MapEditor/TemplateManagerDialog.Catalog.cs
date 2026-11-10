using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.VFX;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;

namespace Realm.Client.UI.MapEditor;

public partial class TemplateManagerDialog
{
	private static readonly Dictionary<string, Func<TemplateManagerDialog, MapMetadata, List<ObjectItemInfo>>> CategoryHandlers = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "units", (dialog, meta) => dialog.GetUnitsInfo(meta) },
		{ "buildings", (dialog, meta) => dialog.GetBuildingsInfo(meta) },
		{ "resources", (dialog, meta) => dialog.GetResourcesInfo(meta) },
		{ "props", (dialog, meta) => dialog.GetPropsInfo(meta) },
		{ "weapons", (dialog, meta) => dialog.GetWeaponsInfo(meta) },
		{ "abilities", (dialog, meta) => dialog.GetAbilitiesInfo(meta) },
		{ "upgrades", (dialog, meta) => dialog.GetUpgradesInfo(meta) },
		{ "items", (dialog, meta) => dialog.GetItemsInfo(meta) },
		{ "terrain", (dialog, meta) => dialog.GetTerrainInfo(meta) },
		{ "spritesheets", (dialog, meta) => dialog.GetSpritesheetsInfo(meta) },
		{ "decals", (dialog, meta) => dialog.GetDecalsInfo(meta) },
		{ "shaders", (dialog, meta) => dialog.GetShadersInfo(meta) },
		{ "shader", (dialog, meta) => dialog.GetShadersInfo(meta) },
		{ "spawnshader", (dialog, meta) => dialog.GetShadersInfo(meta) },
		{ "spawnshaders", (dialog, meta) => dialog.GetShadersInfo(meta) },
		{ "vfx", (dialog, meta) => dialog.GetVfxInfo(meta) }
	};

	private List<ObjectItemInfo> GetObjectsForCategory(string category)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) || meta == null)
		{
			return new List<ObjectItemInfo>();
		}

		return CategoryHandlers.TryGetValue(category, out var handler)
			? handler(this, meta)
			: new List<ObjectItemInfo>();
	}

	private List<ObjectItemInfo> GetUnitsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Units == null) return list;
		foreach (var u in meta.Templates.Units)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "units",
				TemplateID = u.TemplateID,
				Name = u.Name ?? u.TemplateID,
				Description = u.Description ?? "",
				ModelPath = u.ModelPath ?? "",
				VisualMode = u.VisualMode ?? "GroundPlane",
				Scale = u.Scale > 0 ? u.Scale : 1.0f
			});
		}

		return list;
	}

	private List<ObjectItemInfo> GetBuildingsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Buildings == null) return list;
		foreach (var b in meta.Templates.Buildings)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "buildings",
				TemplateID = b.TemplateID,
				Name = b.Name ?? b.TemplateID,
				Description = b.Description ?? "",
				ModelPath = b.ModelPath ?? "",
				VisualMode = b.VisualMode ?? "GroundPlane",
				Scale = b.Scale > 0 ? b.Scale : 1.0f
			});
		}

		return list;
	}

	private List<ObjectItemInfo> GetResourcesInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Resources == null) return list;
		foreach (var r in meta.Templates.Resources)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "resources",
				TemplateID = r.TemplateID,
				Name = r.Name ?? r.TemplateID,
				Description = r.Description ?? "",
				ModelPath = r.ModelPath ?? "",
				VisualMode = r.VisualMode ?? "GroundPlane",
				Scale = r.Scale > 0 ? r.Scale : 1.0f
			});
		}

		return list;
	}

	private List<ObjectItemInfo> GetPropsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Props == null) return list;
		foreach (var p in meta.Templates.Props)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "props",
				TemplateID = p.TemplateID,
				Name = p.Name ?? p.TemplateID,
				Description = p.Description ?? "",
				ModelPath = p.ModelPath ?? "",
				VisualMode = p.VisualMode ?? "GroundPlane",
				Scale = p.Scale > 0 ? p.Scale : 1.0f
			});
		}

		return list;
	}

	private List<ObjectItemInfo> GetWeaponsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Weapons == null) return list;
		foreach (var w in meta.Templates.Weapons)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "weapons",
				TemplateID = w.TemplateID,
				Name = w.Name ?? w.TemplateID,
				Description = $"Damage: {w.Damage} | Range: {w.Range} | Type: {w.AttackType}",
				ModelPath = w.ProjectileModelPath ?? ""
			});
		}

		return list;
	}

	private List<ObjectItemInfo> GetAbilitiesInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Abilities == null) return list;
		foreach (var a in meta.Templates.Abilities)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "abilities",
				TemplateID = a.TemplateID,
				Name = a.Name ?? a.TemplateID,
				Description = a.Description ?? "",
				IconPath = a.IconPath ?? ""
			});
		}

		return list;
	}

	private List<ObjectItemInfo> GetUpgradesInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Upgrades == null) return list;
		foreach (var u in meta.Templates.Upgrades)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "upgrades",
				TemplateID = u.TemplateID,
				Name = u.Name ?? u.TemplateID,
				Description = u.Description ?? "",
				IconPath = u.IconPath ?? ""
			});
		}

		return list;
	}

	private List<ObjectItemInfo> GetItemsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Items == null) return list;
		foreach (var itm in meta.Templates.Items)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "items",
				TemplateID = itm.TemplateID,
				Name = itm.Name ?? itm.TemplateID,
				Description = itm.Description ?? "",
				IconPath = itm.IconPath ?? ""
			});
		}

		return list;
	}

	private List<ObjectItemInfo> GetTerrainInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Textures == null) return list;
		foreach (var kvp in meta.Textures)
		{
			string normalizedId = TemplateIDHelper.NormalizeTemplateID("terrain", kvp.Key);
			var (_, slug) = TemplateIDHelper.ParseTemplateID(normalizedId);
			string rtex = !string.IsNullOrWhiteSpace(kvp.Value?.TexturePath) ? kvp.Value.TexturePath : (!string.IsNullOrWhiteSpace(slug) ? $"{slug}.rtex" : kvp.Key);
			list.Add(new ObjectItemInfo
			{
				Category = "terrain",
				TemplateID = normalizedId,
				Name = rtex,
				Description = "Terrain texture swatch config",
				ModelPath = rtex
			});
		}

		return list;
	}

	private List<ObjectItemInfo> GetSpritesheetsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.VfxSpritesheets == null) return list;
		foreach (var kvp in meta.VfxSpritesheets)
		{
			list.Add(CreateSpritesheetInfo(kvp.Key, kvp.Value));
		}

		return list;
	}

	private ObjectItemInfo CreateSpritesheetInfo(string key, VfxMetadata sheetObj)
	{
		string normalizedId = TemplateIDHelper.NormalizeTemplateID("spritesheet", key);
		var (_, slug) = TemplateIDHelper.ParseTemplateID(normalizedId);
		int cols = sheetObj?.Columns ?? 1;
		int rows = sheetObj?.Rows ?? 1;
		float fps = sheetObj?.Fps ?? 20.0f;
		string rtex = !string.IsNullOrEmpty(sheetObj?.TexturePath) ? sheetObj.TexturePath : (!string.IsNullOrWhiteSpace(slug) ? $"{slug}.rtex" : key);
		return new ObjectItemInfo
		{
			Category = "spritesheets",
			TemplateID = normalizedId,
			Name = rtex,
			Description = $"{cols}x{rows} @ {fps} FPS",
			ModelPath = rtex
		};
	}

	private List<ObjectItemInfo> GetDecalsInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Decals == null) return list;
		foreach (var kvp in meta.Decals)
		{
			string normalizedId = TemplateIDHelper.NormalizeTemplateID("decal", kvp.Key);
			var (_, slug) = TemplateIDHelper.ParseTemplateID(normalizedId);
			string rtex = !string.IsNullOrWhiteSpace(kvp.Value?.TexturePath) ? kvp.Value.TexturePath : (!string.IsNullOrWhiteSpace(slug) ? $"{slug}.rtex" : kvp.Key);
			list.Add(new ObjectItemInfo
			{
				Category = "decals",
				TemplateID = normalizedId,
				Name = rtex,
				Description = "Decal configuration",
				ModelPath = rtex
			});
		}

		return list;
	}

	private List<ObjectItemInfo> GetShadersInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.SpawnShaders == null) return list;
		foreach (var kvp in meta.SpawnShaders)
		{
			string normalizedId = TemplateIDHelper.NormalizeTemplateID("SpawnShader", kvp.Key);
			var (_, slug) = TemplateIDHelper.ParseTemplateID(normalizedId);
			list.Add(new ObjectItemInfo
			{
				Category = "shaders",
				TemplateID = normalizedId,
				Name = slug,
				Description = "Custom visual shader config"
			});
		}

		return list;
	}

	private List<ObjectItemInfo> GetVfxInfo(MapMetadata meta)
	{
		var list = new List<ObjectItemInfo>();
		if (meta.Templates?.Vfx == null) return list;
		foreach (var v in meta.Templates.Vfx)
		{
			list.Add(new ObjectItemInfo
			{
				Category = "vfx",
				TemplateID = v.VfxId,
				Name = v.Name ?? v.VfxId,
				Description = $"{v.PrimitiveType} | {v.BlendMode}",
				ModelPath = v.BaseTexture
			});
		}

		return list;
	}
}
