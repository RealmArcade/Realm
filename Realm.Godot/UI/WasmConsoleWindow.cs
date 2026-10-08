using Godot;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Realm.Godot.UI;

public class WasmConsoleWindow
{
	private static WasmConsoleWindow? _instance;
	public static WasmConsoleWindow Instance => _instance ??= new WasmConsoleWindow();

	private Thread? _staThread;
	private IntPtr _childHwnd;
	private CoreWebView2Controller? _controller;
	private bool _isWebViewReady;
	private bool _isVisible;
	public bool IsVisible => _isVisible;
	public bool Visible => _isVisible;

	private string _logFilePath = "";
	private bool _hasErrors = false;
	public bool HasErrors
	{
		get => _hasErrors;
		set => _hasErrors = value;
	}

	private readonly ConcurrentQueue<Action> _actionQueue = new ConcurrentQueue<Action>();
	private readonly List<string> _bufferedLogs = new List<string>();
	private string _bufferedStatus = "Initializing compilation pipeline...";
	private string _bufferedStatusColor = "#00e5ff";
	private readonly object _logLock = new object();

	private const uint WM_USER = 0x0400;
	private const uint WM_WAKEUP = WM_USER + 1;
	private const uint WM_CLOSE = 0x0010;
	private const uint WM_SIZE = 0x0005;
	private const int SW_HIDE = 0;
	private const int SW_SHOW = 5;
	private const int SW_RESTORE = 9;

	private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
	private const uint SWP_NOSIZE = 0x0001;
	private const uint SWP_NOMOVE = 0x0002;
	private const uint SWP_NOZORDER = 0x0004;
	private const uint SWP_NOACTIVATE = 0x0010;
	private const uint SWP_SHOWWINDOW = 0x0040;
	private const uint SWP_HIDEWINDOW = 0x0080;

	private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
	private WndProcDelegate? _customWndProcDelegate;

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
	public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegisterClassExW")]
	public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
	public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
	public static extern IntPtr GetModuleHandle(string lpModuleName);

	[DllImport("user32.dll")]
	public static extern bool SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern bool IsIconic(IntPtr hWnd);

	public static bool IsSinglePlayerOrTestMode()
	{
		if (MapEditorHUD.IsTestMode) return true;
		if (GameHost.Instance != null && GameHost.Instance.IsMapEditorMode) return true;
		if (GodotObject.IsInstanceValid(LobbyManager.Instance) && LobbyManager.Instance.IsSinglePlayer) return true;
		return false;
	}

	public WasmConsoleWindow()
	{
		if (!OperatingSystem.IsWindows()) return;
		try
		{
			string userDir = global::Godot.ProjectSettings.GlobalizePath("user://");
			_logFilePath = System.IO.Path.Combine(userDir, "wasm_compile.log");
		}
		catch
		{
			_logFilePath = "";
		}

		// Global listener for WASM runtime output
		Realm.Godot.WasmRuntime.OnWasmLog += line => AppendLog(line);

		EnsureInitialized();
	}

	private readonly object _initLock = new object();

	[DllImport("user32.dll")]
	public static extern void PostQuitMessage(int nExitCode);

	private void EnsureInitialized()
	{
		if (!OperatingSystem.IsWindows()) return;
		lock (_initLock)
		{
			if (_staThread != null && _staThread.IsAlive) return;

			_staThread = new Thread(STAThreadLoop);
			_staThread.SetApartmentState(ApartmentState.STA);
			_staThread.IsBackground = true;
			_staThread.Start();
		}
	}

	private class STASynchronizationContext : SynchronizationContext
	{
		private readonly ConcurrentQueue<Action> _queue;
		private readonly IntPtr _hwnd;
		private readonly int _threadId;

		public STASynchronizationContext(ConcurrentQueue<Action> queue, IntPtr hwnd, int threadId)
		{
			_queue = queue;
			_hwnd = hwnd;
			_threadId = threadId;
		}

		public override void Post(SendOrPostCallback d, object? state)
		{
			_queue.Enqueue(() => d(state));
			if (_hwnd != IntPtr.Zero)
			{
				PostMessage(_hwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
			}
		}

		public override void Send(SendOrPostCallback d, object? state)
		{
			if (System.Environment.CurrentManagedThreadId == _threadId)
			{
				d(state);
			}
			else
			{
				using var evt = new ManualResetEventSlim(false);
				_queue.Enqueue(() =>
				{
					try { d(state); }
					finally { evt.Set(); }
				});
				if (_hwnd != IntPtr.Zero)
				{
					PostMessage(_hwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
				}
				evt.Wait();
			}
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
		wndClass.lpszClassName = "WasmConsoleEmbedWindow";
		wndClass.hIconSm = IntPtr.Zero;

		RegisterClassEx(ref wndClass);

		_childHwnd = CreateWindowEx(
			0,
			"WasmConsoleEmbedWindow",
			"⚙️ WASM COMPILATION & RUNTIME CONSOLE",
			WS_OVERLAPPEDWINDOW,
			0,
			0,
			760,
			520,
			IntPtr.Zero,
			IntPtr.Zero,
			GetModuleHandle(null),
			IntPtr.Zero);

		if (_childHwnd == IntPtr.Zero)
		{
			GD.PrintErr("Failed to create WasmConsole native child window.");
			return;
		}

		SynchronizationContext.SetSynchronizationContext(
			new STASynchronizationContext(_actionQueue, _childHwnd, System.Environment.CurrentManagedThreadId));

		PositionOnScreen();
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
					GD.PrintErr("Error executing WasmConsole action on STA thread: " + ex.Message);
				}
			}
		}
	}

	private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		if (msg == WM_SIZE)
		{
			int w = (int)(lParam.ToInt64() & 0xFFFF);
			int h = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
			long sizeType = wParam.ToInt64();
			if (sizeType == 1) // SIZE_MINIMIZED
			{
				_isVisible = false;
			}
			else if (sizeType == 0 || sizeType == 2) // SIZE_RESTORED or SIZE_MAXIMIZED
			{
				if (w > 0 && h > 0 && IsWindowVisible(hWnd))
				{
					_isVisible = true;
				}
			}

			if (_controller != null && sizeType != 1)
			{
				_controller.Bounds = new System.Drawing.Rectangle(0, 0, w, h);
			}
		}
		else if (msg == WM_CLOSE)
		{
			_actionQueue.Enqueue(() =>
			{
				ShowWindow(hWnd, SW_HIDE);
				_isVisible = false;
				RestoreGodotFocus();
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
			string userDir = global::Godot.ProjectSettings.GlobalizePath("user://");
			string cachePath = Path.Combine(userDir, "wasm_console_webview_cache");

			var env = await CoreWebView2Environment.CreateAsync(userDataFolder: cachePath);
			_controller = await env.CreateCoreWebView2ControllerAsync(_childHwnd);
			_controller.Bounds = new System.Drawing.Rectangle(0, 0, 760, 520);
			_controller.IsVisible = false;

			_controller.AcceleratorKeyPressed += (sender, args) =>
			{
				if (args.VirtualKey == 0xC0 || args.VirtualKey == 0xDF || args.VirtualKey == 0x1B) // VK_OEM_3 (~), VK_OEM_8, or VK_ESCAPE
				{
					if (args.KeyEventKind == CoreWebView2KeyEventKind.KeyDown || args.KeyEventKind == CoreWebView2KeyEventKind.SystemKeyDown)
					{
						args.Handled = true;
						_actionQueue.Enqueue(() =>
						{
							SetVisible(false);
							RestoreGodotFocus();
						});
						PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
					}
				}
			};

			_controller.CoreWebView2.WebMessageReceived += (sender, args) =>
			{
				try
				{
					string rawJson = args.WebMessageAsJson;
					using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
					var root = doc.RootElement;
					if (root.TryGetProperty("action", out var actionProp))
					{
						string action = actionProp.GetString() ?? "";
						if (action == "sendCommand" && root.TryGetProperty("text", out var textProp))
						{
							SendCommand(textProp.GetString() ?? "");
						}
						else if (action == "copyLogs" && root.TryGetProperty("text", out var copyTextProp))
						{
							string textToCopy = copyTextProp.GetString() ?? "";
							Callable.From(() => DisplayServer.ClipboardSet(textToCopy)).CallDeferred();
							SetStatus("✓ Console output copied to clipboard!", new Color(0, 0.9f, 1.0f));
						}
						else if (action == "hideConsole")
						{
							SetVisible(false);
							RestoreGodotFocus();
						}
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr("Error handling WebView message: " + ex.Message);
				}
			};

			_controller.CoreWebView2.NavigationCompleted += (sender, args) =>
			{
				_isWebViewReady = true;
				FlushBufferedLogs();
				if (_isVisible)
				{
					PositionOnScreen();
					ShowWindow(_childHwnd, SW_SHOW);
					ShowWindow(_childHwnd, SW_RESTORE);
					SetForegroundWindow(_childHwnd);
					if (_controller != null) _controller.IsVisible = true;
				}
				else
				{
					ShowWindow(_childHwnd, SW_HIDE);
				}
			};

			_controller.CoreWebView2.NavigateToString(GetConsoleHtml());
		}
		catch (Exception ex)
		{
			GD.PrintErr("Failed to initialize WasmConsole WebView2: " + ex.Message);
		}
	}

	private void FlushBufferedLogs()
	{
		lock (_logLock)
		{
			if (_controller == null || _controller.CoreWebView2 == null) return;

			string jsStatus = $"setStatus({System.Text.Json.JsonSerializer.Serialize(_bufferedStatus)}, {System.Text.Json.JsonSerializer.Serialize(_bufferedStatusColor)});";
			_controller.CoreWebView2.ExecuteScriptAsync(jsStatus);

			foreach (var line in _bufferedLogs)
			{
				string jsLine = $"appendLog({System.Text.Json.JsonSerializer.Serialize(line)});";
				_controller.CoreWebView2.ExecuteScriptAsync(jsLine);
			}
			_bufferedLogs.Clear();
		}
	}

	private void PositionOnScreen()
	{
		try
		{
			int currentScreen = DisplayServer.WindowGetCurrentScreen();
			Vector2I screenPos = DisplayServer.ScreenGetPosition(currentScreen);
			Vector2I screenSize = DisplayServer.ScreenGetSize(currentScreen);

			int width = 760;
			int height = 520;
			int x = screenPos.X + (screenSize.X - width) / 2;
			int y = screenPos.Y + (screenSize.Y - height) / 2;

			uint windowFlags = SWP_NOZORDER | SWP_NOACTIVATE;
			if (_isVisible)
			{
				windowFlags |= SWP_SHOWWINDOW;
			}
			SetWindowPos(_childHwnd, IntPtr.Zero, x, y, width, height, windowFlags);
		}
		catch { }
	}

	public void ToggleVisibility()
	{
		bool currentlyVisible = _childHwnd != IntPtr.Zero && IsWindowVisible(_childHwnd) && !IsIconic(_childHwnd);
		SetVisible(!currentlyVisible);
	}

	public void ShowConsole()
	{
		SetVisible(true);
	}

	public void Show()
	{
		SetVisible(true);
	}

	public void Hide()
	{
		SetVisible(false);
	}

	public void SetVisible(bool visible)
	{
		if (!OperatingSystem.IsWindows()) return;
		_isVisible = visible;
		EnsureInitialized();
		_actionQueue.Enqueue(() =>
		{
			if (_childHwnd != IntPtr.Zero)
			{
				if (visible)
				{
					if (_isWebViewReady)
					{
						PositionOnScreen();
						ShowWindow(_childHwnd, SW_SHOW);
						ShowWindow(_childHwnd, SW_RESTORE);
						SetForegroundWindow(_childHwnd);
						if (_controller != null) _controller.IsVisible = true;
					}
				}
				else
				{
					if (_controller != null) _controller.IsVisible = false;
					ShowWindow(_childHwnd, SW_HIDE);
					RestoreGodotFocus();
				}
			}
		});
		if (_childHwnd != IntPtr.Zero)
		{
			PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
		}
	}

	private void RestoreGodotFocus()
	{
		try
		{
			long godotHandle = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
			if (godotHandle != 0)
			{
				SetForegroundWindow(new IntPtr(godotHandle));
			}
		}
		catch { }
	}

	private void SendCommand(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return;
		string cmd = text.Trim();

		AppendLog($"> {cmd}");

		Callable.From(() =>
		{
			try
			{
				if (GameHost.Instance != null)
				{
					GameHost.Instance.TriggerPlayerChatMessage(cmd);
				}
				else if (GodotObject.IsInstanceValid(LobbyManager.Instance))
				{
					LobbyManager.Instance.SendChatMessage(LobbyManager.Instance.LocalPlayer?.Name ?? "Player", cmd);
				}
			}
			catch (Exception ex)
			{
				AppendLog($"[ERROR] Failed to dispatch command: {ex.Message}");
			}
		}).CallDeferred();
	}

	public void CopyLogsToClipboard()
	{
		if (!OperatingSystem.IsWindows()) return;
		EnsureInitialized();
		_actionQueue.Enqueue(() =>
		{
			if (_controller != null && _controller.CoreWebView2 != null)
			{
				_controller.CoreWebView2.ExecuteScriptAsync("copyLogs();");
			}
		});
		if (_childHwnd != IntPtr.Zero)
		{
			PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
		}
	}

	public void ClearLogs()
	{
		_hasErrors = false;
		lock (_logLock)
		{
			_bufferedLogs.Clear();
		}

		try
		{
			if (!string.IsNullOrEmpty(_logFilePath))
				System.IO.File.WriteAllText(_logFilePath, $"=== WASM COMPILATION & RUNTIME LOG [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===\n");
		}
		catch { }

		if (!OperatingSystem.IsWindows()) return;
		EnsureInitialized();
		_actionQueue.Enqueue(() =>
		{
			if (_controller != null && _controller.CoreWebView2 != null)
			{
				_controller.CoreWebView2.ExecuteScriptAsync("clearLogs();");
			}
		});
		if (_childHwnd != IntPtr.Zero)
		{
			PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
		}
	}

	public void AppendLog(string line)
	{
		if (string.IsNullOrEmpty(line)) return;

		if (!string.IsNullOrEmpty(_logFilePath))
		{
			try { System.IO.File.AppendAllText(_logFilePath, line + "\n"); } catch { }
		}

		if (!OperatingSystem.IsWindows()) return;

		lock (_logLock)
		{
			if (!_isWebViewReady)
			{
				_bufferedLogs.Add(line);
				return;
			}
		}

		EnsureInitialized();
		_actionQueue.Enqueue(() =>
		{
			if (_controller != null && _controller.CoreWebView2 != null)
			{
				string js = $"appendLog({System.Text.Json.JsonSerializer.Serialize(line)});";
				_controller.CoreWebView2.ExecuteScriptAsync(js);
			}
		});
		if (_childHwnd != IntPtr.Zero)
		{
			PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
		}
	}

	public void SetStatus(string statusText, Color color)
	{
		string hexColor = $"#{color.ToHtml()}";
		SetStatus(statusText, hexColor);
	}

	public void SetStatus(string statusText, string hexColor)
	{
		lock (_logLock)
		{
			_bufferedStatus = statusText;
			_bufferedStatusColor = hexColor;
			if (!_isWebViewReady) return;
		}

		if (!OperatingSystem.IsWindows()) return;
		EnsureInitialized();
		_actionQueue.Enqueue(() =>
		{
			if (_controller != null && _controller.CoreWebView2 != null)
			{
				string js = $"setStatus({System.Text.Json.JsonSerializer.Serialize(statusText)}, {System.Text.Json.JsonSerializer.Serialize(hexColor)});";
				_controller.CoreWebView2.ExecuteScriptAsync(js);
			}
		});
		if (_childHwnd != IntPtr.Zero)
		{
			PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
		}
	}

	public void CleanUp()
	{
		if (!OperatingSystem.IsWindows()) return;
		lock (_initLock)
		{
			IntPtr hwnd = _childHwnd;
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
				PostQuitMessage(0);
			});
			if (hwnd != IntPtr.Zero)
			{
				PostMessage(hwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
			}
			_isWebViewReady = false;
		}
	}

	private static string GetConsoleHtml()
	{
		const string templatePath = "res://Templates/wasm_console.html";
		if (global::Godot.FileAccess.FileExists(templatePath))
		{
			using var file = global::Godot.FileAccess.Open(templatePath, global::Godot.FileAccess.ModeFlags.Read);
			if (file != null)
			{
				return file.GetAsText();
			}
		}

		string diskPath = Path.Combine(AppContext.BaseDirectory, "Templates", "wasm_console.html");
		if (File.Exists(diskPath))
		{
			return File.ReadAllText(diskPath);
		}

		return string.Empty;
	}
}
