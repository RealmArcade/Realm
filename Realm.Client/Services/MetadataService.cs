using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Realm.Shared;
using Realm.Shared.Services;

namespace Realm.Client.Services;

public class MetadataService
{
	public const string UgcLicenseUrl = "https://www.realm-game.com/RealmPlatform_UGC_License_v1.txt";

	public event Action<string>? MetadataSaved;

	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		IncludeFields = true,
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
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
		var metadata = MapFileService.LoadMetadata(targetPath);
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
			MapFileService.SaveMetadata(targetPath, metadata);
			string? targetDir = Path.GetDirectoryName(targetPath);
			if (!string.IsNullOrEmpty(targetDir))
			{
				MapTemplateGeneratorService.EnsureMapTemplates(targetDir, metadata);
			}
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

		ValidateEntityList(metadata.Templates?.Units, u => u.TemplateID, u => u.Scale, "Units", result, entityIds);
		ValidateEntityList(metadata.Templates?.Buildings, b => b.TemplateID, b => b.Scale, "Buildings", result, entityIds);
		ValidateEntityList(metadata.Templates?.Resources, r => r.TemplateID, r => r.Scale, "Resources", result, entityIds);
		ValidateEntityList(metadata.Templates?.Props, p => p.TemplateID, p => p.Scale, "Props", result, entityIds);

		ValidateDependencies(metadata.Dependencies, result);
		ValidatePlayerSlots(metadata.MapProperties?.PlayerSlots, result);
		ValidatePlayablePlayerSlots(metadata.MapProperties, result);

		return result;
	}

	private static void ValidateEntityList<T>(IEnumerable<T> items, Func<T, string> getId, Func<T, float> getScale, string domainName, MapValidationResult result, HashSet<string> entityIds)
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

	private static void ValidateDependencies(List<MapDependencyMetadata> dependencies, MapValidationResult result)
	{
		if (dependencies == null) return;

		var depIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var dep in dependencies)
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

	private static void ValidatePlayablePlayerSlots(MapInfoMetadata? mapProperties, MapValidationResult result)
	{
		if (mapProperties?.PlayablePlayerSlots is not int declaredSlots) return;

		if (declaredSlots < MapInfoMetadata.MinPlayablePlayerSlots || declaredSlots > MapInfoMetadata.MaxPlayablePlayerSlots)
		{
			result.Errors.Add($"PlayablePlayerSlots must be between {MapInfoMetadata.MinPlayablePlayerSlots} and {MapInfoMetadata.MaxPlayablePlayerSlots}; found {declaredSlots}.");
			return;
		}

		var declaredPlayerSlots = mapProperties.PlayerSlots;
		if (declaredPlayerSlots != null && declaredPlayerSlots.Count > declaredSlots)
		{
			result.Errors.Add($"PlayerSlots declares {declaredPlayerSlots.Count} slots but PlayablePlayerSlots is {declaredSlots}.");
		}
	}

	private static void ValidatePlayerSlots(List<MapPlayerSlotConfig> playerSlots, MapValidationResult result)
	{
		if (playerSlots == null) return;

		var slotIds = new HashSet<int>();
		foreach (var slot in playerSlots)
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

		metadata.Textures ??= new Dictionary<string, TextureMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Decals ??= new Dictionary<string, DecalMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.VfxSpritesheets ??= new Dictionary<string, VfxMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.NoiseTextures ??= new Dictionary<string, TextureMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Icons ??= new Dictionary<string, IconMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Skyboxes ??= new Dictionary<string, SkyboxMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.Ribbons ??= new Dictionary<string, RibbonMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.SpawnShaders ??= new Dictionary<string, SpawnShaderMetadata>(StringComparer.OrdinalIgnoreCase);
		metadata.GdShaders ??= new Dictionary<string, ShaderMetadata>(StringComparer.OrdinalIgnoreCase);

		if (string.IsNullOrEmpty(metadata.GameBuildNumber))
		{
			metadata.GameBuildNumber = RealmVersion.GameBuildNumber;
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