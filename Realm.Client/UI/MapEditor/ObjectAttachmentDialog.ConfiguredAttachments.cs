using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

public partial class ObjectAttachmentDialog
{
	public static string GetAttachmentKey(string socketId, string attachmentId, int index = -1, string? parentAttachmentId = null)
	{
		string cleanAtt = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: Path.GetFileNameWithoutExtension(attachmentId);
		string normSocket = NormalizeSocketId(socketId);
		string parentPart = string.IsNullOrEmpty(parentAttachmentId) ? "" : $"_p_{Path.GetFileNameWithoutExtension(parentAttachmentId)}";
		return index >= 0
			? $"{normSocket}_{index}_{cleanAtt}{parentPart}"
			: $"{normSocket}_{cleanAtt}{parentPart}";
	}

	private List<ConfiguredAttachmentEntry> GetConfiguredAttachments()
	{
		bool isNonRigged = _isTargetBuilding || _currentAvailableSockets.Any(s => s.SocketId == "Top" || s.SocketId == "Base" || s.SocketId == "Pivot");
		return GetConfiguredAttachmentsFromData(WorkingAttachments, isNonRigged);
	}

	public static List<ConfiguredAttachmentEntry> GetConfiguredAttachmentsForObject(string targetObjectId, bool isBuilding = false)
	{
		if (GameHost.TryGetUnitOrBuildingMetadata(targetObjectId, out var meta))
		{
			return GetConfiguredAttachmentsFromData(meta.ObjectAttachments, isBuilding);
		}

		return new List<ConfiguredAttachmentEntry>();
	}

	public static List<ConfiguredAttachmentEntry> GetConfiguredAttachmentsFromData(UnitObjectAttachments? attsNode, bool isBuilding = false)
	{
		var list = new List<ConfiguredAttachmentEntry>();
		if (attsNode == null) return list;

		if (isBuilding)
		{
			CollectBuildingAttachments(list, attsNode);
		}
		else
		{
			CollectUnitAttachments(list, attsNode);
		}

		return list;
	}

	private static void CollectBuildingAttachments(List<ConfiguredAttachmentEntry> list, UnitObjectAttachments atts)
	{
		CollectToEntryList(list, "Center", atts.center ?? atts.chest);
		CollectToEntryList(list, "Top", atts.overhead ?? atts.head);
		CollectToEntryList(list, "Base", atts.ground ?? atts.root);
		CollectToEntryList(list, "Pivot", atts.pivot ?? atts.right_hand);
		CollectCustomSocketAttachments(list, atts);
	}

	private static void CollectCustomSocketAttachments(List<ConfiguredAttachmentEntry> list, UnitObjectAttachments atts)
	{
		var customSocketLists = atts.CustomSocketLists;
		if (customSocketLists == null) return;

		foreach (var socketEntry in customSocketLists)
		{
			CollectToEntryList(list, socketEntry.Key, socketEntry.Value);
		}
	}

	private static void CollectUnitAttachments(List<ConfiguredAttachmentEntry> list, UnitObjectAttachments atts)
	{
		CollectToEntryList(list, "RightHand", atts.right_hand);
		CollectToEntryList(list, "LeftHand", atts.left_hand);
		CollectToEntryList(list, "Chest", atts.chest);
		CollectToEntryList(list, "Hips", atts.root);
		CollectToEntryList(list, "Head", atts.head);
		CollectToEntryList(list, "LeftFoot", atts.left_foot);
		CollectToEntryList(list, "RightFoot", atts.right_foot);
		CollectToEntryList(list, "Ground", atts.ground);
		CollectToEntryList(list, "Center", atts.center);
		CollectToEntryList(list, "Overhead", atts.overhead);
		CollectToEntryList(list, "Pivot", atts.pivot);
		CollectCustomSocketAttachments(list, atts);
	}

	private static void CollectToEntryList(List<ConfiguredAttachmentEntry> list, string socket, List<Dictionary<string, HandAttachmentOrientation>>? sockList)
	{
		if (sockList == null) return;
		for (int i = 0; i < sockList.Count; i++)
		{
			var dict = sockList[i];
			if (dict == null) continue;
			foreach (var kvp in dict)
			{
				list.Add(new ConfiguredAttachmentEntry
				{
					Index = i,
					SocketId = NormalizeSocketId(socket),
					AttachmentId = kvp.Key,
					Orientation = kvp.Value
				});
			}
		}
	}

	private void ResetConfiguredVisualTransforms()
	{
		var configured = GetConfiguredAttachments();
		for (int i = 0; i < configured.Count; i++)
		{
			var entry = configured[i];
			string key = GetAttachmentKey(entry.SocketId, entry.AttachmentId, entry.Index, entry.Orientation.ParentAttachmentId);
			if (_activeAttachmentVisuals.TryGetValue(key, out var visualNode) && GodotObject.IsInstanceValid(visualNode))
			{
				visualNode.Position = entry.Orientation.Position.ToGodotVector3() + (Vector3.Up * entry.Orientation.NormalOffset);
				visualNode.RotationDegrees = entry.Orientation.RotationDegrees.ToGodotVector3();
				var entryScale = entry.Orientation.ScaleVector.ToGodotVector3();
				visualNode.Scale = entryScale == Vector3.Zero ? Vector3.One : entryScale;
			}
		}
	}
}
