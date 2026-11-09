using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Realm.Client.Core;

namespace Realm.Client.Services;

/// <summary>
///     Makes the opencode CLI usable by the OpenCode Panel extension: installs it if it is missing and
///     points its data directory at the per-user location Realm owns.
/// </summary>
public static class OpencodeCliBootstrap
{
	public static async Task EnsureInstalledAsync()
	{
		if (OpencodeCliResolver.IsAvailable())
		{
			return;
		}

		bool installed = await OpencodeCliInstaller.EnsureInstalledAsync();
		if (!installed)
		{
			GameLog.LogError("[VSCodeManager] opencode CLI is unavailable. The OpenCode Panel extension cannot start its backend.");
		}
	}

	public static void ApplyEnvironmentVariables(ProcessStartInfo startInfo)
	{
		try
		{
			string opencodeDataDirectory = OpencodeCliResolver.GetDataDirectory();
			Directory.CreateDirectory(opencodeDataDirectory);
			startInfo.EnvironmentVariables["XDG_DATA_HOME"] = opencodeDataDirectory;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Failed to prepare opencode data directory: {ex.Message}");
		}
	}
}
