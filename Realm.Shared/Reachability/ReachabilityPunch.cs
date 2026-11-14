namespace Realm.Shared.Reachability;

/// <summary>
///     The datagram a host's game port sends to a registry node's probe socket before that node is asked to reach it. Its
///     purpose is its source endpoint: a router that translates the game port decides the external port the host is
///     reachable on, and the datagram arriving at the probe socket is the only place that port can be read. Both halves of
///     the check therefore agree on these bytes, so the node can tell a punch from a stray datagram.
/// </summary>
public static class ReachabilityPunch
{
	/// <summary>The bytes a punch carries, which are only ever sent, never answered.</summary>
	public static ReadOnlySpan<byte> Datagram => "PUNCH"u8;

	/// <summary>Whether a datagram that arrived at a probe socket is a host punching it.</summary>
	public static bool IsPunch(ReadOnlySpan<byte> datagram) => datagram.SequenceEqual(Datagram);
}
