﻿using System.IO;

namespace Realm.Client.Services;

/// <summary>
///     The folder moves the 0.0.4 migration makes before it touches any metadata: the legacy vfx folder
///     joins the spritesheets, and the per-asset-type folders nested one level too deep by the old
///     exporter are flattened into their category folder.
/// </summary>
internal static class Migration_0_0_4_AssetDirectories
{
	internal static void MigrateAssetsDirectories(string mapDirectory)
	{
		string assetsDir = Path.Combine(mapDirectory, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		MoveLegacyVfxDir(assetsDir);
		MovePrefixSubfolders(assetsDir);
	}

	internal static void MoveLegacyVfxDir(string assetsDir)
	{
		string legacyVfxDir = Path.Combine(assetsDir, "vfx");
		string newVfxDir = Path.Combine(assetsDir, "vfx_spritesheets");
		if (Directory.Exists(legacyVfxDir))
		{
			Directory.CreateDirectory(newVfxDir);
			MoveDirectoryContents(legacyVfxDir, newVfxDir);
			try
			{
				Directory.Delete(legacyVfxDir, true);
			}
			catch
			{
			}
		}
	}

	internal static void MovePrefixSubfolders(string assetsDir)
	{
		var prefixSubfolderChecks = new (string Folder, string Prefix)[]
		{
			("icons", "icon"), ("skyboxes", "skybox"), ("textures", "terrain"),
			("decals", "decal"), ("ribbons", "ribbon"),
			("vfx_spritesheets", "spritesheet"), ("vfx_spritesheets", "vfx")
		};

		foreach (var (folder, prefix) in prefixSubfolderChecks)
		{
			string nestedDir = Path.Combine(assetsDir, folder, prefix);
			if (Directory.Exists(nestedDir))
			{
				string parentDir = Path.Combine(assetsDir, folder);
				MoveDirectoryContents(nestedDir, parentDir);
				try
				{
					Directory.Delete(nestedDir, true);
				}
				catch
				{
				}
			}
		}
	}

	internal static void MoveDirectoryContents(string sourceDir, string targetDir)
	{
		foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
		{
			string rel = Path.GetRelativePath(sourceDir, file);
			string target = Path.Combine(targetDir, rel);
			string? targetFileDir = Path.GetDirectoryName(target);
			if (!string.IsNullOrEmpty(targetFileDir)) Directory.CreateDirectory(targetFileDir);

			if (!File.Exists(target)) File.Move(file, target);
			else File.Delete(file);
		}
	}
}
