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
	public VSCodeManager()
	{
		if (OperatingSystem.IsWindows())
		{
			try
			{
				EditorWindowIntegration.SetCurrentProcessExplicitAppUserModelID("Realm.GameClient");
			}
			catch
			{
			}
			AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanUp();
		}
	}

	private bool _isInstalling = false;
	private bool _installCompleted = false;
	private Task _installTask = null!;
	private readonly object _installLock = new object();

	private static readonly string[] FutureExtensionsToConsider = new[]
	{
		"mechatroner.rainbow-json",	
		"akondratiuk1-dev.texture-viewer"
	};
	private static readonly string[] RequiredExtensions = new[]
	{
		"muhammad-sammy.csharp",
		"OHZIInteractiveStudio.ohzi-vscode-glb-viewer",
		"Gruntfuggly.todo-tree",
		"patcx.vscode-nuget-gallery",
		"AykutSarac.jsoncrack-vscode",
		"vadimcn.vscode-lldb",
		"haoyangzeng.opencui"
	};

	public bool IsInstalling
	{
		get
		{
			lock (_installLock)
			{
				return _isInstalling;
			}
		}
	}

	public bool IsInstallCompleted
	{
		get
		{
			lock (_installLock)
			{
				return _installCompleted;
			}
		}
	}

	public static string GetVSCodeDirectory()
	{
		try
		{
			string appDataDir = OS.GetUserDataDir();
			if (!string.IsNullOrWhiteSpace(appDataDir))
			{
				return Path.Combine(appDataDir, "vscode");
			}
		}
		catch
		{
		}

		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "app_userdata", "Realm", "vscode");
	}

	public static string GetRealmMapEditorVersion()
	{
		try
		{
			if (TryGetVersionFromPackage("vscode_extensions_dist", "speige.realm-map-editor", out string v1))
				return v1;

			if (TryGetVersionFromPackage("..", "Realm.MapEditorExtension", out string v2))
				return v2;

			string versionJson = Path.GetFullPath(Path.Combine(PathUtils.GetProjectRoot(), "..", "version.json"));
			if (File.Exists(versionJson))
			{
				var jsonNode = JsonNode.Parse(File.ReadAllText(versionJson, Encoding.UTF8));
				string? version = jsonNode?["extensionVersion"]?.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(version))
					return version;
			}
		}
		catch
		{
		}

		return "0.0.1";
	}

	private static bool TryGetVersionFromPackage(string folder1, string folder2, out string version)
	{
		version = string.Empty;
		string pkgPath = Path.GetFullPath(Path.Combine(PathUtils.GetProjectRoot(), folder1, folder2, "package.json"));
		if (!File.Exists(pkgPath))
			return false;

		var jsonNode = JsonNode.Parse(File.ReadAllText(pkgPath, Encoding.UTF8));
		version = jsonNode?["version"]?.GetValue<string>() ?? string.Empty;
		return !string.IsNullOrWhiteSpace(version);
	}

	public bool IsInstalled()
	{
		string embedDir = GetVSCodeDirectory();
		string exePath = Path.Combine(embedDir, "editor", "bin", "codium.exe");
		string completedMarkerPath = Path.Combine(embedDir, "install_completed.marker");

		if (!File.Exists(exePath) || new FileInfo(exePath).Length == 0)
			return false;

		return File.Exists(completedMarkerPath);
	}

	public IReadOnlyList<string> GetMissingDependencies()
	{
		var missingDependencies = new List<string>();
		string embedDir = GetVSCodeDirectory();
		string extensionsDir = Path.Combine(embedDir, "user-data-dir", "extensions");

		string wasiPath = WasiSdkResolver.ResolveWasiSdkPath();
		string wasiClangPath = !string.IsNullOrEmpty(wasiPath) ? Path.Combine(wasiPath, "bin", "clang.exe") : string.Empty;
		if (string.IsNullOrEmpty(wasiClangPath) || !File.Exists(wasiClangPath) || new FileInfo(wasiClangPath).Length == 0)
		{
			missingDependencies.Add(TranslationServer.Translate("WASI SDK (map compiler)"));
		}

		string mapEditorExtensionDir = Path.Combine(extensionsDir, $"speige.realm-map-editor-{GetRealmMapEditorVersion()}");
		if (!Directory.Exists(mapEditorExtensionDir))
		{
			missingDependencies.Add(TranslationServer.Translate("Realm Map Editor extension"));
		}

		foreach (string requiredExtension in RequiredExtensions)
		{
			if (!VSCodeInstallPatcher.IsExtensionInstalled(extensionsDir, requiredExtension))
			{
				missingDependencies.Add(GetExtensionDisplayName(requiredExtension));
			}
		}

		if (!OpencodeCliResolver.IsAvailable())
		{
			missingDependencies.Add(TranslationServer.Translate("opencode CLI"));
		}

		return missingDependencies;
	}

	private static string GetExtensionDisplayName(string extensionId)
	{
		switch (extensionId)
		{
			case "muhammad-sammy.csharp": return TranslationServer.Translate("C# language support");
			case "OHZIInteractiveStudio.ohzi-vscode-glb-viewer": return TranslationServer.Translate("3D model viewer");
			case "Gruntfuggly.todo-tree": return TranslationServer.Translate("TODO tree");
			case "patcx.vscode-nuget-gallery": return TranslationServer.Translate("NuGet gallery");
			case "AykutSarac.jsoncrack-vscode": return TranslationServer.Translate("JSON crack");
			case "vadimcn.vscode-lldb": return TranslationServer.Translate("LLDB debugger");
			case "haoyangzeng.opencui": return TranslationServer.Translate("opencode panel");
			default: return extensionId;
		}
	}

	public void ForceReinstall()
	{
		StartInstallIfNeeded(force: true);
	}

	public void StartInstallIfNeeded(bool force = false)
	{
		lock (_installLock)
		{
			if (_isInstalling || (!force && _installCompleted))
				return;

			string embedDir = GetVSCodeDirectory();
			string exePath = Path.Combine(embedDir, "editor", "bin", "codium.exe");

			if (!force && IsInstalled())
			{
				_installCompleted = true;
				Task.Run(() => InstallMissingExtensions(exePath, embedDir));
				return;
			}

			_isInstalling = true;
			_installCompleted = false;
			_installTask = Task.Run(() => RunInstallScript(force, exePath, embedDir));
		}
	}

	private void RunInstallScript(bool force, string exePath, string embedDir)
	{
		try
		{
			string scriptPath = PathUtils.FindPath("install_editor_dependencies.ps1");
			if (File.Exists(scriptPath))
			{
				GameLog.LogToConsole("Starting editor dependencies installation script: " + scriptPath);
				using var installProcess = new Process();
				installProcess.StartInfo.FileName = "powershell.exe";
				string forceArg = force ? " -Force" : string.Empty;
				installProcess.StartInfo.Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"{forceArg}";
				installProcess.StartInfo.CreateNoWindow = true;
				installProcess.StartInfo.UseShellExecute = false;
				installProcess.StartInfo.RedirectStandardOutput = true;
				installProcess.StartInfo.RedirectStandardError = true;

				installProcess.OutputDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrEmpty(e.Data)) GameLog.LogToConsole("[installer] " + e.Data);
				};
				installProcess.ErrorDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrEmpty(e.Data)) GameLog.LogError("[installer error] " + e.Data);
				};

				installProcess.Start();
				installProcess.BeginOutputReadLine();
				installProcess.BeginErrorReadLine();
				installProcess.WaitForExit();
			}
			else
			{
				GameLog.LogError("VS Code installer script not found at: " + scriptPath);
			}

			embedDir = GetVSCodeDirectory();
			exePath = Path.Combine(embedDir, "editor", "bin", "codium.exe");

			if (File.Exists(exePath))
			{
				InstallMissingExtensions(exePath, embedDir);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError("Background VS Code installation failed: " + ex.Message);
		}
		finally
		{
			lock (_installLock)
			{
				_isInstalling = false;
				_installCompleted = IsInstalled();
			}
		}
	}

}