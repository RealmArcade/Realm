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
				SetCurrentProcessExplicitAppUserModelID("Realm.GameClient");
			}
			catch
			{
			}
			AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanUp();
		}
	}

	private bool _isInstalling = false;
	private bool _installCompleted = false;
	private Task _installTask;
	private readonly object _installLock = new object();

	private static readonly string[] RequiredExtensions = new[]
	{
		"muhammad-sammy.csharp",
		"OHZIInteractiveStudio.ohzi-vscode-glb-viewer",
		"Gruntfuggly.todo-tree",
		// "mechatroner.rainbow-json",
		"patcx.vscode-nuget-gallery",
		"AykutSarac.jsoncrack-vscode",
		"vadimcn.vscode-lldb",
		"haoyangzeng.opencui"
		// "akondratiuk1-dev.texture-viewer",
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
				string version = jsonNode?["extensionVersion"]?.GetValue<string>();
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
		version = jsonNode?["version"]?.GetValue<string>();
		return !string.IsNullOrWhiteSpace(version);
	}

	public bool IsInstalled()
	{
		string embedDir = GetVSCodeDirectory();
		string exePath = Path.Combine(embedDir, "editor", "bin", "codium.exe");
		string completedMarkerPath = Path.Combine(embedDir, "install_completed.marker");
		string wasiPath = WasiSdkResolver.ResolveWasiSdkPath();
		string wasiClangPath = !string.IsNullOrEmpty(wasiPath) ? Path.Combine(wasiPath, "bin", "clang.exe") : string.Empty;
		string extVersion = GetRealmMapEditorVersion();
		string extDir = Path.Combine(embedDir, "user-data-dir", "extensions", $"speige.realm-map-editor-{extVersion}");
		string extensionsDir = Path.Combine(embedDir, "user-data-dir", "extensions");

		if (!AreBasePathsValid(exePath, completedMarkerPath, wasiClangPath, extDir))
			return false;

		if (!OpencodeCliResolver.IsAvailable())
			return false;

		return AreExtensionsInstalled(extensionsDir);
	}

	private bool AreBasePathsValid(string exePath, string completedMarkerPath, string wasiClangPath, string extDir)
	{
		if (!File.Exists(exePath) || new FileInfo(exePath).Length == 0)
			return false;
		if (!File.Exists(completedMarkerPath))
			return false;
		if (string.IsNullOrEmpty(wasiClangPath) || !File.Exists(wasiClangPath) || new FileInfo(wasiClangPath).Length == 0)
			return false;
		if (!Directory.Exists(extDir))
			return false;
		return true;
	}

	private bool AreExtensionsInstalled(string extensionsDir)
	{
		foreach (string requiredExt in RequiredExtensions)
		{
			if (!IsExtensionInstalled(extensionsDir, requiredExt))
				return false;
		}

		return true;
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
				GD.Print("Starting editor dependencies installation script: " + scriptPath);
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
					if (!string.IsNullOrEmpty(e.Data)) GD.Print("[installer] " + e.Data);
				};
				installProcess.ErrorDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrEmpty(e.Data)) GD.PrintErr("[installer error] " + e.Data);
				};

				installProcess.Start();
				installProcess.BeginOutputReadLine();
				installProcess.BeginErrorReadLine();
				installProcess.WaitForExit();
			}
			else
			{
				GD.PrintErr("VS Code installer script not found at: " + scriptPath);
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
			GD.PrintErr("Background VS Code installation failed: " + ex.Message);
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

	private Process _vscodeProcess;
	private Control _containerControl;
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

	public const int SW_RESTORE = 9;

	[DllImport("user32.dll")]
	public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	public static extern bool SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern bool BringWindowToTop(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern void SwitchToThisWindow(IntPtr hWnd, bool fUnknown);

	public static void RestoreAndFocusGodotWindow()
	{
		Callable.From(() =>
		{
			try
			{
				var currentMode = DisplayServer.WindowGetMode();
				if (currentMode == DisplayServer.WindowMode.Minimized)
				{
					var targetMode = GameSettings.WindowModeIdx switch
					{
						WindowMode.Fullscreen => DisplayServer.WindowMode.ExclusiveFullscreen,
						WindowMode.Borderless => DisplayServer.WindowMode.Windowed,
						_ => DisplayServer.WindowMode.Windowed
					};
					DisplayServer.WindowSetMode(targetMode);
				}

				DisplayServer.WindowMoveToForeground();

				if (OperatingSystem.IsWindows())
				{
					long rawHandle = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
					if (rawHandle != 0)
					{
						IntPtr hWnd = (IntPtr)rawHandle;
						ShowWindow(hWnd, SW_RESTORE);
						SwitchToThisWindow(hWnd, true);
						SetForegroundWindow(hWnd);
						BringWindowToTop(hWnd);
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[VSCodeManager] RestoreAndFocusGodotWindow error: {ex.Message}");
			}
		}).CallDeferred();
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

	[DllImport("kernel32.dll")]
	public static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

	[DllImport("kernel32.dll")]
	public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

	private static IntPtr _jobHandle = IntPtr.Zero;

	private static void EnsureChildProcessJobObject()
	{
		if (_jobHandle != IntPtr.Zero || !OperatingSystem.IsWindows()) return;
		try
		{
			_jobHandle = CreateJobObject(IntPtr.Zero, null);
			if (_jobHandle != IntPtr.Zero)
			{
				int JOBOBJECT_EXTENDED_LIMIT_INFORMATION = 9;
				uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

				int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION_STRUCT));
				IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
				try
				{
					ZeroMemory(extendedInfoPtr, length);
					// Structure layout:
					// JOBOBJECT_BASIC_LIMIT_INFORMATION (48 bytes):
					//   LONGLONG PerProcessUserTimeLimit (8)
					//   LONGLONG PerJobUserTimeLimit (8)
					//   DWORD LimitFlags (4) -> offset 16					
					Marshal.WriteInt32(extendedInfoPtr, 4, (int)JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE);
					Marshal.WriteInt32(extendedInfoPtr, 16, (int)JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE);
					SetInformationJobObject(_jobHandle, JOBOBJECT_EXTENDED_LIMIT_INFORMATION, extendedInfoPtr, (uint)length);
				}
				finally
				{
					Marshal.FreeHGlobal(extendedInfoPtr);
				}

				AssignProcessToJobObject(_jobHandle, Process.GetCurrentProcess().Handle);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to create/assign Job Object: {ex.Message}");
		}
	}

	[DllImport("kernel32.dll", EntryPoint = "RtlZeroMemory")]
	private static extern void ZeroMemory(IntPtr destination, int length);


	private static void AddProcessToJob(Process proc)
	{
		if (proc == null || proc.HasExited || !OperatingSystem.IsWindows()) return;
		try
		{
			EnsureChildProcessJobObject();
			if (_jobHandle != IntPtr.Zero)
			{
				AssignProcessToJobObject(_jobHandle, proc.Handle);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to assign process {proc.Id} to Job Object: {ex.Message}");
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

	private void StartVSCodeServer()
	{
		StartIpcHttpListener();
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
					GD.Print("Realm.MapAPI.dll missing in workspace. Running EnsureMapProjectFiles...");
					GameHost.EnsureMapProjectFiles(mapFolderRaw);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to ensure Realm.MapAPI.dll in workspace: {ex.Message}");
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

			GD.Print($"[VSCodeManager] HTTP IPC bridge listening on http://127.0.0.1:{_ipcHttpPort}/api/");

			while (true)
			{
				var ctx = await listener.GetContextAsync();
				_ = Task.Run(() => { HandleIpcHttpRequest(ctx); });
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[VSCodeManager] HTTP IPC bridge error: {ex.Message}");
		}
	}

	private readonly ConcurrentQueue<string> _pendingExtensionCommands = new ConcurrentQueue<string>();

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
			GD.PrintErr($"[VSCodeManager] HandleIpcHttpRequest error: {ex.Message}");
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
		var node = JsonNode.Parse(body);
		string action = GetActionFromNode(node);

		CheckFocusGodotWindow(action, node);

		var responseObj = new JsonObject();
		await RouteIpcAction(action, node, responseObj);

		await SendIpcResponse(ctx, responseObj);
	}

	private string GetActionFromNode(JsonNode node)
	{
		return node?["action"]?.ToString() ?? node?["type"]?.ToString();
	}

	private void CheckFocusGodotWindow(string action, JsonNode node)
	{
		if (ShouldFocusGodot(action, node))
		{
			RestoreAndFocusGodotWindow();
		}
	}

	private bool ShouldFocusGodot(string action, JsonNode node)
	{
		return action == "openVfxDialog" ||
		       action == "openModelPicker" ||
		       action == "openAbilityVfxDialog" ||
		       action == "openAnimationStudio" ||
		       action == "openAnimationPreview" ||
		       action == "openEditAnimations" ||
		       (node?["focusGodot"]?.GetValue<bool>() ?? false);
	}

	private async Task RouteIpcAction(string action, JsonNode node, JsonObject responseObj)
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

	private bool TryRouteDialogAction(string action, JsonNode node, JsonObject responseObj)
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

	private bool TryRouteFileAction(string action, JsonNode node, JsonObject responseObj)
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

	private bool TryRouteConversionAction(string action, JsonNode node, JsonObject responseObj)
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

		pollResponseObj["commands"] = commandsArray;
		string pollResJson = pollResponseObj.ToJsonString();
		byte[] pollResBytes = Encoding.UTF8.GetBytes(pollResJson);
		ctx.Response.ContentType = "application/json";
		ctx.Response.ContentLength64 = pollResBytes.Length;
		await ctx.Response.OutputStream.WriteAsync(pollResBytes, 0, pollResBytes.Length);
		ctx.Response.Close();
	}

	private async Task HandleEditorApiRequest(JsonNode node, string action, JsonObject responseObj)
	{
		string apiMethod = GetApiMethodName(node, action);
		string reqId = node?["requestId"]?.ToString() ?? "";

		var (apiSuccess, invocationResult, apiError) = await ExecuteEditorApiMethod(apiMethod, node);

		PopulateEditorApiResponse(responseObj, apiMethod, reqId, apiSuccess, invocationResult, apiError);
	}

	private string GetApiMethodName(JsonNode node, string action)
	{
		return action == "invokeEditorApi" ? (node?["method"]?.ToString() ?? "") : action.Substring(10);
	}

	private async Task<(bool success, object? result, string error)> ExecuteEditorApiMethod(string apiMethod, JsonNode node)
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
			GD.PrintErr($"[VSCodeManager] EditorAPI call {methodInfo.Name} failed: {exAsync.Message}");
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


	private object?[] ParseApiArguments(MethodInfo methodInfo, JsonNode node)
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

	private object? ParseSingleArgument(ParameterInfo param, JsonObject argsNode)
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

	private JsonNode? GetParamNode(string paramName, JsonObject argsNode)
	{
		if (argsNode == null) return null;

		if (argsNode.TryGetPropertyValue(paramName, out var matchedNode))
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

	private object? GetDefaultArgument(Type paramType, ParameterInfo param, JsonObject argsNode)
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

	private Vector3 ExtractVector3FromArgsNode(JsonObject argsNode)
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


	private void HandleOpenVfxDialog(JsonNode node, JsonObject responseObj)
	{
		string weaponId = node["weaponId"]?.ToString() ?? "";
		var weaponDataNode = node["weaponData"];

		Callable.From(() =>
		{
			WeaponMetadata meta = null;
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
					GD.PrintErr($"[VSCodeManager] openVfxDialog deserialize error: {ex.Message}");
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

	private void HandleOpenModelPicker(JsonNode node, JsonObject responseObj)
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


	private void HandleOpenAbilityVfxDialog(JsonNode node, JsonObject responseObj)
	{
		string abilityId = GetStringProperty(node, "abilityId", "");
		var abilityDataNode = node["abilityData"] as JsonObject;

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


	private void HandleOpenAnimationStudio(JsonNode node, JsonObject responseObj)
	{
		string unitId = node["unitId"]?.ToString() ?? node["entityId"]?.ToString() ?? "";
		string modelPath = node["modelPath"]?.ToString() ?? "";

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

	private void HandleFormatAndSaveJson(JsonNode node, string action, JsonObject responseObj)
	{
		string filePath = GetSaveFilePath(node, action);
		string content = node["content"]?.ToString() ?? node["text"]?.ToString() ?? "";
		string requestId = node["requestId"]?.ToString() ?? "";

		var (success, formattedContent, errorMsg) = TryFormatAndSaveJsonFile(filePath, content);

		if (success)
		{
			NotifyEditorOfFileUpdate(filePath);
		}

		PopulateFormatAndSaveJsonResponse(responseObj, requestId, success, filePath, formattedContent, errorMsg);
	}


	private string GetSaveFilePath(JsonNode node, string action)
	{
		string filePath = node["filePath"]?.ToString() ?? "";
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
			EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;

			string formattedContent = FormatSpecialJsonFile(fileName, filePath, content);

			return (true, formattedContent, "");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[VSCodeManager] formatAndSaveJson error: {ex.Message}");
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
				confirmText: "RELOAD",
				cancelText: "KEEP CHANGES"
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
				"External edits detected in terrain.json. Reload external changes or keep current dialog changes?",
				onConfirm: reloadAction,
				confirmText: "RELOAD",
				cancelText: "KEEP CHANGES"
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
					"External edits detected in metadata.json. Reload external changes or keep current dialog changes?",
					onConfirm: reloadAction,
					confirmText: "RELOAD",
					cancelText: "KEEP CHANGES"
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

	private void HandleConvertRtex(JsonNode node, JsonObject responseObj)
	{
		string inputPath = node["inputPath"]?.ToString() ?? node["filePath"]?.ToString() ?? "";
		string outputPath = node["outputPath"]?.ToString() ?? "";
		int layer = node["layer"] != null ? (int)node["layer"] : 0;

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


	private void HandleRenderRanim(JsonNode node, JsonObject responseObj)
	{
		string inputPath = node["inputPath"]?.ToString() ?? node["filePath"]?.ToString() ?? "";
		string outputPath = node["outputPath"]?.ToString() ?? "";
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

	private void HandleConvertRmesh(JsonNode node, JsonObject responseObj)
	{
		string inputPath = node["inputPath"]?.ToString() ?? node["filePath"]?.ToString() ?? "";
		string outputPath = node["outputPath"]?.ToString() ?? "";

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


	private string GetStringProperty(JsonNode node, string key, string defaultValue = "")
	{
		if (node == null) return defaultValue;
		var val = node[key];
		if (val == null) return defaultValue;
		return val.ToString() ?? defaultValue;
	}

	private int GetIntProperty(JsonNode node, string key, int defaultValue = 0)
	{
		if (node == null) return defaultValue;
		var val = node[key];
		if (val == null) return defaultValue;
		return (int)val;
	}

	private float GetFloatProperty(JsonNode node, string key, float defaultValue = 0f)
	{
		if (node == null) return defaultValue;
		var val = node[key];
		if (val == null) return defaultValue;
		return (float)val;
	}

	private void HandleConvertRaud(JsonNode node, JsonObject responseObj)
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
			GD.PrintErr($"Failed to open file in VSCodium: {ex.Message}");
		}
	}

	private bool _isLaunchingEditor;

	private async void LaunchOrFocusEditor()
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
				GD.PrintErr("VS Code executable not found at: " + desktopExe);
				return;
			}

			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string extensionsDir = Path.Combine(serverDataDir, "extensions");
			string workspacePath = GetMapFolderToOpen(projectRoot);

			await EnsureOpencodeCliInstalledAsync();

			EnsureWorkspaceMapApiDll(projectRoot);
			EnsureVSCodiumSettings(serverDataDir, workspacePath);
			RemoveAntigravityExtension(extensionsDir);
			EnsureOhziExtensionPatched(extensionsDir);
			EnsureOpencuiExtensionAutoStarts(extensionsDir);
			PatchVSCodiumConfiguration(embedDir);

			if (IsInstalling && _installTask != null)
			{
				await _installTask;
			}

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
				ApplyOpencodeEnvironmentVariables(_vscodeProcess.StartInfo);
				_vscodeProcess.Start();
				AddProcessToJob(_vscodeProcess);
				AttachWindowGrouping(_vscodeProcess);
				_isVisible = true;
				GD.Print($"[VSCodeManager] VSCodium desktop launched (PID {_vscodeProcess.Id}). Godot Host PID: {Environment.ProcessId}.");
			}
			else
			{
				_vscodeProcess.Refresh();
				IntPtr mainWindowHandle = _vscodeProcess.MainWindowHandle;
				if (mainWindowHandle != IntPtr.Zero)
				{
					AttachWindowGrouping(_vscodeProcess);
					ShowWindow(mainWindowHandle, SW_RESTORE);
					BringWindowToTop(mainWindowHandle);
					SetForegroundWindow(mainWindowHandle);
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
		}
		catch (Exception ex)
		{
			GD.PrintErr("Failed to launch or focus VSCodium: " + ex.Message);
		}
		finally
		{
			_isLaunchingEditor = false;
		}
	}

	private static async Task EnsureOpencodeCliInstalledAsync()
	{
		if (OpencodeCliResolver.IsAvailable())
		{
			return;
		}

		bool installed = await OpencodeCliInstaller.EnsureInstalledAsync();
		if (!installed)
		{
			GD.PrintErr("[VSCodeManager] opencode CLI is unavailable. The OpenCode Panel extension cannot start its backend.");
		}
	}

	private static void ApplyOpencodeEnvironmentVariables(ProcessStartInfo startInfo)
	{
		try
		{
			string opencodeDataDirectory = OpencodeCliResolver.GetDataDirectory();
			Directory.CreateDirectory(opencodeDataDirectory);
			startInfo.EnvironmentVariables["XDG_DATA_HOME"] = opencodeDataDirectory;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to prepare opencode data directory: {ex.Message}");
		}
	}

	private const uint WM_GETICON = 0x007F;
	private const uint WM_SETICON = 0x0080;
	private const int ICON_SMALL = 0;
	private const int ICON_BIG = 1;

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	private struct PROPERTYKEY
	{
		public Guid fmtid;
		public uint pid;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct PROPVARIANT
	{
		[FieldOffset(0)] public ushort vt;
		[FieldOffset(8)] public IntPtr pwszVal;
	}

	[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPropertyStore
	{
		[PreserveSig] int GetCount(out uint cProps);
		[PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
		[PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
		[PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
		[PreserveSig] int Commit();
	}

	[DllImport("shell32.dll", SetLastError = true)]
	private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore propertyStore);

	private static void SetWindowAppUserModelId(IntPtr hwnd, string appModelId)
	{
		if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindows()) return;
		try
		{
			var guid = new Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
			if (SHGetPropertyStoreForWindow(hwnd, ref guid, out var store) == 0 && store != null)
			{
				var pkey = new PROPERTYKEY
				{
					fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
					pid = 5
				};
				var propvar = new PROPVARIANT
				{
					vt = 31,
					pwszVal = Marshal.StringToCoTaskMemUni(appModelId)
				};
				try
				{
					store.SetValue(ref pkey, ref propvar);
					store.Commit();
				}
				finally
				{
					Marshal.FreeCoTaskMem(propvar.pwszVal);
				}
			}
		}
		catch
		{
		}
	}

	private static void SyncWindowIcon(IntPtr sourceHwnd, IntPtr targetHwnd)
	{
		if (sourceHwnd == IntPtr.Zero || targetHwnd == IntPtr.Zero || !OperatingSystem.IsWindows()) return;
		try
		{
			IntPtr smallIcon = SendMessage(sourceHwnd, WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);
			if (smallIcon != IntPtr.Zero)
			{
				SendMessage(targetHwnd, WM_SETICON, (IntPtr)ICON_SMALL, smallIcon);
			}

			IntPtr bigIcon = SendMessage(sourceHwnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero);
			if (bigIcon != IntPtr.Zero)
			{
				SendMessage(targetHwnd, WM_SETICON, (IntPtr)ICON_BIG, bigIcon);
			}
		}
		catch
		{
		}
	}

	private void AttachWindowGrouping(Process proc)
	{
		if (proc == null || proc.HasExited || !OperatingSystem.IsWindows()) return;

		Task.Run(async () =>
		{
			try
			{
				for (int i = 0; i < 40; i++)
				{
					if (proc.HasExited) break;
					proc.Refresh();
					IntPtr hwnd = proc.MainWindowHandle;
					if (hwnd != IntPtr.Zero)
					{
						SetWindowAppUserModelId(hwnd, "Realm.GameClient");

						long godotRaw = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
						if (godotRaw != 0)
						{
							SyncWindowIcon((IntPtr)godotRaw, hwnd);
						}
						break;
					}

					await Task.Delay(150);
				}
			}
			catch
			{
			}
		});
	}

	private static void EnsureVSCodiumSettings(string serverDataDir, string workspacePath)
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
			GD.PrintErr($"Failed to ensure VSCodium settings: {ex.Message}");
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

	private static void EnsureOhziExtensionPatched(string extensionsDir)
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
						GD.Print($"VS Code: Patched OHZI GLB viewer extension in {dir}");
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"VS Code: Failed to patch OHZI extension: {ex.Message}");
		}
	}

	private static void EnsureOpencuiExtensionAutoStarts(string extensionsDir)
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
				GD.Print($"VS Code: Enabled startup activation for the OpenCode Panel extension in {extensionDirectory}");
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"VS Code: Failed to enable startup activation for the OpenCode Panel extension: {ex.Message}");
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

	private static void RemoveAntigravityExtension(string extensionsDir)
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
					GD.Print($"VS Code: Removed the deprecated antigravity extension at {extensionDirectory}");
				}
				catch (Exception ex)
				{
					GD.PrintErr($"VS Code: Failed to delete {extensionDirectory}: {ex.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"VS Code: Failed to enumerate deprecated antigravity extensions: {ex.Message}");
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
			GD.PrintErr($"VS Code: Failed to update {extensionsJsonPath}: {ex.Message}");
		}
	}

	public void SaveRecentMapDir(string dirPath)
	{
		try
		{
			string recentPathFile = ProjectSettings.GlobalizePath("user://recent_map_dir.txt");
			string directory = Path.GetDirectoryName(recentPathFile);
			if (!Directory.Exists(directory))
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
		if (TryGetTempWorkspacePath(out string tempPath))
			return tempPath;

		if (TryGetRecentMapPath(out string recentPath))
			return recentPath;

		if (TryGetBlankMapPath(out string blankPath))
			return blankPath;

		string fallbackMap = GameContext.Simulation.ActiveMapName ?? "melee";
		return Path.Combine(projectRoot, "Maps", fallbackMap).Replace("\\", "/");
	}

	private bool TryGetTempWorkspacePath(out string path)
	{
		path = string.Empty;
		if (GameContext.Host != null && GameContext.SessionState.IsMapEditorMode)
		{
			string tempWorkspace = ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath);
			if (Directory.Exists(tempWorkspace))
			{
				path = tempWorkspace.Replace("\\", "/");
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
			GD.Print("Installing Realm Map Editor VSCode extension...");
			ExecuteVSCodeCommand(exePath, $"--install-extension speige.realm-map-editor");
		}

		string csharpExtFolder = Path.Combine(embedDir, "data", "extensions", "ms-dotnettools.csharp");
		if (!Directory.Exists(csharpExtFolder))
		{
			GD.Print("Installing C# VSCode extension...");
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
			var process = Process.Start(startInfo);
			process.WaitForExit();

			if (process.ExitCode != 0)
			{
				string error = process.StandardError.ReadToEnd();
				GD.PrintErr($"VSCode command failed ({process.ExitCode}): {error}");
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"Failed to execute VSCode command '{arguments}': {e.Message}");
		}
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

	private static void PatchVSCodiumConfiguration(string embedDir)
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
			GD.PrintErr($"VS Code: Failed to patch VSCodium configuration: {ex.Message}");
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
				GD.Print($"Successfully patched VSCode product.json at {productJsonPath}");
			}
		}
		catch (UnauthorizedAccessException)
		{
			GD.PrintErr("Cannot patch VSCode product.json. Please run Realm as Administrator once to enable Map Editor features.");
		}
		catch (Exception e)
		{
			GD.PrintErr($"Failed to patch VSCode product.json: {e.Message}");
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

	private static void CopyItemIfExists(string src, string dst)
	{
		if (File.Exists(src))
			File.Copy(src, dst, true);
	}

	private static void CopyDirectoryIfExists(string src, string dst)
	{
		if (Directory.Exists(src))
		{
			Directory.CreateDirectory(dst);
			foreach (string filePath in Directory.GetFiles(src))
			{
				string fileName = Path.GetFileName(filePath);
				File.Copy(filePath, Path.Combine(dst, fileName), true);
			}
		}
	}

	private bool IsExtensionInstalled(string extensionsDir, string extensionId)
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
}
