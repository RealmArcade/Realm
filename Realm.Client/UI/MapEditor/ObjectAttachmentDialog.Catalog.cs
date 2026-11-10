// The attachment id catalogue the picker offers: VFX registry, workspace assets, metadata, map
// templates and the running host are unioned into one ordered, case-insensitive set.

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
	public static List<string> GetAvailableObjectAttachmentIds()
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var result = new List<string>();

		if (seen.Add(Unit3D.PlayerColorGlowAttachmentId)) result.Add(Unit3D.PlayerColorGlowAttachmentId);

		AddVfxRegistryAttachments(seen, result);

		string wsPath = !string.IsNullOrEmpty(MapWorkspaceService.GetActiveWorkspacePath())
			? MapWorkspaceService.GetActiveWorkspacePath()
			: GameContext.EditorService._editingDirectory;

		AddUnionedAssets(wsPath, seen, result);
		AddMetadataAssets(wsPath, seen, result);
		AddGameHostAttachments(seen, result);

		return result;
	}

	private static void AddVfxRegistryAttachments(HashSet<string> seen, List<string> result)
	{
		if (GameContext.RegistryService.VfxRegistry == null) return;
		foreach (var kvp in GameContext.RegistryService.VfxRegistry)
		{
			string vfxKey = kvp.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? kvp.Key : $"vfx:{kvp.Key}";
			if (seen.Add(vfxKey)) result.Add(vfxKey);
		}
	}

	private static void AddUnionedAssets(string wsPath, HashSet<string> seen, List<string> result)
	{
		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			if (assetsObj == null) return;

			var itemsDict = assetsObj.GetCategory("Item");
			if (itemsDict != null)
			{
				foreach (var modelProp in itemsDict)
				{
					string id = Path.GetFileNameWithoutExtension(modelProp.Key);
					if (seen.Add(id)) result.Add(id);
				}
			}

			var vfxDict = assetsObj.GetCategory("Spritesheet");
			if (vfxDict != null)
			{
				foreach (var prop in vfxDict)
				{
					string vfxKey = prop.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? prop.Key : $"vfx:{prop.Key}";
					if (seen.Add(vfxKey)) result.Add(vfxKey);
				}
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[ObjectAttachmentDialog] Error loading unioned assets: {ex.Message}");
		}
	}

	private static void AddMetadataAssets(string wsPath, HashSet<string> seen, List<string> result)
	{
		try
		{
			var metadata = GameContext.MetadataService.LoadMetadata(wsPath);
			if (metadata.Templates == null) return;

			ProcessTemplates(metadata.Templates, seen, result);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[ObjectAttachmentDialog] Error scanning metadata: {ex.Message}");
		}
	}

	private static void ProcessTemplates(TemplateContainer templates, HashSet<string> seen, List<string> result)
	{
		if (templates.Items != null)
			foreach (var it in templates.Items)
				TryAddTemplateId(it.TemplateID, seen, result);

		if (templates.Weapons != null)
			foreach (var wpn in templates.Weapons)
				TryAddTemplateId(wpn.TemplateID, seen, result);

		if (templates.Attachments != null)
			foreach (var att in templates.Attachments)
				TryAddTemplateId(att.AttachmentId, seen, result);

		if (templates.Vfx != null)
			foreach (var vfx in templates.Vfx)
				TryAddVfx(vfx.VfxId, seen, result);
	}

	private static void TryAddVfx(string? vfxId, HashSet<string> seen, List<string> result)
	{
		if (string.IsNullOrEmpty(vfxId)) return;
		string vfxKey = $"vfx:{vfxId}";
		if (seen.Add(vfxKey)) result.Add(vfxKey);
	}

	private static void TryAddTemplateId(string? templateId, HashSet<string> seen, List<string> result)
	{
		if (string.IsNullOrEmpty(templateId)) return;
		string cleanId = Path.GetFileNameWithoutExtension(templateId);
		if (seen.Add(cleanId)) result.Add(cleanId);
	}

	private static void AddGameHostAttachments(HashSet<string> seen, List<string> result)
	{
		if (GameHost.AttachmentRegistry == null) return;
		foreach (var kvp in GameHost.AttachmentRegistry)
		{
			string id = Path.GetFileNameWithoutExtension(kvp.Key);
			if (seen.Add(id)) result.Add(id);
		}
	}
}
