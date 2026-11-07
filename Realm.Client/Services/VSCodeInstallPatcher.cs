using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Utils;
using Environment = System.Environment;

namespace Realm.Client.Services;

/// <summary>
///     Edits an installed editor tree in place: the settings that turn off workspace trust, the activation event the
///     opencui extension needs, the ohzi patch, the proposed-API allow list, and the cache entries for extensions the
///     Realm editor replaces. Every member takes the directory it should touch, so nothing here knows how the install was
///     located.
/// </summary>
internal static class VSCodeInstallPatcher
{
	internal static void EnsureVSCodiumSettings(string serverDataDir, string workspacePath)
	{
		try
		{
			string userDir = Path.Combine(serverDataDir, "User");
			if (!Directory.Exists(userDir))
			{
				Directory.CreateDirectory(userDir);
			}

			string userSettingsFile = Path.Combine(userDir, "settings.json");
			JsonObject settings = null;
			if (File.Exists(userSettingsFile))
			{
				try
				{
					settings = JsonNode.Parse(File.ReadAllText(userSettingsFile, Encoding.UTF8))?.AsObject();
				}
				catch
				{
				}
			}
			settings ??= new JsonObject();

			settings["security.workspace.trust.enabled"] = false;
			settings["security.workspace.trust.startupPrompt"] = "never";
			settings["security.workspace.trust.banner"] = "never";
			settings["security.workspace.trust.emptyWindow"] = false;
			settings["security.workspace.trust.untrustedFiles"] = "open";

			EnsureOpencodeBinarySetting(settings);

			File.WriteAllText(userSettingsFile, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

			if (!string.IsNullOrEmpty(workspacePath) && Directory.Exists(workspacePath))
			{
				string wsVscodeDir = Path.Combine(workspacePath, ".vscode");
				if (!Directory.Exists(wsVscodeDir))
				{
					Directory.CreateDirectory(wsVscodeDir);
				}

				string wsSettingsFile = Path.Combine(wsVscodeDir, "settings.json");
				JsonObject wsSettings = null;
				if (File.Exists(wsSettingsFile))
				{
					try
					{
						wsSettings = JsonNode.Parse(File.ReadAllText(wsSettingsFile, Encoding.UTF8))?.AsObject();
					}
					catch
					{
					}
				}
				wsSettings ??= new JsonObject();
				wsSettings["security.workspace.trust.enabled"] = false;
				File.WriteAllText(wsSettingsFile, wsSettings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Failed to ensure VSCodium settings: {ex.Message}");
		}
	}

	private static void EnsureOpencodeBinarySetting(JsonObject settings)
	{
		string managedBinaryPath = OpencodeCliResolver.GetManagedCliExecutablePath();
		if (File.Exists(managedBinaryPath))
		{
			settings["opencui.binaryPath"] = managedBinaryPath;
			return;
		}

		string resolvedBinaryPath = OpencodeCliResolver.ResolveExecutablePath();
		if (string.IsNullOrEmpty(resolvedBinaryPath))
		{
			return;
		}

		string configuredBinaryPath = settings["opencui.binaryPath"]?.ToString();
		if (!string.IsNullOrEmpty(configuredBinaryPath) && File.Exists(configuredBinaryPath))
		{
			return;
		}

		settings["opencui.binaryPath"] = resolvedBinaryPath;
	}

	private static string GetOhziPatchContent()
	{
		try
		{
			string projectRoot = PathUtils.GetProjectRoot();
			string tpl1 = Path.Combine(projectRoot, "Templates", "ohzi_extension_patch.js");
			if (File.Exists(tpl1)) return File.ReadAllText(tpl1, Encoding.UTF8);

			string tpl2 = Path.Combine(AppContext.BaseDirectory, "Templates", "ohzi_extension_patch.js");
			if (File.Exists(tpl2)) return File.ReadAllText(tpl2, Encoding.UTF8);
		}
		catch
		{
		}

		return string.Empty;
	}

	internal static void EnsureOhziExtensionPatched(string extensionsDir)
	{
		if (string.IsNullOrEmpty(extensionsDir) || !Directory.Exists(extensionsDir)) return;

		try
		{
			string[] directories = Directory.GetDirectories(extensionsDir);
			foreach (string dir in directories)
			{
				string name = Path.GetFileName(dir);
				if (!name.Contains("ohzi-vscode-glb-viewer", StringComparison.OrdinalIgnoreCase) &&
				    !name.Contains("ohziinteractivestudio", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				string extJsPath = Path.Combine(dir, "extension.js");
				bool needsPatch = !File.Exists(extJsPath);
				if (!needsPatch)
				{
					string existing = File.ReadAllText(extJsPath, Encoding.UTF8);
					if (!existing.Contains("REALM_PATCHED_OHZI_BASE64", StringComparison.Ordinal))
					{
						needsPatch = true;
					}
				}

				if (needsPatch)
				{
					string patchContent = GetOhziPatchContent();
					if (!string.IsNullOrEmpty(patchContent))
					{
						File.WriteAllText(extJsPath, patchContent, new UTF8Encoding(false));
						GameLog.LogToConsole($"VS Code: Patched OHZI GLB viewer extension in {dir}");
					}
				}
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"VS Code: Failed to patch OHZI extension: {ex.Message}");
		}
	}

	internal static void EnsureOpencuiExtensionAutoStarts(string extensionsDir)
	{
		if (string.IsNullOrEmpty(extensionsDir) || !Directory.Exists(extensionsDir))
		{
			return;
		}

		try
		{
			foreach (string extensionDirectory in Directory.GetDirectories(extensionsDir, "haoyangzeng.opencui*"))
			{
				string packageJsonPath = Path.Combine(extensionDirectory, "package.json");
				if (!File.Exists(packageJsonPath))
				{
					continue;
				}

				JsonObject packageJson = JsonNode.Parse(File.ReadAllText(packageJsonPath, Encoding.UTF8))?.AsObject();
				if (packageJson == null)
				{
					continue;
				}

				JsonArray activationEvents = packageJson["activationEvents"] as JsonArray ?? new JsonArray();
				if (ContainsActivationEvent(activationEvents, "onStartupFinished"))
				{
					continue;
				}

				activationEvents.Add("onStartupFinished");
				packageJson["activationEvents"] = activationEvents;
				File.WriteAllText(packageJsonPath, packageJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
				GameLog.LogToConsole($"VS Code: Enabled startup activation for the OpenCode Panel extension in {extensionDirectory}");
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"VS Code: Failed to enable startup activation for the OpenCode Panel extension: {ex.Message}");
		}
	}

	private static bool ContainsActivationEvent(JsonArray activationEvents, string activationEvent)
	{
		foreach (JsonNode existingEvent in activationEvents)
		{
			if (string.Equals(existingEvent?.ToString(), activationEvent, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	internal static void RemoveAntigravityExtension(string extensionsDir)
	{
		if (string.IsNullOrEmpty(extensionsDir) || !Directory.Exists(extensionsDir))
		{
			return;
		}

		try
		{
			foreach (string extensionDirectory in Directory.GetDirectories(extensionsDir, "google.google-antigravity*"))
			{
				try
				{
					Directory.Delete(extensionDirectory, recursive: true);
					GameLog.LogToConsole($"VS Code: Removed the deprecated antigravity extension at {extensionDirectory}");
				}
				catch (Exception ex)
				{
					GameLog.LogError($"VS Code: Failed to delete {extensionDirectory}: {ex.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"VS Code: Failed to enumerate deprecated antigravity extensions: {ex.Message}");
		}

		RemoveExtensionFromCache(Path.Combine(extensionsDir, "extensions.json"), "google.google-antigravity");
	}

	private static void RemoveExtensionFromCache(string extensionsJsonPath, string extensionId)
	{
		if (!File.Exists(extensionsJsonPath))
		{
			return;
		}

		try
		{
			JsonArray cachedExtensions = JsonNode.Parse(File.ReadAllText(extensionsJsonPath, Encoding.UTF8))?.AsArray();
			if (cachedExtensions == null)
			{
				return;
			}

			bool removed = false;
			for (int index = cachedExtensions.Count - 1; index >= 0; index--)
			{
				string cachedExtensionId = cachedExtensions[index]?["identifier"]?["id"]?.ToString();
				if (!string.Equals(cachedExtensionId, extensionId, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				cachedExtensions.RemoveAt(index);
				removed = true;
			}

			if (removed)
			{
				File.WriteAllText(extensionsJsonPath, cachedExtensions.ToJsonString(new JsonSerializerOptions { WriteIndented = false }), new UTF8Encoding(false));
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"VS Code: Failed to update {extensionsJsonPath}: {ex.Message}");
		}
	}

	internal static void PatchVSCodiumConfiguration(string embedDir)
	{
		try
		{
			foreach (string root in GetVSCodeTargetDirectories(embedDir))
			{
				if (!Directory.Exists(root)) continue;
				string productJsonPath = Path.Combine(root, "product.json");
				if (File.Exists(productJsonPath))
				{
					PatchProductJson(productJsonPath);
				}
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"VS Code: Failed to patch VSCodium configuration: {ex.Message}");
		}
	}

	private static void PatchProductJson(string productJsonPath)
	{
		if (!File.Exists(productJsonPath)) return;

		try
		{
			var jsonNode = JsonNode.Parse(File.ReadAllText(productJsonPath, Encoding.UTF8));
			if (jsonNode == null) return;

			bool needsUpdate = false;
			var extensionAllowedProposedApi = jsonNode["extensionAllowedProposedApi"]?.AsArray();

			if (extensionAllowedProposedApi != null)
			{
				needsUpdate |= AddProposedApi(extensionAllowedProposedApi, "speige.realm-map-editor");
			}

			if (jsonNode["win32AppUserModelId"]?.GetValue<string>() != "Realm.GameClient")
			{
				jsonNode["win32AppUserModelId"] = "Realm.GameClient";
				needsUpdate = true;
			}

			var extensionKindDefaults = jsonNode["extensionKind"]?.AsObject();
			if (extensionKindDefaults != null && extensionKindDefaults.Remove("google.google-antigravity"))
			{
				needsUpdate = true;
			}

			var configDefaults = jsonNode["configurationDefaults"]?.AsObject();
			if (configDefaults == null)
			{
				configDefaults = new JsonObject();
				jsonNode["configurationDefaults"] = configDefaults;
				needsUpdate = true;
			}
			if (configDefaults["security.workspace.trust.enabled"]?.GetValue<bool>() != false)
			{
				configDefaults["security.workspace.trust.enabled"] = false;
				needsUpdate = true;
			}
			if (configDefaults["security.workspace.trust.startupPrompt"]?.GetValue<string>() != "never")
			{
				configDefaults["security.workspace.trust.startupPrompt"] = "never";
				needsUpdate = true;
			}
			if (configDefaults["security.workspace.trust.banner"]?.GetValue<string>() != "never")
			{
				configDefaults["security.workspace.trust.banner"] = "never";
				needsUpdate = true;
			}
			if (configDefaults["security.workspace.trust.emptyWindow"]?.GetValue<bool>() != false)
			{
				configDefaults["security.workspace.trust.emptyWindow"] = false;
				needsUpdate = true;
			}

			if (needsUpdate)
			{
				File.WriteAllText(productJsonPath, jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
				GameLog.LogToConsole($"Successfully patched VSCode product.json at {productJsonPath}");
			}
		}
		catch (UnauthorizedAccessException)
		{
			GameLog.LogError("Cannot patch VSCode product.json. Please run Realm as Administrator once to enable Map Editor features.");
		}
		catch (Exception e)
		{
			GameLog.LogError($"Failed to patch VSCode product.json: {e.Message}");
		}
	}

	private static bool AddProposedApi(JsonArray apiArray, string extensionName)
	{
		foreach (var item in apiArray)
		{
			if (item?.GetValue<string>() == extensionName)
			{
				return false;
			}
		}

		apiArray.Add(extensionName);
		return true;
	}

	internal static bool IsExtensionInstalled(string extensionsDir, string extensionId)
	{
		if (!Directory.Exists(extensionsDir))
		{
			return false;
		}

		try
		{
			string[] directories = Directory.GetDirectories(extensionsDir);
			foreach (string directory in directories)
			{
				string name = Path.GetFileName(directory);
				if (name.StartsWith(extensionId, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}
		catch
		{
		}

		return false;
	}

	private static IEnumerable<string> GetVSCodeTargetDirectories(string embedDir)
	{
		var targets = new List<string>();

		if (!string.IsNullOrEmpty(embedDir))
		{
			targets.Add(embedDir);
			targets.Add(Path.Combine(embedDir, "editor", "resources", "app"));
			targets.Add(Path.Combine(embedDir, "resources", "app"));
			string serveWebDir = Path.Combine(embedDir, "cli-data-dir", "serve-web");
			if (Directory.Exists(serveWebDir))
			{
				foreach (string sub in Directory.GetDirectories(serveWebDir))
				{
					targets.Add(sub);
				}
			}
		}

		if (OS.GetName() == "Windows")
		{
			string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

			targets.Add(Path.Combine(localAppData, "Programs", "Microsoft VS Code"));
			targets.Add(Path.Combine(programFiles, "Microsoft VS Code"));
			targets.Add(Path.Combine(programFilesX86, "Microsoft VS Code"));
		}
		else if (OS.GetName() == "macOS")
		{
			targets.Add("/Applications/Visual Studio Code.app/Contents/Resources/app");
		}
		else
		{
			targets.Add("/usr/share/code");
			targets.Add("/opt/visual-studio-code");
		}

		return targets;
	}
}
