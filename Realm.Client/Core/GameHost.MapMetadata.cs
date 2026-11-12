using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Realm.Ecs;
using Realm.Ecs.Replication;
using DotRecast.Detour;
using Godot;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = Godot.Environment;
using FileAccess = System.IO.FileAccess;
using Realm.Shared.Abilities;

namespace Realm.Client.Core;
public partial class GameHost
{
	public event Action<int>? OnPlayerLeft;

	private void TickZoneTriggers()
	{
		if (OnUnitEnterZone == null || GameContext.World == null || !World.IsAlive(GameContext.Simulation.WorldEntity) || !World.Has<ScriptZonesState>(GameContext.Simulation.WorldEntity)) return;

		int zoneHandle = World.Get<ScriptZonesState>(GameContext.Simulation.WorldEntity).Handle;
		int zoneCount = ScriptZonesState.GetZones(zoneHandle).Length;
		if (zoneCount == 0) return;

		if (zoneCount > OccupiedZoneMask.Capacity && !_zoneCapacityWarningIssued)
		{
			_zoneCapacityWarningIssued = true;
			GD.PushWarning($"[GameHost] Map declares {zoneCount} script zones but only the first {OccupiedZoneMask.Capacity} can be tracked for zone enter events.");
		}

		var positionQuery = QueryCache.AllPositionAndDefinitionIdNoneDeadQuery;
		World.Query(in positionQuery, (Entity entity, ref Position posComp) =>
		{
			if (!World.Has<OccupiedZones>(entity)) World.Add(entity, new OccupiedZones());
			ref var occupiedZones = ref World.Get<OccupiedZones>(entity);
			CheckUnitZones(entity, new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z), ref occupiedZones, ScriptZonesState.GetZones(zoneHandle));
		});
	}

	private bool _zoneCapacityWarningIssued;

	private void CheckUnitZones(Entity entity, Vector3 pos, ref OccupiedZones occupiedZones, ReadOnlySpan<ZoneBounds> zones)
	{
		for (int i = 0; i < zones.Length; i++)
		{
			ref readonly ZoneBounds z = ref zones[i];
			bool inside = pos.X >= z.MinX && pos.X <= z.MaxX && pos.Z >= z.MinZ && pos.Z <= z.MaxZ;

			if (inside && occupiedZones.Add(i) && TryGetUnit3D(entity, out _))
				OnUnitEnterZone?.Invoke((UnitId)entity.Id, i);
			else if (!inside)
				occupiedZones.Remove(i);
		}
	}

	public void NotifyPlayerLeft(int playerIndex)
	{
		OnPlayerLeft?.Invoke(playerIndex);
	}

	private void TickScheduledTimers(float delta)
	{
		if (GameContext.TimerService.ScheduledTimers.Count == 0) return;

		Span<int> keysBuffer = stackalloc int[Math.Min(GameContext.TimerService.ScheduledTimers.Count, 64)];
		int keyCount = 0;
		foreach (int k in GameContext.TimerService.ScheduledTimers.Keys)
		{
			if (keyCount < keysBuffer.Length)
				keysBuffer[keyCount++] = k;
		}

		List<int>? toRemove = null;
		for (int i = 0; i < keyCount; i++)
		{
			int handle = keysBuffer[i];
			if (!GameContext.TimerService.ScheduledTimers.TryGetValue(handle, out var entry)) continue;

			float remaining = entry.Remaining - delta;
			if (remaining <= 0f)
			{
				entry.Callback.Invoke();

				if (entry.Repeating)
				{
					GameContext.TimerService.ScheduledTimers[handle] = (entry.Interval, entry.Interval + remaining, true, entry.Callback);
				}
				else
				{
					toRemove ??= new List<int>();
					toRemove.Add(handle);
				}
			}
			else
			{
				GameContext.TimerService.ScheduledTimers[handle] = (entry.Interval, remaining, entry.Repeating, entry.Callback);
			}
		}

		if (toRemove != null)
		{
			foreach (int h in toRemove)
				GameContext.TimerService.ScheduledTimers.Remove(h);
		}
	}

	private static string SafeGlobalizePath(string path)
	{
		if (string.IsNullOrEmpty(path)) return path;
		try
		{
			return ProjectSettings.GlobalizePath(path);
		}
		catch
		{
			return path;
		}
	}

	public static string? ResolveMapDirectory(string mapNameOrPath, string? version = null)
	{
		if (string.IsNullOrWhiteSpace(mapNameOrPath)) return null;

		string norm = mapNameOrPath.Replace('\\', '/').Trim();
		string? explicitPath = TryResolveExplicitPath(norm);
		if (explicitPath != null) return explicitPath;

		string? targetVersion = !string.IsNullOrWhiteSpace(version) ? version : GameContext.Lobby?.ActiveMapVersion;
		string? manifestPath = MapAssetManager.FindManifestPath(norm, targetVersion) ?? MapAssetManager.FindManifestPath(norm, null);

		if (!string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath))
			return Path.GetDirectoryName(manifestPath);

		return CheckMapDir(norm) ?? CheckMapDir(norm.ToLowerInvariant());
	}

	private static string? TryResolveExplicitPath(string norm)
	{
		if (!norm.StartsWith("user://") && !norm.StartsWith("res://") && !Path.IsPathRooted(norm)) return null;

		string global = norm.StartsWith("user://") || norm.StartsWith("res://") ? SafeGlobalizePath(norm) : norm;
		if (Directory.Exists(global)) return global;
		if (File.Exists(global)) return Path.GetDirectoryName(global);

		return null;
	}

	private static string? CheckMapDir(string name)
	{
		string resDir = SafeGlobalizePath($"res://Maps/{name}");
		if (Directory.Exists(resDir)) return resDir;

		string userDir = SafeGlobalizePath($"user://maps/{name}");
		if (Directory.Exists(userDir)) return userDir;

		return null;
	}

	public void LoadUnitMetadata(string? mapName = null)
	{
		ResetAbilityCatalog();
		mapName = !string.IsNullOrEmpty(mapName) ? mapName : (!string.IsNullOrEmpty(GameContext.Simulation.ActiveMapName) ? GameContext.Simulation.ActiveMapName : "Untitled Map");
		GameContext.Simulation.ActiveMapName = mapName;
		LocalizationManager.CurrentMapName = mapName;
		LocalizationManager.SetupTranslations();

		string? resolvedDir = ResolveMapDirectory(mapName);
		string path = resolvedDir != null ? Path.Combine(resolvedDir, "metadata.json") : (mapName.StartsWith("user://") || mapName.StartsWith("res://") || Path.IsPathRooted(mapName) ? Path.Combine(mapName, "metadata.json") : $"res://Maps/{mapName}/metadata.json");
		if (resolvedDir != null) GameContext.MapStorageService.CurrentMapDirectory = resolvedDir;

		var metadataService = GameContext.MetadataService
			?? throw new InvalidOperationException("LoadUnitMetadata ran before the metadata service existed.");
		var metadata = metadataService.LoadMetadata(path);

		// Tuning is applied before anything is spawned, so the first stat calculation already sees the map's numbers.
		var combatTuningWarnings = new List<string>(4);
		MapCombatTuning.Apply(GameContext.Simulation, metadata, combatTuningWarnings);
		for (int index = 0; index < combatTuningWarnings.Count; index++)
		{
			GD.PushWarning(combatTuningWarnings[index]);
		}

		// The plate a unit draws and the slots it may fill are the match's rules rather than a screen preference, so this
		// process states them from the same map the authority states them from: a screen that skipped this drew no mana
		// bar, no level badge and the wrong carry capacity until the authority's own catalog reached it.
		MapWorldLoader.ApplyMapRules(GameContext.Simulation.MapRules, metadata.MapProperties);

		UnitRegistry.Clear();
		BuildingRegistry.Clear();
		PropRegistry.Clear();
		ResourceRegistry.Clear();
		WeaponRegistry.Clear();
		AttachmentRegistry.Clear();
		ItemRegistry.Clear();
		GameContext.RegistryService.VfxRegistry.Clear();

		MatchLoadTrace.Mark("map_metadata");
		if (metadata.Templates != null)
		{
			DefinitionCatalog catalog = DefinitionCatalog.Build(metadata);
			MatchLoadTrace.Mark("catalog_build");
			LoadMetadataTemplates(catalog);
		}

		Prop3D.ClearModelPathCache();
	}

	private void LoadMetadataTemplates(DefinitionCatalog catalog)
	{
		catalog.BindTo(GameContext.Simulation.MapRules);
		catalog.BindTo(GameContext.CommandAuthority);
		LoadWeaponAndAttachmentTemplates(catalog.Metadata.Templates);
		LoadItemTemplates(catalog.Metadata.Templates);
		LoadUpgradeTemplates(catalog.Metadata.Templates);
		LoadConsumableTemplates(catalog.Metadata.Templates);
		LoadUnitTemplates(catalog);
		LoadBuildingTemplates(catalog);
		LoadResourceAndPropTemplates(catalog);

		if (catalog.Metadata.Templates.Abilities != null && catalog.Metadata.Templates.Abilities.Count > 0) RegisterCustomAbilities(catalog.Metadata.Templates.Abilities);
		foreach (var cfg in catalog.Metadata.Templates.Vfx)
		{
			if (!string.IsNullOrEmpty(cfg.VfxId))
			{
				GameContext.RegistryService.RegisterVfxConfig(cfg.VfxId, cfg);
			}
		}
	}

	private void LoadWeaponAndAttachmentTemplates(TemplateContainer templates)
	{
		foreach (var meta in templates.Weapons)
		{
			if (!string.IsNullOrEmpty(meta.TemplateID))
			{
				WeaponRegistry[(StringName)meta.TemplateID] = meta;
				if (meta.TemplateID.StartsWith("weapon/", StringComparison.OrdinalIgnoreCase))
				{
					WeaponRegistry[(StringName)meta.TemplateID.Substring(7)] = meta;
				}
			}
		}

		foreach (var meta in templates.Attachments)
		{
			if (!string.IsNullOrEmpty(meta.AttachmentId))
			{
				AttachmentRegistry[(StringName)meta.AttachmentId] = meta;
			}
		}
	}

	private void LoadItemTemplates(TemplateContainer templates)
	{
		foreach (var meta in templates.Items)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;

			ItemRegistry[(StringName)meta.TemplateID] = meta;
			if (meta.TemplateID.StartsWith("item/", StringComparison.OrdinalIgnoreCase))
			{
				ItemRegistry[(StringName)meta.TemplateID.Substring(5)] = meta;
			}
		}
	}

	private void LoadUpgradeTemplates(TemplateContainer templates)
	{
		if (templates.Upgrades == null) return;
		foreach (var meta in templates.Upgrades)
		{
			if (string.IsNullOrEmpty(meta.TemplateID)) continue;

			UpgradeRegistry[(StringName)meta.TemplateID] = meta;
			if (meta.TemplateID.StartsWith("upgrade/", StringComparison.OrdinalIgnoreCase))
			{
				UpgradeRegistry[(StringName)meta.TemplateID.Substring(8)] = meta;
			}
		}
	}

	private void LoadConsumableTemplates(TemplateContainer templates)
	{
		if (templates.Consumables == null) return;
		foreach (var meta in templates.Consumables)
		{
			GameContext.RegistryService.RegisterConsumable(meta);
		}
	}

	private void LoadUnitTemplates(DefinitionCatalog catalog)
	{
		RegisterTemplates(UnitRegistry, catalog.Units);
		PreloadTemplates(catalog.Metadata.Templates.Units);
		MatchLoadTrace.Mark("unit_models");
	}

	private void LoadBuildingTemplates(DefinitionCatalog catalog)
	{
		RegisterTemplates(BuildingRegistry, catalog.Buildings);
		PreloadTemplates(catalog.Metadata.Templates.Buildings);
		MatchLoadTrace.Mark("building_models");
	}

	private void LoadResourceAndPropTemplates(DefinitionCatalog catalog)
	{
		RegisterTemplates(ResourceRegistry, catalog.Resources);
		RegisterTemplates(PropRegistry, catalog.Props);
	}

	private static void RegisterTemplates<T>(Dictionary<StringName, T> registry, IReadOnlyDictionary<string, T> templates)
	{
		foreach (KeyValuePair<string, T> entry in templates)
		{
			registry[(StringName)entry.Key] = entry.Value;
		}
	}

	private static void PreloadTemplates(List<UnitMetadata> templates)
	{
		for (int index = 0; index < templates.Count; index++)
		{
			var modelPath = templates[index].ModelPath;
			if (!string.IsNullOrEmpty(modelPath)) ModelCache.PreloadModel(modelPath);
		}
	}

	public void SaveAttachmentDefaultsToMetadata(string fileName, AttachmentMetadata meta)
	{
		try
		{
			string dir = !string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory) ? GameContext.MapStorageService.CurrentMapDirectory : GameContext.EditorService._editingDirectory;
			var metadata = MapFileService.LoadMetadata(dir);
			metadata.Templates ??= new TemplateContainer();
			metadata.Templates.Attachments ??= new List<AttachmentMetadata>();

			int existingIndex = metadata.Templates.Attachments.FindIndex(a => string.Equals(a.AttachmentId, meta.AttachmentId, StringComparison.OrdinalIgnoreCase) || string.Equals(a.AttachmentId, fileName, StringComparison.OrdinalIgnoreCase));
			if (existingIndex >= 0)
			{
				metadata.Templates.Attachments[existingIndex] = meta;
			}
			else
			{
				if (string.IsNullOrEmpty(meta.AttachmentId))
				{
					meta.AttachmentId = fileName;
				}

				metadata.Templates.Attachments.Add(meta);
			}

			MapFileService.SaveMetadata(dir, metadata);
			LoadUnitMetadata(dir);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[GameHost] SaveAttachmentDefaultsToMetadata error: {ex.Message}");
		}
	}

	public void SaveUnitAnimationsToMetadata(string unitId, Dictionary<string, List<UnitAnimationEntry>> animations)
	{
		try
		{
			string dir = !string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory) ? GameContext.MapStorageService.CurrentMapDirectory : GameContext.EditorService._editingDirectory;
			var metadata = MapFileService.LoadMetadata(dir);
			var unit = metadata.GetUnit(unitId);
			if (unit != null)
			{
				unit.Animations = animations;
				MapFileService.SaveMetadata(dir, metadata);
				LoadUnitMetadata(dir);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[GameHost] SaveUnitAnimationsToMetadata error: {ex.Message}");
		}
	}

	public List<Unit3D>[] _controlGroups { get; set; } = new List<Unit3D>[10];
}
