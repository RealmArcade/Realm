using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.UI;

namespace Realm.Client.Services;

/// <summary>
///     The Windows half of hosting the external editor: keep the child process inside a job object so it dies with the game,
///     give its window the game's taskbar identity and icon, and bring the game window back to the foreground when the
///     editor asks it to. Every entry point is a no-op off Windows.
/// </summary>
public static class EditorWindowIntegration
{
	public const int SW_RESTORE = 9;

	[DllImport("user32.dll")]
	public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	public static extern bool SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern bool BringWindowToTop(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern void SwitchToThisWindow(IntPtr hWnd, bool fUnknown);

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
				GameLog.LogError($"[VSCodeManager] RestoreAndFocusGodotWindow error: {ex.Message}");
			}
		}).CallDeferred();
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

	[DllImport("kernel32.dll")]
	private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

	[DllImport("kernel32.dll")]
	private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

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

				var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION_STRUCT
				{
					BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION_STRUCT
					{
						LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
					}
				};

				int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION_STRUCT));
				IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
				try
				{
					ZeroMemory(extendedInfoPtr, length);
					Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
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
			GameLog.LogError($"Failed to create/assign Job Object: {ex.Message}");
		}
	}

	[DllImport("kernel32.dll", EntryPoint = "RtlZeroMemory")]
	private static extern void ZeroMemory(IntPtr destination, int length);


	public static void AddProcessToJob(Process proc)
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
			GameLog.LogError($"Failed to assign process {proc.Id} to Job Object: {ex.Message}");
		}
	}

	private const uint WM_GETICON = 0x007F;
	private const uint WM_SETICON = 0x0080;
	private const int ICON_SMALL = 0;
	private const int ICON_BIG = 1;

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	public static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

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

	public static void AttachWindowGrouping(Process proc)
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
}
