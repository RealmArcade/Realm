using System;
using System.Runtime.InteropServices;

namespace Realm.Client;

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