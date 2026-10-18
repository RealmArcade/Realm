using System.Runtime.InteropServices;
using System.Numerics;

namespace Realm.Ecs.Components.Movement;

/// <summary>
///     An intent component that signals an entity should move to a target position.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct MoveTo(Vector3 Target);