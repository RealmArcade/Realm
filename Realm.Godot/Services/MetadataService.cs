using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Godot;
using Realm.Godot.Utils;
using Realm.Shared.Metadata;
using Realm.Shared.Terrain;

namespace Realm.Godot.Services;

public class MapValidationResult
{
	public bool IsValid => Errors.Count == 0;
	public List<string> Errors { get; } = new();
	public List<string> Warnings { get; } = new();
}

public class MetadataService
{
	public const string UgcLicenseUrl = "https://www.realm-game.com/RealmPlatform_UGC_License_v1.txt";

	public event Action<string>? MetadataSaved;

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
			MetadataSaved?.Invoke(targetPath);
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

		ValidateEntityList(metadata.CustomUnits, u => u.TemplateID, u => u.Scale, "CustomUnits");
		ValidateEntityList(metadata.CustomBuildings, b => b.TemplateID, b => b.Scale, "CustomBuildings");
		ValidateEntityList(metadata.CustomResources, r => r.TemplateID, r => r.Scale, "CustomResources");
		ValidateEntityList(metadata.CustomProps, p => p.TemplateID, p => p.Scale, "CustomProps");

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
		metadata.CustomUnits ??= new List<UnitMetadata>();
		metadata.CustomBuildings ??= new List<UnitMetadata>();
		metadata.CustomResources ??= new List<ResourceMetadata>();
		metadata.CustomProps ??= new List<PropMetadata>();
		metadata.CustomAbilities ??= new List<AbilityMetadata>();
		metadata.CustomWeapons ??= new List<WeaponMetadata>();
		metadata.CustomUpgrades ??= new List<UpgradeMetadata>();
		metadata.CustomItems ??= new List<ItemMetadata>();
		metadata.CustomAttachments ??= new List<AttachmentMetadata>();
		metadata.CustomVfx ??= new List<VfxAttachmentConfig>();

		metadata.Models ??= new Dictionary<string, ModelMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Textures ??= new Dictionary<string, TextureMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Decals ??= new Dictionary<string, DecalMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.VfxSpritesheets ??= new Dictionary<string, VfxMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.NoiseTextures ??= new Dictionary<string, TextureMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Icons ??= new Dictionary<string, IconMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Skyboxes ??= new Dictionary<string, SkyboxMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Ribbons ??= new Dictionary<string, RibbonMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Shaders ??= new Dictionary<string, ShaderMetadata>(StringComparer.OrdinalIgnoreCase);

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

	public UnitMetadata? FindUnit(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomUnits == null || string.IsNullOrWhiteSpace(objectId)) return null;
		return metadata.CustomUnits.FirstOrDefault(u => string.Equals(u.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateUnit(MapMetadata metadata, UnitMetadata unit)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(unit.TemplateID)) return;
		int index = metadata.CustomUnits.FindIndex(u => string.Equals(u.TemplateID, unit.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomUnits[index] = unit;
		}
		else
		{
			metadata.CustomUnits.Add(unit);
		}
	}

	public bool RemoveUnit(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomUnits == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return metadata.CustomUnits.RemoveAll(u => string.Equals(u.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public UnitMetadata? FindBuilding(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomBuildings == null || string.IsNullOrWhiteSpace(objectId)) return null;
		return metadata.CustomBuildings.FirstOrDefault(b => string.Equals(b.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateBuilding(MapMetadata metadata, UnitMetadata building)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(building.TemplateID)) return;
		int index = metadata.CustomBuildings.FindIndex(b => string.Equals(b.TemplateID, building.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomBuildings[index] = building;
		}
		else
		{
			metadata.CustomBuildings.Add(building);
		}
	}

	public bool RemoveBuilding(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomBuildings == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return metadata.CustomBuildings.RemoveAll(b => string.Equals(b.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public PropMetadata? FindProp(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomProps == null || string.IsNullOrWhiteSpace(objectId)) return null;
		return metadata.CustomProps.FirstOrDefault(p => string.Equals(p.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateProp(MapMetadata metadata, PropMetadata prop)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(prop.TemplateID)) return;
		int index = metadata.CustomProps.FindIndex(p => string.Equals(p.TemplateID, prop.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomProps[index] = prop;
		}
		else
		{
			metadata.CustomProps.Add(prop);
		}
	}

	public bool RemoveProp(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomProps == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return metadata.CustomProps.RemoveAll(p => string.Equals(p.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public ResourceMetadata? FindResource(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomResources == null || string.IsNullOrWhiteSpace(objectId)) return null;
		return metadata.CustomResources.FirstOrDefault(r => string.Equals(r.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateResource(MapMetadata metadata, ResourceMetadata resource)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(resource.TemplateID)) return;
		int index = metadata.CustomResources.FindIndex(r => string.Equals(r.TemplateID, resource.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomResources[index] = resource;
		}
		else
		{
			metadata.CustomResources.Add(resource);
		}
	}

	public bool RemoveResource(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomResources == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return metadata.CustomResources.RemoveAll(r => string.Equals(r.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public WeaponMetadata? FindWeapon(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomWeapons == null || string.IsNullOrWhiteSpace(objectId)) return null;
		return metadata.CustomWeapons.FirstOrDefault(w => string.Equals(w.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateWeapon(MapMetadata metadata, WeaponMetadata weapon)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(weapon.TemplateID)) return;
		int index = metadata.CustomWeapons.FindIndex(w => string.Equals(w.TemplateID, weapon.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomWeapons[index] = weapon;
		}
		else
		{
			metadata.CustomWeapons.Add(weapon);
		}
	}

	public bool RemoveWeapon(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomWeapons == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return metadata.CustomWeapons.RemoveAll(w => string.Equals(w.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public AbilityMetadata? FindAbility(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomAbilities == null || string.IsNullOrWhiteSpace(objectId)) return null;
		return metadata.CustomAbilities.FirstOrDefault(a => string.Equals(a.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateAbility(MapMetadata metadata, AbilityMetadata ability)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(ability.TemplateID)) return;
		int index = metadata.CustomAbilities.FindIndex(a => string.Equals(a.TemplateID, ability.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomAbilities[index] = ability;
		}
		else
		{
			metadata.CustomAbilities.Add(ability);
		}
	}

	public bool RemoveAbility(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomAbilities == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return metadata.CustomAbilities.RemoveAll(a => string.Equals(a.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public UpgradeMetadata? FindUpgrade(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomUpgrades == null || string.IsNullOrWhiteSpace(objectId)) return null;
		return metadata.CustomUpgrades.FirstOrDefault(u => string.Equals(u.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateUpgrade(MapMetadata metadata, UpgradeMetadata upgrade)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(upgrade.TemplateID)) return;
		int index = metadata.CustomUpgrades.FindIndex(u => string.Equals(u.TemplateID, upgrade.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomUpgrades[index] = upgrade;
		}
		else
		{
			metadata.CustomUpgrades.Add(upgrade);
		}
	}

	public bool RemoveUpgrade(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomUpgrades == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return metadata.CustomUpgrades.RemoveAll(u => string.Equals(u.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public ItemMetadata? FindItem(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomItems == null || string.IsNullOrWhiteSpace(objectId)) return null;
		return metadata.CustomItems.FirstOrDefault(i => string.Equals(i.TemplateID, objectId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateItem(MapMetadata metadata, ItemMetadata item)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(item.TemplateID)) return;
		int index = metadata.CustomItems.FindIndex(i => string.Equals(i.TemplateID, item.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomItems[index] = item;
		}
		else
		{
			metadata.CustomItems.Add(item);
		}
	}

	public bool RemoveItem(MapMetadata metadata, string objectId)
	{
		if (metadata?.CustomItems == null || string.IsNullOrWhiteSpace(objectId)) return false;
		return metadata.CustomItems.RemoveAll(i => string.Equals(i.TemplateID, objectId, StringComparison.OrdinalIgnoreCase)) > 0;
	}

	public AttachmentMetadata? FindAttachment(MapMetadata metadata, string attachmentId)
	{
		if (metadata?.CustomAttachments == null || string.IsNullOrWhiteSpace(attachmentId)) return null;
		return metadata.CustomAttachments.FirstOrDefault(a => string.Equals(a.AttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateAttachment(MapMetadata metadata, AttachmentMetadata attachment)
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

	public EnvironmentPresetConfig? GetEnvironmentPreset(MapMetadata metadata, string presetId)
	{
		if (metadata?.CustomEnvironmentPresets == null || string.IsNullOrWhiteSpace(presetId)) return null;
		return metadata.CustomEnvironmentPresets.FirstOrDefault(p => string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase));
	}

	public void AddOrUpdateEnvironmentPreset(MapMetadata metadata, EnvironmentPresetConfig preset)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(preset.Id)) return;
		metadata.CustomEnvironmentPresets ??= new();
		int index = metadata.CustomEnvironmentPresets.FindIndex(p => string.Equals(p.Id, preset.Id, StringComparison.OrdinalIgnoreCase));
		if (index >= 0)
		{
			metadata.CustomEnvironmentPresets[index] = preset;
		}
		else
		{
			metadata.CustomEnvironmentPresets.Add(preset);
		}
	}

	public bool RemoveEnvironmentPreset(MapMetadata metadata, string presetId)
	{
		if (metadata?.CustomEnvironmentPresets == null || string.IsNullOrWhiteSpace(presetId)) return false;
		return metadata.CustomEnvironmentPresets.RemoveAll(p => string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase)) > 0;
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
