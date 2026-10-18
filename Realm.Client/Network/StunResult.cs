using System.Net;

namespace Realm.Client.Network;

public class StunResult
{
	public bool Success { get; set; }
	public IPEndPoint LocalEndPoint { get; set; }
	public IPEndPoint MappedEndPoint { get; set; }
	public IPEndPoint ChangedEndPoint { get; set; }
}