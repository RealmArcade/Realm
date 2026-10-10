using System.Text.Json;
using System.Text.Json.Serialization;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

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
	public Dictionary<string, SpawnShaderMetadata> SpawnShaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

	public TextureMetadata? GetTerrainTexture(string swatchOrKey)
	{
		if (Textures == null || string.IsNullOrWhiteSpace(swatchOrKey)) return null;
		if (Textures.TryGetValue(swatchOrKey, out var tex) && tex != null) return tex;
		return FindTextureByCleanName(swatchOrKey);
	}

	private TextureMetadata? FindTextureByCleanName(string swatchOrKey)
	{
		string clean = Path.GetFileNameWithoutExtension(swatchOrKey);
		string normalized = clean.StartsWith("terrain/", StringComparison.OrdinalIgnoreCase) ? clean : $"terrain/{clean}";
		if (Textures!.TryGetValue(normalized, out var tex) && tex != null) return tex;
		return Textures.FirstOrDefault(kvp => IsTextureMatch(kvp, clean)).Value;
	}

	private bool IsTextureMatch(KeyValuePair<string, TextureMetadata> kvp, string clean)
	{
		if (string.Equals(kvp.Key, clean, StringComparison.OrdinalIgnoreCase)) return true;
		if (string.Equals(Path.GetFileNameWithoutExtension(kvp.Key), clean, StringComparison.OrdinalIgnoreCase)) return true;
		return string.Equals(Path.GetFileNameWithoutExtension(kvp.Value.TexturePath ?? ""), clean, StringComparison.OrdinalIgnoreCase);
	}

	public TerrainSwatchProfileData? GetTerrainProfile(string swatchName)
	{
		var tex = GetTerrainTexture(swatchName);
		if (tex == null) return null;
		return new TerrainSwatchProfileData
		{
			DefaultPathingCode = tex.DefaultPathingCode,
			DecalBombingRules = tex.DecalBombingRules,
			VfxBombingRules = tex.VfxBombingRules
		};
	}

	public void AddOrUpdateTerrainProfile(string keyOrSwatch, TerrainSwatchProfileData profile)
	{
		if (string.IsNullOrWhiteSpace(keyOrSwatch)) return;
		Textures ??= new(StringComparer.OrdinalIgnoreCase);
		string clean = Path.GetFileNameWithoutExtension(keyOrSwatch);
		string normalized = clean.StartsWith("terrain/", StringComparison.OrdinalIgnoreCase) ? clean : $"terrain/{clean}";
		if (!Textures.TryGetValue(normalized, out var tex) || tex == null)
		{
			tex = GetTerrainTexture(keyOrSwatch) ?? new TextureMetadata
			{
				TexturePath = keyOrSwatch.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? keyOrSwatch : $"{clean}.rtex",
				TileMode = "Stochastic",
				UvScale = 1.0f,
				Brightness = 1.0f
			};
			Textures[normalized] = tex;
		}
		tex.DefaultPathingCode = profile.DefaultPathingCode;
		tex.DecalBombingRules = profile.DecalBombingRules ?? new();
		tex.VfxBombingRules = profile.VfxBombingRules ?? new();
	}

	public bool RemoveTerrainProfile(string swatchName)
	{
		if (Textures == null || string.IsNullOrWhiteSpace(swatchName)) return false;
		string clean = Path.GetFileNameWithoutExtension(swatchName);
		string normalized = clean.StartsWith("terrain/", StringComparison.OrdinalIgnoreCase) ? clean : $"terrain/{clean}";
		bool removed = Textures.Remove(normalized);
		removed |= Textures.Remove(swatchName);
		removed |= Textures.Remove(clean);
		return removed;
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