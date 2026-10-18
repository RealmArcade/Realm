using System.Runtime.InteropServices;

namespace Realm.Client;

[StructLayout(LayoutKind.Sequential)]
public struct IO_COUNTERS_STRUCT
{
	public ulong ReadOperationCount;
	public ulong WriteOperationCount;
	public ulong OtherOperationCount;
	public ulong ReadTransferCount;
	public ulong WriteTransferCount;
	public ulong OtherTransferCount;
}