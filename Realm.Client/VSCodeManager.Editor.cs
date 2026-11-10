using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared.Audio;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = System.Environment;
using FileAccess = Godot.FileAccess;
using HttpClient = System.Net.Http.HttpClient;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client;

[SupportedOSPlatform("windows")]
public partial class VSCodeManager
{
	public void SetVisible(bool visible)
	{
		_isVisible = visible;
		if (visible)
		{
			LaunchOrFocusEditor();
		}
	}

	public void Focus()
	{
		LaunchOrFocusEditor();
	}

	public void OpenFile(string relativePath)
	{
		try
		{
			string projectRoot = PathUtils.GetProjectRoot();
			string mapFolderRaw = GetMapFolderToOpen(projectRoot);
			string fullPath = Path.Combine(mapFolderRaw, relativePath);

			string embedDir = GetVSCodeDirectory();
			string desktopExe = Path.Combine(embedDir, "editor", "VSCodium.exe");
			if (!File.Exists(desktopExe))
			{
				desktopExe = Path.Combine(embedDir, "editor", "bin", "codium.cmd");
			}

			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string extensionsDir = Path.Combine(serverDataDir, "extensions");

			var openStartInfo = new ProcessStartInfo
			{
				FileName = desktopExe,
				Arguments = $"--user-data-dir \"{serverDataDir}\" --extensions-dir \"{extensionsDir}\" -r -g \"{fullPath}\"",
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using var openProc = Process.Start(openStartInfo);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Failed to open file in VSCodium: {ex.Message}");
		}
	}

	private bool _isLaunchingEditor;

	private void LaunchOrFocusEditor()
	{
		if (_isLaunchingEditor)
		{
			return;
		}

		_isLaunchingEditor = true;

		try
		{
			string projectRoot = PathUtils.GetProjectRoot();
			string embedDir = GetVSCodeDirectory();
			string desktopExe = Path.Combine(embedDir, "editor", "VSCodium.exe");
			if (!File.Exists(desktopExe))
			{
				desktopExe = Path.Combine(embedDir, "editor", "bin", "codium.cmd");
			}

			if (!File.Exists(desktopExe))
			{
				GameLog.LogError("VS Code executable not found at: " + desktopExe);
				return;
			}

			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string extensionsDir = Path.Combine(serverDataDir, "extensions");
			string workspacePath = GetMapFolderToOpen(projectRoot);

			EnsureWorkspaceMapApiDll(projectRoot);
			VSCodeInstallPatcher.EnsureVSCodiumSettings(serverDataDir, workspacePath);
			VSCodeInstallPatcher.RemoveAntigravityExtension(extensionsDir);
			VSCodeInstallPatcher.EnsureOhziExtensionPatched(extensionsDir);
			VSCodeInstallPatcher.EnsureOpencuiExtensionAutoStarts(extensionsDir);
			VSCodeInstallPatcher.PatchVSCodiumConfiguration(embedDir);

			StartIpcHttpListener();

			if (_vscodeProcess == null || _vscodeProcess.HasExited)
			{
				_vscodeProcess = new Process();
				_vscodeProcess.StartInfo.FileName = desktopExe;
				_vscodeProcess.StartInfo.Arguments = $"--user-data-dir \"{serverDataDir}\" --extensions-dir \"{extensionsDir}\" \"{workspacePath}\"";
				_vscodeProcess.StartInfo.UseShellExecute = false;
				_vscodeProcess.StartInfo.CreateNoWindow = false;
				_vscodeProcess.StartInfo.EnvironmentVariables["VSCODE_EXTENSIONS"] = extensionsDir;
				_vscodeProcess.StartInfo.EnvironmentVariables["VSCODE_EXTENSIONS_DIR"] = extensionsDir;
				_vscodeProcess.StartInfo.EnvironmentVariables["REALM_IPC_PORT"] = _ipcHttpPort.ToString();
				_vscodeProcess.StartInfo.EnvironmentVariables["_NO_DEBUG_HEAP"] = "1";
				OpencodeCliBootstrap.ApplyEnvironmentVariables(_vscodeProcess.StartInfo);
				_vscodeProcess.Start();
				EditorWindowIntegration.AddProcessToJob(_vscodeProcess);
				EditorWindowIntegration.AttachWindowGrouping(_vscodeProcess);
				_isVisible = true;
				GameLog.LogToConsole($"[VSCodeManager] VSCodium desktop launched (PID {_vscodeProcess.Id}). Godot Host PID: {Environment.ProcessId}.");
			}
			else
			{
				_vscodeProcess.Refresh();
				IntPtr mainWindowHandle = _vscodeProcess.MainWindowHandle;
				if (mainWindowHandle != IntPtr.Zero)
				{
					EditorWindowIntegration.AttachWindowGrouping(_vscodeProcess);
					EditorWindowIntegration.ShowWindow(mainWindowHandle, EditorWindowIntegration.SW_RESTORE);
					EditorWindowIntegration.BringWindowToTop(mainWindowHandle);
					EditorWindowIntegration.SetForegroundWindow(mainWindowHandle);
				}
				else
				{
					var reuseStartInfo = new ProcessStartInfo
					{
						FileName = desktopExe,
						Arguments = $"--user-data-dir \"{serverDataDir}\" --extensions-dir \"{extensionsDir}\" -r \"{workspacePath}\"",
						UseShellExecute = false,
						CreateNoWindow = true
					};
					using var reuseProcess = Process.Start(reuseStartInfo);
				}
			}

			_ = OpencodeCliBootstrap.EnsureInstalledAsync();
		}
		catch (Exception ex)
		{
			GameLog.LogError("Failed to launch or focus VSCodium: " + ex.Message);
		}
		finally
		{
			_isLaunchingEditor = false;
		}
	}



	public void SaveRecentMapDir(string dirPath)
	{
		try
		{
			string recentPathFile = ProjectSettings.GlobalizePath("user://recent_map_dir.txt");
			string? directory = Path.GetDirectoryName(recentPathFile);
			if (directory != null && !Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}

			File.WriteAllText(recentPathFile, dirPath);
		}
		catch
		{
		}
	}

	private string GetMapFolderToOpen(string projectRoot)
	{
		if (TryGetEditingDirectoryPath(out string tempPath))
			return tempPath;

		if (TryGetRecentMapPath(out string recentPath))
			return recentPath;

		if (TryGetBlankMapPath(out string blankPath))
			return blankPath;

		string fallbackMap = GameContext.Simulation.ActiveMapName ?? "melee";
		return Path.Combine(projectRoot, "Maps", fallbackMap).Replace("\\", "/");
	}

	private bool TryGetEditingDirectoryPath(out string path)
	{
		path = string.Empty;
		if (GameContext.Host != null && GameContext.SessionState.IsMapEditorMode)
		{
			string editingDirectory = GameContext.EditorService._editingDirectory;
			if (Directory.Exists(editingDirectory))
			{
				path = editingDirectory.Replace("\\", "/");
				return true;
			}
		}

		return false;
	}

	private bool TryGetRecentMapPath(out string path)
	{
		path = string.Empty;
		try
		{
			string recentPathFile = ProjectSettings.GlobalizePath("user://recent_map_dir.txt");
			if (File.Exists(recentPathFile))
			{
				string recentMapDir = File.ReadAllText(recentPathFile).Trim();
				if (!string.IsNullOrEmpty(recentMapDir) && Directory.Exists(recentMapDir))
				{
					path = recentMapDir.Replace("\\", "/");
					return true;
				}
			}
		}
		catch
		{
		}

		return false;
	}

	private bool TryGetBlankMapPath(out string path)
	{
		path = string.Empty;
		try
		{
			if (GameContext.Host != null)
			{
				string docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
				string blankDir = Path.Combine(docPath, "blank_map");
				if (!Directory.Exists(blankDir))
				{
					EditorWorkspace.CreateMapDirectory("blank_map", blankDir, Realm.Client.Utils.PathUtils.GetProjectRoot(), Realm.Client.Utils.PathUtils.FindPath("MapTemplate"));
					GameHost.EnsureMapProjectFiles(blankDir);
				}

				path = blankDir.Replace("\\", "/");
				return true;
			}
		}
		catch
		{
		}

		return false;
	}

	public void CleanUp()
	{
		if (_vscodeProcess != null && !_vscodeProcess.HasExited)
		{
			try
			{
				_vscodeProcess.Kill(true);
			}
			catch
			{
			}

			_vscodeProcess = null;
		}

		_isInitialized = false;
	}

	private void InstallMissingExtensions(string exePath, string embedDir)
	{
		string extensionFolder = Path.Combine(embedDir, "data", "extensions", $"speige.realm-map-editor-{GetRealmMapEditorVersion()}");

		if (!Directory.Exists(extensionFolder))
		{
			GameLog.LogToConsole("Installing Realm Map Editor VSCode extension...");
			ExecuteVSCodeCommand(exePath, $"--install-extension speige.realm-map-editor");
		}

		string csharpExtFolder = Path.Combine(embedDir, "data", "extensions", "ms-dotnettools.csharp");
		if (!Directory.Exists(csharpExtFolder))
		{
			GameLog.LogToConsole("Installing C# VSCode extension...");
			ExecuteVSCodeCommand(exePath, $"--install-extension ms-dotnettools.csharp");
		}
	}

	private void ExecuteVSCodeCommand(string vscodePath, string arguments)
	{
		try
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = vscodePath,
				Arguments = arguments,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			using var process = Process.Start(startInfo);
			if (process == null)
			{
				GameLog.LogError($"VSCode command could not be started: {vscodePath} {arguments}");
				return;
			}

			process.WaitForExit();

			if (process.ExitCode != 0)
			{
				string error = process.StandardError.ReadToEnd();
				GameLog.LogError($"VSCode command failed ({process.ExitCode}): {error}");
			}
		}
		catch (Exception e)
		{
			GameLog.LogError($"Failed to execute VSCode command '{arguments}': {e.Message}");
		}
	}


}
