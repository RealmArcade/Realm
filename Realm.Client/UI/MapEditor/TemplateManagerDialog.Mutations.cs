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
	private static readonly Dictionary<string, Action<TemplateManagerDialog, MapMetadata, ObjectItemInfo, string, string>> DuplicateMetadataHandlers = new(StringComparer.OrdinalIgnoreCase)
	{
		{
			"units", (dialog, m, item, newId, newSlug) =>
			{
				var orig = m.GetUnit(item.TemplateID) ?? m.GetUnit(item.Name);
				if (orig == null) return;
				var copy = JsonSerializer.Deserialize<UnitMetadata>(JsonSerializer.Serialize(orig))!;
				copy.TemplateID = newId;
				copy.Name = newSlug;
				m.AddOrUpdateUnit(copy);
			}
		},
		{
			"buildings", (dialog, m, item, newId, newSlug) =>
			{
				var orig = m.GetBuilding(item.TemplateID) ?? m.GetBuilding(item.Name);
				if (orig == null) return;
				var copy = JsonSerializer.Deserialize<UnitMetadata>(JsonSerializer.Serialize(orig))!;
				copy.TemplateID = newId;
				copy.Name = newSlug;
				m.AddOrUpdateBuilding(copy);
			}
		},
		{
			"resources", (dialog, m, item, newId, newSlug) =>
			{
				var orig = m.GetResource(item.TemplateID) ?? m.GetResource(item.Name);
				if (orig == null) return;
				var copy = JsonSerializer.Deserialize<ResourceMetadata>(JsonSerializer.Serialize(orig))!;
				copy.TemplateID = newId;
				copy.Name = newSlug;
				m.AddOrUpdateResource(copy);
			}
		},
		{
			"props", (dialog, m, item, newId, newSlug) =>
			{
				var orig = m.GetProp(item.TemplateID) ?? m.GetProp(item.Name);
				if (orig == null) return;
				var copy = JsonSerializer.Deserialize<PropMetadata>(JsonSerializer.Serialize(orig))!;
				copy.TemplateID = newId;
				copy.Name = newSlug;
				m.AddOrUpdateProp(copy);
			}
		},
		{
			"weapons", (dialog, m, item, newId, newSlug) =>
			{
				var orig = m.GetWeapon(item.TemplateID) ?? m.GetWeapon(item.Name);
				if (orig == null) return;
				var copy = JsonSerializer.Deserialize<WeaponMetadata>(JsonSerializer.Serialize(orig))!;
				copy.TemplateID = newId;
				copy.Name = newSlug;
				m.AddOrUpdateWeapon(copy);
			}
		},
		{
			"abilities", (dialog, m, item, newId, newSlug) =>
			{
				var orig = m.GetAbility(item.TemplateID) ?? m.GetAbility(item.Name);
				if (orig == null) return;
				var copy = JsonSerializer.Deserialize<AbilityMetadata>(JsonSerializer.Serialize(orig))!;
				copy.TemplateID = newId;
				copy.Name = newSlug;
				m.AddOrUpdateAbility(copy);
			}
		},
		{
			"upgrades", (dialog, m, item, newId, newSlug) =>
			{
				var orig = m.GetUpgrade(item.TemplateID) ?? m.GetUpgrade(item.Name);
				if (orig == null) return;
				var copy = JsonSerializer.Deserialize<UpgradeMetadata>(JsonSerializer.Serialize(orig))!;
				copy.TemplateID = newId;
				copy.Name = newSlug;
				m.AddOrUpdateUpgrade(copy);
			}
		},
		{
			"items", (dialog, m, item, newId, newSlug) =>
			{
				var orig = m.GetItem(item.TemplateID) ?? m.GetItem(item.Name);
				if (orig == null) return;
				var copy = JsonSerializer.Deserialize<ItemMetadata>(JsonSerializer.Serialize(orig))!;
				copy.TemplateID = newId;
				copy.Name = newSlug;
				m.AddOrUpdateItem(copy);
			}
		},
		{
			"terrain", (dialog, m, item, newId, newSlug) =>
			{
				m.Textures ??= new(StringComparer.OrdinalIgnoreCase);
				var orig = dialog.FindTextureMetadata(m, item);
				TextureMetadata copy = orig != null
					? JsonSerializer.Deserialize<TextureMetadata>(JsonSerializer.Serialize(orig))!
					: new TextureMetadata();
				copy.SwatchIndex = dialog.GetNextTerrainSwatchSlot(m);
				m.Textures[newId] = copy;
			}
		},
		{
			"spritesheets", (dialog, m, item, newId, newSlug) =>
			{
				m.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
				var orig = dialog.FindSpritesheetMetadata(m, item);
				VfxMetadata copy = orig != null
					? JsonSerializer.Deserialize<VfxMetadata>(JsonSerializer.Serialize(orig))!
					: new VfxMetadata();
				m.VfxSpritesheets[newId] = copy;
			}
		},
		{
			"decals", (dialog, m, item, newId, newSlug) =>
			{
				m.Decals ??= new(StringComparer.OrdinalIgnoreCase);
				m.Decals.TryGetValue(item.TemplateID, out var orig);
				if (orig == null)
				{
					var (_, oldSlug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
					if (!string.IsNullOrEmpty(oldSlug)) m.Decals.TryGetValue(oldSlug, out orig);
				}
				DecalMetadata copy = orig != null
					? JsonSerializer.Deserialize<DecalMetadata>(JsonSerializer.Serialize(orig))!
					: new DecalMetadata();
				m.Decals[newId] = copy;
			}
		},
		{
				"shaders", SpawnShaderDuplicateHandler
		},
		{
				"shader", SpawnShaderDuplicateHandler
		},
		{
				"spawnshader", SpawnShaderDuplicateHandler
		},
		{
				"spawnshaders", SpawnShaderDuplicateHandler
		},
		{
			"vfx", (dialog, m, item, newId, newSlug) =>
			{
				var orig = m.GetVfx(item.TemplateID) ?? m.GetVfx(item.Name);
				if (orig == null) return;
				var copy = JsonSerializer.Deserialize<VfxAttachmentConfig>(JsonSerializer.Serialize(orig))!;
				copy.VfxId = newId;
				copy.Name = newSlug;
				m.AddOrUpdateVfx(copy);
			}
		}
	};

	private void DuplicateObject(ObjectItemInfo item)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string objectType = GetObjectTypeFromCategory(item.Category);
		var existingIDs = GetExistingObjectIDs(wsPath, item.Category);

		var (_, sourceSlug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
		if (string.IsNullOrEmpty(sourceSlug))
		{
			sourceSlug = item.TemplateID;
		}

		string newTemplateID = TemplateIDHelper.GenerateDuplicateTemplateID(objectType, sourceSlug, existingIDs);
		var (_, parsedSlug) = TemplateIDHelper.ParseTemplateID(newTemplateID);

		GameContext.MetadataService.UpdateMetadata(wsPath, m =>
		{
			if (DuplicateMetadataHandlers.TryGetValue(item.Category, out var handler))
			{
				handler(this, m, item, newTemplateID, parsedSlug);
			}
		});

		if (item.Category == "terrain")
		{
			Hud?.SetupTextureSwatches(false);
			if (GameContext.Host?.GroundTerrain != null)
			{
				GameContext.Host.GroundTerrain.ReloadTerrainTextures(true);
			}
		}

		_currentPreviewTemplateID = newTemplateID;
		RefreshObjectList();
	}

	private void AddNewObjectForCategory(string category)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string objectType = GetObjectTypeFromCategory(category);
		var existingIDs = GetExistingObjectIDs(wsPath, category);

		string newTemplateID = TemplateIDHelper.GenerateTemplateID(objectType, $"new_{objectType}", existingIDs);
		var (parsedType, parsedSlug) = TemplateIDHelper.ParseTemplateID(newTemplateID);

		if (category == "vfx")
		{
			_pendingNewVfxTemplateId = newTemplateID;
			_pendingNewVfxWasApplied = false;
		}

		GameContext.MetadataService.UpdateMetadata(wsPath, m => { AddMetadataForCategory(m, category, newTemplateID, parsedSlug); });

		HandlePostAddObject(category, newTemplateID, parsedSlug);
	}

	private static readonly Dictionary<string, string> ObjectTypeMap = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "units", "unit" },
		{ "buildings", "building" },
		{ "resources", "resource" },
		{ "props", "prop" },
		{ "weapons", "weapon" },
		{ "abilities", "ability" },
		{ "upgrades", "upgrade" },
		{ "items", "item" },
		{ "terrain", "terrain" },
		{ "spritesheets", "spritesheet" },
		{ "decals", "decal" },
		{ "shaders", "SpawnShader" },
		{ "shader", "SpawnShader" },
		{ "spawnshader", "SpawnShader" },
		{ "spawnshaders", "SpawnShader" },
		{ "vfx", "vfx" }
	};

	private string GetObjectTypeFromCategory(string category)
	{
		return ObjectTypeMap.TryGetValue(category, out var type) ? type : "unit";
	}

	private HashSet<string> GetExistingObjectIDs(string wsPath, string category)
	{
		var existingIDs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) && meta != null)
		{
			var currentList = GetObjectsForCategory(category);
			foreach (var obj in currentList) existingIDs.Add(obj.TemplateID);
		}

		return existingIDs;
	}

	private static readonly Dictionary<string, Action<TemplateManagerDialog, MapMetadata, string, string>> AddMetadataHandlers = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "units", (dialog, m, id, slug) => dialog.AddUnitMetadata(m, id, slug) },
		{ "buildings", (dialog, m, id, slug) => dialog.AddBuildingMetadata(m, id, slug) },
		{ "resources", (dialog, m, id, slug) => dialog.AddResourceMetadata(m, id, slug) },
		{ "props", (dialog, m, id, slug) => dialog.AddPropMetadata(m, id, slug) },
		{ "weapons", (dialog, m, id, slug) => dialog.AddWeaponMetadata(m, id, slug) },
		{ "abilities", (dialog, m, id, slug) => dialog.AddAbilityMetadata(m, id, slug) },
		{ "upgrades", (dialog, m, id, slug) => dialog.AddUpgradeMetadata(m, id, slug) },
		{ "items", (dialog, m, id, slug) => dialog.AddItemMetadata(m, id, slug) },
		{ "terrain", (dialog, m, id, slug) => dialog.AddTerrainMetadata(m, id, slug) },
		{ "spritesheets", (dialog, m, id, slug) => dialog.AddSpritesheetMetadata(m, id, slug) },
		{ "decals", (dialog, m, id, slug) => dialog.AddDecalMetadata(m, id, slug) },
		{ "shaders", (dialog, m, id, slug) => dialog.AddShaderMetadata(m, id, slug) },
		{ "shader", (dialog, m, id, slug) => dialog.AddShaderMetadata(m, id, slug) },
		{ "spawnshader", (dialog, m, id, slug) => dialog.AddShaderMetadata(m, id, slug) },
		{ "spawnshaders", (dialog, m, id, slug) => dialog.AddShaderMetadata(m, id, slug) },
		{ "vfx", (dialog, m, id, slug) => dialog.AddVfxMetadata(m, id, slug) }
	};

	private void AddMetadataForCategory(MapMetadata m, string category, string newTemplateID, string parsedSlug)
	{
		if (AddMetadataHandlers.TryGetValue(category, out var handler))
		{
			handler(this, m, newTemplateID, parsedSlug);
		}
	}

	private void AddUnitMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateUnit(new UnitMetadata { TemplateID = id, Name = slug, Description = "A new unit entity.", Scale = 1.0f, MaxHp = 100, Damage = 10, Range = 2.0f, Armor = 0, Speed = 5.0f, AttackCooldown = 1.5f, CostGold = 100, ProductionTime = 10.0f, PopCost = 1, DamageType = "physical", PathingType = 8 });
	private void AddBuildingMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateBuilding(new UnitMetadata { TemplateID = id, Name = slug, Description = "A new building entity.", Scale = 1.5f, MaxHp = 1000, Armor = 10, CostGold = 200, CostWood = 100, ProductionTime = 15.0f, DamageType = "physical", PathingType = 32 });
	private void AddResourceMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateResource(new ResourceMetadata { TemplateID = id, Name = slug, Description = "Harvestable resource deposit.", Scale = 1.0f, MaxCapacity = 2000.0f, HarvestRate = 10.0f, MaxWorkers = 5, PathingType = 255 });
	private void AddPropMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateProp(new PropMetadata { TemplateID = id, Name = slug, Description = "Decorative prop.", Scale = 1.0f, PathingType = 255 });
	private void AddWeaponMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateWeapon(new WeaponMetadata { TemplateID = id, Name = slug, Damage = 10, Range = 8.0f, AttackCooldown = 1.5f, AttackType = "ranged", ProjectileSpeed = 25.0f, ArcHeight = 2.0f, OrientToTrajectory = true });
	private void AddAbilityMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateAbility(new AbilityMetadata { TemplateID = id, Name = slug, Description = "A new ability.", AbilityType = "target_spell", Cooldown = 10, AreaOfEffectRadius = 4.0f });
	private void AddUpgradeMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateUpgrade(new UpgradeMetadata { TemplateID = id, Name = slug, Description = "A new upgrade." });
	private void AddItemMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateItem(new ItemMetadata { TemplateID = id, Name = slug, Description = "A new item.", ItemClass = "consumable" });

	private void AddTerrainMetadata(MapMetadata m, string id, string slug)
	{
		m.Textures ??= new(StringComparer.OrdinalIgnoreCase);
		m.Textures[id] = new TextureMetadata { TileMode = "Stochastic", UvScale = 1.0f, Brightness = 1.0f, TexturePath = $"{slug}.rtex", SwatchIndex = GetNextTerrainSwatchSlot(m), DefaultPathingCode = EditableTerrain.PATHING_GROUND | EditableTerrain.PATHING_BUILDABLE | EditableTerrain.PATHING_FLYING };
	}

	private void AddSpritesheetMetadata(MapMetadata m, string id, string slug)
	{
		m.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
		m.VfxSpritesheets[id] = new VfxMetadata { Columns = 4, Rows = 4, Fps = 20.0f, SubframeBlend = true };
	}

	private void AddDecalMetadata(MapMetadata m, string id, string slug)
	{
		m.Decals ??= new(StringComparer.OrdinalIgnoreCase);
		m.Decals[id] = new DecalMetadata();
	}

	private void AddShaderMetadata(MapMetadata m, string id, string slug)
	{
		m.SpawnShaders ??= new(StringComparer.OrdinalIgnoreCase);
		m.SpawnShaders[id] = new SpawnShaderMetadata { Name = slug, TransitionMode = 0, Direction = 0, EdgeColor = "#00e5ffff", EdgeWidth = 0.06f, EdgeEmission = 6.0f, NoiseScale = 12.0f, NoiseRoughness = 0.4f, FresnelPower = 3.0f, VertexDisplacement = 0.0f, AlphaFade = 0.9f, Duration = 1.2f, AssetType = "SpawnShader" };
	}

	private void AddVfxMetadata(MapMetadata m, string id, string slug) => m.AddOrUpdateVfx(new VfxAttachmentConfig { VfxId = id, Name = slug });

	private int GetNextTerrainSwatchSlot(MapMetadata m)
	{
		var occupiedSlots = new bool[TextureSwatchSlots.MaxSlots];
		foreach (var existingTex in m.Textures.Values)
		{
			if (existingTex != null && existingTex.SwatchIndex >= 0 && existingTex.SwatchIndex < TextureSwatchSlots.MaxSlots)
			{
				occupiedSlots[existingTex.SwatchIndex] = true;
			}
		}

		int nextFreeSlot = TextureSwatchSlots.FirstFreeSlot(occupiedSlots);
		return nextFreeSlot < 0 ? 0 : nextFreeSlot;
	}

	private void HandlePostAddObject(string category, string newTemplateID, string parsedSlug)
	{
		if (category == "terrain")
		{
			Hud?.SetupTextureSwatches(false);
			if (GameContext.Host?.GroundTerrain != null)
			{
				GameContext.Host.GroundTerrain.ReloadTerrainTextures(true);
			}
		}

		RefreshObjectList();
		OpenEditDialogForObject(new ObjectItemInfo
		{
			Category = category,
			TemplateID = newTemplateID,
			Name = parsedSlug,
			Description = ""
		});
	}


	private static readonly Dictionary<string, Action<TemplateManagerDialog, MapMetadata, ObjectItemInfo, string>> DeleteMetadataHandlers = new(StringComparer.OrdinalIgnoreCase)
	{
		{
			"units", (dialog, meta, item, delSlug) =>
			{
				meta.RemoveUnit(item.TemplateID);
				if (!string.IsNullOrEmpty(delSlug)) meta.RemoveUnit(delSlug);
			}
		},
		{
			"buildings", (dialog, meta, item, delSlug) =>
			{
				meta.RemoveBuilding(item.TemplateID);
				if (!string.IsNullOrEmpty(delSlug)) meta.RemoveBuilding(delSlug);
			}
		},
		{
			"resources", (dialog, meta, item, delSlug) =>
			{
				meta.RemoveResource(item.TemplateID);
				if (!string.IsNullOrEmpty(delSlug)) meta.RemoveResource(delSlug);
			}
		},
		{
			"props", (dialog, meta, item, delSlug) =>
			{
				meta.RemoveProp(item.TemplateID);
				if (!string.IsNullOrEmpty(delSlug)) meta.RemoveProp(delSlug);
			}
		},
		{
			"weapons", (dialog, meta, item, delSlug) =>
			{
				meta.RemoveWeapon(item.TemplateID);
				if (!string.IsNullOrEmpty(delSlug)) meta.RemoveWeapon(delSlug);
			}
		},
		{
			"abilities", (dialog, meta, item, delSlug) =>
			{
				meta.RemoveAbility(item.TemplateID);
				if (!string.IsNullOrEmpty(delSlug)) meta.RemoveAbility(delSlug);
			}
		},
		{
			"upgrades", (dialog, meta, item, delSlug) =>
			{
				meta.RemoveUpgrade(item.TemplateID);
				if (!string.IsNullOrEmpty(delSlug)) meta.RemoveUpgrade(delSlug);
			}
		},
		{
			"items", (dialog, meta, item, delSlug) =>
			{
				meta.RemoveItem(item.TemplateID);
				if (!string.IsNullOrEmpty(delSlug)) meta.RemoveItem(delSlug);
			}
		},
		{ "terrain", (dialog, meta, item, delSlug) => dialog.RemoveTerrainTexture(meta, item, delSlug) },
		{ "spritesheets", (dialog, meta, item, delSlug) => dialog.RemoveSpritesheet(meta, item, delSlug) },
		{ "decals", (dialog, meta, item, delSlug) => dialog.RemoveDecal(meta, item, delSlug) },
		{ "shaders", (dialog, meta, item, delSlug) => dialog.RemoveSpawnShader(meta, item, delSlug) },
		{ "shader", (dialog, meta, item, delSlug) => dialog.RemoveSpawnShader(meta, item, delSlug) },
		{ "spawnshader", (dialog, meta, item, delSlug) => dialog.RemoveSpawnShader(meta, item, delSlug) },
		{ "spawnshaders", (dialog, meta, item, delSlug) => dialog.RemoveSpawnShader(meta, item, delSlug) },
		{
			"vfx", (dialog, meta, item, delSlug) =>
			{
				meta.RemoveVfx(item.TemplateID);
				if (!string.IsNullOrEmpty(delSlug)) meta.RemoveVfx(delSlug);
			}
		}
	};

	private void DeleteObject(ObjectItemInfo item)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		var references = CheckObjectReferences(wsPath, item);

		string message = references.Count > 0
			? string.Format(TranslationServer.Translate("Object '{0}' is referenced:\n- {1}\n\nAre you sure you want to delete it?"), item.TemplateID, string.Join("\n- ", references))
			: string.Format(TranslationServer.Translate("Are you sure you want to delete object '{0}' from metadata.json?"), item.TemplateID);

		Hud?.ShowConfirmationDialog(message, () =>
		{
			if (item.Category == "vfx")
			{
				GameContext.RegistryService.UnregisterVfxConfig(item.TemplateID);
			}

			GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
			{
				var (_, delSlug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
				if (DeleteMetadataHandlers.TryGetValue(item.Category, out var handler))
				{
					handler(this, meta, item, delSlug);
				}
			});

			if (item.Category == "terrain")
			{
				Hud?.SetupTextureSwatches(false);
				if (GameContext.Host?.GroundTerrain != null)
				{
					GameContext.Host.GroundTerrain.ReloadTerrainTextures(true);
				}
			}

			RefreshObjectList();
		});
	}

	private List<string> CheckObjectReferences(string wsPath, ObjectItemInfo item)
	{
		var references = new List<string>();
		string terrainPath = Path.Combine(wsPath, "terrain.json");
		if (File.Exists(terrainPath))
		{
			try
			{
				string terrainJson = File.ReadAllText(terrainPath);
				if (terrainJson.Contains(item.TemplateID, StringComparison.OrdinalIgnoreCase))
				{
					references.Add($"Placed instances found on map terrain (terrain.json)");
				}
			}
			catch
			{
			}
		}

		return references;
	}

	private void RemoveKeysFromDict<T>(Dictionary<string, T>? dict, string category, ObjectItemInfo item, string delSlug)
	{
		if (dict == null) return;
		var keysToRemove = dict.Keys.Where(k =>
			string.Equals(k, item.TemplateID, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(k, delSlug, StringComparison.OrdinalIgnoreCase) ||
			(!string.IsNullOrEmpty(item.ModelPath) && string.Equals(k, item.ModelPath, StringComparison.OrdinalIgnoreCase)) ||
			(!string.IsNullOrEmpty(delSlug) && (string.Equals(k, $"{delSlug}.rtex", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(k), delSlug, StringComparison.OrdinalIgnoreCase))) ||
			string.Equals(TemplateIDHelper.NormalizeTemplateID(category, k), item.TemplateID, StringComparison.OrdinalIgnoreCase)
		).ToList();

		foreach (var k in keysToRemove)
		{
			dict.Remove(k);
		}
	}

	private void RemoveTerrainTexture(MapMetadata meta, ObjectItemInfo item, string delSlug)
	{
		RemoveKeysFromDict(meta.Textures, "terrain", item, delSlug);
	}

	private void RemoveSpritesheet(MapMetadata meta, ObjectItemInfo item, string delSlug)
	{
		RemoveKeysFromDict(meta.VfxSpritesheets, "spritesheet", item, delSlug);
	}

	private void RemoveDecal(MapMetadata meta, ObjectItemInfo item, string delSlug)
	{
		RemoveKeysFromDict(meta.Decals, "decal", item, delSlug);
	}

	private void RemoveSpawnShader(MapMetadata meta, ObjectItemInfo item, string delSlug)
	{
		if (meta.SpawnShaders == null) return;
		var keysToRemove = meta.SpawnShaders.Keys.Where(k =>
			string.Equals(k, item.TemplateID, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(k, delSlug, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.NormalizeTemplateID("SpawnShader", k), item.TemplateID, StringComparison.OrdinalIgnoreCase)
		).ToList();

		foreach (var k in keysToRemove)
		{
			meta.SpawnShaders.Remove(k);
		}
	}
}
