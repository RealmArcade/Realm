using System;
using System.Runtime.InteropServices;

namespace Realm.Client;

[StructLayout(LayoutKind.Sequential)]
public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION_STRUCT
{
	public JOBOBJECT_BASIC_LIMIT_INFORMATION_STRUCT BasicLimitInformation;
	public IO_COUNTERS_STRUCT IoInfo;
	public UIntPtr ProcessMemoryLimit;
	public UIntPtr JobMemoryLimit;
	public UIntPtr PeakProcessMemoryUsed;
	public UIntPtr PeakJobMemoryUsed;
}