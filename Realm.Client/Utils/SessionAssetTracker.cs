using System;
using System.Collections.Generic;
using System.IO;
using Realm.Client.Services;
using Realm.Shared.Distribution;
using Realm.Shared.Services;

namespace Realm.Client.Utils;

public class SessionAssetTracker
{
	public Dictionary<string, string> BaselineManifestAssets { get; } = new(StringComparer.OrdinalIgnoreCase);
	public HashSet<string> SessionImportedAssetNames { get; } = new(StringComparer.OrdinalIgnoreCase);
	public string? InitialRandomizeTemplateJson { get; set; }
	public string? RandomizeTemplateSlug { get; set; }

	public void CaptureBaselineManifestSnapshot(string? workspacePath = null)
	{
		BaselineManifestAssets.Clear();
		string wsPath = !string.IsNullOrEmpty(workspacePath)
			? workspacePath
			: MapWorkspaceService.GetActiveWorkspacePath();

		if (!string.IsNullOrEmpty(wsPath) && Directory.Exists(wsPath))
		{
			var manifest = MapFileService.LoadManifest(wsPath);
			if (manifest?.Assets != null)
			{
				MapManifest.FlattenAssetsInto(BaselineManifestAssets, manifest.Assets);
				foreach (var catKvp in manifest.Assets.GetAllCategories())
				{
					foreach (var item in catKvp.Value)
					{
						BaselineManifestAssets[item.Key] = item.Value;
					}
				}
			}
		}
	}

	public void RecordImportedAssets(string? workspacePath = null)
	{
		string wsPath = !string.IsNullOrEmpty(workspacePath)
			? workspacePath
			: MapWorkspaceService.GetActiveWorkspacePath();

		if (string.IsNullOrEmpty(wsPath) || !Directory.Exists(wsPath))
		{
			return;
		}

		var postManifest = MapFileService.LoadManifest(wsPath);
		if (postManifest?.Assets == null)
		{
			return;
		}

		var postAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		MapManifest.FlattenAssetsInto(postAssets, postManifest.Assets);

		foreach (var kvp in postAssets)
		{
			string fileName = Path.GetFileName(kvp.Key);
			if (!BaselineManifestAssets.ContainsKey(kvp.Key) && !BaselineManifestAssets.ContainsKey(fileName))
			{
				SessionImportedAssetNames.Add(fileName);
			}
		}

		foreach (var catKvp in postManifest.Assets.GetAllCategories())
		{
			foreach (var item in catKvp.Value)
			{
				string fileName = Path.GetFileName(item.Key);
				if (!BaselineManifestAssets.ContainsKey(item.Key) && !BaselineManifestAssets.ContainsKey(fileName))
				{
					SessionImportedAssetNames.Add(fileName);
				}
			}
		}
	}

	public void PruneUnreferencedSessionAssets(bool revertSnapshot = false, string? workspacePath = null)
	{
		string wsPath = !string.IsNullOrEmpty(workspacePath)
			? workspacePath
			: MapWorkspaceService.GetActiveWorkspacePath();

		if (string.IsNullOrEmpty(wsPath) || !Directory.Exists(wsPath))
		{
			Reset();
			return;
		}

		if (revertSnapshot && !string.IsNullOrEmpty(RandomizeTemplateSlug))
		{
			EditorService.RevertTemplateJson(wsPath, RandomizeTemplateSlug, InitialRandomizeTemplateJson);
		}

		if (SessionImportedAssetNames.Count == 0)
		{
			Reset();
			return;
		}

		PruneUnreferencedAssetsCore(wsPath, SessionImportedAssetNames, BaselineManifestAssets);
		Reset();
	}

	public static void PruneUnreferencedAssetsCore(string wsPath, ISet<string> sessionImportedAssetNames, IDictionary<string, string> baselineManifestAssets)
	{
		if (sessionImportedAssetNames.Count == 0 || string.IsNullOrEmpty(wsPath) || !Directory.Exists(wsPath))
		{
			return;
		}

		var metadata = MapFileService.LoadMetadata(wsPath);
		string metadataJson = MapFileService.SaveMetadataToJson(metadata);
		var manifest = MapFileService.LoadManifest(wsPath);
		var assetsDir = Path.Combine(wsPath, "Assets");

		foreach (string rawAssetName in sessionImportedAssetNames)
		{
			string fileName = Path.GetFileName(rawAssetName);
			if (baselineManifestAssets.ContainsKey(rawAssetName) || baselineManifestAssets.ContainsKey(fileName))
			{
				continue;
			}

			if (!MapAssetHelper.IsAssetReferencedInMetadataJson(metadataJson, fileName) &&
			    !MapAssetHelper.IsAssetReferencedInMetadataJson(metadataJson, rawAssetName))
			{
				if (manifest?.Assets != null)
				{
					foreach (var catKvp in manifest.Assets.GetAllCategories())
					{
						if (catKvp.Value.ContainsKey(fileName))
						{
							MapAssetHelper.RemoveManifestAsset(wsPath, catKvp.Key, fileName);
						}
						else if (catKvp.Value.ContainsKey(rawAssetName))
						{
							MapAssetHelper.RemoveManifestAsset(wsPath, catKvp.Key, rawAssetName);
						}
					}
				}

				DeleteAssetFileSafely(wsPath, assetsDir, fileName);
			}
		}
	}

	public static void DeleteAssetFileSafely(string wsPath, string assetsDir, string assetName)
	{
		if (!Directory.Exists(assetsDir)) return;

		string fullAssetsDir = Path.GetFullPath(assetsDir);
		try
		{
			foreach (var file in Directory.EnumerateFiles(fullAssetsDir, assetName, SearchOption.AllDirectories))
			{
				string fullFile = Path.GetFullPath(file);
				if (fullFile.StartsWith(fullAssetsDir, StringComparison.OrdinalIgnoreCase) && File.Exists(fullFile))
				{
					File.Delete(fullFile);
				}
			}
		}
		catch
		{
		}
	}

	public void Reset()
	{
		SessionImportedAssetNames.Clear();
		BaselineManifestAssets.Clear();
		InitialRandomizeTemplateJson = null;
		RandomizeTemplateSlug = null;
	}
}
