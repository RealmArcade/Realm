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

	private Process? _vscodeProcess;
	private Control _containerControl = null!;
	private bool _isInitialized;
	private bool _isVisible;

	public bool IsVisible
	{
		get
		{
			if (_vscodeProcess == null)
			{
				return false;
			}
			try
			{
				return !_vscodeProcess.HasExited;
			}
			catch
			{
				return false;
			}
		}
	}

	private void SaveCurrentLocaleToFile()
	{
		try
		{
			string dir = ProjectSettings.GlobalizePath("user://");
			if (!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			string code = GameSettings.Language.ToLocaleCode();
			File.WriteAllText(Path.Combine(dir, "realm-locale.txt"), code);

			var activeDict = LocalizationManager.GetDictionary(code);
			File.WriteAllText(Path.Combine(dir, "realm-locale-dict.json"), JsonSerializer.Serialize(activeDict));

			var enDict = LocalizationManager.GetDictionary("en");
			File.WriteAllText(Path.Combine(dir, "realm-locale-en.json"), JsonSerializer.Serialize(enDict));
		}
		catch
		{
		}
	}

	private void OnLanguageChanged(GameLanguage newLanguage)
	{
		SaveCurrentLocaleToFile();
	}

	public void Initialize(Control containerControl)
	{
		if (_isInitialized)
		{
			_containerControl = containerControl;
			return;
		}

		LocalizationManager.LanguageChanged -= OnLanguageChanged;
		LocalizationManager.LanguageChanged += OnLanguageChanged;
		SaveCurrentLocaleToFile();

		_containerControl = containerControl;

		StartIpcHttpListener();

		_isInitialized = true;
	}

	private void EnsureWorkspaceMapApiDll(string projectRoot)
	{
		try
		{
			string mapFolderRaw = GetMapFolderToOpen(projectRoot);
			if (!string.IsNullOrEmpty(mapFolderRaw) && Directory.Exists(mapFolderRaw))
			{
				string libDir = Path.Combine(mapFolderRaw, "lib");
				if (!File.Exists(Path.Combine(libDir, "Realm.MapAPI.dll")))
				{
					GameLog.LogToConsole("Realm.MapAPI.dll missing in workspace. Running EnsureMapProjectFiles...");
					GameHost.EnsureMapProjectFiles(mapFolderRaw);
				}
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Failed to ensure Realm.MapAPI.dll in workspace: {ex.Message}");
		}
	}

	private int _ipcHttpPort = 8092;

	private async void StartIpcHttpListener()
	{
		try
		{
			var listener = new HttpListener();
			try
			{
				listener.Prefixes.Add($"http://127.0.0.1:{_ipcHttpPort}/api/");
				listener.Start();
			}
			catch
			{
				_ipcHttpPort = 8093;
				listener = new HttpListener();
				listener.Prefixes.Add($"http://127.0.0.1:{_ipcHttpPort}/api/");
				listener.Start();
			}

			GameLog.LogToConsole($"[VSCodeManager] HTTP IPC bridge listening on http://127.0.0.1:{_ipcHttpPort}/api/");

			while (true)
			{
				var ctx = await listener.GetContextAsync();
				_ = Task.Run(() => { HandleIpcHttpRequest(ctx); });
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[VSCodeManager] HTTP IPC bridge error: {ex.Message}");
		}
	}

	private readonly ConcurrentQueue<string> _pendingExtensionCommands = new ConcurrentQueue<string>();

	public void QueueWorkspaceMetadataReload()
	{
		_pendingExtensionCommands.Enqueue("reloadWorkspaceMetadata");
	}

	public async Task SaveAllOpenFilesAsync()
	{
		_pendingExtensionCommands.Enqueue("saveAll");
		await Task.Delay(500);
	}

	private async void HandleIpcHttpRequest(HttpListenerContext ctx)
	{
		try
		{
			SetCorsHeaders(ctx);

			if (ctx.Request.HttpMethod == "OPTIONS")
			{
				HandleIpcOptionsRequest(ctx);
				return;
			}

			if (ctx.Request.HttpMethod == "GET")
			{
				await HandleIpcGetRequest(ctx);
				return;
			}

			await ProcessIpcPostRequest(ctx);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[VSCodeManager] HandleIpcHttpRequest error: {ex.Message}");
			try
			{
				ctx.Response.StatusCode = 500;
				ctx.Response.Close();
			}
			catch
			{
			}
		}
	}

	private void SetCorsHeaders(HttpListenerContext ctx)
	{
		ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
		ctx.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
		ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
	}

	private async Task ProcessIpcPostRequest(HttpListenerContext ctx)
	{
		using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
		string body = await reader.ReadToEndAsync();
		JsonNode? node = JsonNode.Parse(body);
		string? action = GetActionFromNode(node);

		CheckFocusGodotWindow(action, node);

		var responseObj = new JsonObject();
		await RouteIpcAction(action, node, responseObj);

		await SendIpcResponse(ctx, responseObj);
	}

	private string? GetActionFromNode(JsonNode? node)
	{
		return node?["action"]?.ToString() ?? node?["type"]?.ToString();
	}

	private void CheckFocusGodotWindow(string? action, JsonNode? node)
	{
		if (ShouldFocusGodot(action, node))
		{
			EditorWindowIntegration.RestoreAndFocusGodotWindow();
		}
	}

	private bool ShouldFocusGodot(string? action, JsonNode? node)
	{
		return action == "openVfxDialog" ||
		       action == "openModelPicker" ||
		       action == "openAbilityVfxDialog" ||
		       action == "openAnimationStudio" ||
		       action == "openAnimationPreview" ||
		       action == "openEditAnimations" ||
		       (node?["focusGodot"]?.GetValue<bool>() ?? false);
	}

	private async Task RouteIpcAction(string? action, JsonNode? node, JsonObject responseObj)
	{
		if (string.IsNullOrEmpty(action)) return;

		if (action == "invokeEditorApi" || action.StartsWith("editorApi."))
		{
			await HandleEditorApiRequest(node, action, responseObj);
			return;
		}

		if (TryRouteDialogAction(action, node, responseObj)) return;
		if (TryRouteFileAction(action, node, responseObj)) return;
		if (TryRouteConversionAction(action, node, responseObj)) return;
	}

	private bool TryRouteDialogAction(string? action, JsonNode? node, JsonObject responseObj)
	{
		switch (action)
		{
			case "openVfxDialog":
				HandleOpenVfxDialog(node, responseObj);
				return true;
			case "openModelPicker":
				HandleOpenModelPicker(node, responseObj);
				return true;
			case "openAbilityVfxDialog":
				HandleOpenAbilityVfxDialog(node, responseObj);
				return true;
			case "openAnimationStudio":
			case "openAnimationPreview":
			case "openEditAnimations":
				HandleOpenAnimationStudio(node, responseObj);
				return true;
		}

		return false;
	}

	private bool TryRouteFileAction(string? action, JsonNode? node, JsonObject responseObj)
	{
		switch (action)
		{
			case "formatAndSaveJson":
			case "saveJsonFile":
			case "saveMetadata":
			case "saveTerrain":
				HandleFormatAndSaveJson(node, action, responseObj);
				return true;
			case "reloadMetadata":
			case "updateMetadata":
				HandleReloadMetadata(responseObj);
				return true;
		}

		return false;
	}

	private bool TryRouteConversionAction(string? action, JsonNode? node, JsonObject responseObj)
	{
		switch (action)
		{
			case "convertRtex":
				HandleConvertRtex(node, responseObj);
				return true;
			case "renderRanim":
				HandleRenderRanim(node, responseObj);
				return true;
			case "convertRmesh":
				HandleConvertRmesh(node, responseObj);
				return true;
			case "convertRaud":
				HandleConvertRaud(node, responseObj);
				return true;
		}

		return false;
	}

	private async Task SendIpcResponse(HttpListenerContext ctx, JsonObject responseObj)
	{
		string resJson = responseObj.ToJsonString();
		byte[] resBytes = Encoding.UTF8.GetBytes(resJson);
		ctx.Response.ContentType = "application/json";
		ctx.Response.ContentLength64 = resBytes.Length;
		await ctx.Response.OutputStream.WriteAsync(resBytes, 0, resBytes.Length);
		ctx.Response.Close();
	}


	private void HandleIpcOptionsRequest(HttpListenerContext ctx)
	{
		ctx.Response.StatusCode = 200;
		ctx.Response.Close();
	}

	private async Task HandleIpcGetRequest(HttpListenerContext ctx)
	{
		if (ctx.Request.Url != null && ctx.Request.Url.AbsolutePath.EndsWith("/locale"))
		{
			string locCode = GameSettings.Language.ToLocaleCode();
			var dict = LocalizationManager.GetDictionary(locCode);
			var locObj = new JsonObject();
			locObj["locale"] = locCode;
			locObj["dictionary"] = JsonSerializer.SerializeToNode(dict);
			string locJson = locObj.ToJsonString();
			byte[] locBytes = Encoding.UTF8.GetBytes(locJson);
			ctx.Response.ContentType = "application/json";
			ctx.Response.ContentLength64 = locBytes.Length;
			await ctx.Response.OutputStream.WriteAsync(locBytes, 0, locBytes.Length);
			ctx.Response.Close();
			return;
		}

		var pollResponseObj = new JsonObject();
		var commandsArray = new JsonArray();
		while (_pendingExtensionCommands.TryDequeue(out var cmd))
		{
			commandsArray.Add(cmd);
		}

		if (GameContext.SessionState.IsMapEditorMode && !string.IsNullOrEmpty(GameContext.EditorService._editingDirectory))
		{
			pollResponseObj["workspaceMetadataPath"] = Path.Combine(GameContext.EditorService._editingDirectory, "metadata.json");
		}

		pollResponseObj["commands"] = commandsArray;
		string pollResJson = pollResponseObj.ToJsonString();
		byte[] pollResBytes = Encoding.UTF8.GetBytes(pollResJson);
		ctx.Response.ContentType = "application/json";
		ctx.Response.ContentLength64 = pollResBytes.Length;
		await ctx.Response.OutputStream.WriteAsync(pollResBytes, 0, pollResBytes.Length);
		ctx.Response.Close();
	}

	private async Task HandleEditorApiRequest(JsonNode? node, string action, JsonObject responseObj)
	{
		string apiMethod = GetApiMethodName(node, action);
		string reqId = node?["requestId"]?.ToString() ?? "";

		var (apiSuccess, invocationResult, apiError) = await ExecuteEditorApiMethod(apiMethod, node);

		PopulateEditorApiResponse(responseObj, apiMethod, reqId, apiSuccess, invocationResult, apiError);
	}

	private string GetApiMethodName(JsonNode? node, string action)
	{
		return action == "invokeEditorApi" ? (node?["method"]?.ToString() ?? "") : action.Substring(10);
	}

	private async Task<(bool success, object? result, string error)> ExecuteEditorApiMethod(string apiMethod, JsonNode? node)
	{
		try
		{
			var editorApi = GameContext.IEditorAPI;
			if (editorApi == null)
				return (false, null, "IEditorAPI service not registered or editor inactive.");

			var methodInfo = typeof(IEditorAPI).GetMethods()
				.FirstOrDefault(m => string.Equals(m.Name, apiMethod, StringComparison.OrdinalIgnoreCase));

			if (methodInfo == null)
				return (false, null, $"Method '{apiMethod}' not found on IEditorAPI.");

			var args = ParseApiArguments(methodInfo, node);
			return await InvokeMethodAsync(editorApi, methodInfo, args);
		}
		catch (Exception ex)
		{
			return (false, null, ex.Message);
		}
	}

	private async Task<(bool success, object? result, string error)> InvokeMethodAsync(IEditorAPI editorApi, MethodInfo methodInfo, object?[] args)
	{
		var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		Callable.From(() =>
		{
			try
			{
				var result = methodInfo.Invoke(editorApi, args);
				tcs.TrySetResult(result);
			}
			catch (TargetInvocationException tie)
			{
				tcs.TrySetException(tie.InnerException ?? tie);
			}
			catch (Exception exMethod)
			{
				tcs.TrySetException(exMethod);
			}
		}).CallDeferred();

		try
		{
			var invocationResult = await tcs.Task;
			return (true, invocationResult, "");
		}
		catch (Exception exAsync)
		{
			GameLog.LogError($"[VSCodeManager] EditorAPI call {methodInfo.Name} failed: {exAsync.Message}");
			return (false, null, exAsync.Message);
		}
	}

	private void PopulateEditorApiResponse(JsonObject responseObj, string apiMethod, string reqId, bool apiSuccess, object? invocationResult, string apiError)
	{
		responseObj["action"] = "invokeEditorApiResult";
		responseObj["method"] = apiMethod;
		responseObj["requestId"] = reqId;
		responseObj["success"] = apiSuccess;

		if (invocationResult != null)
		{
			responseObj["result"] = JsonSerializer.SerializeToNode(invocationResult);
			if (invocationResult is string strVal)
				responseObj["instanceId"] = strVal;
			else if (invocationResult is bool bVal)
				responseObj["deleted"] = bVal;
		}

		if (!string.IsNullOrEmpty(apiError))
			responseObj["error"] = apiError;
	}


	private object?[] ParseApiArguments(MethodInfo methodInfo, JsonNode? node)
	{
		var parameters = methodInfo.GetParameters();
		var args = new object?[parameters.Length];
		var argsNode = node?["args"] as JsonObject ?? node as JsonObject;

		for (int i = 0; i < parameters.Length; i++)
		{
			args[i] = ParseSingleArgument(parameters[i], argsNode);
		}

		return args;
	}

	private object? ParseSingleArgument(ParameterInfo param, JsonObject? argsNode)
	{
		Type paramType = param.ParameterType;
		JsonNode? paramNode = GetParamNode(param.Name, argsNode);

		if (paramNode == null)
		{
			return GetDefaultArgument(paramType, param, argsNode);
		}

		if (paramType == typeof(Vector3))
		{
			return ParseVector3Node(paramNode);
		}

		return JsonSerializer.Deserialize(paramNode.ToJsonString(), paramType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
	}

	private JsonNode? GetParamNode(string? paramName, JsonObject? argsNode)
	{
		if (argsNode == null) return null;

		if (paramName != null && argsNode.TryGetPropertyValue(paramName, out var matchedNode))
		{
			return matchedNode;
		}

		foreach (var kvp in argsNode)
		{
			if (string.Equals(kvp.Key, paramName, StringComparison.OrdinalIgnoreCase))
			{
				return kvp.Value;
			}
		}

		return null;
	}

	private object? GetDefaultArgument(Type paramType, ParameterInfo param, JsonObject? argsNode)
	{
		if (paramType == typeof(Vector3))
		{
			return ExtractVector3FromArgsNode(argsNode);
		}

		if (param.HasDefaultValue)
			return param.DefaultValue;
		if (paramType.IsValueType)
			return Activator.CreateInstance(paramType);

		return null;
	}

	private Vector3 ExtractVector3FromArgsNode(JsonObject? argsNode)
	{
		float vx = 0f, vy = 0f, vz = 0f;
		if (argsNode != null)
		{
			foreach (var kvp in argsNode)
			{
				if (kvp.Value == null) continue;
				if (string.Equals(kvp.Key, "x", StringComparison.OrdinalIgnoreCase))
					vx = kvp.Value.GetValue<float>();
				else if (string.Equals(kvp.Key, "y", StringComparison.OrdinalIgnoreCase))
					vy = kvp.Value.GetValue<float>();
				else if (string.Equals(kvp.Key, "z", StringComparison.OrdinalIgnoreCase))
					vz = kvp.Value.GetValue<float>();
			}
		}

		return new Vector3(vx, vy, vz);
	}

	private Vector3 ParseVector3Node(JsonNode paramNode)
	{
		if (paramNode is JsonObject vObj)
		{
			return ExtractVector3FromArgsNode(vObj);
		}

		if (paramNode is JsonArray vArr && vArr.Count >= 3)
		{
			return new Vector3(
				vArr[0]?.GetValue<float>() ?? 0f,
				vArr[1]?.GetValue<float>() ?? 0f,
				vArr[2]?.GetValue<float>() ?? 0f
			);
		}

		return new Vector3(0f, 0f, 0f);
	}


	private void HandleOpenVfxDialog(JsonNode? node, JsonObject responseObj)
	{
		string weaponId = node?["weaponId"]?.ToString() ?? "";
		var weaponDataNode = node?["weaponData"];

		Callable.From(() =>
		{
			WeaponMetadata? meta = null;
			if (!string.IsNullOrEmpty(weaponId) && GameHost.WeaponRegistry.TryGetValue(weaponId, out var existing))
			{
				meta = existing;
			}
			else if (weaponDataNode != null)
			{
				try
				{
					meta = JsonSerializer.Deserialize<WeaponMetadata>(
						weaponDataNode.ToJsonString(),
						new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
					);
				}
				catch (Exception ex)
				{
					GameLog.LogError($"[VSCodeManager] openVfxDialog deserialize error: {ex.Message}");
				}
			}

			if (GameContext.MapEditorHud != null)
			{
				GameContext.MapEditorHud.OpenWeaponVfxDialog(weaponId, meta, (updatedMeta) => { GameContext.MapEditorHud.SaveCustomWeaponToMetadata(weaponId, updatedMeta); });
			}
		}).CallDeferred();

		responseObj["action"] = "openVfxDialogResult";
		responseObj["success"] = true;
	}

	private void HandleOpenModelPicker(JsonNode? node, JsonObject responseObj)
	{
		string entityId = GetStringProperty(node, "unitId", "");
		if (string.IsNullOrEmpty(entityId)) entityId = GetStringProperty(node, "entityId", "");
		string fieldName = GetStringProperty(node, "field", "ModelPath");
		string domain = GetStringProperty(node, "domain", "units");
		string currentPath = GetStringProperty(node, "currentPath", "");

		Callable.From(() =>
		{
			if (GameContext.MapEditorHud == null) return;

			GameContext.MapEditorHud.OpenModelPickerDialog(entityId, fieldName, domain, currentPath, (updatedPath) => { GameContext.MapEditorHud.SaveEntityModelPathToMetadata(entityId, fieldName, domain, updatedPath); });
		}).CallDeferred();

		responseObj["action"] = "openModelPickerResult";
		responseObj["success"] = true;
	}


	private void HandleOpenAbilityVfxDialog(JsonNode? node, JsonObject responseObj)
	{
		string abilityId = GetStringProperty(node, "abilityId", "");
		var abilityDataNode = node?["abilityData"] as JsonObject;

		Callable.From(() =>
		{
			if (GameContext.MapEditorHud == null) return;

			GameContext.MapEditorHud.OpenAbilityVfxDialog(abilityId, abilityDataNode, (updatedData) =>
			{
				if (updatedData == null) return;

				string vfx = GetStringProperty(updatedData, "VisualEffect", "");
				string sound = GetStringProperty(updatedData, "CastSound", "");
				string icon = GetStringProperty(updatedData, "IconPath", "");
				float aoe = GetFloatProperty(updatedData, "AreaOfEffectRadius", 0f);

				GameContext.MapEditorHud.SaveCustomAbilityVfxToMetadata(abilityId, vfx, sound, icon, aoe);
			});
		}).CallDeferred();

		responseObj["action"] = "openAbilityVfxDialogResult";
		responseObj["success"] = true;
	}


	private void HandleOpenAnimationStudio(JsonNode? node, JsonObject responseObj)
	{
		string unitId = node?["unitId"]?.ToString() ?? node?["entityId"]?.ToString() ?? "";
		string modelPath = node?["modelPath"]?.ToString() ?? "";

		Callable.From(() =>
		{
			if (GameContext.MapEditorHud != null)
			{
				GameContext.MapEditorHud.OpenAnimationPreviewDialog(unitId, modelPath);
			}
		}).CallDeferred();

		responseObj["action"] = "openAnimationStudioResult";
		responseObj["success"] = true;
	}

	private void HandleFormatAndSaveJson(JsonNode? node, string action, JsonObject responseObj)
	{
		string filePath = GetSaveFilePath(node, action);
		string content = node?["content"]?.ToString() ?? node?["text"]?.ToString() ?? "";
		string requestId = node?["requestId"]?.ToString() ?? "";

		var (success, formattedContent, errorMsg) = TryFormatAndSaveJsonFile(filePath, content);

		if (success)
		{
			NotifyEditorOfFileUpdate(filePath);
		}

		PopulateFormatAndSaveJsonResponse(responseObj, requestId, success, filePath, formattedContent, errorMsg);
	}


	private string GetSaveFilePath(JsonNode? node, string action)
	{
		string filePath = node?["filePath"]?.ToString() ?? "";
		if (string.IsNullOrEmpty(filePath))
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			filePath = Path.Combine(wsPath, action == "saveTerrain" ? "terrain.json" : "metadata.json");
		}
		else if (!Path.IsPathRooted(filePath))
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			filePath = Path.Combine(wsPath, filePath);
		}

		return filePath;
	}


	private (bool success, string formattedContent, string errorMsg) TryFormatAndSaveJsonFile(string filePath, string content)
	{
		try
		{
			string fileName = Path.GetFileName(filePath).ToLowerInvariant();

			string formattedContent = FormatSpecialJsonFile(fileName, filePath, content);

			return (true, formattedContent, "");
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[VSCodeManager] formatAndSaveJson error: {ex.Message}");
			return (false, "", ex.Message);
		}
	}

	private string FormatSpecialJsonFile(string fileName, string filePath, string content)
	{
		string formattedContent = "";
		if (fileName == "metadata.json")
		{
			try
			{
				var metadata = MapFileService.LoadMetadataFromJson(content);
				MapFileService.SaveMetadata(filePath, metadata);
				formattedContent = MapFileService.SaveMetadataToJson(metadata);
			}
			catch
			{
				formattedContent = MapJsonFormatter.FormatJson(content);
				MapJsonFormatter.SaveFormattedJson(filePath, formattedContent);
			}
		}
		else if (fileName == "manifest.json")
		{
			try
			{
				var manifest = MapFileService.LoadManifestFromJson(content);
				MapFileService.SaveManifest(filePath, manifest);
				formattedContent = MapFileService.SaveManifestToJson(manifest);
			}
			catch
			{
				formattedContent = MapJsonFormatter.FormatJson(content);
				MapJsonFormatter.SaveFormattedJson(filePath, formattedContent);
			}
		}
		else if (fileName == "terrain.json")
		{
			try
			{
				var terrain = MapFileService.LoadTerrainFromJson(content);
				MapFileService.SaveTerrain(filePath, terrain);
				formattedContent = MapFileService.SaveTerrainToJson(terrain);
			}
			catch
			{
				formattedContent = MapJsonFormatter.FormatJson(content);
				MapJsonFormatter.SaveFormattedJson(filePath, formattedContent);
			}
		}
		else
		{
			formattedContent = MapJsonFormatter.FormatJson(content);
			MapJsonFormatter.SaveFormattedJson(filePath, formattedContent);
		}

		return formattedContent;
	}


	private void NotifyEditorOfFileUpdate(string filePath)
	{
		Callable.From(() =>
		{
			string fileName = Path.GetFileName(filePath).ToLowerInvariant();
			if (fileName == "metadata.json" || fileName == "manifest.json")
			{
				HandleMetadataUpdate(fileName);
			}
			else if (fileName == "terrain.json")
			{
				HandleTerrainUpdate(filePath);
			}
		}).CallDeferred();
	}

	private void HandleMetadataUpdate(string fileName)
	{
		string display = fileName == "manifest.json" ? "manifest.json" : (MapWorkspaceService.GetActiveWorkspacePath() != null ? "Workspace Metadata" : "metadata.json");
		Action reloadAction = () =>
		{
			if (GameContext.MapEditorHud != null)
			{
				GameContext.MapEditorHud.ReadMetadataAndRefreshTextures();
				GameContext.MapEditorHud.ShowFeedback(string.Format(TranslationServer.Translate("{0} updated externally â€” reloaded."), display));
			}
			else if (GameContext.Host != null && GameContext.Host.GroundTerrain != null)
			{
				GameContext.Host.GroundTerrain.ReloadTerrainTextures(true);
			}
		};

		if (FloatingDialogBase.HasAnyDialogOpen && GameContext.MapEditorHud != null)
		{
			GameContext.MapEditorHud.ShowConfirmationDialog(
				$"External edits detected in {display}. Reload external changes or keep current dialog changes?",
				onConfirm: reloadAction,
				confirmText: TranslationServer.Translate("RELOAD"),
				cancelText: TranslationServer.Translate("KEEP CHANGES")
			);
		}
		else
		{
			reloadAction();
		}
	}


	private void HandleTerrainUpdate(string filePath)
	{
		Action reloadAction = () =>
		{
			if (GameContext.Host != null && GameContext.SessionState.IsMapEditorMode)
			{
				GameContext.Host.LoadMapFromFile(filePath);
				GameContext.MapEditorHud?.ShowFeedback(TranslationServer.Translate("terrain.json updated externally â€” reloaded."));
			}
		};

		if (FloatingDialogBase.HasAnyDialogOpen && GameContext.MapEditorHud != null)
		{
			GameContext.MapEditorHud.ShowConfirmationDialog(
				TranslationServer.Translate("External edits detected in terrain.json. Reload external changes or keep current dialog changes?"),
				onConfirm: reloadAction,
				confirmText: TranslationServer.Translate("RELOAD"),
				cancelText: TranslationServer.Translate("KEEP CHANGES")
			);
		}
		else
		{
			reloadAction();
		}
	}

	private void PopulateFormatAndSaveJsonResponse(JsonObject responseObj, string requestId, bool success, string filePath, string formattedContent, string errorMsg)
	{
		responseObj["action"] = "formatAndSaveJsonResult";
		responseObj["type"] = "formatAndSaveJsonResult";
		responseObj["requestId"] = requestId;
		responseObj["success"] = success;
		responseObj["filePath"] = filePath;
		responseObj["formattedContent"] = formattedContent;
		responseObj["error"] = errorMsg;
	}


	private void HandleReloadMetadata(JsonObject responseObj)
	{
		Callable.From(() =>
		{
			Action reloadAction = () =>
			{
				if (GameContext.MapEditorHud != null)
				{
					GameContext.MapEditorHud.ReadMetadataAndRefreshTextures();
					GameContext.MapEditorHud.ShowFeedback(TranslationServer.Translate("metadata.json updated externally â€” reloaded."));
				}
				else if (GameContext.Host != null && GameContext.Host.GroundTerrain != null)
				{
					GameContext.Host.GroundTerrain.ReloadTerrainTextures(true);
				}
			};

			if (FloatingDialogBase.HasAnyDialogOpen && GameContext.MapEditorHud != null)
			{
				GameContext.MapEditorHud.ShowConfirmationDialog(
					TranslationServer.Translate("External edits detected in metadata.json. Reload external changes or keep current dialog changes?"),
					onConfirm: reloadAction,
					confirmText: TranslationServer.Translate("RELOAD"),
					cancelText: TranslationServer.Translate("KEEP CHANGES")
				);
			}
			else
			{
				reloadAction();
			}
		}).CallDeferred();

		responseObj["action"] = "reloadMetadataResult";
		responseObj["type"] = "reloadMetadataResult";
		responseObj["success"] = true;
	}

	private void HandleConvertRtex(JsonNode? node, JsonObject responseObj)
	{
		string inputPath = node?["inputPath"]?.ToString() ?? node?["filePath"]?.ToString() ?? "";
		string outputPath = node?["outputPath"]?.ToString() ?? "";
		int layer = node?["layer"] is JsonValue layerNode ? layerNode.GetValue<int>() : 0;

		try
		{
			var res = TextureConverter.ExtractPngFromRtex(inputPath, outputPath, layer);
			PopulateRtexConversionResponse(responseObj, res, inputPath);
		}
		catch (Exception ex)
		{
			PopulateErrorResponse(responseObj, "convertRtexResult", ex.Message);
		}
	}

	private void PopulateRtexConversionResponse(JsonObject responseObj, TextureConversionResult res, string inputPath)
	{
		responseObj["action"] = "convertRtexResult";
		responseObj["type"] = "convertRtexResult";
		responseObj["success"] = res.Success;

		if (!res.Success)
		{
			responseObj["error"] = res.ErrorMessage;
			return;
		}

		responseObj["outputPath"] = res.OutputPath;
		AttachRtexMetadataIfExists(responseObj, inputPath);
	}

	private void AttachRtexMetadataIfExists(JsonObject responseObj, string inputPath)
	{
		try
		{
			if (File.Exists(inputPath))
			{
				byte[] rtexBytes = File.ReadAllBytes(inputPath);
				string? metaJson = RealmContainerFile.ExtractMetadataFromBytes(rtexBytes, RtexFile.MagicBytes);
				if (!string.IsNullOrEmpty(metaJson))
				{
					responseObj["metadata"] = JsonNode.Parse(metaJson);
				}
			}
		}
		catch
		{
		}
	}

	private void PopulateErrorResponse(JsonObject responseObj, string actionName, string errorMessage)
	{
		responseObj["action"] = actionName;
		responseObj["type"] = actionName;
		responseObj["success"] = false;
		responseObj["error"] = errorMessage;
	}


	private void HandleRenderRanim(JsonNode? node, JsonObject responseObj)
	{
		string inputPath = node?["inputPath"]?.ToString() ?? node?["filePath"]?.ToString() ?? "";
		string outputPath = node?["outputPath"]?.ToString() ?? "";
		try
		{
			var options = new RanimRenderOptions
			{
				Format = RanimOutputFormat.Webp,
				Width = 128,
				Height = 128,
				Fps = 12.0f
			};
			var res = RanimRenderer.ExportFile(inputPath, outputPath, options);
			responseObj["action"] = "renderRanimResult";
			responseObj["type"] = "renderRanimResult";
			responseObj["success"] = res.Success;
			if (!res.Success)
			{
				responseObj["error"] = res.ErrorMessage;
			}
			else
			{
				responseObj["outputPath"] = res.OutputPath;
			}
		}
		catch (Exception ex)
		{
			responseObj["action"] = "renderRanimResult";
			responseObj["type"] = "renderRanimResult";
			responseObj["success"] = false;
			responseObj["error"] = ex.Message;
		}
	}

	private void HandleConvertRmesh(JsonNode? node, JsonObject responseObj)
	{
		string inputPath = node?["inputPath"]?.ToString() ?? node?["filePath"]?.ToString() ?? "";
		string outputPath = node?["outputPath"]?.ToString() ?? "";

		try
		{
			if (!File.Exists(inputPath))
			{
				PopulateErrorResponse(responseObj, "convertRmeshResult", $"RMESH file not found: {inputPath}");
				return;
			}

			byte[] rmeshBytes = File.ReadAllBytes(inputPath);
			var (metaJson, glbBytes, _) = RmeshFile.Parse(rmeshBytes);

			if (glbBytes.Length == 0)
			{
				PopulateErrorResponse(responseObj, "convertRmeshResult", "No GLB payload found in RMESH file.");
				return;
			}

			SaveGlbBytes(outputPath, glbBytes);
			PopulateRmeshConversionResponse(responseObj, outputPath, metaJson);
		}
		catch (Exception ex)
		{
			PopulateErrorResponse(responseObj, "convertRmeshResult", ex.Message);
		}
	}

	private void SaveGlbBytes(string outputPath, byte[] glbBytes)
	{
		if (string.IsNullOrEmpty(outputPath)) return;

		string? dir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);

		File.WriteAllBytes(outputPath, glbBytes);
	}

	private void PopulateRmeshConversionResponse(JsonObject responseObj, string outputPath, string? metaJson)
	{
		responseObj["action"] = "convertRmeshResult";
		responseObj["type"] = "convertRmeshResult";
		responseObj["success"] = true;
		responseObj["outputPath"] = outputPath;

		if (!string.IsNullOrEmpty(metaJson))
		{
			responseObj["metadata"] = JsonNode.Parse(metaJson);
		}
	}


	private string GetStringProperty(JsonNode? node, string key, string defaultValue = "")
	{
		if (node == null) return defaultValue;
		var val = node[key];
		if (val == null) return defaultValue;
		return val.ToString() ?? defaultValue;
	}

	private int GetIntProperty(JsonNode? node, string key, int defaultValue = 0)
	{
		if (node == null) return defaultValue;
		var val = node[key];
		if (val == null) return defaultValue;
		return (int)val;
	}

	private float GetFloatProperty(JsonNode? node, string key, float defaultValue = 0f)
	{
		if (node == null) return defaultValue;
		var val = node[key];
		if (val == null) return defaultValue;
		return (float)val;
	}

	private void HandleConvertRaud(JsonNode? node, JsonObject responseObj)
	{
		string inputPath = GetStringProperty(node, "inputPath", "");
		if (string.IsNullOrEmpty(inputPath)) inputPath = GetStringProperty(node, "filePath", "");
		string outputPath = GetStringProperty(node, "outputPath", "");
		int trackIndex = GetIntProperty(node, "trackIndex", 0);

		try
		{
			if (!File.Exists(inputPath))
			{
				PopulateErrorResponse(responseObj, "convertRaudResult", $"RAUD file not found: {inputPath}");
				return;
			}

			byte[] raudBytes = File.ReadAllBytes(inputPath);
			var (metaJson, entries, _) = RaudFile.Parse(raudBytes);

			if (entries.Count <= trackIndex)
			{
				PopulateErrorResponse(responseObj, "convertRaudResult", $"Audio track {trackIndex} not found in RAUD file.");
				return;
			}

			if (entries[trackIndex].Data.Length == 0)
			{
				PopulateErrorResponse(responseObj, "convertRaudResult", $"Audio track {trackIndex} not found in RAUD file.");
				return;
			}

			SaveAudioTrack(outputPath, entries[trackIndex].Data);
			PopulateRaudConversionResponse(responseObj, outputPath, entries.Count, metaJson);
		}
		catch (Exception ex)
		{
			PopulateErrorResponse(responseObj, "convertRaudResult", ex.Message);
		}
	}

	private void SaveAudioTrack(string outputPath, byte[] trackData)
	{
		if (string.IsNullOrEmpty(outputPath)) return;

		string? dir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);

		File.WriteAllBytes(outputPath, trackData);
	}

	private void PopulateRaudConversionResponse(JsonObject responseObj, string outputPath, int trackCount, string? metaJson)
	{
		responseObj["action"] = "convertRaudResult";
		responseObj["type"] = "convertRaudResult";
		responseObj["success"] = true;
		responseObj["outputPath"] = outputPath;
		responseObj["trackCount"] = trackCount;

		if (!string.IsNullOrEmpty(metaJson))
		{
			responseObj["metadata"] = JsonNode.Parse(metaJson);
		}
	}


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
					GameContext.EditorService.GenerateMapDirectoryDomain("blank_map", blankDir, Realm.Client.Utils.PathUtils.GetProjectRoot(), Realm.Client.Utils.PathUtils.FindPath("MapTemplate"));
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
