using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Distribution;
using Realm.Shared.Services;

namespace Realm.Client.Utils;


public static partial class MapAssetHelper
{
	public static string? FindModelOnDisk(string targetDirectory, string? preferredSubCategory, string fileName, out string resolvedSubCategory)
	{
		resolvedSubCategory = !string.IsNullOrEmpty(preferredSubCategory) ? preferredSubCategory : "props";
		if (string.IsNullOrEmpty(targetDirectory) || !Directory.Exists(targetDirectory)) return null;

		string modelsDir = Path.Combine(targetDirectory, "Assets", "models");
		if (!Directory.Exists(modelsDir)) return null;

		var candidateFiles = GetCandidateModelFiles(fileName);

		string? foundPath = CheckPreferredCategory(modelsDir, preferredSubCategory, candidateFiles, ref resolvedSubCategory);
		if (foundPath != null) return foundPath;

		foundPath = CheckKnownSubCategories(modelsDir, candidateFiles, ref resolvedSubCategory);
		if (foundPath != null) return foundPath;

		foundPath = CheckUnknownSubCategories(modelsDir, candidateFiles, ref resolvedSubCategory);
		if (foundPath != null) return foundPath;

		return CheckDirectPath(modelsDir, candidateFiles);
	}

	private static readonly string[] KnownModelSubCategories = { "units", "buildings", "resources", "props", "projectiles", "attachments", "weapons", "environment" };

	private static string? CheckPreferredCategory(string modelsDir, string? preferredSubCategory, string[] candidateFiles, ref string resolvedSubCategory)
	{
		if (string.IsNullOrEmpty(preferredSubCategory)) return null;

		string prefSub = NormalizeGlbSubCategory(preferredSubCategory);
		string? prefPath = TryFindModelInSubDir(modelsDir, prefSub, candidateFiles);
		if (prefPath != null)
		{
			resolvedSubCategory = prefSub;
			return prefPath;
		}

		return null;
	}

	private static string? CheckKnownSubCategories(string modelsDir, string[] candidateFiles, ref string resolvedSubCategory)
	{
		foreach (var sub in KnownModelSubCategories)
		{
			string? candPath = TryFindModelInSubDir(modelsDir, sub, candidateFiles);
			if (candPath != null)
			{
				resolvedSubCategory = sub;
				return candPath;
			}
		}

		return null;
	}

	private static string? CheckUnknownSubCategories(string modelsDir, string[] candidateFiles, ref string resolvedSubCategory)
	{
		foreach (var dir in Directory.GetDirectories(modelsDir))
		{
			string sub = Path.GetFileName(dir).ToLowerInvariant();
			if (KnownModelSubCategories.Contains(sub)) continue;

			string? candPath = TryFindModelInSubDir(modelsDir, sub, candidateFiles);
			if (candPath != null)
			{
				resolvedSubCategory = sub;
				return candPath;
			}
		}

		return null;
	}

	private static string? CheckDirectPath(string modelsDir, string[] candidateFiles)
	{
		foreach (var cand in candidateFiles)
		{
			string directPath = Path.Combine(modelsDir, cand);
			if (File.Exists(directPath)) return directPath;
		}

		return null;
	}

	private static string[] GetCandidateModelFiles(string fileName)
	{
		var (pType, pSlug) = TemplateIDHelper.ParseTemplateID(fileName);
		string effectiveName = (!string.IsNullOrEmpty(pType) && !string.IsNullOrEmpty(pSlug)) ? pSlug : fileName;

		if (effectiveName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			return new[] { effectiveName, Path.GetFileName(fileName) };
		}

		return new[]
		{
			$"{effectiveName}.rmesh",
			$"{Path.GetFileNameWithoutExtension(effectiveName)}.rmesh",
			$"{Path.GetFileNameWithoutExtension(fileName)}.rmesh"
		};
	}

	private static string? TryFindModelInSubDir(string modelsDir, string subDir, string[] candidateFiles)
	{
		foreach (var cand in candidateFiles)
		{
			string path = Path.Combine(modelsDir, subDir, cand);
			if (File.Exists(path)) return path;
		}

		return null;
	}

	public static string? FindModelOnDisk(string targetDirectory, string? preferredSubCategory, string fileName)
	{
		return FindModelOnDisk(targetDirectory, preferredSubCategory, fileName, out _);
	}

	public static string? FindAssetOnDisk(string workspacePath, string subFolder, string relativeKey)
	{
		if (string.IsNullOrEmpty(workspacePath) || string.IsNullOrEmpty(relativeKey)) return null;

		string effectiveKey = NormalizeAssetKey(subFolder, relativeKey);
		string assetsDir = Path.Combine(workspacePath, "Assets");
		var candidateNames = GetCandidateAssetNames(subFolder, effectiveKey);
		var subFoldersToCheck = GetSubFoldersToCheck(subFolder);

		string? foundPath = TryFindAssetInSubFolders(workspacePath, assetsDir, subFoldersToCheck, candidateNames);
		if (foundPath != null) return foundPath;

		foundPath = TryFindAssetInRootOrAssets(workspacePath, assetsDir, candidateNames);
		if (foundPath != null) return foundPath;

		return TryFindAssetDeepSearch(assetsDir, subFoldersToCheck, candidateNames);
	}

	private static string NormalizeAssetKey(string subFolder, string relativeKey)
	{
		string normKey = relativeKey.Replace('\\', '/').TrimStart('/');
		if (normKey.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			normKey = normKey.Substring(6).TrimStart('/');
		}

		if (normKey.StartsWith($"Assets/{subFolder}/", StringComparison.OrdinalIgnoreCase))
		{
			normKey = normKey.Substring($"Assets/{subFolder}/".Length);
		}

		var (parsedType, parsedSlug) = TemplateIDHelper.ParseTemplateID(normKey);
		return (!string.IsNullOrEmpty(parsedType) && !string.IsNullOrEmpty(parsedSlug)) ? parsedSlug : normKey;
	}

	private static List<string> GetCandidateAssetNames(string subFolder, string effectiveKey)
	{
		string baseName = Path.GetFileName(effectiveKey);
		var candidates = new List<string> { effectiveKey, baseName };
		if (!Path.HasExtension(baseName))
		{
			string defaultExt = GetDefaultExtensionForSubFolder(subFolder);
			candidates.Add($"{effectiveKey}{defaultExt}");
			candidates.Add($"{baseName}{defaultExt}");
		}

		return candidates;
	}

	private static List<string> GetSubFoldersToCheck(string subFolder)
	{
		var list = new List<string> { subFolder };
		if (subFolder == "vfx_spritesheets") list.Add("vfx");
		else if (subFolder == "vfx") list.Add("vfx_spritesheets");
		return list;
	}

	private static string? TryFindAssetInSubFolders(string workspacePath, string assetsDir, List<string> subFolders, List<string> candidateNames)
	{
		foreach (var sf in subFolders)
		{
			foreach (var cand in candidateNames)
			{
				string p1 = sf == "other" ? Path.Combine(workspacePath, cand) : Path.Combine(assetsDir, sf, cand);
				if (File.Exists(p1)) return p1;

				if (sf is "audio/sfx" or "audio/music")
				{
					string p2 = Path.Combine(assetsDir, sf.Substring(6), cand);
					if (File.Exists(p2)) return p2;
				}

				if (sf == "icons")
				{
					string pAbilities = Path.Combine(assetsDir, "icons", "abilities", cand);
					if (File.Exists(pAbilities)) return pAbilities;
				}
			}
		}

		return null;
	}

	private static string? TryFindAssetInRootOrAssets(string workspacePath, string assetsDir, List<string> candidateNames)
	{
		foreach (var cand in candidateNames)
		{
			string pAssets = Path.Combine(assetsDir, cand);
			if (File.Exists(pAssets)) return pAssets;

			string pRoot = Path.Combine(workspacePath, cand);
			if (File.Exists(pRoot)) return pRoot;
		}

		return null;
	}

	private static string? TryFindAssetDeepSearch(string assetsDir, List<string> subFolders, List<string> candidateNames)
	{
		foreach (var sf in subFolders)
		{
			string searchRoot = Path.Combine(assetsDir, sf);
			if (!Directory.Exists(searchRoot)) continue;

			try
			{
				foreach (var cand in candidateNames)
				{
					var match = Directory.EnumerateFiles(searchRoot, cand, SearchOption.AllDirectories).FirstOrDefault();
					if (!string.IsNullOrEmpty(match) && File.Exists(match)) return match;
				}
			}
			catch
			{
			}
		}

		return null;
	}

}
