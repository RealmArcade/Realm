using System;
using System.Text;

namespace Realm.Shared.Reachability;

/// <summary>
///     The datagram a host's game port sends to a registry node's probe socket before that node is asked to reach it. Its
///     purpose is its source endpoint: a router that translates the game port decides the external port the host is
///     reachable on, and the datagram arriving at the probe socket is the only place that port can be read. Both halves of
///     the check therefore agree on these bytes, so the node can tell a punch from a stray datagram.
/// </summary>
/// <remarks>
///     A punch also names the check it is for. The address alone cannot identify it: two hosts behind one router, an
///     office, or two screens on one machine are seen by the same node from the same public address, so a punch from one
///     would otherwise be taken by the other's check and point it at a game port that is not its own. The check's name is
///     handed to the host in the ticket it is given, and it is sent back here, which is what keeps one host's punch to its
///     own probe.
/// </remarks>
public static class ReachabilityPunch
{
	/// <summary>
	///     The datagram that opens a path without naming a check, which is what a published lobby punches a joining player
	///     with: that player is not running a check, and only its source endpoint matters.
	/// </summary>
	public static ReadOnlySpan<byte> Datagram => "PUNCH"u8;

	/// <summary>The longest check name a punch can carry, which is the size of the identifier a ticket states.</summary>
	public const int MaximumProbeIdBytes = 64;

	/// <summary>The bytes every punch begins with, followed by the name of the check it is for.</summary>
	private static ReadOnlySpan<byte> ProbePunchPrefix => "PUNCH:"u8;

	/// <summary>The largest datagram <see cref="TryWrite" /> writes, so a caller can size its buffer once.</summary>
	public const int MaximumDatagramBytes = 6 + MaximumProbeIdBytes;

	/// <summary>
	///     Writes the punch that names a check. It is built into the caller's buffer rather than returning a string, because
	///     the authority writes it while a lobby is being published and nothing on that path should allocate.
	/// </summary>
	/// <param name="destination">The buffer to write into, which must hold <see cref="MaximumDatagramBytes" />.</param>
	/// <param name="probeId">The check the host was given a ticket for.</param>
	/// <returns>The number of bytes written, or zero when the name does not fit or the buffer is too small.</returns>
	public static int TryWrite(Span<byte> destination, ReadOnlySpan<char> probeId)
	{
		if (probeId.Length == 0 || probeId.Length > MaximumProbeIdBytes) return 0;
		if (destination.Length < ProbePunchPrefix.Length + probeId.Length) return 0;

		ProbePunchPrefix.CopyTo(destination);
		Span<byte> name = destination.Slice(ProbePunchPrefix.Length, probeId.Length);
		for (int index = 0; index < probeId.Length; index++)
		{
			char character = probeId[index];
			if (character > 0x7F) return 0;

			name[index] = (byte)character;
		}

		return ProbePunchPrefix.Length + probeId.Length;
	}

	/// <summary>
	///     Reads the check a punch names. A punch that names none, or names one this build cannot read, is not a punch this
	///     node can act on, so it is refused here rather than being guessed at.
	/// </summary>
	/// <param name="datagram">The datagram that arrived at a probe socket.</param>
	/// <param name="probeId">The check the punch is for, or empty when it names none.</param>
	/// <returns>Whether the datagram is a punch that named a check.</returns>
	public static bool TryReadProbeId(ReadOnlySpan<byte> datagram, out string probeId)
	{
		probeId = string.Empty;
		if (datagram.Length <= ProbePunchPrefix.Length || datagram.Length > MaximumDatagramBytes) return false;
		if (!datagram.StartsWith(ProbePunchPrefix)) return false;

		ReadOnlySpan<byte> name = datagram.Slice(ProbePunchPrefix.Length);
		for (int index = 0; index < name.Length; index++)
		{
			if (name[index] > 0x7F) return false;
		}

		probeId = Encoding.ASCII.GetString(name);
		return true;
	}
}
