using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Godot;
using Realm.Godot.Utils;
using Realm.Godot.VFX;

namespace Realm.Godot.Services;

public class MapMetadata
{
	[JsonPropertyName("license")]
	public string License { get; set; } = MetadataService.UgcLicenseUrl;

	[JsonPropertyName("GameBuildNumber")]
	public string? GameBuildNumber { get; set; }

	[JsonPropertyName("MapProperties")]
	public MapInfoMetadata MapProperties { get; set; } = new();

	[JsonPropertyName("Dependencies")]
	public List<MapDependencyMetadata> Dependencies { get; set; } = new();

	[JsonPropertyName("CustomUnits")]
	public List<GameHost.UnitMetadata> CustomUnits { get; set; } = new();

	[JsonPropertyName("CustomBuildings")]
	public List<GameHost.UnitMetadata> CustomBuildings { get; set; } = new();

	[JsonPropertyName("CustomResources")]
	public List<GameHost.ResourceMetadata> CustomResources { get; set; } = new();

	[JsonPropertyName("CustomProps")]
	public List<GameHost.PropMetadata> CustomProps { get; set; } = new();

	[JsonPropertyName("CustomAbilities")]
	public List<GameHost.AbilityMetadata> CustomAbilities { get; set; } = new();

	[JsonPropertyName("CustomWeapons")]
	public List<GameHost.WeaponMetadata> CustomWeapons { get; set; } = new();

	[JsonPropertyName("CustomUpgrades")]
	public List<GameHost.UpgradeMetadata> CustomUpgrades { get; set; } = new();

	[JsonPropertyName("CustomItems")]
	public List<GameHost.ItemMetadata> CustomItems { get; set; } = new();

	[JsonPropertyName("CustomAttachments")]
	public List<GameHost.AttachmentMetadata> CustomAttachments { get; set; } = new();

	[JsonPropertyName("CustomVfx")]
	public List<VfxAttachmentConfig> CustomVfx { get; set; } = new();

	[JsonPropertyName("CustomProceduralAnimations")]
	public List<ProceduralAnimationConfig> CustomProceduralAnimations { get; set; } = new();

	[JsonPropertyName("CustomWaterProfiles")]
	public List<WaterProfileSaveData> CustomWaterProfiles { get; set; } = new();

	[JsonPropertyName("TerrainProfiles")]
	public List<TerrainSwatchProfileData> TerrainProfiles { get; set; } = new();

	[JsonPropertyName("Models")]
	public Dictionary<string, ModelMetadata> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("textures")]
	public Dictionary<string, JsonNode> Textures { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("decals")]
	public Dictionary<string, JsonNode> Decals { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("vfx_spritesheets")]
	public Dictionary<string, JsonNode> VfxSpritesheets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("noise_textures")]
	public Dictionary<string, JsonNode> NoiseTextures { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("icons")]
	public Dictionary<string, JsonNode> Icons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("skyboxes")]
	public Dictionary<string, JsonNode> Skyboxes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("ribbons")]
	public Dictionary<string, JsonNode> Ribbons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; set; }

	public GameHost.UnitMetadata? GetUnit(string unitId) => CustomUnits?.FirstOrDefault(u => string.Equals(u.UnitId, unitId, StringComparison.OrdinalIgnoreCase));
	public GameHost.UnitMetadata? FindUnit(string unitId) => GetUnit(unitId);

	public GameHost.UnitMetadata? GetBuilding(string buildingId) => CustomBuildings?.FirstOrDefault(b => string.Equals(b.UnitId, buildingId, StringComparison.OrdinalIgnoreCase));
	public GameHost.UnitMetadata? FindBuilding(string buildingId) => GetBuilding(buildingId);

	public GameHost.PropMetadata? GetProp(string propId) => CustomProps?.FirstOrDefault(p => string.Equals(p.UnitId, propId, StringComparison.OrdinalIgnoreCase));
	public GameHost.PropMetadata? FindProp(string propId) => GetProp(propId);

	public GameHost.ResourceMetadata? GetResource(string resourceId) => CustomResources?.FirstOrDefault(r => string.Equals(r.UnitId, resourceId, StringComparison.OrdinalIgnoreCase));
	public GameHost.ResourceMetadata? FindResource(string resourceId) => GetResource(resourceId);

	public GameHost.AbilityMetadata? GetAbility(string abilityId) => CustomAbilities?.FirstOrDefault(a => string.Equals(a.AbilityId, abilityId, StringComparison.OrdinalIgnoreCase));
	public GameHost.AbilityMetadata? FindAbility(string abilityId) => GetAbility(abilityId);

	public GameHost.WeaponMetadata? GetWeapon(string weaponId) => CustomWeapons?.FirstOrDefault(w => string.Equals(w.WeaponId, weaponId, StringComparison.OrdinalIgnoreCase));
	public GameHost.WeaponMetadata? FindWeapon(string weaponId) => GetWeapon(weaponId);

	public GameHost.AttachmentMetadata? GetAttachment(string attachmentId) => CustomAttachments?.FirstOrDefault(a => string.Equals(a.AttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase));
	public GameHost.AttachmentMetadata? FindAttachment(string attachmentId) => GetAttachment(attachmentId);

	public VfxAttachmentConfig? GetVfx(string vfxId) => CustomVfx?.FirstOrDefault(v => string.Equals(v.VfxId, vfxId, StringComparison.OrdinalIgnoreCase));
	public VfxAttachmentConfig? FindVfx(string vfxId) => GetVfx(vfxId);

	public ProceduralAnimationConfig? GetProceduralAnimation(string animId) => CustomProceduralAnimations?.FirstOrDefault(a => string.Equals(a.Id, animId, StringComparison.OrdinalIgnoreCase));
	public ProceduralAnimationConfig? FindProceduralAnimation(string animId) => GetProceduralAnimation(animId);

	public GameHost.ItemMetadata? GetItem(string itemId) => CustomItems?.FirstOrDefault(i => string.Equals(i.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
	public GameHost.ItemMetadata? FindItem(string itemId) => GetItem(itemId);

	public GameHost.UpgradeMetadata? GetUpgrade(string upgradeId) => CustomUpgrades?.FirstOrDefault(u => string.Equals(u.UpgradeId, upgradeId, StringComparison.OrdinalIgnoreCase));
	public GameHost.UpgradeMetadata? FindUpgrade(string upgradeId) => GetUpgrade(upgradeId);

	public void AddOrUpdateUnit(GameHost.UnitMetadata unit)
	{
		if (string.IsNullOrWhiteSpace(unit.UnitId)) return;
		CustomUnits ??= new();
		int idx = CustomUnits.FindIndex(u => string.Equals(u.UnitId, unit.UnitId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) CustomUnits[idx] = unit;
		else CustomUnits.Add(unit);
	}

	public bool RemoveUnit(string unitId)
	{
		if (CustomUnits == null || string.IsNullOrWhiteSpace(unitId)) return false;
		return CustomUnits.RemoveAll(u => string.Equals(u.UnitId, unitId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateBuilding(GameHost.UnitMetadata building)
	{
		if (string.IsNullOrWhiteSpace(building.UnitId)) return;
		CustomBuildings ??= new();
		int idx = CustomBuildings.FindIndex(b => string.Equals(b.UnitId, building.UnitId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) CustomBuildings[idx] = building;
		else CustomBuildings.Add(building);
	}

	public bool RemoveBuilding(string buildingId)
	{
		if (CustomBuildings == null || string.IsNullOrWhiteSpace(buildingId)) return false;
		return CustomBuildings.RemoveAll(b => string.Equals(b.UnitId, buildingId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateProp(GameHost.PropMetadata prop)
	{
		if (string.IsNullOrWhiteSpace(prop.UnitId)) return;
		CustomProps ??= new();
		int idx = CustomProps.FindIndex(p => string.Equals(p.UnitId, prop.UnitId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) CustomProps[idx] = prop;
		else CustomProps.Add(prop);
	}

	public bool RemoveProp(string propId)
	{
		if (CustomProps == null || string.IsNullOrWhiteSpace(propId)) return false;
		return CustomProps.RemoveAll(p => string.Equals(p.UnitId, propId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateResource(GameHost.ResourceMetadata resource)
	{
		if (string.IsNullOrWhiteSpace(resource.UnitId)) return;
		CustomResources ??= new();
		int idx = CustomResources.FindIndex(r => string.Equals(r.UnitId, resource.UnitId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) CustomResources[idx] = resource;
		else CustomResources.Add(resource);
	}

	public bool RemoveResource(string resourceId)
	{
		if (CustomResources == null || string.IsNullOrWhiteSpace(resourceId)) return false;
		return CustomResources.RemoveAll(r => string.Equals(r.UnitId, resourceId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateAbility(GameHost.AbilityMetadata ability)
	{
		if (string.IsNullOrWhiteSpace(ability.AbilityId)) return;
		CustomAbilities ??= new();
		int idx = CustomAbilities.FindIndex(a => string.Equals(a.AbilityId, ability.AbilityId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) CustomAbilities[idx] = ability;
		else CustomAbilities.Add(ability);
	}

	public bool RemoveAbility(string abilityId)
	{
		if (CustomAbilities == null || string.IsNullOrWhiteSpace(abilityId)) return false;
		return CustomAbilities.RemoveAll(a => string.Equals(a.AbilityId, abilityId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateWeapon(GameHost.WeaponMetadata weapon)
	{
		if (string.IsNullOrWhiteSpace(weapon.WeaponId)) return;
		CustomWeapons ??= new();
		int idx = CustomWeapons.FindIndex(w => string.Equals(w.WeaponId, weapon.WeaponId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) CustomWeapons[idx] = weapon;
		else CustomWeapons.Add(weapon);
	}

	public bool RemoveWeapon(string weaponId)
	{
		if (CustomWeapons == null || string.IsNullOrWhiteSpace(weaponId)) return false;
		return CustomWeapons.RemoveAll(w => string.Equals(w.WeaponId, weaponId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateAttachment(GameHost.AttachmentMetadata attachment)
	{
		if (string.IsNullOrWhiteSpace(attachment.AttachmentId)) return;
		CustomAttachments ??= new();
		int idx = CustomAttachments.FindIndex(a => string.Equals(a.AttachmentId, attachment.AttachmentId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) CustomAttachments[idx] = attachment;
		else CustomAttachments.Add(attachment);
	}

	public bool RemoveAttachment(string attachmentId)
	{
		if (CustomAttachments == null || string.IsNullOrWhiteSpace(attachmentId)) return false;
		return CustomAttachments.RemoveAll(a => string.Equals(a.AttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateVfx(VfxAttachmentConfig vfx)
	{
		if (string.IsNullOrWhiteSpace(vfx.VfxId)) return;
		CustomVfx ??= new();
		int idx = CustomVfx.FindIndex(v => string.Equals(v.VfxId, vfx.VfxId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) CustomVfx[idx] = vfx;
		else CustomVfx.Add(vfx);
	}

	public bool RemoveVfx(string vfxId)
	{
		if (CustomVfx == null || string.IsNullOrWhiteSpace(vfxId)) return false;
		return CustomVfx.RemoveAll(v => string.Equals(v.VfxId, vfxId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateItem(GameHost.ItemMetadata item)
	{
		if (string.IsNullOrWhiteSpace(item.ItemId)) return;
		CustomItems ??= new();
		int idx = CustomItems.FindIndex(i => string.Equals(i.ItemId, item.ItemId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) CustomItems[idx] = item;
		else CustomItems.Add(item);
	}

	public bool RemoveItem(string itemId)
	{
		if (CustomItems == null || string.IsNullOrWhiteSpace(itemId)) return false;
		return CustomItems.RemoveAll(i => string.Equals(i.ItemId, itemId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateUpgrade(GameHost.UpgradeMetadata upgrade)
	{
		if (string.IsNullOrWhiteSpace(upgrade.UpgradeId)) return;
		CustomUpgrades ??= new();
		int idx = CustomUpgrades.FindIndex(u => string.Equals(u.UpgradeId, upgrade.UpgradeId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) CustomUpgrades[idx] = upgrade;
		else CustomUpgrades.Add(upgrade);
	}

	public bool RemoveUpgrade(string upgradeId)
	{
		if (CustomUpgrades == null || string.IsNullOrWhiteSpace(upgradeId)) return false;
		return CustomUpgrades.RemoveAll(u => string.Equals(u.UpgradeId, upgradeId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public WaterProfileSaveData? GetWaterProfile(string id) => CustomWaterProfiles?.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));
	public WaterProfileSaveData? GetWaterProfileByIndex(byte index) => CustomWaterProfiles?.FirstOrDefault(w => w.ProfileIndex == index);

	public void AddOrUpdateWaterProfile(WaterProfileSaveData profile)
	{
		if (string.IsNullOrWhiteSpace(profile.Id)) return;
		CustomWaterProfiles ??= new();
		int idx = CustomWaterProfiles.FindIndex(w => string.Equals(w.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) CustomWaterProfiles[idx] = profile;
		else CustomWaterProfiles.Add(profile);
	}

	public bool RemoveWaterProfile(string id)
	{
		if (CustomWaterProfiles == null || string.IsNullOrWhiteSpace(id)) return false;
		return CustomWaterProfiles.RemoveAll(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public TerrainSwatchProfileData? GetTerrainProfile(string swatchName)
	{
		if (TerrainProfiles == null) return null;
		string clean = Path.GetFileNameWithoutExtension(swatchName);
		return TerrainProfiles.FirstOrDefault(t => string.Equals(Path.GetFileNameWithoutExtension(t.SwatchName), clean, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateTerrainProfile(TerrainSwatchProfileData profile)
	{
		if (string.IsNullOrWhiteSpace(profile.SwatchName)) return;
		TerrainProfiles ??= new();
		string clean = Path.GetFileNameWithoutExtension(profile.SwatchName);
		int idx = TerrainProfiles.FindIndex(t => string.Equals(Path.GetFileNameWithoutExtension(t.SwatchName), clean, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) TerrainProfiles[idx] = profile;
		else TerrainProfiles.Add(profile);
	}

	public bool RemoveTerrainProfile(string swatchName)
	{
		if (TerrainProfiles == null || string.IsNullOrWhiteSpace(swatchName)) return false;
		string clean = Path.GetFileNameWithoutExtension(swatchName);
		return TerrainProfiles.RemoveAll(t => string.Equals(Path.GetFileNameWithoutExtension(t.SwatchName), clean, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public bool UpdateUnit(string unitId, Func<GameHost.UnitMetadata, GameHost.UnitMetadata> update)
	{
		if (CustomUnits == null || string.IsNullOrWhiteSpace(unitId)) return false;
		int idx = CustomUnits.FindIndex(u => string.Equals(u.UnitId, unitId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			CustomUnits[idx] = update(CustomUnits[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateBuilding(string buildingId, Func<GameHost.UnitMetadata, GameHost.UnitMetadata> update)
	{
		if (CustomBuildings == null || string.IsNullOrWhiteSpace(buildingId)) return false;
		int idx = CustomBuildings.FindIndex(b => string.Equals(b.UnitId, buildingId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			CustomBuildings[idx] = update(CustomBuildings[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateProp(string propId, Func<GameHost.PropMetadata, GameHost.PropMetadata> update)
	{
		if (CustomProps == null || string.IsNullOrWhiteSpace(propId)) return false;
		int idx = CustomProps.FindIndex(p => string.Equals(p.UnitId, propId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			CustomProps[idx] = update(CustomProps[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateResource(string resourceId, Func<GameHost.ResourceMetadata, GameHost.ResourceMetadata> update)
	{
		if (CustomResources == null || string.IsNullOrWhiteSpace(resourceId)) return false;
		int idx = CustomResources.FindIndex(r => string.Equals(r.UnitId, resourceId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			CustomResources[idx] = update(CustomResources[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateAbility(string abilityId, Func<GameHost.AbilityMetadata, GameHost.AbilityMetadata> update)
	{
		if (CustomAbilities == null || string.IsNullOrWhiteSpace(abilityId)) return false;
		int idx = CustomAbilities.FindIndex(a => string.Equals(a.AbilityId, abilityId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			CustomAbilities[idx] = update(CustomAbilities[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateWeapon(string weaponId, Func<GameHost.WeaponMetadata, GameHost.WeaponMetadata> update)
	{
		if (CustomWeapons == null || string.IsNullOrWhiteSpace(weaponId)) return false;
		int idx = CustomWeapons.FindIndex(w => string.Equals(w.WeaponId, weaponId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			CustomWeapons[idx] = update(CustomWeapons[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateAttachment(string attachmentId, Func<GameHost.AttachmentMetadata, GameHost.AttachmentMetadata> update)
	{
		if (CustomAttachments == null || string.IsNullOrWhiteSpace(attachmentId)) return false;
		int idx = CustomAttachments.FindIndex(a => string.Equals(a.AttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			CustomAttachments[idx] = update(CustomAttachments[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateVfx(string vfxId, Func<VfxAttachmentConfig, VfxAttachmentConfig> update)
	{
		if (CustomVfx == null || string.IsNullOrWhiteSpace(vfxId)) return false;
		int idx = CustomVfx.FindIndex(v => string.Equals(v.VfxId, vfxId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			CustomVfx[idx] = update(CustomVfx[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateItem(string itemId, Func<GameHost.ItemMetadata, GameHost.ItemMetadata> update)
	{
		if (CustomItems == null || string.IsNullOrWhiteSpace(itemId)) return false;
		int idx = CustomItems.FindIndex(i => string.Equals(i.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			CustomItems[idx] = update(CustomItems[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateUpgrade(string upgradeId, Func<GameHost.UpgradeMetadata, GameHost.UpgradeMetadata> update)
	{
		if (CustomUpgrades == null || string.IsNullOrWhiteSpace(upgradeId)) return false;
		int idx = CustomUpgrades.FindIndex(u => string.Equals(u.UpgradeId, upgradeId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			CustomUpgrades[idx] = update(CustomUpgrades[idx]);
			return true;
		}
		return false;
	}

	private ModelMetadata GetOrCreateModel(string modelKey)
	{
		Models ??= new Dictionary<string, ModelMetadata>(StringComparer.OrdinalIgnoreCase);
		if (!Models.TryGetValue(modelKey, out var model) || model == null)
		{
			model = new ModelMetadata();
			Models[modelKey] = model;
		}
		return model;
	}

	public void SetModelYOffset(string modelKey, float yOffset)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).Offsets = yOffset;
	}

	public void SetModelScale(string modelKey, float scale)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).Scales = scale;
	}

	public void SetModelCollisionCircleRatio(string modelKey, float ratio)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).CollisionCircleRatios = ratio;
	}

	public void SetModelObstacleRadius(string modelKey, float radius)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).ObstacleRadii = radius;
	}

	public void SetModelBrightness(string modelKey, float brightness)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).Brightness = brightness;
	}

	public void SetModelColorTint(string modelKey, string tint)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).ColorTint = tint;
	}

	public void SetModelDespillPlayerColor(string modelKey, bool despill)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).DespillPlayerColor = despill;
	}

	public void SetModelNormalizeLuminance(string modelKey, bool normalize)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).NormalizeLuminance = normalize;
	}

	public void SetModelIgnorePlayerColor(string modelKey, bool ignore)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).IgnorePlayerColor = ignore;
	}
}

public class MapInfoMetadata
{
	public string? MapName { get; set; }
	public string? MapDescription { get; set; }
	public string? Author { get; set; }
	public string? SuggestedPlayers { get; set; }
	public string? MinimapImage { get; set; }
	public string? ShroudType { get; set; }
	public string? WeatherType { get; set; }
	public float? TerrainBaseHeight { get; set; }
	public float? ShadowIntensity { get; set; }
	public int? MapWidth { get; set; }
	public int? MapHeight { get; set; }
	public int? PlayableWidth { get; set; }
	public int? PlayableHeight { get; set; }
	public float? CameraBoundsLeft { get; set; }
	public float? CameraBoundsRight { get; set; }
	public float? CameraBoundsTop { get; set; }
	public float? CameraBoundsBottom { get; set; }
	public string? LoadingImage { get; set; }
	public string? LoadingMusic { get; set; }
	public string? LoadingTitle { get; set; }
	public string? LoadingSubtitle { get; set; }
	public string? LoadingBodyText { get; set; }
	public List<string>? HowToPlayInstructions { get; set; }
	public string? HowToPlayObjective { get; set; }
	public string? Version { get; set; }
	public List<MapChangelogEntry>? Changelog { get; set; }
	public List<MapPlayerSlotConfig>? PlayerSlots { get; set; }
	public List<MapTeamConfig>? Teams { get; set; }
	public List<string>? Tags { get; set; }
	public string? MapType { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public class MapChangelogEntry
{
	public string Version { get; set; } = string.Empty;
	public string Date { get; set; } = string.Empty;
	public string Details { get; set; } = string.Empty;
}

public class MapPlayerSlotConfig
{
	public int SlotId { get; set; }
	public string Name { get; set; } = string.Empty;
	public string Color { get; set; } = string.Empty;
	public string Faction { get; set; } = string.Empty;
	public string Controller { get; set; } = "HumanPlayer";
	public string? AiType { get; set; }
	public string? StartLocation { get; set; }
	public string? CustomDecal { get; set; }
}

public class MapTeamConfig
{
	public string TeamName { get; set; } = string.Empty;
	public List<int> Slots { get; set; } = new();
}

public class MapDependencyMetadata
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Version { get; set; } = "1.0.0";
	public string? Hash { get; set; }
	public bool IsOptional { get; set; }
	public string? Url { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public class ModelMetadata
{
	[JsonPropertyName("Brightness")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public float? Brightness { get; set; }

	[JsonPropertyName("CollisionCircleRatios")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public float? CollisionCircleRatios { get; set; }

	[JsonPropertyName("ColorTint")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ColorTint { get; set; }

	[JsonPropertyName("DespillPlayerColor")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? DespillPlayerColor { get; set; }

	[JsonPropertyName("IgnorePlayerColor")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? IgnorePlayerColor { get; set; }

	[JsonPropertyName("NormalizeLuminance")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? NormalizeLuminance { get; set; }

	[JsonPropertyName("ObstacleRadii")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public float? ObstacleRadii { get; set; }

	[JsonPropertyName("Offsets")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public float? Offsets { get; set; }

	[JsonPropertyName("Scales")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public float? Scales { get; set; }

	[JsonPropertyName("SpawnShaders")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? SpawnShaders { get; set; }

	[JsonPropertyName("DeathShaders")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? DeathShaders { get; set; }

	[JsonPropertyName("ProceduralAnimation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ProceduralAnimation { get; set; }

	[JsonPropertyName("EnableProceduralAnimation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? EnableProceduralAnimation { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public class MapValidationResult
{
	public bool IsValid => Errors.Count == 0;
	public List<string> Errors { get; } = new();
	public List<string> Warnings { get; } = new();
}

public class MetadataService
{
	public const string UgcLicenseUrl = "https://www.realm-game.com/RealmPlatform_UGC_License_v1.txt";

	private static MetadataService? _defaultFallbackInstance;
	public static MetadataService Instance => ServiceLocator.TryGet<MetadataService>() ?? (_defaultFallbackInstance ??= new MetadataService());

	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		IncludeFields = true,
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};

	public static string ResolveMetadataPath(string pathOrDirectory)
	{
		if (string.IsNullOrWhiteSpace(pathOrDirectory))
		{
			pathOrDirectory = MapWorkspaceService.GetActiveWorkspacePath();
		}

		if (pathOrDirectory.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
		    pathOrDirectory.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
		{
			string globalized = ProjectSettings.GlobalizePath(pathOrDirectory);
			if (Directory.Exists(globalized) || !globalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			{
				return Path.Combine(globalized, "metadata.json");
			}
			return globalized;
		}

		if (Directory.Exists(pathOrDirectory) || !pathOrDirectory.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
		{
			return Path.Combine(pathOrDirectory, "metadata.json");
		}

		return pathOrDirectory;
	}

	public MapMetadata LoadMetadata(string pathOrDirectory)
	{
		string targetPath = ResolveMetadataPath(pathOrDirectory);
		string jsonText = string.Empty;

		if (File.Exists(targetPath))
		{
			try
			{
				jsonText = File.ReadAllText(targetPath);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MetadataService] Failed reading file at {targetPath}: {ex.Message}");
			}
		}

		if (string.IsNullOrWhiteSpace(jsonText))
		{
			return new MapMetadata();
		}

		try
		{
			var metadata = JsonSerializer.Deserialize<MapMetadata>(jsonText, SerializerOptions) ?? new MapMetadata();
			CleanMetadata(metadata);
			return metadata;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MetadataService] Failed deserializing metadata from {targetPath}: {ex.Message}");
			return new MapMetadata();
		}
	}

	public bool TryLoadMetadata(string pathOrDirectory, out MapMetadata metadata)
	{
		string targetPath = ResolveMetadataPath(pathOrDirectory);
		if (!File.Exists(targetPath))
		{
			metadata = new MapMetadata();
			return false;
		}

		metadata = LoadMetadata(pathOrDirectory);
		return true;
	}

	public void SaveMetadata(string pathOrDirectory, MapMetadata metadata)
	{
		if (metadata == null) return;

		string targetPath = ResolveMetadataPath(pathOrDirectory);
		string? directory = Path.GetDirectoryName(targetPath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}

		CleanMetadata(metadata);

		try
		{
			string jsonString = JsonSerializer.Serialize(metadata, SerializerOptions);
			MapJsonFormatter.SaveFormattedJson(targetPath, jsonString);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MetadataService] Failed saving metadata to {targetPath}: {ex.Message}");
		}
	}

	public void UpdateMetadata(string pathOrDirectory, Action<MapMetadata> updateAction)
	{
		if (updateAction == null) return;

		string targetPath = ResolveMetadataPath(pathOrDirectory);
		var metadata = LoadMetadata(targetPath);
		updateAction(metadata);
		SaveMetadata(targetPath, metadata);
	}

	public MapValidationResult ValidateMetadata(MapMetadata metadata)
	{
		var result = new MapValidationResult();
		if (metadata == null)
		{
			result.Errors.Add("Metadata object is null.");
			return result;
		}

		var entityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		void ValidateEntityList<T>(IEnumerable<T> items, Func<T, string> getId, Func<T, float> getScale, string domainName)
		{
			if (items == null) return;
			foreach (var item in items)
			{
				string id = getId(item);
				if (string.IsNullOrWhiteSpace(id))
				{
					result.Errors.Add($"Found {domainName} entry with empty ID.");
					continue;
				}

				if (!entityIds.Add(id))
				{
					result.Warnings.Add($"Duplicate object ID '{id}' detected in {domainName}.");
				}

				float scale = getScale(item);
				if (scale <= 0f)
				{
					result.Warnings.Add($"{domainName} '{id}' has non-positive scale {scale}.");
				}
			}
		}

		ValidateEntityList(metadata.CustomUnits, u => u.UnitId, u => u.Scale, "CustomUnits");
		ValidateEntityList(metadata.CustomBuildings, b => b.UnitId, b => b.Scale, "CustomBuildings");
		ValidateEntityList(metadata.CustomResources, r => r.UnitId, r => r.Scale, "CustomResources");
		ValidateEntityList(metadata.CustomProps, p => p.UnitId, p => p.Scale, "CustomProps");

		if (metadata.Dependencies != null)
		{
			var depIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var dep in metadata.Dependencies)
			{
				if (string.IsNullOrWhiteSpace(dep.Id))
				{
					result.Errors.Add("Found dependency entry with empty Id.");
				}
				else if (!depIds.Add(dep.Id))
				{
					result.Warnings.Add($"Duplicate dependency Id '{dep.Id}'.");
				}
			}
		}

		if (metadata.MapProperties != null && metadata.MapProperties.PlayerSlots != null)
		{
			var slotIds = new HashSet<int>();
			foreach (var slot in metadata.MapProperties.PlayerSlots)
			{
				if (slot.SlotId < 0)
				{
					result.Errors.Add($"Player slot has invalid negative SlotId {slot.SlotId}.");
				}
				else if (!slotIds.Add(slot.SlotId))
				{
					result.Warnings.Add($"Duplicate player SlotId {slot.SlotId}.");
				}
			}
		}

		return result;
	}

	public void CleanMetadata(string pathOrDirectory)
	{
		UpdateMetadata(pathOrDirectory, meta => CleanMetadata(meta));
	}

	public void CleanMetadata(MapMetadata metadata)
	{
		if (metadata == null) return;

		metadata.MapProperties ??= new MapInfoMetadata();
		metadata.Dependencies ??= new List<MapDependencyMetadata>();
		metadata.CustomUnits ??= new List<GameHost.UnitMetadata>();
		metadata.CustomBuildings ??= new List<GameHost.UnitMetadata>();
		metadata.CustomResources ??= new List<GameHost.ResourceMetadata>();
		metadata.CustomProps ??= new List<GameHost.PropMetadata>();
		metadata.CustomAbilities ??= new List<GameHost.AbilityMetadata>();
		metadata.CustomWeapons ??= new List<GameHost.WeaponMetadata>();
		metadata.CustomUpgrades ??= new List<GameHost.UpgradeMetadata>();
		metadata.CustomItems ??= new List<GameHost.ItemMetadata>();
		metadata.CustomAttachments ??= new List<GameHost.AttachmentMetadata>();
		metadata.CustomVfx ??= new List<VfxAttachmentConfig>();

		metadata.Models ??= new Dictionary<string, ModelMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Textures ??= new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
		metadata.Decals ??= new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
		metadata.VfxSpritesheets ??= new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
		metadata.NoiseTextures ??= new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
		metadata.Icons ??= new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
		metadata.Skyboxes ??= new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
		metadata.Ribbons ??= new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);

		if (string.IsNullOrEmpty(metadata.GameBuildNumber))
		{
			metadata.GameBuildNumber = Realm.Shared.RealmVersion.GameBuildNumber;
		}

		metadata.License = UgcLicenseUrl;

		if (metadata.ExtensionData != null)
		{
			metadata.ExtensionData.Remove("Assets");
			metadata.ExtensionData.Remove("assets");
		}

		if (metadata.MapProperties.ExtensionData != null)
		{
			metadata.MapProperties.ExtensionData.Remove("Assets");
			metadata.MapProperties.ExtensionData.Remove("assets");
		}
	}

	public GameHost.UnitMetadata? FindUnit(MapMetadata metadata, string unitId)
	{
		if (metadata?.CustomUnits == null || string.IsNullOrWhiteSpace(unitId)) return null;
		return metadata.CustomUnits.FirstOrDefault(u => string.Equals(u.UnitId, unitId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateUnit(MapMetadata metadata, GameHost.UnitMetadata unit)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(unit.UnitId)) return;
		int index = metadata.CustomUnits.FindIndex(u => string.Equals(u.UnitId, unit.UnitId, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomUnits[index] = unit;
		}
		else
		{
			metadata.CustomUnits.Add(unit);
		}
	}

	public bool RemoveUnit(MapMetadata metadata, string unitId)
	{
		if (metadata?.CustomUnits == null || string.IsNullOrWhiteSpace(unitId)) return false;
		return metadata.CustomUnits.RemoveAll(u => string.Equals(u.UnitId, unitId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public GameHost.UnitMetadata? FindBuilding(MapMetadata metadata, string buildingId)
	{
		if (metadata?.CustomBuildings == null || string.IsNullOrWhiteSpace(buildingId)) return null;
		return metadata.CustomBuildings.FirstOrDefault(b => string.Equals(b.UnitId, buildingId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateBuilding(MapMetadata metadata, GameHost.UnitMetadata building)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(building.UnitId)) return;
		int index = metadata.CustomBuildings.FindIndex(b => string.Equals(b.UnitId, building.UnitId, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomBuildings[index] = building;
		}
		else
		{
			metadata.CustomBuildings.Add(building);
		}
	}

	public bool RemoveBuilding(MapMetadata metadata, string buildingId)
	{
		if (metadata?.CustomBuildings == null || string.IsNullOrWhiteSpace(buildingId)) return false;
		return metadata.CustomBuildings.RemoveAll(b => string.Equals(b.UnitId, buildingId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public GameHost.PropMetadata? FindProp(MapMetadata metadata, string propId)
	{
		if (metadata?.CustomProps == null || string.IsNullOrWhiteSpace(propId)) return null;
		return metadata.CustomProps.FirstOrDefault(p => string.Equals(p.UnitId, propId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateProp(MapMetadata metadata, GameHost.PropMetadata prop)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(prop.UnitId)) return;
		int index = metadata.CustomProps.FindIndex(p => string.Equals(p.UnitId, prop.UnitId, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomProps[index] = prop;
		}
		else
		{
			metadata.CustomProps.Add(prop);
		}
	}

	public bool RemoveProp(MapMetadata metadata, string propId)
	{
		if (metadata?.CustomProps == null || string.IsNullOrWhiteSpace(propId)) return false;
		return metadata.CustomProps.RemoveAll(p => string.Equals(p.UnitId, propId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public GameHost.ResourceMetadata? FindResource(MapMetadata metadata, string resourceId)
	{
		if (metadata?.CustomResources == null || string.IsNullOrWhiteSpace(resourceId)) return null;
		return metadata.CustomResources.FirstOrDefault(r => string.Equals(r.UnitId, resourceId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateResource(MapMetadata metadata, GameHost.ResourceMetadata resource)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(resource.UnitId)) return;
		int index = metadata.CustomResources.FindIndex(r => string.Equals(r.UnitId, resource.UnitId, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomResources[index] = resource;
		}
		else
		{
			metadata.CustomResources.Add(resource);
		}
	}

	public bool RemoveResource(MapMetadata metadata, string resourceId)
	{
		if (metadata?.CustomResources == null || string.IsNullOrWhiteSpace(resourceId)) return false;
		return metadata.CustomResources.RemoveAll(r => string.Equals(r.UnitId, resourceId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public GameHost.WeaponMetadata? FindWeapon(MapMetadata metadata, string weaponId)
	{
		if (metadata?.CustomWeapons == null || string.IsNullOrWhiteSpace(weaponId)) return null;
		return metadata.CustomWeapons.FirstOrDefault(w => string.Equals(w.WeaponId, weaponId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateWeapon(MapMetadata metadata, GameHost.WeaponMetadata weapon)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(weapon.WeaponId)) return;
		int index = metadata.CustomWeapons.FindIndex(w => string.Equals(w.WeaponId, weapon.WeaponId, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomWeapons[index] = weapon;
		}
		else
		{
			metadata.CustomWeapons.Add(weapon);
		}
	}

	public bool RemoveWeapon(MapMetadata metadata, string weaponId)
	{
		if (metadata?.CustomWeapons == null || string.IsNullOrWhiteSpace(weaponId)) return false;
		return metadata.CustomWeapons.RemoveAll(w => string.Equals(w.WeaponId, weaponId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public GameHost.AbilityMetadata? FindAbility(MapMetadata metadata, string abilityId)
	{
		if (metadata?.CustomAbilities == null || string.IsNullOrWhiteSpace(abilityId)) return null;
		return metadata.CustomAbilities.FirstOrDefault(a => string.Equals(a.AbilityId, abilityId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateAbility(MapMetadata metadata, GameHost.AbilityMetadata ability)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(ability.AbilityId)) return;
		int index = metadata.CustomAbilities.FindIndex(a => string.Equals(a.AbilityId, ability.AbilityId, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomAbilities[index] = ability;
		}
		else
		{
			metadata.CustomAbilities.Add(ability);
		}
	}

	public bool RemoveAbility(MapMetadata metadata, string abilityId)
	{
		if (metadata?.CustomAbilities == null || string.IsNullOrWhiteSpace(abilityId)) return false;
		return metadata.CustomAbilities.RemoveAll(a => string.Equals(a.AbilityId, abilityId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public GameHost.UpgradeMetadata? FindUpgrade(MapMetadata metadata, string upgradeId)
	{
		if (metadata?.CustomUpgrades == null || string.IsNullOrWhiteSpace(upgradeId)) return null;
		return metadata.CustomUpgrades.FirstOrDefault(u => string.Equals(u.UpgradeId, upgradeId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateUpgrade(MapMetadata metadata, GameHost.UpgradeMetadata upgrade)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(upgrade.UpgradeId)) return;
		int index = metadata.CustomUpgrades.FindIndex(u => string.Equals(u.UpgradeId, upgrade.UpgradeId, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomUpgrades[index] = upgrade;
		}
		else
		{
			metadata.CustomUpgrades.Add(upgrade);
		}
	}

	public bool RemoveUpgrade(MapMetadata metadata, string upgradeId)
	{
		if (metadata?.CustomUpgrades == null || string.IsNullOrWhiteSpace(upgradeId)) return false;
		return metadata.CustomUpgrades.RemoveAll(u => string.Equals(u.UpgradeId, upgradeId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public GameHost.ItemMetadata? FindItem(MapMetadata metadata, string itemId)
	{
		if (metadata?.CustomItems == null || string.IsNullOrWhiteSpace(itemId)) return null;
		return metadata.CustomItems.FirstOrDefault(i => string.Equals(i.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateItem(MapMetadata metadata, GameHost.ItemMetadata item)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(item.ItemId)) return;
		int index = metadata.CustomItems.FindIndex(i => string.Equals(i.ItemId, item.ItemId, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomItems[index] = item;
		}
		else
		{
			metadata.CustomItems.Add(item);
		}
	}

	public bool RemoveItem(MapMetadata metadata, string itemId)
	{
		if (metadata?.CustomItems == null || string.IsNullOrWhiteSpace(itemId)) return false;
		return metadata.CustomItems.RemoveAll(i => string.Equals(i.ItemId, itemId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public GameHost.AttachmentMetadata? FindAttachment(MapMetadata metadata, string attachmentId)
	{
		if (metadata?.CustomAttachments == null || string.IsNullOrWhiteSpace(attachmentId)) return null;
		return metadata.CustomAttachments.FirstOrDefault(a => string.Equals(a.AttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateAttachment(MapMetadata metadata, GameHost.AttachmentMetadata attachment)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(attachment.AttachmentId)) return;
		int index = metadata.CustomAttachments.FindIndex(a => string.Equals(a.AttachmentId, attachment.AttachmentId, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomAttachments[index] = attachment;
		}
		else
		{
			metadata.CustomAttachments.Add(attachment);
		}
	}

	public bool RemoveAttachment(MapMetadata metadata, string attachmentId)
	{
		if (metadata?.CustomAttachments == null || string.IsNullOrWhiteSpace(attachmentId)) return false;
		return metadata.CustomAttachments.RemoveAll(a => string.Equals(a.AttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public VfxAttachmentConfig? FindVfx(MapMetadata metadata, string vfxId)
	{
		if (metadata?.CustomVfx == null || string.IsNullOrWhiteSpace(vfxId)) return null;
		return metadata.CustomVfx.FirstOrDefault(v => string.Equals(v.VfxId, vfxId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateVfx(MapMetadata metadata, VfxAttachmentConfig vfx)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(vfx.VfxId)) return;
		int index = metadata.CustomVfx.FindIndex(v => string.Equals(v.VfxId, vfx.VfxId, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomVfx[index] = vfx;
		}
		else
		{
			metadata.CustomVfx.Add(vfx);
		}
	}

	public bool RemoveVfx(MapMetadata metadata, string vfxId)
	{
		if (metadata?.CustomVfx == null || string.IsNullOrWhiteSpace(vfxId)) return false;
		return metadata.CustomVfx.RemoveAll(v => string.Equals(v.VfxId, vfxId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	private static ModelMetadata GetOrCreateModel(MapMetadata metadata, string modelKey)
	{
		metadata.Models ??= new Dictionary<string, ModelMetadata>(StringComparer.OrdinalIgnoreCase);
		if (!metadata.Models.TryGetValue(modelKey, out var model) || model == null)
		{
			model = new ModelMetadata();
			metadata.Models[modelKey] = model;
		}
		return model;
	}

	public void SetModelYOffset(MapMetadata metadata, string modelKey, float yOffset)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(metadata, modelKey).Offsets = yOffset;
	}

	public void SetModelScale(MapMetadata metadata, string modelKey, float scale)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(metadata, modelKey).Scales = scale;
	}

	public void SetModelCollisionCircleRatio(MapMetadata metadata, string modelKey, float ratio)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(metadata, modelKey).CollisionCircleRatios = ratio;
	}

	public void SetModelObstacleRadius(MapMetadata metadata, string modelKey, float radius)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(metadata, modelKey).ObstacleRadii = radius;
	}

	public void SetModelBrightness(MapMetadata metadata, string modelKey, float brightness)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(metadata, modelKey).Brightness = brightness;
	}

	public void SetModelColorTint(MapMetadata metadata, string modelKey, string tint)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(metadata, modelKey).ColorTint = tint;
	}

	public void SetModelDespillPlayerColor(MapMetadata metadata, string modelKey, bool despill)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(metadata, modelKey).DespillPlayerColor = despill;
	}

	public void SetModelNormalizeLuminance(MapMetadata metadata, string modelKey, bool normalizeLuminance)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(metadata, modelKey).NormalizeLuminance = normalizeLuminance;
	}

	public void SetModelIgnorePlayerColor(MapMetadata metadata, string modelKey, bool ignorePlayerColor)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(metadata, modelKey).IgnorePlayerColor = ignorePlayerColor;
	}

	public void SetModelSpawnShader(MapMetadata metadata, string modelKey, string spawnShader)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(metadata, modelKey).SpawnShaders = string.IsNullOrWhiteSpace(spawnShader) ? null : spawnShader;
	}

	public void SetModelDeathShader(MapMetadata metadata, string modelKey, string deathShader)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(metadata, modelKey).DeathShaders = string.IsNullOrWhiteSpace(deathShader) ? null : deathShader;
	}

	public void SetModelProceduralAnimation(MapMetadata metadata, string modelKey, string animId)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(metadata, modelKey).ProceduralAnimation = string.IsNullOrWhiteSpace(animId) ? null : animId;
	}

	public string? GetModelProceduralAnimation(MapMetadata metadata, string modelKey)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey) || metadata.Models == null) return null;
		return metadata.Models.TryGetValue(modelKey, out var model) ? model.ProceduralAnimation : null;
	}

	public void SetModelEnableProceduralAnimation(MapMetadata metadata, string modelKey, bool enable)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(metadata, modelKey).EnableProceduralAnimation = enable ? true : null;
	}

	public bool GetModelEnableProceduralAnimation(MapMetadata metadata, string modelKey)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey) || metadata.Models == null) return false;
		return metadata.Models.TryGetValue(modelKey, out var model) && (model.EnableProceduralAnimation ?? false);
	}

	public void RemoveModelOverrides(MapMetadata metadata, string modelKey)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(modelKey)) return;
		metadata.Models?.Remove(modelKey);
	}

	public string GetMapName(MapMetadata metadata)
	{
		return metadata?.MapProperties?.MapName ?? string.Empty;
	}

	public void SetMapName(MapMetadata metadata, string mapName)
	{
		if (metadata == null) return;
		metadata.MapProperties ??= new MapInfoMetadata();
		metadata.MapProperties.MapName = mapName;
	}

	public string GetMapDescription(MapMetadata metadata)
	{
		return metadata?.MapProperties?.MapDescription ?? string.Empty;
	}

	public void SetMapDescription(MapMetadata metadata, string description)
	{
		if (metadata == null) return;
		metadata.MapProperties ??= new MapInfoMetadata();
		metadata.MapProperties.MapDescription = description;
	}

	public void AddOrUpdateDependency(MapMetadata metadata, MapDependencyMetadata dependency)
	{
		if (metadata == null || dependency == null || string.IsNullOrWhiteSpace(dependency.Id)) return;
		int index = metadata.Dependencies.FindIndex(d => string.Equals(d.Id, dependency.Id, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.Dependencies[index] = dependency;
		}
		else
		{
			metadata.Dependencies.Add(dependency);
		}
	}

	public bool RemoveDependency(MapMetadata metadata, string dependencyId)
	{
		if (metadata?.Dependencies == null || string.IsNullOrWhiteSpace(dependencyId)) return false;
		return metadata.Dependencies.RemoveAll(d => string.Equals(d.Id, dependencyId, StringComparison.OrdinalIgnoreCase)) > 0;
	}
}
