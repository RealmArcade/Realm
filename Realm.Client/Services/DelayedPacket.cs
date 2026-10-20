namespace Realm.Client.Services;

public struct DelayedPacket
{
	public string FunctionName;
	public object[] Arguments;
	public double SendTime;
}