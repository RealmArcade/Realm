using System;
using System.IO;
using System.Text;
using Realm.Shared.Services;

namespace Realm.Client.Services;

/// <summary>
///     The compiled scripts a map workspace runs: which artifact of each flavor is present, the one-time move of an
///     artifact built under the older per-configuration folders into the single output folder, and whether an artifact
///     still carries the debug information a debugger needs. The folder and file names themselves belong to
///     MapFileService, which both sides of the script pipeline already read.
/// </summary>
public static class MapScriptArtifacts
{
	private static readonly byte[] DebugInformationSectionNameBytes = Encoding.ASCII.GetBytes(".debug_info");

	public static string? FindCompiledScript(string workspace, bool isDebug = false)
	{
		return FindCompiledScriptInBin(Path.Combine(workspace, "bin"), isDebug);
	}

	/// <summary>
	///     Finds the compiled script of one flavor in the folder a build writes it to, and nowhere else. There is no second
	///     location and no newest-file guess: a compile overwrites the artifact of its own flavor, so the one present is the
	///     last one built in that configuration, and the other flavor's artifact is left as it was.
	/// </summary>
	/// <param name="binDirectory">The workspace's <c>bin</c> folder.</param>
	/// <param name="isDebug">Whether the artifact carrying debug information is the one being looked for.</param>
	/// <returns>The compiled script of that flavor, or nothing when the workspace has never been compiled for it.</returns>
	public static string? FindCompiledScriptInBin(string binDirectory, bool isDebug = false)
	{
		string scriptDirectory = Path.Combine(binDirectory, MapFileService.ScriptOutputDirectoryName);
		if (!Directory.Exists(scriptDirectory)) return null;

		foreach (string candidate in Directory.GetFiles(scriptDirectory, "*.wasm", SearchOption.TopDirectoryOnly))
		{
			if (MapFileService.IsDebugScriptFileName(Path.GetFileName(candidate)) != isDebug) continue;

			return candidate;
		}

		return null;
	}

	/// <summary>
	///     Reports whether a compiled script still carries DWARF debug information. A playtest build keeps it so a debugger can
	///     attach and inspect the script; a build meant to ship drops it, and the artifact that still has it is what makes a
	///     package large enough to be refused.
	/// </summary>
	/// <param name="wasmPath">The compiled script to inspect.</param>
	/// <returns>Whether the artifact carries debug information.</returns>
	public static bool ContainsDebugInformation(string wasmPath)
	{
		if (string.IsNullOrEmpty(wasmPath) || !File.Exists(wasmPath)) return false;

		try
		{
			byte[] artifactBytes = File.ReadAllBytes(wasmPath);
			return artifactBytes.AsSpan().IndexOf(DebugInformationSectionNameBytes) >= 0;
		}
		catch (IOException)
		{
			return false;
		}
	}

	/// <summary>
	///     Brings a workspace that was built before the script had a single output folder onto it: the last compiled
	///     artifact built under the older per-configuration folders is moved into the script output folder under the name
	///     its own debug information says it belongs to, and the folders it came from are removed so nothing can run or
	///     ship a second, staler copy.
	/// </summary>
	/// <param name="workspace">The map workspace.</param>
	public static void EnsureSingleScriptOutput(string workspace)
	{
		string binDirectory = Path.Combine(workspace, "bin");
		if (!Directory.Exists(binDirectory)) return;

		string scriptDirectory = MapFileService.ResolveScriptOutputDirectory(workspace);

		try
		{
			string? relocated = FindNewestLegacyScript(binDirectory);
			if (relocated != null)
			{
				string publishedName = MapFileService.ScriptFileNameForFlavor(Path.GetFileName(relocated), ContainsDebugInformation(relocated));
				string destination = Path.Combine(scriptDirectory, publishedName);
				if (!File.Exists(destination))
				{
					Directory.CreateDirectory(scriptDirectory);
					File.Copy(relocated, destination, true);
					GameLog.LogToConsole($"[MapScriptArtifacts] Moved the compiled script '{relocated}' into '{destination}'.");
				}
			}

			foreach (string legacyConfigurationFolder in Directory.GetDirectories(binDirectory, "*", SearchOption.TopDirectoryOnly))
			{
				if (string.Equals(Path.GetFileName(legacyConfigurationFolder), MapFileService.ScriptOutputDirectoryName, StringComparison.OrdinalIgnoreCase)) continue;
				if (!IsBuildConfigurationFolder(Path.GetFileName(legacyConfigurationFolder))) continue;

				Directory.Delete(legacyConfigurationFolder, true);
			}

			RepairArtifactNames(scriptDirectory);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapScriptArtifacts] Could not single out the script output folder of '{workspace}': {exception.Message}");
		}
	}

	/// <summary>
	///     Makes the names in the script output folder state what the artifacts are, which is what every reader relies on: a
	///     script built with debug information in it is named as the playtest build, so a match cannot mistake it for the
	///     optimized one a workspace built before the two flavors had separate names may still hold. The retired marker that
	///     recorded the flavor of a single artifact is removed with it.
	/// </summary>
	/// <param name="scriptDirectory">The folder a map's scripts are published into.</param>
	private static void RepairArtifactNames(string scriptDirectory)
	{
		if (!Directory.Exists(scriptDirectory)) return;

		foreach (string artifact in Directory.GetFiles(scriptDirectory, "*.wasm", SearchOption.TopDirectoryOnly))
		{
			string fileName = Path.GetFileName(artifact);
			if (MapFileService.IsDebugScriptFileName(fileName) || !ContainsDebugInformation(artifact)) continue;

			string debugName = MapFileService.ScriptFileNameForFlavor(fileName, true);
			try
			{
				File.Move(artifact, Path.Combine(scriptDirectory, debugName), true);
				GameLog.LogToConsole($"[MapScriptArtifacts] Renamed '{fileName}' to '{debugName}', because the script it holds was built with debug information in it.");
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				GameLog.LogError($"[MapScriptArtifacts] Could not rename the playtest build '{artifact}', so a match may run it as the optimized one: {exception.Message}");
			}
		}

		string retiredMarker = Path.Combine(scriptDirectory, MapFileService.RetiredBuildFlavorFileName);
		if (!File.Exists(retiredMarker)) return;

		try
		{
			File.Delete(retiredMarker);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			GameLog.LogError($"[MapScriptArtifacts] Could not remove the retired build marker '{retiredMarker}': {exception.Message}");
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
