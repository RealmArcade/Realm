using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Realm.Client.Utils;

[StructLayout(LayoutKind.Sequential)]
public struct ByHandleFileInformation
{
	public uint FileAttributes;
	public FILETIME CreationTime;
	public FILETIME LastAccessTime;
	public FILETIME LastWriteTime;
	public uint VolumeSerialNumber;
	public uint FileSizeHigh;
	public uint FileSizeLow;
	public uint NumberOfLinks;
	public uint FileIndexHigh;
	public uint FileIndexLow;
}