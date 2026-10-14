using System;
using System.Runtime.InteropServices;

namespace Realm.Client.UI;

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