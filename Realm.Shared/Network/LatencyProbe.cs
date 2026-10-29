using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial record struct LatencyProbe(int Sequence);
