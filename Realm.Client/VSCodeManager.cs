using Godot;
using Microsoft.Web.WebView2.Core;
using Realm.EditorAPI;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.MapAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;

namespace Realm.Client;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public partial class VSCodeManager
{
	private static VSCodeManager _instance;
	public static VSCodeManager Instance => _instance ??= new VSCodeManager();

	private VSCodeManager()
	{
		if (OperatingSystem.IsWindows())
		{
			AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanUp();
		}
	}

	private bool _isInstalling = false;
	private bool _installCompleted = false;
	private System.Threading.Tasks.Task _installTask;
	private readonly object _installLock = new object();
	private int _vscodePort = 8089;
	private static readonly System.Net.Http.HttpClient _localProxyHttpClient = new System.Net.Http.HttpClient(new System.Net.Http.SocketsHttpHandler
	{
		AllowAutoRedirect = false,
		UseCookies = false,
		AutomaticDecompression = System.Net.DecompressionMethods.All
	});

	private static readonly string[] RequiredExtensions = new[]
	{
		"google.google-antigravity",
		"muhammad-sammy.csharp",
		"OHZIInteractiveStudio.ohzi-vscode-glb-viewer",
		"Gruntfuggly.todo-tree",
		// "mechatroner.rainbow-json",
		"patcx.vscode-nuget-gallery",
		"AykutSarac.jsoncrack-vscode"
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

		return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "Godot", "app_userdata", "Realm", "vscode");
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
				var jsonNode = JsonNode.Parse(File.ReadAllText(versionJson, System.Text.Encoding.UTF8));
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

		var jsonNode = JsonNode.Parse(File.ReadAllText(pkgPath, System.Text.Encoding.UTF8));
		version = jsonNode?["version"]?.GetValue<string>();
		return !string.IsNullOrWhiteSpace(version);
	}

	public bool IsInstalled()
	{

		string embedDir = GetVSCodeDirectory();
		string exePath = Path.Combine(embedDir, "editor", "bin", "codium.exe");
		string completedMarkerPath = Path.Combine(embedDir, "install_completed.marker");
		string bypassMarkerPath = Path.Combine(embedDir, "bypass_completed.marker");
		string wasiPath = WasiSdkResolver.ResolveWasiSdkPath();
		string wasiClangPath = !string.IsNullOrEmpty(wasiPath) ? Path.Combine(wasiPath, "bin", "clang.exe") : string.Empty;
		string extVersion = GetRealmMapEditorVersion();
		string extDir = Path.Combine(embedDir, "user-data-dir", "extensions", $"speige.realm-map-editor-{extVersion}");
		string extensionsDir = Path.Combine(embedDir, "user-data-dir", "extensions");

		if (!AreBasePathsValid(exePath, completedMarkerPath, bypassMarkerPath, wasiClangPath, extDir))
			return false;

		return AreExtensionsInstalled(extensionsDir);
	}

	private bool AreBasePathsValid(string exePath, string completedMarkerPath, string bypassMarkerPath, string wasiClangPath, string extDir)
	{
		if (!File.Exists(exePath) || new FileInfo(exePath).Length == 0)
			return false;
		if (!File.Exists(completedMarkerPath) && !File.Exists(bypassMarkerPath))
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
				System.Threading.Tasks.Task.Run(() => InstallMissingExtensions(exePath, embedDir));
				return;
			}

			_isInstalling = true;
			_installCompleted = false;
			_installTask = System.Threading.Tasks.Task.Run(() => RunInstallScript(force, exePath, embedDir));
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

				installProcess.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) GD.Print("[installer] " + e.Data); };
				installProcess.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) GD.PrintErr("[installer error] " + e.Data); };

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
				RunBypassAndVerify(exePath, embedDir);
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
	private Thread _staThread;
	private IntPtr _parentHwnd;
	private IntPtr _childHwnd;
	private Control _containerControl;
	private CoreWebView2Controller _controller;
	private bool _isInitialized;
	private bool _isVisible;
	public bool IsVisible => _isVisible;

	private readonly ConcurrentQueue<Action> _actionQueue = new ConcurrentQueue<Action>();

	private const uint WM_USER = 0x0400;
	private const uint WM_WAKEUP = WM_USER + 1;
	private const uint WM_CLOSE = 0x0010;
	private const int SW_HIDE = 0;
	private const int SW_SHOW = 5;
	private const int SW_SHOWMAXIMIZED = 3;

	private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
	private const uint WS_VISIBLE = 0x10000000;

	private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
	private WndProcDelegate _customWndProcDelegate;

	[StructLayout(LayoutKind.Sequential)]
	public struct MSG
	{
		public IntPtr hwnd;
		public uint message;
		public IntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public Point pt;
		public uint lPrivate;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct Point
	{
		public int x;
		public int y;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct WNDCLASSEX
	{
		public int cbSize;
		public int style;
		public IntPtr lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public IntPtr hInstance;
		public IntPtr hIcon;
		public IntPtr hCursor;
		public IntPtr hbrBackground;
		public string lpszMenuName;
		public string lpszClassName;
		public IntPtr hIconSm;
	}

	[DllImport("user32.dll")]
	public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll")]
	public static extern bool TranslateMessage(ref MSG lpMsg);

	[DllImport("user32.dll")]
	public static extern IntPtr DispatchMessage(ref MSG lpMsg);

	[DllImport("user32.dll")]
	public static extern short GetKeyState(int nVirtKey);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
	public static extern IntPtr CreateWindowEx(
		uint dwExStyle,
		string lpClassName,
		string lpWindowName,
		uint dwStyle,
		int x,
		int y,
		int nWidth,
		int nHeight,
		IntPtr hWndParent,
		IntPtr hMenu,
		IntPtr hInstance,
		IntPtr lpParam);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool DestroyWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

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
						ShowWindow(hWnd, 9); // SW_RESTORE
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

	[DllImport("user32.dll")]
	public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegisterClassExW")]
	public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
	public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
	public static extern IntPtr GetModuleHandle(string lpModuleName);

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
					Marshal.WriteInt32(extendedInfoPtr, 4, (int)JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE); // BasicLimitInformation.LimitFlags at offset 4 (after 4-byte PerProcessUserTimeLimit/PerJobUserTimeLimit or 8-byte alignment)
					// Structure layout:
					// JOBOBJECT_BASIC_LIMIT_INFORMATION (48 bytes):
					//   LONGLONG PerProcessUserTimeLimit (8)
					//   LONGLONG PerJobUserTimeLimit (8)
					//   DWORD LimitFlags (4) -> offset 16
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

	[StructLayout(LayoutKind.Sequential)]
	private struct JOBOBJECT_BASIC_LIMIT_INFORMATION_STRUCT
	{
		public long PerProcessUserTimeLimit;
		public long PerJobUserTimeLimit;
		public uint LimitFlags;
		public UIntPtr MinimumWorkingSetSize;
		public UIntPtr MaximumWorkingSetSize;
		public uint ActiveProcessLimit;
		public UIntPtr Affinity;
		public uint PriorityClass;
		public uint SchedulingClass;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct IO_COUNTERS_STRUCT
	{
		public ulong ReadOperationCount;
		public ulong WriteOperationCount;
		public ulong OtherOperationCount;
		public ulong ReadTransferCount;
		public ulong WriteTransferCount;
		public ulong OtherTransferCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION_STRUCT
	{
		public JOBOBJECT_BASIC_LIMIT_INFORMATION_STRUCT BasicLimitInformation;
		public IO_COUNTERS_STRUCT IoInfo;
		public UIntPtr ProcessMemoryLimit;
		public UIntPtr JobMemoryLimit;
		public UIntPtr PeakProcessMemoryUsed;
		public UIntPtr PeakJobMemoryUsed;
	}

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
			File.WriteAllText(Path.Combine(dir, "realm-locale-dict.json"), System.Text.Json.JsonSerializer.Serialize(activeDict));

			var enDict = LocalizationManager.GetDictionary("en");
			File.WriteAllText(Path.Combine(dir, "realm-locale-en.json"), System.Text.Json.JsonSerializer.Serialize(enDict));
		}
		catch { }
	}

	private void OnLanguageChanged(GameLanguage newLanguage)
	{
		SaveCurrentLocaleToFile();
		string code = newLanguage.ToLocaleCode();
		var dict = LocalizationManager.GetDictionary(code);
		string dictJson = System.Text.Json.JsonSerializer.Serialize(dict);
		_actionQueue.Enqueue(() =>
		{
			try
			{
				if (_controller != null && _controller.CoreWebView2 != null)
				{
					string js = $"window.postMessage({{ type: 'updateLocale', realmLocale: '{code}', dictionary: {dictJson} }}, '*');";
					_controller.CoreWebView2.ExecuteScriptAsync(js);
				}
			}
			catch { }
		});
		if (_childHwnd != IntPtr.Zero)
		{
			PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
		}
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
		int windowId = containerControl.GetWindow().GetWindowId();
		long nativeHandle = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, windowId);
		_parentHwnd = new IntPtr(nativeHandle);

		StartVSCodeServer();

		_staThread = new Thread(STAThreadLoop);
		_staThread.SetApartmentState(ApartmentState.STA);
		_staThread.Start();

		_isInitialized = true;
	}

	private void StartVSCodeServer()
	{
		try
		{
			string projectRoot = PathUtils.GetProjectRoot();
			string embedDir = GetVSCodeDirectory();
			string exePath = Path.Combine(embedDir, "editor", "bin", "codium.exe");

			if (!File.Exists(exePath))
			{
				GD.PrintErr("VS Code executable not found at: " + exePath);
				return;
			}

			EnsureWorkspaceMapApiDll(projectRoot);
			PatchVSCodiumConfiguration(embedDir);

			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string extensionsDir = Path.Combine(serverDataDir, "extensions");
			string cliDataDir = Path.Combine(embedDir, "cli-data-dir");
			Directory.CreateDirectory(cliDataDir);

			if (IsInstalling)
			{
				_installTask?.Wait();
			}

			var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
			l.Start();
			_vscodePort = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
			l.Stop();

			_vscodeProcess = new Process();
			_vscodeProcess.StartInfo.FileName = Path.ChangeExtension(exePath, ".cmd");
			_vscodeProcess.StartInfo.Arguments = $"--cli-data-dir \"{cliDataDir}\" --extensions-dir \"{extensionsDir}\" serve-web --port {_vscodePort} --server-data-dir \"{serverDataDir}\" --accept-server-license-terms --without-connection-token";
			_vscodeProcess.StartInfo.CreateNoWindow = true;
			_vscodeProcess.StartInfo.UseShellExecute = false;
			_vscodeProcess.StartInfo.EnvironmentVariables["VSCODE_CLI_DATA_DIR"] = cliDataDir;
			_vscodeProcess.StartInfo.EnvironmentVariables["VSCODE_EXTENSIONS"] = extensionsDir;
			_vscodeProcess.StartInfo.EnvironmentVariables["VSCODE_EXTENSIONS_DIR"] = extensionsDir;
			_vscodeProcess.Start();
			AddProcessToJob(_vscodeProcess);
			GD.Print("VS Code server started successfully.");

			StartIpcHttpListener();
		}
		catch (Exception ex)
		{
			GD.PrintErr("Failed to start VS Code server: " + ex.Message);
		}
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
					Realm.Client.Core.GameHost.EnsureMapProjectFiles(mapFolderRaw);
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
			var listener = new System.Net.HttpListener();
			try
			{
				listener.Prefixes.Add($"http://127.0.0.1:{_ipcHttpPort}/api/");
				listener.Start();
			}
			catch
			{
				_ipcHttpPort = 8093;
				listener = new System.Net.HttpListener();
				listener.Prefixes.Add($"http://127.0.0.1:{_ipcHttpPort}/api/");
				listener.Start();
			}
			GD.Print($"[VSCodeManager] HTTP IPC bridge listening on http://127.0.0.1:{_ipcHttpPort}/api/");

			while (true)
			{
				var ctx = await listener.GetContextAsync();
				_ = System.Threading.Tasks.Task.Run(() =>
				{
					HandleIpcHttpRequest(ctx);
				});
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[VSCodeManager] HTTP IPC bridge error: {ex.Message}");
		}
	}

	private readonly ConcurrentQueue<string> _pendingExtensionCommands = new ConcurrentQueue<string>();

	public async System.Threading.Tasks.Task SaveAllOpenFilesAsync()
	{
		_pendingExtensionCommands.Enqueue("saveAll");
		_actionQueue.Enqueue(() =>
		{
			try
			{
				if (_controller != null && _controller.CoreWebView2 != null)
				{
					_controller.CoreWebView2.ExecuteScriptAsync(@"
(function() {
	try {
		window.dispatchEvent(new KeyboardEvent('keydown', { key: 's', code: 'KeyS', keyCode: 83, which: 83, ctrlKey: true, altKey: true, bubbles: true }));
		window.dispatchEvent(new KeyboardEvent('keydown', { key: 's', code: 'KeyS', keyCode: 83, which: 83, ctrlKey: true, bubbles: true }));
	} catch(e) {}
})();
");
				}
			}
			catch { }
		});
		await System.Threading.Tasks.Task.Delay(500);
	}

	private async void HandleIpcHttpRequest(System.Net.HttpListenerContext ctx)
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
			try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
		}
	}

	private void SetCorsHeaders(System.Net.HttpListenerContext ctx)
	{
		ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
		ctx.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
		ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
	}

	private async System.Threading.Tasks.Task ProcessIpcPostRequest(System.Net.HttpListenerContext ctx)
	{
		using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
		string body = await reader.ReadToEndAsync();
		var node = System.Text.Json.Nodes.JsonNode.Parse(body);
		string action = GetActionFromNode(node);
		
		CheckFocusGodotWindow(action, node);

		var responseObj = new System.Text.Json.Nodes.JsonObject();
		await RouteIpcAction(action, node, responseObj);

		await SendIpcResponse(ctx, responseObj);
	}

	private string GetActionFromNode(System.Text.Json.Nodes.JsonNode node)
	{
		return node?["action"]?.ToString() ?? node?["type"]?.ToString();
	}

	private void CheckFocusGodotWindow(string action, System.Text.Json.Nodes.JsonNode node)
	{
		if (ShouldFocusGodot(action, node))
		{
			RestoreAndFocusGodotWindow();
		}
	}

	private bool ShouldFocusGodot(string action, System.Text.Json.Nodes.JsonNode node)
	{
		return action == "openVfxDialog" || 
			   action == "openModelPicker" || 
			   action == "openAbilityVfxDialog" || 
			   action == "openAnimationStudio" || 
			   action == "openAnimationPreview" || 
			   action == "openEditAnimations" || 
			   (node?["focusGodot"]?.GetValue<bool>() ?? false);
	}

	private async System.Threading.Tasks.Task RouteIpcAction(string action, System.Text.Json.Nodes.JsonNode node, System.Text.Json.Nodes.JsonObject responseObj)
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

	private bool TryRouteDialogAction(string action, System.Text.Json.Nodes.JsonNode node, System.Text.Json.Nodes.JsonObject responseObj)
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

	private bool TryRouteFileAction(string action, System.Text.Json.Nodes.JsonNode node, System.Text.Json.Nodes.JsonObject responseObj)
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

	private bool TryRouteConversionAction(string action, System.Text.Json.Nodes.JsonNode node, System.Text.Json.Nodes.JsonObject responseObj)
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

	private async System.Threading.Tasks.Task SendIpcResponse(System.Net.HttpListenerContext ctx, System.Text.Json.Nodes.JsonObject responseObj)
	{
		string resJson = responseObj.ToJsonString();
		byte[] resBytes = System.Text.Encoding.UTF8.GetBytes(resJson);
		ctx.Response.ContentType = "application/json";
		ctx.Response.ContentLength64 = resBytes.Length;
		await ctx.Response.OutputStream.WriteAsync(resBytes, 0, resBytes.Length);
		ctx.Response.Close();
	}



	private void HandleIpcOptionsRequest(System.Net.HttpListenerContext ctx)
	{
		ctx.Response.StatusCode = 200;
		ctx.Response.Close();
	}

	private async System.Threading.Tasks.Task HandleIpcGetRequest(System.Net.HttpListenerContext ctx)
	{
		if (ctx.Request.Url != null && ctx.Request.Url.AbsolutePath.EndsWith("/locale"))
		{
			string locCode = GameSettings.Language.ToLocaleCode();
			var dict = LocalizationManager.GetDictionary(locCode);
			var locObj = new System.Text.Json.Nodes.JsonObject();
			locObj["locale"] = locCode;
			locObj["dictionary"] = System.Text.Json.JsonSerializer.SerializeToNode(dict);
			string locJson = locObj.ToJsonString();
			byte[] locBytes = System.Text.Encoding.UTF8.GetBytes(locJson);
			ctx.Response.ContentType = "application/json";
			ctx.Response.ContentLength64 = locBytes.Length;
			await ctx.Response.OutputStream.WriteAsync(locBytes, 0, locBytes.Length);
			ctx.Response.Close();
			return;
		}

		var pollResponseObj = new System.Text.Json.Nodes.JsonObject();
		var commandsArray = new System.Text.Json.Nodes.JsonArray();
		while (_pendingExtensionCommands.TryDequeue(out var cmd))
		{
			commandsArray.Add(cmd);
		}
		pollResponseObj["commands"] = commandsArray;
		string pollResJson = pollResponseObj.ToJsonString();
		byte[] pollResBytes = System.Text.Encoding.UTF8.GetBytes(pollResJson);
		ctx.Response.ContentType = "application/json";
		ctx.Response.ContentLength64 = pollResBytes.Length;
		await ctx.Response.OutputStream.WriteAsync(pollResBytes, 0, pollResBytes.Length);
		ctx.Response.Close();
	}

	private async System.Threading.Tasks.Task HandleEditorApiRequest(JsonNode node, string action, JsonObject responseObj)
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

	private async System.Threading.Tasks.Task<(bool success, object? result, string error)> ExecuteEditorApiMethod(string apiMethod, JsonNode node)
	{
		try
		{
			var editorApi = ServiceLocator.Get<IEditorAPI>();
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

	private async System.Threading.Tasks.Task<(bool success, object? result, string error)> InvokeMethodAsync(IEditorAPI editorApi, System.Reflection.MethodInfo methodInfo, object?[] args)
	{
		var tcs = new System.Threading.Tasks.TaskCompletionSource<object?>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
		Callable.From(() =>
		{
			try
			{
				var result = methodInfo.Invoke(editorApi, args);
				tcs.TrySetResult(result);
			}
			catch (System.Reflection.TargetInvocationException tie)
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
			responseObj["result"] = System.Text.Json.JsonSerializer.SerializeToNode(invocationResult);
			if (invocationResult is string strVal)
				responseObj["instanceId"] = strVal;
			else if (invocationResult is bool bVal)
				responseObj["deleted"] = bVal;
		}
		if (!string.IsNullOrEmpty(apiError)) 
			responseObj["error"] = apiError;
	}



	private object?[] ParseApiArguments(System.Reflection.MethodInfo methodInfo, JsonNode node)
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

	private object? ParseSingleArgument(System.Reflection.ParameterInfo param, JsonObject argsNode)
	{
		Type paramType = param.ParameterType;
		JsonNode? paramNode = GetParamNode(param.Name, argsNode);

		if (paramNode == null)
		{
			return GetDefaultArgument(paramType, param, argsNode);
		}

		if (paramType == typeof(System.Numerics.Vector3))
		{
			return ParseVector3Node(paramNode);
		}

		return System.Text.Json.JsonSerializer.Deserialize(paramNode.ToJsonString(), paramType, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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

	private object? GetDefaultArgument(Type paramType, System.Reflection.ParameterInfo param, JsonObject argsNode)
	{
		if (paramType == typeof(System.Numerics.Vector3))
		{
			return ExtractVector3FromArgsNode(argsNode);
		}

		if (param.HasDefaultValue)
			return param.DefaultValue;
		if (paramType.IsValueType)
			return Activator.CreateInstance(paramType);
			
		return null;
	}

	private System.Numerics.Vector3 ExtractVector3FromArgsNode(JsonObject argsNode)
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
		return new System.Numerics.Vector3(vx, vy, vz);
	}

	private System.Numerics.Vector3 ParseVector3Node(JsonNode paramNode)
	{
		if (paramNode is JsonObject vObj)
		{
			return ExtractVector3FromArgsNode(vObj);
		}
		
		if (paramNode is JsonArray vArr && vArr.Count >= 3)
		{
			return new System.Numerics.Vector3(
				vArr[0]?.GetValue<float>() ?? 0f,
				vArr[1]?.GetValue<float>() ?? 0f,
				vArr[2]?.GetValue<float>() ?? 0f
			);
		}
		
		return new System.Numerics.Vector3(0f, 0f, 0f);
	}



	private void HandleOpenVfxDialog(JsonNode node, JsonObject responseObj)
	{
		string weaponId = node["weaponId"]?.ToString() ?? "";
		var weaponDataNode = node["weaponData"];

		Callable.From(() =>
		{
			WeaponMetadata meta = null;
			if (!string.IsNullOrEmpty(weaponId) && Realm.Client.Core.GameHost.WeaponRegistry.TryGetValue(weaponId, out var existing))
			{
				meta = existing;
			}
			else if (weaponDataNode != null)
			{
				try
				{
					meta = System.Text.Json.JsonSerializer.Deserialize<WeaponMetadata>(
						weaponDataNode.ToJsonString(),
						new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
					);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[VSCodeManager] openVfxDialog deserialize error: {ex.Message}");
				}
			}

			if (UI.MapEditorHUD.Instance != null)
			{
				UI.MapEditorHUD.Instance.OpenWeaponVfxDialog(weaponId, meta, (updatedMeta) =>
				{
					UI.MapEditorHUD.Instance.SaveCustomWeaponToMetadata(weaponId, updatedMeta);
				});
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
			if (UI.MapEditorHUD.Instance == null) return;
			
			UI.MapEditorHUD.Instance.OpenModelPickerDialog(entityId, fieldName, domain, currentPath, (updatedPath) =>
			{
				UI.MapEditorHUD.Instance.SaveEntityModelPathToMetadata(entityId, fieldName, domain, updatedPath);
			});
		}).CallDeferred();

		responseObj["action"] = "openModelPickerResult";
		responseObj["success"] = true;
	}



	private void HandleOpenAbilityVfxDialog(JsonNode node, JsonObject responseObj)
	{
		string abilityId = GetStringProperty(node, "abilityId", "");
		var abilityDataNode = node["abilityData"] as System.Text.Json.Nodes.JsonObject;

		Callable.From(() =>
		{
			if (UI.MapEditorHUD.Instance == null) return;

			UI.MapEditorHUD.Instance.OpenAbilityVfxDialog(abilityId, abilityDataNode, (updatedData) =>
			{
				if (updatedData == null) return;

				string vfx = GetStringProperty(updatedData, "VisualEffect", "");
				string sound = GetStringProperty(updatedData, "CastSound", "");
				string icon = GetStringProperty(updatedData, "IconPath", "");
				float aoe = GetFloatProperty(updatedData, "AreaOfEffectRadius", 0f);
				
				UI.MapEditorHUD.Instance.SaveCustomAbilityVfxToMetadata(abilityId, vfx, sound, icon, aoe);
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
			if (UI.MapEditorHUD.Instance != null)
			{
				UI.MapEditorHUD.Instance.OpenAnimationPreviewDialog(unitId, modelPath);
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
			string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
			filePath = System.IO.Path.Combine(wsPath, action == "saveTerrain" ? "terrain.json" : "metadata.json");
		}
		else if (!System.IO.Path.IsPathRooted(filePath))
		{
			string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
			filePath = System.IO.Path.Combine(wsPath, filePath);
		}
		return filePath;
	}



	private (bool success, string formattedContent, string errorMsg) TryFormatAndSaveJsonFile(string filePath, string content)
	{
		try
		{
			string fileName = System.IO.Path.GetFileName(filePath).ToLowerInvariant();
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
				var metadata = Realm.Shared.Services.MapFileService.LoadMetadataFromJson(content);
				Realm.Shared.Services.MapFileService.SaveMetadata(filePath, metadata);
				formattedContent = Realm.Shared.Services.MapFileService.SaveMetadataToJson(metadata);
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
				var manifest = Realm.Shared.Services.MapFileService.LoadManifestFromJson(content);
				Realm.Shared.Services.MapFileService.SaveManifest(filePath, manifest);
				formattedContent = Realm.Shared.Services.MapFileService.SaveManifestToJson(manifest);
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
				var terrain = Realm.Shared.Services.MapFileService.LoadTerrainFromJson(content);
				Realm.Shared.Services.MapFileService.SaveTerrain(filePath, terrain);
				formattedContent = Realm.Shared.Services.MapFileService.SaveTerrainToJson(terrain);
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
			string fileName = System.IO.Path.GetFileName(filePath).ToLowerInvariant();
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
		string display = fileName == "manifest.json" ? "manifest.json" : (Services.MapWorkspaceService.GetActiveWorkspacePath() != null ? "Workspace Metadata" : "metadata.json");
		Action reloadAction = () =>
		{
			if (UI.MapEditorHUD.Instance != null)
			{
				UI.MapEditorHUD.Instance.ReadMetadataAndRefreshTextures();
				UI.MapEditorHUD.Instance.ShowFeedback(string.Format(TranslationServer.Translate("{0} updated externally — reloaded."), display));
			}
			else if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.GroundTerrain != null)
			{
				Realm.Client.Core.GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
			}
		};

		if (UI.MapEditor.FloatingDialogBase.HasAnyDialogOpen && UI.MapEditorHUD.Instance != null)
		{
			UI.MapEditorHUD.Instance.ShowConfirmationDialog(
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
			if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.IsMapEditorMode)
			{
				Realm.Client.Core.GameHost.Instance.LoadMapFromFile(filePath);
				UI.MapEditorHUD.Instance?.ShowFeedback(TranslationServer.Translate("terrain.json updated externally — reloaded."));
			}
		};

		if (UI.MapEditor.FloatingDialogBase.HasAnyDialogOpen && UI.MapEditorHUD.Instance != null)
		{
			UI.MapEditorHUD.Instance.ShowConfirmationDialog(
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
				if (UI.MapEditorHUD.Instance != null)
				{
					UI.MapEditorHUD.Instance.ReadMetadataAndRefreshTextures();
					UI.MapEditorHUD.Instance.ShowFeedback(TranslationServer.Translate("metadata.json updated externally — reloaded."));
				}
				else if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.GroundTerrain != null)
				{
					Realm.Client.Core.GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
				}
			};

			if (UI.MapEditor.FloatingDialogBase.HasAnyDialogOpen && UI.MapEditorHUD.Instance != null)
			{
				UI.MapEditorHUD.Instance.ShowConfirmationDialog(
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
			var res = Realm.Shared.Textures.TextureConverter.ExtractPngFromRtex(inputPath, outputPath, layer);
			PopulateRtexConversionResponse(responseObj, res, inputPath);
		}
		catch (Exception ex)
		{
			PopulateErrorResponse(responseObj, "convertRtexResult", ex.Message);
		}
	}

	private void PopulateRtexConversionResponse(JsonObject responseObj, Realm.Shared.Textures.TextureConversionResult res, string inputPath)
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
				string? metaJson = Realm.Shared.Textures.RtexFile.ExtractMetadata(rtexBytes);
				if (!string.IsNullOrEmpty(metaJson))
				{
					responseObj["metadata"] = JsonNode.Parse(metaJson);
				}
			}
		}
		catch { }
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
			var options = new Realm.Shared.Animation.RanimRenderOptions
			{
				Format = Realm.Shared.Animation.RanimOutputFormat.Webp,
				Width = 128,
				Height = 128,
				Fps = 12.0f
			};
			var res = Realm.Shared.Animation.RanimRenderer.ExportFile(inputPath, outputPath, options);
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
			var (metaJson, glbBytes, _) = Realm.Shared.ModelOptimization.RmeshFile.Parse(rmeshBytes);
			
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



	private string GetStringProperty(System.Text.Json.Nodes.JsonNode node, string key, string defaultValue = "")
	{
		if (node == null) return defaultValue;
		var val = node[key];
		if (val == null) return defaultValue;
		return val.ToString() ?? defaultValue;
	}

	private int GetIntProperty(System.Text.Json.Nodes.JsonNode node, string key, int defaultValue = 0)
	{
		if (node == null) return defaultValue;
		var val = node[key];
		if (val == null) return defaultValue;
		return (int)val;
	}

	private float GetFloatProperty(System.Text.Json.Nodes.JsonNode node, string key, float defaultValue = 0f)
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
			var (metaJson, tracks, _) = Realm.Shared.Audio.RaudFile.Parse(raudBytes);
			
			if (tracks.Count <= trackIndex)
			{
				PopulateErrorResponse(responseObj, "convertRaudResult", $"Audio track {trackIndex} not found in RAUD file.");
				return;
			}
			if (tracks[trackIndex].Length == 0)
			{
				PopulateErrorResponse(responseObj, "convertRaudResult", $"Audio track {trackIndex} not found in RAUD file.");
				return;
			}

			SaveAudioTrack(outputPath, tracks[trackIndex]);
			PopulateRaudConversionResponse(responseObj, outputPath, tracks.Count, metaJson);
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



	private void STAThreadLoop()
	{
		var syncContext = new SingleThreadSynchronizationContext();
		SynchronizationContext.SetSynchronizationContext(syncContext);

		var wndClass = new WNDCLASSEX();
		wndClass.cbSize = Marshal.SizeOf(typeof(WNDCLASSEX));
		wndClass.style = 0;
		_customWndProcDelegate = CustomWndProc;
		wndClass.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_customWndProcDelegate);
		wndClass.cbClsExtra = 0;
		wndClass.cbWndExtra = 0;
		wndClass.hInstance = GetModuleHandle(null);
		wndClass.hIcon = IntPtr.Zero;
		wndClass.hCursor = IntPtr.Zero;
		wndClass.hbrBackground = IntPtr.Zero;
		wndClass.lpszMenuName = null;
		wndClass.lpszClassName = "VSCodeEmbedWindow";
		wndClass.hIconSm = IntPtr.Zero;

		RegisterClassEx(ref wndClass);

		_childHwnd = CreateWindowEx(
			0,
			"VSCodeEmbedWindow",
			"Realm Editor",
			WS_OVERLAPPEDWINDOW | WS_VISIBLE,
			0,
			0,
			800,
			600,
			IntPtr.Zero,
			IntPtr.Zero,
			GetModuleHandle(null),
			IntPtr.Zero);

		if (_childHwnd == IntPtr.Zero)
		{
			GD.PrintErr("Failed to create child window.");
			return;
		}

		syncContext.SetTargetHwnd(_childHwnd);

		InitializeWebView();

		MSG msg;
		while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
		{
			TranslateMessage(ref msg);
			DispatchMessage(ref msg);

			syncContext.RunPending();

			while (_actionQueue.TryDequeue(out var action))
			{
				try
				{
					action();
				}
				catch (Exception ex)
				{
					GD.PrintErr("Error executing action on STA thread: " + ex.Message);
				}
			}
		}
	}

	private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		if (msg == 0x0005) // WM_SIZE
		{
			int w = (int)(lParam.ToInt64() & 0xFFFF);
			int h = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
			if (_controller != null)
			{
				_controller.Bounds = new System.Drawing.Rectangle(0, 0, w, h);
			}
		}
		else if (msg == 0x0010) // WM_CLOSE
		{
			if (!_isVisible) return IntPtr.Zero; // already hidden
			_actionQueue.Enqueue(() =>
			{
				if (_controller != null && _controller.CoreWebView2 != null)
				{
					_controller.CoreWebView2.Navigate("about:blank");
					_controller.IsVisible = false;
				}
				ShowWindow(hWnd, SW_HIDE);
				_isVisible = false;
			});
			PostMessage(hWnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
			return IntPtr.Zero;
		}
		return DefWindowProc(hWnd, msg, wParam, lParam);
	}

	private async void InitializeWebView()
	{
		try
		{
			string projectRoot = PathUtils.GetProjectRoot();
			string embedDir = GetVSCodeDirectory();
			PatchVSCodiumConfiguration(embedDir);
			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string cachePath = Path.Combine(serverDataDir, "webview-cache");
			
			ClearWebviewCache(cachePath);

			var envOptions = new CoreWebView2EnvironmentOptions
			{
				AdditionalBrowserArguments = "--disable-web-security --allow-running-insecure-content"
			};
			var env = await CoreWebView2Environment.CreateAsync(userDataFolder: cachePath, options: envOptions);
			_controller = await env.CreateCoreWebView2ControllerAsync(_childHwnd);
			_controller.Bounds = new System.Drawing.Rectangle(0, 0, 800, 600);
			if (_controller.CoreWebView2?.Settings != null)
			{
				_controller.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
				_controller.CoreWebView2.Settings.AreDevToolsEnabled = true;
			}

			if (_controller.CoreWebView2 != null)
			{
				SetupWebviewFilters();
				SetupWebviewMessageHandling();
			}

			await ClearWebviewCacheAndStorage();
			NavigateToMapFolder(projectRoot);
			
			_controller.IsVisible = _isVisible;
			ShowWindow(_childHwnd, _isVisible ? SW_SHOW : SW_HIDE);
		}
		catch (Exception ex)
		{
			GD.PrintErr("Failed to initialize WebView2: " + ex.Message);
		}
	}

	

	

	



	private async System.Threading.Tasks.Task ClearWebviewCacheAndStorage()
	{
		try
		{
			if (_controller?.CoreWebView2?.Profile != null)
			{
				await _controller.CoreWebView2.Profile.ClearBrowsingDataAsync(
					CoreWebView2BrowsingDataKinds.ServiceWorkers |
					CoreWebView2BrowsingDataKinds.CacheStorage |
					CoreWebView2BrowsingDataKinds.DiskCache
				);
			}
		}
		catch { }
	}

	private void NavigateToMapFolder(string projectRoot)
	{
		string mapFolderRaw = GetMapFolderToOpen(projectRoot);
		string mapFolder = FormatWinPathForUrl(mapFolderRaw);
		string targetUrl = $"http://127.0.0.1:{_vscodePort}/?folder={Uri.EscapeDataString(mapFolder)}&ipcPort={_ipcHttpPort}";

		_controller.CoreWebView2.NavigationCompleted += HandleNavigationCompleted;
		_controller.CoreWebView2.Navigate(targetUrl);
	}

	private async void HandleNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs args)
	{
		if (!args.IsSuccess || sender != _controller.CoreWebView2) return;
		
		try
		{
			await _controller.CoreWebView2.ExecuteScriptAsync(@"
(async () => {
    const DB = 'vscode-web-db';
    const STORE = 'vscode-userdata-store';
    const KEY = '/User/settings.json';
    try {
        const db = await new Promise((resolve, reject) => {
            const r = indexedDB.open(DB);
            r.onupgradeneeded = (e) => {
                if (!e.target.result.objectStoreNames.contains(STORE))
                    e.target.result.createObjectStore(STORE);
            };
            r.onsuccess = (e) => resolve(e.target.result);
            r.onerror = (e) => reject(e.target.error);
        });
        let config = {};
        const raw = await new Promise((resolve, reject) => {
            const t = db.transaction([STORE], 'readwrite');
            const g = t.objectStore(STORE).get(KEY);
            g.onsuccess = () => resolve(g.result);
            g.onerror = () => reject(g.error);
        });
        if (raw) {
            const buf = raw instanceof Uint8Array ? raw : raw.value;
            if (buf instanceof Uint8Array) {
                const s = new TextDecoder('utf-8').decode(buf).trim();
                if (s) config = JSON.parse(s);
            }
        }
        if (config['security.workspace.trust.enabled'] === false &&
            config['security.workspace.trust.startupPrompt'] === 'never') {
            return;
        }
        config['security.workspace.trust.enabled'] = false;
        config['security.workspace.trust.startupPrompt'] = 'never';
        const encoded = new TextEncoder().encode(JSON.stringify(config, null, '\t'));
        await new Promise((resolve, reject) => {
            const t = db.transaction([STORE], 'readwrite');
            const p = t.objectStore(STORE).put(encoded, KEY);
            p.onsuccess = () => resolve();
            p.onerror = () => reject(p.error);
        });
        window.location.replace(window.location.href);
    } catch (e) {
        console.error('Failed to set workspace trust:', e);
    }
})();
");
		}
		catch { }
	}



	private void ClearWebviewCache(string cachePath)
	{
		try
		{
			string swDir = Path.Combine(cachePath, "Default", "Service Worker");
			if (Directory.Exists(swDir)) Directory.Delete(swDir, true);
			string codeCache = Path.Combine(cachePath, "Default", "Code Cache");
			if (Directory.Exists(codeCache)) Directory.Delete(codeCache, true);
			string cacheDir = Path.Combine(cachePath, "Default", "Cache");
			if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
		}
		catch { }
	}

	private void SetupWebviewFilters()
	{
		_controller.CoreWebView2.AddWebResourceRequestedFilter("http://127.0.0.1:*", CoreWebView2WebResourceContext.All);
		_controller.CoreWebView2.AddWebResourceRequestedFilter("http://localhost:*", CoreWebView2WebResourceContext.All);
		_controller.CoreWebView2.AddWebResourceRequestedFilter("http://[::1]:*", CoreWebView2WebResourceContext.All);
		_controller.CoreWebView2.AddWebResourceRequestedFilter("https://127.0.0.1:*", CoreWebView2WebResourceContext.All);
		_controller.CoreWebView2.AddWebResourceRequestedFilter("https://localhost:*", CoreWebView2WebResourceContext.All);
		_controller.CoreWebView2.AddWebResourceRequestedFilter("https://[::1]:*", CoreWebView2WebResourceContext.All);

		_controller.CoreWebView2.WebResourceRequested += HandleWebResourceRequested;
	}

	

	

	

	

	

	



	private async void HandleWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs args)
	{
		try
		{
			if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uriObj)) return;
			if (uriObj.Port == _ipcHttpPort) return;

			if (!ShouldProxyRequest(uriObj, args.Request.Headers)) return;

			var deferral = args.GetDeferral();
			try
			{
				var requestMsg = CreateProxyRequest(args.Request, uriObj.ToString());
				var responseMsg = await _localProxyHttpClient.SendAsync(requestMsg, HttpCompletionOption.ResponseHeadersRead);
				
				var webResponse = await CreateProxyResponse(responseMsg, uriObj);
				args.Response = webResponse;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[VSCodeManager] WebResourceRequested proxy error for {uriObj}: {ex.Message}");
			}
			finally
			{
				deferral.Complete();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[VSCodeManager] WebResourceRequested outer error: {ex.Message}");
		}
	}

	private bool ShouldProxyRequest(Uri uriObj, CoreWebView2HttpRequestHeaders headers)
	{
		bool isVSCodeServer = uriObj.Port == _vscodePort;
		bool isSpecialVSCodeResource = isVSCodeServer && (
			uriObj.AbsolutePath.EndsWith("index.html", StringComparison.OrdinalIgnoreCase) ||
			uriObj.AbsolutePath.EndsWith("workbench.js", StringComparison.OrdinalIgnoreCase)
		);

		if (isVSCodeServer && !isSpecialVSCodeResource) return false;

		if (headers.Contains("Upgrade") || headers.Contains("Sec-WebSocket-Key"))
		{
			return false;
		}

		return true;
	}

	private HttpRequestMessage CreateProxyRequest(CoreWebView2WebResourceRequest request, string uri)
	{
		var requestMsg = new HttpRequestMessage(new HttpMethod(request.Method), uri);
		foreach (var h in request.Headers)
		{
			if (h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
			if (h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
			if (h.Key.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) continue;
			if (h.Key.StartsWith("Sec-WebSocket", StringComparison.OrdinalIgnoreCase)) continue;
			requestMsg.Headers.TryAddWithoutValidation(h.Key, h.Value);
		}

		if (request.Content != null)
		{
			var ms = new MemoryStream();
			request.Content.CopyTo(ms);
			ms.Position = 0;
			requestMsg.Content = new StreamContent(ms);
			if (request.Headers.Contains("Content-Type"))
			{
				requestMsg.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(request.Headers.GetHeader("Content-Type"));
			}
		}
		
		return requestMsg;
	}

	private async System.Threading.Tasks.Task<CoreWebView2WebResourceResponse> CreateProxyResponse(HttpResponseMessage responseMsg, Uri uriObj)
	{
		byte[] responseBytes = await responseMsg.Content.ReadAsByteArrayAsync();

		responseBytes = ProcessSpecialVSCodeResources(responseBytes, uriObj);

		var responseStream = new MemoryStream(responseBytes);
		string headersString = BuildProxyResponseHeaders(responseMsg);

		return _controller.CoreWebView2.Environment.CreateWebResourceResponse(
			responseStream,
			(int)responseMsg.StatusCode,
			responseMsg.ReasonPhrase ?? "OK",
			headersString
		);
	}

	private byte[] ProcessSpecialVSCodeResources(byte[] responseBytes, Uri uriObj)
	{
		bool isSpecialVSCodeResource = uriObj.Port == _vscodePort && (
			uriObj.AbsolutePath.EndsWith("index.html", StringComparison.OrdinalIgnoreCase) ||
			uriObj.AbsolutePath.EndsWith("workbench.js", StringComparison.OrdinalIgnoreCase)
		);

		if (isSpecialVSCodeResource)
		{
			string originalContent = System.Text.Encoding.UTF8.GetString(responseBytes);
			string patchedContent = originalContent;
			if (uriObj.AbsolutePath.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
			{
				patchedContent = PatchIndexHtmlContent(originalContent);
			}
			else
			{
				patchedContent = PatchWorkbenchJsContent(originalContent);
			}

			if (!object.ReferenceEquals(originalContent, patchedContent))
			{
				return System.Text.Encoding.UTF8.GetBytes(patchedContent);
			}
		}
		
		return responseBytes;
	}

	private string BuildProxyResponseHeaders(HttpResponseMessage responseMsg)
	{
		var headersBuilder = new System.Text.StringBuilder();
		void AppendHeader(string key, IEnumerable<string> values)
		{
			if (key.Equals("X-Frame-Options", StringComparison.OrdinalIgnoreCase)) return;
			if (key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) return;
			if (key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase)) return;
			if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) return;

			if (key.Equals("Content-Security-Policy", StringComparison.OrdinalIgnoreCase))
			{
				string relaxed = RelaxContentSecurityPolicy(string.Join(" ", values));
				if (!string.IsNullOrWhiteSpace(relaxed))
				{
					headersBuilder.AppendLine($"Content-Security-Policy: {relaxed}");
				}
				return;
			}
			headersBuilder.AppendLine($"{key}: {string.Join(" ", values)}");
		}

		foreach (var h in responseMsg.Headers) AppendHeader(h.Key, h.Value);
		foreach (var h in responseMsg.Content.Headers) AppendHeader(h.Key, h.Value);

		if (!headersBuilder.ToString().Contains("Content-Security-Policy", StringComparison.OrdinalIgnoreCase))
		{
			headersBuilder.AppendLine("Content-Security-Policy: frame-ancestors *;");
		}

		headersBuilder.AppendLine("Access-Control-Allow-Origin: *");
		headersBuilder.AppendLine("Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS, PATCH, HEAD");
		headersBuilder.AppendLine("Access-Control-Allow-Headers: *");
		headersBuilder.AppendLine("Access-Control-Allow-Private-Network: true");
		
		return headersBuilder.ToString();
	}



	private void SetupWebviewMessageHandling()
	{
		_controller.AcceleratorKeyPressed += HandleAcceleratorKeyPressed;
	}

	

	

	



	private void HandleAcceleratorKeyPressed(object sender, CoreWebView2AcceleratorKeyPressedEventArgs args)
	{
		if (args.VirtualKey == 0x73) // VK_F4
		{
			HandleF4Key(args);
		}
		else if (args.VirtualKey == 0x7B) // VK_F12
		{
			args.Handled = false;
		}
		else
		{
			HandleDevToolsShortcut(args);
		}
	}

	private void HandleF4Key(CoreWebView2AcceleratorKeyPressedEventArgs args)
	{
		if (args.KeyEventKind == CoreWebView2KeyEventKind.SystemKeyDown)
		{
			args.Handled = true;
			_actionQueue.Enqueue(() =>
			{
				ShowWindow(_childHwnd, SW_HIDE);
				_isVisible = false;
			});
			PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
		}
	}

	private void HandleDevToolsShortcut(CoreWebView2AcceleratorKeyPressedEventArgs args)
	{
		bool isControlPressed = (GetKeyState(0x11) & 0x8000) != 0;
		bool isShiftPressed = (GetKeyState(0x10) & 0x8000) != 0;
		
		if (isControlPressed && isShiftPressed && (args.VirtualKey == 0x49 || args.VirtualKey == 0x4A)) // Ctrl+Shift+I or Ctrl+Shift+J
		{
			if (args.KeyEventKind == CoreWebView2KeyEventKind.KeyDown)
			{
				args.Handled = true;
				_actionQueue.Enqueue(() =>
				{
					_controller?.CoreWebView2?.OpenDevToolsWindow();
				});
				PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
			}
		}
	}



	private void PositionOnScreen()
	{
		if (_containerControl == null || !GodotObject.IsInstanceValid(_containerControl))
		{
			return;
		}

		int screenCount = DisplayServer.GetScreenCount();
		int currentScreen = DisplayServer.WindowGetCurrentScreen();
		Vector2I targetPos;
		Vector2I targetSize;

		if (screenCount > 1)
		{
			int targetScreen = (currentScreen + 1) % screenCount;
			targetPos = DisplayServer.ScreenGetPosition(targetScreen);
			targetSize = DisplayServer.ScreenGetSize(targetScreen);
		}
		else
		{
			targetPos = DisplayServer.ScreenGetPosition(currentScreen);
			targetSize = DisplayServer.ScreenGetSize(currentScreen);
		}

		SetWindowPos(_childHwnd, IntPtr.Zero, targetPos.X, targetPos.Y, targetSize.X, targetSize.Y, 0x0014);
	}

	public void SetVisible(bool visible)
	{

		_isVisible = visible;
		if (!_isInitialized)
		{
			if (visible)
			{
				_containerControl = null;
				Initialize(_containerControl);
			}
			return;
		}

		if (visible)
		{
			EnsureVSCodeServerRunning();
		}

		_actionQueue.Enqueue(() =>
		{
			if (visible)
			{
				ShowWebViewWindow();
			}
			else
			{
				if (_controller != null)
				{
					_controller.IsVisible = false;
				}
				ShowWindow(_childHwnd, SW_HIDE);
			}
		});

		if (_childHwnd != IntPtr.Zero)
		{
			PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
		}
	}

	private void EnsureVSCodeServerRunning()
	{
		if (_vscodeProcess != null && _vscodeProcess.HasExited)
		{
			StartVSCodeServer();
			if (_vscodeProcess != null && !_vscodeProcess.HasExited)
			{
				_vscodeProcess.WaitForExit(5000);
			}
		}
	}

	private void ShowWebViewWindow()
	{
		bool controllerValid = false;
		try
		{
			controllerValid = _controller != null && _controller.CoreWebView2 != null;
		}
		catch
		{
			controllerValid = false;
		}

		if (!controllerValid)
		{
			InitializeWebView();
			return;
		}

		string projectRoot = PathUtils.GetProjectRoot();
		string mapFolderRaw = GetMapFolderToOpen(projectRoot);
		string mapFolder = FormatWinPathForUrl(mapFolderRaw);
		string targetUrl = $"http://127.0.0.1:{_vscodePort}/?folder={Uri.EscapeDataString(mapFolder)}&ipcPort={_ipcHttpPort}";
		_controller.CoreWebView2.Navigate(targetUrl);

		_controller.IsVisible = true;
		PositionOnScreen();
		ShowWindow(_childHwnd, SW_SHOWMAXIMIZED);
	}

	public void Focus()
	{
		if (_childHwnd != IntPtr.Zero)
		{
			_actionQueue.Enqueue(() =>
			{
				ShowWindow(_childHwnd, SW_SHOWMAXIMIZED);
				BringWindowToTop(_childHwnd);
				SetForegroundWindow(_childHwnd);
			});
			PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
		}
	}

	public void OpenDevTools()
	{
		if (_childHwnd != IntPtr.Zero)
		{
			_actionQueue.Enqueue(() =>
			{
				try
				{
					_controller?.CoreWebView2?.OpenDevToolsWindow();
				}
				catch (Exception ex)
				{
					GD.PrintErr("Failed to open DevTools: " + ex.Message);
				}
			});
			PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
		}
	}

	public void UpdateBounds()
	{
	}

	public void OpenFile(string relativePath)
	{
		string projectRoot = PathUtils.GetProjectRoot();
		string mapFolderRaw = GetMapFolderToOpen(projectRoot);
		string fullPathRaw = Path.Combine(mapFolderRaw, relativePath);

		string mapFolder = FormatWinPathForUrl(mapFolderRaw);
		string fullPath = FormatWinPathForUrl(fullPathRaw);

		_actionQueue.Enqueue(() =>
		{
			if (_controller != null)
			{
				string payload = System.Text.Json.JsonSerializer.Serialize(new[] { new[] { "openFile", fullPath } });
				string targetUrl = $"http://127.0.0.1:{_vscodePort}/?folder={Uri.EscapeDataString(mapFolder)}&payload={Uri.EscapeDataString(payload)}&ipcPort={_ipcHttpPort}";
				_controller.CoreWebView2.Navigate(targetUrl);
			}
		});
		PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
	}

	private string FormatWinPathForUrl(string path)
	{
		string formatted = path.Replace("\\", "/");
		if (!formatted.StartsWith("/"))
		{
			formatted = "/" + formatted;
		}
		return formatted;
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

		string fallbackMap = Realm.Client.Core.GameHost.Instance?.ActiveMapName ?? "melee";
		return Path.Combine(projectRoot, "Maps", fallbackMap).Replace("\\", "/");
	}

	private bool TryGetTempWorkspacePath(out string path)
	{
		path = string.Empty;
		if (Realm.Client.Core.GameHost.Instance != null && Realm.Client.Core.GameHost.Instance.IsMapEditorMode)
		{
			string tempWorkspace = ProjectSettings.GlobalizePath(UI.MapEditorHUD.TempWorkspaceGodotPath);
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
		catch { }
		return false;
	}

	private bool TryGetBlankMapPath(out string path)
	{
		path = string.Empty;
		try
		{
			if (Realm.Client.Core.GameHost.Instance != null)
			{
				string docPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
				string blankDir = Path.Combine(docPath, "blank_map");
				if (!Directory.Exists(blankDir))
				{
					((IGameAPI)Realm.Client.Core.GameHost.Instance).GenerateMapDirectory("blank_map", docPath);
				}
				path = blankDir.Replace("\\", "/");
				return true;
			}
		}
		catch { }
		return false;
	}

	public void CleanUp()
	{
		_actionQueue.Enqueue(() =>
		{
			if (_controller != null)
			{
				_controller.Close();
				_controller = null;
			}
			if (_childHwnd != IntPtr.Zero)
			{
				DestroyWindow(_childHwnd);
				_childHwnd = IntPtr.Zero;
			}
		});
		PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);

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

	private void RunBypassAndVerify(string exePath, string embedDir)
	{
		string serverDataDir = Path.Combine(embedDir, "user-data-dir");
		string extensionsDir = Path.Combine(serverDataDir, "extensions");

		Process tempProcess = new Process();
		tempProcess.StartInfo.FileName = Path.ChangeExtension(exePath, ".cmd");
		tempProcess.StartInfo.Arguments = $"--extensions-dir \"{extensionsDir}\" serve-web --port 8089 --server-data-dir \"{serverDataDir}\" --accept-server-license-terms --without-connection-token";
		tempProcess.StartInfo.CreateNoWindow = true;
		tempProcess.StartInfo.UseShellExecute = false;
		tempProcess.StartInfo.EnvironmentVariables["VSCODE_EXTENSIONS"] = extensionsDir;
		tempProcess.StartInfo.EnvironmentVariables["VSCODE_EXTENSIONS_DIR"] = extensionsDir;
		tempProcess.Start();
		AddProcessToJob(tempProcess);

		System.Threading.Thread.Sleep(2000);

		var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
		var thread = new Thread(() =>
		{
			try
			{
				RunBypassSTA(tcs, embedDir);
			}
			catch (Exception ex)
			{
				GD.PrintErr("Bypass STA thread failed: " + ex.Message);
				tcs.TrySetException(ex);
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();

		bool success = false;
		if (tcs.Task.Wait(45000))
		{
			success = tcs.Task.Result;
		}
		else
		{
			GD.PrintErr("Bypass task timed out.");
		}

		try
		{
			if (!tempProcess.HasExited)
			{
				tempProcess.Kill(true);
			}
		}
		catch
		{
		}

		if (success)
		{
			try
			{
				string markerPath = Path.Combine(embedDir, "bypass_completed.marker");
				File.WriteAllText(markerPath, "completed");
			}
			catch (Exception ex)
			{
				GD.PrintErr("Failed to write bypass marker file: " + ex.Message);
			}
		}
	}

	private void RunBypassSTA(System.Threading.Tasks.TaskCompletionSource<bool> tcs, string embedDir)
	{
		var syncContext = new SingleThreadSynchronizationContext();
		SynchronizationContext.SetSynchronizationContext(syncContext);

		var wndClass = new WNDCLASSEX();
		wndClass.cbSize = Marshal.SizeOf(typeof(WNDCLASSEX));
		wndClass.style = 0;
		WndProcDelegate bypassWndProc = BypassWndProc;
		wndClass.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(bypassWndProc);
		wndClass.cbClsExtra = 0;
		wndClass.cbWndExtra = 0;
		wndClass.hInstance = GetModuleHandle(null);
		wndClass.hIcon = IntPtr.Zero;
		wndClass.hCursor = IntPtr.Zero;
		wndClass.hbrBackground = IntPtr.Zero;
		wndClass.lpszMenuName = null;
		wndClass.lpszClassName = "VSCodeBypassWindow";
		wndClass.hIconSm = IntPtr.Zero;

		RegisterClassEx(ref wndClass);

		IntPtr bypassHwnd = CreateWindowEx(
			0,
			"VSCodeBypassWindow",
			"Bypass Window",
			0,
			0,
			0,
			100,
			100,
			IntPtr.Zero,
			IntPtr.Zero,
			GetModuleHandle(null),
			IntPtr.Zero);

		if (bypassHwnd == IntPtr.Zero)
		{
			tcs.TrySetResult(false);
			return;
		}

		syncContext.SetTargetHwnd(bypassHwnd);

		CoreWebView2Controller controller = null;
		bool running = true;

		Action closeAction = () =>
		{
			if (controller != null)
			{
				controller.Close();
				controller = null;
			}
			running = false;
			PostMessage(bypassHwnd, WM_USER + 1, IntPtr.Zero, IntPtr.Zero);
		};

		InitializeBypassWebView(
			bypassHwnd,
			tcs,
			syncContext,
			c => { controller = c; },
			closeAction,
			embedDir
		);

		MSG msg;
		while (running && GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
		{
			if (msg.message == 0x0010)
			{
				running = false;
				break;
			}
			TranslateMessage(ref msg);
			DispatchMessage(ref msg);
			syncContext.RunPending();
		}
	}

	private static IntPtr BypassWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		return DefWindowProc(hWnd, msg, wParam, lParam);
	}

	private async void InitializeBypassWebView(IntPtr hwnd, System.Threading.Tasks.TaskCompletionSource<bool> tcs, SingleThreadSynchronizationContext syncContext, Action<CoreWebView2Controller> setController, Action closeAction, string embedDir)
	{
		try
		{
			string cachePath = Path.Combine(embedDir, "user-data-dir", "webview-cache");
			var env = await CoreWebView2Environment.CreateAsync(userDataFolder: cachePath);
			var localController = await env.CreateCoreWebView2ControllerAsync(hwnd);
			
			setController(localController);
			localController.Bounds = new System.Drawing.Rectangle(0, 0, 100, 100);
			localController.IsVisible = false;

			int navigationCount = 0;
			int retryCount = 0;
			
			localController.CoreWebView2.NavigationCompleted += async (sender, args) =>
			{
				await HandleBypassNavigationCompleted(args, localController, tcs, closeAction, navigationCount, retryCount);
				if (args.IsSuccess) navigationCount++;
				if (!args.IsSuccess && navigationCount == 0 && retryCount < 10) retryCount++;
			};

			localController.CoreWebView2.Navigate($"http://127.0.0.1:{_vscodePort}/");
		}
		catch (Exception ex)
		{
			GD.PrintErr("InitializeBypassWebView failed: " + ex.Message);
			tcs.TrySetResult(false);
			closeAction();
		}
	}

	private async System.Threading.Tasks.Task HandleBypassNavigationCompleted(CoreWebView2NavigationCompletedEventArgs args, CoreWebView2Controller localController, System.Threading.Tasks.TaskCompletionSource<bool> tcs, Action closeAction, int navigationCount, int retryCount)
	{
		if (!args.IsSuccess)
		{
			if (navigationCount == 0 && retryCount < 10)
			{
				await System.Threading.Tasks.Task.Delay(500);
				localController.CoreWebView2.Navigate($"http://127.0.0.1:{_vscodePort}/");
			}
			else
			{
				GD.PrintErr($"Bypass navigation failed: {args.WebErrorStatus}");
				tcs.TrySetResult(false);
				closeAction();
			}
			return;
		}

		if (navigationCount == 0) // Next will be 1
		{
			string jsScript = GetBypassTrustScript();
			try
			{
				await localController.CoreWebView2.ExecuteScriptAsync(jsScript);
			}
			catch (Exception ex)
			{
				GD.PrintErr("ExecuteScriptAsync failed: " + ex.Message);
				tcs.TrySetResult(false);
				closeAction();
			}
		}
		else if (navigationCount == 1) // Next will be 2
		{
			tcs.TrySetResult(true);
			closeAction();
		}
	}

	private string GetBypassTrustScript()
	{
		const string templatePath = "res://Templates/vscode_bypass_trust.js";
		if (global::Godot.FileAccess.FileExists(templatePath))
		{
			using var file = global::Godot.FileAccess.Open(templatePath, global::Godot.FileAccess.ModeFlags.Read);
			if (file != null) return file.GetAsText();
		}
		
		string diskPath = Path.Combine(AppContext.BaseDirectory, "Templates", "vscode_bypass_trust.js");
		if (File.Exists(diskPath)) return File.ReadAllText(diskPath);
		
		return string.Empty;
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
			var startInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = vscodePath,
				Arguments = arguments,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			var process = System.Diagnostics.Process.Start(startInfo);
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


	[GeneratedRegex(@"\bframe-ancestors\s+[^;]+;?", RegexOptions.IgnoreCase)]
	private static partial Regex FrameAncestorsRegex();

	[GeneratedRegex(@"hostMessaging\.onMessage\('did-load-resource'[\s\S]*?assertIsDefined\(navigator\.serviceWorker\.controller\)\.postMessage\(\{ channel: 'did-load-resource',\s*data \}[^\n\r;]*\);(?:\s*\}\s*catch[^\}]*\})?\s*\}\);")]
	private static partial Regex DidLoadResourceRegex();

	[GeneratedRegex(@"script-src\s+[^;]+;")]
	private static partial Regex IndexHtmlScriptCspRegex();

	private static string RelaxContentSecurityPolicy(string csp)
	{
		if (string.IsNullOrWhiteSpace(csp)) return "frame-ancestors *;";
		if (FrameAncestorsRegex().IsMatch(csp))
		{
			return FrameAncestorsRegex().Replace(csp, "frame-ancestors *;");
		}
		return csp.TrimEnd(';', ' ') + "; frame-ancestors *;";
	}

	private static IEnumerable<string> GetVSCodeTargetDirectories(string embedDir)
	{
		var targets = new List<string>();
		
		if (!string.IsNullOrEmpty(embedDir))
		{
			targets.Add(embedDir);
		}

		if (OS.GetName() == "Windows")
		{
			string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
			string programFiles = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
			string programFilesX86 = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86);
			
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
				string outDir = Path.Combine(root, "out");
				if (Directory.Exists(outDir))
				{
					PatchWebviewOutDirectory(outDir);
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
			var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(productJsonPath, System.Text.Encoding.UTF8));
			if (jsonNode == null) return;
			
			bool needsUpdate = false;
			var extensionAllowedProposedApi = jsonNode["extensionAllowedProposedApi"]?.AsArray();
			
			if (extensionAllowedProposedApi != null)
			{
				needsUpdate = AddProposedApi(extensionAllowedProposedApi, "speige.realm-map-editor");
			}
			
			if (needsUpdate)
			{
				File.WriteAllText(productJsonPath, jsonNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
				GD.Print("Successfully patched VSCode product.json for proposed APIs.");
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

	private static bool AddProposedApi(System.Text.Json.Nodes.JsonArray apiArray, string extensionName)
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

	private static string PatchWorkbenchJsContent(string content)
	{
		if (string.IsNullOrEmpty(content)) return content;

		string wbTargetEndpoint = "get webviewExternalEndpoint(){const i=this.options.webviewEndpoint||this.productService.webviewContentExternalBaseUrlTemplate||\"https://{{uuid}}.vscode-cdn.net/{{quality}}/{{commit}}/out/vs/workbench/contrib/webview/browser/pre/\",e=this.payload?.get(\"webviewExternalEndpointCommit\");return i.replace(\"{{commit}}\",e??this.productService.commit??\"ef65ac1ba57f57f2a3961bfe94aa20481caca4c6\").replace(\"{{quality}}\",(e?\"insider\":this.productService.quality)??\"insider\")}";
		string wbReplEndpoint = "get webviewExternalEndpoint(){return (window.location.origin + \"/static/out/vs/workbench/contrib/webview/browser/pre/\");}";
		if (content.Contains(wbTargetEndpoint, StringComparison.Ordinal))
		{
			content = content.Replace(wbTargetEndpoint, wbReplEndpoint, StringComparison.Ordinal);
		}

		if (content.Contains("t.port1.postMessage(e,[e])", StringComparison.Ordinal))
		{
			content = content.Replace("try{const e=new ReadableStream,t=new MessageChannel;return t.port1.postMessage(e,[e]),t.port1.close(),t.port2.close(),!0}catch{return!1}", "try{return!1}catch{return!1}", StringComparison.Ordinal);
		}

		return content;
	}

	private static string PatchIndexHtmlContent(string content)
	{
		if (string.IsNullOrEmpty(content)) return content;

		string hostCheckTarget = "if (hostname === parentOriginHash || hostname.startsWith(parentOriginHash + '.')) {";
		string hostCheckRepl = "if (hostname === '127.0.0.1' || hostname === 'localhost' || hostname === parentOriginHash || hostname.startsWith(parentOriginHash + '.')) {";
		if (content.Contains(hostCheckTarget, StringComparison.Ordinal))
		{
			content = content.Replace(hostCheckTarget, hostCheckRepl, StringComparison.Ordinal);
		}

		if (IndexHtmlScriptCspRegex().IsMatch(content) && !content.Contains("script-src 'self' 'unsafe-inline';", StringComparison.Ordinal))
		{
			content = IndexHtmlScriptCspRegex().Replace(content, "script-src 'self' 'unsafe-inline';");
		}

		string cspTarget = "frame-src 'self';";
		string cspRepl = "frame-src 'self' http://127.0.0.1:* http://localhost:* vscode-webview:;";
		if (content.Contains(cspTarget, StringComparison.Ordinal))
		{
			content = content.Replace(cspTarget, cspRepl, StringComparison.Ordinal);
		}

		if (DidLoadResourceRegex().IsMatch(content))
		{
			string replacement = "hostMessaging.onMessage('did-load-resource', (_event, data) => {\n\t\t\t\tif (data && data.stream) { try { data.stream.cancel().catch(() => {}); } catch {} delete data.stream; }\n\t\t\t\ttry { if (navigator.serviceWorker && navigator.serviceWorker.controller) { navigator.serviceWorker.controller.postMessage({ channel: 'did-load-resource', data }); } } catch (e) { console.warn('SW did-load-resource postMessage failed:', e); }\n\t\t\t});";
			content = DidLoadResourceRegex().Replace(content, replacement);
		}


		return content;
	}

	private static void PatchWebviewOutDirectory(string outDir)
	{
		try
		{
			string jsPath = Path.Combine(outDir, "vs", "code", "browser", "workbench", "workbench.js");
			if (File.Exists(jsPath))
			{
				try
				{
					string content = File.ReadAllText(jsPath, System.Text.Encoding.UTF8);
					string patched = PatchWorkbenchJsContent(content);
					if (patched != content)
					{
						File.WriteAllText(jsPath, patched, new System.Text.UTF8Encoding(false));
						GD.Print($"VS Code: Patched transferable stream support in {jsPath}");
					}
				}
				catch { }
			}

			string htmlPath = Path.Combine(outDir, "vs", "workbench", "contrib", "webview", "browser", "pre", "index.html");
			if (File.Exists(htmlPath))
			{
				try
				{
					string content = File.ReadAllText(htmlPath, System.Text.Encoding.UTF8);
					string patched = PatchIndexHtmlContent(content);
					if (patched != content)
					{
						File.WriteAllText(htmlPath, patched, new System.Text.UTF8Encoding(false));
						GD.Print($"VS Code: Patched service worker postMessage error guard in {htmlPath}");
					}
				}
				catch { }
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"VS Code: Failed to patch webview out directory {outDir}: {ex.Message}");
		}
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

	private class SingleThreadSynchronizationContext : SynchronizationContext
	{
		private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
		private IntPtr _targetHwnd = IntPtr.Zero;

		public void SetTargetHwnd(IntPtr hwnd)
		{
			_targetHwnd = hwnd;
		}

		public override void Post(SendOrPostCallback d, object? state)
		{
			_queue.Enqueue(() => d(state));
			if (_targetHwnd != IntPtr.Zero)
			{
				PostMessage(_targetHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
			}
		}

		public override void Send(SendOrPostCallback d, object? state)
		{
			throw new NotSupportedException();
		}

		public void RunPending()
		{
			while (_queue.TryDequeue(out var action))
			{
				try
				{
					action();
				}
				catch (Exception ex)
				{
					GD.PrintErr("Error in SingleThreadSynchronizationContext: " + ex.Message);
				}
			}
		}
	}
}