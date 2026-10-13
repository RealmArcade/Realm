namespace Realm.Client.Services;

public readonly struct ReadyPacket
{
	public readonly int PeerId;
	public readonly string FunctionName;
	public readonly object[] Arguments;

	public ReadyPacket(int peerId, string functionName, object[] arguments)
	{
		PeerId = peerId;
		FunctionName = functionName;
		Arguments = arguments;
	}
}