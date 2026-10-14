using System.Runtime.InteropServices;

namespace Realm.Client;

[StructLayout(LayoutKind.Sequential)]
public struct Point
{
	public int x;
	public int y;
}