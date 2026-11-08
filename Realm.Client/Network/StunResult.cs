using System.Net;

namespace Realm.Client.Network;

public class StunResult
{
	public bool Success { get; set; }
	public IPEndPoint LocalEndPoint { get; set; } = null!;
	public IPEndPoint MappedEndPoint { get; set; } = null!;
	public IPEndPoint ChangedEndPoint { get; set; } = null!;
}