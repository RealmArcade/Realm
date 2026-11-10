using System;
using System.IO;
using System.Linq;
using Realm.Shared.Services;

namespace Realm.Client.Services;

/// <summary>
///     The compiled script a map workspace runs: where the single output folder is, which build configuration
///     produced the artifact in it, and the one-time move of an artifact built under the older
///     per-configuration folders into that folder. The folder and file names themselves belong to
///     MapFileService, which both sides of the script pipeline already read.
/// </summary>
public static class MapScriptArtifacts
{
	public static string? FindCompiledScript(string workspace)
	{
		return FindCompiledScriptInBin(Path.Combine(workspace, "bin"));
	}

	/// <summary>
	///     Finds the compiled script in the folder a build writes it to, and nowhere else. There is no second location and
	///     no newest-file guess: a compile overwrites the artifact that was already there, so the one present is the last
	///     one built.
	/// </summary>
	/// <param name="binDirectory">The workspace's <c>bin</c> folder.</param>
	/// <returns>The compiled script, or nothing when the workspace has never been compiled.</returns>
	public static string? FindCompiledScriptInBin(string binDirectory)
	{
		string scriptDirectory = Path.Combine(binDirectory, MapFileService.ScriptOutputDirectoryName);
		if (!Directory.Exists(scriptDirectory)) return null;

		return Directory.GetFiles(scriptDirectory, "*.wasm", SearchOption.TopDirectoryOnly).FirstOrDefault();
	}

	/// <summary>
	///     Reports which configuration produced the artifact in the script output folder.
	/// </summary>
	/// <param name="workspace">The map workspace.</param>
	/// <returns>The recorded flavor, or nothing when the artifact was not produced by a publish this build recorded.</returns>
	public static string? ReadBuildFlavor(string workspace)
	{
		string flavorPath = MapFileService.ResolveBuildFlavorPath(workspace);
		if (!File.Exists(flavorPath)) return null;

		try
		{
			return File.ReadAllText(flavorPath).Trim();
		}
		catch (IOException)
		{
			return null;
		}
	}

	public static void WriteBuildFlavor(string workspace, bool isDebug)
	{
		string scriptDirectory = MapFileService.ResolveScriptOutputDirectory(workspace);
		Directory.CreateDirectory(scriptDirectory);
		File.WriteAllText(Path.Combine(scriptDirectory, MapFileService.BuildFlavorFileName), isDebug ? MapFileService.DebugBuildFlavor : MapFileService.ReleaseBuildFlavor);
	}

	/// <summary>
	///     Brings a workspace that was built before the script had a single output folder onto it: the last compiled
	///     artifact built under the older per-configuration folders is moved into the script output folder, and the folders
	///     it came from are removed so nothing can run or ship a second, staler copy.
	/// </summary>
	/// <param name="workspace">The map workspace.</param>
	public static void EnsureSingleScriptOutput(string workspace)
	{
		string binDirectory = Path.Combine(workspace, "bin");
		if (!Directory.Exists(binDirectory)) return;

		string scriptDirectory = MapFileService.ResolveScriptOutputDirectory(workspace);

		try
		{
			if (FindCompiledScriptInBin(binDirectory) == null)
			{
				string? relocated = FindNewestLegacyScript(binDirectory);
				if (relocated != null)
				{
					Directory.CreateDirectory(scriptDirectory);
					File.Copy(relocated, Path.Combine(scriptDirectory, Path.GetFileName(relocated)), true);
					GameLog.LogToConsole($"[MapScriptArtifacts] Moved the compiled script '{relocated}' into '{scriptDirectory}'.");
				}
			}

			foreach (string legacyConfigurationFolder in Directory.GetDirectories(binDirectory, "*", SearchOption.TopDirectoryOnly))
			{
				if (string.Equals(Path.GetFileName(legacyConfigurationFolder), MapFileService.ScriptOutputDirectoryName, StringComparison.OrdinalIgnoreCase)) continue;
				if (!IsBuildConfigurationFolder(Path.GetFileName(legacyConfigurationFolder))) continue;

				Directory.Delete(legacyConfigurationFolder, true);
			}
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapScriptArtifacts] Could not single out the script output folder of '{workspace}': {exception.Message}");
		}
	}

	private static string? FindNewestLegacyScript(string binDirectory)
	{
		string? newest = null;

		foreach (string configurationFolder in Directory.GetDirectories(binDirectory, "*", SearchOption.TopDirectoryOnly))
		{
			if (!IsBuildConfigurationFolder(Path.GetFileName(configurationFolder))) continue;

			foreach (string candidate in Directory.GetFiles(configurationFolder, "*.wasm", SearchOption.AllDirectories))
			{
				string normalized = candidate.Replace('\\', '/');
				if (!normalized.Contains("/publish/", StringComparison.OrdinalIgnoreCase)) continue;

				if (newest == null || File.GetLastWriteTimeUtc(candidate) > File.GetLastWriteTimeUtc(newest)) newest = candidate;
			}
		}

		return newest;
	}

	private static bool IsBuildConfigurationFolder(string directoryName)
	{
		return string.Equals(directoryName, MapFileService.DebugBuildFlavor, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(directoryName, MapFileService.ReleaseBuildFlavor, StringComparison.OrdinalIgnoreCase);
	}
}
