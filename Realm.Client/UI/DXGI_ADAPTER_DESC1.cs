using System.Runtime.InteropServices;

namespace Realm.Client.UI;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct DXGI_ADAPTER_DESC1
{
	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
	public string Description;

	public uint VendorId;
	public uint DeviceId;
	public uint SubSysId;
	public uint Revision;
	public nuint DedicatedVideoMemory;
	public nuint DedicatedSystemMemory;
	public nuint SharedSystemMemory;
	public long AdapterLuid;
	public uint Flags;
}