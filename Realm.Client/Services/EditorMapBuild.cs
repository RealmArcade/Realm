using System;
using System.IO;
using System.Threading.Tasks;

namespace Realm.Client.Services;

public static class EditorMapBuild
{
	public static async Task<bool> PerformCompilationStepAsync(string workspace, Func<string, string?> findProjectFile, Func<string, bool, bool> tryBypassCompilation, Func<string, bool> checkMapApiDependency, Func<string, string, bool, Task<bool>> executeDotnetPublishAsync, Action<string> onStatusError, Action<string> onLog, bool isDebug = false)	{
		try
		{
			if (!Directory.Exists(workspace))
			{
				onStatusError($"[MapEditorHUD] ERROR: The map folder '{workspace}' is not open, so there is nothing to compile");
				onLog($"[MapEditorHUD] The compilation step was asked for a workspace that does not exist: '{workspace}'");
				return false;
			}

			MapWorkspaceService.EnsureWitFile(workspace);
			MapWorkspaceService.EnsureWasmEntryPoint(workspace);
			MapWorkspaceService.EnsureCsproj(workspace, Path.GetFileName(workspace));
			MapWorkspaceService.EnsureSingleScriptOutput(workspace);

			string? csproj = findProjectFile(workspace);
			if (string.IsNullOrEmpty(csproj)) return false;

			if (!isDebug && tryBypassCompilation(workspace, isDebug)) return true;

			if (!checkMapApiDependency(workspace)) return false;

			return await executeDotnetPublishAsync(workspace, csproj, isDebug);
		}
		catch (Exception ex)
		{
			onStatusError(ex.Message);
			onLog(ex.ToString());
			return false;
		}
	}

	public static string? FindProjectFile(string workspace, Action<string> onErrorMessage)
	{
		var csprojFiles = Directory.GetFiles(workspace, "*.csproj", SearchOption.TopDirectoryOnly);
		if (csprojFiles.Length == 0)
		{
			onErrorMessage("[MapEditorHUD] ERROR: No .csproj found in workspace, cannot compile map script");
			return null;
		}

		return System.Linq.Enumerable.FirstOrDefault(csprojFiles, f => Path.GetFileName(f).Equals("MapScript.csproj", StringComparison.OrdinalIgnoreCase)) ?? csprojFiles[0];
	}

	public static bool TryBypassCompilation(string workspace, Func<string, bool, string?> findExistingWasmFile, Func<string, DateTime, bool> hasNewerSourceFiles, Action onBypassSuccess, bool isDebug = false)
	{
		string? existingWasm = findExistingWasmFile(workspace, isDebug);
		if (string.IsNullOrEmpty(existingWasm) || !File.Exists(existingWasm)) return false;

		if (!isDebug && MapScriptArtifacts.ContainsDebugInformation(existingWasm)) return false;

		DateTime wasmTime = File.GetLastWriteTimeUtc(existingWasm);
		if (hasNewerSourceFiles(workspace, wasmTime)) return false;

		onBypassSuccess();
		return true;
	}
}
