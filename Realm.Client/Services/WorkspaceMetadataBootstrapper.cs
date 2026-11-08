using System;
using System.IO;
using Realm.Client.Animation;
using Realm.Client.Utils;
using Realm.Shared.Metadata;

namespace Realm.Client.Services;

/// <summary>
///     Turns a folder into a usable map workspace: it seeds metadata.json from the template, fills in a name when the file
///     is empty, copies the template assets and writes the UGC licence. Every step tolerates a missing template, because a
///     workspace that is only half scaffolded is still better than an editor that refuses to open.
/// </summary>
internal static class WorkspaceMetadataBootstrapper
{
	internal const string UgcLicenseSummaryText = @"REALM PLATFORM USER-GENERATED CONTENT (UGC)

This map and its custom assets are User-Generated Content created for the 
Realm Platform ecosystem.

Use, remixing, and redistribution of this content are governed by the 
Realm Platform UGC License:
https://www.realm-game.com/RealmPlatform_UGC_License_v1.txt

For details on permissions, streaming, and standalone export restrictions, 
please visit the URL above.
";

	internal static void EnsureMetadata(string directory, string? templateMetadataPath)
	{
		string metadataPath = MetadataService.ResolveMetadataPath(directory);
		if (!File.Exists(metadataPath) || new FileInfo(metadataPath).Length == 0)
		{
			string templateMeta = templateMetadataPath ?? string.Empty;
			if (File.Exists(templateMeta))
			{
				File.Copy(templateMeta, metadataPath, true);
			}
			else
			{
				GameContext.MetadataService.SaveMetadata(metadataPath, new MapMetadata());
			}
		}

		if (File.Exists(metadataPath))
		{
			try
			{
				GameContext.MetadataService.UpdateMetadata(directory, meta =>
				{
					meta.MapProperties ??= new MapInfoMetadata();
					if (string.IsNullOrWhiteSpace(meta.MapProperties.MapName))
					{
						meta.MapProperties.MapName = MapNaming.CreateUntitledMapName();
					}

					GameContext.MetadataService.CleanMetadata(meta);
				});
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapWorkspaceService] Failed to ensure license in metadata.json: {ex.Message}");
			}
		}

		string templateMetaPath = templateMetadataPath ?? string.Empty;
		string? templateDirectory = Path.GetDirectoryName(templateMetaPath);
		if (templateDirectory != null && File.Exists(templateMetaPath))
		{
			string templateAssetsDir = Path.Combine(templateDirectory, "Assets");
			if (Directory.Exists(templateAssetsDir))
			{
				RealmDefaultAnimations.EnsureDefaultTemplateAnimations(templateAssetsDir);
				string destAssetsDir = Path.Combine(directory, "Assets");
				foreach (var assetFile in Directory.GetFiles(templateAssetsDir, "*", SearchOption.AllDirectories))
				{
					string relPath = Path.GetRelativePath(templateAssetsDir, assetFile);
					string destFile = Path.Combine(destAssetsDir, relPath);
					string? destDirectory = Path.GetDirectoryName(destFile);
					if (destDirectory != null) Directory.CreateDirectory(destDirectory);
					if (!File.Exists(destFile))
					{
						File.Copy(assetFile, destFile, true);
					}
				}
			}
		}

		RealmDefaultAnimations.EnsureDefaultTemplateAnimations(Path.Combine(directory, "Assets"));
		EnsureLicenseFile(directory);
	}

	internal static void EnsureLicenseFile(string directory)
	{
		if (string.IsNullOrEmpty(directory)) return;

		try
		{
			Directory.CreateDirectory(directory);
			string targetLicensePath = Path.Combine(directory, "LICENSE.md");
			string[] candidateSourcePaths = new[]
			{
				PathUtils.FindPath("MapTemplate/LICENSE.md"),
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MapTemplate", "LICENSE.md")
			};

			bool copied = false;
			foreach (var candidate in candidateSourcePaths)
			{
				if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
				{
					File.Copy(candidate, targetLicensePath, overwrite: true);
					copied = true;
					break;
				}
			}

			if (!copied)
			{
				File.WriteAllText(targetLicensePath, UgcLicenseSummaryText);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapWorkspaceService] Failed to copy LICENSE.md to {directory}: {ex.Message}");
		}
	}
}
