using System;
using System.IO;
using Realm.Client.Core;
using Realm.Shared.Services;

namespace Realm.Client.Services;

public sealed class EditorMapIdentity
{
	public string? CachedName { get; set; }

	public string? CachedVersion { get; set; }

	public long CacheTicks { get; set; }

	public string SanitizeName(string? candidateName)
	{
		if (string.IsNullOrWhiteSpace(candidateName))
		{
			return MapNaming.CreateUntitledMapName();
		}

		string sanitized = candidateName.Trim();
		return string.IsNullOrEmpty(sanitized) ? MapNaming.CreateUntitledMapName() : sanitized;
	}

	public string UpdateCachedName(string mapName, long nowTicks)
	{
		CachedName = mapName;
		CacheTicks = nowTicks;
		return mapName;
	}

	public bool TryGetNameFromManifest(string workspacePath, out string mapName)
	{
		var manifest = MapFileService.LoadManifest(workspacePath);
		return TrySanitizeCandidate(manifest?.MapName, out mapName);
	}

	public bool TryGetNameFromMetadata(string workspacePath, out string mapName)
	{
		var metadata = MapFileService.LoadMetadata(workspacePath);
		return TrySanitizeCandidate(metadata?.MapProperties?.MapName, out mapName);
	}

	public bool TryGetNameFromActiveHost(out string mapName)
	{
		mapName = string.Empty;
		if (!string.IsNullOrEmpty(GameContext.Simulation.ActiveMapName))
		{
			string candidate = Path.GetFileNameWithoutExtension(GameContext.Simulation.ActiveMapName);
			return TrySanitizeCandidate(candidate, out mapName);
		}

		return false;
	}

	public string GetVersionFromMetadata(bool forceReload, string workspacePath)
	{
		long now = Environment.TickCount64;
		if (!forceReload && !string.IsNullOrEmpty(CachedVersion) && (now - CacheTicks < 2000))
		{
			return CachedVersion;
		}

		try
		{
			var manifest = MapFileService.LoadManifest(workspacePath);
			if (!string.IsNullOrWhiteSpace(manifest.Version))
			{
				CachedVersion = manifest.Version.Trim();
				return CachedVersion;
			}

			var metadata = MapFileService.LoadMetadata(workspacePath);
			if (!string.IsNullOrWhiteSpace(metadata.MapProperties?.Version))
			{
				CachedVersion = metadata.MapProperties.Version.Trim();
				return CachedVersion;
			}

			CachedVersion = "1.0.0";
			return "1.0.0";
		}
		catch
		{
			CachedVersion = "1.0.0";
			return "1.0.0";
		}
	}

	public string GetManifestBlake3(string workspacePath, string mapTitle, string mapVersion, string author)
	{
		try
		{
			string manifestJsonPath = Path.Combine(workspacePath, "manifest.json");
			if (File.Exists(manifestJsonPath))
			{
				string manifestBlake3 = RealmMetadataHelper.ComputeBlake3(manifestJsonPath);
				if (!string.IsNullOrEmpty(manifestBlake3))
				{
					return manifestBlake3;
				}
			}

			if (System.IO.Directory.Exists(workspacePath))
			{
				var manifest = MapManifest.CreateFromDirectory(workspacePath, mapTitle, author, mapVersion);
				return manifest.ComputeManifestBlake3();
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"could not compute the manifest hash of workspace {workspacePath}, so changes to it may go unnoticed: {ex.GetType().Name}: {ex.Message}");
		}

		return string.Empty;
	}

	public static bool TrySanitizeCandidate(string? candidateName, out string sanitizedMapName)
	{
		sanitizedMapName = string.Empty;
		if (string.IsNullOrWhiteSpace(candidateName))
		{
			return false;
		}

		string cleaned = candidateName.Trim();
		if (string.IsNullOrEmpty(cleaned))
		{
			return false;
		}

		sanitizedMapName = cleaned;
		return true;
	}
}
