using System;
using System.Runtime.InteropServices;

namespace Realm.Client;

[StructLayout(LayoutKind.Sequential)]
public struct JOBOBJECT_BASIC_LIMIT_INFORMATION_STRUCT
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