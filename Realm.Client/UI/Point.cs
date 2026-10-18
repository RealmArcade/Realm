using System.Runtime.InteropServices;

namespace Realm.Client.UI;

[StructLayout(LayoutKind.Sequential)]
public struct Point
{
	public int x;
	public int y;
}