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
		var metadata = Realm.Shared.Services.MapFileService.LoadMetadata(targetPath);
		CleanMetadata(metadata);
		return metadata;
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
		CleanMetadata(metadata);

		try
		{
			EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
			Realm.Shared.Services.MapFileService.SaveMetadata(targetPath, metadata);
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

		ValidateEntityList(metadata.Templates?.Units, u => u.TemplateID, u => u.Scale, "Units");
		ValidateEntityList(metadata.Templates?.Buildings, b => b.TemplateID, b => b.Scale, "Buildings");
		ValidateEntityList(metadata.Templates?.Resources, r => r.TemplateID, r => r.Scale, "Resources");
		ValidateEntityList(metadata.Templates?.Props, p => p.TemplateID, p => p.Scale, "Props");

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
		metadata.Templates ??= new TemplateContainer();
		metadata.Templates.Units ??= new List<UnitMetadata>();
		metadata.Templates.Buildings ??= new List<UnitMetadata>();
		metadata.Templates.Resources ??= new List<ResourceMetadata>();
		metadata.Templates.Props ??= new List<PropMetadata>();
		metadata.Templates.Abilities ??= new List<AbilityMetadata>();
		metadata.Templates.Weapons ??= new List<WeaponMetadata>();
		metadata.Templates.Upgrades ??= new List<UpgradeMetadata>();
		metadata.Templates.Items ??= new List<ItemMetadata>();
		metadata.Templates.Attachments ??= new List<AttachmentMetadata>();
		metadata.Templates.Vfx ??= new List<VfxAttachmentConfig>();

		metadata.Models ??= new Dictionary<string, ModelMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Textures ??= new Dictionary<string, TextureMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Decals ??= new Dictionary<string, DecalMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.VfxSpritesheets ??= new Dictionary<string, VfxMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.NoiseTextures ??= new Dictionary<string, TextureMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Icons ??= new Dictionary<string, IconMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Skyboxes ??= new Dictionary<string, SkyboxMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Ribbons ??= new Dictionary<string, RibbonMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.SpawnShaders ??= new Dictionary<string, ShaderMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.GdShaders ??= new Dictionary<string, ShaderMetadata>(StringComparer.OrdinalIgnoreCase);

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

	public UnitMetadata? FindUnit(MapMetadata metadata, string objectId) => metadata?.FindUnit(objectId);
	public void AddOrUpdateUnit(MapMetadata metadata, UnitMetadata unit) => metadata?.AddOrUpdateUnit(unit);
	public bool RemoveUnit(MapMetadata metadata, string objectId) => metadata?.RemoveUnit(objectId) ?? false;

	public UnitMetadata? FindBuilding(MapMetadata metadata, string objectId) => metadata?.FindBuilding(objectId);
	public void AddOrUpdateBuilding(MapMetadata metadata, UnitMetadata building) => metadata?.AddOrUpdateBuilding(building);
	public bool RemoveBuilding(MapMetadata metadata, string objectId) => metadata?.RemoveBuilding(objectId) ?? false;

	public PropMetadata? FindProp(MapMetadata metadata, string objectId) => metadata?.FindProp(objectId);
	public void AddOrUpdateProp(MapMetadata metadata, PropMetadata prop) => metadata?.AddOrUpdateProp(prop);
	public bool RemoveProp(MapMetadata metadata, string objectId) => metadata?.RemoveProp(objectId) ?? false;

	public ResourceMetadata? FindResource(MapMetadata metadata, string objectId) => metadata?.FindResource(objectId);
	public void AddOrUpdateResource(MapMetadata metadata, ResourceMetadata resource) => metadata?.AddOrUpdateResource(resource);
	public bool RemoveResource(MapMetadata metadata, string objectId) => metadata?.RemoveResource(objectId) ?? false;

	public WeaponMetadata? FindWeapon(MapMetadata metadata, string objectId) => metadata?.FindWeapon(objectId);
	public void AddOrUpdateWeapon(MapMetadata metadata, WeaponMetadata weapon) => metadata?.AddOrUpdateWeapon(weapon);
	public bool RemoveWeapon(MapMetadata metadata, string objectId) => metadata?.RemoveWeapon(objectId) ?? false;

	public AbilityMetadata? FindAbility(MapMetadata metadata, string objectId) => metadata?.FindAbility(objectId);
	public void AddOrUpdateAbility(MapMetadata metadata, AbilityMetadata ability) => metadata?.AddOrUpdateAbility(ability);
	public bool RemoveAbility(MapMetadata metadata, string objectId) => metadata?.RemoveAbility(objectId) ?? false;

	public UpgradeMetadata? FindUpgrade(MapMetadata metadata, string objectId) => metadata?.FindUpgrade(objectId);
	public void AddOrUpdateUpgrade(MapMetadata metadata, UpgradeMetadata upgrade) => metadata?.AddOrUpdateUpgrade(upgrade);
	public bool RemoveUpgrade(MapMetadata metadata, string objectId) => metadata?.RemoveUpgrade(objectId) ?? false;

	public ItemMetadata? FindItem(MapMetadata metadata, string objectId) => metadata?.FindItem(objectId);
	public void AddOrUpdateItem(MapMetadata metadata, ItemMetadata item) => metadata?.AddOrUpdateItem(item);
	public bool RemoveItem(MapMetadata metadata, string objectId) => metadata?.RemoveItem(objectId) ?? false;

	public AttachmentMetadata? FindAttachment(MapMetadata metadata, string attachmentId) => metadata?.FindAttachment(attachmentId);
	public void AddOrUpdateAttachment(MapMetadata metadata, AttachmentMetadata attachment) => metadata?.AddOrUpdateAttachment(attachment);
	public bool RemoveAttachment(MapMetadata metadata, string attachmentId) => metadata?.RemoveAttachment(attachmentId) ?? false;

	public VfxAttachmentConfig? FindVfx(MapMetadata metadata, string vfxId) => metadata?.FindVfx(vfxId);
	public void AddOrUpdateVfx(MapMetadata metadata, VfxAttachmentConfig vfx) => metadata?.AddOrUpdateVfx(vfx);
	public bool RemoveVfx(MapMetadata metadata, string vfxId) => metadata?.RemoveVfx(vfxId) ?? false;

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
