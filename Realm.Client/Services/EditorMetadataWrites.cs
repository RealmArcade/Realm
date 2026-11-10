using System;
using System.Collections.Generic;
using System.IO;
using Realm.Client.Core;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Client.Services;

/// <summary>
///     Writing editor changes back to a map's metadata.json and to the registries that mirror it:
///     unit animations, ability presentation, and the model or portrait path of a unit, building,
///     resource or prop. Every entry point returns the metadata file's new timestamp, or null when
///     it declined to write, so EditorService keeps owning the sync-time field.
/// </summary>
public static class EditorMetadataWrites
{
	public static long? SaveCustomUnitAnimations(string wsPath, string unitId, Dictionary<string, List<UnitAnimationEntry>> animations)
	{
		if (string.IsNullOrEmpty(unitId)) return null;

		if (GameHost.UnitRegistry.TryGetValue(unitId, out var uMeta))
		{
			uMeta.Animations = animations;
			GameHost.UnitRegistry[unitId] = uMeta;
		}

		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return null;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			meta.UpdateUnit(unitId, u =>
			{
				u.Animations = animations;
				return u;
			});
		});
		return File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
	}

	public static long? SaveCustomAbilityVfxToMetadata(string wsPath, string abilityId, string visualEffect, string castSound, string iconPath, float aoeRadius)
	{
		if (string.IsNullOrEmpty(abilityId)) return null;

		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return null;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			meta.UpdateAbility(abilityId, ab =>
			{
				ab.VisualEffect = visualEffect;
				ab.CastSound = castSound;
				ab.IconPath = iconPath;
				ab.AreaOfEffectRadius = aoeRadius;
				return ab;
			});
		});
		return File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
	}

	public static long? SaveEntityModelPathToMetadata(string wsPath, string entityId, string fieldName, string domain, string newModelPath)
	{
		if (string.IsNullOrEmpty(entityId)) return null;

		fieldName = string.IsNullOrEmpty(fieldName) ? "ModelPath" : fieldName;
		domain = string.IsNullOrEmpty(domain) ? "units" : domain;

		UpdateLocalRegistryForModelPath(entityId, fieldName, domain, newModelPath);
		return UpdateMetadataFileForModelPath(wsPath, entityId, fieldName, domain, newModelPath);
	}

	private static void UpdateLocalRegistryForModelPath(string entityId, string fieldName, string domain, string newModelPath)
	{
		switch (domain.ToLowerInvariant())
		{
			case "units":
				if (!GameHost.UnitRegistry.TryGetValue(entityId, out var uMeta)) return;
				if (fieldName == "PortraitModelPath") uMeta.PortraitModelPath = newModelPath;
				else uMeta.ModelPath = newModelPath;
				GameHost.UnitRegistry[entityId] = uMeta;
				break;
			case "buildings":
				if (!GameHost.BuildingRegistry.TryGetValue(entityId, out var bMeta)) return;
				if (fieldName == "PortraitModelPath") bMeta.PortraitModelPath = newModelPath;
				else bMeta.ModelPath = newModelPath;
				GameHost.BuildingRegistry[entityId] = bMeta;
				break;
			case "resources":
				if (!GameHost.ResourceRegistry.TryGetValue(entityId, out var rMeta)) return;
				if (fieldName == "PortraitModelPath") rMeta.PortraitModelPath = newModelPath;
				else rMeta.ModelPath = newModelPath;
				GameHost.ResourceRegistry[entityId] = rMeta;
				break;
			case "props":
				if (!GameHost.PropRegistry.TryGetValue(entityId, out var pMeta)) return;
				pMeta.ModelPath = newModelPath;
				GameHost.PropRegistry[entityId] = pMeta;
				break;
		}
	}

	private static long? UpdateMetadataFileForModelPath(string wsPath, string entityId, string fieldName, string domain, string newModelPath)
	{
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return null;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			switch (domain.ToLowerInvariant())
			{
				case "units":
					meta.UpdateUnit(entityId, u =>
					{
						if (fieldName == "PortraitModelPath") u.PortraitModelPath = newModelPath;
						else u.ModelPath = newModelPath;
						return u;
					});
					break;
				case "buildings":
					meta.UpdateBuilding(entityId, b =>
					{
						if (fieldName == "PortraitModelPath") b.PortraitModelPath = newModelPath;
						else b.ModelPath = newModelPath;
						return b;
					});
					break;
				case "resources":
					meta.UpdateResource(entityId, r =>
					{
						if (fieldName == "PortraitModelPath") r.PortraitModelPath = newModelPath;
						else r.ModelPath = newModelPath;
						return r;
					});
					break;
				case "props":
					meta.UpdateProp(entityId, p =>
					{
						p.ModelPath = newModelPath;
						return p;
					});
					break;
			}
		});
		return File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
	}

	public static void UpdateScaleMetadata(int newWidth, int newDepth, MapCameraBounds cameraBounds)
	{
		string scaleWsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string scaleMetaPath = MetadataService.ResolveMetadataPath(scaleWsPath);

		if (File.Exists(scaleMetaPath))
		{
			try
			{
				GameContext.MetadataService.UpdateMetadata(scaleWsPath, meta =>
				{
					meta.MapProperties.MapWidth = newWidth;
					meta.MapProperties.MapHeight = newDepth;
					meta.MapProperties.CameraBoundsLeft = cameraBounds.Left;
					meta.MapProperties.CameraBoundsRight = cameraBounds.Right;
					meta.MapProperties.CameraBoundsTop = cameraBounds.Top;
					meta.MapProperties.CameraBoundsBottom = cameraBounds.Bottom;
				});
			}
			catch (Exception ex)
			{
				GameLog.LogError($"Failed to update metadata.json map dimensions during scale: {ex.Message}");
			}
		}
	}
}
