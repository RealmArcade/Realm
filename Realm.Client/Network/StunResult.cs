using System.Net;

namespace Realm.Client.Network;

public class StunResult
{
	public bool Success { get; set; }

	/// <summary>The socket's own address. Present once the probe managed to bind.</summary>
	public IPEndPoint? LocalEndPoint { get; set; }

	/// <summary>The address the STUN server saw. Absent when the response carried no mapping.</summary>
	public IPEndPoint? MappedEndPoint { get; set; }

	/// <summary>The address the server reported for its alternate endpoint, when it sends one.</summary>
	public IPEndPoint? ChangedEndPoint { get; set; }
}