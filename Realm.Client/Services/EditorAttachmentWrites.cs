using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Meta;
using Realm.Shared.Maps;

namespace Realm.Client.Services;

/// <summary>
/// Writing a unit's or building's object attachments and pseudo sockets back into the registries and the map
/// metadata, which is what an undo or a cancel of an attachment edit has to do. Every entry point applies
/// the snapshot to both stores and reports the metadata write time, so the caller decides whether to track
/// it; nothing here holds state.
/// </summary>
internal static class EditorAttachmentWrites
{
	private static string ResolveUnitOrBuildingKey(string id, RegistryService registryService, out bool isBuildingMeta)
	{
		var unitRegistry = registryService.UnitRegistry;
		var buildingRegistry = registryService.BuildingRegistry;

		string regKey = id;
		isBuildingMeta = false;

		if (unitRegistry.ContainsKey((StringName)id))
		{
			regKey = id;
		}
		else if (buildingRegistry != null && buildingRegistry.ContainsKey((StringName)id))
		{
			regKey = id;
			isBuildingMeta = true;
		}
		else
		{
			string cleanId = Path.GetFileNameWithoutExtension(id);
			if (unitRegistry.ContainsKey((StringName)cleanId))
			{
				regKey = cleanId;
			}
			else if (buildingRegistry != null && buildingRegistry.ContainsKey((StringName)cleanId))
			{
				regKey = cleanId;
				isBuildingMeta = true;
			}
			else
			{
				foreach (var k in unitRegistry.Keys)
				{
					string kStr = k.ToString();
					if (kStr.Equals(id, StringComparison.OrdinalIgnoreCase) ||
						kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
						Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
					{
						regKey = kStr;
						break;
					}
				}

				if (!isBuildingMeta && buildingRegistry != null)
				{
					foreach (var k in buildingRegistry.Keys)
					{
						string kStr = k.ToString();
						if (kStr.Equals(id, StringComparison.OrdinalIgnoreCase) ||
							kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
							Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
						{
							regKey = kStr;
							isBuildingMeta = true;
							break;
						}
					}
				}
			}
		}

		return regKey;
	}

	public static string RemoveUnitObjectAttachment(string unitId, string socket, string attachmentId, string? parentAttachmentId, string editingDirectory, out long? metadataSyncTime)
	{
		metadataSyncTime = null;
		if (string.IsNullOrEmpty(unitId) || string.IsNullOrEmpty(attachmentId)) return unitId;

		var registryService = GameContext.RegistryService;
		var unitRegistry = registryService.UnitRegistry;
		var buildingRegistry = registryService.BuildingRegistry;

		string regKey = ResolveUnitOrBuildingKey(unitId, registryService, out bool isBuildingMeta);

		if (isBuildingMeta && buildingRegistry != null && buildingRegistry.TryGetValue((StringName)regKey, out var bMeta))
		{
			bMeta.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
			buildingRegistry[(StringName)regKey] = bMeta;
		}
		else if (unitRegistry.TryGetValue((StringName)regKey, out var uMeta))
		{
			uMeta.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
			unitRegistry[(StringName)regKey] = uMeta;
		}

		string wsPath = editingDirectory;
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return regKey;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			bool updated = meta.UpdateUnit(unitId, u =>
			{
				u.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
				return u;
			});
			if (!updated)
				updated = meta.UpdateBuilding(unitId, b =>
				{
					b.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
					return b;
				});
			if (!updated)
				updated = meta.UpdateUnit(regKey, u =>
				{
					u.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
					return u;
				});
			if (!updated)
				meta.UpdateBuilding(regKey, b =>
				{
					b.RemoveObjectAttachment(socket, attachmentId, parentAttachmentId);
					return b;
				});
		});

		long getWriteTimeSafe = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
		metadataSyncTime = getWriteTimeSafe;

		return regKey;
	}

	public static string RestoreUnitObjectAttachments(string targetId, UnitObjectAttachments? snapshot, string editingDirectory, out long? metadataSyncTime)
	{
		metadataSyncTime = null;
		if (string.IsNullOrEmpty(targetId)) return targetId;

		var registryService = GameContext.RegistryService;
		var unitRegistry = registryService.UnitRegistry;
		var buildingRegistry = registryService.BuildingRegistry;

		string regKey = ResolveUnitOrBuildingKey(targetId, registryService, out bool isBuildingMeta);

		if (isBuildingMeta && buildingRegistry != null && buildingRegistry.TryGetValue((StringName)regKey, out var bMeta))
		{
			bMeta.ObjectAttachments = (snapshot != null && snapshot.HasAny()) ? snapshot?.Clone() : null;
			buildingRegistry[(StringName)regKey] = bMeta;
		}
		else if (unitRegistry.TryGetValue((StringName)regKey, out var uMeta))
		{
			uMeta.ObjectAttachments = (snapshot != null && snapshot.HasAny()) ? snapshot?.Clone() : null;
			unitRegistry[(StringName)regKey] = uMeta;
		}

		string wsPath = editingDirectory;
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (File.Exists(metadataPath))
		{
			GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
			{
				var atts = (snapshot != null && snapshot.HasAny()) ? snapshot?.Clone() : null;
				bool updated = meta.UpdateUnit(targetId, u =>
				{
					u.ObjectAttachments = atts;
					return u;
				});
				if (!updated)
					updated = meta.UpdateBuilding(targetId, b =>
					{
						b.ObjectAttachments = atts;
						return b;
					});
				if (!updated)
					updated = meta.UpdateUnit(regKey, u =>
					{
						u.ObjectAttachments = atts;
						return u;
					});
				if (!updated)
					meta.UpdateBuilding(regKey, b =>
					{
						b.ObjectAttachments = atts;
						return b;
					});
			});

			long getWriteTimeSafe = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
			metadataSyncTime = getWriteTimeSafe;
		}

		return regKey;
	}

	public static string RestoreUnitPseudoSockets(string targetId, List<PseudoSocketAnchor>? snapshot, string editingDirectory, out long? metadataSyncTime)
	{
		metadataSyncTime = null;
		if (string.IsNullOrEmpty(targetId)) return targetId;

		var registryService = GameContext.RegistryService;
		string cleanId = Path.GetFileNameWithoutExtension(targetId);
		string regKey = ResolvePseudoSocketRegistryKey(targetId, cleanId, registryService);

		ApplyPseudoSocketsToRegistry(regKey, snapshot, registryService);
		PseudoSocketCache.Clear();

		string wsPath = editingDirectory;
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (File.Exists(metadataPath))
		{
			GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
			{
				meta.UpdateUnit(targetId, u => { u.PseudoSockets = ClonePseudoSockets(snapshot); return u; });
				meta.UpdateBuilding(targetId, b => { b.PseudoSockets = ClonePseudoSockets(snapshot); return b; });
				meta.UpdateProp(targetId, p => { p.PseudoSockets = ClonePseudoSockets(snapshot); return p; });
				meta.UpdateResource(targetId, r => { r.PseudoSockets = ClonePseudoSockets(snapshot); return r; });

				if (!string.Equals(regKey, targetId, StringComparison.OrdinalIgnoreCase))
				{
					meta.UpdateUnit(regKey, u => { u.PseudoSockets = ClonePseudoSockets(snapshot); return u; });
					meta.UpdateBuilding(regKey, b => { b.PseudoSockets = ClonePseudoSockets(snapshot); return b; });
					meta.UpdateProp(regKey, p => { p.PseudoSockets = ClonePseudoSockets(snapshot); return p; });
					meta.UpdateResource(regKey, r => { r.PseudoSockets = ClonePseudoSockets(snapshot); return r; });
				}
			});

			long getWriteTimeSafe = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
			metadataSyncTime = getWriteTimeSafe;
		}

		return regKey;
	}

	private static string ResolvePseudoSocketRegistryKey(string targetId, string cleanId, RegistryService registryService)
	{
		if (registryService.UnitRegistry.ContainsKey((StringName)targetId)) return targetId;
		if (registryService.BuildingRegistry.ContainsKey((StringName)targetId)) return targetId;
		if (registryService.PropRegistry.ContainsKey((StringName)targetId)) return targetId;
		if (registryService.ResourceRegistry.ContainsKey((StringName)targetId)) return targetId;

		if (registryService.UnitRegistry.ContainsKey((StringName)cleanId)) return cleanId;
		if (registryService.BuildingRegistry.ContainsKey((StringName)cleanId)) return cleanId;
		if (registryService.PropRegistry.ContainsKey((StringName)cleanId)) return cleanId;
		if (registryService.ResourceRegistry.ContainsKey((StringName)cleanId)) return cleanId;

		return targetId;
	}

	private static void ApplyPseudoSocketsToRegistry(string regKey, List<PseudoSocketAnchor>? snapshot, RegistryService registryService)
	{
		var storedAnchors = ClonePseudoSockets(snapshot);

		if (registryService.UnitRegistry.TryGetValue((StringName)regKey, out var unitMeta))
		{
			unitMeta.PseudoSockets = storedAnchors;
			registryService.UnitRegistry[(StringName)regKey] = unitMeta;
		}

		if (registryService.BuildingRegistry.TryGetValue((StringName)regKey, out var buildingMeta))
		{
			buildingMeta.PseudoSockets = storedAnchors;
			registryService.BuildingRegistry[(StringName)regKey] = buildingMeta;
		}

		if (registryService.PropRegistry.TryGetValue((StringName)regKey, out var propMeta))
		{
			propMeta.PseudoSockets = storedAnchors;
			registryService.PropRegistry[(StringName)regKey] = propMeta;
		}

		if (registryService.ResourceRegistry.TryGetValue((StringName)regKey, out var resourceMeta))
		{
			resourceMeta.PseudoSockets = storedAnchors;
			registryService.ResourceRegistry[(StringName)regKey] = resourceMeta;
		}
	}

	private static List<PseudoSocketAnchor>? ClonePseudoSockets(List<PseudoSocketAnchor>? source)
	{
		if (source == null || source.Count == 0) return null;

		var clone = new List<PseudoSocketAnchor>(source.Count);
		foreach (var anchor in source)
		{
			if (anchor != null) clone.Add(anchor.Clone());
		}

		return clone;
	}
}
