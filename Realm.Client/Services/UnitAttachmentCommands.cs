﻿using System;
using System.Collections.Generic;
using System.IO;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     Writing a unit's object attachments: resolving which registry a unit id belongs to, updating the
///     live registry and the metadata file, applying the result to the running instances, and the
///     save/restore pair the editor's undo uses.
/// </summary>
internal static class UnitAttachmentCommands
{
	internal static void SaveUnitObjectAttachment(string unitId, HumanoidBone hand, string attachmentId, HandAttachmentOrientation orientation, string wsPath)
	{
		string handKey = hand switch
		{
			HumanoidBone.LeftHand => "left_hand",
			HumanoidBone.RightHand => "right_hand",
			HumanoidBone.Chest => "chest",
			HumanoidBone.Hips => "root",
			HumanoidBone.Head => "head",
			HumanoidBone.LeftFoot => "left_foot",
			HumanoidBone.RightFoot => "right_foot",
			_ => "right_hand"
		};
		SaveUnitObjectAttachment(unitId, handKey, attachmentId, orientation, wsPath);
	}

	internal static void SaveUnitObjectAttachment(string unitId, string socket, string attachmentId, HandAttachmentOrientation orientation, string? wsPath = null)
	{
		if (string.IsNullOrEmpty(unitId) || string.IsNullOrEmpty(attachmentId)) return;

		var (regKey, isBuildingMeta) = ResolveRegistryKeyAndType(unitId);

		UpdateLocalRegistry(regKey, isBuildingMeta, socket, attachmentId, orientation);

		UpdateMetadataFile(unitId, regKey, socket, attachmentId, orientation, wsPath);

		ApplyAttachmentsToGameInstances(unitId, regKey);
	}

	internal static (string regKey, bool isBuildingMeta) ResolveRegistryKeyAndType(string unitId)
	{
		string regKey = unitId;
		bool isBuildingMeta = false;

		if (GameHost.UnitRegistry.ContainsKey(unitId)) return (regKey, isBuildingMeta);
		if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(unitId)) return (regKey, true);

		string cleanId = Path.GetFileNameWithoutExtension(unitId);
		if (GameHost.UnitRegistry.ContainsKey(cleanId)) return (cleanId, false);
		if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(cleanId)) return (cleanId, true);

		return ResolveRegistryKeyByIterating(unitId, cleanId);
	}

	internal static (string regKey, bool isBuildingMeta) ResolveRegistryKeyByIterating(string unitId, string cleanId)
	{
		string regKey = unitId;
		bool isBuildingMeta = false;

		foreach (var k in GameHost.UnitRegistry.Keys)
		{
			string kStr = k.ToString();
			if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
			    kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
			    Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
			{
				return (k.ToString(), false);
			}
		}

		if (GameHost.BuildingRegistry != null)
		{
			foreach (var k in GameHost.BuildingRegistry.Keys)
			{
				string kStr = k.ToString();
				if (kStr.Equals(unitId, StringComparison.OrdinalIgnoreCase) ||
				    kStr.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
				    Path.GetFileNameWithoutExtension(kStr).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
				{
					return (k.ToString(), true);
				}
			}
		}

		return (regKey, isBuildingMeta);
	}

	internal static void UpdateLocalRegistry(string regKey, bool isBuildingMeta, string socket, string attachmentId, HandAttachmentOrientation orientation)
	{
		if (isBuildingMeta && GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.TryGetValue(regKey, out var bMeta))
		{
			bMeta.SetObjectAttachment(socket, attachmentId, orientation);
			GameHost.BuildingRegistry[regKey] = bMeta;
		}
		else if (GameHost.UnitRegistry.TryGetValue(regKey, out var uMeta))
		{
			uMeta.SetObjectAttachment(socket, attachmentId, orientation);
			GameHost.UnitRegistry[regKey] = uMeta;
		}
	}

	internal static void UpdateMetadataFile(string unitId, string regKey, string socket, string attachmentId, HandAttachmentOrientation orientation, string? wsPath = null)
	{
		string metadataPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metadataPath)) return;

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			bool updated = meta.UpdateUnit(unitId, u =>
			{
				u.SetObjectAttachment(socket, attachmentId, orientation);
				return u;
			});
			if (!updated)
				updated = meta.UpdateBuilding(unitId, b =>
				{
					b.SetObjectAttachment(socket, attachmentId, orientation);
					return b;
				});
			if (!updated)
				updated = meta.UpdateUnit(regKey, u =>
				{
					u.SetObjectAttachment(socket, attachmentId, orientation);
					return u;
				});
			if (!updated)
				meta.UpdateBuilding(regKey, b =>
				{
					b.SetObjectAttachment(socket, attachmentId, orientation);
					return b;
				});
		});

				GameContext.EditorService.LastMetadataSyncTime = GameContext.EditorService.GetLastWriteTimeSafe(metadataPath);
	}

	internal static void ApplyAttachmentsToGameInstances(string unitId, string regKey)
	{
		if (GameContext.Host == null) return;

		if (GameContext.Host.AllUnits != null)
		{
			foreach (var u in GameContext.Host.AllUnits)
			{
				if (u != null && IsMatchingUnit(u.UnitId, unitId, regKey))
				{
					u.ApplyAllConfiguredAttachments();
				}
			}
		}

		if (GameContext.Host.SelectedEditorObject is Unit3D selUnit)
		{
			if (IsMatchingUnit(selUnit.UnitId, unitId, regKey))
			{
				selUnit.ApplyAllConfiguredAttachments();
			}
		}
	}

	internal static bool IsMatchingUnit(string instanceUnitId, string unitId, string regKey)
	{
		return instanceUnitId == unitId ||
		       instanceUnitId == regKey ||
		       Path.GetFileNameWithoutExtension(instanceUnitId ?? "").Equals(Path.GetFileNameWithoutExtension(unitId), StringComparison.OrdinalIgnoreCase);
	}

	internal static void SaveAllUnitObjectAttachments(string targetId, UnitObjectAttachments? attachments, Action<string, UnitObjectAttachments?> restoreCallback)
	{
		restoreCallback?.Invoke(targetId, attachments);
	}

	internal static void SaveUnitPseudoSockets(string targetId, List<PseudoSocketAnchor>? pseudoSockets, Action<string, List<PseudoSocketAnchor>?> restoreCallback)
	{
		restoreCallback?.Invoke(targetId, pseudoSockets);
	}
}
