using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Movement;

/// <summary>
///     The snapshot sequence that last described this unit on a client, so a unit the authority stopped describing can be told apart from one it describes every tick.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct DescribedAtSequence(int Sequence);
