using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     A client asking the authority to answer at once, so the client can measure the round trip it is playing over.
///     The authority echoes the sequence unchanged: the measurement is the client's, and the payload carries nothing the
///     authority has to interpret.
/// </summary>
/// <param name="Sequence">Which probe this is, so an answer to an older probe is not taken as an answer to a newer one.</param>
[MemoryPackable]
public partial record struct LatencyProbe(int Sequence);
