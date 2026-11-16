namespace Realm.Shared.Network.Relay;

/// <summary>
///     The ways a relayed path can be carried. A carrier is an implementation detail behind the path layer: adding one must
///     not reach the lobby, the match or the presentation code.
/// </summary>
public enum RelayCarrierKind : byte
{
	/// <summary>
	///     A UDP tunnel through the registry's punch machinery, which is the default and the only carrier the feature needs
	///     to be useful.
	/// </summary>
	UdpTunnel = 1,

	/// <summary>
	///     An overlay network both peers already share, used as an additional path when it is detected rather than
	///     configured.
	/// </summary>
	ExistingOverlay = 2,

	/// <summary>
	///     QUIC, which is not required for the first version and is recorded here so a later carrier is a value rather than
	///     a change to anything above the path layer.
	/// </summary>
	Quic = 3
}

/// <summary>
///     What a carrier states about itself before it is used, so that the path layer can tell whether it can carry a
///     datagram rather than discovering it by failing. Every field is a capability the carrier measured or was built with,
///     never a guess made about the network it runs on.
/// </summary>
/// <param name="Kind">Which carrier this is.</param>
/// <param name="MaximumPayloadBytes">The most a single datagram of this carrier may hold, its own overhead included.</param>
/// <param name="GuaranteesDelivery">Whether the carrier retransmits, which decides whether the path may drop a copy.</param>
/// <param name="ProvidesOwnEncryption">Whether the carrier encrypts on its own, which never replaces the path layer's seal.</param>
/// <param name="SupportsIndependentPaths">Whether two paths through one carrier are independent of each other.</param>
/// <param name="SetupCostMilliseconds">What opening this carrier costs, which candidate scoring reads.</param>
public readonly record struct RelayCarrierCapabilities(
	RelayCarrierKind Kind,
	int MaximumPayloadBytes,
	bool GuaranteesDelivery,
	bool ProvidesOwnEncryption,
	bool SupportsIndependentPaths,
	int SetupCostMilliseconds)
{
	/// <summary>The path layer's overhead, which any carrier that carries a relayed datagram must leave room for.</summary>
	public const int EnvelopeBytes = RelayEnvelope.HeaderBytes;

	/// <summary>Whether this carrier can carry a datagram of the given length at all.</summary>
	/// <param name="length">The relayed datagram's length, header and tag included.</param>
	/// <returns>Whether the carrier states room for it.</returns>
	public bool Fits(int length) => length > 0 && length <= MaximumPayloadBytes;
}

/// <summary>
///     One way of carrying a relayed connection's datagrams. The path layer owns the sealing, the duplicate window and the
///     path choice; a carrier only moves bytes and states what it can move.
/// </summary>
/// <remarks>
///     A carrier exists only for a connection that is carried through volunteers, and the envelope and keys belong to that
///     connection rather than to the transport. A connection on the direct path therefore keeps exactly the datagrams it had
///     before this layer existed: it is not sealed, not sequenced, and not a path a relayed connection can fall back to.
///     Uniformity was the other choice and was declined on purpose, because sealing every direct connection would have to be
///     paid for by every player in every match to rescue the one whose volunteers have gone, and a direct path that answers
///     a shim's loopback endpoint is a different change to where the transport binds.
/// </remarks>
public interface IRelayCarrier : System.IDisposable
{
	/// <summary>What this carrier states about itself.</summary>
	RelayCarrierCapabilities Capabilities { get; }

	/// <summary>The path identifier every datagram this carrier moves is sealed under.</summary>
	byte PathId { get; }

	/// <summary>Whether this carrier is open and able to move a datagram right now.</summary>
	bool IsOpen { get; }

	/// <summary>Moves one relayed datagram, whole or not at all.</summary>
	/// <param name="datagram">The datagram to move.</param>
	/// <returns>Whether the carrier took it.</returns>
	bool TrySend(System.ReadOnlySpan<byte> datagram);

	/// <summary>Takes the next datagram that has arrived, without waiting for one.</summary>
	/// <param name="destination">The buffer to write the datagram into.</param>
	/// <param name="length">The bytes the datagram occupies.</param>
	/// <returns>Whether a datagram was waiting.</returns>
	bool TryReceive(System.Span<byte> destination, out int length);

	/// <summary>States what this carrier has moved, which the diagnostics report per path.</summary>
	RelayCarrierTraffic Traffic { get; }
}

/// <summary>
///     What one carrier has moved, in the shape the diagnostics page and the traffic report state per path.
/// </summary>
/// <param name="Sent">The datagrams this carrier took from the path layer.</param>
/// <param name="Received">The datagrams this carrier handed back.</param>
/// <param name="Refused">The datagrams this carrier would not take.</param>
/// <param name="Bytes">The bytes this carrier moved in both directions.</param>
public readonly record struct RelayCarrierTraffic(int Sent, int Received, int Refused, long Bytes);
