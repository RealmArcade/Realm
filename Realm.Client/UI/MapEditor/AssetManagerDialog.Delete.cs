using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.EditorAPI;
using Realm.Shared.Audio;
using Realm.Shared.Textures;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

public partial class AssetManagerDialog
{
	private void OnDeleteAssetClicked()
	{
		if (_selectedAsset == null) return;

		string wsPath = GetWorkspacePath();
		string assetFileName = _selectedAsset.FileName;
		string categoryKey = _selectedAsset.AssetType?.ToCanonicalString() ?? "Prop";

		var orphanedObjects = FindOrphanedObjectsInMetadata(wsPath, assetFileName);

		if (orphanedObjects.Count > 0)
		{
			string objectListStr = string.Join("\n • ", orphanedObjects.Take(8));
			if (orphanedObjects.Count > 8)
			{
				objectListStr += $"\n • ... and {orphanedObjects.Count - 8} more";
			}

			string warningMessage = string.Format(
				TranslationServer.Translate("Warning: Asset '{0}' is referenced by {1} object(s) in metadata.json:\n\n • {2}\n\nDeleting this asset from manifest.json will orphan these objects. Are you sure you want to proceed?"),
				assetFileName, orphanedObjects.Count, objectListStr);

			Hud?.ShowConfirmationDialog(
				warningMessage,
				() => ExecuteDeleteAsset(wsPath, categoryKey, assetFileName),
				confirmText: TranslationServer.Translate("DELETE ANYWAY"),
				cancelText: TranslationServer.Translate("CANCEL")
			);
		}
		else
		{
			string confirmMessage = string.Format(TranslationServer.Translate("Are you sure you want to remove asset '{0}' from manifest.json?"), assetFileName);
			Hud?.ShowConfirmationDialog(
				confirmMessage,
				() => ExecuteDeleteAsset(wsPath, categoryKey, assetFileName),
				confirmText: TranslationServer.Translate("DELETE"),
				cancelText: TranslationServer.Translate("CANCEL")
			);
		}
	}

	private List<string> FindOrphanedObjectsInMetadata(string wsPath, string assetFileName)
	{
		var orphans = new List<string>();
		string metaPath = Path.Combine(wsPath, "metadata.json");
		if (!File.Exists(metaPath)) return orphans;

		try
		{
			string metaJson = File.ReadAllText(metaPath);
			var metaRoot = JsonNode.Parse(metaJson)?.AsObject();
			if (metaRoot == null) return orphans;

			JsonObject? templatesObj = GetTemplatesObject(metaRoot);
			if (templatesObj == null) return orphans;

			string cleanName = Path.GetFileName(assetFileName);
			string cleanNoExt = Path.GetFileNameWithoutExtension(assetFileName);

			CheckTemplatesForOrphans(templatesObj, cleanName, cleanNoExt, orphans);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetManagerDialog] FindOrphanedObjectsInMetadata error: {ex.Message}");
		}

		return orphans;
	}

	private static JsonObject? GetTemplatesObject(JsonObject metaRoot)
	{
		if (metaRoot.TryGetPropertyValue("Templates", out var templatesNode) && templatesNode is JsonObject tObj)
		{
			return tObj;
		}

		return null;
	}

	private static void CheckTemplatesForOrphans(JsonObject templatesObj, string cleanName, string cleanNoExt, List<string> orphans)
	{
		var arrayKeys = new (string Key, string Label)[]
		{
			("Units", "Unit"),
			("Buildings", "Building"),
			("Resources", "Resource"),
			("Props", "Prop"),
			("Attachments", "Attachment"),
			("Weapons", "Weapon"),
			("Abilities", "Ability"),
			("TechTree", "Tech"),
			("StatusEffects", "Status Effect")
		};

		foreach (var (arrKey, label) in arrayKeys)
		{
			if (templatesObj.TryGetPropertyValue(arrKey, out var arrNode) && arrNode is JsonArray arr)
			{
				CheckJsonArrayForOrphans(arr, label, cleanName, cleanNoExt, orphans);
			}
		}
	}

	private static void CheckJsonArrayForOrphans(JsonArray arr, string label, string cleanName, string cleanNoExt, List<string> orphans)
	{
		foreach (var itemNode in arr)
		{
			if (itemNode is JsonObject obj && IsObjectOrphaned(obj, cleanName, cleanNoExt))
			{
				string name = GetObjectName(obj);
				orphans.Add($"{label}: {name}");
			}
		}
	}

	private static string GetObjectName(JsonObject obj)
	{
		return obj["Name"]?.ToString() ?? obj["UnitId"]?.ToString() ?? obj["AbilityId"]?.ToString() ?? obj["Id"]?.ToString() ?? "Unknown";
	}

	private static bool IsObjectOrphaned(JsonObject obj, string cleanName, string cleanNoExt)
	{
		foreach (var prop in obj)
		{
			if (prop.Value is JsonValue jv && MatchString(jv.ToString(), cleanName, cleanNoExt))
			{
				return true;
			}
		}

		return false;
	}

	private static bool MatchString(string? val, string cleanName, string cleanNoExt)
	{
		if (string.IsNullOrWhiteSpace(val)) return false;
		string v = val.Trim();
		return string.Equals(v, cleanName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(v, cleanNoExt, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(Path.GetFileName(v), cleanName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(Path.GetFileNameWithoutExtension(v), cleanNoExt, StringComparison.OrdinalIgnoreCase);
	}

	private void ExecuteDeleteAsset(string wsPath, string categoryKey, string assetFileName)
	{
		try
		{
			MapAssetHelper.RemoveManifestAsset(wsPath, categoryKey, assetFileName);

			string subFolder = GetAssetSubFolder(categoryKey, categoryKey.ToLowerInvariant());
			string fullPath = Path.Combine(wsPath, "Assets", subFolder, assetFileName);

			if (File.Exists(fullPath))
			{
				File.Delete(fullPath);
			}

			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Deleted asset '{0}' from manifest.json"), assetFileName));
			RefreshAssetList();
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetManagerDialog] ExecuteDeleteAsset error: {ex.Message}");
			Hud?.ShowFeedback($"Error deleting asset: {ex.Message}");
		}
	}
}
