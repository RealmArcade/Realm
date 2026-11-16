using System;
using System.Text;

namespace Realm.Shared.Reachability;

/// <summary>
///     The datagram a joining screen sends from the socket its connection attempt holds, so that a registry node can read
///     the endpoint that screen's router exposes and hand it to the lobby's host.
/// </summary>
/// <remarks>
///     A host behind a filtering router only lets a joining player in when it has sent toward that player first, and it can
///     only do that once it knows where to send. The joining screen cannot measure that itself: its ENet socket is created
///     when the join connects, so the port it will use is not known when the join is requested, and no local query can see
///     the external port a router hands out. The registry node is the only party that sees the datagram arrive, so this is
///     the datagram that carries the join it belongs to and nothing else.
/// </remarks>
public static class JoinerPunch
{
    /// <summary>The bytes every joiner report begins with, followed by the join it belongs to.</summary>
    private static ReadOnlySpan<byte> ReportPrefix => "JOIN:"u8;

    /// <summary>The longest join token a report can carry, which is the size of the identifier a join answer states.</summary>
    public const int MaximumTokenBytes = 64;

    /// <summary>The largest datagram this writes, so a caller can size its buffer once.</summary>
    public const int MaximumDatagramBytes = 5 + MaximumTokenBytes;

    /// <summary>
    ///     Writes the report that names the join it belongs to. It is built into the caller's buffer rather than returning a
    ///     string, because the screen writes it while a join is being made and nothing on that path should allocate.
    /// </summary>
    /// <param name="destination">The buffer to write into, which must hold <see cref="MaximumDatagramBytes" />.</param>
    /// <param name="token">The join this report belongs to, as the registry node stated it.</param>
    /// <returns>The number of bytes written, or zero when the token does not fit or the buffer is too small.</returns>
    public static int TryWrite(Span<byte> destination, ReadOnlySpan<char> token)
    {
        if (token.Length == 0 || token.Length > MaximumTokenBytes) return 0;
        if (destination.Length < ReportPrefix.Length + token.Length) return 0;

        ReportPrefix.CopyTo(destination);
        Span<byte> name = destination.Slice(ReportPrefix.Length, token.Length);
        for (int index = 0; index < token.Length; index++)
        {
            char character = token[index];
            if (character > 0x7F) return 0;

            name[index] = (byte)character;
        }

        return ReportPrefix.Length + token.Length;
    }

    /// <summary>
    ///     Reads the join a report names. A datagram that names none, or names one this build cannot read, is not a report
    ///     this node can act on, so it is refused here rather than being guessed at.
    /// </summary>
    /// <param name="datagram">The datagram that arrived at a node's joiner socket.</param>
    /// <param name="token">The join the report belongs to, or empty when it names none.</param>
    /// <returns>Whether the datagram is a report that named a join.</returns>
    public static bool TryReadToken(ReadOnlySpan<byte> datagram, out string token)
    {
        token = string.Empty;
        if (datagram.Length <= ReportPrefix.Length || datagram.Length > MaximumDatagramBytes) return false;
        if (!datagram.StartsWith(ReportPrefix)) return false;

        ReadOnlySpan<byte> name = datagram.Slice(ReportPrefix.Length);
        for (int index = 0; index < name.Length; index++)
        {
            if (name[index] > 0x7F) return false;
        }

        token = Encoding.ASCII.GetString(name);
        return true;
    }
}

/// <summary>
///     What a registry node hands a joining screen so the screen can report the endpoint its router exposes: the socket to
///     send the report to, and the join the report belongs to.
/// </summary>
/// <param name="Address">The address of the node's joiner socket, which the join answer states.</param>
/// <param name="Port">The port of the node's joiner socket.</param>
/// <param name="Token">The token this join was given, which the report must name.</param>
public readonly record struct JoinerPunchTicket(string Address, int Port, string Token)
{
    /// <summary>Whether this ticket is complete enough to send a report with.</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(Address) && Port > 0 && !string.IsNullOrWhiteSpace(Token);
}
