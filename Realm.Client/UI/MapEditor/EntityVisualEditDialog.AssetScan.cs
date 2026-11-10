using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

public partial class EntityVisualEditDialog
{
	private List<string> ScanCompatibleModels(bool allFolders)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		bool isMesh = string.Equals(_visualMode, "Mesh", StringComparison.OrdinalIgnoreCase);

		if (isMesh)
		{
			ScanCompatibleMeshes(wsPath, allFolders, results);
		}
		else
		{
			ScanCompatibleDecals(wsPath, results);
		}

		var list = results.ToList();
		list.Sort();
		return list;
	}


	private void ScanCompatibleMeshes(string wsPath, bool allFolders, HashSet<string> results)
	{
		ScanMeshMetadataAssets(wsPath, allFolders, results);
		ScanMeshDirectoryAssets(wsPath, allFolders, results);

		if (results.Count == 0)
		{
			var fallbackModels = ScanAvailableAssets("models", allFolders);
			foreach (var f in fallbackModels)
			{
				if (f.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
				{
					results.Add(Path.GetFileName(f));
				}
			}
		}
	}

	private void ScanMeshMetadataAssets(string wsPath, bool allFolders, HashSet<string> results)
	{
		try
		{
			var assets = MapAssetHelper.LoadAssets(wsPath);
			if (assets == null) return;

			foreach (var groupKvp in assets.GetAllCategories())
			{
				bool matches = allFolders || IsCategoryMatch(groupKvp.Key);
				if (!matches) continue;

				foreach (var itemKvp in groupKvp.Value)
				{
					if (itemKvp.Key.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
					{
						results.Add(Path.GetFileName(itemKvp.Key));
					}
				}
			}
		}
		catch
		{
		}
	}

	private bool IsCategoryMatch(string groupName)
	{
		if (string.IsNullOrEmpty(groupName)) return false;

		return _category switch
		{
			"units" => IsUnitGroup(groupName),
			"buildings" => IsBuildingGroup(groupName),
			"resources" => IsResourceGroup(groupName),
			"props" => IsPropGroup(groupName),
			_ => true
		};
	}

	private bool IsUnitGroup(string groupName)
	{
		return groupName.Equals("Character", StringComparison.OrdinalIgnoreCase) ||
		       groupName.Equals("units", StringComparison.OrdinalIgnoreCase) ||
		       groupName.Equals("characters", StringComparison.OrdinalIgnoreCase);
	}

	private bool IsBuildingGroup(string groupName)
	{
		return groupName.Equals("Building", StringComparison.OrdinalIgnoreCase) ||
		       groupName.Equals("buildings", StringComparison.OrdinalIgnoreCase);
	}

	private bool IsResourceGroup(string groupName)
	{
		return groupName.Equals("Resource", StringComparison.OrdinalIgnoreCase) ||
		       groupName.Equals("resources", StringComparison.OrdinalIgnoreCase) ||
		       groupName.Equals("Prop", StringComparison.OrdinalIgnoreCase) ||
		       groupName.Equals("props", StringComparison.OrdinalIgnoreCase);
	}

	private bool IsPropGroup(string groupName)
	{
		return groupName.Equals("Prop", StringComparison.OrdinalIgnoreCase) ||
		       groupName.Equals("props", StringComparison.OrdinalIgnoreCase) ||
		       groupName.Equals("Item", StringComparison.OrdinalIgnoreCase) ||
		       groupName.Equals("items", StringComparison.OrdinalIgnoreCase) ||
		       groupName.Equals("Resource", StringComparison.OrdinalIgnoreCase) ||
		       groupName.Equals("resources", StringComparison.OrdinalIgnoreCase);
	}

	private void ScanMeshDirectoryAssets(string wsPath, bool allFolders, HashSet<string> results)
	{
		string[] modelSubFolders = GetModelSubFolders(allFolders);
		string[] baseLocations = GetBaseLocations(wsPath);

		foreach (var baseLoc in baseLocations)
		{
			if (!Directory.Exists(baseLoc)) continue;

			ScanSubDirectories(baseLoc, modelSubFolders, results);
			ScanTopDirectory(baseLoc, results);
		}
	}

	private string[] GetModelSubFolders(bool allFolders)
	{
		return _category switch
		{
			"units" => allFolders ? new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" } : new[] { "units" },
			"buildings" => allFolders ? new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" } : new[] { "buildings" },
			"resources" => allFolders ? new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" } : new[] { "resources", "props" },
			"props" => allFolders ? new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" } : new[] { "props", "resources", "items" },
			_ => new[] { "units", "buildings", "props", "resources", "items", "projectiles", "attachments" }
		};
	}

	private string[] GetBaseLocations(string wsPath)
	{
		return new[]
		{
			Path.Combine(wsPath, "Assets", "models"),
			Path.Combine("MapTemplate", "Assets", "models"),
			Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "models"),
			Path.Combine(ProjectSettings.GlobalizePath("res://"), "MapTemplate", "Assets", "models")
		};
	}

	private void ScanSubDirectories(string baseLoc, string[] modelSubFolders, HashSet<string> results)
	{
		foreach (var sub in modelSubFolders)
		{
			string subDir = Path.Combine(baseLoc, sub);
			if (Directory.Exists(subDir))
			{
				foreach (var file in Directory.GetFiles(subDir, "*.rmesh", SearchOption.AllDirectories))
				{
					results.Add(Path.GetFileName(file));
				}
			}
		}
	}

	private void ScanTopDirectory(string baseLoc, HashSet<string> results)
	{
		foreach (var file in Directory.GetFiles(baseLoc, "*.rmesh", SearchOption.TopDirectoryOnly))
		{
			results.Add(Path.GetFileName(file));
		}
	}

	private void ScanCompatibleDecals(string wsPath, HashSet<string> results)
	{
		try
		{
			var assets = MapAssetHelper.LoadAssets(wsPath);
			if (assets != null)
			{
				foreach (var groupKvp in assets.GetAllCategories())
				{
					foreach (var itemKvp in groupKvp.Value)
					{
						if (itemKvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
						{
							results.Add(Path.GetFileName(itemKvp.Key));
						}
					}
				}
			}
		}
		catch
		{
		}

		string[] candidateFolders = new[]
		{
			Path.Combine(wsPath, "Assets", "decals"),
			Path.Combine(wsPath, "Assets", "textures"),
			Path.Combine("MapTemplate", "Assets", "decals"),
			Path.Combine("MapTemplate", "Assets", "textures"),
			Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "decals"),
			Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "textures"),
			Path.Combine(ProjectSettings.GlobalizePath("res://"), "MapTemplate", "Assets", "decals"),
			Path.Combine(ProjectSettings.GlobalizePath("res://"), "MapTemplate", "Assets", "textures")
		};

		foreach (var folder in candidateFolders)
		{
			if (Directory.Exists(folder))
			{
				foreach (var file in Directory.GetFiles(folder, "*.rtex", SearchOption.AllDirectories))
				{
					results.Add(Path.GetFileName(file));
				}
			}
		}
	}


	private List<string> ScanPortraitModels(bool allFolders)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		LoadPortraitMetadata(wsPath, results);

		string[] baseLocations = new[]
		{
			Path.Combine(wsPath, "Assets", "models"),
			Path.Combine("MapTemplate", "Assets", "models"),
			Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "models"),
			Path.Combine(ProjectSettings.GlobalizePath("res://"), "MapTemplate", "Assets", "models")
		};

		foreach (var baseLoc in baseLocations)
		{
			if (Directory.Exists(baseLoc))
			{
				foreach (var file in Directory.GetFiles(baseLoc, "*.rmesh", SearchOption.AllDirectories))
				{
					results.Add(Path.GetFileName(file));
				}
			}
		}

		if (results.Count == 0)
		{
			ScanFallbackAssets(allFolders, results);
		}

		var list = results.ToList();
		list.Sort();
		return list;
	}

	private void LoadPortraitMetadata(string wsPath, HashSet<string> results)
	{
		try
		{
			var assets = MapAssetHelper.LoadAssets(wsPath);
			if (assets != null)
			{
				foreach (var groupKvp in assets.GetAllCategories())
				{
					foreach (var itemKvp in groupKvp.Value)
					{
						if (itemKvp.Key.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
						{
							results.Add(Path.GetFileName(itemKvp.Key));
						}
					}
				}
			}
		}
		catch
		{
		}
	}

	private void ScanFallbackAssets(bool allFolders, HashSet<string> results)
	{
		var fallbackModels = ScanAvailableAssets("models", allFolders);
		foreach (var f in fallbackModels)
		{
			if (f.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			{
				results.Add(Path.GetFileName(f));
			}
		}
	}
}
