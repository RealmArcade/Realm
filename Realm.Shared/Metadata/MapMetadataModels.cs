using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using Realm.Shared.Animation;
using Realm.Shared.Serialization;
using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

public class TemplateContainer
{
	[JsonPropertyName("Units")]
	public List<UnitMetadata> Units { get; set; } = new();

	[JsonPropertyName("Buildings")]
	public List<UnitMetadata> Buildings { get; set; } = new();

	[JsonPropertyName("Resources")]
	public List<ResourceMetadata> Resources { get; set; } = new();

	[JsonPropertyName("Props")]
	public List<PropMetadata> Props { get; set; } = new();

	[JsonPropertyName("Abilities")]
	public List<AbilityMetadata> Abilities { get; set; } = new();

	[JsonPropertyName("Weapons")]
	public List<WeaponMetadata> Weapons { get; set; } = new();

	[JsonPropertyName("Upgrades")]
	public List<UpgradeMetadata> Upgrades { get; set; } = new();

	[JsonPropertyName("Items")]
	public List<ItemMetadata> Items { get; set; } = new();

	[JsonPropertyName("Attachments")]
	public List<AttachmentMetadata> Attachments { get; set; } = new();

	[JsonPropertyName("Vfx")]
	public List<VfxAttachmentConfig> Vfx { get; set; } = new();

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public class MapMetadata
{
	[JsonPropertyName("license")]
	public string License { get; set; } = "https://www.realm-game.com/RealmPlatform_UGC_License_v1.txt";

	[JsonPropertyName("GameBuildNumber")]
	public string? GameBuildNumber { get; set; }

	[JsonPropertyName("MapProperties")]
	public MapInfoMetadata MapProperties { get; set; } = new();

	[JsonPropertyName("Dependencies")]
	public List<MapDependencyMetadata> Dependencies { get; set; } = new();

	[JsonPropertyName("Templates")]
	public TemplateContainer Templates { get; set; } = new();

	[JsonPropertyName("CustomProceduralAnimations")]
	public List<ProceduralAnimationConfig> CustomProceduralAnimations { get; set; } = new();

	[JsonPropertyName("CustomWaterProfiles")]
	public List<WaterProfileSaveData> CustomWaterProfiles { get; set; } = new();

	[JsonPropertyName("CustomEnvironmentPresets")]
	public List<EnvironmentPresetConfig> CustomEnvironmentPresets { get; set; } = new();

	[JsonPropertyName("DefaultEnvironmentPreset")]
	public string? DefaultEnvironmentPreset { get; set; }

	[JsonPropertyName("TerrainProfiles")]
	public List<TerrainSwatchProfileData> TerrainProfiles { get; set; } = new();

	[JsonPropertyName("Models")]
	public Dictionary<string, ModelMetadata> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("textures")]
	public Dictionary<string, TextureMetadata> Textures { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("decals")]
	public Dictionary<string, DecalMetadata> Decals { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("vfx_spritesheets")]
	public Dictionary<string, VfxMetadata> VfxSpritesheets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("noise_textures")]
	public Dictionary<string, TextureMetadata> NoiseTextures { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("icons")]
	public Dictionary<string, IconMetadata> Icons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("skyboxes")]
	public Dictionary<string, SkyboxMetadata> Skyboxes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("ribbons")]
	public Dictionary<string, RibbonMetadata> Ribbons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("SpawnShader")]
	public Dictionary<string, ShaderMetadata> SpawnShaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonPropertyName("gdshader")]
	public Dictionary<string, ShaderMetadata> GdShaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; set; }

	public UnitMetadata? GetUnit(string objectId) => Templates?.Units?.FirstOrDefault(u => string.Equals(u.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	public UnitMetadata? FindUnit(string objectId) => GetUnit(objectId);

	public UnitMetadata? GetBuilding(string objectId) => Templates?.Buildings?.FirstOrDefault(b => string.Equals(b.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	public UnitMetadata? FindBuilding(string objectId) => GetBuilding(objectId);

	public PropMetadata? GetProp(string objectId) => Templates?.Props?.FirstOrDefault(p => string.Equals(p.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	public PropMetadata? FindProp(string objectId) => GetProp(objectId);

	public ResourceMetadata? GetResource(string objectId) => Templates?.Resources?.FirstOrDefault(r => string.Equals(r.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	public ResourceMetadata? FindResource(string objectId) => GetResource(objectId);

	public AbilityMetadata? GetAbility(string objectId) => Templates?.Abilities?.FirstOrDefault(a => string.Equals(a.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	public AbilityMetadata? FindAbility(string objectId) => GetAbility(objectId);

	public WeaponMetadata? GetWeapon(string objectId) => Templates?.Weapons?.FirstOrDefault(w => string.Equals(w.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	public WeaponMetadata? FindWeapon(string objectId) => GetWeapon(objectId);

	public AttachmentMetadata? GetAttachment(string attachmentId) => Templates?.Attachments?.FirstOrDefault(a => string.Equals(a.AttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase));
	public AttachmentMetadata? FindAttachment(string attachmentId) => GetAttachment(attachmentId);

	public VfxAttachmentConfig? GetVfx(string vfxId) => Templates?.Vfx?.FirstOrDefault(v => string.Equals(v.VfxId, vfxId, StringComparison.OrdinalIgnoreCase));
	public VfxAttachmentConfig? FindVfx(string vfxId) => GetVfx(vfxId);

	public ProceduralAnimationConfig? GetProceduralAnimation(string animId) => CustomProceduralAnimations?.FirstOrDefault(a => string.Equals(a.Id, animId, StringComparison.OrdinalIgnoreCase));
	public ProceduralAnimationConfig? FindProceduralAnimation(string animId) => GetProceduralAnimation(animId);

	public ItemMetadata? GetItem(string objectId) => Templates?.Items?.FirstOrDefault(i => string.Equals(i.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	public ItemMetadata? FindItem(string objectId) => GetItem(objectId);

	public UpgradeMetadata? GetUpgrade(string objectId) => Templates?.Upgrades?.FirstOrDefault(u => string.Equals(u.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	public UpgradeMetadata? FindUpgrade(string objectId) => GetUpgrade(objectId);

	public EnvironmentPresetConfig? GetEnvironmentPreset(string presetId) => CustomEnvironmentPresets?.FirstOrDefault(p => string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase));
	public EnvironmentPresetConfig? FindEnvironmentPreset(string presetId) => GetEnvironmentPreset(presetId);

	public void AddOrUpdateEnvironmentPreset(EnvironmentPresetConfig preset)
	{
		if (string.IsNullOrWhiteSpace(preset.Id)) return;
		CustomEnvironmentPresets ??= new();
		int idx = CustomEnvironmentPresets.FindIndex(p => string.Equals(p.Id, preset.Id, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) CustomEnvironmentPresets[idx] = preset;
		else CustomEnvironmentPresets.Add(preset);
	}

	public bool RemoveEnvironmentPreset(string presetId)
	{
		if (CustomEnvironmentPresets == null || string.IsNullOrWhiteSpace(presetId)) return false;
		return CustomEnvironmentPresets.RemoveAll(p => string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateUnit(UnitMetadata unit)
	{
		if (string.IsNullOrWhiteSpace(unit.TemplateID)) return;
		Templates ??= new();
		Templates.Units ??= new();
		int idx = Templates.Units.FindIndex(u => string.Equals(u.TemplateID, unit.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) Templates.Units[idx] = unit;
		else Templates.Units.Add(unit);
	}

	public bool RemoveUnit(string objectId)
	{
		if (Templates?.Units == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return Templates.Units.RemoveAll(u => string.Equals(u.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateBuilding(UnitMetadata building)
	{
		if (string.IsNullOrWhiteSpace(building.TemplateID)) return;
		Templates ??= new();
		Templates.Buildings ??= new();
		int idx = Templates.Buildings.FindIndex(b => string.Equals(b.TemplateID, building.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) Templates.Buildings[idx] = building;
		else Templates.Buildings.Add(building);
	}

	public bool RemoveBuilding(string objectId)
	{
		if (Templates?.Buildings == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return Templates.Buildings.RemoveAll(b => string.Equals(b.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateProp(PropMetadata prop)
	{
		if (string.IsNullOrWhiteSpace(prop.TemplateID)) return;
		Templates ??= new();
		Templates.Props ??= new();
		int idx = Templates.Props.FindIndex(p => string.Equals(p.TemplateID, prop.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) Templates.Props[idx] = prop;
		else Templates.Props.Add(prop);
	}

	public bool RemoveProp(string objectId)
	{
		if (Templates?.Props == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return Templates.Props.RemoveAll(p => string.Equals(p.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateResource(ResourceMetadata resource)
	{
		if (string.IsNullOrWhiteSpace(resource.TemplateID)) return;
		Templates ??= new();
		Templates.Resources ??= new();
		int idx = Templates.Resources.FindIndex(r => string.Equals(r.TemplateID, resource.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) Templates.Resources[idx] = resource;
		else Templates.Resources.Add(resource);
	}

	public bool RemoveResource(string objectId)
	{
		if (Templates?.Resources == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return Templates.Resources.RemoveAll(r => string.Equals(r.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateAbility(AbilityMetadata ability)
	{
		if (string.IsNullOrWhiteSpace(ability.TemplateID)) return;
		Templates ??= new();
		Templates.Abilities ??= new();
		int idx = Templates.Abilities.FindIndex(a => string.Equals(a.TemplateID, ability.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) Templates.Abilities[idx] = ability;
		else Templates.Abilities.Add(ability);
	}

	public bool RemoveAbility(string objectId)
	{
		if (Templates?.Abilities == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return Templates.Abilities.RemoveAll(a => string.Equals(a.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateWeapon(WeaponMetadata weapon)
	{
		if (string.IsNullOrWhiteSpace(weapon.TemplateID)) return;
		Templates ??= new();
		Templates.Weapons ??= new();
		int idx = Templates.Weapons.FindIndex(w => string.Equals(w.TemplateID, weapon.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) Templates.Weapons[idx] = weapon;
		else Templates.Weapons.Add(weapon);
	}

	public bool RemoveWeapon(string objectId)
	{
		if (Templates?.Weapons == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return Templates.Weapons.RemoveAll(w => string.Equals(w.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateAttachment(AttachmentMetadata attachment)
	{
		if (string.IsNullOrWhiteSpace(attachment.AttachmentId)) return;
		Templates ??= new();
		Templates.Attachments ??= new();
		int idx = Templates.Attachments.FindIndex(a => string.Equals(a.AttachmentId, attachment.AttachmentId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) Templates.Attachments[idx] = attachment;
		else Templates.Attachments.Add(attachment);
	}

	public bool RemoveAttachment(string attachmentId)
	{
		if (Templates?.Attachments == null || string.IsNullOrWhiteSpace(attachmentId)) return false;
		return Templates.Attachments.RemoveAll(a => string.Equals(a.AttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateVfx(VfxAttachmentConfig vfx)
	{
		if (string.IsNullOrWhiteSpace(vfx.VfxId)) return;
		Templates ??= new();
		Templates.Vfx ??= new();
		int idx = Templates.Vfx.FindIndex(v => string.Equals(v.VfxId, vfx.VfxId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) Templates.Vfx[idx] = vfx;
		else Templates.Vfx.Add(vfx);
	}

	public bool RemoveVfx(string vfxId)
	{
		if (Templates?.Vfx == null || string.IsNullOrWhiteSpace(vfxId)) return false;
		return Templates.Vfx.RemoveAll(v => string.Equals(v.VfxId, vfxId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateItem(ItemMetadata item)
	{
		if (string.IsNullOrWhiteSpace(item.TemplateID)) return;
		Templates ??= new();
		Templates.Items ??= new();
		int idx = Templates.Items.FindIndex(i => string.Equals(i.TemplateID, item.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) Templates.Items[idx] = item;
		else Templates.Items.Add(item);
	}

	public bool RemoveItem(string objectId)
	{
		if (Templates?.Items == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return Templates.Items.RemoveAll(i => string.Equals(i.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public void AddOrUpdateUpgrade(UpgradeMetadata upgrade)
	{
		if (string.IsNullOrWhiteSpace(upgrade.TemplateID)) return;
		Templates ??= new();
		Templates.Upgrades ??= new();
		int idx = Templates.Upgrades.FindIndex(u => string.Equals(u.TemplateID, upgrade.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0) Templates.Upgrades[idx] = upgrade;
		else Templates.Upgrades.Add(upgrade);
	}

	public bool RemoveUpgrade(string objectId)
	{
		if (Templates?.Upgrades == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return Templates.Upgrades.RemoveAll(u => string.Equals(u.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
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

	public bool UpdateUnit(string objectId, Func<UnitMetadata, UnitMetadata> update)
	{
		if (Templates?.Units == null || string.IsNullOrWhiteSpace(objectId)) return false;
		int idx = Templates.Units.FindIndex(u => string.Equals(u.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			Templates.Units[idx] = update(Templates.Units[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateBuilding(string objectId, Func<UnitMetadata, UnitMetadata> update)
	{
		if (Templates?.Buildings == null || string.IsNullOrWhiteSpace(objectId)) return false;
		int idx = Templates.Buildings.FindIndex(b => string.Equals(b.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			Templates.Buildings[idx] = update(Templates.Buildings[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateProp(string objectId, Func<PropMetadata, PropMetadata> update)
	{
		if (Templates?.Props == null || string.IsNullOrWhiteSpace(objectId)) return false;
		int idx = Templates.Props.FindIndex(p => string.Equals(p.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			Templates.Props[idx] = update(Templates.Props[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateResource(string objectId, Func<ResourceMetadata, ResourceMetadata> update)
	{
		if (Templates?.Resources == null || string.IsNullOrWhiteSpace(objectId)) return false;
		int idx = Templates.Resources.FindIndex(r => string.Equals(r.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			Templates.Resources[idx] = update(Templates.Resources[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateAbility(string objectId, Func<AbilityMetadata, AbilityMetadata> update)
	{
		if (Templates?.Abilities == null || string.IsNullOrWhiteSpace(objectId)) return false;
		int idx = Templates.Abilities.FindIndex(a => string.Equals(a.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			Templates.Abilities[idx] = update(Templates.Abilities[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateWeapon(string objectId, Func<WeaponMetadata, WeaponMetadata> update)
	{
		if (Templates?.Weapons == null || string.IsNullOrWhiteSpace(objectId)) return false;
		int idx = Templates.Weapons.FindIndex(w => string.Equals(w.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			Templates.Weapons[idx] = update(Templates.Weapons[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateAttachment(string attachmentId, Func<AttachmentMetadata, AttachmentMetadata> update)
	{
		if (Templates?.Attachments == null || string.IsNullOrWhiteSpace(attachmentId)) return false;
		int idx = Templates.Attachments.FindIndex(a => string.Equals(a.AttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			Templates.Attachments[idx] = update(Templates.Attachments[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateVfx(string vfxId, Func<VfxAttachmentConfig, VfxAttachmentConfig> update)
	{
		if (Templates?.Vfx == null || string.IsNullOrWhiteSpace(vfxId)) return false;
		int idx = Templates.Vfx.FindIndex(v => string.Equals(v.VfxId, vfxId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			Templates.Vfx[idx] = update(Templates.Vfx[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateItem(string objectId, Func<ItemMetadata, ItemMetadata> update)
	{
		if (Templates?.Items == null || string.IsNullOrWhiteSpace(objectId)) return false;
		int idx = Templates.Items.FindIndex(i => string.Equals(i.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			Templates.Items[idx] = update(Templates.Items[idx]);
			return true;
		}
		return false;
	}

	public bool UpdateUpgrade(string objectId, Func<UpgradeMetadata, UpgradeMetadata> update)
	{
		if (Templates?.Upgrades == null || string.IsNullOrWhiteSpace(objectId)) return false;
		int idx = Templates.Upgrades.FindIndex(u => string.Equals(u.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
		{
			Templates.Upgrades[idx] = update(Templates.Upgrades[idx]);
			return true;
		}
		return false;
	}

	public ModelMetadata GetOrCreateModel(string modelKey)
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

	public void SetModelNormalizeLuminance(string modelKey, bool normalizeLuminance)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).NormalizeLuminance = normalizeLuminance;
	}

	public void SetModelIgnorePlayerColor(string modelKey, bool ignorePlayerColor)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).IgnorePlayerColor = ignorePlayerColor;
	}

	public void SetModelSpawnShader(string modelKey, string spawnShader)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).SpawnShaders = string.IsNullOrWhiteSpace(spawnShader) ? null : spawnShader;
	}

	public void SetModelDeathShader(string modelKey, string deathShader)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).DeathShaders = string.IsNullOrWhiteSpace(deathShader) ? null : deathShader;
	}

	public void SetModelProceduralAnimation(string modelKey, string animId)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).ProceduralAnimation = string.IsNullOrWhiteSpace(animId) ? null : animId;
	}

	public string? GetModelProceduralAnimation(string modelKey)
	{
		if (string.IsNullOrWhiteSpace(modelKey) || Models == null) return null;
		return Models.TryGetValue(modelKey, out var model) ? model.ProceduralAnimation : null;
	}

	public void SetModelEnableProceduralAnimation(string modelKey, bool enable)
	{
		if (string.IsNullOrWhiteSpace(modelKey)) return;
		GetOrCreateModel(modelKey).EnableProceduralAnimation = enable ? true : null;
	}

	public bool GetModelEnableProceduralAnimation(string modelKey)
	{
		if (string.IsNullOrWhiteSpace(modelKey) || Models == null) return false;
		return Models.TryGetValue(modelKey, out var model) && (model.EnableProceduralAnimation ?? false);
	}

	public void RemoveModelOverrides(string modelKey)
	{
		if (string.IsNullOrWhiteSpace(modelKey) || Models == null) return;
		Models.Remove(modelKey);
	}

	public static string GenerateJsonSchema()
	{
		return RealmJsonSchemaExporter.GenerateJsonSchema(typeof(MapMetadata));
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

public class UnitMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public float MaxHp { get; set; }
	public float Damage { get; set; }
	public float Range { get; set; }
	public float Armor { get; set; }
	public float Speed { get; set; }
	public float AttackCooldown { get; set; }
	public float ScanRadius { get; set; }
	public float CostGold { get; set; }
	public float CostWood { get; set; }
	public float CostStone { get; set; }
	public int PopCost { get; set; }
	public float ProductionTime { get; set; }
	public string? AttackType { get; set; }
	public string? ArmorType { get; set; }
	public float Strength { get; set; }
	public float Agility { get; set; }
	public float Vitality { get; set; }
	public float Intelligence { get; set; }
	public float Wisdom { get; set; }
	public float Fortune { get; set; }
	public float HpRegen { get; set; }
	public float HpRegenCombatDelay { get; set; }
	public float MaxMana { get; set; }
	public float ManaRegen { get; set; }
	public float RatedArmor { get; set; }
	public float FlatArmorPenetration { get; set; }
	public float PercentArmorPenetration { get; set; }
	public float DamageVariance { get; set; }
	public string DamageType { get; set; } = "normal";
	public float CritChance { get; set; }
	public float CritMultiplier { get; set; } = 1.0f;
	public string SplashType { get; set; } = "None";
	public float SplashInnerRadius { get; set; }
	public float SplashMediumRadius { get; set; }
	public float SplashOuterRadius { get; set; }
	public float SplashInnerRatio { get; set; } = 1.0f;
	public float SplashMediumRatio { get; set; } = 0.5f;
	public float SplashOuterRatio { get; set; } = 0.25f;
	public bool FriendlyFire { get; set; }
	public int PushPriority { get; set; }
	public string MovementType { get; set; } = "Ground";
	public float SightRange { get; set; } = 15.0f;
	public float AcquisitionRange { get; set; } = 15.0f;
	public float GoldBounty { get; set; }
	public string? ModelPath { get; set; }
	public string? PortraitModelPath { get; set; }
	public string VisualMode { get; set; } = "GroundPlane";
	public float Scale { get; set; } = 1.0f;
	public float YOffset { get; set; }
	public float CollisionCircle { get; set; }
	public float Brightness { get; set; } = 0.5f;
	public string? Tint { get; set; }
	public bool NormalizeLuminance { get; set; } = true;
	public bool IgnorePlayerColor { get; set; }
	public bool DespillPlayerColor { get; set; } = false;
	public string[]? BuildOptions { get; set; }
	public bool IsHero { get; set; }
	public string[]? Abilities { get; set; }
	public float XpBounty { get; set; }
	public string[]? PathingCapabilities { get; set; }
	public int PathingType { get; set; }
	public float? ObstacleRadius { get; set; }
	public string[]? Targets { get; set; }
	public string[]? Weapons { get; set; }
	public string? ProjectileModelPath { get; set; }
	public Dictionary<string, List<UnitAnimationEntry>>? Animations { get; set; }
	public UnitObjectAttachments? ObjectAttachments { get; set; }
	public UnitSoundsMetadata? Sounds { get; set; }
	public string[]? StartingItems { get; set; }
	public string[]? Upgrades { get; set; }
	public string[]? StatusEffects { get; set; }
	public string[]? SoundEvents { get; set; }
	public string? SpawnShader { get; set; }
	public string? DeathShader { get; set; }
	public string? DespawnShader
	{
		get => DeathShader;
		set => DeathShader = value;
	}

	public bool TryGetObjectAttachment(HumanoidBone hand, string attachmentId, out HandAttachmentOrientation? orientation)
	{
		if (ObjectAttachments != null)
		{
			return ObjectAttachments.TryGetOrientation(hand, attachmentId, out orientation);
		}
		orientation = default;
		return false;
	}

	public bool TryGetObjectAttachment(string socket, string attachmentId, out HandAttachmentOrientation? orientation)
	{
		if (ObjectAttachments != null)
		{
			return ObjectAttachments.TryGetSocketOrientation(socket, attachmentId, out orientation);
		}
		orientation = default;
		return false;
	}

	public void SetObjectAttachment(HumanoidBone hand, string attachmentId, HandAttachmentOrientation orientation)
	{
		ObjectAttachments ??= new UnitObjectAttachments();
		ObjectAttachments.SetOrientation(hand, attachmentId, orientation);
	}

	public void SetObjectAttachment(string socket, string attachmentId, HandAttachmentOrientation orientation)
	{
		ObjectAttachments ??= new UnitObjectAttachments();
		ObjectAttachments.SetSocketOrientation(socket, attachmentId, orientation);
	}

	public bool RemoveObjectAttachment(string socket, string attachmentId, string? parentAttachmentId = null)
	{
		if (ObjectAttachments != null)
		{
			return ObjectAttachments.RemoveSocketAttachment(socket, attachmentId, parentAttachmentId);
		}
		return false;
	}
}

public class PropMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public string? ModelPath { get; set; }
	public string? PortraitModelPath { get; set; }
	public string VisualMode { get; set; } = "GroundPlane";
	public float Scale { get; set; } = 1.25f;
	public float YOffset { get; set; }
	public float CollisionCircle { get; set; }
	public float Brightness { get; set; } = 0.5f;
	public string? Tint { get; set; }
	public bool NormalizeLuminance { get; set; } = true;
	public bool IgnorePlayerColor { get; set; } = true;
	public bool DespillPlayerColor { get; set; } = false;
	public int PathingType { get; set; }
	public string? SpawnShader { get; set; }
	public string? DeathShader { get; set; }
	public string? DespawnShader
	{
		get => DeathShader;
		set => DeathShader = value;
	}
}

public class ResourceMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public string? ModelPath { get; set; }
	public string? PortraitModelPath { get; set; }
	public string VisualMode { get; set; } = "GroundPlane";
	public float MaxCapacity { get; set; }
	public float HarvestRate { get; set; }
	public float GrowthRate { get; set; }
	public int MaxWorkers { get; set; }
	public float Scale { get; set; } = 2.75f;
	public float YOffset { get; set; }
	public float CollisionCircle { get; set; }
	public float Brightness { get; set; } = 0.5f;
	public string? Tint { get; set; }
	public bool NormalizeLuminance { get; set; } = true;
	public bool IgnorePlayerColor { get; set; } = true;
	public bool DespillPlayerColor { get; set; } = false;
	public int PathingType { get; set; }
	public string? SpawnShader { get; set; }
	public string? DeathShader { get; set; }
	public string? DespawnShader
	{
		get => DeathShader;
		set => DeathShader = value;
	}
}

public class AbilityMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string AbilityType { get; set; } = "target_spell";
	public string? IconPath { get; set; }
	public float ManaCost { get; set; }
	public float Cooldown { get; set; }
	public float TargetRange { get; set; }
	public string? VisualEffect { get; set; }
	public string? CastSound { get; set; }
	public string[]? AppliedStatusEffects { get; set; }
	public float AreaOfEffectRadius { get; set; }
	public float Damage { get; set; }
	public float Healing { get; set; }
	public string? SummonedUnitId { get; set; }
	public int SummonCount { get; set; }
	public float SummonDuration { get; set; }
}

public class WeaponMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public float Damage { get; set; }
	public float Range { get; set; }
	public float AttackCooldown { get; set; }
	public string? AttackType { get; set; }
	public float ProjectileSpeed { get; set; }
	public string? VisualEffect { get; set; }
	public string? AttackSound { get; set; }
	public string? ProjectileModelPath { get; set; }
	public string? ImpactVisualEffect { get; set; }
	public string? ImpactSound { get; set; }

	public float ArcHeight { get; set; }
	public float HomingWeight { get; set; }
	public float TurnRateLimit { get; set; }
	public string? EaseCurve { get; set; }
	public string? SpeedCurve { get; set; }
	public float Acceleration { get; set; }
	public float MaxLifetime { get; set; }
	public float FailsafeRange { get; set; }
	public string? ScaleCurve { get; set; }
	public Vector3Data? TumbleAngularVelocity { get; set; }
	public bool OrientToTrajectory { get; set; } = true;
	public string ForwardAxisPreset { get; set; } = "-Z";
	public Vector3Data? MeshRotationOffset { get; set; }
	public Vector3Data? MeshTranslationOffset { get; set; }
	public Vector3Data? MeshScaleOffset { get; set; }
	public float SpiralRadius { get; set; }
	public float SpiralFrequency { get; set; }
	public float ZigzagAmplitude { get; set; }
	public float ZigzagFrequency { get; set; }
	public int MaxBounces { get; set; }
	public int PierceCount { get; set; }

	public string? ShaderEffectType { get; set; }
	public string EmissionMaskSource { get; set; } = "noise";
	public string? BaseColor { get; set; }
	public string? EmissionColor { get; set; }
	public float EmissionEnergy { get; set; } = 4f;
	public float FresnelPower { get; set; } = 3f;
	public string? FresnelColor { get; set; }
	public float FresnelFactor { get; set; } = 1.5f;
	public float NoiseScale { get; set; } = 3f;
	public string? NoiseTexture { get; set; }
	public Vector2Data? UvScrollSpeed1 { get; set; }
	public Vector2Data? UvScrollSpeed2 { get; set; }
	public float ThresholdCutoff { get; set; } = 0.5f;
	public float ThresholdSmoothness { get; set; } = 0.1f;

	public bool PointLightEnabled { get; set; }
	public string? PointLightColor { get; set; }
	public float PointLightIntensity { get; set; } = 2.0f;
	public float PointLightRange { get; set; } = 6.0f;

	public string? RibbonTexture { get; set; }
	public string? RibbonColor { get; set; }
	public float RibbonWidth { get; set; } = 0.4f;
	public float RibbonLifetime { get; set; } = 0.5f;
	public bool RibbonTaper { get; set; } = true;
	public bool RibbonAdditive { get; set; } = true;
	public float RibbonScrollSpeed { get; set; }
	public Vector3Data? TrailOffset { get; set; }
}

public class UpgradeMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string? IconPath { get; set; }
	public float CostGold { get; set; }
	public float CostWood { get; set; }
	public float CostStone { get; set; }
	public float ResearchTime { get; set; }
	public string Requirement { get; set; } = string.Empty;
	public int MaxLevel { get; set; } = 1;
	public string[]? AffectedUnitIds { get; set; }
	public float MaxHpBonus { get; set; }
	public float DamageBonus { get; set; }
	public float ArmorBonus { get; set; }
	public float SpeedBonus { get; set; }
}

public class ItemMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string ItemClass { get; set; } = "consumable";
	public float CostGold { get; set; }
	public string? UseAbility { get; set; }
	public int ChargeCount { get; set; }
	public string? CooldownLink { get; set; }
	public bool CanDrop { get; set; }
	public int ItemLevel { get; set; }
	public string? IconPath { get; set; }
	public string[]? PassiveStatusEffects { get; set; }
	public string[]? GrantedWeapons { get; set; }
	public bool IsContainer { get; set; }
	public int ContainerSize { get; set; }
	public string Requirements { get; set; } = string.Empty;
}

public class AttachmentMetadata
{
	public string AttachmentId { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string ModelPath { get; set; } = string.Empty;
	public float Scale { get; set; } = 1.0f;
	public Vector3Data PositionOffset { get; set; }
	public Vector3Data RotationOffset { get; set; }
	public string DefaultHand { get; set; } = "RightHand";
	public string? ChildVfxId { get; set; }
	public Vector3Data ChildVfxPosition { get; set; }
	public Vector3Data ChildVfxRotation { get; set; }
	public Vector3Data ChildVfxScale { get; set; } = Vector3Data.One;
}

public class TextureMetadata
{
	public string Hash { get; set; } = string.Empty;
	public int SwatchIndex { get; set; }
	public float ScaleFactor { get; set; }
	public string? AssetType { get; set; }
	public int TextureSize { get; set; }
	public string? NoiseConfig { get; set; }
	public float Brightness { get; set; }
	public string? Tint { get; set; }
	public float RoughnessScale { get; set; }
	public float NormalScale { get; set; }
	public float HeightScale { get; set; }
	public float HeightOffset { get; set; }
	public float CrevicePower { get; set; }
	public string? TileMode { get; set; }
	public float UvScale { get; set; }
	public float StochasticTileSize { get; set; }
	public float CrossFade { get; set; }
	public float Contrast { get; set; }
	public float Saturation { get; set; }
	public float Specular { get; set; }
	public float Roughness { get; set; }
	public float Metallic { get; set; }
}

public class DecalMetadata
{
	public string Hash { get; set; } = string.Empty;
	public string? TexturePath { get; set; }
	public string? Tint { get; set; }
	public float Brightness { get; set; }
	public float Contrast { get; set; }
	public float Saturation { get; set; }
	public float Opacity { get; set; }
	public float AlbedoMix { get; set; }
	public float NormalStrength { get; set; }
	public float Roughness { get; set; }
	public float Metallic { get; set; }
	public string? BlendMode { get; set; }
	public string? AssetType { get; set; }
	public string? TextureNormal { get; set; }
	public string? TextureOrm { get; set; }
	public string? TextureEmission { get; set; }
	public float EmissionEnergy { get; set; }
	public bool AnimateOpacity { get; set; }
	public float OpacityPulseSpeed { get; set; }
	public float MinOpacity { get; set; }
	public float MaxOpacity { get; set; }
	public bool AnimateEmission { get; set; }
	public float EmissionPulseSpeed { get; set; }
	public float MinEmission { get; set; }
	public float MaxEmission { get; set; }
	public bool AnimateScale { get; set; }
	public float ScalePulseSpeed { get; set; }
	public float MinScaleRatio { get; set; }
	public float MaxScaleRatio { get; set; }
	public float UpperFade { get; set; }
	public float LowerFade { get; set; }
}

public class VfxMetadata
{
	public string Hash { get; set; } = string.Empty;
	public int Columns { get; set; }
	public int Rows { get; set; }
	public float Fps { get; set; }
	public bool SubframeBlend { get; set; }
	public string? AssetType { get; set; }
}

public class GlbItemMetadata
{
	public string Hash { get; set; } = string.Empty;
	public string? DefaultAssetType { get; set; }
	public float MinY { get; set; }
	public float YOffset { get; set; }
	public float Scale { get; set; }
	public float CollisionCircleRatio { get; set; }
	public float CollisionRadius { get; set; }
	public float Brightness { get; set; }
	public float Contrast { get; set; }
	public float Saturation { get; set; }
	public bool NormalizeLuminance { get; set; }
	public bool DespillPlayerColor { get; set; }
	public float RotX { get; set; }
	public float RotY { get; set; }
	public float RotZ { get; set; }
	public object? WeaponLayers { get; set; }
	public string? WeaponPreset { get; set; }
	public string? WeaponRibbon { get; set; }
	public bool IgnorePlayerColor { get; set; }
	public string? TeamColorMask { get; set; }
	public string? SpawnShader { get; set; }
	public string? DeathShader { get; set; }
	public string? DespawnShader
	{
		get => DeathShader;
		set => DeathShader = value;
	}
}

public class IconMetadata
{
	public string Hash { get; set; } = string.Empty;
}

public class SkyboxMetadata
{
	public string Hash { get; set; } = string.Empty;
}

public class RibbonMetadata
{
	public string Hash { get; set; } = string.Empty;
}

public class ShaderMetadata
{
	public string Hash { get; set; } = string.Empty;
	public string? ConfigJson { get; set; }
}

public class HandAttachmentOrientation
{
	public float PositionX { get; set; }
	public float PositionY { get; set; }
	public float PositionZ { get; set; }
	public float PitchX { get; set; }
	public float YawY { get; set; }
	public float RollZ { get; set; }
	public float Scale { get; set; }
	public float ScaleX { get; set; }
	public float ScaleY { get; set; }
	public float ScaleZ { get; set; }
	public float NormalOffset { get; set; }
	public string? ParentAttachmentId { get; set; }

	[JsonIgnore]
	public Vector3Data Position => new(PositionX, PositionY, PositionZ);
	[JsonIgnore]
	public Vector3Data RotationDegrees => new(PitchX, YawY, RollZ);
	[JsonIgnore]
	public Vector3Data ScaleVector => new(
		ScaleX > 0.0001f ? ScaleX : (Scale > 0f ? Scale : 1.0f),
		ScaleY > 0.0001f ? ScaleY : (Scale > 0f ? Scale : 1.0f),
		ScaleZ > 0.0001f ? ScaleZ : (Scale > 0f ? Scale : 1.0f));

	public HandAttachmentOrientation Clone() => new()
	{
		PositionX = PositionX,
		PositionY = PositionY,
		PositionZ = PositionZ,
		PitchX = PitchX,
		YawY = YawY,
		RollZ = RollZ,
		Scale = Scale,
		ScaleX = ScaleX,
		ScaleY = ScaleY,
		ScaleZ = ScaleZ,
		NormalOffset = NormalOffset,
		ParentAttachmentId = ParentAttachmentId
	};
}

public class UnitObjectAttachments
{
	public List<Dictionary<string, HandAttachmentOrientation>>? right_hand { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? left_hand { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? chest { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? root { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? head { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? left_foot { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? right_foot { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? ground { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? center { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? overhead { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? pivot { get; set; }

	public bool HasAny() => (right_hand?.Count > 0) || (left_hand?.Count > 0) || (chest?.Count > 0) || (root?.Count > 0) || (head?.Count > 0) || (left_foot?.Count > 0) || (right_foot?.Count > 0) || (ground?.Count > 0) || (center?.Count > 0) || (overhead?.Count > 0) || (pivot?.Count > 0);

	public List<Dictionary<string, HandAttachmentOrientation>>? GetSocketList(string socket)
	{
		if (string.IsNullOrEmpty(socket)) return right_hand;
		string s = socket.ToLowerInvariant().Replace("_", "").Replace(" ", "");
		return s switch
		{
			"ground" or "footprint" or "base" => ground,
			"center" or "centerofmass" => center,
			"overhead" or "top" or "crown" or "roof" => overhead,
			"pivot" or "origin" => pivot,
			"root" or "hips" => root,
			"chest" or "spine" => chest,
			"head" => head,
			"lefthand" => left_hand,
			"righthand" => right_hand,
			"leftfoot" => left_foot,
			"rightfoot" => right_foot,
			_ => right_hand
		};
	}

	public void SetSocketList(string socket, List<Dictionary<string, HandAttachmentOrientation>> list)
	{
		string s = (socket ?? "righthand").ToLowerInvariant().Replace("_", "").Replace(" ", "");
		switch (s)
		{
			case "ground":
			case "footprint":
			case "base":
				ground = list;
				break;
			case "center":
			case "centerofmass":
				center = list;
				break;
			case "overhead":
			case "top":
			case "crown":
			case "roof":
				overhead = list;
				break;
			case "pivot":
			case "origin":
				pivot = list;
				break;
			case "root":
			case "hips":
				root = list;
				break;
			case "chest":
			case "spine":
				chest = list;
				break;
			case "head":
				head = list;
				break;
			case "lefthand":
				left_hand = list;
				break;
			case "righthand":
				right_hand = list;
				break;
			case "leftfoot":
				left_foot = list;
				break;
			case "rightfoot":
				right_foot = list;
				break;
			default:
				right_hand = list;
				break;
		}
	}

	public List<Dictionary<string, HandAttachmentOrientation>>? GetBoneList(HumanoidBone bone)
	{
		return bone switch
		{
			HumanoidBone.LeftHand => left_hand,
			HumanoidBone.RightHand => right_hand,
			HumanoidBone.Chest or HumanoidBone.Spine => chest,
			HumanoidBone.Hips => root,
			HumanoidBone.Head => head,
			HumanoidBone.LeftFoot => left_foot,
			HumanoidBone.RightFoot => right_foot,
			_ => right_hand
		};
	}

	public void SetBoneList(HumanoidBone bone, List<Dictionary<string, HandAttachmentOrientation>> list)
	{
		switch (bone)
		{
			case HumanoidBone.LeftHand: left_hand = list; break;
			case HumanoidBone.RightHand: right_hand = list; break;
			case HumanoidBone.Chest:
			case HumanoidBone.Spine: chest = list; break;
			case HumanoidBone.Hips: root = list; break;
			case HumanoidBone.Head: head = list; break;
			case HumanoidBone.LeftFoot: left_foot = list; break;
			case HumanoidBone.RightFoot: right_foot = list; break;
			default: right_hand = list; break;
		}
	}

	public bool TryGetOrientation(HumanoidBone hand, string attachmentId, out HandAttachmentOrientation? orientation)
	{
		var list = GetBoneList(hand);
		if (list != null && !string.IsNullOrEmpty(attachmentId))
		{
			string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
				? attachmentId
				: Path.GetFileNameWithoutExtension(attachmentId);
			foreach (var dict in list)
			{
				if (dict != null)
				{
					foreach (var kvp in dict)
					{
						if (kvp.Key.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
							kvp.Key.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
							Path.GetFileNameWithoutExtension(kvp.Key).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
						{
							orientation = kvp.Value;
							return true;
						}
					}
				}
			}
		}
		orientation = default;
		return false;
	}

	public void SetOrientation(HumanoidBone hand, string attachmentId, HandAttachmentOrientation orientation)
	{
		string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: Path.GetFileNameWithoutExtension(attachmentId);
		var list = GetBoneList(hand);
		if (list == null)
		{
			list = new List<Dictionary<string, HandAttachmentOrientation>>();
			SetBoneList(hand, list);
		}
		UpdateList(list, cleanId, orientation);
	}

	public bool TryGetSocketOrientation(string socket, string attachmentId, out HandAttachmentOrientation? orientation)
	{
		var list = GetSocketList(socket);
		if (list != null && !string.IsNullOrEmpty(attachmentId))
		{
			string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
				? attachmentId
				: Path.GetFileNameWithoutExtension(attachmentId);
			foreach (var dict in list)
			{
				if (dict != null)
				{
					foreach (var kvp in dict)
					{
						if (kvp.Key.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
							kvp.Key.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
							Path.GetFileNameWithoutExtension(kvp.Key).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
						{
							orientation = kvp.Value;
							return true;
						}
					}
				}
			}
		}
		orientation = default;
		return false;
	}

	public void SetSocketOrientation(string socket, string attachmentId, HandAttachmentOrientation orientation)
	{
		string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: Path.GetFileNameWithoutExtension(attachmentId);
		var list = GetSocketList(socket);
		if (list == null)
		{
			list = new List<Dictionary<string, HandAttachmentOrientation>>();
			SetSocketList(socket, list);
		}
		UpdateList(list, cleanId, orientation);
	}

	private static void UpdateList(List<Dictionary<string, HandAttachmentOrientation>> list, string attachmentId, HandAttachmentOrientation orientation)
	{
		foreach (var dict in list)
		{
			if (dict != null)
			{
				foreach (var key in dict.Keys.ToList())
				{
					if (key.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
						Path.GetFileNameWithoutExtension(key).Equals(attachmentId, StringComparison.OrdinalIgnoreCase))
					{
						if (string.Equals(dict[key].ParentAttachmentId, orientation.ParentAttachmentId, StringComparison.OrdinalIgnoreCase))
						{
							dict[key] = orientation;
							return;
						}
					}
				}
			}
		}
		list.Add(new Dictionary<string, HandAttachmentOrientation>(StringComparer.OrdinalIgnoreCase)
		{
			[attachmentId] = orientation
		});
	}

	public bool RemoveSocketAttachment(string socket, string attachmentId, string? parentAttachmentId = null)
	{
		var list = GetSocketList(socket);
		if (list == null || string.IsNullOrEmpty(attachmentId)) return false;
		string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: Path.GetFileNameWithoutExtension(attachmentId);
		bool removed = false;
		for (int i = list.Count - 1; i >= 0; i--)
		{
			var dict = list[i];
			if (dict != null)
			{
				var keysToRemove = dict.Keys.Where(k =>
				{
					bool keyMatch = k.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
						k.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
						Path.GetFileNameWithoutExtension(k).Equals(cleanId, StringComparison.OrdinalIgnoreCase);

					if (!string.IsNullOrEmpty(parentAttachmentId))
					{
						return keyMatch && string.Equals(dict[k].ParentAttachmentId, parentAttachmentId, StringComparison.OrdinalIgnoreCase);
					}

					bool isChildOfThis = dict[k].ParentAttachmentId != null &&
						(dict[k].ParentAttachmentId!.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
						 dict[k].ParentAttachmentId!.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
						 Path.GetFileNameWithoutExtension(dict[k].ParentAttachmentId!).Equals(cleanId, StringComparison.OrdinalIgnoreCase));

					return (keyMatch && string.IsNullOrEmpty(dict[k].ParentAttachmentId)) || isChildOfThis;
				}).ToList();

				foreach (var k in keysToRemove)
				{
					dict.Remove(k);
					removed = true;
				}
				if (dict.Count == 0)
				{
					list.RemoveAt(i);
				}
			}
		}
		return removed;
	}

	public UnitObjectAttachments Clone()
	{
		static List<Dictionary<string, HandAttachmentOrientation>>? CloneList(List<Dictionary<string, HandAttachmentOrientation>>? src)
		{
			if (src == null) return null;
			var res = new List<Dictionary<string, HandAttachmentOrientation>>(src.Count);
			foreach (var dict in src)
			{
				if (dict == null) continue;
				var d = new Dictionary<string, HandAttachmentOrientation>(dict.Count, StringComparer.OrdinalIgnoreCase);
				foreach (var kvp in dict)
				{
					d[kvp.Key] = kvp.Value.Clone();
				}
				res.Add(d);
			}
			return res;
		}

		return new UnitObjectAttachments
		{
			right_hand = CloneList(right_hand),
			left_hand = CloneList(left_hand),
			chest = CloneList(chest),
			root = CloneList(root),
			head = CloneList(head),
			left_foot = CloneList(left_foot),
			right_foot = CloneList(right_foot),
			ground = CloneList(ground),
			center = CloneList(center),
			overhead = CloneList(overhead),
			pivot = CloneList(pivot)
		};
	}
}

[JsonConverter(typeof(UnitAnimationEntryJsonConverter))]
public struct UnitAnimationEntry
{
	public string Animation { get; set; }
	public string? RightHandAttachment { get; set; }
	public string? LeftHandAttachment { get; set; }
}

public class UnitAnimationEntryJsonConverter : JsonConverter<UnitAnimationEntry>
{
	public override UnitAnimationEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			return new UnitAnimationEntry
			{
				Animation = reader.GetString() ?? string.Empty
			};
		}

		if (reader.TokenType == JsonTokenType.StartObject)
		{
			using var doc = JsonDocument.ParseValue(ref reader);
			var root = doc.RootElement;
			string anim = string.Empty;
			string? right = null;
			string? left = null;

			foreach (var prop in root.EnumerateObject())
			{
				if (prop.Name.Equals("Animation", StringComparison.OrdinalIgnoreCase) ||
					prop.Name.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
					prop.Name.Equals("Path", StringComparison.OrdinalIgnoreCase))
				{
					anim = prop.Value.GetString() ?? string.Empty;
				}
				else if (prop.Name.Equals("RightHandAttachment", StringComparison.OrdinalIgnoreCase) ||
						 prop.Name.Equals("RightHand", StringComparison.OrdinalIgnoreCase) ||
						 prop.Name.Equals("AttachmentRight", StringComparison.OrdinalIgnoreCase))
				{
					right = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetString();
				}
				else if (prop.Name.Equals("LeftHandAttachment", StringComparison.OrdinalIgnoreCase) ||
						 prop.Name.Equals("LeftHand", StringComparison.OrdinalIgnoreCase) ||
						 prop.Name.Equals("AttachmentLeft", StringComparison.OrdinalIgnoreCase))
				{
					left = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetString();
				}
			}

			return new UnitAnimationEntry
			{
				Animation = anim,
				RightHandAttachment = right,
				LeftHandAttachment = left
			};
		}

		return default;
	}

	public override void Write(Utf8JsonWriter writer, UnitAnimationEntry value, JsonSerializerOptions options)
	{
		if (string.IsNullOrEmpty(value.RightHandAttachment) && string.IsNullOrEmpty(value.LeftHandAttachment))
		{
			writer.WriteStringValue(value.Animation ?? string.Empty);
		}
		else
		{
			writer.WriteStartObject();
			writer.WriteString("Animation", value.Animation ?? string.Empty);
			if (value.RightHandAttachment != null)
			{
				writer.WriteString("RightHandAttachment", value.RightHandAttachment);
			}
			else
			{
				writer.WriteNull("RightHandAttachment");
			}
			if (value.LeftHandAttachment != null)
			{
				writer.WriteString("LeftHandAttachment", value.LeftHandAttachment);
			}
			else
			{
				writer.WriteNull("LeftHandAttachment");
			}
			writer.WriteEndObject();
		}
	}
}

public class UnitSoundsMetadata
{
	public string[]? OnSelect { get; set; }
	public string[]? OnMoveOrder { get; set; }
	public string[]? OnAttackOrder { get; set; }
	public string[]? OnWounded { get; set; }
	public string[]? OnDeath { get; set; }
	public string[]? OnReady { get; set; }
	public string[]? OnSpellCast { get; set; }
}
