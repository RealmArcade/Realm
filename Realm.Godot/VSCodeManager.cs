using Godot;
using Microsoft.Web.WebView2.Core;
using Realm.MapAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using System.Threading;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class VSCodeManager
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

	private static readonly string[] RequiredExtensions = new[]
	{
		"muhammad-sammy.csharp",
		"OHZIInteractiveStudio.ohzi-vscode-glb-viewer",
		"Gruntfuggly.todo-tree",
		// "mechatroner.rainbow-json",
		"patcx.vscode-nuget-gallery",
		"AykutSarac.jsoncrack-vscode",
		// "akondratiuk1-dev.texture-viewer",
		"Google.google-antigravity"
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
				string appDataVSCode = Path.Combine(appDataDir, "vscode");
				if (Directory.Exists(appDataVSCode))
				{
					return appDataVSCode;
				}
			}
		}
		catch
		{
		}

		try
		{
			string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
			string appDataFallback = Path.Combine(appData, "Godot", "app_userdata", "Realm", "vscode");
			if (Directory.Exists(appDataFallback))
			{
				return appDataFallback;
			}
		}
		catch
		{
		}

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
			string distPkg = Path.Combine(PathUtils.GetProjectRoot(), "vscode_extensions_dist", "speige.realm-map-editor", "package.json");
			if (File.Exists(distPkg))
			{
				var jsonNode = JsonNode.Parse(File.ReadAllText(distPkg, System.Text.Encoding.UTF8));
				string version = jsonNode?["version"]?.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(version))
				{
					return version;
				}
			}

			string srcPkg = Path.GetFullPath(Path.Combine(PathUtils.GetProjectRoot(), "..", "Realm.MapEditorExtension", "package.json"));
			if (File.Exists(srcPkg))
			{
				var jsonNode = JsonNode.Parse(File.ReadAllText(srcPkg, System.Text.Encoding.UTF8));
				string version = jsonNode?["version"]?.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(version))
				{
					return version;
				}
			}

			string versionJson = Path.GetFullPath(Path.Combine(PathUtils.GetProjectRoot(), "..", "version.json"));
			if (File.Exists(versionJson))
			{
				var jsonNode = JsonNode.Parse(File.ReadAllText(versionJson, System.Text.Encoding.UTF8));
				string version = jsonNode?["extensionVersion"]?.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(version))
				{
					return version;
				}
			}
		}
		catch
		{
		}
		return "0.0.1";
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

		if (!File.Exists(exePath)
			|| new FileInfo(exePath).Length == 0
			|| !File.Exists(exePath)
			|| new FileInfo(exePath).Length == 0
			|| (!File.Exists(completedMarkerPath) && !File.Exists(bypassMarkerPath))
			|| string.IsNullOrEmpty(wasiClangPath)
			|| !File.Exists(wasiClangPath)
			|| new FileInfo(wasiClangPath).Length == 0
			|| !Directory.Exists(extDir))
		{
			return false;
		}

		foreach (string requiredExt in RequiredExtensions)
		{
			if (!IsExtensionInstalled(extensionsDir, requiredExt))
			{
				return false;
			}
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
			if (_isInstalling)
			{
				return;
			}

			if (!force && _installCompleted)
			{
				return;
			}

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
			_installTask = System.Threading.Tasks.Task.Run(() =>
			{
				try
				{
					string scriptPath = PathUtils.FindPath("install_editor_dependencies.ps1");
					if (File.Exists(scriptPath))
					{
						GD.Print("Starting editor dependencies installation script: " + scriptPath);
						using (var installProcess = new Process())
						{
							installProcess.StartInfo.FileName = "powershell.exe";
							string forceArg = force ? " -Force" : string.Empty;
							installProcess.StartInfo.Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"{forceArg}";
							installProcess.StartInfo.CreateNoWindow = true;
							installProcess.StartInfo.UseShellExecute = false;
							installProcess.StartInfo.RedirectStandardOutput = true;
							installProcess.StartInfo.RedirectStandardError = true;

							installProcess.OutputDataReceived += (sender, e) =>
							{
								if (!string.IsNullOrEmpty(e.Data))
								{
									GD.Print("[installer] " + e.Data);
								}
							};
							installProcess.ErrorDataReceived += (sender, e) =>
							{
								if (!string.IsNullOrEmpty(e.Data))
								{
									GD.PrintErr("[installer error] " + e.Data);
								}
							};

							installProcess.Start();
							installProcess.BeginOutputReadLine();
							installProcess.BeginErrorReadLine();
							installProcess.WaitForExit();
						}
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
			});
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

			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string extensionsDir = Path.Combine(serverDataDir, "extensions");

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
			_vscodeProcess.StartInfo.Arguments = $"--extensions-dir \"{extensionsDir}\" serve-web --port {_vscodePort} --server-data-dir \"{serverDataDir}\" --accept-server-license-terms --without-connection-token";
			_vscodeProcess.StartInfo.CreateNoWindow = true;
			_vscodeProcess.StartInfo.UseShellExecute = false;
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
			ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
			ctx.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
			ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

			if (ctx.Request.HttpMethod == "OPTIONS")
			{
				ctx.Response.StatusCode = 200;
				ctx.Response.Close();
				return;
			}

			if (ctx.Request.HttpMethod == "GET")
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
				return;
			}

			using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
			string body = await reader.ReadToEndAsync();
			var node = System.Text.Json.Nodes.JsonNode.Parse(body);
			string action = node?["action"]?.ToString() ?? node?["type"]?.ToString();
			if (action == "openVfxDialog" || action == "openModelPicker" || action == "openAbilityVfxDialog" || action == "openAnimationStudio" || action == "openAnimationPreview" || action == "openEditAnimations" || (node?["focusGodot"]?.GetValue<bool>() ?? false))
			{
				RestoreAndFocusGodotWindow();
			}

			var responseObj = new System.Text.Json.Nodes.JsonObject();

			if (action == "openVfxDialog")
			{
				string weaponId = node["weaponId"]?.ToString() ?? "";
				var weaponDataNode = node["weaponData"];

				Callable.From(() =>
				{
					GameHost.WeaponMetadata meta = default;
					if (!string.IsNullOrEmpty(weaponId) && GameHost.WeaponRegistry.TryGetValue(weaponId, out var existing))
					{
						meta = existing;
					}
					else if (weaponDataNode != null)
					{
						try
						{
							meta = System.Text.Json.JsonSerializer.Deserialize<GameHost.WeaponMetadata>(
								weaponDataNode.ToJsonString(),
								new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
							);
						}
						catch (Exception ex)
						{
							GD.PrintErr($"[VSCodeManager] openVfxDialog deserialize error: {ex.Message}");
						}
					}

					if (MapEditorHUD.Instance != null)
					{
						MapEditorHUD.Instance.OpenWeaponVfxDialog(weaponId, meta, (updatedMeta) =>
						{
							MapEditorHUD.Instance.SaveCustomWeaponToMetadata(weaponId, updatedMeta);
						});
					}
				}).CallDeferred();

				responseObj["action"] = "openVfxDialogResult";
				responseObj["success"] = true;
			}
			else if (action == "openModelPicker")
			{
				string entityId = node["unitId"]?.ToString() ?? node["entityId"]?.ToString() ?? "";
				string fieldName = node["field"]?.ToString() ?? "ModelPath";
				string domain = node["domain"]?.ToString() ?? "units";
				string currentPath = node["currentPath"]?.ToString() ?? "";

				Callable.From(() =>
				{
					if (MapEditorHUD.Instance != null)
					{
						MapEditorHUD.Instance.OpenModelPickerDialog(entityId, fieldName, domain, currentPath, (updatedPath) =>
						{
							MapEditorHUD.Instance.SaveEntityModelPathToMetadata(entityId, fieldName, domain, updatedPath);
						});
					}
				}).CallDeferred();

				responseObj["action"] = "openModelPickerResult";
				responseObj["success"] = true;
			}
			else if (action == "openAbilityVfxDialog")
			{
				string abilityId = node["abilityId"]?.ToString() ?? "";
				var abilityDataNode = node["abilityData"] as System.Text.Json.Nodes.JsonObject;

				Callable.From(() =>
				{
					if (MapEditorHUD.Instance != null)
					{
						MapEditorHUD.Instance.OpenAbilityVfxDialog(abilityId, abilityDataNode, (updatedData) =>
						{
							if (updatedData != null)
							{
								string vfx = updatedData["VisualEffect"]?.ToString() ?? "";
								string sound = updatedData["CastSound"]?.ToString() ?? "";
								string icon = updatedData["IconPath"]?.ToString() ?? "";
								float aoe = updatedData["AreaOfEffectRadius"] != null ? (float)updatedData["AreaOfEffectRadius"] : 0f;
								MapEditorHUD.Instance.SaveCustomAbilityVfxToMetadata(abilityId, vfx, sound, icon, aoe);
							}
						});
					}
				}).CallDeferred();

				responseObj["action"] = "openAbilityVfxDialogResult";
				responseObj["success"] = true;
			}
			else if (action == "openAnimationStudio" || action == "openAnimationPreview" || action == "openEditAnimations")
			{
				string unitId = node["unitId"]?.ToString() ?? node["entityId"]?.ToString() ?? "";
				string modelPath = node["modelPath"]?.ToString() ?? "";

				Callable.From(() =>
				{
					if (MapEditorHUD.Instance != null)
					{
						MapEditorHUD.Instance.OpenAnimationPreviewDialog(unitId, modelPath);
					}
				}).CallDeferred();

				responseObj["action"] = "openAnimationStudioResult";
				responseObj["success"] = true;
			}
			else if (action == "formatAndSaveJson" || action == "saveJsonFile" || action == "saveMetadata" || action == "saveTerrain")
			{
				string filePath = node["filePath"]?.ToString() ?? "";
				string content = node["content"]?.ToString() ?? node["text"]?.ToString() ?? "";
				string requestId = node["requestId"]?.ToString() ?? "";

				if (string.IsNullOrEmpty(filePath))
				{
					string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
					filePath = System.IO.Path.Combine(wsPath, action == "saveTerrain" ? "terrain.json" : "metadata.json");
				}
				else if (!System.IO.Path.IsPathRooted(filePath))
				{
					string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
					filePath = System.IO.Path.Combine(wsPath, filePath);
				}

				bool success = false;
				string errorMsg = "";
				string formattedContent = "";

				try
				{
					string fileName = System.IO.Path.GetFileName(filePath).ToLowerInvariant();
					if (fileName == "metadata.json")
					{
						try
						{
							var rootObj = JsonNode.Parse(content)?.AsObject();
							if (rootObj != null && (rootObj.ContainsKey("Assets") || rootObj.ContainsKey("textures")))
							{
								string mapDir = System.IO.Path.GetDirectoryName(filePath) ?? MapWorkspaceService.GetActiveWorkspacePath();
								var unionedAssets = Realm.Godot.Utils.MapAssetHelper.LoadUnionedAssets(mapDir) ?? new JsonObject();
								if (rootObj.TryGetPropertyValue("Assets", out var aNode) && aNode is JsonObject aObj)
								{
									foreach (var kvp in aObj)
									{
										if (kvp.Value != null)
										{
											unionedAssets[kvp.Key] = kvp.Value.DeepClone();
										}
									}
								}
								if (rootObj.TryGetPropertyValue("textures", out var tNode) && tNode is JsonObject tObj)
								{
									var existingTextures = unionedAssets["textures"] as JsonObject ?? new JsonObject();
									foreach (var kvp in tObj)
									{
										if (kvp.Value is JsonObject incomingObj)
										{
											if (existingTextures.TryGetPropertyValue(kvp.Key, out var existNode) && existNode is JsonObject existObj)
											{
												foreach (var p in incomingObj)
												{
													existObj[p.Key] = p.Value?.DeepClone();
												}
											}
											else
											{
												existingTextures[kvp.Key] = incomingObj.DeepClone();
											}
										}
										else if (kvp.Value != null)
										{
											existingTextures[kvp.Key] = kvp.Value.DeepClone();
										}
									}
									unionedAssets["textures"] = existingTextures;
								}
								MapWorkspaceService.NormalizeTextureEntries(unionedAssets, mapDir);
								Realm.Godot.Utils.MapAssetHelper.SaveAssetsToManifest(mapDir, unionedAssets, removeFromMetadata: true);
								if (unionedAssets["textures"] is JsonObject normTextures)
								{
									var targetTextures = new JsonObject();
									foreach (var kvp in normTextures)
									{
										if (kvp.Value is JsonObject itemObj)
										{
											var cleanItem = itemObj.DeepClone() as JsonObject ?? new JsonObject();
											cleanItem.Remove("hash");
											targetTextures[kvp.Key] = cleanItem;
										}
									}
									rootObj["textures"] = targetTextures;
								}
								SaveLoadService.CleanMetadataJsonSchema(rootObj);
								content = rootObj.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
							}
						}
						catch { }
					}

					formattedContent = MapJsonFormatter.FormatJson(content);
					EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
					MapJsonFormatter.SaveFormattedJson(filePath, formattedContent);
					success = true;

					Callable.From(() =>
					{
						if (fileName == "metadata.json" || fileName == "manifest.json")
						{
							if (MapEditorHUD.Instance != null)
							{
								MapEditorHUD.Instance.ReadMetadataAndRefreshTextures();
								string display = fileName == "manifest.json" ? "manifest.json" : "metadata.json";
								MapEditorHUD.Instance.ShowFeedback(string.Format(TranslationServer.Translate("{0} updated externally — reloaded."), display));
							}
							else if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
							{
								GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
							}
						}
						else if (fileName == "terrain.json")
						{
							if (GameHost.Instance != null && GameHost.Instance.IsMapEditorMode)
							{
								GameHost.Instance.LoadMapFromFile(filePath);
								MapEditorHUD.Instance?.ShowFeedback(TranslationServer.Translate("terrain.json updated externally — reloaded."));
							}
						}
					}).CallDeferred();
				}
				catch (Exception ex)
				{
					errorMsg = ex.Message;
					GD.PrintErr($"[VSCodeManager] formatAndSaveJson error: {ex.Message}");
				}

				responseObj["action"] = "formatAndSaveJsonResult";
				responseObj["type"] = "formatAndSaveJsonResult";
				responseObj["requestId"] = requestId;
				responseObj["success"] = success;
				responseObj["filePath"] = filePath;
				responseObj["formattedContent"] = formattedContent;
				responseObj["error"] = errorMsg;
			}
			else if (action == "reloadMetadata" || action == "updateMetadata")
			{
				Callable.From(() =>
				{
					if (MapEditorHUD.Instance != null)
					{
						MapEditorHUD.Instance.ReadMetadataAndRefreshTextures();
						MapEditorHUD.Instance.ShowFeedback(TranslationServer.Translate("metadata.json updated externally — reloaded."));
					}
					else if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
					{
						GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
					}
				}).CallDeferred();

				responseObj["action"] = "reloadMetadataResult";
				responseObj["type"] = "reloadMetadataResult";
				responseObj["success"] = true;
			}
			else if (action == "convertRtex")
			{
				string inputPath = node["inputPath"]?.ToString() ?? node["filePath"]?.ToString() ?? "";
				string outputPath = node["outputPath"]?.ToString() ?? "";
				int layer = node["layer"] != null ? (int)node["layer"] : 0;
				try
				{
					var res = Realm.Shared.Textures.TextureConverter.ExtractPngFromRtex(inputPath, outputPath, layer);
					responseObj["action"] = "convertRtexResult";
					responseObj["type"] = "convertRtexResult";
					responseObj["success"] = res.Success;
					if (!res.Success)
					{
						responseObj["error"] = res.ErrorMessage;
					}
					else
					{
						responseObj["outputPath"] = res.OutputPath;
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
				}
				catch (Exception ex)
				{
					responseObj["action"] = "convertRtexResult";
					responseObj["type"] = "convertRtexResult";
					responseObj["success"] = false;
					responseObj["error"] = ex.Message;
				}
			}
			else if (action == "renderRanim")
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
			else if (action == "convertRmesh")
			{
				string inputPath = node["inputPath"]?.ToString() ?? node["filePath"]?.ToString() ?? "";
				string outputPath = node["outputPath"]?.ToString() ?? "";
				try
				{
					if (File.Exists(inputPath))
					{
						byte[] rmeshBytes = File.ReadAllBytes(inputPath);
						var (metaJson, glbBytes, _) = Realm.Shared.ModelOptimization.RmeshFile.Parse(rmeshBytes);
						if (glbBytes.Length > 0)
						{
							if (!string.IsNullOrEmpty(outputPath))
							{
								string? dir = Path.GetDirectoryName(outputPath);
								if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
								File.WriteAllBytes(outputPath, glbBytes);
							}
							responseObj["action"] = "convertRmeshResult";
							responseObj["type"] = "convertRmeshResult";
							responseObj["success"] = true;
							responseObj["outputPath"] = outputPath;
							if (!string.IsNullOrEmpty(metaJson))
							{
								responseObj["metadata"] = JsonNode.Parse(metaJson);
							}
						}
						else
						{
							responseObj["action"] = "convertRmeshResult";
							responseObj["type"] = "convertRmeshResult";
							responseObj["success"] = false;
							responseObj["error"] = "No GLB payload found in RMESH file.";
						}
					}
					else
					{
						responseObj["action"] = "convertRmeshResult";
						responseObj["type"] = "convertRmeshResult";
						responseObj["success"] = false;
						responseObj["error"] = $"RMESH file not found: {inputPath}";
					}
				}
				catch (Exception ex)
				{
					responseObj["action"] = "convertRmeshResult";
					responseObj["type"] = "convertRmeshResult";
					responseObj["success"] = false;
					responseObj["error"] = ex.Message;
				}
			}
			else if (action == "convertRaud")
			{
				string inputPath = node["inputPath"]?.ToString() ?? node["filePath"]?.ToString() ?? "";
				string outputPath = node["outputPath"]?.ToString() ?? "";
				int trackIndex = node["trackIndex"] != null ? (int)node["trackIndex"] : 0;
				try
				{
					if (File.Exists(inputPath))
					{
						byte[] raudBytes = File.ReadAllBytes(inputPath);
						var (metaJson, tracks, _) = Realm.Shared.Audio.RaudFile.Parse(raudBytes);
						if (tracks.Count > trackIndex && tracks[trackIndex].Length > 0)
						{
							if (!string.IsNullOrEmpty(outputPath))
							{
								string? dir = Path.GetDirectoryName(outputPath);
								if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
								File.WriteAllBytes(outputPath, tracks[trackIndex]);
							}
							responseObj["action"] = "convertRaudResult";
							responseObj["type"] = "convertRaudResult";
							responseObj["success"] = true;
							responseObj["outputPath"] = outputPath;
							responseObj["trackCount"] = tracks.Count;
							if (!string.IsNullOrEmpty(metaJson))
							{
								responseObj["metadata"] = JsonNode.Parse(metaJson);
							}
						}
						else
						{
							responseObj["action"] = "convertRaudResult";
							responseObj["type"] = "convertRaudResult";
							responseObj["success"] = false;
							responseObj["error"] = $"Audio track {trackIndex} not found in RAUD file.";
						}
					}
					else
					{
						responseObj["action"] = "convertRaudResult";
						responseObj["type"] = "convertRaudResult";
						responseObj["success"] = false;
						responseObj["error"] = $"RAUD file not found: {inputPath}";
					}
				}
				catch (Exception ex)
				{
					responseObj["action"] = "convertRaudResult";
					responseObj["type"] = "convertRaudResult";
					responseObj["success"] = false;
					responseObj["error"] = ex.Message;
				}
			}

			string resJson = responseObj.ToJsonString();
			byte[] resBytes = System.Text.Encoding.UTF8.GetBytes(resJson);
			ctx.Response.ContentType = "application/json";
			ctx.Response.ContentLength64 = resBytes.Length;
			await ctx.Response.OutputStream.WriteAsync(resBytes, 0, resBytes.Length);
			ctx.Response.Close();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[VSCodeManager] HandleIpcHttpRequest error: {ex.Message}");
			try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
		}
	}

	private void STAThreadLoop()
	{
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

		InitializeWebView();

		MSG msg;
		while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
		{
			TranslateMessage(ref msg);
			DispatchMessage(ref msg);

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
			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string cachePath = Path.Combine(serverDataDir, "webview-cache");

			var env = await CoreWebView2Environment.CreateAsync(userDataFolder: cachePath);
			_controller = await env.CreateCoreWebView2ControllerAsync(_childHwnd);
			_controller.Bounds = new System.Drawing.Rectangle(0, 0, 800, 600);
			if (_controller.CoreWebView2?.Settings != null)
			{
				_controller.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
				_controller.CoreWebView2.Settings.AreDevToolsEnabled = true;
			}
			_controller.AcceleratorKeyPressed += (sender, args) =>
			{
				if (args.VirtualKey == 0x73) // VK_F4
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
				else if (args.VirtualKey == 0x7B) // VK_F12
				{
					args.Handled = false;
				}
				else
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
			};
			
			string mapFolderRaw = GetMapFolderToOpen(projectRoot);
			string unitsPathRaw = Path.Combine(mapFolderRaw, "metadata.json");
			string scriptPathRaw = Path.Combine(mapFolderRaw, "MapScript.cs");
			
			string mapFolder = FormatWinPathForUrl(mapFolderRaw);
			string unitsPath = FormatWinPathForUrl(unitsPathRaw);
			string scriptPath = FormatWinPathForUrl(scriptPathRaw);
			
			string targetUrl = $"http://127.0.0.1:{_vscodePort}/?folder={Uri.EscapeDataString(mapFolder)}&ipcPort={_ipcHttpPort}";

			_controller.CoreWebView2.NavigationCompleted += async (sender, args) =>
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
			};

			_controller.CoreWebView2.Navigate(targetUrl);

			_controller.IsVisible = _isVisible;
			ShowWindow(_childHwnd, _isVisible ? SW_SHOW : SW_HIDE);
		}
		catch (Exception ex)
		{
			GD.PrintErr("Failed to initialize WebView2: " + ex.Message);
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
			if (_vscodeProcess != null && _vscodeProcess.HasExited)
			{
				StartVSCodeServer();
				if (_vscodeProcess != null && !_vscodeProcess.HasExited)
				{
					_vscodeProcess.WaitForExit(5000);
				}
			}
		}

		_actionQueue.Enqueue(() =>
		{
			if (visible)
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
				string targetUrl = $"http://127.0.0.1:{_vscodePort}/?folder={Uri.EscapeDataString(mapFolder)}";
				_controller.CoreWebView2.Navigate(targetUrl);

				_controller.IsVisible = true;
				PositionOnScreen();
				ShowWindow(_childHwnd, SW_SHOWMAXIMIZED);
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
				string targetUrl = $"http://127.0.0.1:{_vscodePort}/?folder={Uri.EscapeDataString(mapFolder)}&payload={Uri.EscapeDataString(payload)}";
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
		if (GameHost.Instance != null && GameHost.Instance.IsMapEditorMode)
		{
			string tempWorkspace = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			if (Directory.Exists(tempWorkspace))
			{
				return tempWorkspace.Replace("\\", "/");
			}
		}

		try
		{
			string recentPathFile = ProjectSettings.GlobalizePath("user://recent_map_dir.txt");
			if (File.Exists(recentPathFile))
			{
				string path = File.ReadAllText(recentPathFile).Trim();
				if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
				{
					return path.Replace("\\", "/");
				}
			}
		}
		catch
		{
		}

		try
		{
			if (GameHost.Instance != null)
			{
				string docPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
				string blankDir = Path.Combine(docPath, "blank_map");
				if (!Directory.Exists(blankDir))
				{
					((IGameAPI)GameHost.Instance).GenerateMapDirectory("blank_map", docPath);
				}
				return blankDir.Replace("\\", "/");
			}
		}
		catch
		{
		}

		string fallbackMap = GameHost.Instance?.ActiveMapName ?? "melee";
		return Path.Combine(projectRoot, "Maps", fallbackMap).Replace("\\", "/");
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
			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string cachePath = Path.Combine(serverDataDir, "webview-cache");

			var env = await CoreWebView2Environment.CreateAsync(userDataFolder: cachePath);
			var localController = await env.CreateCoreWebView2ControllerAsync(hwnd);
			setController(localController);
			localController.Bounds = new System.Drawing.Rectangle(0, 0, 100, 100);
			localController.IsVisible = false;

			int navigationCount = 0;
			int retryCount = 0;
			localController.CoreWebView2.NavigationCompleted += async (sender, args) =>
			{
				if (!args.IsSuccess)
				{
					if (navigationCount == 0 && retryCount < 10)
					{
						retryCount++;
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

				navigationCount++;
				if (navigationCount == 1)
				{
					string jsScript = """
					(async () => {
						const DB_NAME = 'vscode-web-db';
						const STORE_NAME = 'vscode-userdata-store'; 
						const TARGET_KEY = '/User/settings.json';

						const request = indexedDB.open(DB_NAME);

						request.onsuccess = (event) => {
							const db = event.target.result;
							const transaction = db.transaction([STORE_NAME], 'readwrite');
							const store = transaction.objectStore(STORE_NAME);
							
							const getRequest = store.get(TARGET_KEY);

							getRequest.onsuccess = () => {
								let config = {};
								const rawData = getRequest.result;

								if (rawData) {
									const buffer = rawData instanceof Uint8Array ? rawData : rawData.value;
									
									if (buffer instanceof Uint8Array) {
										try {
											const decoder = new TextDecoder('utf-8');
											const jsonString = decoder.decode(buffer);
											if (jsonString.trim()) {
												config = JSON.parse(jsonString);
											}
										} catch (e) {
											console.warn("Error parsing existing binary settings. Resetting configuration layer.", e);
										}
									}
								}

								config["security.workspace.trust.enabled"] = false;
								config["security.workspace.trust.startupPrompt"] = "never";

								const updatedJsonString = JSON.stringify(config, null, '\t');
								const encoder = new TextEncoder();
								const encodedUint8Array = encoder.encode(updatedJsonString);

								let putPayload;
								if (rawData && typeof rawData === 'object' && !(rawData instanceof Uint8Array) && 'key' in rawData) {
									putPayload = { key: TARGET_KEY, value: encodedUint8Array };
								} else {
									putPayload = encodedUint8Array;
								}

								const putRequest = store.keyPath === null || !store.keyPath
									? store.put(putPayload, TARGET_KEY)
									: store.put(putPayload);

								putRequest.onsuccess = () => {
									console.log("%c[Success] Restricted mode successfully disabled via binary mutation! Reloading...", "color: #00ff00; font-weight: bold;");
									window.location.reload();
								};

								putRequest.onerror = (e) => console.error("Failed to write binary buffer to IndexedDB store:", e);
							};
						};

						request.onerror = () => console.error("Could not establish a database connection to:", DB_NAME);
					})();
					""";
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
				else if (navigationCount == 2)
				{
					tcs.TrySetResult(true);
					closeAction();
				}
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

	private void InstallMissingExtensions(string exePath, string embedDir)
	{
		try
		{
			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string extensionsDir = Path.Combine(serverDataDir, "extensions");

			foreach (string extensionId in RequiredExtensions)
			{
				if (!IsExtensionInstalled(extensionsDir, extensionId))
				{
					GD.Print("VS Code: Installing missing extension " + extensionId);
					using (var process = new Process())
					{
						process.StartInfo.FileName = Path.ChangeExtension(exePath, ".cmd");
						process.StartInfo.Arguments = $"--extensions-dir \"{extensionsDir}\" --user-data-dir \"{serverDataDir}\" --install-extension {extensionId}";
						process.StartInfo.CreateNoWindow = true;
						process.StartInfo.UseShellExecute = false;
						process.StartInfo.RedirectStandardOutput = true;
						process.StartInfo.RedirectStandardError = true;
						process.OutputDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) GD.Print("[vscode ext] " + e.Data); };
						process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) GD.PrintErr("[vscode ext error] " + e.Data); };
						process.Start();
						process.BeginOutputReadLine();
						process.BeginErrorReadLine();
						AddProcessToJob(process);
						process.WaitForExit();
					}
				}
			}

			string realmMapEditorId = "speige.realm-map-editor";
			string extVersion = GetRealmMapEditorVersion();
			string srcPath = PathUtils.FindPath("Realm.MapEditorExtension");
			if (!Directory.Exists(srcPath))
			{
				srcPath = Path.GetFullPath(Path.Combine(PathUtils.GetProjectRoot(), "..", "Realm.MapEditorExtension"));
			}
			if (!Directory.Exists(srcPath))
			{
				srcPath = Path.Combine(PathUtils.GetProjectRoot(), "vscode_extensions_dist", realmMapEditorId);
			}
			string dstPath = Path.Combine(extensionsDir, $"{realmMapEditorId}-{extVersion}");

			try
			{
				if (Directory.Exists(srcPath))
				{
					Directory.CreateDirectory(dstPath);
					CopyItemIfExists(Path.Combine(srcPath, "package.json"), Path.Combine(dstPath, "package.json"));
					CopyItemIfExists(Path.Combine(srcPath, "map_schema.json"), Path.Combine(dstPath, "map_schema.json"));
					CopyDirectoryIfExists(Path.Combine(srcPath, "dist"), Path.Combine(dstPath, "dist"));
					CopyDirectoryIfExists(Path.Combine(srcPath, "media"), Path.Combine(dstPath, "media"));
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr("VS Code: Failed to sync Realm Map Editor extension files: " + ex.Message);
			}

			try
			{
				if (Directory.Exists(srcPath) || Directory.Exists(dstPath))
				{
					string obsoletePath = Path.Combine(extensionsDir, ".obsolete");
					if (File.Exists(obsoletePath))
					{
						try
						{
							var fi = new FileInfo(obsoletePath);
							if (fi.Length < 10 * 1024 * 1024)
							{
								var obsoleteJson = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, bool>>(File.ReadAllText(obsoletePath, System.Text.Encoding.UTF8));
								if (obsoleteJson != null)
								{
									bool changed = obsoleteJson.Remove($"{realmMapEditorId}-{extVersion}") | obsoleteJson.Remove("speige.realm-map-editor-1.0.0");
									if (changed)
									{
										File.WriteAllText(obsoletePath, System.Text.Json.JsonSerializer.Serialize(obsoleteJson), System.Text.Encoding.UTF8);
										GD.Print("VS Code: Removed Realm Map Editor from .obsolete.");
									}
								}
							}
							else
							{
								File.Delete(obsoletePath);
							}
						}
						catch (Exception ex)
						{
							GD.PrintErr($"VS Code: Failed to clean .obsolete ({ex.Message}). Resetting...");
							try { File.Delete(obsoletePath); } catch { }
						}
					}

					string extensionsJsonPath = Path.Combine(extensionsDir, "extensions.json");
					JsonNode jsonNode = null;
					if (File.Exists(extensionsJsonPath))
					{
						try
						{
							var fi = new FileInfo(extensionsJsonPath);
							if (fi.Length < 10 * 1024 * 1024)
							{
								jsonNode = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(extensionsJsonPath, System.Text.Encoding.UTF8));
							}
							else
							{
								GD.PrintErr($"VS Code: extensions.json was excessively large ({fi.Length} bytes). Resetting...");
								try { File.Delete(extensionsJsonPath); } catch { }
							}
						}
						catch (Exception ex)
						{
							GD.PrintErr($"VS Code: extensions.json was corrupted or unreadable ({ex.Message}). Resetting...");
							try { File.Delete(extensionsJsonPath); } catch { }
							jsonNode = null;
						}
					}

					var jsonArray = jsonNode as System.Text.Json.Nodes.JsonArray ?? new System.Text.Json.Nodes.JsonArray();
					System.Text.Json.Nodes.JsonObject existingEntry = null;
					foreach (var item in jsonArray)
					{
						if (item?["identifier"]?["id"]?.GetValue<string>() == realmMapEditorId)
						{
							existingEntry = item as System.Text.Json.Nodes.JsonObject;
							break;
						}
					}

					string dstAbsPath = Path.GetFullPath(dstPath).Replace("\\", "/");
					if (existingEntry != null)
					{
						existingEntry["version"] = extVersion;
						existingEntry["relativeLocation"] = $"{realmMapEditorId}-{extVersion}";
						if (existingEntry["location"] is System.Text.Json.Nodes.JsonObject locObj)
						{
							locObj["path"] = "/" + dstAbsPath;
						}
					}
					else
					{
						var newEntry = new System.Text.Json.Nodes.JsonObject
						{
							["identifier"] = new System.Text.Json.Nodes.JsonObject { ["id"] = realmMapEditorId },
							["version"] = extVersion,
							["location"] = new System.Text.Json.Nodes.JsonObject
							{
								["$mid"] = 1,
								["path"] = "/" + dstAbsPath,
								["scheme"] = "file"
							},
							["relativeLocation"] = $"{realmMapEditorId}-{extVersion}",
							["metadata"] = new System.Text.Json.Nodes.JsonObject
							{
								["installedTimestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
								["source"] = "local",
								["isApplicationScoped"] = false,
								["isMachineScoped"] = false
							}
						};
						jsonArray.Add(newEntry);
					}
					File.WriteAllText(extensionsJsonPath, jsonArray.ToJsonString(), System.Text.Encoding.UTF8);
					GD.Print("VS Code: Registered Realm Map Editor in extensions.json.");

					GD.Print("VS Code: Realm Map Editor extension installed successfully.");
				}
				else
				{
					GD.PrintErr("VS Code: Realm Map Editor extension source not found at " + srcPath);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr("VS Code: Failed to install Realm Map Editor extension: " + ex.Message);
			}

			PatchOhziExtension(extensionsDir);
		}
		catch (Exception ex)
		{
			GD.PrintErr("Failed to install missing VS Code extensions: " + ex.Message);
		}
	}

	private static void PatchOhziExtension(string extensionsDir)
	{
		try
		{
			if (!Directory.Exists(extensionsDir)) return;

			string patchSrc = Path.Combine(PathUtils.GetProjectRoot(), "vscode_extensions_dist", "patches", "ohzi-vscode-glb-viewer", "extension.js");
			if (!File.Exists(patchSrc))
			{
				patchSrc = Path.GetFullPath(Path.Combine(PathUtils.GetProjectRoot(), "..", "Realm.MapEditorExtension", "patches", "ohzi-vscode-glb-viewer", "extension.js"));
			}
			if (!File.Exists(patchSrc))
			{
				string found = PathUtils.FindPath(Path.Combine("patches", "ohzi-vscode-glb-viewer", "extension.js"));
				if (File.Exists(found)) patchSrc = found;
			}
			if (!File.Exists(patchSrc))
			{
				GD.PrintErr("VS Code: OHZI patch source file not found.");
				return;
			}

			string[] extDirs = Directory.GetDirectories(extensionsDir);
			foreach (string dir in extDirs)
			{
				string dirName = Path.GetFileName(dir);
				if (dirName.Contains("ohzi-vscode-glb-viewer", StringComparison.OrdinalIgnoreCase))
				{
					string extJsPath = Path.Combine(dir, "extension.js");
					bool needsPatch = !File.Exists(extJsPath);
					if (!needsPatch && File.Exists(extJsPath))
					{
						string content = File.ReadAllText(extJsPath, System.Text.Encoding.UTF8);
						if (!content.Contains("REALM_PATCHED_OHZI_BASE64", StringComparison.Ordinal) && (!content.Contains("threeDataUri", StringComparison.Ordinal) || content.Contains("loadModelFromUri", StringComparison.Ordinal)))
						{
							needsPatch = true;
						}
					}

					if (needsPatch)
					{
						File.Copy(patchSrc, extJsPath, true);
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

		public override void Post(SendOrPostCallback d, object? state)
		{
			_queue.Enqueue(() => d(state));
		}

		public override void Send(SendOrPostCallback d, object? state)
		{
			throw new NotSupportedException();
		}

		public void RunPending()
		{
			while (_queue.TryDequeue(out var action))
			{
				action();
			}
		}
	}
}
