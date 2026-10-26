using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Movement;

/// <summary>
///     A client-only movement intent that predicts an order before the host confirms it, never replicated and never authoritative.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct PredictedMoveTo(Vector3 Target, int OrderKind);
